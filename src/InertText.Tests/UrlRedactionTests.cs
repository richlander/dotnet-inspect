using System.Reflection;

namespace InertText.Tests;

public class UrlRedactionTests
{
    private const string Secret = "VERY-SECRET-TOKEN";

    [Fact]
    public void ForPathComponent_DeclarationIsTypedAndPathOnly()
    {
        MethodInfo method = Assert.Single(
            typeof(UrlRedaction).GetMethods(
                BindingFlags.Public
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(UrlRedaction.ForPathComponent));

        Assert.Equal(typeof(InertString), method.ReturnType);
        ParameterInfo parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(string), parameter.ParameterType);
        Assert.Equal("path", parameter.Name);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("/", "/")]
    [InlineData("//feed.test/path", "//feed.test/path")]
    [InlineData(
        "/proxy/https://a/v3/index.json",
        "/proxy/https://a/v3/index.json")]
    [InlineData(
        "/proxy/https://b/v3/index.json",
        "/proxy/https://b/v3/index.json")]
    [InlineData("/path/user@host/resource", "/path/user@host/resource")]
    [InlineData("/path/scheme:value/resource", "/path/scheme:value/resource")]
    [InlineData("/flat/%C3%A9/%20", "/flat/%C3%A9/%20")]
    [InlineData("/literal?query#fragment", "/literal?query#fragment")]
    public void ForPathComponent_PreservesNonCredentialPathText(
        string path,
        string expected)
    {
        Assert.Equal(expected, UrlRedaction.ForPathComponent(path).ToString());
    }

    [Fact]
    public void ForPathComponent_KeepsAuthorityShapedPathsDistinct()
    {
        InertString first =
            UrlRedaction.ForPathComponent("/proxy/https://a/");
        InertString second =
            UrlRedaction.ForPathComponent("/proxy/https://b/");

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("/F/auth/{0}/api", "/F/auth/REDACTED/api")]
    [InlineData("/F/AUTH/{0}/api", "/F/AUTH/REDACTED/api")]
    [InlineData(
        "/F/auth/auth/{0}/api",
        "/F/auth/REDACTED/REDACTED/api")]
    [InlineData("/F/auth//{0}/api", "/F/auth//REDACTED/api")]
    [InlineData("\\F\\auth\\{0}\\api", "\\F\\auth\\REDACTED\\api")]
    public void ForPathComponent_RedactsCredentialSlots(
        string template,
        string expected)
    {
        InertString redacted = UrlRedaction.ForPathComponent(
            string.Format(template, Secret));

        Assert.Equal(expected, redacted.ToString());
        Assert.DoesNotContain(
            Secret,
            redacted.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ForPathComponent_EncodesNonGraphicScalars()
    {
        InertString redacted =
            UrlRedaction.ForPathComponent("/flat/\u202egnp.evil");

        Assert.True(redacted.WasEncoded);
        Assert.DoesNotContain('\u202e', redacted.ToString());
    }

    [Fact]
    public void ForPathComponent_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => UrlRedaction.ForPathComponent(null!));
    }
}
