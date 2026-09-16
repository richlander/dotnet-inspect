namespace CSharpText.Tests;

public sealed class CSharpParameterNamesTests
{
    [Fact]
    public void Allocate_ReservesArtifactNamesBeforeSynthesizingFallbacks()
    {
        string[] names = CSharpParameterNames.Allocate([null, "arg0", "", "arg2"]);

        Assert.Equal(["arg0_1", "arg0", "arg2_1", "arg2"], names);
    }

    [Fact]
    public void Allocate_PreservesNonEmptyArtifactNamesExactly()
    {
        string[] names = CSharpParameterNames.Allocate(["class", "A\u0301", " "]);

        Assert.Equal(["class", "A\u0301", " "], names);
    }

    [Fact]
    public void Allocate_ReservesDeclarationBindersForSynthesizedNamesOnly()
    {
        string[] names = CSharpParameterNames.Allocate(
            [null, "arg1"],
            ["arg0", "arg1"]);

        Assert.Equal(["arg0_1", "arg1"], names);
    }
}
