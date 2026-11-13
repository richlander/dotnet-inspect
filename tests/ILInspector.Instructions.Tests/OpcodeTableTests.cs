using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Instructions.Tests;

/// <summary>
/// The opcode tables a reader below the decoder relies on: validity is the
/// set ECMA-335 defines, which is the BCL's <see cref="ILOpCode"/> plus the
/// <c>no.</c> prefix the enum omits, and the operand classification names an
/// inline operand for exactly the opcodes that carry one.
/// </summary>
public class OpcodeTableTests
{
    const int NoPrefix = 0xFE19;

    [Fact]
    public void IsValid_admits_every_defined_opcode_and_the_no_prefix_only()
    {
        for (int value = 0; value <= 0xFF; value++)
        {
            if (value == 0xFE)
                continue; // the two-byte escape is not an opcode
            Check((ILOpCode)value);
        }
        for (int value = 0xFE00; value <= 0xFEFF; value++)
            Check((ILOpCode)value);

        static void Check(ILOpCode opcode)
        {
            bool defined = Enum.IsDefined(opcode) || (int)opcode == NoPrefix;
            Assert.Equal(defined, opcode.IsValid());
        }
    }

    [Fact]
    public void GetOperandKind_matches_the_instruction_size_table()
    {
        for (int value = 0; value <= 0xFF; value++)
        {
            if (value == 0xFE)
                continue;
            Check((ILOpCode)value);
        }
        for (int value = 0xFE00; value <= 0xFEFF; value++)
            Check((ILOpCode)value);

        static void Check(ILOpCode opcode)
        {
            if (!opcode.IsValid())
            {
                Assert.Equal(OperandKind.None, opcode.GetOperandKind());
                return;
            }
            int opcodeSize = (int)opcode <= 0xFF ? 1 : 2;
            int operandSize = opcode.GetOperandKind() switch
            {
                OperandKind.None => 0,
                OperandKind.ShortInlineVar or OperandKind.ShortInlineI or OperandKind.ShortInlineBrTarget => 1,
                OperandKind.InlineVar => 2,
                OperandKind.InlineI or OperandKind.ShortInlineR or OperandKind.InlineBrTarget
                    or OperandKind.InlineString or OperandKind.InlineMethod or OperandKind.InlineField
                    or OperandKind.InlineType or OperandKind.InlineSig or OperandKind.InlineTok => 4,
                OperandKind.InlineI8 or OperandKind.InlineR => 8,
                OperandKind.InlineSwitch => -1,
                _ => throw new InvalidOperationException(),
            };
            if (operandSize < 0)
            {
                Assert.Equal(-1, opcode.GetInstructionSize());
                return;
            }
            Assert.Equal(opcodeSize + operandSize, opcode.GetInstructionSize());
        }
    }
}
