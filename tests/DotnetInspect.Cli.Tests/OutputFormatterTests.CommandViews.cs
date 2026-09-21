using DotnetInspect.Cli.Models;
using System.Reflection;
using System.Text.Json;
using DotnetInspect.Cli.Views;
using DotnetInspect.Cli;
using DotnetInspect.Cli.Commands;
using ILInspector.Analysis;
using Inspector.Findings;
using ILInspector.Metadata;
using InertText;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using Markout;

namespace DotnetInspect.Cli.Tests;

public partial class OutputFormatterTests
{
    [Fact]
    public void ApiFullSurface_QuietMode_SuppressesTypeTables()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions { Verbosity = Verbosity.Quiet };

        var output = RenderFullApi(api, options);

        Assert.Contains("Source: NuGet", output);
        Assert.DoesNotContain("## Classes", output);
        Assert.DoesNotContain("Type1", output);
    }

    [Fact]
    public void ApiFullSurface_MinimalMode_ShowsTypeTables()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions { Verbosity = Verbosity.Minimal };

        var output = RenderFullApi(api, options);

        Assert.Contains("## Classes", output);
        Assert.Contains("TestLib.Type1", output);
    }

    [Fact]
    public void ApiFullSurface_QuietWithTypeFilter_ShowsTypeTables()
    {
        var api = CreateTestApiSurface();
        // Glob upgrade: quiet + TypeFilter should behave as minimal
        var options = new TypeOptions
        {
            Verbosity = Verbosity.Minimal,  // caller upgrades quiet to minimal for globs
            TypeFilter = "Type1*"
        };

        var output = RenderFullApi(api, options);

        Assert.Contains("## Classes", output);
        Assert.Contains("TestLib.Type1", output);
    }

    [Fact]
    public void ApiFullSurface_SourceAndTfm_PresentInCompactLine()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions { Verbosity = Verbosity.Quiet };

        var output = RenderFullApi(api, options);

        Assert.Contains("Source: NuGet", output);
        Assert.Contains("TFM: net10.0", output);
        Assert.Contains("Version: 1.0.0", output);
    }

    [Fact]
    public void TypeView_SourceAndTfm_PresentInCompactLine()
    {
        var type = new ApiType
        {
            Namespace = "TestLib",
            Name = "MyClass",
            Kind = "class",
            Members = [new ApiMember { Name = "Run", Kind = "method", Signature = "void Run()" }]
        };
        var options = new ApiOptions { Verbosity = Verbosity.Quiet };

        var view = ApiOutputFormatter.BuildTypeView(type, "TestLib", "TestLib", "1.0.0", "NuGet", "net10.0", options);
        var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
        var writer = new MarkoutWriter(new MarkdownFormatter(), writerOptions);
        ApiViewContext.Default.Serialize(view, writer);
        var output = writer.ToString().TrimEnd();

        Assert.Contains("Source: NuGet", output);
        Assert.Contains("TFM: net10.0", output);
    }

    [Fact]
    public void TypeView_NullSource_OmitsSourceField()
    {
        var type = new ApiType
        {
            Namespace = "TestLib",
            Name = "MyClass",
            Kind = "class",
            Members = []
        };
        var options = new ApiOptions { Verbosity = Verbosity.Minimal };

        var view = ApiOutputFormatter.BuildTypeView(type, "TestLib", null, null, null, null, options);
        var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);
        var writer = new MarkoutWriter(new MarkdownFormatter(), writerOptions);
        ApiViewContext.Default.Serialize(view, writer);
        var output = writer.ToString().TrimEnd();

        Assert.DoesNotContain("Source:", output);
        Assert.DoesNotContain("TFM:", output);
    }

    [Fact]
    public void ApiTypeWriterOptions_IncludeFieldsProjection()
    {
        var type = new ApiType
        {
            Namespace = "TestLib",
            Name = "MyClass",
            Kind = "class"
        };
        var options = new ApiOptions
        {
            Columns = ["Name"],
            Fields = ["Title"]
        };

        var writerOptions = ApiOutputFormatter.BuildTypeWriterOptions(type, options);

        Assert.NotNull(writerOptions.Projection);
        Assert.Equal(["Name"], writerOptions.Projection!.IncludeColumns);
        Assert.Equal(["Title"], writerOptions.Projection!.IncludeFields);
    }

    [Fact]
    public void ApiSurfaceWriterOptions_IncludeFieldsProjection()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions
        {
            Columns = ["Name"],
            Fields = ["Title"]
        };

        var writerOptions = ApiOutputFormatter.BuildWriterOptions(api, options);

        Assert.NotNull(writerOptions.Projection);
        Assert.Equal(["Name"], writerOptions.Projection!.IncludeColumns);
        Assert.Equal(["Title"], writerOptions.Projection!.IncludeFields);
    }

    [Fact]
    public void PackageSignature_FieldsAreAlphabetical()
    {
        var result = new InspectionResult
        {
            PackageName = "Test.Package",
            Version = "1.0.0",
            SignatureResult = new SignatureVerificationResult
            {
                AuthorVerified = true,
                Publisher = "Example Publisher",
                Repository = "nuget.org",
                RepositoryVerified = true,
                StatusMessage = "Valid"
            }
        };

        var output = OutputFormatter.FormatResult(result, new InspectionOptions
        {
            IncludeSections = [PackageSections.Signature]
        }, PackageSectionDescriptors.CreatePipeline());

        Assert.True(output.IndexOf("| Author Verified |", StringComparison.Ordinal)
            < output.IndexOf("| Publisher |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Repository |", StringComparison.Ordinal)
            < output.IndexOf("| Repository Verified |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Signed |", StringComparison.Ordinal)
            < output.IndexOf("| Status |", StringComparison.Ordinal));
    }

    // ===== Quiet Output Tests =====

    [Fact]
    public void LibraryQuiet_ThreeLines()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        var pipeline = LibrarySections.CreatePipeline();
        var includeSections = pipeline.ComputeIncludeSections(
            inspection, Verbosity.Quiet);
        var output = SerializeWithInclude(inspection, includeSections, topFieldsOnly: true);
        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.None);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("# ", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Contains("Name: ", lines[2]);
        Assert.Contains(" | ", lines[2]);
        Assert.DoesNotContain("## ", output);
    }

    [Fact]
    public void LibrarySelectedSection_OmitsCompactContext()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.Source = "NuGet";
        inspection.PlatformVersion = "1.2.3";
        inspection.AuditSignals =
        [
            new AuditSignal("Provenance", "SourceLink", "Present", "PDB")
        ];
        var output = SerializeWithInclude(
            inspection,
            includeSections: ["Signals"],
            topFieldsOnly: false);

        Assert.StartsWith("# Test.dll (net9.0)", output.TrimStart());
        Assert.DoesNotContain("Name: Test", output);
        Assert.DoesNotContain("Version: 1.2.3", output);
        Assert.DoesNotContain("Source: NuGet", output);
        Assert.Contains("## Signals", output);
    }

    [Fact]
    public void LibrarySelectedSection_FormatterOmitsCompactContext()
    {
        var options = new LibraryOptions
        {
            Verbosity = Verbosity.Minimal,
            IncludeSections = ["Signals"],
            Format = OutputFormat.Markdown
        };

        Assert.False(OutputFormatter.ShouldRenderLibraryContext(options));
    }

    [Fact]
    public void PackageQuiet_ThreeLines()
    {
        var result = CreateTestPackageResult();
        var view = new InspectionResultView(result);
        var output = MarkoutSerializer.Serialize(view, InspectionContext.Default, new MarkoutWriterOptions
        {
            IncludeSections = [PackageSections.Summary],
            IncludeDescription = false
        }).TrimEnd();
        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.None);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("# ", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Contains(" | ", lines[2]);
        Assert.DoesNotContain("## ", output);
    }

    [Fact]
    public void PackageDefaultOutput_OmitsTitleVersion()
    {
        var result = CreateTestPackageResult();
        var options = new InspectionOptions { Verbosity = Verbosity.Minimal };

        var output = OutputFormatter.FormatResult(result, options, PackageSectionDescriptors.CreatePipeline());

        Assert.StartsWith("# TestPackage", output.TrimStart());
        Assert.DoesNotContain("# TestPackage (1.0.0)", output);
        Assert.Contains("Version", output);
    }

    [Fact]
    public void PackageSelectedSection_OmitsCompactContextAndDescription()
    {
        var result = CreateTestPackageResult();
        result.Description = new InertText.InertString(
            InertText.TextPolicy.Prose,
            "Package description that should only appear in default views.");
        result.Source = "NuGet";
        result.AuditSignals =
        [
            new AuditSignal("NuGet", "Known vulnerabilities", "0", "NuGet advisory data")
        ];
        var options = new InspectionOptions
        {
            Verbosity = Verbosity.Minimal,
            IncludeSections = [PackageSections.Signals]
        };

        var output = OutputFormatter.FormatResult(result, options, PackageSectionDescriptors.CreatePipeline());

        Assert.StartsWith("# TestPackage", output.TrimStart());
        Assert.DoesNotContain("# TestPackage (1.0.0)", output);
        Assert.DoesNotContain("Version: 1.0.0", output);
        Assert.DoesNotContain("Source: NuGet", output);
        Assert.DoesNotContain(result.Description.Value.ToString(), output);
        Assert.Contains("## Signals", output);
    }

    [Fact]
    public async Task PackageArtifactTextAudit_ListsLocationsAndKindsInMarkdownAndJsonl()
    {
        const string secret = "DO-NOT-REPORT";
        var result = new InspectionResult
        {
            PackageName = "TestPackage",
            Version = "1.0.0",
            Owners = [$"owner\u202E{secret}"],
            PackageFiles = [new PackageFile($"file\u001B{secret}", 42)],
            AuditSignals =
            [
                new AuditSignal(
                    "Text",
                    "Artifact text containment",
                    "Required",
                    "control (Cc), format/bidi (Cf)"),
            ],
        };
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var options = new InspectionOptions
        {
            Verbosity = Verbosity.Minimal,
            IncludeSections =
            [
                PackageSections.Signals,
                PackageSections.AuditArtifactText,
            ],
        };

        string markdown = OutputFormatter.FormatResult(result, options, pipeline);

        Assert.Contains("## Signals", markdown, StringComparison.Ordinal);
        Assert.Contains("## Audit: Artifact Text", markdown, StringComparison.Ordinal);
        Assert.Contains("| Owners[0] | format/bidi (Cf) |", markdown, StringComparison.Ordinal);
        Assert.Contains("| PackageFiles[0].Path | control (Cc) |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, markdown, StringComparison.Ordinal);

        var (jsonl, error) = await ConsoleCapture.RunAsync(() =>
            OutputFormatter.WritePackageTable(
                Console.Out,
                result,
                options with
                {
                    IncludeSections = [PackageSections.AuditArtifactText],
                    Jsonl = true,
                    Tabular = true,
                },
                pipeline,
                showHeader: true));

        Assert.Equal(string.Empty, error);
        string[] lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        using JsonDocument owner = JsonDocument.Parse(lines[0]);
        using JsonDocument file = JsonDocument.Parse(lines[1]);
        Assert.Equal("Owners[0]", owner.RootElement.GetProperty("location").GetString());
        Assert.Equal("format/bidi (Cf)", owner.RootElement.GetProperty("concerns").GetString());
        Assert.Equal("PackageFiles[0].Path", file.RootElement.GetProperty("location").GetString());
        Assert.Equal("control (Cc)", file.RootElement.GetProperty("concerns").GetString());
        Assert.DoesNotContain(secret, jsonl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageIdentifierConfusionAudit_ListsClassificationWithoutIdentifierContent()
    {
        const string secret = "DO-NOT-REPORT";
        var result = new InspectionResult
        {
            PackageName = "TestPackage",
            Version = "1.0.0",
            DependencyGroups =
            [
                new DependencyGroup
                {
                    TargetFramework = "net11.0",
                    Dependencies = [new PackageDependency { Id = $"Ѕystem.{secret}" }],
                },
            ],
        };
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var options = new InspectionOptions
        {
            Verbosity = Verbosity.Minimal,
            IncludeSections = [PackageSections.AuditIdentifierConfusion],
        };

        string markdown = OutputFormatter.FormatResult(result, options, pipeline);

        Assert.Contains("## Audit: Identifier Confusion", markdown, StringComparison.Ordinal);
        Assert.Contains("DependencyGroups[0].Dependencies[0].Id", markdown, StringComparison.Ordinal);
        Assert.Contains("Package ID", markdown, StringComparison.Ordinal);
        Assert.Contains(
            "non-ASCII characters; reserved-prefix homoglyph",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains("System", markdown, StringComparison.Ordinal);
        Assert.Contains("83%", markdown, StringComparison.Ordinal);
        Assert.Contains("U+0405→S", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, markdown, StringComparison.Ordinal);

        var (jsonl, error) = await ConsoleCapture.RunAsync(() =>
            OutputFormatter.WritePackageTable(
                Console.Out,
                result,
                options with { Jsonl = true, Tabular = true },
                pipeline,
                showHeader: true));

        Assert.Equal(string.Empty, error);
        using JsonDocument row = JsonDocument.Parse(jsonl);
        Assert.Equal(
            "DependencyGroups[0].Dependencies[0].Id",
            row.RootElement.GetProperty("location").GetString());
        Assert.Equal("Package ID", row.RootElement.GetProperty("kind").GetString());
        Assert.Equal(
            "non-ASCII characters; reserved-prefix homoglyph",
            row.RootElement.GetProperty("concern").GetString());
        Assert.Equal("System", row.RootElement.GetProperty("reserved_prefix").GetString());
        Assert.Equal("83%", row.RootElement.GetProperty("similarity").GetString());
        Assert.Equal("U+0405→S", row.RootElement.GetProperty("characters").GetString());
        Assert.DoesNotContain(secret, jsonl, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiQuiet_ThreeLines()
    {
        var api = CreateTestApiSurface();
        var options = new ApiOptions { Verbosity = Verbosity.Quiet };

        var output = RenderFullApi(api, options).TrimEnd();
        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.None);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("# ", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Contains(" | ", lines[2]);
        Assert.DoesNotContain("## ", output);
    }

    private static string RenderFullApi(ApiSurface api, ApiOptions options)
    {
        var (view, truncatedCount) = ApiOutputFormatter.BuildFullApiView(api, options);
        var writerOptions = ApiOutputFormatter.BuildWriterOptions(api, options);
        var writer = new MarkoutWriter(new MarkdownFormatter(), writerOptions);
        ApiViewContext.Default.Serialize(view, writer);
        if (truncatedCount > 0)
            writer.WriteParagraph($"... *and {truncatedCount} more types*");
        return writer.ToString().TrimEnd();
    }

    private static string RenderLibraryTable(
        LibraryInspectionView view,
        bool tsv,
        bool jsonl) =>
        OutputFormatter.RenderTable(
            showHeader: true,
            (writer, formatter) => MarkoutSerializer.Serialize(
                view,
                writer,
                formatter,
                InspectionContext.Default,
                OutputFormatter.ConfigureTableWriterOptions(
                    new MarkoutWriterOptions
                    {
                        IncludeSections = [SectionNames.SourceLinkIntegrity],
                    },
                    tsv,
                    jsonl)));

    private static string RenderPerformanceGroupTable(
        PerformanceGroupView view,
        bool tsv,
        bool jsonl) =>
        OutputFormatter.RenderTable(
            showHeader: true,
            (writer, formatter) => MarkoutSerializer.Serialize(
                view,
                writer,
                formatter,
                InspectionContext.Default,
                OutputFormatter.ConfigureTableWriterOptions(
                    new MarkoutWriterOptions(),
                    tsv,
                    jsonl)));

    private static string SerializeWithInclude(LibraryInspection inspection, HashSet<string>? includeSections, bool topFieldsOnly = false)
    {
        var view = new LibraryInspectionView(inspection, topFieldsOnly);
        return MarkoutSerializer.Serialize(view, InspectionContext.Default, new MarkoutWriterOptions
        {
            IncludeSections = includeSections
        }).TrimEnd();
    }

    private static InspectionResult CreateTestPackageResult()
    {
        return new InspectionResult
        {
            PackageName = "TestPackage",
            Version = "1.0.0",
            PackageTypes = ["Library"],
            Published = DateTimeOffset.Parse("2025-01-15"),
        };
    }

    [Fact]
    public void LibraryCompactView_AllSourcePaths_ShowSameFields()
    {
        var modified = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var assemblyInfo = new AssemblyInfo
        {
            AssemblyName = "TestLib",
            AssemblyVersion = "10.0.0.0",
            TargetFramework = ".NETCoreApp,Version=v10.0",
            Architecture = "AnyCPU"
        };

        var platform = new LibraryInspection
        {
            FileName = "TestLib.dll",
            FileType = "dll",
            AssemblyInfo = assemblyInfo,
            FileSize = 1024,
            Source = SourceKind.Platform,
            PlatformVersion = "10.0.1",
            LastModified = modified
        };

        var nuget = new LibraryInspection
        {
            FileName = "TestLib.dll",
            FileType = "dll",
            AssemblyInfo = assemblyInfo,
            FileSize = 1024,
            Source = "NuGet",
            LastModified = modified
        };

        var file = new LibraryInspection
        {
            FileName = "TestLib.dll",
            FileType = "dll",
            AssemblyInfo = assemblyInfo,
            FileSize = 1024,
            Source = "File",
            LastModified = modified
        };

        var platformOutput = Serialize(platform, topFieldsOnly: true);
        var nugetOutput = Serialize(nuget, topFieldsOnly: true);
        var fileOutput = Serialize(file, topFieldsOnly: true);

        // Extract field names from compact line (format: "Name: value | Name: value | ...")
        static HashSet<string> ExtractFieldNames(string output)
        {
            var compactLine = output.Split('\n').First(l => l.Contains('|'));
            return compactLine.Split('|')
                .Select(f => f.Trim().Split(':')[0].Trim())
                .ToHashSet();
        }

        var platformFields = ExtractFieldNames(platformOutput);
        var nugetFields = ExtractFieldNames(nugetOutput);
        var fileFields = ExtractFieldNames(fileOutput);

        Assert.Equal(platformFields, nugetFields);
        Assert.Equal(platformFields, fileFields);

        // Verify expected fields are present
        Assert.Contains("Name", platformFields);
        Assert.Contains("Version", platformFields);
        Assert.Contains("TFM", platformFields);
        Assert.Contains("Arch", platformFields);
        Assert.Contains("Size", platformFields);
        Assert.Contains("Source", platformFields);
        Assert.Contains("Modified", platformFields);
    }

    /// <summary>
    /// Captures stderr. These diagnostics now go to <c>CommandError</c>, which
    /// owns the severity prefix and the containment, so the test can no longer
    /// hand in a writer of its own.
    /// </summary>
    /// <remarks>
    /// Routed through <see cref="ConsoleCapture"/> rather than redirecting
    /// directly: the console is process-global and xUnit runs these in
    /// parallel, which is the #3416 flake.
    /// </remarks>
    private static async Task<string> CaptureErrorAsync(Action action)
    {
        var (_, error) = await ConsoleCapture.RunAsync(action);
        return error;
    }

    /// <summary>
    /// The aggregate Library sections declare a <see cref="MarkoutTable"/> rather
    /// than appending Markdown, so their rows reach the writer and <c>--rows</c> applies at the
    /// writer seam. This is the gate for that routing: a window set on the writer options must
    /// drop rows from a runtime-column table it never saw at compile time.
    /// </summary>
    [Fact]
    public void AggregatedSection_RowWindow_AppliesAtTheWriterSeam()
    {
        var document = new AggregatedSectionDocument
        {
            Sections =
            [
                new AggregatedSectionView
                {
                    Name = "Switches",
                    Body = new MarkoutTable(
                        ["Kind", "Switch"],
                        [["AppContext", "A"], ["AppContext", "B"], ["Feature Switch", "C"]])
                }
            ]
        };

        var all = MarkoutSerializer.Serialize(document, InspectionContext.Default);
        var windowed = MarkoutSerializer.Serialize(
            document, InspectionContext.Default, OutputFormatter.CreateWindowedOptions(RowWindow.Head(2)));

        Assert.Contains("## Switches", all, StringComparison.Ordinal);
        Assert.Contains("| Feature Switch | C |", all, StringComparison.Ordinal);
        Assert.DoesNotContain("| Feature Switch | C |", windowed, StringComparison.Ordinal);
        Assert.Contains("| AppContext | B |", windowed, StringComparison.Ordinal);
    }

    /// <summary>
    /// Routing aggregate cells through markout's semantic code tag rather than literal backticks
    /// corrects two escapes that a hand-written code span gets wrong, neither of which the
    /// differential corpus exercises. This is the gate that keeps them fixed.
    ///
    /// A pipe must not become <c>&amp;#124;</c> inside a code span, where it would render as that
    /// literal text; GFM unescapes <c>\|</c> while splitting table rows, before code spans are
    /// parsed. A backtick must not be backslash-escaped, because backslash escapes do not apply
    /// inside a code span; the delimiter has to be doubled instead.
    /// </summary>
    [Theory]
    [InlineData("Foo.Bar(a|b)", "\\|")]
    [InlineData("IEnumerable`1", "``")]
    public void AggregatedSection_CodeCell_EscapesForACodeSpanRatherThanForPlainText(
        string value, string expectedSpelling)
    {
        var document = new AggregatedSectionDocument
        {
            Sections =
            [
                new AggregatedSectionView
                {
                    Name = "Switches",
                    Body = new MarkoutTable(["API"], [[MarkoutInline.Code(value)]])
                }
            ]
        };

        var rendered = MarkoutSerializer.Serialize(document, InspectionContext.Default);

        Assert.Contains(expectedSpelling, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("&#124;", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("\\`", rendered, StringComparison.Ordinal);
    }
}
