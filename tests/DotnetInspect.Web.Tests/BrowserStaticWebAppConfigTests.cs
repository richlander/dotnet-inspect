using System.Text.Json;

namespace DotnetInspect.Web.Tests;

public class BrowserStaticWebAppConfigTests
{
    [Fact]
    public void EntryDocumentsAreNotCachedAndApiRuntimeIsPinned()
    {
        string repository = RepositoryRoot();
        string configPath = Path.Combine(
            repository,
            "inspect-web",
            "staticwebapp.config.json");

        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement[] routes =
        [
            .. config.RootElement.GetProperty("routes").EnumerateArray(),
        ];

        Assert.Equal(8, routes.Length);
        AssertRoute(routes[0], "/");
        AssertRoute(routes[1], "/index.html");
        AssertRoute(routes[2], "/activity", "/index.html");
        AssertRoute(routes[3], "/credits", "/index.html");
        AssertRoute(routes[4], "/demos", "/index.html");
        AssertRoute(routes[5], "/diagnostics", "/index.html");
        AssertRoute(routes[6], "/query", "/index.html");
        AssertRoute(routes[7], "/type-explorer", "/index.html");
        Assert.Equal(
            "dotnet-isolated:8.0",
            config.RootElement
                .GetProperty("platform")
                .GetProperty("apiRuntime")
                .GetString());
    }

    [Fact]
    public void RouteKeysAreUnique()
    {
        string repository = RepositoryRoot();
        string configPath = Path.Combine(
            repository,
            "inspect-web",
            "staticwebapp.config.json");

        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(configPath));
        string[] routeKeys =
        [
            .. config.RootElement
                .GetProperty("routes")
                .EnumerateArray()
                .Select(route => route.GetProperty("route").GetString()!),
        ];

        Assert.Equal(
            routeKeys.Length,
            routeKeys.Distinct(StringComparer.Ordinal).Count());

        // Azure Static Web Apps normalizes a trailing slash away when matching
        // routes, so "/credits" and "/credits/" collide even though they are
        // distinct strings above. That collision failed deployment twice
        // (#4634, then reintroduced by #5039): catch it here instead of at
        // deploy time.
        string[] normalizedRouteKeys =
        [
            .. routeKeys.Select(
                key => key.Length > 1 ? key.TrimEnd('/') : key),
        ];

        Assert.Equal(
            normalizedRouteKeys.Length,
            normalizedRouteKeys.Distinct(StringComparer.Ordinal).Count());
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
