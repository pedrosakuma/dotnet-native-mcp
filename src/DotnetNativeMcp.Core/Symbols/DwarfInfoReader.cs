using System.Collections.Concurrent;
using System.IO.Compression;
using DotnetNativeMcp.Core.Imaging;

namespace DotnetNativeMcp.Core.Symbols;

/// <summary>
/// Parses DWARF <c>.debug_info</c> and <c>.debug_abbrev</c> sections to extract
/// function signatures (return type + parameter type list) for a given RVA.
///
/// <para>
/// Coverage: DWARF 4 <c>DW_TAG_subprogram</c> with <c>DW_AT_low_pc</c> /
/// <c>DW_AT_high_pc</c>, full <c>DW_AT_specification</c> chain. Type resolution
/// covers pointer, const, reference, typedef, base_type, structure, class, array,
/// and unspecified types. C++ template types are rendered as <c>&lt;unknown&gt;</c>.
/// </para>
///
/// <para>
/// Recursive type walks are bounded to <see cref="MaxTypeDepth"/> levels and include
/// a cycle-guard set so a malformed type chain cannot stack-overflow.
/// </para>
/// </summary>
public static class DwarfInfoReader
{
    private const int MaxTypeDepth = 32;

    // Tag constants
    private const ushort TagSubprogram = 0x2e;
    private const ushort TagFormalParameter = 0x05;
    private const ushort TagBaseType = 0x24;
    private const ushort TagPointerType = 0x0f;
    private const ushort TagConstType = 0x26;
    private const ushort TagVolatileType = 0x35;
    private const ushort TagRestrictType = 0x37;
    private const ushort TagReferenceType = 0x10;
    private const ushort TagTypedef = 0x16;
    private const ushort TagStructureType = 0x13;
    private const ushort TagClassType = 0x02;
    private const ushort TagArrayType = 0x01;
    private const ushort TagEnumerationType = 0x04;
    private const ushort TagUnspecifiedType = 0x3b;
    private const ushort TagTemplateTypeParam = 0x2f;
    private const ushort TagTemplateValueParam = 0x30;
    private const ushort TagCompileUnit = 0x11;

    // Attribute constants
    private const ushort AtName = 0x03;
    private const ushort AtType = 0x49;
    private const ushort AtLowPc = 0x11;
    private const ushort AtHighPc = 0x12;
    private const ushort AtSpecification = 0x47;
    private const ushort AtAbstractOrigin = 0x31;
    private const ushort AtArtificial = 0x34;
    private const ushort AtByteSize = 0x0b;
    private const ushort AtEncoding = 0x3e;
    private const ushort AtLinkageName = 0x6e;
    private const ushort AtObjectPointer = 0x64;
    private const ushort AtExternal = 0x3f;
    private const ushort AtDeclaration = 0x3c;
    private const ushort AtFrameBase = 0x40;
    private const ushort AtLocation = 0x02;
    private const ushort AtDataMemberLocation = 0x38;
    private const ushort AtUpperBound = 0x2f;
    private const ushort AtConstValue = 0x1c;
    private const ushort AtStmtList = 0x10;
    private const ushort AtCompDir = 0x1b;
    private const ushort AtProducer = 0x25;
    private const ushort AtLanguage = 0x13;
    private const ushort AtRanges = 0x55;

    // Form constants (DWARF 4)
    private const byte FormAddr = 0x01;
    private const byte FormBlock2 = 0x03;
    private const byte FormBlock4 = 0x04;
    private const byte FormData2 = 0x05;
    private const byte FormData4 = 0x06;
    private const byte FormData8 = 0x07;
    private const byte FormString = 0x08;
    private const byte FormBlock = 0x09;
    private const byte FormBlock1 = 0x0a;
    private const byte FormData1 = 0x0b;
    private const byte FormFlag = 0x0c;
    private const byte FormSdata = 0x0d;
    private const byte FormStrp = 0x0e;
    private const byte FormUdata = 0x0f;
    private const byte FormRefAddr = 0x10;
    private const byte FormRef1 = 0x11;
    private const byte FormRef2 = 0x12;
    private const byte FormRef4 = 0x13;
    private const byte FormRef8 = 0x14;
    private const byte FormRefUdata = 0x15;
    private const byte FormIndirect = 0x16;
    private const byte FormSecOffset = 0x17;
    private const byte FormExprloc = 0x18;
    private const byte FormFlagPresent = 0x19;
    // DWARF 5 forms (skip gracefully)
    private const byte FormStrx = 0x1a;
    private const byte FormAddrx = 0x1b;
    private const byte FormLineStrp = 0x1f;
    private const byte FormStrx1 = 0x25;
    private const byte FormStrx2 = 0x26;
    private const byte FormStrx3 = 0x27;
    private const byte FormStrx4 = 0x28;
    private const byte FormAddrx1 = 0x29;
    private const byte FormAddrx2 = 0x2a;
    private const byte FormAddrx3 = 0x2b;
    private const byte FormAddrx4 = 0x2c;

    // -------------------------------------------------------------------------
    // Internal data structures
    // -------------------------------------------------------------------------

    private sealed class AbbrevEntry
    {
        public ushort Tag;
        public bool HasChildren;
        public (ushort Attr, byte Form)[] Attrs = [];
    }

    private sealed class DieData
    {
        public ushort Tag;
        public string? Name;
        public uint? TypeOffset;       // DW_AT_type, CU-relative
        public uint? SpecOffset;       // DW_AT_specification, CU-relative
        public uint? AbstractOrigin;   // DW_AT_abstract_origin, CU-relative
        public ulong LowPc;
        public ulong HighPcEnd;        // absolute end address (low_pc + high_pc_size or high_pc_abs)
        public bool HasLowPc;
        public List<uint>? ChildOffsets; // formal_parameter child CU-relative offsets (ordered)
        public bool IsArtificial;
    }

    private sealed class DwarfInfoIndex
    {
        // Ranges sorted by Start for binary search.
        public readonly List<(ulong Start, ulong End, uint DieOffset)> Ranges = [];
        // All DIEs we care about, keyed by CU-relative offset.
        public readonly Dictionary<uint, DieData> Dies = [];
        public byte[] DebugStr = [];
    }

    private static readonly ConcurrentDictionary<string, DwarfInfoIndex?> _cache = new();

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts to resolve the DWARF function signature at <paramref name="rva"/>
    /// in <paramref name="image"/>. Returns a string like
    /// <c>ReturnType Name(ParamType, ParamType)</c> or <c>null</c> when debug
    /// info is not available or no subprogram covers the address.
    /// </summary>
    public static string? TryGetSignatureForRva(NativeImage image, ulong rva)
    {
        try
        {
            var idx = _cache.GetOrAdd(image.Handle.Value, _ => TryBuildIndex(image));
            if (idx is null) return null;
            return TryResolveSignature(idx, rva);
        }
        catch
        {
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Index building
    // -------------------------------------------------------------------------

    private static DwarfInfoIndex? TryBuildIndex(NativeImage image)
    {
        try
        {
            var infoSection = image.Sections.FirstOrDefault(s => s.Name == ".debug_info");
            var abbrevSection = image.Sections.FirstOrDefault(s => s.Name == ".debug_abbrev");
            if (infoSection is null || abbrevSection is null) return null;

            var infoRaw = image.GetSectionBytes(infoSection).ToArray();
            var abbrevRaw = image.GetSectionBytes(abbrevSection).ToArray();

            var infoData = TryDecompressElf(infoRaw) ?? infoRaw;
            var abbrevData = TryDecompressElf(abbrevRaw) ?? abbrevRaw;

            var strSection = image.Sections.FirstOrDefault(s => s.Name == ".debug_str");
            var strData = Array.Empty<byte>();
            if (strSection is not null)
            {
                var strRaw = image.GetSectionBytes(strSection).ToArray();
                strData = TryDecompressElf(strRaw) ?? strRaw;
            }

            var idx = new DwarfInfoIndex { DebugStr = strData };
            ParseDebugInfo(infoData, abbrevData, idx);
            idx.Ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
            return idx;
        }
        catch
        {
            return null;
        }
    }

    private static void ParseDebugInfo(byte[] info, byte[] abbrev, DwarfInfoIndex idx)
    {
        int offset = 0;
        while (offset < info.Length)
        {
            if (!TryParseCu(info, abbrev, idx, ref offset)) break;
        }
    }

    private static bool TryParseCu(byte[] info, byte[] abbrev, DwarfInfoIndex idx, ref int offset)
    {
        try
        {
            if (offset + 4 > info.Length) return false;
            // Save the section-absolute start of this CU (unit_length field).
            // DW_FORM_ref1/2/4 values are offsets from here.
            int cuStart = offset;
            uint unitLen = ReadU32(info, offset); offset += 4;
            if (unitLen == 0xFFFFFFFF)
            {
                // 64-bit DWARF — skip (not common in NativeAOT)
                if (offset + 8 > info.Length) return false;
                offset += 8;
                return false;
            }
            if (unitLen == 0) return false;

            long unitEndL = (long)offset + unitLen;
            if (unitEndL > info.Length) return false;
            int unitEnd = (int)unitEndL;

            if (offset + 2 > unitEnd) { offset = unitEnd; return true; }
            ushort version = ReadU16(info, offset); offset += 2;
            if (version < 2 || version > 5) { offset = unitEnd; return true; }

            // DWARF 4 header: debug_abbrev_offset(4), address_size(1).
            // DWARF 5 header: unit_type(1), address_size(1), debug_abbrev_offset(4).
            uint abbrevOffset;
            byte addressSize;
            if (version >= 5)
            {
                if (offset + 1 > unitEnd) { offset = unitEnd; return true; }
                offset += 1; // unit_type
                if (offset + 1 > unitEnd) { offset = unitEnd; return true; }
                addressSize = info[offset++]; // address_size comes before abbrev_offset in DWARF 5
                if (offset + 4 > unitEnd) { offset = unitEnd; return true; }
                abbrevOffset = ReadU32(info, offset); offset += 4;
            }
            else
            {
                if (offset + 4 > unitEnd) { offset = unitEnd; return true; }
                abbrevOffset = ReadU32(info, offset); offset += 4;
                if (offset + 1 > unitEnd) { offset = unitEnd; return true; }
                addressSize = info[offset++];
            }

            // Parse the abbreviation table for this CU.
            var abbrevTable = ParseAbbrevTable(abbrev, (int)abbrevOffset);

            // Walk the DIE tree.
            // cuStart is the section-absolute base for CU-relative (ref1/2/4) references.
            ParseDieTree(info, cuStart, ref offset, unitEnd, abbrevTable,
                abbrevOffset, addressSize, idx, version);

            offset = unitEnd;
            return true;
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or OverflowException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static Dictionary<uint, AbbrevEntry> ParseAbbrevTable(byte[] abbrev, int offset)
    {
        var table = new Dictionary<uint, AbbrevEntry>();
        while (offset < abbrev.Length)
        {
            uint code = (uint)ReadULEB128(abbrev, ref offset);
            if (code == 0) break;
            ushort tag = (ushort)ReadULEB128(abbrev, ref offset);
            if (offset >= abbrev.Length) break;
            bool hasChildren = abbrev[offset++] == 1;

            var attrs = new List<(ushort, byte)>();
            while (offset < abbrev.Length)
            {
                ushort attr = (ushort)ReadULEB128(abbrev, ref offset);
                byte form = (byte)ReadULEB128(abbrev, ref offset);
                if (attr == 0 && form == 0) break;
                attrs.Add((attr, form));
            }

            table[code] = new AbbrevEntry
            {
                Tag = tag,
                HasChildren = hasChildren,
                Attrs = [.. attrs],
            };
        }
        return table;
    }

    private static void ParseDieTree(
        byte[] info, int cuBase, ref int offset, int unitEnd,
        Dictionary<uint, AbbrevEntry> abbrevTable,
        uint abbrevOffset, byte addressSize, DwarfInfoIndex idx, ushort version)
    {
        // Stack of (parent DieData?, isParentSubprogram) to track formal_parameter
        // children. We only need one level of nesting for our use case.
        var parentStack = new Stack<DieData?>();
        parentStack.Push(null);
        var depthStack = new Stack<int>();
        depthStack.Push(0);
        int depth = 0;

        while (offset < unitEnd)
        {
            int dieOffset = offset;
            if (offset >= info.Length) break;

            uint code = (uint)ReadULEB128(info, ref offset);
            if (code == 0)
            {
                // null DIE = end of children
                depth--;
                if (depthStack.Count > 0 && depthStack.Peek() == depth + 1)
                    depthStack.Pop();
                if (parentStack.Count > 1)
                    parentStack.Pop();
                continue;
            }

            if (!abbrevTable.TryGetValue(code, out var entry))
                break; // Unknown abbreviation code — stop this CU.

            // Use section-absolute offset as the key for this DIE.
            // DW_FORM_ref1/2/4 values, when added to cuBase (CU header start),
            // also produce section-absolute offsets — so keys and lookups are consistent.
            uint dieKey = (uint)dieOffset;

            // Parse attributes.
            DieData? die = null;
            string? name = null;
            uint? typeRef = null;
            uint? specRef = null;
            uint? abstractOriginRef = null;
            ulong lowPc = 0;
            ulong highPcValue = 0;
            bool hasLowPc = false;
            bool highPcIsAbsolute = false;
            bool isArtificial = false;

            bool isSubprogram = entry.Tag == TagSubprogram;
            bool isFormalParam = entry.Tag == TagFormalParameter;
            bool isInterestingType = IsTypeTag(entry.Tag);

            foreach (var (attr, form) in entry.Attrs)
            {
                int attrStart = offset;
                switch (attr)
                {
                    case AtName:
                        name = ReadAttrString(info, form, addressSize, version, idx.DebugStr, ref offset);
                        break;
                    case AtType:
                        typeRef = ReadAttrRef(info, form, addressSize, version, cuBase, ref offset);
                        break;
                    case AtSpecification:
                        specRef = ReadAttrRef(info, form, addressSize, version, cuBase, ref offset);
                        break;
                    case AtAbstractOrigin:
                        abstractOriginRef = ReadAttrRef(info, form, addressSize, version, cuBase, ref offset);
                        break;
                    case AtLowPc:
                        lowPc = ReadAttrAddr(info, form, addressSize, ref offset);
                        hasLowPc = true;
                        break;
                    case AtHighPc:
                        (highPcValue, highPcIsAbsolute) = ReadAttrHighPc(info, form, addressSize, ref offset);
                        break;
                    case AtArtificial:
                        isArtificial = true;
                        SkipAttr(info, form, addressSize, version, ref offset);
                        break;
                    default:
                        SkipAttr(info, form, addressSize, version, ref offset);
                        break;
                }
                // Bounds check: if offset went backwards or past unitEnd, bail.
                if (offset < attrStart || offset > unitEnd) goto done;
            }

            if (isSubprogram || isInterestingType || isFormalParam)
            {
                die = new DieData
                {
                    Tag = entry.Tag,
                    Name = name,
                    TypeOffset = typeRef,
                    SpecOffset = specRef,
                    AbstractOrigin = abstractOriginRef,
                    LowPc = lowPc,
                    HasLowPc = hasLowPc,
                    IsArtificial = isArtificial,
                };

                if (hasLowPc)
                {
                    die.HighPcEnd = highPcIsAbsolute
                        ? highPcValue
                        : lowPc + highPcValue;
                }

                idx.Dies[dieKey] = die;

                // Register in range table if this is a concrete subprogram with address.
                if (isSubprogram && hasLowPc && die.HighPcEnd > lowPc)
                    idx.Ranges.Add((lowPc, die.HighPcEnd, dieKey));

                // Attach as child of current parent if it is a formal_parameter.
                if (isFormalParam && parentStack.Count > 0 && parentStack.Peek() is DieData parent)
                {
                    parent.ChildOffsets ??= [];
                    parent.ChildOffsets.Add(dieKey);
                }
            }

            if (entry.HasChildren)
            {
                depth++;
                // Push current die as new parent only for subprograms
                // (we track formal_parameter children of subprograms).
                parentStack.Push(isSubprogram ? die : null);
                depthStack.Push(depth);
            }
        }

        done: ;
    }

    // -------------------------------------------------------------------------
    // Signature resolution
    // -------------------------------------------------------------------------

    private static string? TryResolveSignature(DwarfInfoIndex idx, ulong rva)
    {
        // Binary-search the sorted range list.
        var ranges = idx.Ranges;
        if (ranges.Count == 0) return null;

        int lo = 0, hi = ranges.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (ranges[mid].Start > rva) hi = mid - 1;
            else if (ranges[mid].End <= rva) lo = mid + 1;
            else { lo = mid; break; }
        }
        if (lo >= ranges.Count || lo < 0) return null;
        var (start, end, dieOffset) = ranges[lo];
        if (rva < start || rva >= end) return null;

        if (!idx.Dies.TryGetValue(dieOffset, out var subprogramDie)) return null;

        // Follow specification or abstract_origin to get the declaration.
        DieData? decl = null;
        if (subprogramDie.SpecOffset is uint specOff && idx.Dies.TryGetValue(specOff, out var specDie))
            decl = specDie;
        else if (subprogramDie.AbstractOrigin is uint absOff && idx.Dies.TryGetValue(absOff, out var absDie))
            decl = absDie;
        else
            decl = subprogramDie; // inline definition

        var funcName = decl?.Name ?? subprogramDie.Name ?? "?";

        // Return type.
        uint? retTypeOffset = decl?.TypeOffset ?? subprogramDie.TypeOffset;
        var visited = new HashSet<uint>();
        string retType = retTypeOffset.HasValue
            ? FormatType(idx, retTypeOffset.Value, 0, visited)
            : "void";

        // Formal parameters (from the decl, not the concrete subprogram).
        // Concrete subprograms may also have params — use decl first.
        var paramSource = decl ?? subprogramDie;
        var paramOffsets = paramSource.ChildOffsets ?? subprogramDie.ChildOffsets;
        var paramNames = new List<string>();
        if (paramOffsets is not null)
        {
            foreach (var paramOff in paramOffsets)
            {
                if (!idx.Dies.TryGetValue(paramOff, out var param)) continue;
                // Skip 'this' (artificial) parameters.
                if (param.IsArtificial) continue;
                visited.Clear();
                string paramType = param.TypeOffset.HasValue
                    ? FormatType(idx, param.TypeOffset.Value, 0, visited)
                    : "?";
                paramNames.Add(paramType);
            }
        }

        return $"{retType} {funcName}({string.Join(", ", paramNames)})";
    }

    private static string FormatType(DwarfInfoIndex idx, uint typeOffset, int depth, HashSet<uint> visited)
    {
        if (depth > MaxTypeDepth) return "<unknown>";
        if (!visited.Add(typeOffset)) return "<cyclic>";

        if (!idx.Dies.TryGetValue(typeOffset, out var die)) return "<unknown>";

        return die.Tag switch
        {
            TagBaseType => die.Name ?? "<base>",
            TagUnspecifiedType => die.Name ?? "void",
            TagStructureType or TagClassType or TagEnumerationType =>
                die.Name ?? "<struct>",
            TagTypedef => die.Name ??
                (die.TypeOffset.HasValue ? FormatType(idx, die.TypeOffset.Value, depth + 1, visited) : "<typedef>"),
            TagPointerType =>
                (die.TypeOffset.HasValue ? FormatType(idx, die.TypeOffset.Value, depth + 1, visited) : "void") + "*",
            TagReferenceType =>
                (die.TypeOffset.HasValue ? FormatType(idx, die.TypeOffset.Value, depth + 1, visited) : "void") + "&",
            TagConstType =>
                "const " + (die.TypeOffset.HasValue ? FormatType(idx, die.TypeOffset.Value, depth + 1, visited) : "void"),
            TagVolatileType =>
                "volatile " + (die.TypeOffset.HasValue ? FormatType(idx, die.TypeOffset.Value, depth + 1, visited) : "void"),
            TagRestrictType =>
                (die.TypeOffset.HasValue ? FormatType(idx, die.TypeOffset.Value, depth + 1, visited) : "void") + " __restrict",
            TagArrayType =>
                (die.TypeOffset.HasValue ? FormatType(idx, die.TypeOffset.Value, depth + 1, visited) : "<type>") + "[]",
            TagTemplateTypeParam or TagTemplateValueParam => "<unknown>",
            _ => die.Name ?? "<unknown>",
        };
    }

    // -------------------------------------------------------------------------
    // Attribute reading helpers
    // -------------------------------------------------------------------------

    private static string? ReadAttrString(
        byte[] info, byte form, byte addrSize, ushort version,
        byte[] debugStr, ref int offset)
    {
        switch (form)
        {
            case FormString:
                return ReadNTS(info, ref offset);
            case FormStrp:
                if (offset + 4 > info.Length) return null;
                uint strOff = ReadU32(info, offset); offset += 4;
                return strOff < debugStr.Length ? ReadNTSBytes(debugStr, (int)strOff) : null;
            case FormLineStrp:
                // DWARF 5: offset into .debug_line_str; skip for now.
                offset += 4;
                return null;
            case FormStrx: case FormAddrx:
                ReadULEB128(info, ref offset);
                return null;
            case FormStrx1:
                if (offset < info.Length) offset += 1;
                return null;
            case FormStrx2:
                if (offset + 2 <= info.Length) offset += 2;
                return null;
            case FormStrx3:
                if (offset + 3 <= info.Length) offset += 3;
                return null;
            case FormStrx4:
                if (offset + 4 <= info.Length) offset += 4;
                return null;
            default:
                SkipAttr(info, form, addrSize, version, ref offset);
                return null;
        }
    }

    private static uint? ReadAttrRef(
        byte[] info, byte form, byte addrSize, ushort version,
        int cuBase, ref int offset)
    {
        switch (form)
        {
            case FormRef1:
                if (offset >= info.Length) return null;
                return (uint)(cuBase + info[offset++]);
            case FormRef2:
                if (offset + 2 > info.Length) return null;
                var v2 = ReadU16(info, offset); offset += 2;
                return (uint)(cuBase + v2);
            case FormRef4:
                if (offset + 4 > info.Length) return null;
                var v4 = ReadU32(info, offset); offset += 4;
                return (uint)(cuBase + v4);
            case FormRef8:
                if (offset + 8 > info.Length) return null;
                var v8 = ReadU64(info, offset); offset += 8;
                return v8 <= uint.MaxValue ? (uint)v8 : null;
            case FormRefUdata:
                var vu = ReadULEB128(info, ref offset);
                return (uint)(cuBase + (int)vu);
            case FormRefAddr:
                // Global section-relative reference — skip for simplicity.
                offset += addrSize;
                return null;
            default:
                SkipAttr(info, form, addrSize, version, ref offset);
                return null;
        }
    }

    private static ulong ReadAttrAddr(byte[] info, byte form, byte addrSize, ref int offset)
    {
        if (form == FormAddr)
        {
            if (addrSize == 8 && offset + 8 <= info.Length)
            {
                var v = ReadU64(info, offset); offset += 8; return v;
            }
            if (addrSize == 4 && offset + 4 <= info.Length)
            {
                var v = ReadU32(info, offset); offset += 4; return v;
            }
        }
        else if (form == FormAddrx || form == FormStrx)
        {
            ReadULEB128(info, ref offset);
        }
        else if (form == FormAddrx1 && offset < info.Length) { offset++; }
        else if (form == FormAddrx2 && offset + 2 <= info.Length) { offset += 2; }
        else if (form == FormAddrx3 && offset + 3 <= info.Length) { offset += 3; }
        else if (form == FormAddrx4 && offset + 4 <= info.Length) { offset += 4; }
        return 0;
    }

    private static (ulong Value, bool IsAbsolute) ReadAttrHighPc(byte[] info, byte form, byte addrSize, ref int offset)
    {
        // DWARF 4 spec §2.17: if DW_AT_high_pc is a constant (data form), it's an offset from low_pc.
        return form switch
        {
            FormAddr => (ReadAttrAddr(info, form, addrSize, ref offset), true),
            FormData1 when offset < info.Length => ((ulong)info[offset++], false),
            FormData2 when offset + 2 <= info.Length => (ReadU16(info, offset) is var v2 ? (offset += 2, (ulong)v2) : (offset, 0UL)) switch { var r => (r.Item2, false) },
            FormData4 when offset + 4 <= info.Length => (ReadU32(info, offset) is var v4 ? (offset += 4, (ulong)v4) : (offset, 0UL)) switch { var r => (r.Item2, false) },
            FormData8 when offset + 8 <= info.Length => (ReadU64(info, offset) is var v8 ? (offset += 8, v8) : (offset, 0UL)) switch { var r => (r.Item2, false) },
            _ => (SkipAttrAndReturn(info, form, addrSize, ref offset), false),
        };
    }

    private static ulong SkipAttrAndReturn(byte[] info, byte form, byte addrSize, ref int offset)
    {
        SkipAttr(info, form, addrSize, 4, ref offset);
        return 0;
    }

    private static void SkipAttr(byte[] info, byte form, byte addrSize, ushort version, ref int offset)
    {
        switch (form)
        {
            case FormAddr: offset += addrSize; break;
            case FormData1: case FormFlag: case FormRef1: offset++; break;
            case FormData2: case FormRef2: offset += 2; break;
            case FormData4: case FormRef4: offset += 4; break;
            case FormData8: case FormRef8: offset += 8; break;
            case FormFlagPresent: break; // implicit flag, 0 bytes
            case FormString: while (offset < info.Length && info[offset] != 0) offset++; if (offset < info.Length) offset++; break;
            case FormStrp: case FormRefAddr: case FormSecOffset: offset += 4; break;
            case FormLineStrp: offset += 4; break;
            case FormBlock1:
                if (offset < info.Length) { int len1 = info[offset++]; offset += len1; } break;
            case FormBlock2:
                if (offset + 2 <= info.Length) { int len2 = ReadU16(info, offset); offset += 2 + len2; } break;
            case FormBlock4:
                if (offset + 4 <= info.Length) { int len4 = (int)ReadU32(info, offset); offset += 4 + len4; } break;
            case FormBlock: case FormExprloc:
                var blen = (int)ReadULEB128(info, ref offset); offset += blen; break;
            case FormSdata: ReadSLEB128(info, ref offset); break;
            case FormUdata: case FormRefUdata: ReadULEB128(info, ref offset); break;
            case FormIndirect:
                var indForm = (byte)ReadULEB128(info, ref offset);
                SkipAttr(info, indForm, addrSize, version, ref offset);
                break;
            case FormStrx: case FormAddrx: ReadULEB128(info, ref offset); break;
            case FormStrx1: case FormAddrx1: if (offset < info.Length) offset++; break;
            case FormStrx2: case FormAddrx2: if (offset + 2 <= info.Length) offset += 2; break;
            case FormStrx3: case FormAddrx3: if (offset + 3 <= info.Length) offset += 3; break;
            case FormStrx4: case FormAddrx4: if (offset + 4 <= info.Length) offset += 4; break;
            default:
                // Unknown form — skip nothing but don't crash.
                break;
        }
    }

    // -------------------------------------------------------------------------
    // ELF SHF_COMPRESSED decompression (same as DwarfLineReader)
    // -------------------------------------------------------------------------

    private static byte[]? TryDecompressElf(byte[] data)
    {
        if (data.Length < 24) return null;
        if (ReadU32(data, 0) != 1) return null;
        if (ReadU32(data, 4) != 0) return null;
        var uncompressedSize = (int)ReadU64(data, 8);
        if (uncompressedSize <= 0 || uncompressedSize > 256 * 1024 * 1024) return null;
        try
        {
            using var compressed = new MemoryStream(data, 24, data.Length - 24);
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            var output = new byte[uncompressedSize];
            int total = 0;
            while (total < uncompressedSize)
            {
                int read = zlib.Read(output, total, uncompressedSize - total);
                if (read == 0) return null;
                total += read;
            }
            if (zlib.ReadByte() != -1) return null;
            return output;
        }
        catch { return null; }
    }

    // -------------------------------------------------------------------------
    // Primitive readers
    // -------------------------------------------------------------------------

    private static bool IsTypeTag(ushort tag) =>
        tag is TagBaseType or TagPointerType or TagConstType or TagVolatileType
            or TagRestrictType or TagReferenceType or TagTypedef or TagStructureType
            or TagClassType or TagArrayType or TagEnumerationType or TagUnspecifiedType
            or TagTemplateTypeParam or TagTemplateValueParam;

    private static uint ReadU32(byte[] d, int o) =>
        (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));

    private static ushort ReadU16(byte[] d, int o) =>
        (ushort)(d[o] | (d[o + 1] << 8));

    private static ulong ReadU64(byte[] d, int o)
    {
        uint lo = ReadU32(d, o);
        uint hi = ReadU32(d, o + 4);
        return lo | ((ulong)hi << 32);
    }

    private static ulong ReadULEB128(byte[] d, ref int o)
    {
        ulong r = 0; int s = 0;
        while (o < d.Length)
        {
            byte b = d[o++];
            r |= (ulong)(b & 0x7F) << s;
            if ((b & 0x80) == 0) break;
            s += 7;
            if (s >= 64) break;
        }
        return r;
    }

    private static long ReadSLEB128(byte[] d, ref int o)
    {
        long r = 0; int s = 0; byte b = 0;
        while (o < d.Length)
        {
            b = d[o++];
            r |= (long)(b & 0x7F) << s;
            s += 7;
            if ((b & 0x80) == 0) break;
            if (s >= 64) break;
        }
        if (s < 64 && (b & 0x40) != 0) r |= -(1L << s);
        return r;
    }

    private static string ReadNTS(byte[] d, ref int o)
    {
        int start = o;
        while (o < d.Length && d[o] != 0) o++;
        var s = System.Text.Encoding.UTF8.GetString(d, start, o - start);
        if (o < d.Length) o++;
        return s;
    }

    private static string? ReadNTSBytes(byte[] d, int o)
    {
        if (o < 0 || o >= d.Length) return null;
        int end = o;
        while (end < d.Length && d[end] != 0) end++;
        return System.Text.Encoding.UTF8.GetString(d, o, end - o);
    }
}
