using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A label binds to the statement that follows it, so a labeled block that is the
// last thing in a container — with no statement after the label — strands the
// label as `IL_XXXX:` directly before a `}` (or end of method). That is invalid
// C# (CS1525/CS1002). The printer must bind such a trailing label to an empty
// statement (`IL_XXXX: ;`) so the output still parses. This shape is common: a
// forward branch whose fall-through target is the empty tail of a try block.
public class StrandedLabelRenderingTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");

    static IrFunction BuildWithTrailingLabel(int targetOffset)
    {
        // Block 0: if (flag) goto IL_<target>; return 1;
        var entry = new Block(0);
        entry.Add(new ConditionalBranch(new LoadArgument(0, "flag", Boolean), targetOffset));
        entry.Add(new Return(new Constant(1, Int32)));
        // The branch target is an empty block at the very end of the container,
        // so its label has no following statement.
        var tail = new Block(targetOffset);

        var container = new BlockContainer();
        container.Add(entry);
        container.Add(tail);

        var signature = new MethodSignature(Int32, [new Parameter("flag", Boolean)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("System", "Sample"), signature, [], container);
    }

    [Fact]
    public void TrailingEmptyLabeledBlock_BindsLabelToEmptyStatement()
    {
        var result = CSharpPrinter.Print(BuildWithTrailingLabel(0x10));

        var lines = (result.Output ?? "")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        int labelIndex = lines.IndexOf("IL_0010:");
        Assert.True(labelIndex >= 0, $"expected a stranded label in:\n{result.Output}");
        // The label must not be the last token, and what follows it is the empty
        // statement that keeps the label legal.
        Assert.True(labelIndex + 1 < lines.Count, "label is the last token — it is stranded");
        Assert.Equal(";", lines[labelIndex + 1]);
    }
}
