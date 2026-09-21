using System.Diagnostics;
using System.Text.Json;
using CSharpText;
using CSharpText.MemberSlicing;

using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class LocalRepoSourceProjectionTests : IDisposable
{
    readonly string _cacheDirectory =
        Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-local-repo-projection-{Guid.NewGuid():N}");

    // PR-fast: bounded offline production-host document requests.
    [Theory]
    [InlineData("first", "SourceLinkService.cs", false)]
    [InlineData("first", "SourceLinkService.cs", true)]
    [InlineData("2", "SourceLinkService.SourceContent.cs", false)]
    [InlineData("2", "SourceLinkService.SourceContent.cs", true)]
    public async Task TypeSourceFilesPrint_SelectsExactRepositoryDocument(
        string row,
        string fileName,
        bool preferRenderedUrls)
    {
        string repositoryRoot = FindRepositoryRoot();
        var result = await RunCliAsync(
            [
                "type", typeof(ILInspector.SourceLink.SourceLinkService).FullName!,
                "--library", typeof(ILInspector.SourceLink.SourceLinkService).Assembly.Location,
                "-S", "Source Files", "--print", "--row", row,
                "--repo", repositoryRoot, "--tips", "q",
                .. preferRenderedUrls ? new[] { "--prefer-rendered-urls" } : [],
            ]);

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.Equal(
            File.ReadAllText(Path.Combine(repositoryRoot, "src", "ILInspector.SourceLink", fileName))
                .ReplaceLineEndings("\n"),
            result.Output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task TypeSourceFilesPrint_AcceptsRepoAtCliBoundaryWhileOffline()
    {
        string repositoryRoot = FindRepositoryRoot();
        var result = await RunCliAsync(
            "type",
            typeof(CommandLineBuilder).FullName!,
            "--library",
            ProductAssemblyPath(),
            "-S",
            "Source Files",
            "--print",
            "--row",
            "first",
            "--repo",
            repositoryRoot,
            "-v:n",
            "--trace-mermaid",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "public static class CommandLineBuilder",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "failed to fetch verified source",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "source-fetch",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeSourceFilesPrint_EnvironmentMarkdownFramesDocument()
    {
        string? originalFormat =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", "markdown");

            var result = await RunCliAsync(
                "type",
                typeof(CommandLineBuilder).FullName!,
                "--library",
                ProductAssemblyPath(),
                "-S",
                "Source Files",
                "--print",
                "--row",
                "first",
                "--repo",
                FindRepositoryRoot(),
                "--tips",
                "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.StartsWith("# ", result.Output);
            Assert.Contains("```csharp", result.Output);
            Assert.Contains("public static class CommandLineBuilder", result.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", originalFormat);
        }
    }

    [Fact]
    public async Task TypeSourceFilesPrint_ExplicitMarkdownFramesDocument()
    {
        var result = await RunCliAsync(
            "type",
            typeof(CommandLineBuilder).FullName!,
            "--library",
            ProductAssemblyPath(),
            "-S",
            "Source Files",
            "--print",
            "--row",
            "first",
            "--repo",
            FindRepositoryRoot(),
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.StartsWith("# ", result.Output);
        Assert.Contains("```csharp", result.Output);
        Assert.Contains(
            "public static class CommandLineBuilder",
            result.Output,
            StringComparison.Ordinal);
    }

    // PR-fast: each selected member addresses one of the real partial type's documents.
    [Theory]
    [InlineData("ResolveTypeSource:1", "SourceLinkService.cs", false)]
    [InlineData("ResolveTypeSource:1", "SourceLinkService.cs", true)]
    [InlineData("VerifySourceContent:1", "SourceLinkService.SourceContent.cs", false)]
    [InlineData("VerifySourceContent:1", "SourceLinkService.SourceContent.cs", true)]
    public async Task MemberSourceLocationsPrint_SelectsExactRepositoryDocument(
        string member,
        string fileName,
        bool preferRenderedUrls)
    {
        string repositoryRoot = FindRepositoryRoot();
        var result = await RunCliAsync(
            [
                "member", typeof(ILInspector.SourceLink.SourceLinkService).FullName!,
                member,
                "--library", typeof(ILInspector.SourceLink.SourceLinkService).Assembly.Location,
                "-S", "Source Locations", "--print", "--row", "first",
                "--repo", repositoryRoot, "--tips", "q",
                .. preferRenderedUrls ? new[] { "--prefer-rendered-urls" } : [],
            ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal(
            File.ReadAllText(Path.Combine(repositoryRoot, "src", "ILInspector.SourceLink", fileName))
                .ReplaceLineEndings("\n"),
            result.Output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task TypeSourceFilesPrint_RouterPreservesRepoAtCliBoundaryWhileOffline()
    {
        string[] arguments =
        [
            typeof(CommandLineBuilder).FullName!,
            "--library",
            ProductAssemblyPath(),
            "-S",
            "Source Files",
            "--print",
            "--row",
            "first",
            "--repo",
            FindRepositoryRoot(),
            "-v:n",
            "--tips",
            "q"
        ];

        var direct = await RunCliAsync(["type", .. arguments]);
        var deferred = await RunCliAsync(arguments);

        Assert.Equal(direct, deferred);
        Assert.Equal(0, deferred.Exit);
        Assert.Contains(
            "public static class CommandLineBuilder",
            deferred.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberSourceLocationsPrint_UsesPdbRecordedLocalPathWhileOffline()
    {
        var result = await RunCliAsync(
            "member",
            typeof(CommandLineBuilder).FullName!,
            "--library",
            ProductAssemblyPath(),
            "-m",
            nameof(CommandLineBuilder.TryGetStaleArgumentError),
            "-S",
            "Source Locations",
            "--print",
            "--row",
            "first",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Contains(
            "public static bool TryGetStaleArgumentError",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "failed to fetch verified source",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberSourceLocationsPrint_ExplicitMarkdownFramesDocument()
    {
        var result = await RunCliAsync(
            "member",
            typeof(CommandLineBuilder).FullName!,
            "--library",
            ProductAssemblyPath(),
            "-m",
            nameof(CommandLineBuilder.TryGetStaleArgumentError),
            "-S",
            "Source Locations",
            "--print",
            "--row",
            "first",
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Error);
        Assert.StartsWith("# ", result.Output);
        Assert.Contains("```csharp", result.Output);
        Assert.Contains(
            "public static bool TryGetStaleArgumentError",
            result.Output,
            StringComparison.Ordinal);
    }

    // PR-fast: one bounded offline ordinary PDB Source request against the real repository asset.
    [Fact]
    public async Task MemberPdbSource_RendersTheVerifiedDeclarationWithoutAuthoredParts()
    {
        var result = await RunCliAsync(
            "member",
            typeof(MemberTextSlicer).FullName!,
            "ExtractMemberText:1",
            "--library",
            typeof(MemberTextSlicer).Assembly.Location,
            "--repo",
            FindRepositoryRoot(),
            "-S",
            "PDB Source",
            "--tips",
            "q");

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.DoesNotContain("## PDB Source", result.Output);
        Assert.DoesNotContain("```", result.Output);
        Assert.Contains(
            "public static string? ExtractMemberText(",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "return string.Join('\\n', dedented).TrimEnd();",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "recognized <c>#line</c> directive",
            result.Output,
            StringComparison.Ordinal);
    }

    // PR-fast: explicit Source Diff retains authored-content authorization through the public CLI.
    [Fact]
    public async Task MemberSourceDiff_ExplicitSelectionExecutesTheAuthoredPipeline()
    {
        var result = await RunCliAsync(
            "member",
            typeof(MemberTextSlicer).FullName!,
            "ExtractMemberText:1",
            "--library",
            typeof(MemberTextSlicer).Assembly.Location,
            "--repo",
            FindRepositoryRoot(),
            "-S",
            "Source Diff",
            "--tips",
            "q");

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.DoesNotContain("## Source Diff", result.Output);
        Assert.DoesNotContain("```", result.Output);
        Assert.Contains("PDB source", result.Output);
        Assert.Contains("Integrity", result.Output);
        Assert.Contains("Changed lines", result.Output);
    }

    // PR-fast: bounded offline parts requests against the repository's compiled source.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberParts_MetadataAndOptInJsonKeepPdbAndLexicalRangesSeparate(bool includeParts)
    {
        var result = await RunCliAsync(
        [
            "member", typeof(MemberTextSlicer).FullName!, "ExtractMemberText:1",
            "--library", typeof(MemberTextSlicer).Assembly.Location,
            "-S", "Source Locations", "--json", "--tips", "q",
            .. includeParts ? new[] { "--source-parts", "--repo", FindRepositoryRoot() } : [],
        ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.Contains("ExtractMemberText", root.GetProperty("member").GetString());
        Assert.EndsWith("MemberTextSlicer.cs", root.GetProperty("document").GetProperty("path").GetString());
        Assert.False(root.TryGetProperty("section", out _));
        Assert.False(root.TryGetProperty("row", out _));
        Assert.False(root.TryGetProperty("content", out _));
        Assert.Equal(includeParts, root.TryGetProperty("parts", out var parts));
        if (includeParts)
        {
            Assert.True(parts.GetProperty("member").GetProperty("start_line").GetInt32()
                < root.GetProperty("pdb_span").GetProperty("start_line").GetInt32());
            Assert.True(parts.GetProperty("xml_docs").GetProperty("end_line").GetInt32()
                < parts.GetProperty("signature").GetProperty("start_line").GetInt32());
            Assert.True(parts.GetProperty("signature").GetProperty("end_line").GetInt32()
                < parts.GetProperty("body").GetProperty("start_line").GetInt32());
        }
        else
        {
            Assert.Equal(3, root.EnumerateObject().Count());
        }
    }

    [Theory]
    [InlineData("member")]
    [InlineData("xml-docs")]
    [InlineData("signature")]
    [InlineData("body")]
    public async Task MemberParts_PrintPreservesOriginalTextAndInitialIndentation(string partName)
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "CSharpText.MemberSlicing", "MemberTextSlicer.cs"));
        var index = DeclarationIndex.Build(source);
        var declaration = Assert.Single(index.Declarations, item => item.Name == "ExtractMemberText");
        var parts = Assert.IsType<MemberTextParts>(index.GetMemberTextParts(declaration));
        MemberTextPart part = partName switch
        {
            "member" => parts.Member,
            "xml-docs" => Assert.Single(parts.XmlDocumentation),
            "signature" => parts.Signature,
            "body" => Assert.IsType<MemberTextPart>(parts.Body),
            _ => throw new InvalidOperationException(),
        };
        var result = await RunCliAsync(
            "member", typeof(MemberTextSlicer).FullName!, "ExtractMemberText:1",
            "--library", typeof(MemberTextSlicer).Assembly.Location,
            "--repo", FindRepositoryRoot(), "--print", "--part", partName, "--tips", "q");

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal("    " + source.Substring(part.Start, part.Length), result.Output);
    }

    [Fact]
    public async Task MemberParts_AbsentPartFailsWithoutSubstituteText()
    {
        var result = await RunCliAsync(
            "member", typeof(MemberTextSlicer).FullName!, "ExtractMemberText:1",
            "--library", typeof(MemberTextSlicer).Assembly.Location,
            "--repo", FindRepositoryRoot(), "--print", "--part", "attributes", "--tips", "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("no 'attributes' part", result.Error);
    }

    [Fact]
    public async Task MemberParts_PropertyUsesTheLocatedAccessorAndReturnsTheProperty()
    {
        var result = await RunCliAsync(
            "member", typeof(ILInspector.SourceLink.SourceLinkService).FullName!, "HasPdb",
            "--library", typeof(ILInspector.SourceLink.SourceLinkService).Assembly.Location,
            "--repo", FindRepositoryRoot(), "--print", "--part", "member", "--json", "--tips", "q");

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.Contains("public bool HasPdb", root.GetProperty("content").GetString());
        Assert.Equal("member", root.GetProperty("part").GetString());
        Assert.False(root.TryGetProperty("section", out _));
    }

    [Fact]
    public async Task MemberParts_RouterRetainsThePartAndRepositoryOptions()
    {
        string[] arguments =
        [
            typeof(MemberTextSlicer).FullName!,
            "--library", typeof(MemberTextSlicer).Assembly.Location,
            "-m", "ExtractMemberText:1", "--repo", FindRepositoryRoot(),
            "--print", "--part", "signature", "--tips", "q",
        ];
        var direct = await RunCliAsync(["member", .. arguments]);
        var deferred = await RunCliAsync(arguments);

        Assert.True(direct.Exit == 0, direct.Error);
        Assert.Equal(direct, deferred);
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--jsonl")]
    [InlineData("--json-array")]
    public async Task MemberParts_StructuredPrintKeepsTheSelectedText(string format)
    {
        var result = await RunCliAsync(
            "member", typeof(MemberTextSlicer).FullName!, "ExtractMemberText:1",
            "--library", typeof(MemberTextSlicer).Assembly.Location,
            "--print", "--part", "signature", format, "--tips", "q");

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        using var json = JsonDocument.Parse(result.Output);
        var item = format == "--json-array" ? json.RootElement[0] : json.RootElement;
        Assert.StartsWith("public static string? ExtractMemberText(", item.GetProperty("content").GetString());
        Assert.Equal("signature", item.GetProperty("part").GetString());
        if (format == "--jsonl")
            Assert.Single(result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData("--table")]
    [InlineData("--jsonl")]
    [InlineData("--json-array")]
    public async Task MemberParts_DiscoveryRejectsUnsupportedDocumentFormats(string format)
    {
        var result = await RunCliAsync(
            "member", typeof(MemberTextSlicer).FullName!, "ExtractMemberText:1",
            "--library", typeof(MemberTextSlicer).Assembly.Location,
            "--source-parts", format, "--tips", "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.NotEmpty(result.Error);
    }

    // PR-fast: incompatible sections are rejected before clone or source acquisition.
    [Theory]
    [InlineData("Clone Candidates", false)]
    [InlineData("Clone Candidates", true)]
    [InlineData("Clone*", false)]
    [InlineData("clone candidates", true)]
    [InlineData("Facts", false)]
    [InlineData("Facts", true)]
    public async Task MemberParts_RejectsIncompatibleResolvedSections(string section, bool print)
    {
        var result = await RunCliAsync(
        [
            "member", typeof(MemberTextSlicer).FullName!, "ExtractMemberText:1",
            "--library", typeof(MemberTextSlicer).Assembly.Location,
            "-S", section, "--json", "--tips", "q",
            .. print ? new[] { "--print", "--part", "signature" } : new[] { "--source-parts" },
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Authored member parts require Source Locations as the only selected section.",
            result.Error);
    }

    [Fact]
    public async Task MemberParts_RenderedUrlPreferenceDoesNotChangeTheSelectedText()
    {
        string[] arguments =
        [
            "member", typeof(MemberTextSlicer).FullName!, "ExtractMemberText:1",
            "--library", typeof(MemberTextSlicer).Assembly.Location,
            "--print", "--part", "xml-docs", "--json", "--tips", "q",
        ];
        var raw = await RunCliAsync(arguments);
        var rendered = await RunCliAsync([.. arguments, "--prefer-rendered-urls"]);

        Assert.True(raw.Exit == 0, raw.Error);
        Assert.True(rendered.Exit == 0, rendered.Error);
        using var rawJson = JsonDocument.Parse(raw.Output);
        using var renderedJson = JsonDocument.Parse(rendered.Output);
        Assert.Equal(rawJson.RootElement.GetProperty("content").GetString(),
            renderedJson.RootElement.GetProperty("content").GetString());
        Assert.StartsWith("https://raw.githubusercontent.com/",
            rawJson.RootElement.GetProperty("document").GetProperty("url").GetString());
        Assert.StartsWith("https://github.com/",
            renderedJson.RootElement.GetProperty("document").GetProperty("url").GetString());
    }

    [Fact]
    public async Task MemberLocations_MetadataJsonHonorsTheMemberRowWindow()
    {
        string[] arguments =
        [
            "member", typeof(ILInspector.SourceLink.SourceLinkService).FullName!,
            "--library", typeof(ILInspector.SourceLink.SourceLinkService).Assembly.Location,
            "-m", "Get*", "-S", "Source Locations", "--json", "--tips", "q",
        ];
        var all = await RunCliAsync(arguments);
        var selected = await RunCliAsync([.. arguments, "--rows", "2..2"]);

        Assert.True(all.Exit == 0, all.Error);
        Assert.True(selected.Exit == 0, selected.Error);
        using var allJson = JsonDocument.Parse(all.Output);
        using var selectedJson = JsonDocument.Parse(selected.Output);
        Assert.Equal(allJson.RootElement[1].GetProperty("member").GetString(),
            selectedJson.RootElement.GetProperty("member").GetString());
    }

    async Task<(int Exit, string Output, string Error)> RunCliAsync(
        params string[] arguments)
    {
        string executable = Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "dotnet-inspect.exe" : "dotnet-inspect");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["DOTNET_INSPECT_OFFLINE"] = "1";
        startInfo.Environment["DOTNET_INSPECT_CACHE_DIR"] = _cacheDirectory;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {executable}.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(
            TestContext.Current.CancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(
            TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (
            !TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            OutOfProcessCliProcess.KillAndWaitForExit(
                process,
                TimeSpan.FromSeconds(10));
            throw new TimeoutException($"{executable} did not exit.");
        }

        string output = await standardOutput;
        string error = await standardError;
        return (process.ExitCode, output, error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDirectory))
            Directory.Delete(_cacheDirectory, recursive: true);
    }

    static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root from '{AppContext.BaseDirectory}'.");
    }

    static string ProductAssemblyPath()
        => Path.Combine(AppContext.BaseDirectory, "dotnet-inspect.dll");
}
