using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using AsmArm64;
using DotnetNativeMcp.Core.Errors;
using DotnetNativeMcp.Core.Imaging;
using DotnetNativeMcp.Core.Symbols;
using DotnetNativeMcp.Core.Xref;

namespace DotnetNativeMcp.Core.Disassembly;

/// <summary>
/// Disassembles ARM64 (AArch64) code from a <see cref="NativeImage"/> using AsmArm64.
/// </summary>
public static class Arm64Disassembler
{
    /// <summary>
    /// Disassembles up to <paramref name="maxInstructions"/> instructions starting at <paramref name="rva"/>.
    /// </summary>
    public static NativeResult<IReadOnlyList<InstructionView>> Disassemble(
        NativeImage image,
        ulong rva,
        int maxInstructions)
    {
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
        if (bytesAvailable < 4)
            return NativeResult.Fail<IReadOnlyList<InstructionView>>(
                ErrorKinds.AddressOutOfRange,
                $"File offset 0x{startOffset:x} is beyond the end of the binary.");

        var codeBytes = rawBytes.Span[startOffset..];
        var ip = image.ImageBase + rva;

        var results = new List<InstructionView>(maxInstructions);
        var offset = 0;

        while (results.Count < maxInstructions && offset + 4 <= codeBytes.Length)
        {
            var instrBytes = codeBytes.Slice(offset, 4);
            var rawValue = MemoryMarshal.Read<uint>(instrBytes);
            var instr = Arm64Instruction.Decode(rawValue);

            var instrIp = ip + (ulong)offset;
            var addrHex = instrIp.ToString("x16", CultureInfo.InvariantCulture);
            var bytesHex = BytesToHex(instrBytes);

            if (instr.Id == Arm64InstructionId.Invalid)
            {
                results.Add(new InstructionView(addrHex, bytesHex, "unknown", string.Empty, null));
                offset += 4;
                continue;
            }

            var mnemonic = instr.Mnemonic.ToText(false).ToLowerInvariant();
            var operands = FormatOperands(instr, instrIp);

            CrossRefHint? crossRef = null;
            if (IsBranchOrCall(instr))
            {
                var targetIp = ExtractLabelTarget(instr, instrIp);
                if (targetIp.HasValue)
                    crossRef = BuildCrossRef(image, targetIp.Value);
            }

            results.Add(new InstructionView(addrHex, bytesHex, mnemonic, operands, crossRef));
            offset += 4;

            if (IsReturn(instr))
                break;
        }

        return NativeResult.Ok<IReadOnlyList<InstructionView>>(
            $"Disassembled {results.Count} instruction(s) at RVA 0x{rva:x} in {image.Handle.Value}.",
            results);
    }

    /// <summary>
    /// Scans a span of ARM64 code and populates the xref index.
    /// </summary>
    public static void ScanSection(
        NativeImage image,
        NativeSection section,
        Dictionary<ulong, List<CallSite>> index)
    {
        var fileStart = (int)section.FileOffset;
        var fileSize = (int)Math.Min(section.FileSize, (ulong)(image.RawBytes.Length - fileStart));
        if (fileSize < 4)
            return;

        var sectionBytes = image.RawBytes.Span[fileStart..(fileStart + fileSize)];
        var ip = image.ImageBase + section.VirtualAddress;

        var offset = 0;
        while (offset + 4 <= fileSize)
        {
            var instrBytes = sectionBytes.Slice(offset, 4);
            var rawValue = MemoryMarshal.Read<uint>(instrBytes);
            var instr = Arm64Instruction.Decode(rawValue);
            var instrIp = ip + (ulong)offset;

            if (instr.Id != Arm64InstructionId.Invalid && IsBranchOrCall(instr))
            {
                var targetIp = ExtractLabelTarget(instr, instrIp);
                if (targetIp.HasValue && targetIp.Value != 0)
                {
                    var instrBytesHex = BytesToHex(instrBytes);
                    var sourceAddressHex = instrIp.ToString("x16", CultureInfo.InvariantCulture);
                    var sourceRva = instrIp >= image.ImageBase ? instrIp - image.ImageBase : instrIp;
                    var callerSym = SymbolResolution.FindByRva(image.Symbols, sourceRva);
                    var mnemonic = instr.Mnemonic.ToText(false).ToLowerInvariant();
                    var operands = FormatOperands(instr, instrIp);

                    var site = new CallSite(
                        sourceAddressHex,
                        callerSym?.Name,
                        callerSym?.DemangledName,
                        mnemonic,
                        operands,
                        instrBytesHex);

                    if (!index.TryGetValue(targetIp.Value, out var list))
                    {
                        list = [];
                        index[targetIp.Value] = list;
                    }

                    list.Add(site);
                }
            }

            offset += 4;
        }
    }

    /// <summary>Returns true for BL, BLR, B, BR, B.cond, CBZ, CBNZ, TBZ, TBNZ.</summary>
    public static bool IsBranchOrCall(Arm64Instruction instr)
    {
        return instr.Mnemonic switch
        {
            Arm64Mnemonic.BL => true,
            Arm64Mnemonic.BLR => true,
            Arm64Mnemonic.B => true,
            Arm64Mnemonic.BR => true,
            Arm64Mnemonic.CBZ => true,
            Arm64Mnemonic.CBNZ => true,
            Arm64Mnemonic.TBZ => true,
            Arm64Mnemonic.TBNZ => true,
            Arm64Mnemonic.BC => true,
            _ => false,
        };
    }

    private static bool IsReturn(Arm64Instruction instr) =>
        instr.Mnemonic is Arm64Mnemonic.RET;

    /// <summary>Extracts the PC-relative target address from a label operand, if any.</summary>
    public static ulong? ExtractLabelTarget(Arm64Instruction instr, ulong instrIp)
    {
        for (var i = 0; i < instr.OperandCount; i++)
        {
            var op = instr.GetOperand(i);
            if (op.Kind != Arm64OperandKind.Label)
                continue;

            var labelOp = (Arm64LabelOperand)op;
            var target = (long)instrIp + labelOp.Offset;
            return (ulong)target;
        }

        return null;
    }

    private static string FormatOperands(Arm64Instruction instr, ulong instrIp)
    {
        Span<char> buf = stackalloc char[256];
        if (instr.TryFormat(buf, out var written, default, null))
        {
            var text = buf[..written];
            var spaceIdx = text.IndexOf(' ');
            if (spaceIdx >= 0)
                return text[(spaceIdx + 1)..].ToString();
        }

        return string.Empty;
    }

    private static CrossRefHint BuildCrossRef(NativeImage image, ulong targetIp)
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
}
