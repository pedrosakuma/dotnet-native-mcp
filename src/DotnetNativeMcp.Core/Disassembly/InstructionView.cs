using DotnetNativeMcp.Core.Symbols;

namespace DotnetNativeMcp.Core.Disassembly;

/// <summary>One decoded native instruction.</summary>
/// <param name="AddressHex">Absolute address in lowercase hex (16 chars for x64).</param>
/// <param name="Bytes">Raw bytes in lowercase hex (e.g. <c>90</c> for NOP).</param>
/// <param name="Mnemonic">Lowercase mnemonic (e.g. <c>nop</c>, <c>ret</c>).</param>
/// <param name="Operands">Operand text (e.g. <c>rax, [rbx+8]</c>); empty for no-operand instructions.</param>
/// <param name="CrossRef">Populated for CALL/JMP instructions that target a resolvable address; <c>null</c> otherwise.</param>
/// <param name="Source">Source file+line from DWARF/PDB debug info, when resolveSource=true; <c>null</c> otherwise.</param>
public sealed record InstructionView(
    string AddressHex,
    string Bytes,
    string Mnemonic,
    string Operands,
    CrossRefHint? CrossRef,
    SourceLocation? Source = null);
