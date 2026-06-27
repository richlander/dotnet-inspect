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

    [Theory]
    [InlineData("<seed>P", "seed")]                 // C# 12 primary-ctor capture
    [InlineData("<value>P", "value")]
    [InlineData("<\u00e9>P", "\u00e9")]             // precomposed Unicode letter
    [InlineData("<e\u0301>P", "e\u0301")]           // letter + combining mark
    [InlineData("<\u216b>P", "\u216b")]             // letter-number start (Roman numeral)
    [InlineData("<\U0001d4cd>P", "\U0001d4cd")]    // astral-plane letter (surrogate pair)
    [InlineData("<seed>k__BackingField", null)]     // auto-property backing field
    [InlineData("<>c", null)]                       // display class
    [InlineData("<M>g__Local|0_0", null)]           // local function
    [InlineData("ordinary", null)]                  // ordinary field
    [InlineData("<>P", null)]                        // empty inner name
    [InlineData("<<x>g__>P", null)]                 // nested mangle, not an identifier
    public void PrimaryConstructorCaptureName_DemanglesCaptureFields(string fieldName, string? expected)
        => Assert.Equal(expected, CSharpNaming.PrimaryConstructorCaptureName(fieldName));
}
