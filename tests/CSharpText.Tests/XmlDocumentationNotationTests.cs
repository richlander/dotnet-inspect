namespace CSharpText.Tests;

public sealed class XmlDocumentationNotationTests
{
    [Fact]
    public void CreateMemberIdentity_UsesXmlDocumentationGrammar()
    {
        var identity = XmlDocumentationNotation.CreateMemberIdentity(
            "M",
            "Samples.Outer+Inner",
            "System.IEquatable<System.String>.Equals",
            ["TType", "TMethod"],
            ["TType"],
            "Equals<TMethod>",
            "int");

        Assert.Equal(
            "M:Samples.Outer.Inner.System#IEquatable{System#String}#Equals",
            identity.LookupKey);
        Assert.Equal(["T0", "M0"], identity.NormalizedParameters);
        Assert.Equal("System.Int32", identity.NormalizedReturnType);
    }

    [Theory]
    [InlineData(".ctor", "#ctor")]
    [InlineData(".cctor", ".cctor")]
    public void MemberNameNormalization_PreservesConstructorNotation(string input, string expected)
        => Assert.Equal(expected, XmlDocumentationNotation.NormalizeMemberName(input));

    [Fact]
    public void ParameterNormalization_UsesGenericMaps()
    {
        Dictionary<string, int> typeParameters = new(StringComparer.Ordinal)
        {
            ["TType"] = 0
        };
        Dictionary<string, int> methodParameters = new(StringComparer.Ordinal)
        {
            ["TMethod"] = 0
        };

        Assert.Equal(
            "T0",
            XmlDocumentationNotation.NormalizeParameterType(
                "TType",
                typeParameters,
                methodParameters));
        Assert.Equal(
            "M0",
            XmlDocumentationNotation.NormalizeParameterType(
                "TMethod",
                typeParameters,
                methodParameters));
        Assert.Equal(
            "System.Collections.Generic.Dictionary{T0,M0}",
            XmlDocumentationNotation.NormalizeParameterType(
                "System.Collections.Generic.Dictionary<TType, TMethod>",
                typeParameters,
                methodParameters));
    }

    [Fact]
    public void SignatureParameterNormalization_StripsParameterNamesForFallback()
    {
        Assert.Equal(
            "System.Int32",
            NormalizeSignatureParameter("int index"));
        Assert.Equal(
            "System.Collections.Generic.Dictionary{System.String,System.Int32}",
            NormalizeSignatureParameter(
                "System.Collections.Generic.Dictionary<string, int> values = null"));
        Assert.Equal(
            "System.Int32",
            NormalizeSignatureParameter(
                """[System.ComponentModel.DefaultValue("=")] int value = 0"""));
        Assert.Equal(
            "System.Int32@",
            NormalizeSignatureParameter(
                "[System.Runtime.InteropServices.In] ref int value"));
    }

    [Theory]
    [InlineData("ref int", "System.Int32@")]
    [InlineData("out int", "System.Int32@")]
    [InlineData("in int", "System.Int32@")]
    [InlineData("System.Int32@", "System.Int32@")]
    [InlineData("params int[]", "System.Int32[]")]
    [InlineData("int*", "System.Int32*")]
    [InlineData("int[,]", "System.Int32[,]")]
    [InlineData("System.Int32[0:,0:]", "System.Int32[,]")]
    [InlineData("int?", "System.Nullable{System.Int32}")]
    [InlineData("System.DateTime?", "System.Nullable{System.DateTime}")]
    [InlineData("string?", "System.String")]
    public void ParameterNormalization_PreservesExistingNotation(string input, string expected)
        => Assert.Equal(expected, XmlDocumentationNotation.NormalizeParameterType(input));

    [Theory]
    [InlineData("dynamic", "object")]
    [InlineData("List<dynamic>", "List<object>")]
    [InlineData("dynamic[]", "object[]")]
    [InlineData("Dictionary<dynamic,object>", "Dictionary<object,object>")]
    [InlineData("Func<object,dynamic>", "Func<object,object>")]
    [InlineData("MyDynamicType", "MyDynamicType")]
    [InlineData("System.Dynamic.ExpandoObject", "System.Dynamic.ExpandoObject")]
    [InlineData("Ns.dynamic", "Ns.dynamic")]
    [InlineData("dynamic`1<System.Int32>", "dynamic`1<System.Int32>")]
    [InlineData("Outer+dynamic", "Outer+dynamic")]
    [InlineData("Outer/dynamic", "Outer/dynamic")]
    public void NormalizeDynamicToObject_KeywordOnly_SparesRealTypeNames(
        string input,
        string expected)
        => Assert.Equal(expected, XmlDocumentationNotation.NormalizeDynamicToObject(input));

    private static string NormalizeSignatureParameter(string parameter)
        => XmlDocumentationNotation.NormalizeSignatureParameter(
            parameter,
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, int>(StringComparer.Ordinal));
}
