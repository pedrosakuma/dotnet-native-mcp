using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using DotnetNativeMcp.Core.Disassembly;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using Iced.Intel;

namespace DotnetNativeMcp.Core.Xref;

/// <summary>
/// Scans all executable sections of a <see cref="NativeImage"/> and builds a
/// complete xref index: target virtual address → list of <see cref="CallSite"/>s.
/// x86/x64 and ARM64 are supported; other architectures produce an empty index.
/// </summary>
public static class NativeCallGraphBuilder
{
    /// <summary>
    /// Section names treated as executable code. Extended with common variants to
    /// cover both ELF and PE NativeAOT binaries.
    /// </summary>
    private static readonly HashSet<string> CodeSectionNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".text",
            ".plt",
            ".plt.got",
            ".plt.sec",
            ".init",
            ".fini",
            "CODE",
            "__TEXT,__text",
            "__TEXT_EXEC,__text",
            "__TEXT,__stubs",
            "__TEXT,__stub_helper",
        };

    /// <summary>
    /// Builds a full xref index for the given image.
    /// </summary>
    /// <param name="image">The loaded native image.</param>
    /// <returns>
    /// A dictionary that maps each call-target virtual address to the list of
    /// <see cref="CallSite"/>s that branch to it. Never <c>null</c>; may be empty
    /// for unsupported architectures or images with no branch instructions.
    /// </returns>
    public static IReadOnlyDictionary<ulong, IReadOnlyList<CallSite>> Build(NativeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Architecture is Architecture.Arm64)
        {
            var arm64Index = new Dictionary<ulong, List<CallSite>>();
            foreach (var section in image.Sections)
            {
                if (!IsCodeSection(section))
                    continue;
                Arm64Disassembler.ScanSection(image, section, arm64Index);
            }
            var arm64Result = new Dictionary<ulong, IReadOnlyList<CallSite>>(arm64Index.Count);
            foreach (var (key, list) in arm64Index)
                arm64Result[key] = list;
            return arm64Result;
        }

        if (image.Architecture is not (Architecture.X64 or Architecture.X86))
            return new Dictionary<ulong, IReadOnlyList<CallSite>>();

        var bitness = image.Architecture == Architecture.X64 ? 64 : 32;

        // Accumulate results in a dict: target VA → list of call sites.
        var index = new Dictionary<ulong, List<CallSite>>();

        foreach (var section in image.Sections)
        {
            if (!IsCodeSection(section))
                continue;

            ScanSection(image, section, bitness, index);
        }

        // Convert to read-only view.
        var result = new Dictionary<ulong, IReadOnlyList<CallSite>>(index.Count);
        foreach (var (key, list) in index)
            result[key] = list;

        return result;
    }

    private static bool IsCodeSection(NativeSection section)
    {
        return CodeSectionNames.Contains(section.Name) ||
               section.Name.StartsWith(".text", StringComparison.OrdinalIgnoreCase) ||
               section.Name.EndsWith(",__text", StringComparison.OrdinalIgnoreCase) ||
               section.Name.EndsWith(",__stubs", StringComparison.OrdinalIgnoreCase) ||
               section.Name.EndsWith(",__stub_helper", StringComparison.OrdinalIgnoreCase);
    }

    private static void ScanSection(
        NativeImage image,
        NativeSection section,
        int bitness,
        Dictionary<ulong, List<CallSite>> index)
    {
        var fileStart = (int)section.FileOffset;
        if (fileStart < 0 || fileStart >= image.RawBytes.Length)
            return;

        var fileSize = (int)Math.Min(section.FileSize, (ulong)(image.RawBytes.Length - fileStart));
        if (fileSize <= 0)
            return;

        var sectionBytes = image.RawBytes.Span[fileStart..(fileStart + fileSize)].ToArray();
        var ip = image.ImageBase + section.VirtualAddress;

        var reader = new ByteArrayCodeReader(sectionBytes);
        var decoder = Iced.Intel.Decoder.Create(bitness, reader, ip);

        var formatter = new IntelFormatter();
        formatter.Options.UppercaseHex = false;
        formatter.Options.FirstOperandCharIndex = 0;
        formatter.Options.SpaceAfterOperandSeparator = true;

        while (reader.CanReadByte)
        {
            var prevPos = reader.Position;
            decoder.Decode(out var instr);
            if (instr.IsInvalid)
            {
                // Skip one byte and continue to maximise coverage.
                if (reader.Position == prevPos)
                    reader.Advance();
                continue;
            }

            if (!IsBranch(instr))
                continue;

            ulong targetIp = 0;
            if (instr.Op0Kind is OpKind.NearBranch16 or OpKind.NearBranch32 or OpKind.NearBranch64)
                targetIp = instr.NearBranch64;

            if (targetIp == 0)
                continue;

            var instrLen = reader.Position - prevPos;
            var instrBytesHex = BytesToHex(sectionBytes.AsSpan(prevPos, instrLen));

            var sourceIp = instr.IP;
            var sourceAddressHex = sourceIp.ToString("x16", CultureInfo.InvariantCulture);

            // Resolve the calling symbol.
            var sourceRva = sourceIp >= image.ImageBase ? sourceIp - image.ImageBase : sourceIp;
            var callerSym = SymbolResolution.FindByRva(image.Symbols, sourceRva);

            var output = new StringOutput();
            formatter.FormatAllOperands(instr, output);
            var operands = output.ToStringAndReset();

            var site = new CallSite(
                sourceAddressHex,
                callerSym?.Name,
                callerSym?.DemangledName,
                instr.Mnemonic.ToString().ToLowerInvariant(),
                operands,
                instrBytesHex);

            if (!index.TryGetValue(targetIp, out var list))
            {
                list = [];
                index[targetIp] = list;
            }

            list.Add(site);
        }
    }

    private static bool IsBranch(in Instruction instr) =>
        instr.FlowControl is FlowControl.Call
            or FlowControl.UnconditionalBranch
            or FlowControl.ConditionalBranch;

    private static string BytesToHex(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private sealed class StringOutput : FormatterOutput
    {
        private readonly StringBuilder _sb = new();

        public override void Write(string text, FormatterTextKind kind) => _sb.Append(text);

        public string ToStringAndReset()
        {
            var result = _sb.ToString();
            _sb.Clear();
            return result;
        }
    }

    private sealed class ByteArrayCodeReader : CodeReader
    {
        private readonly byte[] _data;

        public ByteArrayCodeReader(byte[] data) => _data = data;

        public int Position { get; private set; }

        public bool CanReadByte => Position < _data.Length;

        public void Advance() => Position++;

        public override int ReadByte()
        {
            if (Position >= _data.Length) return -1;
            return _data[Position++];
        }
    }
}
