
namespace DotnetInspector.Tests;

/// <summary>
/// A C# operator declaration is not decided by the selected member alone: an
/// <c>operator ==</c> is only spellable because its declaring type also declares
/// <c>operator !=</c>. Every path that narrows a type's rendered member list to
/// a selection must therefore keep the declaring inventory, or the selected
/// operator degrades to its raw <c>op_*</c> method spelling.
/// </summary>
public partial class CommandExecutionTests
{
    public sealed class IncrementSelection
    {
        public void operator ++() { }

        public static IncrementSelection operator ++(
            IncrementSelection value) => value;
    }

    public readonly struct OperatorSelectionMoney
    {
        public OperatorSelectionMoney(decimal amount) => Amount = amount;

        public decimal Amount { get; }

        public static bool operator ==(OperatorSelectionMoney left, OperatorSelectionMoney right)
            => left.Amount == right.Amount;

        public static bool operator !=(OperatorSelectionMoney left, OperatorSelectionMoney right)
            => left.Amount != right.Amount;

        public override bool Equals(object? obj)
            => obj is OperatorSelectionMoney other && this == other;

        public override int GetHashCode() => Amount.GetHashCode();
    }

    [Theory]
    // The digest/index narrowing path and the generic-arity narrowing path must
    // agree: an arity selector narrows the member list too, and lost sibling
    // context there rendered `public static bool op_Equality(...)`.
    [InlineData("op_Equality")]
    [InlineData("op_Equality<>")]
    public async Task Member_SelectedOperator_KeepsSiblingContextThroughEveryNarrowingPath(
        string selector)
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            typeof(OperatorSelectionMoney).FullName!,
            "--library",
            TestAssemblyPath,
            "-m",
            selector,
            "--markdown",
            "-v:d");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("operator ==(", output);
        Assert.DoesNotContain("bool op_Equality(", output);
    }

    [Fact]
    public async Task Member_IncrementToken_SelectsStaticAndInstanceShapes()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            $"{typeof(IncrementSelection).FullName}.++",
            "--library",
            TestAssemblyPath,
            "--markdown",
            "-v:d");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(
            2,
            output.Split("operator ++(", StringSplitOptions.None)
                .Length - 1);
        Assert.Contains(
            "public void operator ++()",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static ",
            output,
            StringComparison.Ordinal);
    }
}
