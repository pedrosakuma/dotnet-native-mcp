using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Reflection;
using DotnetNativeMcp.Core.Errors;

namespace DotnetNativeMcp.Core.Mstat;

public enum MstatGroupBy
{
    Assembly,
    Namespace,
    Type,
    Method,
}

public sealed record MstatAttribution(
    string AssemblyName,
    string NamespaceName,
    string TypeName,
    string? MethodName,
    int TotalSize,
    string Source);

public sealed record MstatDocument(
    string FilePath,
    IReadOnlyList<MstatAttribution> Attributions,
    int MethodCount,
    int TypeCount,
    long TotalSize);

public sealed record MstatBreakdown(
    string Key,
    string AssemblyName,
    string NamespaceName,
    string TypeName,
    string? MethodName,
    long TotalSize,
    int AttributionCount);

public static class MstatReader
{
    public const int DefaultTopN = 25;
    public const int MaxTopN = 500;

    public static NativeResult<MstatDocument> Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return NativeResult.Fail<MstatDocument>(ErrorKinds.InvalidArgument, "mstatPath must not be empty.");

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return NativeResult.Fail<MstatDocument>(ErrorKinds.MstatNotFound, $"Mstat sidecar not found: '{Path.GetFileName(fullPath)}'.");

        try
        {
            var info = new FileInfo(fullPath);
            if (info.Length > ResourceLimits.MaxMstatBytes)
            {
                return NativeResult.Fail<MstatDocument>(
                    ErrorKinds.FileTooLarge,
                    $"Mstat sidecar '{Path.GetFileName(fullPath)}' is {info.Length} bytes, which exceeds the limit of {ResourceLimits.MaxMstatBytes} bytes.");
            }

            using var stream = File.OpenRead(fullPath);
            if (stream.CanSeek && stream.Length > ResourceLimits.MaxMstatBytes)
            {
                return NativeResult.Fail<MstatDocument>(
                    ErrorKinds.FileTooLarge,
                    $"Mstat sidecar '{Path.GetFileName(fullPath)}' is {stream.Length} bytes, which exceeds the limit of {ResourceLimits.MaxMstatBytes} bytes.");
            }
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                return NativeResult.Fail<MstatDocument>(
                    ErrorKinds.MstatInvalid,
                    $"'{Path.GetFileName(fullPath)}' is not a valid .mstat metadata image.");
            }

            var metadataReader = peReader.GetMetadataReader();
            var methodsMethod = FindGlobalMethod(metadataReader, "Methods");
            var typesMethod = FindGlobalMethod(metadataReader, "Types");
            if (methodsMethod.IsNil || typesMethod.IsNil)
            {
                return NativeResult.Fail<MstatDocument>(
                    ErrorKinds.MstatInvalid,
                    $"'{Path.GetFileName(fullPath)}' does not expose the expected NativeAOT mstat tables.");
            }

            var resolver = new MetadataNameResolver(metadataReader);
            var attributions = new List<MstatAttribution>();
            foreach (var attribution in ReadMethodAttributions(peReader, metadataReader, resolver, methodsMethod))
            {
                if (attributions.Count >= ResourceLimits.MaxMstatAttributions)
                {
                    return NativeResult.Fail<MstatDocument>(
                        ErrorKinds.FileTooLarge,
                        $"Mstat sidecar '{Path.GetFileName(fullPath)}' exceeds the maximum of {ResourceLimits.MaxMstatAttributions} attributions.");
                }
                attributions.Add(attribution);
            }
            foreach (var attribution in ReadTypeAttributions(peReader, metadataReader, resolver, typesMethod))
            {
                if (attributions.Count >= ResourceLimits.MaxMstatAttributions)
                {
                    return NativeResult.Fail<MstatDocument>(
                        ErrorKinds.FileTooLarge,
                        $"Mstat sidecar '{Path.GetFileName(fullPath)}' exceeds the maximum of {ResourceLimits.MaxMstatAttributions} attributions.");
                }
                attributions.Add(attribution);
            }

            long totalSize = 0;
            foreach (var attribution in attributions)
                totalSize += attribution.TotalSize;

            return NativeResult.Ok(
                $"Read {attributions.Count} attribution(s) from '{Path.GetFileName(fullPath)}'.",
                new MstatDocument(
                    fullPath,
                    new ReadOnlyCollection<MstatAttribution>(attributions),
                    attributions.Count(a => a.MethodName is not null),
                    attributions.Count(a => a.MethodName is null),
                    totalSize));
        }
        catch (BadImageFormatException ex)
        {
            return NativeResult.Fail<MstatDocument>(
                ErrorKinds.MstatInvalid,
                $"'{Path.GetFileName(fullPath)}' is not a readable NativeAOT mstat image.",
                SanitisedError.From(ex, fullPath));
        }
        catch (InvalidDataException ex)
        {
            return NativeResult.Fail<MstatDocument>(
                ErrorKinds.MstatInvalid,
                $"'{Path.GetFileName(fullPath)}' contains malformed NativeAOT mstat table data.",
                SanitisedError.From(ex, fullPath));
        }
        catch (Exception ex)
        {
            return NativeResult.Fail<MstatDocument>(
                ErrorKinds.InternalError,
                $"Failed to read '{Path.GetFileName(fullPath)}'.",
                SanitisedError.From(ex, fullPath));
        }
    }

    public static IReadOnlyList<MstatBreakdown> Aggregate(IReadOnlyList<MstatAttribution> attributions, MstatGroupBy groupBy, int topN)
    {
        ArgumentNullException.ThrowIfNull(attributions);

        if (topN <= 0)
            topN = DefaultTopN;
        if (topN > MaxTopN)
            topN = MaxTopN;

        IEnumerable<MstatAttribution> source = attributions;
        if (groupBy == MstatGroupBy.Method)
            source = source.Where(a => a.MethodName is not null);

        var rows = source
            .GroupBy(attribution => BuildKey(attribution, groupBy), StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                return new MstatBreakdown(
                    group.Key,
                    first.AssemblyName,
                    first.NamespaceName,
                    first.TypeName,
                    groupBy == MstatGroupBy.Method ? first.MethodName : null,
                    group.Sum(a => (long)a.TotalSize),
                    group.Count());
            })
            .OrderByDescending(row => row.TotalSize)
            .ThenBy(row => row.Key, StringComparer.Ordinal)
            .Take(topN)
            .ToList();

        return new ReadOnlyCollection<MstatBreakdown>(rows);
    }

    public static string GetDefaultMstatPath(string binaryPath) => Path.ChangeExtension(binaryPath, ".mstat");

    public static bool HasSiblingMstat(string binaryPath) => File.Exists(GetDefaultMstatPath(binaryPath));

    private static string BuildKey(MstatAttribution attribution, MstatGroupBy groupBy) => groupBy switch
    {
        MstatGroupBy.Assembly => attribution.AssemblyName,
        MstatGroupBy.Namespace => $"{attribution.AssemblyName}:{attribution.NamespaceName}",
        MstatGroupBy.Type => $"{attribution.AssemblyName}:{attribution.TypeName}",
        MstatGroupBy.Method => $"{attribution.AssemblyName}:{attribution.TypeName}.{attribution.MethodName}",
        _ => throw new ArgumentOutOfRangeException(nameof(groupBy), groupBy, null),
    };

    private static IEnumerable<MstatAttribution> ReadMethodAttributions(
        PEReader peReader,
        MetadataReader metadataReader,
        MetadataNameResolver resolver,
        MethodDefinitionHandle methodHandle)
    {
        var body = GetMethodBody(peReader, metadataReader, methodHandle, "Methods");
        var reader = body.GetILReader();

        while (reader.RemainingBytes > 0)
        {
            if (TryReadRet(ref reader))
                break;

            var tokenHandle = ReadLdToken(ref reader);
            var codeSize = ReadLdcI4(ref reader);
            var gcInfoSize = ReadLdcI4(ref reader);
            var ehInfoSize = ReadLdcI4(ref reader);
            _ = ReadLdcI4(ref reader);

            var resolvedMethod = resolver.ResolveMethod(tokenHandle);
            yield return new MstatAttribution(
                resolvedMethod.AssemblyName,
                resolvedMethod.NamespaceName,
                resolvedMethod.TypeName,
                resolvedMethod.MethodName,
                checked(codeSize + gcInfoSize + ehInfoSize),
                "method");
        }
    }

    private static IEnumerable<MstatAttribution> ReadTypeAttributions(
        PEReader peReader,
        MetadataReader metadataReader,
        MetadataNameResolver resolver,
        MethodDefinitionHandle methodHandle)
    {
        var body = GetMethodBody(peReader, metadataReader, methodHandle, "Types");
        var reader = body.GetILReader();

        while (reader.RemainingBytes > 0)
        {
            if (TryReadRet(ref reader))
                break;

            var tokenHandle = ReadLdToken(ref reader);
            var size = ReadLdcI4(ref reader);
            _ = ReadLdcI4(ref reader);

            var resolvedType = resolver.ResolveType(tokenHandle);
            yield return new MstatAttribution(
                resolvedType.AssemblyName,
                resolvedType.NamespaceName,
                resolvedType.TypeName,
                null,
                size,
                "type");
        }
    }

    private static MethodBodyBlock GetMethodBody(
        PEReader peReader,
        MetadataReader metadataReader,
        MethodDefinitionHandle methodHandle,
        string expectedName)
    {
        var method = metadataReader.GetMethodDefinition(methodHandle);
        var actualName = metadataReader.GetString(method.Name);
        if (!string.Equals(actualName, expectedName, StringComparison.Ordinal))
            throw new InvalidDataException($"Expected global method '{expectedName}', found '{actualName}'.");

        return peReader.GetMethodBody(method.RelativeVirtualAddress);
    }

    private static MethodDefinitionHandle FindGlobalMethod(MetadataReader metadataReader, string name)
    {
        foreach (var typeHandle in metadataReader.TypeDefinitions)
        {
            var type = metadataReader.GetTypeDefinition(typeHandle);
            if (!string.Equals(metadataReader.GetString(type.Name), "<Module>", StringComparison.Ordinal))
                continue;

            foreach (var methodHandle in type.GetMethods())
            {
                var method = metadataReader.GetMethodDefinition(methodHandle);
                if (string.Equals(metadataReader.GetString(method.Name), name, StringComparison.Ordinal))
                    return methodHandle;
            }
        }

        return default;
    }

    private static bool TryReadRet(ref BlobReader reader)
    {
        if (reader.RemainingBytes == 0)
            return true;

        var offset = reader.Offset;
        var opcode = reader.ReadByte();
        if (opcode == 0x2A)
            return true;

        reader.Offset = offset;
        return false;
    }

    private static EntityHandle ReadLdToken(ref BlobReader reader)
    {
        var opcode = reader.ReadByte();
        if (opcode != 0xD0)
            throw new InvalidDataException($"Expected ldtoken (0xD0), found 0x{opcode:x2}.");

        var token = reader.ReadInt32();
        var handle = MetadataTokens.Handle(token);
        if (handle.Kind == HandleKind.UserString)
            throw new InvalidDataException($"Unexpected user-string token 0x{token:x8} in ldtoken.");

        return (EntityHandle)handle;
    }

    private static int ReadLdcI4(ref BlobReader reader)
    {
        var opcode = reader.ReadByte();
        return opcode switch
        {
            0x15 => -1,
            >= 0x16 and <= 0x1E => opcode - 0x16,
            0x1F => reader.ReadSByte(),
            0x20 => reader.ReadInt32(),
            _ => throw new InvalidDataException($"Expected ldc.i4 opcode, found 0x{opcode:x2}."),
        };
    }

    private readonly record struct ResolvedType(string AssemblyName, string NamespaceName, string TypeName);

    private readonly record struct ResolvedMethod(string AssemblyName, string NamespaceName, string TypeName, string MethodName);

    private sealed class MetadataNameResolver(MetadataReader metadataReader)
    {
        private readonly MetadataReader _metadataReader = metadataReader;
        private readonly string _currentAssemblyName = metadataReader.IsAssembly ? metadataReader.GetString(metadataReader.GetAssemblyDefinition().Name) : string.Empty;

        public ResolvedMethod ResolveMethod(EntityHandle handle)
        {
            return handle.Kind switch
            {
                HandleKind.MethodDefinition => ResolveMethodDefinition((MethodDefinitionHandle)handle),
                HandleKind.MemberReference => ResolveMemberReference((MemberReferenceHandle)handle),
                HandleKind.MethodSpecification => ResolveMethodSpecification((MethodSpecificationHandle)handle),
                _ => new ResolvedMethod(_currentAssemblyName, string.Empty, $"[token 0x{MetadataTokens.GetToken(handle):x8}]", $"[token 0x{MetadataTokens.GetToken(handle):x8}]"),
            };
        }

        public ResolvedType ResolveType(EntityHandle handle)
        {
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => ResolveTypeDefinition((TypeDefinitionHandle)handle),
                HandleKind.TypeReference => ResolveTypeReference((TypeReferenceHandle)handle),
                HandleKind.TypeSpecification => ResolveTypeSpecification((TypeSpecificationHandle)handle),
                _ => new ResolvedType(_currentAssemblyName, string.Empty, $"[token 0x{MetadataTokens.GetToken(handle):x8}]"),
            };
        }

        private ResolvedMethod ResolveMethodDefinition(MethodDefinitionHandle handle)
        {
            var method = _metadataReader.GetMethodDefinition(handle);
            var type = ResolveTypeDefinition(method.GetDeclaringType());
            return new ResolvedMethod(type.AssemblyName, type.NamespaceName, type.TypeName, _metadataReader.GetString(method.Name));
        }

        private ResolvedMethod ResolveMemberReference(MemberReferenceHandle handle)
        {
            var member = _metadataReader.GetMemberReference(handle);
            var methodName = _metadataReader.GetString(member.Name);
            var parent = member.Parent;
            if (parent.Kind is HandleKind.MethodDefinition or HandleKind.MemberReference or HandleKind.MethodSpecification)
            {
                var parentMethod = ResolveMethod(parent);
                return new ResolvedMethod(parentMethod.AssemblyName, parentMethod.NamespaceName, parentMethod.TypeName, methodName);
            }

            var type = ResolveType(parent);
            return new ResolvedMethod(type.AssemblyName, type.NamespaceName, type.TypeName, methodName);
        }

        private ResolvedMethod ResolveMethodSpecification(MethodSpecificationHandle handle)
        {
            var specification = _metadataReader.GetMethodSpecification(handle);
            return ResolveMethod(specification.Method);
        }

        private ResolvedType ResolveTypeDefinition(TypeDefinitionHandle handle)
        {
            var type = _metadataReader.GetTypeDefinition(handle);
            var name = _metadataReader.GetString(type.Name);
            var ns = _metadataReader.GetString(type.Namespace);
            if ((type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public &&
                (type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.NotPublic)
            {
                var declaring = ResolveType(type.GetDeclaringType());
                return new ResolvedType(declaring.AssemblyName, declaring.NamespaceName, $"{declaring.TypeName}+{name}");
            }

            return new ResolvedType(_currentAssemblyName, ns, string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}");
        }

        private ResolvedType ResolveTypeReference(TypeReferenceHandle handle)
        {
            var type = _metadataReader.GetTypeReference(handle);
            var name = _metadataReader.GetString(type.Name);
            var ns = _metadataReader.GetString(type.Namespace);
            var scope = type.ResolutionScope;

            if (scope.Kind == HandleKind.TypeReference)
            {
                var declaring = ResolveTypeReference((TypeReferenceHandle)scope);
                return new ResolvedType(declaring.AssemblyName, declaring.NamespaceName, $"{declaring.TypeName}+{name}");
            }

            return new ResolvedType(ResolveAssemblyName(scope), ns, string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}");
        }

        private ResolvedType ResolveTypeSpecification(TypeSpecificationHandle handle)
        {
            var type = _metadataReader.GetTypeSpecification(handle);
            var provider = new TypeNameProvider(_metadataReader, this, _currentAssemblyName);
            return type.DecodeSignature(provider, genericContext: (object?)null);
        }

        private string ResolveAssemblyName(EntityHandle scope) => scope.Kind switch
        {
            HandleKind.AssemblyReference => _metadataReader.GetString(_metadataReader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name),
            HandleKind.ModuleDefinition or HandleKind.ModuleReference => _currentAssemblyName,
            HandleKind.TypeReference => ResolveTypeReference((TypeReferenceHandle)scope).AssemblyName,
            _ => _currentAssemblyName,
        };
    }

    private sealed class TypeNameProvider(MetadataReader metadataReader, MetadataNameResolver resolver, string currentAssemblyName)
        : ISignatureTypeProvider<ResolvedType, object?>
    {
        private readonly MetadataReader _metadataReader = metadataReader;
        private readonly MetadataNameResolver _resolver = resolver;
        private readonly string _currentAssemblyName = currentAssemblyName;

        public ResolvedType GetArrayType(ResolvedType elementType, ArrayShape shape)
            => new(elementType.AssemblyName, elementType.NamespaceName, $"{elementType.TypeName}[{new string(',', shape.Rank - 1)}]");

        public ResolvedType GetByReferenceType(ResolvedType elementType)
            => new(elementType.AssemblyName, elementType.NamespaceName, $"{elementType.TypeName}&");

        public ResolvedType GetFunctionPointerType(MethodSignature<ResolvedType> signature)
            => new(_currentAssemblyName, string.Empty, "fnptr");

        public ResolvedType GetGenericInstantiation(ResolvedType genericType, ImmutableArray<ResolvedType> typeArguments)
            => new(
                genericType.AssemblyName,
                genericType.NamespaceName,
                $"{genericType.TypeName}<{string.Join(", ", typeArguments.Select(static arg => arg.TypeName))}>");

        public ResolvedType GetGenericMethodParameter(object? genericContext, int index)
            => new(_currentAssemblyName, string.Empty, $"!!{index}");

        public ResolvedType GetGenericTypeParameter(object? genericContext, int index)
            => new(_currentAssemblyName, string.Empty, $"!{index}");

        public ResolvedType GetModifiedType(ResolvedType modifier, ResolvedType unmodifiedType, bool isRequired)
            => unmodifiedType;

        public ResolvedType GetPinnedType(ResolvedType elementType)
            => elementType;

        public ResolvedType GetPointerType(ResolvedType elementType)
            => new(elementType.AssemblyName, elementType.NamespaceName, $"{elementType.TypeName}*");

        public ResolvedType GetPrimitiveType(PrimitiveTypeCode typeCode)
            => new(_currentAssemblyName, string.Empty, typeCode.ToString());

        public ResolvedType GetSZArrayType(ResolvedType elementType)
            => new(elementType.AssemblyName, elementType.NamespaceName, $"{elementType.TypeName}[]");

        public ResolvedType GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => _resolver.ResolveType(handle);

        public ResolvedType GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => _resolver.ResolveType(handle);

        public ResolvedType GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            var specification = _metadataReader.GetTypeSpecification(handle);
            return specification.DecodeSignature(this, genericContext);
        }

        public ResolvedType GetTypeFromSerializedName(string name)
        {
            var trimmed = name.Trim();
            var lastDot = trimmed.LastIndexOf('.');
            var ns = lastDot > 0 ? trimmed[..lastDot] : string.Empty;
            return new ResolvedType(_currentAssemblyName, ns, trimmed);
        }
    }
}
