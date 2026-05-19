using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DotnetNativeMcp.Core;

public enum SizeBreakdownGroupBy
{
    Assembly,
    Namespace,
    Type,
    Method,
}

public sealed record SizeBreakdownItem(string Name, long Bytes);

public sealed record SizeBreakdownValue(
    string ImageHandle,
    string GroupBy,
    string MstatPath,
    int TotalBytes,
    int TotalGroups,
    IReadOnlyList<SizeBreakdownItem> Items);

public static class MstatSizeAnalyzer
{
    public static NativeResult<SizeBreakdownValue> GetSizeBreakdown(
        NativeImageInfo image,
        SizeBreakdownGroupBy groupBy,
        int topN,
        string? mstatPath = null)
    {
        if (topN is < 1 or > 500)
        {
            return NativeResult.Fail<SizeBreakdownValue>(
                NativeErrorKind.InvalidArgument,
                "topN must be between 1 and 500.");
        }

        var resolvedMstatPath = ResolveMstatPath(image, mstatPath);
        if (resolvedMstatPath is null)
        {
            return NativeResult.Fail<SizeBreakdownValue>(
                NativeErrorKind.MstatNotFound,
                $"No .mstat sidecar was found for '{image.BinaryPath}'.");
        }

        if (!TryParseContributions(resolvedMstatPath, out var contributions, out var error))
        {
            return NativeResult.Fail<SizeBreakdownValue>(NativeErrorKind.InvalidMstat, error);
        }

        IEnumerable<IGrouping<string, SizeContribution>> grouped = groupBy switch
        {
            SizeBreakdownGroupBy.Assembly => contributions.GroupBy(x => x.Assembly, StringComparer.Ordinal),
            SizeBreakdownGroupBy.Namespace => contributions.GroupBy(x => x.Namespace, StringComparer.Ordinal),
            SizeBreakdownGroupBy.Type => contributions.GroupBy(x => x.Type, StringComparer.Ordinal),
            SizeBreakdownGroupBy.Method => contributions
                .Where(x => x.Method is not null)
                .GroupBy(x => x.Method!, StringComparer.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(groupBy)),
        };

        var ordered = grouped
            .Select(x => new SizeBreakdownItem(x.Key, x.Sum(y => y.Bytes)))
            .OrderByDescending(x => x.Bytes)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();

        var value = new SizeBreakdownValue(
            image.ImageHandle,
            groupBy.ToString().ToLowerInvariant(),
            resolvedMstatPath,
            contributions.Sum(x => x.Bytes),
            ordered.Length,
            ordered.Take(topN).ToArray());

        return NativeResult.Success(value);
    }

    public static bool TryParseContributions(
        string mstatPath,
        out IReadOnlyList<SizeContribution> contributions,
        out string error)
    {
        contributions = [];
        error = string.Empty;

        try
        {
            using var stream = File.OpenRead(mstatPath);
            using var peReader = new PEReader(stream);

            if (!peReader.HasMetadata)
            {
                error = ".mstat is not a managed metadata PE stream.";
                return false;
            }

            var reader = peReader.GetMetadataReader();
            var assemblyName = reader.IsAssembly
                ? reader.GetString(reader.GetAssemblyDefinition().Name)
                : Path.GetFileNameWithoutExtension(mstatPath);

            var entries = new List<SizeContribution>();

            foreach (var methodHandle in reader.MethodDefinitions)
            {
                var methodDef = reader.GetMethodDefinition(methodHandle);
                var methodName = reader.GetString(methodDef.Name);
                var rva = methodDef.RelativeVirtualAddress;
                if (rva == 0)
                {
                    continue;
                }

                var body = peReader.GetMethodBody(rva).GetILBytes();
                if (body is null || body.Length == 0)
                {
                    continue;
                }

                switch (methodName)
                {
                    case "Methods":
                        ParseMethods(body, reader, assemblyName, entries);
                        break;
                    case "Types":
                        ParseTypes(body, reader, assemblyName, entries);
                        break;
                    case "RvaFields":
                        ParseRvaFields(body, reader, assemblyName, entries);
                        break;
                    case "FrozenObjects":
                        ParseFrozenObjects(body, reader, assemblyName, entries);
                        break;
                    case "ManifestResources":
                        ParseResources(body, reader, assemblyName, entries);
                        break;
                    case "Blobs":
                        ParseBlobs(body, reader, assemblyName, entries);
                        break;
                }
            }

            contributions = entries;
            return true;
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void ParseMethods(
        ReadOnlySpan<byte> il,
        MetadataReader reader,
        string fallbackAssemblyName,
        List<SizeContribution> entries)
    {
        var instructions = DecodeIl(il);
        for (var i = 0; i + 4 < instructions.Count; i++)
        {
            if (instructions[i].OpCode != OpCodeKind.Ldtoken
                || instructions[i + 1].OpCode != OpCodeKind.LdcI4
                || instructions[i + 2].OpCode != OpCodeKind.LdcI4
                || instructions[i + 3].OpCode != OpCodeKind.LdcI4
                || instructions[i + 4].OpCode != OpCodeKind.LdcI4)
            {
                continue;
            }

            var methodHandle = MetadataTokens.EntityHandle(instructions[i].Operand);
            var codeSize = instructions[i + 1].Operand;
            var gcSize = instructions[i + 2].Operand;
            var ehSize = instructions[i + 3].Operand;

            if (!TryResolveMethodIdentity(reader, methodHandle, fallbackAssemblyName, out var id))
            {
                continue;
            }

            entries.Add(new SizeContribution(id.Assembly, id.Namespace, id.Type, id.Method, codeSize + gcSize + ehSize));
        }
    }

    private static void ParseTypes(
        ReadOnlySpan<byte> il,
        MetadataReader reader,
        string fallbackAssemblyName,
        List<SizeContribution> entries)
    {
        var instructions = DecodeIl(il);
        for (var i = 0; i + 2 < instructions.Count; i++)
        {
            if (instructions[i].OpCode != OpCodeKind.Ldtoken
                || instructions[i + 1].OpCode != OpCodeKind.LdcI4
                || instructions[i + 2].OpCode != OpCodeKind.LdcI4)
            {
                continue;
            }

            var typeHandle = MetadataTokens.EntityHandle(instructions[i].Operand);
            var size = instructions[i + 1].Operand;

            if (!TryResolveTypeIdentity(reader, typeHandle, fallbackAssemblyName, out var id))
            {
                continue;
            }

            entries.Add(new SizeContribution(id.Assembly, id.Namespace, id.Type, null, size));
        }
    }

    private static void ParseRvaFields(
        ReadOnlySpan<byte> il,
        MetadataReader reader,
        string fallbackAssemblyName,
        List<SizeContribution> entries)
    {
        var instructions = DecodeIl(il);
        for (var i = 0; i + 2 < instructions.Count; i++)
        {
            if (instructions[i].OpCode != OpCodeKind.Ldtoken
                || instructions[i + 1].OpCode != OpCodeKind.LdcI4
                || instructions[i + 2].OpCode != OpCodeKind.LdcI4)
            {
                continue;
            }

            var fieldHandle = MetadataTokens.EntityHandle(instructions[i].Operand);
            var size = instructions[i + 1].Operand;

            if (!TryResolveFieldIdentity(reader, fieldHandle, fallbackAssemblyName, out var id))
            {
                continue;
            }

            entries.Add(new SizeContribution(id.Assembly, id.Namespace, id.Type, null, size));
        }
    }

    private static void ParseFrozenObjects(
        ReadOnlySpan<byte> il,
        MetadataReader reader,
        string fallbackAssemblyName,
        List<SizeContribution> entries)
    {
        var instructions = DecodeIl(il);
        for (var i = 0; i + 2 < instructions.Count; i++)
        {
            if (instructions[i].OpCode != OpCodeKind.Ldtoken
                || instructions[i + 1].OpCode != OpCodeKind.LdcI4
                || instructions[i + 2].OpCode != OpCodeKind.LdcI4)
            {
                continue;
            }

            var typeHandle = MetadataTokens.EntityHandle(instructions[i].Operand);
            var size = instructions[i + 1].Operand;

            if (!TryResolveTypeIdentity(reader, typeHandle, fallbackAssemblyName, out var id))
            {
                continue;
            }

            entries.Add(new SizeContribution(id.Assembly, id.Namespace, id.Type, null, size));
        }
    }

    private static void ParseResources(
        ReadOnlySpan<byte> il,
        MetadataReader reader,
        string fallbackAssemblyName,
        List<SizeContribution> entries)
    {
        var instructions = DecodeIl(il);
        for (var i = 0; i + 2 < instructions.Count; i++)
        {
            if (instructions[i].OpCode != OpCodeKind.LdcI4
                || instructions[i + 1].OpCode != OpCodeKind.Ldstr
                || instructions[i + 2].OpCode != OpCodeKind.LdcI4)
            {
                continue;
            }

            var assembly = TryResolveAssemblyReference(reader, instructions[i].Operand) ?? fallbackAssemblyName;
            var size = instructions[i + 2].Operand;
            entries.Add(new SizeContribution(assembly, "<global>", "<resource>", null, size));
        }
    }

    private static void ParseBlobs(
        ReadOnlySpan<byte> il,
        MetadataReader reader,
        string fallbackAssemblyName,
        List<SizeContribution> entries)
    {
        var instructions = DecodeIl(il);
        for (var i = 0; i + 1 < instructions.Count; i++)
        {
            if (instructions[i].OpCode != OpCodeKind.Ldstr || instructions[i + 1].OpCode != OpCodeKind.LdcI4)
            {
                continue;
            }

            entries.Add(new SizeContribution(fallbackAssemblyName, "<global>", "<blob>", null, instructions[i + 1].Operand));
        }
    }

    private static string? ResolveMstatPath(NativeImageInfo image, string? explicitMstatPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitMstatPath))
        {
            var fullPath = Path.GetFullPath(explicitMstatPath);
            return File.Exists(fullPath) ? fullPath : null;
        }

        return image.MstatPath ?? NativeImageLoader.FindPairedMstatPath(image.BinaryPath);
    }

    private static bool TryResolveMethodIdentity(
        MetadataReader reader,
        EntityHandle handle,
        string fallbackAssemblyName,
        out SymbolIdentity identity)
    {
        identity = default;
        if (handle.Kind != HandleKind.MethodDefinition)
        {
            return false;
        }

        var methodHandle = (MethodDefinitionHandle)handle;
        var methodDef = reader.GetMethodDefinition(methodHandle);
        var methodName = reader.GetString(methodDef.Name);

        if (!TryResolveTypeIdentity(reader, methodDef.GetDeclaringType(), fallbackAssemblyName, out var typeIdentity))
        {
            return false;
        }

        identity = typeIdentity with { Method = $"{typeIdentity.Type}::{methodName}" };
        return true;
    }

    private static bool TryResolveFieldIdentity(
        MetadataReader reader,
        EntityHandle handle,
        string fallbackAssemblyName,
        out SymbolIdentity identity)
    {
        identity = default;
        if (handle.Kind != HandleKind.FieldDefinition)
        {
            return false;
        }

        var fieldDef = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
        return TryResolveTypeIdentity(reader, fieldDef.GetDeclaringType(), fallbackAssemblyName, out identity);
    }

    private static bool TryResolveTypeIdentity(
        MetadataReader reader,
        EntityHandle handle,
        string fallbackAssemblyName,
        out SymbolIdentity identity)
    {
        identity = default;
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            var typeDef = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            var ns = NormalizeNamespace(reader.GetString(typeDef.Namespace));
            var typeName = reader.GetString(typeDef.Name);
            identity = new SymbolIdentity(fallbackAssemblyName, ns, string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}", null);
            return true;
        }

        if (handle.Kind == HandleKind.TypeReference)
        {
            var typeRef = reader.GetTypeReference((TypeReferenceHandle)handle);
            var ns = NormalizeNamespace(reader.GetString(typeRef.Namespace));
            var typeName = reader.GetString(typeRef.Name);
            var assembly = typeRef.ResolutionScope.Kind == HandleKind.AssemblyReference
                ? reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)typeRef.ResolutionScope).Name)
                : fallbackAssemblyName;
            identity = new SymbolIdentity(assembly, ns, string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}", null);
            return true;
        }

        return false;
    }

    private static string? TryResolveAssemblyReference(MetadataReader reader, int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind != HandleKind.AssemblyReference)
        {
            return null;
        }

        return reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)handle).Name);
    }

    private static string NormalizeNamespace(string ns) => string.IsNullOrEmpty(ns) ? "<global>" : ns;

    private static List<IlInstruction> DecodeIl(ReadOnlySpan<byte> il)
    {
        var result = new List<IlInstruction>(il.Length / 2);
        var i = 0;

        while (i < il.Length)
        {
            var op = il[i++];
            switch (op)
            {
                case 0x2A:
                    result.Add(new IlInstruction(OpCodeKind.Ret, 0));
                    break;
                case 0xD0:
                    result.Add(new IlInstruction(OpCodeKind.Ldtoken, ReadInt32(il, ref i)));
                    break;
                case 0x72:
                    result.Add(new IlInstruction(OpCodeKind.Ldstr, ReadInt32(il, ref i)));
                    break;
                case 0x20:
                    result.Add(new IlInstruction(OpCodeKind.LdcI4, ReadInt32(il, ref i)));
                    break;
                case 0x1F:
                    result.Add(new IlInstruction(OpCodeKind.LdcI4, unchecked((sbyte)il[i++])));
                    break;
                case 0x15:
                    result.Add(new IlInstruction(OpCodeKind.LdcI4, -1));
                    break;
                case >= 0x16 and <= 0x1E:
                    result.Add(new IlInstruction(OpCodeKind.LdcI4, op - 0x16));
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported IL opcode 0x{op:x2} in .mstat stream.");
            }
        }

        return result;
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, ref int index)
    {
        if (index + 4 > data.Length)
        {
            throw new InvalidOperationException("Unexpected end of IL stream.");
        }

        var value = data[index]
            | (data[index + 1] << 8)
            | (data[index + 2] << 16)
            | (data[index + 3] << 24);
        index += 4;
        return value;
    }

    public sealed record SizeContribution(string Assembly, string Namespace, string Type, string? Method, int Bytes);

    private readonly record struct SymbolIdentity(string Assembly, string Namespace, string Type, string? Method);

    private readonly record struct IlInstruction(OpCodeKind OpCode, int Operand);

    private enum OpCodeKind
    {
        Ldtoken,
        Ldstr,
        LdcI4,
        Ret,
    }
}
