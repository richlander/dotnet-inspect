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
}
