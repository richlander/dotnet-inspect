using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class CSharpNamingTests
{
    [Theory]
    [InlineData("<M>g__Local|0_0", "Local")]
    [InlineData("<Ag__B>g__Local|0_0", "Local")]
    [InlineData("<Ag__B>g__Local", "Local")]
    [InlineData("<Ag__B>b__0_0", "<Ag__B>b__0_0")]
    public void MethodName_DemanglesLocalFunctionAfterEnclosingName(string metadataName, string expected)
        => Assert.Equal(expected, CSharpNaming.MethodName(metadataName));

    [Theory]
    [InlineData("return", "@return")]
    [InlineData("event", "@event")]
    [InlineData("<M>g__return|0_0", "@return")]
    [InlineData("Normal", "Normal")]
    public void SourceMethodName_EscapesKeywordsAfterDemangling(string metadataName, string expected)
        => Assert.Equal(expected, CSharpNaming.SourceMethodName(metadataName));

    [Theory]
    [InlineData("class", "@class")]
    [InlineData("class`1", "@class")]
    [InlineData("Normal`1", "Normal")]
    public void TypeNameSegment_StripsArityAndEscapesKeywords(string metadataName, string expected)
        => Assert.Equal(expected, CSharpNaming.TypeNameSegment(metadataName));
}
