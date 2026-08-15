namespace CSharpText.Tests;

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
    [InlineData("op_AdditionAssignment", "operator +=")]
    [InlineData("op_CheckedAdditionAssignment", "checked operator +=")]
    [InlineData("op_ModulusAssignment", "operator %=")]
    [InlineData("op_CheckedMultiplicationAssignment", "checked operator *=")]
    [InlineData("op_IncrementAssignment", "operator ++")]
    [InlineData("op_CheckedIncrementAssignment", "checked operator ++")]
    public void Assignment_operators(string input, string expected)
        => Assert.Equal(expected, OperatorNames.FormatDisplayName(input));

    [Theory]
    [InlineData("ToString")]
    [InlineData("GetHashCode")]
    [InlineData("Equals")]
    [InlineData("get_Length")]
    [InlineData("set_Value")]
    [InlineData("op_CheckedModulusAssignment")]
    [InlineData("op_CheckedBitwiseAndAssignment")]
    [InlineData("op_CheckedUnsignedRightShiftAssignment")]
    public void Non_operator_names_pass_through(string input)
        => Assert.Equal(input, OperatorNames.FormatDisplayName(input));

    [Fact]
    public void Unknown_op_prefix_passes_through()
        => Assert.Equal("op_SomeFutureOp", OperatorNames.FormatDisplayName("op_SomeFutureOp"));

    [Theory]
    [InlineData("op_AdditionAssignment")]
    [InlineData("op_CheckedAdditionAssignment")]
    [InlineData("op_CheckedMultiplicationAssignment")]
    [InlineData("op_IncrementAssignment")]
    [InlineData("op_CheckedIncrementAssignment")]
    [InlineData("op_Exponent")]
    [InlineData("op_IntegerDivision")]
    [InlineData("op_Concatenate")]
    [InlineData("op_Like")]
    public void Recognizes_language_operator_names(string input)
        => Assert.True(OperatorNames.IsOperatorMethodName(input));

    [Theory]
    [InlineData("op_Custom")]
    [InlineData("op_SomeFutureOp")]
    [InlineData("op_CheckedModulusAssignment")]
    [InlineData("op_CheckedBitwiseAndAssignment")]
    [InlineData("op_CheckedUnsignedRightShiftAssignment")]
    public void Rejects_unknown_op_prefix_names(string input)
        => Assert.False(OperatorNames.IsOperatorMethodName(input));

    [Theory]
    [InlineData("op_AdditionAssignment", false, true, "void", 1, false, true)]
    [InlineData("op_CheckedMultiplicationAssignment", false, true, "void", 1, false, true)]
    [InlineData("op_IncrementAssignment", false, true, "void", 0, false, true)]
    [InlineData("op_AdditionAssignment", false, true, "void", 1, true, false)]
    [InlineData("op_AdditionAssignment", true, true, "void", 1, false, false)]
    [InlineData("op_AdditionAssignment", false, false, "void", 1, false, false)]
    [InlineData("op_AdditionAssignment", false, true, "int", 1, false, false)]
    [InlineData("op_AdditionAssignment", false, true, "void", 2, false, false)]
    [InlineData("op_IncrementAssignment", false, true, "void", 1, false, false)]
    [InlineData("op_CheckedModulusAssignment", false, true, "void", 1, false, false)]
    public void CSharp_instance_assignment_operator_shape(
        string name,
        bool isStatic,
        bool isPublic,
        string returnType,
        int parameterCount,
        bool hasRefOrOutParameter,
        bool expected)
        => Assert.Equal(
            expected,
            OperatorNames.IsCSharpInstanceAssignmentOperator(
                name,
                isStatic,
                isPublic,
                returnType,
                parameterCount,
                hasRefOrOutParameter));

    [Theory]
    [InlineData("op_Implicit")]
    [InlineData("op_Explicit")]
    [InlineData("op_CheckedImplicit")]
    [InlineData("op_CheckedExplicit")]
    public void Conversion_operator_names_share_one_vocabulary(string name)
        => Assert.True(OperatorNames.IsConversionOperatorMethodName(name));

    [Theory]
    [InlineData("op_CheckedAddition", "op_Addition")]
    [InlineData("op_CheckedSubtraction", "op_Subtraction")]
    [InlineData("op_CheckedMultiply", "op_Multiply")]
    [InlineData("op_CheckedDivision", "op_Division")]
    [InlineData("op_CheckedIncrement", "op_Increment")]
    [InlineData("op_CheckedDecrement", "op_Decrement")]
    [InlineData("op_CheckedUnaryNegation", "op_UnaryNegation")]
    [InlineData("op_CheckedExplicit", "op_Explicit")]
    [InlineData("op_CheckedImplicit", "op_Implicit")]
    [InlineData("op_CheckedAdditionAssignment", "op_AdditionAssignment")]
    [InlineData("op_CheckedMultiplicationAssignment", "op_MultiplicationAssignment")]
    [InlineData("op_CheckedIncrementAssignment", "op_IncrementAssignment")]
    public void UncheckedOperator_maps_checked_operator_to_its_sibling(string input, string expected)
        => Assert.Equal(expected, OperatorNames.UncheckedOperator(input));

    [Theory]
    [InlineData("op_Addition")]
    [InlineData("op_Explicit")]
    [InlineData("op_CheckedModulus")]
    [InlineData("op_CheckedModulusAssignment")]
    [InlineData("op_CheckedBitwiseAndAssignment")]
    [InlineData("op_CheckedUnsignedRightShiftAssignment")]
    [InlineData("op_Checked")]
    [InlineData("get_Length")]
    [InlineData("ToString")]
    public void UncheckedOperator_returns_null_for_non_checked_or_unpaired_names(string input)
        => Assert.Null(OperatorNames.UncheckedOperator(input));

    [Theory]
    [InlineData("op_Addition", "op_CheckedAddition")]
    [InlineData("op_Explicit", "op_CheckedExplicit")]
    [InlineData("op_AdditionAssignment", "op_CheckedAdditionAssignment")]
    [InlineData("op_Implicit", null)]
    [InlineData("op_Modulus", null)]
    [InlineData("op_Equality", null)]
    [InlineData("get_Length", null)]
    public void CheckedOperator_maps_only_operators_with_checked_siblings(
        string input,
        string? expected)
        => Assert.Equal(expected, OperatorNames.CheckedOperator(input));

    [Theory]
    [InlineData("+", 1, false, "op_UnaryPlus")]
    [InlineData("+", 2, true, "op_CheckedAddition")]
    [InlineData("+=", 1, false, "op_AdditionAssignment")]
    [InlineData("+=", 1, true, "op_CheckedAdditionAssignment")]
    [InlineData("*=", 1, false, "op_MultiplicationAssignment")]
    [InlineData("*=", 1, true, "op_CheckedMultiplicationAssignment")]
    [InlineData("++", 1, false, "op_Increment")]
    [InlineData("++", 0, false, "op_IncrementAssignment")]
    [InlineData("++", 0, true, "op_CheckedIncrementAssignment")]
    public void MetadataNameFromSourceToken_distinguishes_operator_shapes(
        string token,
        int parameterCount,
        bool isChecked,
        string expected)
        => Assert.Equal(
            expected,
            OperatorNames.MetadataNameFromSourceToken(token, parameterCount, isChecked));

    [Theory]
    [InlineData("%=", 1, true)]
    [InlineData("==", 2, true)]
    [InlineData("future", 2, false)]
    public void MetadataNameFromSourceToken_rejects_unknown_or_invalid_checked_shapes(
        string token,
        int parameterCount,
        bool isChecked)
        => Assert.Null(
            OperatorNames.MetadataNameFromSourceToken(token, parameterCount, isChecked));

    [Theory]
    [InlineData("op_Equality", "op_Inequality")]
    [InlineData("op_Inequality", "op_Equality")]
    [InlineData("op_CheckedAddition", "op_Addition")]
    [InlineData("op_CheckedAdditionAssignment", "op_AdditionAssignment")]
    [InlineData("op_Addition", null)]
    [InlineData("op_Custom", null)]
    public void RequiredOperatorSibling_maps_only_language_required_pairs(
        string input,
        string? expected)
        => Assert.Equal(expected, OperatorNames.RequiredOperatorSibling(input));
}
