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
    public void SingleAssemblyAudit_HasSingleH1()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        var output = Serialize(inspection);

        Assert.Single(output.Split('\n'), l => l.StartsWith("# "));
    }

    [Fact]
    public async Task MultiAssemblyReport_SelectedChildSectionsRenderPerAssembly()
    {
        var inspections = CreateTestAudits("net9.0", "net8.0");
        var pipeline = LibrarySections.CreatePipeline();
        var options = new LibraryOptions
        {
            IncludeSections = ["Library Info", "Signals"],
            Format = OutputFormat.Markdown
        };

        var (markdown, markdownError) = await ConsoleCapture.RunAsync(
            () => OutputFormatter.WriteLibraryResults(inspections, options, pipeline));

        Assert.Empty(markdownError);
        Assert.StartsWith("# Test\n\n## Libraries\n", markdown);
        Assert.Single(
            markdown.ReplaceLineEndings("\n").Split('\n'),
            line => line.StartsWith("# ", StringComparison.Ordinal));
        Assert.Contains("### Test.dll (net9.0)", markdown);
        Assert.Contains("### Test.dll (net8.0)", markdown);
        Assert.Equal(2, markdown.Split("#### Library Info", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, markdown.Split("#### Signals", StringSplitOptions.None).Length - 1);

        var quietOptions = options with
        {
            Verbosity = Verbosity.Quiet,
            IncludeSections = null
        };
        var (quiet, quietError) = await ConsoleCapture.RunAsync(
            () => OutputFormatter.WriteLibraryResults(inspections, quietOptions, pipeline));

        Assert.Empty(quietError);
        Assert.Contains("Name: Test", quiet);

        var plainOptions = options with
        {
            Format = OutputFormat.PlainText,
            PlainText = true
        };
        var (plain, plainError) = await ConsoleCapture.RunAsync(
            () => OutputFormatter.WriteLibraryResults(inspections, plainOptions, pipeline));

        Assert.Empty(plainError);
        Assert.StartsWith("Test\n\nLibraries\n", plain);
        Assert.DoesNotContain("#", plain);
        Assert.Equal(
            2,
            plain.ReplaceLineEndings("\n").Split('\n')
                .Count(line => line == "Signals"));
    }

    [Fact]
    public async Task MultiAssemblyReport_ProjectionPreservesAssemblyHeadings()
    {
        var inspections = CreateTestAudits("net9.0", "net8.0");
        var pipeline = LibrarySections.CreatePipeline();
        var columnOptions = new LibraryOptions
        {
            IncludeSections = ["Signals"],
            Columns = ["Area"],
            Format = OutputFormat.Markdown
        };

        var (columns, columnsError) = await ConsoleCapture.RunAsync(
            () => OutputFormatter.WriteLibraryResults(inspections, columnOptions, pipeline));

        Assert.Empty(columnsError);
        Assert.Equal(
            2,
            columns.ReplaceLineEndings("\n").Split('\n')
                .Count(line => line.StartsWith("### Test.dll (net", StringComparison.Ordinal)));
        Assert.Equal(2, columns.Split("#### Signals", StringSplitOptions.None).Length - 1);

        var fieldOptions = columnOptions with
        {
            IncludeSections = ["Library Info"],
            Columns = null,
            Fields = ["Name"],
            Verbosity = Verbosity.Quiet
        };
        var (fields, fieldsError) = await ConsoleCapture.RunAsync(
            () => OutputFormatter.WriteLibraryResults(inspections, fieldOptions, pipeline));

        Assert.Empty(fieldsError);
        Assert.Equal(
            2,
            fields.ReplaceLineEndings("\n").Split('\n')
                .Count(line => line.StartsWith("### Test.dll (net", StringComparison.Ordinal)));

        var plainOptions = columnOptions with
        {
            Format = OutputFormat.PlainText,
            PlainText = true,
            Verbosity = Verbosity.Quiet
        };
        var (plain, plainError) = await ConsoleCapture.RunAsync(
            () => OutputFormatter.WriteLibraryResults(inspections, plainOptions, pipeline));

        Assert.Empty(plainError);
        Assert.Equal(
            2,
            plain.ReplaceLineEndings("\n").Split('\n')
                .Count(line => line.StartsWith("Test.dll (net", StringComparison.Ordinal)));
        Assert.Equal(
            2,
            plain.ReplaceLineEndings("\n").Split('\n')
                .Count(line => line.StartsWith("Test.dll", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task MultiAssemblyReport_CountAggregatesChildSections()
    {
        var inspections = CreateTestAudits("net9.0", "net8.0");
        var pipeline = LibrarySections.CreatePipeline();

        var scalarOptions = new LibraryOptions
        {
            Count = true,
            IncludeSections = ["Signals"]
        };
        var (scalar, scalarError) = await ConsoleCapture.RunAsync(
            () => OutputFormatter.WriteLibraryResults(inspections, scalarOptions, pipeline));

        Assert.Empty(scalarError);
        Assert.Equal("2", scalar.Trim());

        var mapOptions = scalarOptions with
        {
            IncludeSections = ["Library Info", "Signals"]
        };
        var (map, mapError) = await ConsoleCapture.RunAsync(
            () => OutputFormatter.WriteLibraryResults(inspections, mapOptions, pipeline));

        Assert.Empty(mapError);
        Assert.Contains("| Library Info |", map);
        Assert.DoesNotContain("| Library Info | 0 |", map);
        Assert.Contains("| Signals | 2 |", map);
    }

    [Fact]
    public void ShiftMarkdownHeadingLevels_LeavesFencedPayloadHeadings()
    {
        const string markdown = """
            # Document

            ## Section

            ```text
            # Payload heading
            ```
            """;

        var shifted = OutputFormatter.ShiftMarkdownHeadingLevels(markdown, 2);

        Assert.StartsWith("### Document\n\n#### Section", shifted);
        Assert.Contains("```text\n# Payload heading\n```", shifted);
    }

    [Fact]
    public async Task MultiAssemblyReport_ContainsTheManualOuterTitle()
    {
        var inspections = CreateTestAudits("net9.0", "net8.0");
        inspections[0].FileName = "Test<tag>&\n## FORGED.dll";
        var options = new LibraryOptions
        {
            IncludeSections = ["Signals"],
            Format = OutputFormat.Markdown
        };

        var (output, error) = await ConsoleCapture.RunAsync(
            () => OutputFormatter.WriteLibraryResults(
                inspections, options, LibrarySections.CreatePipeline()));

        Assert.Empty(error);
        Assert.DoesNotContain("\n## FORGED", output);
        Assert.StartsWith("# Test&lt;tag&gt;&amp; ## FORGED\n", output);
        Assert.Contains("### Test&lt;tag&gt;&amp; ## FORGED.dll (net9.0)", output);
        Assert.Single(
            output.ReplaceLineEndings("\n").Split('\n'),
            line => line.StartsWith("# ", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleAudit_IncludesSymbols_AtNormalVerbosity()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        var pipeline = LibrarySections.CreatePipeline();
        var includeSections = pipeline.ComputeIncludeSections(inspection, Verbosity.Normal);
        var output = SerializeWithInclude(inspection, includeSections);

        Assert.Contains("## Symbols", output);
    }

    [Fact]
    public void SingleAudit_LibraryInfo_CountsIntegrationCategories()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.HasDependencyInjectionSupport = true;
        inspection.HasLoggingSupport = true;
        inspection.HasOpenTelemetrySupport = true;
        inspection.EcosystemIntegrationInspection = MetadataFindings.InspectEcosystemIntegrations(
            [
                new EcosystemIntegrationSignalInfo(
                    EcosystemIntegrationNames.DependencyInjection,
                    "Service registration",
                    "Microsoft.Extensions.DependencyInjection.IServiceCollection"),
                new EcosystemIntegrationSignalInfo(
                    EcosystemIntegrationNames.Logging,
                    "Logging",
                    "Microsoft.Extensions.Logging.ILogger"),
            ],
            FindingTestData.Subject);
        inspection.OpenTelemetryInspection = MetadataFindings.InspectOpenTelemetrySignals(
            [
                new OpenTelemetrySignalInfo("Tracing", "System.Diagnostics.ActivitySource"),
                new OpenTelemetrySignalInfo("Metrics", "System.Diagnostics.Metrics.Meter"),
            ],
            FindingTestData.Subject);

        var output = Serialize(inspection);

        Assert.Contains("| Integrations | 3 |", output);
    }

    [Fact]
    public void SingleAudit_FailedFindingRendersDiagnosticWithoutAbortingOtherSections()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.SwitchInspection = new FindingInspection<SwitchInfo>.Failed(
            new InspectionError(
                FindingTestData.Subject,
                MetadataFindings.SwitchDescriptor,
                "switch scan failed"));
        var pipeline = LibrarySections.CreatePipeline();
        var includeSections = pipeline.ComputeIncludeSections(inspection, Verbosity.Normal);

        var output = SerializeWithInclude(inspection, includeSections);

        Assert.Contains("## Library Info", output);
        Assert.Contains("## Inspection Failures", output);
        Assert.Contains("Switches", output);
        Assert.Contains("Switch", output);
        Assert.Contains("switch scan failed", output);
        Assert.DoesNotContain("## Switches", output);
    }

    [Fact]
    public void SingleAudit_LibraryInfo_UsesExactIntegrationAndSwitchCounts()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.IntegrationCount = 1;
        inspection.SwitchCount = 5;

        var output = Serialize(inspection);

        Assert.Contains("| Integrations | 1 |", output);
        Assert.Contains("| Switches | 5 |", output);
    }

    [Fact]
    public void SingleAudit_LibraryInfo_FieldsAreAlphabetical()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.AssemblyInfo!.InformationalVersion = "1.0.0+abc";
        inspection.AssemblyInfo.MethodDefinitionCount = 42;
        inspection.HasOpenTelemetrySupport = true;

        var output = Serialize(inspection);

        Assert.True(output.IndexOf("| Architecture |", StringComparison.Ordinal)
            < output.IndexOf("| Assembly Version |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Informational Version |", StringComparison.Ordinal)
            < output.IndexOf("| Integrations |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Integrations |", StringComparison.Ordinal)
            < output.IndexOf("| Methods |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Source |", StringComparison.Ordinal)
            < output.IndexOf("| Switches |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Switches |", StringComparison.Ordinal)
            < output.IndexOf("| Target Framework |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Target Framework |", StringComparison.Ordinal)
            < output.IndexOf("| Type Forwarders |", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleAudit_IncludesSymbols_AtDetailedVerbosity()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        var pipeline = LibrarySections.CreatePipeline();
        var includeSections = pipeline.ComputeIncludeSections(inspection, Verbosity.Detailed);
        var output = SerializeWithInclude(inspection, includeSections);

        Assert.Contains("## Symbols", output);
    }

    [Fact]
    public void SingleAudit_MetadataIncludesDeterministic()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.IsDeterministic = true;
        inspection.HasReproducibleFlag = true;
        var output = Serialize(inspection);

        Assert.Contains("Deterministic", output);
        Assert.Contains("Reproducible", output);
    }

    [Fact]
    public void SingleAudit_CustomAttributes_AreSortedByName()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.SetAssemblyAttributeInspection(
            MetadataFindings.InspectAssemblyAttributes(
                [
                    new AssemblyAttributeInfo("NeutralResourcesLanguage", "Assembly", "en-US"),
                    new AssemblyAttributeInfo("AssemblyMetadata(Serviceable)", "Assembly", "True"),
                    new AssemblyAttributeInfo("AssemblyDefaultAlias", "Assembly", "Test"),
                ],
                FindingTestData.Subject),
            jsonOrder: null);

        var output = Serialize(inspection);

        Assert.True(output.IndexOf("AssemblyDefaultAlias", StringComparison.Ordinal)
            < output.IndexOf("AssemblyMetadata(Serviceable)", StringComparison.Ordinal));
        Assert.True(output.IndexOf("AssemblyMetadata(Serviceable)", StringComparison.Ordinal)
            < output.IndexOf("NeutralResourcesLanguage", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleAudit_MethodSections_AreSortedByTypeThenName()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.UnsafeMembers =
        [
            new UnsafeMemberSummary { Member = "B.Type.A()", Reason = "Unsafe signature", Detail = "void A()", Kind = "signature" },
            new UnsafeMemberSummary { Member = "A.Type.Z()", Reason = "Unsafe signature", Detail = "void Z()", Kind = "signature" }
        ];
        ExtensionMethodInfo[] extensionMembers =
        [
            FindingTestData.ExtensionMember("A", "B.Type"),
            FindingTestData.ExtensionMember("Z", "A.Type"),
        ];
        inspection.SetExtensionMemberInspection(
            MetadataFindings.InspectExtensionMembers(
                extensionMembers,
                FindingTestData.Subject),
            extensionMembers);

        var output = Serialize(inspection);

        Assert.True(output.IndexOf("| `A.Type.Z()` |", StringComparison.Ordinal)
            < output.IndexOf("| `B.Type.A()` |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Z | method | A.Type |", StringComparison.Ordinal)
            < output.IndexOf("| A | method | B.Type |", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleAudit_SymbolFields_AreSortedByFieldName()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.Builder = "Microsoft";
        inspection.PdbFormat = "Portable";
        inspection.PdbLocation = "Symbol Package";
        inspection.SourceLinkJson = "{}";
        inspection.HasSourceLink = true;
        inspection.SymbolServer = "msdl.microsoft.com";

        var output = Serialize(inspection);

        Assert.True(output.IndexOf("| Builder |", StringComparison.Ordinal)
            < output.IndexOf("| PDB Format |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| PDB Path |", StringComparison.Ordinal)
            < output.IndexOf("| Source Link |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Source Link |", StringComparison.Ordinal)
            < output.IndexOf("| Symbol Server |", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleAudit_SourceLinkAudit_UsesAvailableSourceFilesLabel()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.AllSourcesAccessible = false;
        inspection.AccessibleSourceFiles = 343;
        inspection.TotalSourceFiles = 345;
        inspection.EmbeddedSourceFiles = 2;

        var output = Serialize(inspection);

        Assert.Contains("| Source Files | 343/345 available |", output);
        Assert.DoesNotContain("accessible or embedded", output);
    }

    [Fact]
    public void SingleAudit_SourceIntegrity_RendersMismatchedFilesInSection()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.SourceIntegrityChecked = true;
        inspection.SourceIntegrityMismatched = 2;
        inspection.SourceIntegrityMismatches =
        [
            "/_/src/A.cs",
            "/_/src/B.cs"
        ];

        var output = Serialize(inspection);

        Assert.Contains("## SourceLink: Integrity", output);
        Assert.Contains("| Mismatched | 2 |", output);
        Assert.Contains("| Mismatched Files | `/_/src/A.cs`, `/_/src/B.cs` |", output);
        Assert.DoesNotContain("Source integrity mismatch:", output);
    }

    [Fact]
    public void SingleAudit_SourceIntegrity_RendersLineEndingNormalizedCount()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.SourceIntegrityChecked = true;
        inspection.SourceIntegrityVerified = 2;
        inspection.SourceIntegrityLineEndingNormalized = 2;

        var output = Serialize(inspection);

        Assert.Contains("## SourceLink: Integrity", output);
        Assert.Contains("| CR/LF Mismatch | 2 normalized |", output);
        Assert.Contains("| Status | Verified |", output);
        Assert.Contains("| Verified | 2 |", output);
    }

    [Fact]
    public void SingleAudit_SourceIntegrity_FieldsAreAlphabetical()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.SourceIntegrityChecked = true;
        inspection.SourceIntegrityVerified = 2;
        inspection.SourceIntegrityMismatched = 1;
        inspection.SourceIntegrityLineEndingNormalized = 1;
        inspection.SourceIntegrityUnverifiable = 1;
        inspection.SourceIntegrityMismatches = ["/_/src/A.cs"];

        var output = Serialize(inspection);

        Assert.True(output.IndexOf("| CR/LF Mismatch |", StringComparison.Ordinal)
            < output.IndexOf("| Mismatched |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Mismatched Files |", StringComparison.Ordinal)
            < output.IndexOf("| Status |", StringComparison.Ordinal));
        Assert.True(output.IndexOf("| Unverifiable |", StringComparison.Ordinal)
            < output.IndexOf("| Verified |", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleAudit_Signals_DoNotRenderSourceLinkCrlfMismatch()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.HasSourceLink = true;
        inspection.SourceIntegrityChecked = true;
        inspection.SourceIntegrityVerified = 2;
        inspection.SourceIntegrityLineEndingNormalized = 2;

        using var session =
            AssemblyInspectionSession.Open(typeof(OutputFormatterTests).Assembly.Location);
        AuditSignalBuilder.ApplyLibraryAudit(inspection, session.AuditMetadata());
        var output = Serialize(inspection);

        Assert.Contains("## Signals", output);
        Assert.DoesNotContain("SourceLink CR/LF", output);
        Assert.Contains("## SourceLink: Integrity", output);
        Assert.Contains("| CR/LF Mismatch | 2 normalized |", output);
    }

    [Fact]
    public void SingleAudit_Signals_AbsentSourceLink_DoesNotClaimSourceLinkFound()
    {
        // A PDB is present but carries no SourceLink data. The SourceLink signal must report
        // "Not found" without claiming SourceLink data was found in the PDB (#675).
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.HasSourceLink = false;
        inspection.PdbLocation = "standalone";
        inspection.SourceLinkUnavailableReason = "PDB checked; no SourceLink data";

        using var session =
            AssemblyInspectionSession.Open(typeof(OutputFormatterTests).Assembly.Location);
        AuditSignalBuilder.ApplyLibraryAudit(inspection, session.AuditMetadata());

        var sourceLink = Assert.Single(inspection.AuditSignals!, s => s.Signal == "SourceLink");
        Assert.Equal("Not found", sourceLink.Value);
        Assert.DoesNotContain("SourceLink data found", sourceLink.Evidence);
        Assert.Contains("no SourceLink data", sourceLink.Evidence);
    }

    [Fact]
    public void SingleAudit_Signals_UnusableSourceLink_ReportsTheParseError()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.HasSourceLink = true;
        inspection.PdbLocation = "standalone";
        inspection.SourceLinkMap = new SourceLinkMapInspection(
            SourceLinkMapStatus.Unusable,
            "invalid JSON",
            [],
            []);

        using var session =
            AssemblyInspectionSession.Open(typeof(OutputFormatterTests).Assembly.Location);
        AuditSignalBuilder.ApplyLibraryAudit(inspection, session.AuditMetadata());

        var sourceLink = Assert.Single(
            inspection.AuditSignals!,
            signal => signal.Signal == "SourceLink");
        Assert.Equal("Present (unusable)", sourceLink.Value);
        Assert.Contains("invalid JSON", sourceLink.Evidence);
        Assert.DoesNotContain("SourceLink data found", sourceLink.Evidence);
    }

    [Fact]
    public void SingleAudit_SourceLinkDiagnostics_RendersParseAndEntryFailures()
    {
        var inspection = CreateTestAudit("Test.dll", "net9.0");
        inspection.HasSourceLink = true;
        inspection.SourceLinkMap = new SourceLinkMapInspection(
            SourceLinkMapStatus.Unusable,
            "invalid JSON",
            ["/_/*"],
            ["/_/*"]);

        var output = Serialize(inspection);

        Assert.Contains("## SourceLink: Diagnostics", output);
        Assert.Contains("| Map error |  | invalid JSON |", output);
        Assert.Contains(
            "| Rejected mapping | /_/* | entry does not conform to the SourceLink document-map schema |",
            output);
    }

    private static LibraryInspection CreateTestAudit(string fileName, string? tfm)
    {
        return new LibraryInspection
        {
            FileName = fileName,
            FileType = "dll",
            Tfm = tfm,
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = Path.GetFileNameWithoutExtension(fileName),
                AssemblyVersion = "1.0.0.0",
                TargetFramework = tfm != null ? $".NETCoreApp,Version=v{tfm[3..]}" : null,
                Architecture = "AnyCPU"
            }
        };
    }

    private static List<LibraryInspection> CreateTestAudits(params string[] tfms)
    {
        return tfms.Select(tfm =>
        {
            var inspection = CreateTestAudit("Test.dll", tfm);
            inspection.AuditSignals =
            [
                new AuditSignal("Package", "Assemblies", "1", "test")
            ];
            return inspection;
        }).ToList();
    }

    private static void AssertMarkdownTablesHaveUniformColumnCounts(string markdown)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var inCodeFence = false;
        for (var i = 0; i < lines.Length - 1; i++)
        {
            if (IsCodeFence(lines[i]))
            {
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence || !IsTableLine(lines[i]) || !IsSeparatorLine(lines[i + 1]))
                continue;

            var expected = CountCells(lines[i]);
            var tableStart = i + 1;
            i++;
            while (i < lines.Length && IsTableLine(lines[i]))
            {
                var actual = CountCells(lines[i]);
                if (actual != expected)
                    throw new InvalidOperationException($"Markdown table row {i + 1} has {actual} columns; expected {expected}. Table starts at line {tableStart}.");
                i++;
            }
        }

        static bool IsCodeFence(string line)
            => line.TrimStart().StartsWith("```", StringComparison.Ordinal);

        static bool IsTableLine(string line)
        {
            var trimmed = line.Trim();
            return trimmed.Length >= 2 && trimmed.StartsWith('|') && trimmed.EndsWith('|');
        }

        static bool IsSeparatorLine(string line)
        {
            if (!IsTableLine(line))
                return false;

            var cells = line.Trim().Trim('|').Split('|', StringSplitOptions.TrimEntries);
            return cells.Length > 0 && cells.All(cell =>
                cell.Length > 0
                && cell.Any(c => c == '-')
                && cell.All(c => c is '-' or ':' or ' '));
        }

        static int CountCells(string line)
            => line.Trim().Trim('|').Split('|').Length;
    }

    private static string Serialize(LibraryInspection inspection, bool topFieldsOnly = false)
    {
        var view = new LibraryInspectionView(inspection, topFieldsOnly);
        return MarkoutSerializer.Serialize(view, InspectionContext.Default).TrimEnd();
    }

    // ===== API Output Formatter Tests =====

    private static ApiSurface CreateTestApiSurface(int typeCount = 3)
    {
        var types = Enumerable.Range(1, typeCount).Select(i => new ApiType
        {
            Namespace = "TestLib",
            Name = $"Type{i}",
            Kind = "class",
            Members = [new ApiMember { Name = "Method1", Kind = "method", Signature = "void Method1()" }]
        }).ToList();

        return new ApiSurface
        {
            Name = "TestLib",
            Source = "NuGet",
            Version = "1.0.0",
            Tfm = "net10.0",
            Types = types,
            PublicTypeCount = types.Count,
            PublicMethodCount = types.Count,
            PublicPropertyCount = 0
        };
    }
}
