using System.Reflection;

namespace ILInspector.Metadata.Tests;

public sealed class GenericConstraintKeywordsTests
{
    [Theory]
    [InlineData(GenericParameterAttributes.ReferenceTypeConstraint, 0, false, "class")]
    [InlineData(GenericParameterAttributes.ReferenceTypeConstraint, 2, false, "class?")]
    [InlineData(GenericParameterAttributes.NotNullableValueTypeConstraint, 0, false, "struct")]
    [InlineData(GenericParameterAttributes.NotNullableValueTypeConstraint, 0, true, "unmanaged")]
    [InlineData(GenericParameterAttributes.None, 0, true, "unmanaged")]
    [InlineData(GenericParameterAttributes.None, 1, false, "notnull")]
    [InlineData(GenericParameterAttributes.None, 0, false, null)]
    [InlineData(GenericParameterAttributes.None, 2, false, null)]
    public void PrimaryKeyword_MapsMutuallyExclusiveConstraints(
        GenericParameterAttributes attributes,
        int nullableFlag,
        bool isUnmanaged,
        string? expected)
        => Assert.Equal(expected, GenericConstraintKeywords.PrimaryKeyword(attributes, nullableFlag, isUnmanaged));

    [Fact]
    public void PrimaryKeyword_ReferenceConstraintWinsOverUnmanagedContext()
        => Assert.Equal("class", GenericConstraintKeywords.PrimaryKeyword(
            GenericParameterAttributes.ReferenceTypeConstraint, nullableFlag: 0, isUnmanaged: true));

    [Theory]
    [InlineData(GenericParameterAttributes.DefaultConstructorConstraint, "new()")]
    [InlineData(
        GenericParameterAttributes.DefaultConstructorConstraint | GenericParameterAttributes.NotNullableValueTypeConstraint,
        null)]
    [InlineData(GenericParameterAttributes.None, null)]
    public void NewConstraintKeyword_SuppressedByValueTypeConstraint(
        GenericParameterAttributes attributes,
        string? expected)
        => Assert.Equal(expected, GenericConstraintKeywords.NewConstraintKeyword(attributes));

    [Theory]
    [InlineData(GenericParameterAttributes.Covariant, "out")]
    [InlineData(GenericParameterAttributes.Contravariant, "in")]
    [InlineData(GenericParameterAttributes.None, null)]
    public void VarianceKeyword_MapsCovarianceAndContravariance(
        GenericParameterAttributes attributes,
        string? expected)
        => Assert.Equal(expected, GenericConstraintKeywords.VarianceKeyword(attributes));

    [Theory]
    [InlineData(GenericParameterAttributes.AllowByRefLike, "allows ref struct")]
    [InlineData(GenericParameterAttributes.None, null)]
    public void AllowsRefStructKeyword_MapsByRefLike(
        GenericParameterAttributes attributes,
        string? expected)
        => Assert.Equal(expected, GenericConstraintKeywords.AllowsRefStructKeyword(attributes));
}
