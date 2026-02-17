using DotnetInspector.Metadata;

namespace DotnetInspector.Metadata.Tests;

public class OperatorNamesTests
{
    [Theory]
    [InlineData("op_Addition", "operator +")]
    [InlineData("op_Subtraction", "operator -")]
    [InlineData("op_Multiply", "operator *")]
    [InlineData("op_Division", "operator /")]
    [InlineData("op_Modulus", "operator %")]
    [InlineData("op_UnaryPlus", "operator +")]
    [InlineData("op_UnaryNegation", "operator -")]
    [InlineData("op_Increment", "operator ++")]
    [InlineData("op_Decrement", "operator --")]
    public void Arithmetic_operators(string input, string expected)
        => Assert.Equal(expected, OperatorNames.FormatDisplayName(input));

    [Theory]
    [InlineData("op_BitwiseAnd", "operator &")]
    [InlineData("op_BitwiseOr", "operator |")]
    [InlineData("op_ExclusiveOr", "operator ^")]
    [InlineData("op_OnesComplement", "operator ~")]
    [InlineData("op_LeftShift", "operator <<")]
    [InlineData("op_RightShift", "operator >>")]
    [InlineData("op_UnsignedRightShift", "operator >>>")]
    public void Bitwise_operators(string input, string expected)
        => Assert.Equal(expected, OperatorNames.FormatDisplayName(input));

    [Theory]
    [InlineData("op_Equality", "operator ==")]
    [InlineData("op_Inequality", "operator !=")]
    [InlineData("op_LessThan", "operator <")]
    [InlineData("op_GreaterThan", "operator >")]
    [InlineData("op_LessThanOrEqual", "operator <=")]
    [InlineData("op_GreaterThanOrEqual", "operator >=")]
    public void Comparison_operators(string input, string expected)
        => Assert.Equal(expected, OperatorNames.FormatDisplayName(input));

    [Theory]
    [InlineData("op_Implicit", "implicit operator")]
    [InlineData("op_Explicit", "explicit operator")]
    [InlineData("op_CheckedExplicit", "checked explicit operator")]
    public void Conversion_operators(string input, string expected)
        => Assert.Equal(expected, OperatorNames.FormatDisplayName(input));

    [Theory]
    [InlineData("op_True", "operator true")]
    [InlineData("op_False", "operator false")]
    [InlineData("op_LogicalNot", "operator !")]
    public void Logical_operators(string input, string expected)
        => Assert.Equal(expected, OperatorNames.FormatDisplayName(input));

    [Theory]
    [InlineData("op_CheckedAddition", "checked operator +")]
    [InlineData("op_CheckedSubtraction", "checked operator -")]
    [InlineData("op_CheckedMultiply", "checked operator *")]
    [InlineData("op_CheckedDivision", "checked operator /")]
    [InlineData("op_CheckedIncrement", "checked operator ++")]
    [InlineData("op_CheckedDecrement", "checked operator --")]
    [InlineData("op_CheckedUnaryNegation", "checked operator -")]
    public void Checked_operators(string input, string expected)
        => Assert.Equal(expected, OperatorNames.FormatDisplayName(input));

    [Theory]
    [InlineData("ToString")]
    [InlineData("GetHashCode")]
    [InlineData("Equals")]
    [InlineData("get_Length")]
    [InlineData("set_Value")]
    public void Non_operator_names_pass_through(string input)
        => Assert.Equal(input, OperatorNames.FormatDisplayName(input));

    [Fact]
    public void Unknown_op_prefix_passes_through()
        => Assert.Equal("op_SomeFutureOp", OperatorNames.FormatDisplayName("op_SomeFutureOp"));
}
