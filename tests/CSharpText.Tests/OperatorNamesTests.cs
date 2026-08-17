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
    [InlineData("op_CheckedImplicit")]
    public void Non_operator_names_pass_through(string input)
        => Assert.Equal(input, OperatorNames.FormatDisplayName(input));

    [Fact]
    public void Unknown_op_prefix_passes_through()
        => Assert.Equal("op_SomeFutureOp", OperatorNames.FormatDisplayName("op_SomeFutureOp"));

    [Theory]
    // C# operator names.
    [InlineData("op_Addition")]
    [InlineData("op_Implicit")]
    [InlineData("op_Explicit")]
    [InlineData("op_CheckedExplicit")]
    [InlineData("op_AdditionAssignment")]
    [InlineData("op_CheckedAdditionAssignment")]
    [InlineData("op_CheckedMultiplicationAssignment")]
    [InlineData("op_IncrementAssignment")]
    [InlineData("op_CheckedIncrementAssignment")]
    // ECMA-335 I.10.3 names C# has no declaration syntax for.
    [InlineData("op_AddressOf")]
    [InlineData("op_PointerDereference")]
    [InlineData("op_LogicalAnd")]
    [InlineData("op_LogicalOr")]
    [InlineData("op_Assign")]
    [InlineData("op_SignedRightShift")]
    [InlineData("op_Comma")]
    [InlineData("op_MemberSelection")]
    [InlineData("op_PointerToMemberSelection")]
    [InlineData("op_UnsignedRightShiftAssignment")]
    [InlineData("op_RightShiftAssignment")]
    // Recognized by the operator convention, but not a C# declaration.
    [InlineData("op_CheckedImplicit")]
    // Other-language arithmetic names sharing the convention.
    [InlineData("op_Exponent")]
    [InlineData("op_IntegerDivision")]
    [InlineData("op_Concatenate")]
    [InlineData("op_Like")]
    public void Recognizes_metadata_operator_names(string input)
        => Assert.True(OperatorNames.IsMetadataOperatorMethodName(input));

    [Theory]
    [InlineData("op_Custom")]
    [InlineData("op_SomeFutureOp")]
    [InlineData("op_")]
    [InlineData("op_CheckedModulusAssignment")]
    [InlineData("op_CheckedBitwiseAndAssignment")]
    [InlineData("op_CheckedUnsignedRightShiftAssignment")]
    [InlineData("op_CheckedLogicalNot")]
    [InlineData("op_CheckedEquality")]
    [InlineData("Addition")]
    [InlineData("ToString")]
    public void Rejects_non_operator_names(string input)
    {
        Assert.False(OperatorNames.IsMetadataOperatorMethodName(input));
        Assert.False(OperatorNames.IsCSharpOperatorMethodName(input));
    }

    [Theory]
    [InlineData("op_Addition", true)]
    [InlineData("op_UnsignedRightShift", true)]
    [InlineData("op_Implicit", true)]
    [InlineData("op_CheckedExplicit", true)]
    [InlineData("op_CheckedAddition", true)]
    [InlineData("op_AdditionAssignment", true)]
    [InlineData("op_CheckedIncrementAssignment", true)]
    [InlineData("op_UnsignedRightShiftAssignment", true)]
    // CLI vocabulary only: C# cannot declare any of these.
    [InlineData("op_AddressOf", false)]
    [InlineData("op_PointerDereference", false)]
    [InlineData("op_LogicalAnd", false)]
    [InlineData("op_LogicalOr", false)]
    [InlineData("op_Assign", false)]
    [InlineData("op_SignedRightShift", false)]
    [InlineData("op_Comma", false)]
    [InlineData("op_MemberSelection", false)]
    [InlineData("op_PointerToMemberSelection", false)]
    [InlineData("op_CheckedImplicit", false)]
    [InlineData("op_Exponent", false)]
    [InlineData("op_IntegerDivision", false)]
    [InlineData("op_Concatenate", false)]
    [InlineData("op_Like", false)]
    public void CSharp_declaration_vocabulary_is_a_subset_of_the_metadata_vocabulary(
        string input,
        bool declarableInCSharp)
    {
        Assert.True(OperatorNames.IsMetadataOperatorMethodName(input));
        Assert.Equal(declarableInCSharp, OperatorNames.IsCSharpOperatorMethodName(input));
    }

    [Theory]
    // A metadata operator needs the SpecialName flag and zero generic arity;
    // the name alone never classifies.
    [InlineData("op_Addition", true, 0, true)]
    [InlineData("op_Addition", false, 0, false)]
    [InlineData("op_Addition", true, 1, false)]
    [InlineData("op_LogicalAnd", true, 0, true)]
    [InlineData("op_LogicalAnd", false, 0, false)]
    [InlineData("op_IncrementAssignment", true, 1, false)]
    [InlineData("op_Multiply", false, 0, false)]
    [InlineData("op_Custom", true, 0, false)]
    public void Metadata_operator_classification_requires_special_name_and_arity(
        string name,
        bool isSpecialName,
        int genericArity,
        bool expected)
        => Assert.Equal(
            expected,
            OperatorNames.IsMetadataOperatorMethod(name, isSpecialName, genericArity));

    [Theory]
    // name, isStatic, isPublic, returnType, parameterCount, hasRefOrOut, participates, expected
    [InlineData("op_Addition", true, true, "T", 2, false, true, true)]
    [InlineData("op_Addition", true, true, "T", 2, false, false, false)]
    [InlineData("op_Addition", false, true, "T", 2, false, true, false)]
    [InlineData("op_Addition", true, false, "T", 2, false, true, false)]
    [InlineData("op_Addition", true, true, "void", 2, false, true, false)]
    [InlineData("op_Addition", true, true, "T", 1, false, true, false)]
    [InlineData("op_Addition", true, true, "T", 2, true, true, false)]
    [InlineData("op_UnaryNegation", true, true, "T", 1, false, true, true)]
    [InlineData("op_Equality", true, true, "bool", 2, false, true, true)]
    [InlineData("op_Equality", true, true, "T", 2, false, true, true)]
    [InlineData("op_True", true, true, "System.Boolean", 1, false, true, true)]
    [InlineData("op_True", true, true, "int", 1, false, true, false)]
    [InlineData("op_Implicit", true, true, "T", 1, false, true, true)]
    [InlineData("op_Implicit", true, true, "T", 1, false, false, false)]
    // CLI-only names are never a C# declaration, whatever their shape.
    [InlineData("op_LogicalAnd", true, true, "T", 2, false, true, false)]
    [InlineData("op_Assign", true, true, "T", 2, false, true, false)]
    [InlineData("op_CheckedImplicit", true, true, "T", 1, false, true, false)]
    [InlineData("op_SignedRightShift", true, true, "T", 2, false, true, false)]
    public void CSharp_operator_declaration_shape(
        string name,
        bool isStatic,
        bool isPublic,
        string returnType,
        int parameterCount,
        bool hasRefOrOutParameter,
        bool declaringTypeParticipates,
        bool expected)
        => Assert.Equal(
            expected,
            OperatorNames.IsCSharpOperatorDeclaration(
                name,
                isStatic,
                isPublic,
                returnType,
                parameterCount,
                hasRefOrOutParameter,
                declaringTypeParticipates));

    [Fact]
    public void CSharp_comparison_operator_can_return_non_boolean_type()
        => Assert.True(
            OperatorNames.IsCSharpOperatorDeclaration(
                "op_GreaterThan",
                isStatic: true,
                isPublic: true,
                returnType: "TResult",
                parameterCount: 2,
                declaringTypeParticipates: true));

    [Fact]
    public void CSharp_operator_declaration_rejects_by_ref_return()
        => Assert.False(
            OperatorNames.IsCSharpOperatorDeclaration(
                "op_Addition",
                isStatic: true,
                isPublic: true,
                returnType: "T",
                parameterCount: 2,
                declaringTypeParticipates: true,
                hasByRefReturn: true));

    [Theory]
    // Binary/unary operators need the declaring type among their parameters.
    [InlineData("op_Addition", true, false, true)]
    [InlineData("op_Addition", false, true, false)]
    [InlineData("op_Addition", false, false, false)]
    // Conversions accept it as either source or target.
    [InlineData("op_Implicit", false, true, true)]
    [InlineData("op_Explicit", true, false, true)]
    [InlineData("op_Explicit", false, false, false)]
    // The receiver of an instance compound assignment is the declaring type.
    [InlineData("op_AdditionAssignment", false, false, true)]
    [InlineData("op_CheckedIncrementAssignment", false, false, true)]
    public void Declaring_type_participation_rule(
        string name,
        bool anyParameterIsDeclaringType,
        bool returnTypeIsDeclaringType,
        bool expected)
        => Assert.Equal(
            expected,
            OperatorNames.DeclaringTypeParticipates(
                name,
                anyParameterIsDeclaringType,
                returnTypeIsDeclaringType));

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
    [InlineData("op_CheckedImplicit")]
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
    [InlineData("op_LessThan", "op_GreaterThan")]
    [InlineData("op_GreaterThanOrEqual", "op_LessThanOrEqual")]
    [InlineData("op_True", "op_False")]
    [InlineData("op_CheckedAddition", "op_Addition")]
    [InlineData("op_CheckedAdditionAssignment", "op_AdditionAssignment")]
    [InlineData("op_Addition", null)]
    [InlineData("op_Custom", null)]
    public void RequiredOperatorSibling_maps_only_language_required_pairs(
        string input,
        string? expected)
        => Assert.Equal(expected, OperatorNames.RequiredOperatorSibling(input));

    [Theory]
    [InlineData("in Samples.Widget", "Samples.Widget", true)]
    [InlineData("ref Samples.Widget", "out Samples.Widget", true)]
    [InlineData("Samples.Widget&", "Samples.Widget", true)]
    [InlineData("Samples.Widget?", "Samples.Widget", true)]
    [InlineData("List<dynamic?>", "List<object>", true)]
    [InlineData("int?", "int", false)]
    [InlineData("Samples.Widget", "Samples.Other", false)]
    public void OperatorPairingTypesMatch_UsesCSharpPairingIdentity(
        string left,
        string right,
        bool expected)
        => Assert.Equal(
            expected,
            OperatorNames.OperatorPairingTypesMatch(left, right));
}
