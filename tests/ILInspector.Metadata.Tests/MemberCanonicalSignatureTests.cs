using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

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
    [InlineData("P", "P:System.String.Length")]
    [InlineData("F", "F:System.String.Empty")]
    [InlineData("E", "E:System.AppDomain.ProcessExit")]
    public void Build_PropertyFieldEvent_HasNoParameterList(string kind, string expected)
    {
        var memberName = kind switch { "P" => "Length", "F" => "Empty", _ => "ProcessExit" };
        var typeName = kind == "E" ? "System.AppDomain" : "System.String";
        // Parameter list is ignored for P/F/E even if provided.
        Assert.Equal(expected, MemberCanonicalSignature.Build(kind, typeName, memberName, ["System.Int32"]));
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
