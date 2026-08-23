using System.Text.Json;
using System.Xml.Linq;

namespace InspectWeb.Engine.Tests;

public class BrowserStaticWebAppConfigTests
{
    [Fact]
    public void EntryDocumentsAreNotCachedAndConfigIsPublished()
    {
        string repository = RepositoryRoot();
        string configPath = Path.Combine(
            repository,
            "prototypes",
            "inspect-web",
            "staticwebapp.config.json");

        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement[] routes =
        [
            .. config.RootElement.GetProperty("routes").EnumerateArray(),
        ];

        Assert.Equal(4, routes.Length);
        AssertRoute(routes[0], "/");
        AssertRoute(routes[1], "/index.html");
        AssertRoute(routes[2], "/credits", "/index.html");
        AssertRoute(routes[3], "/credits/*", "/index.html");
        Assert.Equal(
            routes.Length,
            routes
                .Select(route => NormalizeAzureRoute(
                    route.GetProperty("route").GetString()!))
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(
            "dotnet-isolated:8.0",
            config.RootElement
                .GetProperty("platform")
                .GetProperty("apiRuntime")
                .GetString());

        XDocument project = XDocument.Load(Path.Combine(
            repository,
            "prototypes",
            "inspect-web",
            "engine",
            "InspectWeb.Engine.csproj"));
        XElement content = Assert.Single(
            project.Descendants("Content"),
            element =>
                (string?)element.Attribute("Include") ==
                @"..\staticwebapp.config.json");

        Assert.Equal(
            @"wwwroot\staticwebapp.config.json",
            (string?)content.Attribute("Link"));
        Assert.Equal(
            "PreserveNewest",
            (string?)content.Attribute("CopyToPublishDirectory"));

        XElement verificationTarget = Assert.Single(
            project.Descendants("Target"),
            element =>
                (string?)element.Attribute("Name") ==
                "VerifyPublishedInspectWebSite");
        Assert.Equal(
            "PublishInspectWebFrontendIndex",
            (string?)verificationTarget.Attribute("AfterTargets"));
        XElement verificationCommand = Assert.Single(
            verificationTarget.Elements("Exec"));
        Assert.Contains(
            "verify-site-artifact.js",
            (string?)verificationCommand.Attribute("Command"));
        Assert.Contains(
            "$(PublishDir)wwwroot",
            (string?)verificationCommand.Attribute("Command"));
    }

    private static void AssertRoute(
        JsonElement route,
        string expectedPath,
        string? expectedRewrite = null)
    {
        Assert.Equal(expectedPath, route.GetProperty("route").GetString());
        if (expectedRewrite is null)
        {
            Assert.False(route.TryGetProperty("rewrite", out _));
        }
        else
        {
            Assert.Equal(
                expectedRewrite,
                route.GetProperty("rewrite").GetString());
        }
        Assert.Equal(
            "no-cache, no-store, must-revalidate",
            route
                .GetProperty("headers")
                .GetProperty("Cache-Control")
                .GetString());
    }

    private static string NormalizeAzureRoute(string route)
    {
        string normalized = route.TrimEnd('/');
        return normalized.Length == 0 ? "/" : normalized;
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find repository root containing dotnet-inspect.slnx.");
    }
}
