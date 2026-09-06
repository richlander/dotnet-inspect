using System.CommandLine;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class SearchSourceAdapterTests
{
    public SearchSourceAdapterTests() => NuGetCache.Initialize("dotnet-inspect");

    [Theory]
    [InlineData("find")]
    [InlineData("implements")]
    [InlineData("extensions")]
    [InlineData("depends")]
    public async Task PackageFormsAndDirectSourcesLowerWithoutRewriting(string command)
    {
        string[] packages = ["Contoso", "Contoso@latest", "Contoso@2.*",
            "Contoso@1.0.0+Build", "Contoso@", "./local/Contoso.1.0.0.nupkg"];
        List<string> args = [];
        foreach (string package in packages)
            args.AddRange(["--package", package]);
        args.AddRange(["--library", "relative library.dll", "--project", "not-created.csproj",
            "--platform", "System.Text.Json"]);
        var intent = DeclareSources(command, [.. args]);
        using var client = new HttpClient();
        var sourceOptions = new NuGetSourceOptions { Sources = ["https://source.example/v3/index.json"] };
        var (selection, request) = await SearchSourceAdapter.BindAsync(intent, client, false, sourceOptions);

        Assert.Same(intent, selection.Intent);
        Assert.False(selection.UsesImplicitPlatform);
        Assert.Equal(packages, request.Packages);
        Assert.Equal(["relative library.dll"], request.Assemblies);
        Assert.Equal(["not-created.csproj"], request.Projects);
        Assert.Equal(["System.Text.Json"], request.PlatformAssemblies);
        Assert.Empty(request.PlatformFrameworks);
        Assert.Same(sourceOptions, request.SourceOptions);
        Assert.Equal("./local/Contoso.1.0.0.nupkg",
            Assert.Single(intent.Selectors.OfType<SourceSelector.PackageArchive>()).Path);
    }

    [Theory]
    [InlineData("find")]
    [InlineData("implements")]
    [InlineData("extensions")]
    public void PrefixDeclarationUsesTheTypeSearchBound(string command)
    {
        var intent = DeclareSources(command, "--package-prefix", "Contoso.");
        var prefix = Assert.IsType<SourceSelector.PackagePrefix>(Assert.Single(intent.Selectors));
        Assert.Equal("Contoso.", prefix.Request.Prefix);
        Assert.Equal(500, prefix.Request.MaxPackages);
        Assert.False(prefix.Request.IncludePrerelease);
    }

    [Fact]
    public async Task PrefixBindingPreservesBoundPrecedenceAndOriginalIntent()
    {
        using var handler = new PrefixHandler(["Contoso.Core", "Contoso.First", "Contoso.Other"]);
        using var client = new HttpClient(handler);
        var prefix = new SourceSelector.PackagePrefix(new("Contoso.", 2, includePrerelease: true));
        var intent = SourceIntent.Create(
        [
            new SourceSelector.PackageGroup([new("Contoso.Core"), new("Group.Remaining")]),
            new SourceSelector.PackageReference("Contoso.Other"),
            prefix,
            new SourceSelector.PackageReference("Contoso.First", "1.0.0"),
        ]);

        var (_, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            var (selection, request) = await SearchSourceAdapter.BindAsync(
                intent, client, false, handler.SourceOptions);
            Assert.Same(intent, selection.Intent);
            Assert.Equal(4, intent.Selectors.Count);
            Assert.Same(prefix, Assert.Single(selection.OtherSources));
            Assert.Equal(["Contoso.Other", "Contoso.First@1.0.0", "Contoso.Core", "Contoso.First", "Group.Remaining"],
                request.Packages);
            Assert.Empty(request.PlatformFrameworks);
            return 0;
        });

        Assert.Empty(output);
        Assert.Contains("2-package search limit", error);
        Assert.Contains(handler.Queries, uri =>
            uri.Query.Contains("prerelease=true", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EmptyPrefixRemainsExplicitAndWarns()
    {
        using var handler = new PrefixHandler([]);
        using var client = new HttpClient(handler);
        var intent = DeclareSources("find", "--package-prefix", "Contoso.");
        var (_, output, error) = await ConsoleCapture.RunAsync(async () =>
        {
            var (selection, request) = await SearchSourceAdapter.BindAsync(
                intent, client, false, handler.SourceOptions);
            Assert.Same(intent, selection.Intent);
            Assert.False(selection.UsesImplicitPlatform);
            Assert.Single(selection.OtherSources);
            Assert.Empty(request.Packages);
            Assert.Empty(request.PlatformFrameworks);
            return 0;
        });

        Assert.Empty(output);
        Assert.Contains("No packages found matching prefix", error);
    }

    [Theory]
    [InlineData("find")]
    [InlineData("implements")]
    [InlineData("extensions")]
    [InlineData("depends")]
    public async Task NormalizedEmptyGroupDoesNotTriggerLegacyCommandDefaults(string command)
    {
        var selection = SearchSourceNormalizer.Normalize(
            SourceIntent.Empty.Append(new SourceSelector.PackageGroup([])));
        var (exit, output, error) = await ConsoleCapture.RunAsync(async () => command switch
        {
            "find" => await FindCommand.ExecuteAsync(new FindOptions
            {
                Pattern = "System.String", SourceSelection = selection, Count = true,
            }),
            "implements" => await ImplementsCommand.ExecuteAsync(new ImplementsOptions
            {
                TargetType = "IDisposable", SourceSelection = selection, Count = true,
            }),
            "extensions" => await ExtensionsCommand.ExecuteAsync(new ExtensionsOptions
            {
                TargetType = "IEnumerable<T>", SourceSelection = selection, Count = true,
            }),
            "depends" => (await DependsCommand.ExecuteTypeDependsAsync(new DependsOptions
            {
                TargetType = "System.String", SourceSelection = selection, Count = true,
            })).ExitCode,
            _ => throw new InvalidOperationException(),
        });

        Assert.Equal(command == "depends" ? DependsCommand.TypeNotFoundExitCode : 0, exit);
        Assert.Equal(command == "depends" ? "" : "0", output.Trim());
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("""{"data":[{"id":"Contoso.not a package","version":"1.0.0"}]}""")]
    public async Task PrefixFailureIsNotAnEmptySuccessfulSelection(string response)
    {
        using var handler = new PrefixHandler([], response);
        using var client = new HttpClient(handler);
        var intent = DeclareSources("find", "--package-prefix", "Contoso.");

        await Assert.ThrowsAsync<PrefixResolutionException>(() =>
            SearchSourceAdapter.BindAsync(intent, client, false, handler.SourceOptions));
    }

    [Theory]
    [InlineData("net10.0", null)]
    [InlineData(null, "linux-x64")]
    public async Task UnsupportedCoordinateQualifiersAreNotSilentlyDiscarded(string? framework, string? runtime)
    {
        var intent = SourceIntent.Empty.Append(
            new SourceSelector.Package(new("Contoso", "1.0.0", framework, runtime)));
        using var client = new HttpClient();
        await Assert.ThrowsAsync<SearchSourceValidationException>(() =>
            SearchSourceAdapter.BindAsync(intent, client, false, null));
    }

    [Theory]
    [InlineData("find", "DotnetInspector.Fixtures.ExternalDerivedFromGeneric")]
    [InlineData("implements", "DotnetInspector.Fixtures.ExternalGenericBase<int>")]
    [InlineData("extensions", "IEnumerable<T>")]
    [InlineData("depends", "DotnetInspector.Fixtures.ExternalDerivedFromGeneric")]
    public async Task EachCommandInspectsExplicitLocalPackage(string command, string target)
    {
        string directory = Directory.CreateTempSubdirectory("source-intent-package-").FullName;
        string package = Path.Combine(directory, "SourceIntentFixture.1.0.0.nupkg");
        string assembly = command == "extensions"
            ? typeof(Enumerable).Assembly.Location
            : typeof(DotnetInspector.Fixtures.ExternalDerivedFromGeneric).Assembly.Location;
        try
        {
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
                archive.CreateEntryFromFile(assembly, "lib/net11.0/SourceIntentFixture.dll");

            var (exit, output, error) = await Invoke(
                command, target, "--package", package, "--count", "--tips", "q");

            Assert.True(exit == 0, error);
            Assert.True(int.TryParse(output.Trim(), out int count) && count > 0,
                $"Expected local-package results, got: {output}\n{error}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("find")]
    [InlineData("implements")]
    [InlineData("extensions")]
    [InlineData("depends")]
    public async Task InvalidSourceTextUsesTheCleanCliErrorBoundary(string command)
    {
        var (exit, _, error) = await Invoke(command, "Probe", "--package", "not a package");
        Assert.Equal(1, exit);
        Assert.Contains("Error:", error);
        Assert.DoesNotMatch(@"(?m)^\s+at\s", error);
        Assert.DoesNotContain("Exception", error);
    }

    [Theory]
    [InlineData("--platform")]
    [InlineData("--extensions")]
    [InlineData("--aspnetcore")]
    public async Task PatternlessProfileRejectsSearchGroupsWithoutTypeSearchPrefixValidation(string group)
    {
        var (exit, _, error) = await Invoke("find", "--package-prefix", "prefix with spaces", group);
        Assert.Equal(1, exit);
        Assert.Contains("Patternless --package-prefix cannot be combined with API search scopes", error);
        Assert.DoesNotContain("Invalid package", error);
    }

    internal static SourceIntent DeclareSources(string command, params string[] args)
    {
        var root = CommandLineBuilder.CreateRootCommand();
        var parsed = root.Parse(CommandLineBuilder.PreprocessArgs([command, "Probe", .. args]));
        Assert.Empty(parsed.Errors);
        Command definition = parsed.CommandResult.Command;
        return SearchSourceAdapter.Declare(
            parsed,
            Required<string[]>("--package"),
            Required<string[]>("--library"),
            Required<string[]>("--project"),
            Required<bool>("--platform"),
            Required<string[]>(CommandLineHelpers.PlatformLibraryOptionName),
            Required<bool>("--extensions"),
            Required<bool>("--aspnetcore"),
            Optional<string[]>("--bin"),
            Optional<string?>("--package-prefix"));

        Option<T> Required<T>(string name) =>
            Assert.IsType<Option<T>>(definition.Options.Single(option => option.Name == name));
        Option<T>? Optional<T>(string name) =>
            definition.Options.OfType<Option<T>>().SingleOrDefault(option => option.Name == name);
    }

    private static Task<(int Exit, string Output, string Error)> Invoke(params string[] args) =>
        ConsoleCapture.RunAsync(() => CommandLineBuilder.InvokeAsync(
            CommandLineBuilder.CreateRootCommand().Parse(CommandLineBuilder.PreprocessArgs(args)), args));

    private sealed class PrefixHandler(string[] ids, string? response = null) : HttpMessageHandler
    {
        private const string Index = "https://source-intent.example/v3/index.json";
        private const string Search = "https://source-intent.example/v3/query";
        public NuGetSourceOptions SourceOptions { get; } = new() { Sources = [Index] };
        public List<Uri> Queries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            string body;
            if (uri.GetLeftPart(UriPartial.Path) == Index)
                body = $$"""{"resources":[{"@id":"{{Search}}","@type":"SearchQueryService"}]}""";
            else if (uri.GetLeftPart(UriPartial.Path) == Search)
            {
                Queries.Add(uri);
                body = response ?? JsonSerializer.Serialize(new
                {
                    data = ids.Select(id => new { id, version = "1.0.0" }).ToArray(),
                });
            }
            else
                throw new InvalidOperationException($"Unexpected source request: {uri}");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }
}
