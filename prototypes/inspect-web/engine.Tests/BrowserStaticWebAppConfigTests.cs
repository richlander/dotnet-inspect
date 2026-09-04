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

        Assert.Equal(5, routes.Length);
        AssertRoute(routes[0], "/");
        AssertRoute(routes[1], "/index.html");
        AssertRoute(routes[2], "/credits", "/index.html");
        AssertRoute(routes[3], "/demos", "/index.html");
        AssertRoute(routes[4], "/query", "/index.html");
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
            "verify-site-artifact.ts",
            (string?)verificationCommand.Attribute("Command"));
        Assert.Contains(
            "$(PublishDir)wwwroot",
            (string?)verificationCommand.Attribute("Command"));
    }

    [Fact]
    public void RouteKeysAreUnique()
    {
        string repository = RepositoryRoot();
        string configPath = Path.Combine(
            repository,
            "prototypes",
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

    // CI build #33038263668 failed because renaming `verify-site-artifact.js` to `.ts`
    // updated every npm script and import but missed both `Exec` commands in the engine
    // project, which invoke the script from MSBuild. The assertion above pins the name in
    // one of those two commands, so it caught nothing about the other, and neither
    // assertion checks that the referenced file exists at all.
    //
    // This derives the expected set from the project file instead of restating it: every
    // script an `Exec` hands to `node` must resolve on disk. A rename that misses either
    // command fails here, and so does a new `Exec` added with a bad path.
    [Fact]
    public void EngineProjectNodeScriptsExist()
    {
        string repository = RepositoryRoot();
        string engineDirectory = Path.Combine(
            repository,
            "prototypes",
            "inspect-web",
            "engine");
        XDocument project = XDocument.Load(
            Path.Combine(engineDirectory, "InspectWeb.Engine.csproj"));

        string[] scripts =
        [
            .. project.Descendants("Exec")
                .Select(element => (string?)element.Attribute("Command"))
                .Where(command => command is not null)
                .SelectMany(command => NodeScriptArguments(command!)),
        ];

        Assert.NotEmpty(scripts);
        foreach (string script in scripts)
        {
            Assert.True(
                File.Exists(Path.Combine(engineDirectory, script)),
                $"The engine project runs 'node \"{script}\"', but that file does not " +
                "exist relative to the project directory.");
        }
    }

    // An `Exec` command is a shell line, so the script is the first quoted argument after
    // `node`. Only unexpanded literals are considered: a path built from an MSBuild
    // property cannot be resolved here, and claiming otherwise would make this pass by
    // finding nothing to check.
    //
    // Both round 1 reviewers noted that matching the segment exactly against "node" made
    // the gate vacuous the moment anyone passed Node an option, because the segment then
    // reads "node --experimental-strip-types". Matching on the leading word instead keeps
    // such a command covered, and `Assert.NotEmpty` alone would not have caught the
    // regression: the other command would have kept the set non-empty.
    private static IEnumerable<string> NodeScriptArguments(string command)
    {
        string[] tokens = command.Split('"', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index + 1 < tokens.Length; index++)
        {
            string[] words = tokens[index]
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0
                && Path.GetFileNameWithoutExtension(words[0]) == "node"
                && !tokens[index + 1].Contains("$("))
                yield return tokens[index + 1];
        }
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
