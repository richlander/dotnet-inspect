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
    [InlineData("Normal", "Normal")]
    public void SourceMethodName_EscapesKeywords(string metadataName, string expected)
        => Assert.Equal(expected, CSharpNaming.SourceMethodName(metadataName));

    // A compiler-generated method name a raising pass left standing (a lambda body
    // method, a record Clone) is unspeakable in C#; SourceMethodName sanitizes it
    // into a legal identifier so the rendered body parses, rather than leaking the
    // raw <...> name into a method-group / call site (#3129).
    //
    // A local function's <Enclosing>g__Name|N_M belongs in this list, not in the
    // demangling one: reaching SourceMethodName at all means LocalFunctionRaisingPass
    // declined to raise it, so no declaration of `Name` is emitted and spelling the
    // call `Name(...)` would be a call to a method that exists nowhere (#3631).
    // CSharpNaming.MethodName still decodes the source spelling for the raising pass,
    // which is the only caller entitled to it.
    [Theory]
    [InlineData("<M>b__0_0", "__M_b__0_0")]           // lambda body method
    [InlineData("<Ag__B>b__0_0", "__Ag__B_b__0_0")]   // enclosing name itself contains g__
    [InlineData("<RedisFireAndForget>b__8_0", "__RedisFireAndForget_b__8_0")]
    [InlineData("<Clone>$", "__Clone__")]             // record synthesized clone
    [InlineData("<M>g__Local|0_0", "__M_g__Local_0_0")]     // unraised local function
    [InlineData("<M>g__return|0_0", "__M_g__return_0_0")]   // ...whose source name is a keyword
    public void SourceMethodName_SanitizesUnspellableGeneratedName(string metadataName, string expected)
    {
        string actual = CSharpNaming.SourceMethodName(metadataName);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain('<', actual);
        Assert.DoesNotContain('>', actual);
    }

    [Theory]
    [InlineData("class", "@class")]
    [InlineData("class`1", "@class")]
    [InlineData("Normal`1", "Normal")]
    [InlineData("<>c__DisplayClass0_0", "___c__DisplayClass0_0")]
    public void TypeNameSegment_StripsArityAndEscapesKeywords(string metadataName, string expected)
        => Assert.Equal(expected, CSharpNaming.TypeNameSegment(metadataName));

    [Fact]
    public void SafeIdentifier_PreservesAstralPlaneIdentifiers()
        => Assert.Equal("\U0001d4cd", CSharpNaming.SafeIdentifier("\U0001d4cd"));

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
