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
    Category,
}

/// <summary>Native-size attribution categories emitted by the NativeAOT mstat dumper (MSTAT 2.x).</summary>
public static class MstatCategory
{
    public const string Method = "method";
    public const string Type = "type";
    public const string Blob = "blob";

    public const string NativeAssembly = "(native)";
}

public sealed record MstatAttribution(
    string AssemblyName,
    string NamespaceName,
    string TypeName,
    string? MethodName,
    int TotalSize,
    string Source,
    string? SymbolName = null);

public sealed record MstatCategoryTotal(string Category, long TotalSize, int AttributionCount);

public sealed record MstatDocument(
    string FilePath,
    IReadOnlyList<MstatAttribution> Attributions,
    int MethodCount,
    int TypeCount,
    long TotalSize,
    string FormatVersion,
    IReadOnlyList<MstatCategoryTotal> CategoryTotals,
    int DeduplicatedMethodCount);

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

            var version = metadataReader.IsAssembly
                ? metadataReader.GetAssemblyDefinition().Version
                : new Version(0, 0);
            var formatVersion = $"{version.Major}.{version.Minor}";

            var names = ReadNamesSection(peReader);
            var resolver = new MetadataNameResolver(metadataReader);
            var attributions = new List<MstatAttribution>();

            // Methods + Types are present in every mstat version. The Blobs table is the catch-all
            // that captures every other native node (dehydrated data, runtime metadata, frozen-object
            // regions, RVA static-field data, manifest resources, …) — without it a size breakdown
            // silently undercounts the binary. In MSTAT 2.x the RvaFields / FrozenObjects /
            // ManifestResources tables are a finer-grained view of bytes that are *also* reported in
            // Blobs (the dumper falls through to `reportAsBlob` for VersionMajor==2), so counting them
            // here would double-count; Methods + Types + Blobs is the authoritative, non-overlapping
            // partition. DeduplicatedMethods carries no new bytes (folded bodies alias an existing one)
            // — it is reported as a count only.
            bool overflow = false;
            bool AddAll(IEnumerable<MstatAttribution> source)
            {
                foreach (var attribution in source)
                {
                    if (attributions.Count >= ResourceLimits.MaxMstatAttributions)
                    {
                        overflow = true;
                        return false;
                    }
                    attributions.Add(attribution);
                }
                return true;
            }

            if (AddAll(ReadMethodAttributions(peReader, metadataReader, resolver, names, methodsMethod))
                && AddAll(ReadTypeAttributions(peReader, metadataReader, resolver, names, typesMethod))
                && AddAll(ReadBlobAttributions(peReader, metadataReader)))
            {
                // All size-contributing tables read without hitting the attribution cap.
            }

            if (overflow)
            {
                return NativeResult.Fail<MstatDocument>(
                    ErrorKinds.FileTooLarge,
                    $"Mstat sidecar '{Path.GetFileName(fullPath)}' exceeds the maximum of {ResourceLimits.MaxMstatAttributions} attributions.");
            }

            var deduplicatedMethodCount = CountDeduplicatedMethods(peReader, metadataReader);

            long totalSize = 0;
            foreach (var attribution in attributions)
                totalSize += attribution.TotalSize;

            var categoryTotals = attributions
                .GroupBy(a => a.Source, StringComparer.Ordinal)
                .Select(g => new MstatCategoryTotal(g.Key, g.Sum(a => (long)a.TotalSize), g.Count()))
                .OrderByDescending(c => c.TotalSize)
                .ThenBy(c => c.Category, StringComparer.Ordinal)
                .ToList();

            return NativeResult.Ok(
                $"Read {attributions.Count} attribution(s) from '{Path.GetFileName(fullPath)}' (mstat {formatVersion}).",
                new MstatDocument(
                    fullPath,
                    new ReadOnlyCollection<MstatAttribution>(attributions),
                    attributions.Count(a => a.Source == MstatCategory.Method),
                    attributions.Count(a => a.Source == MstatCategory.Type),
                    totalSize,
                    formatVersion,
                    new ReadOnlyCollection<MstatCategoryTotal>(categoryTotals),
                    deduplicatedMethodCount));
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
                    groupBy == MstatGroupBy.Category ? MstatCategory.NativeAssembly : first.AssemblyName,
                    groupBy == MstatGroupBy.Category ? string.Empty : first.NamespaceName,
                    groupBy == MstatGroupBy.Category ? first.Source : first.TypeName,
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
        MstatGroupBy.Category => attribution.Source,
        _ => throw new ArgumentOutOfRangeException(nameof(groupBy), groupBy, null),
    };

    private static IEnumerable<MstatAttribution> ReadMethodAttributions(
        PEReader peReader,
        MetadataReader metadataReader,
        MetadataNameResolver resolver,
        NamesSection names,
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
            var nameIndex = ReadLdcI4(ref reader);

            var resolvedMethod = resolver.ResolveMethod(tokenHandle);
            yield return new MstatAttribution(
                resolvedMethod.AssemblyName,
                resolvedMethod.NamespaceName,
                resolvedMethod.TypeName,
                resolvedMethod.MethodName,
                checked(codeSize + gcInfoSize + ehInfoSize),
                MstatCategory.Method,
                names.Resolve(nameIndex));
        }
    }

    private static IEnumerable<MstatAttribution> ReadTypeAttributions(
        PEReader peReader,
        MetadataReader metadataReader,
        MetadataNameResolver resolver,
        NamesSection names,
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
            var nameIndex = ReadLdcI4(ref reader);

            var resolvedType = resolver.ResolveType(tokenHandle);
            yield return new MstatAttribution(
                resolvedType.AssemblyName,
                resolvedType.NamespaceName,
                resolvedType.TypeName,
                null,
                size,
                MstatCategory.Type,
                names.Resolve(nameIndex));
        }
    }

    // Blobs: ldstr(nodeName), ldc(size). The node name is the only identity (no managed token).
    private static IEnumerable<MstatAttribution> ReadBlobAttributions(PEReader peReader, MetadataReader metadataReader)
    {
        var handle = FindGlobalMethod(metadataReader, "Blobs");
        if (handle.IsNil)
            yield break;

        var body = GetMethodBody(peReader, metadataReader, handle, "Blobs");
        var reader = body.GetILReader();

        while (reader.RemainingBytes > 0)
        {
            if (TryReadRet(ref reader))
                break;

            var name = ReadLdStr(ref reader, metadataReader);
            var size = ReadLdcI4(ref reader);

            yield return new MstatAttribution(
                MstatCategory.NativeAssembly,
                string.Empty,
                name,
                null,
                size,
                MstatCategory.Blob,
                name);
        }
    }

    // RvaFields / FrozenObjects / ManifestResources tables are intentionally not summed: in
    // MSTAT 2.x their bytes are also reported in the Blobs table (the dumper falls through to
    // `reportAsBlob`), so they would double-count. Blobs is the authoritative catch-all.

    // DeduplicatedMethods: ldtoken(method), ldc(count), then count * (ldtoken(target), ldc(nameIndex)).
    // Folded bodies alias an existing method body, so they contribute no new native bytes — we only
    // count how many methods were deduplicated.
    private static int CountDeduplicatedMethods(PEReader peReader, MetadataReader metadataReader)
    {
        var handle = FindGlobalMethod(metadataReader, "DeduplicatedMethods");
        if (handle.IsNil)
            return 0;

        var body = GetMethodBody(peReader, metadataReader, handle, "DeduplicatedMethods");
        var reader = body.GetILReader();

        var count = 0;
        while (reader.RemainingBytes > 0)
        {
            if (TryReadRet(ref reader))
                break;

            _ = ReadLdToken(ref reader);
            var folded = ReadLdcI4(ref reader);
            count++;
            for (var i = 0; i < folded; i++)
            {
                _ = ReadLdToken(ref reader);
                _ = ReadLdcI4(ref reader);
            }
        }

        return count;
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

    private static string ReadLdStr(ref BlobReader reader, MetadataReader metadataReader)
    {
        var opcode = reader.ReadByte();
        if (opcode != 0x72)
            throw new InvalidDataException($"Expected ldstr (0x72), found 0x{opcode:x2}.");

        var token = reader.ReadInt32();
        var handle = MetadataTokens.Handle(token);
        if (handle.Kind != HandleKind.UserString)
            throw new InvalidDataException($"Expected a user-string token in ldstr, found 0x{token:x8}.");

        return metadataReader.GetUserString((UserStringHandle)handle);
    }

    /// <summary>
    /// Reads the <c>.names</c> custom PE section that the mstat dumper appends. The size tables
    /// store an integer index into this section; the bytes there are length-prefixed serialized
    /// strings (the native mangled symbol names).
    /// </summary>
    private static NamesSection ReadNamesSection(PEReader peReader)
    {
        foreach (var section in peReader.PEHeaders.SectionHeaders)
        {
            if (!string.Equals(section.Name, ".names", StringComparison.Ordinal))
                continue;

            try
            {
                return NamesSection.From(peReader.GetSectionData(".names"));
            }
            catch (Exception ex) when (ex is InvalidOperationException or BadImageFormatException)
            {
                break;
            }
        }

        return NamesSection.Empty;
    }

    private sealed class NamesSection
    {
        public static readonly NamesSection Empty = new(default, hasData: false);

        private readonly PEMemoryBlock _block;
        private readonly int _length;
        private readonly bool _hasData;

        private NamesSection(PEMemoryBlock block, bool hasData)
        {
            _block = block;
            _length = hasData ? block.Length : 0;
            _hasData = hasData && block.Length > 0;
        }

        public static NamesSection From(PEMemoryBlock block) => new(block, hasData: true);

        public string? Resolve(int index)
        {
            if (!_hasData || index < 0 || index >= _length)
                return null;

            try
            {
                var reader = _block.GetReader(index, _length - index);
                return reader.ReadSerializedString();
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentOutOfRangeException)
            {
                return null;
            }
        }
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
