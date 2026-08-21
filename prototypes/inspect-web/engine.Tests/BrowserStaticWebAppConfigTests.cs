using System.Text.Json;
using System.Xml.Linq;

namespace InspectWeb.Engine.Tests;

public class BrowserStaticWebAppConfigTests
{
    [Fact]
    public void RootDocumentsAreNotCachedAndConfigIsPublished()
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

        Assert.Equal(2, routes.Length);
        AssertRoute(routes[0], "/");
        AssertRoute(routes[1], "/index.html");
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
    }

    private static void AssertRoute(JsonElement route, string expectedPath)
    {
        Assert.Equal(expectedPath, route.GetProperty("route").GetString());
        Assert.Equal(
            "no-cache, no-store, must-revalidate",
            route
                .GetProperty("headers")
                .GetProperty("Cache-Control")
                .GetString());
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
