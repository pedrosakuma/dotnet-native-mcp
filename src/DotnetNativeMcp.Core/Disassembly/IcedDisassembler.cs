using System.Globalization;
using System.Text;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using Iced.Intel;

namespace DotnetNativeMcp.Core.Disassembly;

/// <summary>
/// Wraps <c>Iced.Intel</c> to disassemble x86/x64 code from a <see cref="NativeImage"/>.
/// ARM64 is not yet supported — returns <see cref="ErrorKinds.DisassemblyUnsupported"/>.
/// </summary>
public static class IcedDisassembler
{
    /// <summary>Default number of instructions to decode per call.</summary>
    public const int DefaultMaxInstructions = 64;

    /// <summary>Hard cap on instructions per call.</summary>
    public const int MaxInstructionsCap = 2048;

    /// <summary>
    /// Disassembles up to <paramref name="maxInstructions"/> instructions starting at <paramref name="rva"/>.
    /// </summary>
    /// <param name="image">The loaded native image.</param>
    /// <param name="rva">Start RVA (relative to image base).</param>
    /// <param name="maxInstructions">Maximum instructions to decode. 0 → <see cref="DefaultMaxInstructions"/>.</param>
    /// <returns>A <see cref="NativeResult{T}"/> wrapping the instruction list, or an error.</returns>
    public static NativeResult<IReadOnlyList<InstructionView>> Disassemble(
        NativeImage image,
        ulong rva,
        int maxInstructions = DefaultMaxInstructions)
    {
        if (maxInstructions <= 0)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.InvalidArgument,
                $"maxInstructions must be > 0; got {maxInstructions}.");

        if (maxInstructions > MaxInstructionsCap)
            maxInstructions = MaxInstructionsCap;

        int bitness;
        switch (image.Architecture)
        {
            case Architecture.X64:
                bitness = 64;
                break;
            case Architecture.X86:
                bitness = 32;
                break;
            default:
                return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                    ErrorKinds.DisassemblyUnsupported,
                    $"Disassembly for {image.Architecture} is not supported in V0. Only x86/x64 is implemented.");
        }

        var section = image.FindSection(rva);
        if (section is null)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.AddressOutOfRange,
                $"RVA 0x{rva:x} is not inside any known section.");

        var fileOffset = image.RvaToFileOffset(rva);
        if (fileOffset is null)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.AddressOutOfRange,
                $"RVA 0x{rva:x} could not be mapped to a file offset.");

        var rawBytes = image.RawBytes;
        var startOffset = fileOffset.Value;
        var bytesAvailable = rawBytes.Length - startOffset;
        if (bytesAvailable <= 0)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.AddressOutOfRange,
                $"File offset 0x{startOffset:x} is beyond the end of the binary.");

        var codeBytes = rawBytes.Span[startOffset..];
        // ip = absolute virtual address of the first byte
        var ip = image.ImageBase + rva;
        var reader = new ByteArrayCodeReader(codeBytes.ToArray());
        var decoder = Iced.Intel.Decoder.Create(bitness, reader, ip);

        var formatter = new IntelFormatter();
        formatter.Options.UppercaseHex = false;
        formatter.Options.FirstOperandCharIndex = 0;
        formatter.Options.SpaceAfterOperandSeparator = true;

        var results = new List<InstructionView>(maxInstructions);
        var instrStart = startOffset;

        for (var count = 0; count < maxInstructions && reader.CanReadByte; count++)
        {
            var prevPos = reader.Position;
            decoder.Decode(out var instr);
            if (instr.IsInvalid) break;

            var instrLen = reader.Position - prevPos;
            var instrBytesSpan = codeBytes[prevPos..(prevPos + instrLen)];
            var bytesHex = BytesToHex(instrBytesSpan);
            var addrHex = instr.IP.ToString("x16", CultureInfo.InvariantCulture);
            var mnemonic = instr.Mnemonic.ToString().ToLowerInvariant();

            var output = new StringOutput();
            formatter.FormatAllOperands(instr, output);
            var operands = output.ToStringAndReset();

            CrossRefHint? crossRef = null;
            if (IsCallOrJmp(instr))
            {
                ulong targetIp = 0;
                if (instr.Op0Kind is OpKind.NearBranch16 or OpKind.NearBranch32 or OpKind.NearBranch64)
                    targetIp = instr.NearBranch64;

                if (targetIp != 0)
                {
                    crossRef = BuildCrossRef(image, targetIp);
                }
            }

            results.Add(new InstructionView(addrHex, bytesHex, mnemonic, operands, crossRef));
        }

        return NativeResult.Ok<IReadOnlyList<InstructionView>>(
            $"Disassembled {results.Count} instruction(s) at RVA 0x{rva:x} in {image.Handle.Value}.",
            results);
    }

    private static bool IsCallOrJmp(in Instruction instr)
    {
        return instr.FlowControl is FlowControl.Call
            or FlowControl.UnconditionalBranch
            or FlowControl.ConditionalBranch;
    }

    private static CrossRefHint? BuildCrossRef(NativeImage image, ulong targetIp)
    {
        var targetAddrHex = targetIp.ToString("x16", CultureInfo.InvariantCulture);
        var targetRva = SymbolResolution.VaToRva(targetIp, image.ImageBase);
        var sym = SymbolResolution.FindByRva(image.Symbols, targetRva);
        return new CrossRefHint(targetAddrHex, sym?.Name, sym?.DemangledName);
    }

    private static string BytesToHex(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>Simple string output buffer for Iced formatter.</summary>
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

    /// <summary>Simple wrapper that reads from a byte array, tracking position.</summary>
    private sealed class ByteArrayCodeReader : CodeReader
    {
        private readonly byte[] _data;

        public ByteArrayCodeReader(byte[] data) => _data = data;

        public int Position { get; private set; }

        public bool CanReadByte => Position < _data.Length;

        public override int ReadByte()
        {
            if (Position >= _data.Length) return -1;
            return _data[Position++];
        }
    }
}
