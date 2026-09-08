namespace CSharpText.Tests;

public class MemberCanonicalSignatureTests
{
    [Fact]
    public void Build_Method_UsesFullNameGrammar()
    {
        Assert.Equal(
            "M:System.String.Substring(System.Int32)",
            MemberCanonicalSignature.Build("M", "System.String", "Substring", ["System.Int32"]));
    }

    [Fact]
    public void Build_NoParameters_EmitsEmptyParentheses()
    {
        Assert.Equal(
            "M:System.Object.ToString()",
            MemberCanonicalSignature.Build("M", "System.Object", "ToString", []));
    }

    [Fact]
    public void Build_ConversionOperator_AppendsReturnTypeSuffix()
    {
        Assert.Equal(
            "M:System.Decimal.op_Explicit(System.Decimal)~System.Byte",
            MemberCanonicalSignature.Build("M", "System.Decimal", "op_Explicit", ["System.Decimal"], "System.Byte"));
    }

    [Fact]
    public void Build_NullOrWhitespaceReturnType_OmitsSuffix()
    {
        Assert.Equal(
            "M:System.Decimal.op_Explicit(System.Decimal)",
            MemberCanonicalSignature.Build("M", "System.Decimal", "op_Explicit", ["System.Decimal"], conversionReturnType: null));
        Assert.Equal(
            "M:System.Decimal.op_Explicit(System.Decimal)",
            MemberCanonicalSignature.Build("M", "System.Decimal", "op_Explicit", ["System.Decimal"], conversionReturnType: "  "));
    }

    [Theory]
    [InlineData("F", "F:System.String.Empty")]
    [InlineData("E", "E:System.AppDomain.ProcessExit")]
    public void Build_FieldAndEvent_HaveNoParameterList(string kind, string expected)
    {
        var memberName = kind == "F" ? "Empty" : "ProcessExit";
        var typeName = kind == "E" ? "System.AppDomain" : "System.String";
        // Parameter list is ignored for F/E even if provided: neither can be
        // overloaded, so neither identity can carry one.
        Assert.Equal(expected, MemberCanonicalSignature.Build(kind, typeName, memberName, ["System.Int32"]));
    }

    [Fact]
    public void Build_OrdinaryProperty_HasNoParameterList()
    {
        Assert.Equal(
            "P:System.String.Length",
            MemberCanonicalSignature.Build("P", "System.String", "Length", []));
    }

    [Fact]
    public void Build_Indexer_IncludesIndexParameters()
    {
        // An indexer overloads on its index parameters, so they are part of
        // property identity; two overloads must not collide on "P:Type.Item".
        Assert.Equal(
            "P:System.Collections.Generic.List<T>.Item(System.Int32)",
            MemberCanonicalSignature.Build(
                "P",
                "System.Collections.Generic.List<T>",
                "Item",
                ["System.Int32"]));
        Assert.NotEqual(
            MemberCanonicalSignature.Build("P", "N.C", "Item", ["System.Int32"]),
            MemberCanonicalSignature.Build("P", "N.C", "Item", ["System.String"]));
    }

    [Fact]
    public void BuildExtensionProperty_IncludesReceiverAndIndexerParameters()
    {
        Assert.Equal(
            "P:Sample.Extensions.Item(System.String,System.Int32)",
            MemberCanonicalSignature.BuildExtensionProperty(
                "Sample.Extensions",
                "Item",
                ["System.String", "System.Int32"]));
    }
}
