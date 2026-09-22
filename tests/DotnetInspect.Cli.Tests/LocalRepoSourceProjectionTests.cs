using System.Diagnostics;
using System.Text.Json;
using CSharpText;
using CSharpText.MemberSlicing;

using DotnetInspect.Cli.Sections;
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

    // Full-type source measures over two seconds; daily and focused gates own this.
    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(true, null, false)]
    [InlineData(true, "--markdown", false)]
    [InlineData(true, "-v:q", false)]
    [InlineData(true, "--plaintext", false)]
    [InlineData(false, null, false)]
    [InlineData(false, "-v:q", false)]
    [InlineData(true, null, true)]
    [InlineData(false, "-v:q", true)]
    public async Task TypeSourcePrint_TreeDoesNotRequestMarkdown(
        bool tree,
        string? format,
        bool commandless)
    {
        var result = await RunCliAsync(
        [
            .. commandless ? Array.Empty<string>() : ["type"],
            typeof(JsonNamingPolicy).FullName!,
            "--library", typeof(JsonNamingPolicy).Assembly.Location,
            "-S", "Decompiled Source", "--print", "--tips", "q",
            .. tree ? new[] { "--tree" } : [],
            .. format is not null ? new[] { format } : [],
        ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Contains("class JsonNamingPolicy", result.Output);
        if (format is "--markdown" or "-v:q")
        {
            Assert.StartsWith("# Decompiled Source", result.Output);
            Assert.Contains("```csharp", result.Output);
        }
        else
        {
            Assert.DoesNotContain("# Decompiled Source", result.Output);
            Assert.DoesNotContain("```", result.Output);
        }
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

    // PR-fast: one offline document request against the small member-slicing assembly.
    [Fact]
    public async Task MemberSourceLocationsPrint_ExplicitVerbosityFramesDocument()
    {
        var result = await RunCliAsync(
            "member", typeof(MemberTextSlicer).FullName!, "ExtractMemberText:1",
            "--library", typeof(MemberTextSlicer).Assembly.Location,
            "--repo", FindRepositoryRoot(), "-S", "Source Locations",
            "--print", "-v:q", "--tips", "q");

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.StartsWith("# ", result.Output);
        Assert.Contains("```csharp", result.Output);
        Assert.Contains("public static class MemberTextSlicer", result.Output);
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

    [Fact]
    public async Task MemberSource_PrefersVerifiedAuthoredDeclaration()
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
            SectionNames.Source,
            "--tips",
            "q");

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            "public static string? ExtractMemberText(",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("```", result.Output);
        Assert.Contains("Source provider: pdb.", result.Error);
        Assert.Contains(
            "Source provenance: PDB-checksum-verified authored source",
            result.Error);
        Assert.DoesNotContain("fetched through SourceLink", result.Error);
        Assert.Contains("Source location:", result.Error);
        Assert.DoesNotContain("decompiled fallback", result.Error);
    }

    [Fact]
    public async Task TypeSource_PrefersIssuedRepositoryDocument()
    {
        var result = await RunCliAsync(
            "type",
            typeof(MemberTextSlicer).FullName!,
            "--library",
            typeof(MemberTextSlicer).Assembly.Location,
            "--repo",
            FindRepositoryRoot(),
            "-S",
            SectionNames.Source,
            "--tips",
            "q");

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            "public static class MemberTextSlicer",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("Source provider: pdb.", result.Error);
        Assert.Contains("Type source evidence:", result.Error);
        Assert.Contains("primary_type_document", result.Error);
    }

    [Fact]
    public async Task MemberSource_FallsBackToExactDecompilerOutput()
    {
        var result = await RunCliAsync(
            "member",
            typeof(JsonElement).FullName!,
            "GetArrayLength:1",
            "--library",
            typeof(JsonElement).Assembly.Location,
            "-S",
            SectionNames.Source,
            "--tips",
            "q");

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains("public int GetArrayLength()", result.Output);
        Assert.DoesNotContain("class JsonElement", result.Output);
        Assert.Contains("Source provider: decompiled.", result.Error);
        Assert.Contains(
            "Authored source unavailable; using decompiled fallback:",
            result.Error);
        Assert.DoesNotContain("Unavailable:", result.Error);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TypeSource_FallsBackToCompleteExactType()
    {
        string[] arguments =
        [
            "type",
            typeof(JsonNamingPolicy).FullName!,
            "--library",
            typeof(JsonNamingPolicy).Assembly.Location,
            "-S",
            SectionNames.Source,
            "--tips",
            "q",
        ];
        var result = await RunCliAsync(arguments);
        var all = await RunCliAsync([.. arguments, "--all"]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.True(all.Exit == 0, all.Error);
        Assert.Equal(result.Output, all.Output);
        Assert.Contains("public abstract class JsonNamingPolicy", result.Output);
        Assert.Contains("public static JsonNamingPolicy CamelCase", result.Output);
        Assert.Contains("Source provider: decompiled.", result.Error);
        Assert.Contains(
            "Authored source unavailable; using decompiled fallback:",
            result.Error);
    }

    [Fact]
    public async Task MemberSource_DirectJsonUsesFocusedTypedShape()
    {
        var result = await RunCliAsync(
            "member",
            typeof(JsonElement).FullName!,
            "GetArrayLength:1",
            "--library",
            typeof(JsonElement).Assembly.Location,
            "-S",
            SectionNames.Source,
            "--json",
            "--tips",
            "q");

        Assert.True(result.Exit == 0, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement;
        Assert.Equal("decompiled", root.GetProperty("provider").GetString());
        Assert.Contains(
            "dotnet-inspect decompilation",
            root.GetProperty("provenance").GetString());
        Assert.Contains(
            "portable PDB",
            root.GetProperty("fallback_reason").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "public int GetArrayLength()",
            root.GetProperty("content").GetString());
        Assert.False(root.TryGetProperty("location", out _));
        Assert.False(root.TryGetProperty("type_evidence", out _));
        Assert.Equal(4, root.EnumerateObject().Count());
    }

    [Fact]
    public async Task MemberSource_PrintUsesPrintableDocumentShape()
    {
        var result = await RunCliAsync(
            "member",
            typeof(JsonElement).FullName!,
            "GetArrayLength:1",
            "--library",
            typeof(JsonElement).Assembly.Location,
            "-S",
            SectionNames.Source,
            "--print",
            "--json",
            "--tips",
            "q");

        Assert.True(result.Exit == 0, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("row").GetInt32());
        Assert.Equal(SectionNames.Source, root.GetProperty("section").GetString());
        Assert.Equal(SectionNames.Source, root.GetProperty("label").GetString());
        Assert.Contains(
            "public int GetArrayLength()",
            root.GetProperty("content").GetString());
        Assert.False(root.TryGetProperty("provider", out _));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task MemberSource_PrintHonorsRowAndRenderedLineBoundaries()
    {
        string[] arguments =
        [
            "member",
            typeof(JsonElement).FullName!,
            "GetArrayLength:1",
            "--library",
            typeof(JsonElement).Assembly.Location,
            "-S",
            SectionNames.Source,
            "--print",
            "--tips",
            "q",
        ];

        var limited = await RunCliAsync(
            [.. arguments, "-n", "2", "--lines"]);
        var missing = await RunCliAsync(
            [.. arguments, "--row", "2"]);

        Assert.True(limited.Exit == 0, limited.Error);
        Assert.Equal(
            2,
            limited.Output.TrimEnd('\r', '\n').Split('\n').Length);
        Assert.Equal(1, missing.Exit);
        Assert.Empty(missing.Output);
        Assert.Contains("row 2 is not in this section", missing.Error);
    }

    [Fact]
    public async Task Source_CoSelectedProviderSectionsRetainTheirIdentities()
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
            "Source,PDB Source,Decompiled Source",
            "--markdown",
            "--tips",
            "q");

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains("## Source", result.Output);
        Assert.Contains("## PDB Source", result.Output);
        Assert.Contains("## Decompiled Source", result.Output);
        Assert.Contains("Source provider: pdb.", result.Error);
    }

    [Fact]
    public async Task MemberSource_UnsupportedFieldFailsWithoutEmptySuccess()
    {
        var result = await RunCliAsync(
            "member",
            typeof(JsonElement).FullName!,
            "_parent:1",
            "--library",
            typeof(JsonElement).Assembly.Location,
            "--all",
            "-S",
            SectionNames.Source,
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("physical MethodDef token", result.Error);
    }

    [Fact]
    public async Task MemberSource_BodylessMethodUsesExactDecompilerDeclaration()
    {
        var result = await RunCliAsync(
            "member",
            typeof(JsonNamingPolicy).FullName!,
            "ConvertName:1",
            "--library",
            typeof(JsonNamingPolicy).Assembly.Location,
            "-S",
            SectionNames.Source,
            "--tips",
            "q");

        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains(
            "public abstract string ConvertName(string name);",
            result.Output);
        Assert.Contains("Source provider: decompiled.", result.Error);
    }

    [Fact]
    public async Task TypeSource_DirectJsonPreservesDocumentEvidence()
    {
        var result = await RunCliAsync(
            "type",
            typeof(MemberTextSlicer).FullName!,
            "--library",
            typeof(MemberTextSlicer).Assembly.Location,
            "--repo",
            FindRepositoryRoot(),
            "-S",
            SectionNames.Source,
            "--json",
            "--tips",
            "q");

        Assert.True(result.Exit == 0, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        JsonElement root = document.RootElement;
        Assert.Equal("pdb", root.GetProperty("provider").GetString());
        Assert.EndsWith(
            "MemberTextSlicer.cs",
            root.GetProperty("location").GetString());
        JsonElement evidence = root.GetProperty("type_evidence");
        Assert.Equal(
            "primary_type_document",
            evidence.GetProperty("scope").GetString());
        Assert.Equal(
            "correlated_type_document",
            evidence.GetProperty("mapping_strength").GetString());
        Assert.False(evidence.GetProperty("is_partial").GetBoolean());
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TypeSource_PrintTreeStaysNativeUnlessMarkdownIsExplicit()
    {
        string[] arguments =
        [
            "type",
            typeof(JsonNamingPolicy).FullName!,
            "--library",
            typeof(JsonNamingPolicy).Assembly.Location,
            "-S",
            SectionNames.Source,
            "--print",
            "--tree",
            "--tips",
            "q",
        ];

        var native = await RunCliAsync(arguments);
        var markdown = await RunCliAsync([.. arguments, "--markdown"]);
        var quiet = await RunCliAsync([.. arguments, "-v:q"]);

        Assert.True(native.Exit == 0, native.Error);
        Assert.DoesNotContain("```", native.Output);
        Assert.DoesNotContain("# Source", native.Output);
        Assert.Contains("class JsonNamingPolicy", native.Output);
        Assert.True(markdown.Exit == 0, markdown.Error);
        Assert.StartsWith("# Source", markdown.Output);
        Assert.Contains("```csharp", markdown.Output);
        Assert.True(quiet.Exit == 0, quiet.Error);
        Assert.StartsWith("# Source", quiet.Output);
        Assert.Contains("```csharp", quiet.Output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task MemberSource_EnvironmentMarkdownFramesPrintedDocument()
    {
        string? originalFormat =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                "markdown");
            var result = await RunCliAsync(
                "member",
                typeof(JsonElement).FullName!,
                "GetArrayLength:1",
                "--library",
                typeof(JsonElement).Assembly.Location,
                "-S",
                SectionNames.Source,
                "--print",
                "--tips",
                "q");

            Assert.True(result.Exit == 0, result.Error);
            Assert.StartsWith("# Source", result.Output);
            Assert.Contains("```csharp", result.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                originalFormat);
        }
    }

    [Fact]
    public async Task DefaultTypeListingDoesNotAcquireSource()
    {
        var result = await RunCliAsync(
            "type",
            typeof(JsonNamingPolicy).FullName!,
            "--library",
            typeof(JsonNamingPolicy).Assembly.Location,
            "--tips",
            "q");

        Assert.Equal(0, result.Exit);
        Assert.DoesNotContain("Source provider:", result.Error);
        Assert.DoesNotContain("Authored source unavailable", result.Error);
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
    [InlineData("member", false)]
    [InlineData("member", true)]
    [InlineData("xml-docs", false)]
    [InlineData("xml-docs", true)]
    [InlineData("signature", false)]
    [InlineData("signature", true)]
    [InlineData("body", false)]
    [InlineData("body", true)]
    public async Task MemberParts_PrintPreservesOriginalTextAndInitialIndentation(
        string partName,
        bool markdown)
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
        [
            "member", typeof(MemberTextSlicer).FullName!, "ExtractMemberText:1",
            "--library", typeof(MemberTextSlicer).Assembly.Location,
            "--repo", FindRepositoryRoot(), "--print", "--part", partName, "--tips", "q",
            .. markdown ? new[] { "--markdown" } : [],
        ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        string expected = "    " + source.Substring(part.Start, part.Length);
        if (markdown)
        {
            string output = result.Output.ReplaceLineEndings("\n");
            Assert.StartsWith("# ", output);
            Assert.Contains($"({partName})", output);
            const string openingFence = "```csharp\n";
            int start = output.IndexOf(openingFence, StringComparison.Ordinal);
            Assert.True(start >= 0, output);
            Assert.EndsWith("\n```\n", output);
            string content = output[(start + openingFence.Length)..^5];
            Assert.Equal(
                expected.ReplaceLineEndings("\n").TrimEnd('\n'),
                content.TrimEnd('\n'));
        }
        else
        {
            Assert.Equal(expected, result.Output);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberParts_AbsentPartFailsWithoutSubstituteText(bool markdown)
    {
        var result = await RunCliAsync(
        [
            "member", typeof(MemberTextSlicer).FullName!, "ExtractMemberText:1",
            "--library", typeof(MemberTextSlicer).Assembly.Location,
            "--repo", FindRepositoryRoot(), "--print", "--part", "attributes", "--tips", "q",
            .. markdown ? new[] { "--markdown" } : [],
        ]);

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("no 'attributes' part", result.Error);
    }

    // PR-fast: one offline part request; explicit flags override the environment format.
    [Theory]
    [InlineData(null)]
    [InlineData("-v:q")]
    [InlineData("--plaintext")]
    [InlineData("--json")]
    [InlineData("--jsonl")]
    [InlineData("--json-array")]
    public async Task MemberParts_EnvironmentMarkdownRespectsExplicitFormats(string? format)
    {
        string? originalFormat =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", "markdown");
            var result = await RunCliAsync(
            [
                "member", typeof(MemberTextSlicer).FullName!, "ExtractMemberText:1",
                "--library", typeof(MemberTextSlicer).Assembly.Location,
                "--repo", FindRepositoryRoot(),
                "--print", "--part", "signature", "--tips", "q",
                .. format is not null ? new[] { format } : [],
            ]);

            Assert.True(result.Exit == 0, result.Error);
            Assert.Empty(result.Error);
            if (format is null or "-v:q")
            {
                Assert.StartsWith("# ", result.Output);
                Assert.Contains("```csharp", result.Output);
                Assert.Contains("    public static string? ExtractMemberText(", result.Output);
            }
            else if (format == "--plaintext")
            {
                Assert.StartsWith("    public static string? ExtractMemberText(", result.Output);
                Assert.DoesNotContain("```", result.Output);
            }
            else
            {
                using var json = JsonDocument.Parse(result.Output);
                var item = format == "--json-array"
                    ? Assert.Single(json.RootElement.EnumerateArray())
                    : json.RootElement;
                Assert.Equal("signature", item.GetProperty("part").GetString());
                Assert.StartsWith("public static string? ExtractMemberText(",
                    item.GetProperty("content").GetString());
                Assert.True(item.TryGetProperty("document", out _));
                Assert.True(item.TryGetProperty("pdb_span", out _));
                Assert.False(item.TryGetProperty("row", out _));
                Assert.False(item.TryGetProperty("section", out _));
                if (format == "--jsonl")
                    Assert.Single(result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", originalFormat);
        }
    }

    // PR-fast: one offline part request for each supported verbosity/format choice.
    [Theory]
    [InlineData("-v:q", null)]
    [InlineData("-v:m", null)]
    [InlineData("-v:n", null)]
    [InlineData("-v:d", null)]
    [InlineData("-v:q", "--plaintext")]
    [InlineData("-v:q", "--json")]
    public async Task MemberParts_ExplicitVerbosityHonorsResolvedFormat(
        string verbosity,
        string? format)
    {
        var result = await RunCliAsync(
        [
            "member", typeof(MemberTextSlicer).FullName!, "ExtractMemberText:1",
            "--library", typeof(MemberTextSlicer).Assembly.Location,
            "--repo", FindRepositoryRoot(), "--print", "--part", "signature",
            verbosity, "--tips", "q",
            .. format is not null ? new[] { format } : [],
        ]);

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        if (format == "--json")
        {
            using var json = JsonDocument.Parse(result.Output);
            Assert.Equal("signature", json.RootElement.GetProperty("part").GetString());
            Assert.StartsWith("public static string? ExtractMemberText(",
                json.RootElement.GetProperty("content").GetString());
            Assert.False(json.RootElement.TryGetProperty("row", out _));
        }
        else if (format == "--plaintext")
        {
            Assert.StartsWith("    public static string? ExtractMemberText(", result.Output);
            Assert.DoesNotContain("```", result.Output);
        }
        else
        {
            Assert.StartsWith("# ", result.Output);
            Assert.Contains("(signature)", result.Output);
            Assert.Contains("```csharp", result.Output);
            Assert.Contains("    public static string? ExtractMemberText(", result.Output);
        }
    }

    [Fact]
    public async Task MemberParts_MarkdownUsesTheAlreadySelectedMemberRow()
    {
        string[] arguments =
        [
            "member", typeof(ILInspector.SourceLink.SourceLinkService).FullName!,
            "--library", typeof(ILInspector.SourceLink.SourceLinkService).Assembly.Location,
            "-m", "Get*", "--repo", FindRepositoryRoot(),
            "--print", "--part", "signature", "--row", "2", "--tips", "q",
        ];
        var structured = await RunCliAsync([.. arguments, "--json"]);
        var markdown = await RunCliAsync([.. arguments, "--markdown"]);

        Assert.True(structured.Exit == 0, structured.Error);
        Assert.True(markdown.Exit == 0, markdown.Error);
        Assert.Empty(structured.Error);
        Assert.Empty(markdown.Error);
        using var json = JsonDocument.Parse(structured.Output);
        Assert.StartsWith("# ", markdown.Output);
        Assert.Contains("```csharp", markdown.Output);
        Assert.Contains(json.RootElement.GetProperty("member").GetString()!, markdown.Output);
        Assert.Contains(json.RootElement.GetProperty("content").GetString()!, markdown.Output);
    }

    // PR-fast: one bounded rendered output, not a semantic row limit.
    [Fact]
    public async Task MemberParts_MarkdownHonorsTheRenderedLineLimit()
    {
        var result = await RunCliAsync(
            "member", typeof(MemberTextSlicer).FullName!, "ExtractMemberText:1",
            "--library", typeof(MemberTextSlicer).Assembly.Location,
            "--repo", FindRepositoryRoot(), "--print", "--part", "member",
            "--markdown", "--lines", "-n", "3", "--tips", "q");

        Assert.True(result.Exit == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.StartsWith("# ", result.Output);
        Assert.Equal(3, result.Output.TrimEnd('\r', '\n').Split('\n').Length);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberParts_RouterRetainsThePartAndRepositoryOptions(bool markdown)
    {
        string[] arguments =
        [
            typeof(MemberTextSlicer).FullName!,
            "--library", typeof(MemberTextSlicer).Assembly.Location,
            "-m", "ExtractMemberText:1", "--repo", FindRepositoryRoot(),
            "--print", "--part", "signature", "--tips", "q",
            .. markdown ? new[] { "--markdown" } : [],
        ];
        var direct = await RunCliAsync(["member", .. arguments]);
        var deferred = await RunCliAsync(arguments);

        Assert.True(direct.Exit == 0, direct.Error);
        Assert.Equal(direct, deferred);
        if (markdown)
            Assert.Contains("```csharp", direct.Output);
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
