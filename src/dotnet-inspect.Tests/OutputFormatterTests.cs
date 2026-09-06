using DotnetInspector.Models;
using System.Reflection;
using System.Text.Json;
using DotnetInspector.Views;
using DotnetInspector;
using DotnetInspector.Commands;
using ILInspector.Analysis;
using ILInspector.Findings;
using ILInspector.Metadata;
using InertText;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using Markout;

namespace DotnetInspector.Tests;

// Captures Console.Error, which is process-wide state.
[Collection("Console")]
public class OutputFormatterTests
{
    /// <summary>
    /// This is the named non-vacuity gate for product-owned artifact framing. It fails when the
    /// count-file writer or printable-document JSONL writer inherits CRLF from the Windows host
    /// instead of emitting the repository's LF artifact contract.
    /// </summary>
    [Fact]
    public void ArtifactNewlineGate_ProductOwnedFramingUsesLf()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("artifact-newline-gate-");
        try
        {
            var countPath = Path.Combine(tempDirectory.FullName, "count.txt");
            CountOutput.WriteCount(7, countPath);

            var printPath = Path.Combine(tempDirectory.FullName, "print.jsonl");
            var printExit = PrintProjectionOutput.Write(
                [new PrintableDocument(1, "Docs", "README", "README.md", null, "body")],
                new PrintProjectionOptions(
                    Row: null,
                    JsonOutput: false,
                    Jsonl: true,
                    JsonArray: false,
                    Bare: false,
                    Destination: new ProjectionDestination(printPath)));

            Assert.Equal(0, printExit);
            Assert.Equal("7\n", File.ReadAllText(countPath));
            Assert.Equal(
                "{\"row\":1,\"section\":\"Docs\",\"label\":\"README\",\"path\":\"README.md\",\"content\":\"body\"}\n",
                File.ReadAllText(printPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void OutputDestination_NormalizesBufferedLineEndings()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("output-destination-lf-");
        try
        {
            var path = Path.Combine(tempDirectory.FullName, "output.txt");
            OutputDestination.Write(
                path,
                rowWindow: null,
                writer => writer.Write("first\r\nsecond\rthird\n"));
            Assert.Equal("first\nsecond\nthird\n", File.ReadAllText(path));

            var sink = new StringWriter { NewLine = "\r\n" };
            var normalized = new LfTextWriter(sink);
            normalized.WriteLine("first\r\nsecond".AsSpan());
            normalized.Flush();
            Assert.Equal("first\nsecond\n", sink.ToString());
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ProjectionDestination_DoesNotApplyALineWindowAfterSemanticRows()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("projection-row-window-");
        try
        {
            var root = CommandLineBuilder.CreateRootCommand();
            var args = CommandLineBuilder.PreprocessArgs(["package", "Foo", "-n", "1"]);
            CommandLineBuilder.ApplyParsedLineWindow(root.Parse(args));
            var path = Path.Combine(tempDirectory.FullName, "paths.txt");
            var exit = ShapeProjectionOutput.Write(
                [
                    new ShapeProjectionRow(2, "Files", "second"),
                    new ShapeProjectionRow(3, "Files", "third"),
                ],
                new ShapeProjectionOptions(
                    ShapeProjectionKind.Paths,
                    Row: null,
                    JsonOutput: false,
                    Jsonl: false,
                    JsonArray: false,
                    Destination: new ProjectionDestination(path, RowWindow.Range(2, 3))));

            Assert.Equal(0, exit);
            Assert.Equal("second\nthird\n", File.ReadAllText(path));
        }
        finally
        {
            CommandLine.ArgumentPreprocessor.Reset();
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ExactProjectionLineWindowPreflight_PreservesDestinationsAndSkipsAcquisition()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("projection-exact-preflight-");
        try
        {
            var root = CommandLineBuilder.CreateRootCommand();
            var args = CommandLineBuilder.PreprocessArgs(["package", "Foo", "-n", "1"]);
            CommandLineBuilder.ApplyParsedLineWindow(root.Parse(args));
            var absentPath = Path.Combine(tempDirectory.FullName, "absent.bin");
            var existingPath = Path.Combine(tempDirectory.FullName, "existing.bin");
            byte[] sentinel = [0x10, 0x20, 0x30];
            File.WriteAllBytes(existingPath, sentinel);
            var rows = new[]
            {
                new PrintableRow(1, "Docs", "README", "README.md", null)
            };
            var reads = 0;

            async Task<(int Exit, string Output, string Error)> RunAsync(string path) =>
                await ConsoleCapture.RunAsync(() => Task.FromResult(
                    PrintProjectionOutput.Write(
                        rows,
                        _ =>
                        {
                            reads++;
                            return new PrintableContent("first\nsecond", [0x01, 0x02]);
                        },
                        new PrintProjectionOptions(
                            Row: null,
                            JsonOutput: false,
                            Jsonl: false,
                            JsonArray: false,
                            Bare: true,
                            Destination: new ProjectionDestination(path, ExactTransfer: true)))));

            var absent = await RunAsync(absentPath);
            var existing = await RunAsync(existingPath);

            Assert.Equal(1, absent.Exit);
            Assert.Equal(1, existing.Exit);
            Assert.Empty(absent.Output);
            Assert.Empty(existing.Output);
            Assert.Contains("line limit", absent.Error, StringComparison.Ordinal);
            Assert.Contains("exact --out", absent.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(absentPath));
            Assert.Equal(sentinel, File.ReadAllBytes(existingPath));
            Assert.Equal(0, reads);
        }
        finally
        {
            CommandLine.ArgumentPreprocessor.Reset();
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void LibraryInspectionTypedRows_CarryConcernProvenance()
    {
        const string hostile = "value\u202E\nINJECTED";
        const TextConcern concerns = TextConcern.Control | TextConcern.Format;

        var reference = new ReferenceRow(hostile, hostile, hostile);
        var classified = new ClassifiedMethodRow(hostile, hostile, hostile);
        var resource = new ResourceRow(hostile, hostile, hostile);
        var triage = new ResourceTriageRow(
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile);
        var performance = new PerformanceRow(
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile);
        var performanceGroup = new PerformanceGroupRow(
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile,
            hostile);
        var failure = new InspectionFailureRow(hostile, hostile, hostile);
        var union = new UnionTypeRow(hostile, hostile, hostile, hostile);
        var sourceLink = new SourceLinkAuditSection
        {
            SourceFilesText = new InertString(TextPolicy.Field, hostile),
            StatusText = new InertString(TextPolicy.Field, hostile),
        };
        var sourceIntegrity = new SourceIntegritySection
        {
            CrlfMismatchText = new InertString(TextPolicy.Field, hostile),
            MismatchedFileTexts = [new InertString(TextPolicy.Field, hostile)],
            StatusText = new InertString(TextPolicy.Field, hostile),
        };

        InertString[] texts =
        [
            reference.PublicKeyTokenText,
            classified.DeclaringTypeText,
            classified.SignatureText,
            resource.VisibilityText,
            resource.SizeText,
            triage.MemberText,
            triage.CandidateText,
            triage.BoundaryText,
            triage.AcquireILText,
            triage.BoundaryILText,
            performance.MemberText,
            performance.EvidenceText,
            performance.AllocationText!.Value,
            performance.ReachText,
            performanceGroup.KindText,
            performanceGroup.MemberText,
            performanceGroup.EvidenceText,
            performanceGroup.AllocationText!.Value,
            performanceGroup.LoopText!.Value,
            performanceGroup.ReachText,
            performanceGroup.WeightText!.Value,
            performanceGroup.PriorityText,
            performanceGroup.ConfidenceText,
            failure.SectionText,
            union.IUnionText,
            sourceLink.SourceFilesText,
            sourceLink.StatusText,
            sourceIntegrity.CrlfMismatchText!.Value,
            sourceIntegrity.MismatchedFileTexts![0],
            sourceIntegrity.StatusText,
        ];

        Assert.Equal(30, texts.Length);
        Assert.All(texts, text => Assert.Equal(concerns, text.Concerns));
    }

    [Fact]
    public void SourceIntegrityTypedText_RendersAcrossMarkdownTsvAndJsonl()
    {
        const string cleanPath = @"C:\src\Foo.cs";
        const string hostile = "path\u202E\nINJECTED.cs";
        var view = new LibraryInspectionView(new LibraryInspection
        {
            SourceIntegrityChecked = true,
            SourceIntegrityMismatched = 2,
            SourceIntegrityMismatches = [cleanPath, hostile],
        });
        var writerOptions = new MarkoutWriterOptions
        {
            IncludeSections = [SectionNames.SourceLinkIntegrity],
        };

        string markdown = MarkoutSerializer.Serialize(
            view,
            InspectionContext.Default,
            writerOptions);
        string tsv = RenderLibraryTable(view, tsv: true, jsonl: false);
        string jsonl = RenderLibraryTable(view, tsv: false, jsonl: true);

        foreach (string output in new[] { markdown, tsv })
        {
            Assert.Contains(cleanPath, output, StringComparison.Ordinal);
            Assert.DoesNotContain(@"C:\\src\\Foo.cs", output, StringComparison.Ordinal);
            Assert.DoesNotContain("\u202E", output, StringComparison.Ordinal);
            Assert.Contains(@"\u202E", output, StringComparison.Ordinal);
            Assert.Contains(@"\^J", output, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("\u202E", jsonl, StringComparison.Ordinal);
        Assert.Contains(@"\u202E", jsonl, StringComparison.Ordinal);
        Assert.Contains(@"\^J", jsonl, StringComparison.Ordinal);

        string[] jsonlRows =
            jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, jsonlRows.Length);
        foreach (string jsonlRow in jsonlRows)
        {
            using JsonDocument document = JsonDocument.Parse(jsonlRow);
            Assert.DoesNotContain(
                document.RootElement.EnumerateObject(),
                property => property.Name.EndsWith("_text", StringComparison.Ordinal));
        }
        Assert.Contains(
            jsonlRows,
            row => row.Contains(
                "\"field\":\"Mismatched Files\"",
                StringComparison.Ordinal));
        string mismatchedFilesRow = Assert.Single(
            jsonlRows,
            row => row.Contains(
                "\"field\":\"Mismatched Files\"",
                StringComparison.Ordinal));
        using JsonDocument mismatchedFilesDocument =
            JsonDocument.Parse(mismatchedFilesRow);
        string mismatchedFiles =
            mismatchedFilesDocument.RootElement.GetProperty("value").GetString()!;
        Assert.Contains(cleanPath, mismatchedFiles, StringComparison.Ordinal);
        Assert.DoesNotContain(
            @"C:\\src\\Foo.cs",
            mismatchedFiles,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PerformanceGroupTypedText_RendersAcrossTsvAndJsonl()
    {
        const string hostile = "value\u200D\uFEFF\U000E0041\t\u202E\nINJECTED";
        var view = new PerformanceGroupView(
        [
            new PerformanceGroupRow(
                hostile,
                hostile,
                hostile,
                hostile,
                hostile,
                hostile,
                hostile,
                hostile,
                hostile),
        ]);

        string tsv = RenderPerformanceGroupTable(
            view,
            tsv: true,
            jsonl: false);
        string jsonl = RenderPerformanceGroupTable(
            view,
            tsv: false,
            jsonl: true);

        foreach (string output in new[] { tsv, jsonl })
        {
            Assert.DoesNotContain("\u200D", output, StringComparison.Ordinal);
            Assert.DoesNotContain("\uFEFF", output, StringComparison.Ordinal);
            Assert.DoesNotContain("\U000E0041", output, StringComparison.Ordinal);
            Assert.DoesNotContain("\u202E", output, StringComparison.Ordinal);
            Assert.Contains(@"\u200D", output, StringComparison.Ordinal);
            Assert.Contains(@"\uFEFF", output, StringComparison.Ordinal);
            Assert.Contains(@"\U000E0041", output, StringComparison.Ordinal);
            Assert.Contains(@"\^I", output, StringComparison.Ordinal);
            Assert.Contains(@"\u202E", output, StringComparison.Ordinal);
            Assert.Contains(@"\^J", output, StringComparison.Ordinal);
        }

        string jsonlRow = Assert.Single(
            jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using JsonDocument document = JsonDocument.Parse(jsonlRow);
        Assert.DoesNotContain(
            document.RootElement.EnumerateObject(),
            property => property.Name.EndsWith("_text", StringComparison.Ordinal));
    }

    [Fact]
    public void ResourceTriageFailure_IsVisible()
    {
        var subject = new FindingSubject("fixture", "fixture");
        var inspection = new LibraryInspection
        {
            ResourceLifecycleInspection =
                new FindingInspection<ResourceLifecycleOccurrence>.Failed(
                    new InspectionError(
                        subject,
                        AnalysisFindings.ResourceLifecycleDescriptor,
                        "fixture failure")),
        };

        var failure = Assert.Single(inspection.InspectionFailures!);
        Assert.Equal(SectionNames.ArrayPoolEscapes, failure.Section);
        Assert.Equal("Resource lifecycle occurrence", failure.Finding);
        Assert.Equal("fixture failure", failure.Reason);
    }

    [Fact]
    public void ResourceTriageSection_PreservesRepeatedOperationBoundaryOffsets()
    {
        var rows = new LibraryInspectionView(new LibraryInspection
        {
            ResourceTriage =
            [
                new ResourceTriageSummary
                {
                    Member = "Fixture.ReadTwice",
                    Candidate = "rt~fixture",
                    Finding = "analysis.resource-lifecycle",
                    Provenance = "exact",
                    Resource = "ArrayPool<T>",
                    Shape = "pool-churn-on-exception",
                    Impact = "pool churn if boundary throws",
                    Actionability = "untrusted-input boundary",
                    AcquireOffset = 0x0007,
                    Boundaries =
                    [
                        new ResourceBoundarySummary(
                            "System.IO.Stream::Read",
                            0x0011),
                        new ResourceBoundarySummary(
                            "System.IO.Stream::Read",
                            0x001A),
                    ],
                    Evidence =
                        "An exact external-input boundary is reached before modeled cleanup; an exception can bypass Return.",
                    Direction =
                        "Return the pooled array from finally or catch-all cleanup.",
                    Confidence = "medium",
                },
            ],
        }).ResourceTriageSection;

        Assert.Collection(
            rows,
            row =>
            {
                Assert.Contains("System.IO.Stream::Read", row.Boundary);
                Assert.Contains("IL_0011", row.BoundaryIL);
            },
            row =>
            {
                Assert.Contains("System.IO.Stream::Read", row.Boundary);
                Assert.Contains("IL_001A", row.BoundaryIL);
            });
        Assert.All(rows, row => Assert.Contains("IL_0007", row.AcquireIL));
        Assert.Single(rows.Select(row => row.Candidate).Distinct());
    }

    [Fact]
    public void UnsafeMembersSection_RendersDegradedSignatureScan()
    {
        var view = new LibraryInspectionView(new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            UnsafeSignatureDecodeStatus = SignatureDecodeStatus.Degraded,
        });

        Assert.True(view.HasUnsafeMembers);
        var row = Assert.Single(view.UnsafeMembersSection!);
        Assert.Equal("Decode degraded", row.Reason);
        Assert.Contains("unsafe-code presence may be incomplete", row.Detail);
    }

    [Fact]
    public void WriteTable_WithoutRowLimit_MatchesRenderThenWrite()
    {
        // The uncapped path serializes straight to the writer instead of materializing the
        // whole table as a string (#1205); output must be identical to render-then-write.
        Action<TextWriter, Markout.Formatting.IMarkoutFormatter> serialize = (writer, _) => writer.Write("Name\tValue\nA\t1\nB\t2\n");

        var direct = new StringWriter();
        OutputFormatter.WriteTable(direct, showHeader: true, serialize, maxRows: null);

        Assert.Equal(OutputFormatter.RenderTable(showHeader: true, serialize), direct.ToString());
    }

    [Fact]
    public void WriteTable_WithRowLimit_StillTrimsRows()
    {
        Action<TextWriter, Markout.Formatting.IMarkoutFormatter> serialize = (writer, _) => writer.Write("Name\tValue\nA\t1\nB\t2\n");

        var capped = new StringWriter();
        OutputFormatter.WriteTable(capped, showHeader: true, serialize, maxRows: RowWindow.Head(1));

        Assert.Equal(
            OutputFormatter.LimitRenderedTableRows(OutputFormatter.RenderTable(true, serialize), RowWindow.Head(1), hasHeader: true),
            capped.ToString());
    }

    [Fact]
    public void VersionListings_HeadersAreDefaultAndHeaderlessIsOptIn()
    {
        PackageVersionInfo[] versions =
        [
            new("2.0.0", Listed: true),
            new("1.0.0-preview.1", Listed: false),
        ];
        var withHeader = new StringWriter { NewLine = "\n" };
        var withoutHeader = new StringWriter { NewLine = "\n" };

        OutputFormatter.WriteVersionListings(
            versions,
            new InspectionOptions { Tsv = true },
            withHeader);
        OutputFormatter.WriteVersionListings(
            versions,
            new InspectionOptions
            {
                Tsv = true,
                NoHeader = true,
            },
            withoutHeader);

        Assert.Equal(
            "version\tlisting\n"
            + "2.0.0\tlisted\n"
            + "1.0.0-preview.1\tunlisted\n",
            withHeader.ToString());
        Assert.Equal(
            "2.0.0\tlisted\n"
            + "1.0.0-preview.1\tunlisted\n",
            withoutHeader.ToString());
    }

    [Fact]
    public void VersionFeed_JsonPreservesBooleanListedProperty()
    {
        PackageVersionSourceInfo[] versions =
        [
            new("2.0.0", "local", Listed: true),
            new("1.0.0", "nuget.org", Listed: false),
        ];
        var output = new StringWriter { NewLine = "\n" };

        OutputFormatter.WriteVersionFeedTable(
            versions,
            new InspectionOptions { JsonOutput = true },
            output);

        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement[] rows = [.. document.RootElement.EnumerateArray()];
        Assert.Equal(versions.Length, rows.Length);
        for (int i = 0; i < rows.Length; i++)
        {
            Assert.Equal(
                ["version", "feed", "listed"],
                rows[i].EnumerateObject().Select(property => property.Name));
            Assert.Equal(versions[i].Version, rows[i].GetProperty("version").GetString());
            Assert.Equal(versions[i].Feed, rows[i].GetProperty("feed").GetString());
            Assert.Equal(versions[i].Listed, rows[i].GetProperty("listed").GetBoolean());
        }
    }

    [Fact]
    public void VersionListings_JsonUsesTheJsonlRowShape()
    {
        PackageVersionInfo[] versions =
        [
            new("2.0.0", Listed: true),
            new("1.0.0-preview.1", Listed: false),
        ];
        var output = new StringWriter { NewLine = "\n" };

        OutputFormatter.WriteVersionListings(
            versions,
            new InspectionOptions { JsonOutput = true },
            output);

        using JsonDocument document =
            JsonDocument.Parse(output.ToString());
        JsonElement[] rows =
            [.. document.RootElement.EnumerateArray()];
        Assert.Equal(2, rows.Length);
        Assert.Equal("2.0.0", rows[0].GetProperty("version").GetString());
        Assert.Equal("listed", rows[0].GetProperty("listing").GetString());
        Assert.Equal(
            "unlisted",
            rows[1].GetProperty("listing").GetString());
    }

    [Fact]
    public void WriteTable_ToLineLimitingWriter_PreservesBufferedSemantics()
    {
        // The line-limiting writer counts newlines per write call, so WriteTable must keep the
        // buffered render-then-write path for it (output identical to writing the rendered
        // string), even without a row cap.
        Action<TextWriter, Markout.Formatting.IMarkoutFormatter> serialize = (writer, _) => writer.Write("L1\nL2\nL3\nL4\n");

        var directInner = new StringWriter();
        OutputFormatter.WriteTable(new LineLimitingTextWriter(directInner, maxLines: 2), showHeader: true, serialize, maxRows: null);

        var bufferedInner = new StringWriter();
        var bufferedWriter = new LineLimitingTextWriter(bufferedInner, maxLines: 2);
        bufferedWriter.Write(OutputFormatter.RenderTable(showHeader: true, serialize));

        Assert.Equal(bufferedInner.ToString(), directInner.ToString());
    }

    [Fact]
    public void BuildMemberDrillMap_GivesDistinctStableSelectorsForOverloadedIndexers()
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "T",
            Kind = "class",
            Members =
            [
                new ApiMember { Kind = "property", Name = "Item", Signature = "int this[int index]", GetterToken = 1001 },
                new ApiMember { Kind = "property", Name = "Item", Signature = "int this[string key]", GetterToken = 1002 },
                new ApiMember { Kind = "property", Name = "Count", Signature = "int Count", GetterToken = 1003 },
            ]
        };

        var map = ApiOutputFormatter.BuildMemberDrillMap(type);

        // Overloaded indexers now get distinct parameter-aware canonical signatures
        // (ApiMemberIdentity disambiguates this[int] from this[string] -- see PR #2938),
        // so each overload gets its own round-tripping Stable digest instead of the
        // ambiguous-collision suppression this test previously asserted. The Name:N
        // selector still disambiguates alongside it.
        Assert.True(map.TryGetValue(1001, out var first));
        Assert.NotNull(first.Stable);
        Assert.Matches(@"^Item~[0-9a-f]{10}$", first.Stable);
        Assert.Matches(@"^Item:[12]$", first.Selector);
        Assert.True(map.TryGetValue(1002, out var second));
        Assert.NotNull(second.Stable);
        Assert.Matches(@"^Item~[0-9a-f]{10}$", second.Stable);
        Assert.NotEqual(first.Stable, second.Stable);

        // A uniquely-named property keeps its round-tripping Stable selector.
        Assert.True(map.TryGetValue(1003, out var count));
        Assert.Matches(@"^Count~[0-9a-f]{10}$", count.Stable);
        Assert.Equal("Count", count.Selector);
    }

    [Fact]
    public void PopulateOptimizationOpportunities_RendersRowsForMatchingType()
    {
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class"
        };
        var view = new TypeView();

        ApiOutputFormatter.PopulateOptimizationOpportunities(
            view,
            type,
            ApiAnalysisInspection.OpenTypeAnalysisIndex(typeof(OutputFormatterTests).Assembly.Location),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.PerformanceTriage });

        var rows = Assert.IsType<List<OptimizationOpportunityRow>>(view.OptimizationOpportunityRows);
        Assert.NotEmpty(rows);
        var row = Assert.Single(rows, row =>
            row.Shape == "small-array"
            && row.Member.Contains(nameof(CreateSmallArrayOpportunity), StringComparison.Ordinal));
        Assert.Contains("pt~", row.Candidate, StringComparison.Ordinal);
        Assert.Equal("analysis.allocation", row.Finding);
        Assert.Equal("exact", row.Provenance);
        Assert.Equal("newarr", row.Operation);
        Assert.Contains("0x", row.Token, StringComparison.Ordinal);
        Assert.Equal("low", row.Priority);

        var generatedGenericBox = Assert.Single(rows, row =>
            row.Shape == "generic-parameter-object-box"
            && row.Member.Contains(
                nameof(HasGeneratedGenericObjectBoxOpportunity),
                StringComparison.Ordinal));
        Assert.Equal("medium", generatedGenericBox.Priority);
        Assert.Null(generatedGenericBox.Finding);
        Assert.Equal("unmatched", generatedGenericBox.Provenance);

        Assert.Contains(rows, row =>
            row.Shape == "generic-parameter-object-box"
            && row.Member.Contains(
                nameof(HasGeneratedGenericObjectBoxLambda),
                StringComparison.Ordinal));
    }

    [Fact]
    public void PopulateOptimizationOpportunities_MapsAsyncStateMachineCallToSourceMember()
    {
        var type = new ApiType
        {
            Namespace =
                typeof(OutputFormatterAsyncSiblingFixture).Namespace,
            Name = nameof(OutputFormatterAsyncSiblingFixture),
            Kind = "class"
        };
        var view = new TypeView();

        ApiOutputFormatter.PopulateOptimizationOpportunities(
            view,
            type,
            ApiAnalysisInspection.OpenTypeAnalysisIndex(
                typeof(OutputFormatterTests).Assembly.Location),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.PerformanceTriage
            },
            new PerformanceTriageOptions
            {
                Shapes = ["sync-call-in-async"]
            });

        var row = Assert.Single(
            Assert.IsType<List<OptimizationOpportunityRow>>(
                view.OptimizationOpportunityRows),
            row => row.Member.Contains(
                nameof(
                    OutputFormatterAsyncSiblingFixture
                        .CallsSyncSiblingFromAsync),
                StringComparison.Ordinal));
        Assert.Contains(
            nameof(
                OutputFormatterAsyncSiblingFixture
                    .ReadValueAsync),
            row.Evidence,
            StringComparison.Ordinal);
        Assert.Equal("analysis.call-site", row.Finding);
        Assert.Equal("exact", row.Provenance);
        Assert.Contains("0x", row.EvidenceMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveNext", row.Member, StringComparison.Ordinal);
    }

    [Fact]
    public void PopulateOptimizationOpportunities_AllocationFanoutCountsRepeatedCallSites()
    {
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class"
        };
        var view = new TypeView();

        ApiOutputFormatter.PopulateOptimizationOpportunities(
            view,
            type,
            ApiAnalysisInspection.OpenTypeAnalysisIndex(typeof(OutputFormatterTests).Assembly.Location),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.PerformanceTriage },
            new PerformanceTriageOptions { Shapes = ["allocation-fanout"] });

        var row = Assert.Single(
            Assert.IsType<List<OptimizationOpportunityRow>>(view.OptimizationOpportunityRows),
            row => row.Member.Contains(nameof(CreateAllocationFanout), StringComparison.Ordinal));
        Assert.Equal("allocation-fanout", row.Shape);
        Assert.Null(row.Finding);
        Assert.Equal("aggregate", row.Provenance);
        Assert.Equal("1", row.DirectSites);
        Assert.Equal("4", row.OncePaths);
    }

    [Fact]
    public void RenderTypeSectionsMarkdown_PopulatesOptimizationOpportunitiesWhenRequested()
    {
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = nameof(CreateSmallArrayOpportunity)
                }
            ]
        };
        var options = new MemberOptions
        {
            DllPath = typeof(OutputFormatterTests).Assembly.Location,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.PerformanceTriage }
        };

        var markdown = ApiCommand.RenderTypeSectionsMarkdown(type, options);

        Assert.Contains("Performance Triage", markdown);
        Assert.Contains("small-array", markdown);
        Assert.Contains("| Member | Candidate | Finding | Provenance |", markdown);
        Assert.Contains("| Priority | Confidence |", markdown);
    }

    [Fact]
    public void RenderTypeSectionsMarkdown_ScopesOptimizationOpportunitiesToSelectedMember()
    {
        var method = typeof(OutputFormatterTests).GetMethod(
            nameof(CreateSmallArrayOpportunity),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = nameof(CreateSmallArrayOpportunity),
                    MetadataToken = method.MetadataToken
                }
            ]
        };
        // OverloadIndex selects the single-member detail pipeline, which restricts rows
        // to the selected member instead of the whole declaring type.
        var options = new MemberOptions
        {
            DllPath = typeof(OutputFormatterTests).Assembly.Location,
            OverloadIndex = 1,
            MemberFilter = [nameof(CreateSmallArrayOpportunity)],
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.PerformanceTriage }
        };

        var markdown = ApiCommand.RenderTypeSectionsMarkdown(type, options);

        Assert.Contains("Performance Triage", markdown);
        Assert.Contains(nameof(CreateSmallArrayOpportunity), markdown);
        Assert.Contains("| Member | Candidate | Finding | Provenance |", markdown);
        Assert.DoesNotContain(nameof(CreateTemporaryArray), markdown);
    }

    [Fact]
    public void RenderTypeSectionsMarkdown_MapsLiftedOpportunityToSelectedSourceMember()
    {
        var method = typeof(OutputFormatterTests).GetMethod(
            nameof(HasGeneratedGenericObjectBoxOpportunity),
            System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static)!;
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = nameof(HasGeneratedGenericObjectBoxOpportunity),
                    MetadataToken = method.MetadataToken,
                },
            ],
        };
        var options = new MemberOptions
        {
            DllPath = typeof(OutputFormatterTests).Assembly.Location,
            OverloadIndex = 1,
            MemberFilter = [nameof(HasGeneratedGenericObjectBoxOpportunity)],
            IncludeSections = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.PerformanceTriage,
            },
        };

        var markdown = ApiCommand.RenderTypeSectionsMarkdown(type, options);

        Assert.Contains("generic-parameter-object-box", markdown);
        Assert.Contains(
            nameof(HasGeneratedGenericObjectBoxOpportunity),
            markdown);
    }

    private static int[] CreateSmallArrayOpportunity()
    {
        var value = 3;
        return new int[value];
    }

    private static void CreateTemporaryArray()
    {
        byte[] bytes = new byte[4];
        _ = bytes.Length;
    }

    private static object[] CreateAllocationFanout() =>
    [
        CreateFanoutLeaf(),
        CreateFanoutLeaf(),
        CreateFanoutLeaf(),
    ];

    private static object CreateFanoutLeaf() => new();

    private static async Task<int> CallsFileReadLinesFromAsync(
        string path)
    {
        await Task.Yield();
        return File.ReadLines(path).Count();
    }

    // The local function compiles to a compiler-generated method (<...>g__Make|...)
    // declared on this type, carrying the small-array opportunity from `new int[3]`.
    private static int[] HasGeneratedLocalFunctionOpportunity()
    {
        return Make();
        static int[] Make() => new int[3];
    }

    private static bool HasGeneratedGenericObjectBoxOpportunity<T>(
        T left,
        T right)
    {
        return EqualsCore(left, right);
        static bool EqualsCore(T x, T y) => x!.Equals(y);
    }

    private static Func<T, bool> HasGeneratedGenericObjectBoxLambda<T>(T right)
        => left => left!.Equals(right);

    [Fact]
    public void RenderOptimizationOpportunities_SuppressesGeneratedMethods()
    {
        var type = new ApiType
        {
            Namespace = typeof(OutputFormatterTests).Namespace,
            Name = nameof(OutputFormatterTests),
            Kind = "class",
            Members =
            [
                new ApiMember { Kind = "method", Name = nameof(HasGeneratedLocalFunctionOpportunity) }
            ]
        };
        var options = new MemberOptions
        {
            DllPath = typeof(OutputFormatterTests).Assembly.Location,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.PerformanceTriage }
        };

        // Generated implementation details are not actionable source fixes and are not
        // selectable at member scope (the API surface omits them), so they are suppressed
        // unconditionally — including under --all — to keep the contract consistent (#1267).
        var defaultMarkdown = ApiCommand.RenderTypeSectionsMarkdown(type, options);
        var allMarkdown = ApiCommand.RenderTypeSectionsMarkdown(type, options with { IncludeAll = true });

        Assert.DoesNotContain("g__Make", defaultMarkdown);
        Assert.DoesNotContain("g__Make", allMarkdown);
    }

    [Fact]
    public void OptimizationOpportunitiesQuery_PreservesGeneratedGenericObjectBox()
    {
        var rows = QueryOptimizationOpportunities();

        var row = Assert.Single(rows!, row =>
            row.Member.Contains(
                nameof(HasGeneratedGenericObjectBoxOpportunity),
                StringComparison.Ordinal)
            && row.Shape == "generic-parameter-object-box");
        Assert.Equal("medium", row.Priority);
        Assert.Equal("medium", row.Confidence);
        Assert.Null(row.Allocation);
    }

    [Fact]
    public void OptimizationOpportunitiesQuery_OrdersByTriagePriority()
    {
        var rows = QueryOptimizationOpportunities();

        Assert.NotNull(rows);
        Assert.NotEmpty(rows);
        // Root Reach is the leverage join: at least one opportunity sits in a reached method.
        Assert.Contains(rows, r => r.RootReach > 0);

        // Verify the exposed default composite key is monotonically non-increasing.
        static (int priority, int conf, int weight, int reach) Key(OptimizationOpportunitySummary r) =>
            (r.Priority == "high" ? 2 : r.Priority == "medium" ? 1 : 0,
             r.Confidence == "high" ? 2 : r.Confidence == "medium" ? 1 : 0,
             r.Weight == "high" ? 2 : r.Weight == "medium" ? 1 : r.Weight == "low" ? 0 : -1,
             r.RootReach);
        for (int i = 1; i < rows.Count; i++)
            Assert.True(Compare(Key(rows[i - 1]), Key(rows[i])) >= 0,
                $"rows not ordered by triage priority at index {i}");

        static int Compare((int priority, int conf, int weight, int reach) a, (int priority, int conf, int weight, int reach) b)
        {
            if (a.priority != b.priority) return a.priority - b.priority;
            if (a.conf != b.conf) return a.conf - b.conf;
            if (a.weight != b.weight) return a.weight - b.weight;
            return a.reach - b.reach;
        }
    }

    [Fact]
    public void ProjectOptimizationOpportunity_OmitsUnknownModuleVersionId()
    {
        var opportunity = Opp(
            "UnknownModule",
            inLoop: false,
            confidence: "medium",
            rootReach: 1,
            shape: "small-array");

        var projected =
            LibraryMetadataService.ProjectOptimizationOpportunity(
                opportunity);

        Assert.Null(projected.ModuleVersionId);
    }

    [Fact]
    public void ProjectOptimizationOpportunity_SeparatesAggregateSupportCoordinate()
    {
        var opportunity = Opp(
            "AggregateScan",
            inLoop: true,
            confidence: "low",
            rootReach: 1,
            shape: "scan-method-in-loop-call") with
        {
            Provenance =
                PerformanceTriageProvenance.Aggregate,
            SupportingCallSite =
                new OptimizationSupportingCallSite(
                    0x06000002,
                    0x001F)
                {
                    SourceFinding =
                        AnalysisFindings
                            .CallSiteDescriptor.Id,
                    Operation = "call",
                    OperandToken = 0x0A000001,
                },
        };

        var projected =
            LibraryMetadataService
                .ProjectOptimizationOpportunity(
                    opportunity);

        Assert.Equal("aggregate", projected.Provenance);
        Assert.Null(projected.Finding);
        Assert.Null(projected.IL);
        Assert.Equal(
            AnalysisFindings.CallSiteDescriptor.Id,
            projected.SupportingFinding);
        Assert.Equal(
            "0x06000002",
            projected.SupportingEvidenceMethod);
        Assert.Equal(
            "IL_001F",
            projected.SupportingIL);
        Assert.Equal(
            "call",
            projected.SupportingOperation);
        Assert.Equal(
            "0x0A000001",
            projected.SupportingToken);
    }

    // #1623 rung 5: a labeled, non-vacuous ranking guard for the Performance Triage
    // model. Unlike the monotonicity check above (which re-derives the production key and
    // only proves self-consistency), this asserts the model's intended priority on seeded
    // opportunities: in-loop and confidence dominate raw root-reach, so pay-dirt outranks
    // higher-reach known-good, and within a tier reach is the tie-break. Reordering the
    // key (e.g. ranking reach above loop/confidence) fails here.
    [Fact]
    public void OrderByTriagePriority_RanksSeededPayDirtAboveKnownGood()
    {
        var opportunities = new[]
        {
            Opp("KnownGoodHighReach", inLoop: false, confidence: "low", rootReach: 999, shape: "small-array"),
            Opp("KnownGoodHighConfNoLoop", inLoop: false, confidence: "high", rootReach: 5, shape: "stackalloc-candidate"),
            Opp("PayDirtLinqScanInLoop", inLoop: true, confidence: "medium", rootReach: 1, shape: "linq-scan-in-loop"),
            Opp("PayDirtLoopHigh", inLoop: true, confidence: "high", rootReach: 1, shape: "allocation-hotspot"),
            Opp("GenericLoopHigh", inLoop: true, confidence: "high", rootReach: 100, shape: "capturing-delegate"),
            Opp("KnownGoodReachLow", inLoop: false, confidence: "low", rootReach: 7, shape: "small-array"),
        };

        var ordered = LibraryMetadataService.OrderByTriagePriority(opportunities)
            .Select(o => o.Method.Name)
            .ToList();

        int payDirtHigh = ordered.IndexOf("PayDirtLoopHigh");
        int payDirtScan = ordered.IndexOf("PayDirtLinqScanInLoop");
        int knownHighConf = ordered.IndexOf("KnownGoodHighConfNoLoop");
        int genericLoopHigh = ordered.IndexOf("GenericLoopHigh");
        int knownHighReach = ordered.IndexOf("KnownGoodHighReach");
        int knownReachLow = ordered.IndexOf("KnownGoodReachLow");

        // Algorithmic pay-dirt outranks a generic repeated allocation even when its
        // confidence and reach are lower.
        Assert.True(payDirtHigh < knownHighConf, "in-loop high must precede not-loop high");
        Assert.True(payDirtScan < knownHighConf, "in-loop medium must precede not-loop high");
        Assert.True(payDirtScan < knownHighReach, "in-loop must precede higher-reach not-loop");
        Assert.True(payDirtScan < genericLoopHigh, "algorithmic amplification must precede generic loop allocation");
        // Within the high-priority tier, higher confidence first.
        Assert.True(payDirtHigh < payDirtScan, "in-loop high must precede in-loop medium");
        // Within not-in-loop, higher confidence beats higher reach.
        Assert.True(knownHighConf < knownHighReach, "high confidence must precede higher-reach low");
        // Within the same (loop, confidence) tier, higher reach is the tie-break.
        Assert.True(knownHighReach < knownReachLow, "higher reach must precede lower reach at equal tier");
    }

    [Fact]
    public void PerformanceGroupRows_PreserveGlobalTriageOrderAcrossKinds()
    {
        var view = new LibraryInspectionView(new LibraryInspection
        {
            PerformanceTriageOpportunities =
            [
                Opp(
                    "Algorithmic",
                    inLoop: true,
                    confidence: "high",
                    rootReach: 1,
                    shape: "string-build-in-loop"),
                Opp(
                    "Boxing",
                    inLoop: true,
                    confidence: "high",
                    rootReach: 1,
                    shape: "box-value-type"),
                Opp(
                    "Array",
                    inLoop: false,
                    confidence: "medium",
                    rootReach: 1,
                    shape: "small-array"),
            ],
        });

        var rows = view.PerformanceGroupRows(PerformanceKinds.Sections);

        Assert.Equal(
            [
                MarkoutInline.Code("Ns.Type.Algorithmic()"),
                MarkoutInline.Code("Ns.Type.Boxing()"),
                MarkoutInline.Code("Ns.Type.Array()"),
            ],
            rows.Select(row => row.Member));
        Assert.Equal(
            ["Loop Hot Paths", "Boxing", "Arrays"],
            rows.Select(row => row.Kind));
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_AppliesPaydirtPredicatesAfterRanking()
    {
        var opportunities = new[]
        {
            Opp("LoopMediumDelegate", inLoop: true, confidence: "medium", rootReach: 10, shape: "capturing-delegate"),
            Opp("LoopHighDelegateLowReach", inLoop: true, confidence: "high", rootReach: 1, shape: "capturing-delegate"),
            Opp("LoopHighArray", inLoop: true, confidence: "high", rootReach: 99, shape: "small-array"),
            Opp("NoLoopHighDelegate", inLoop: false, confidence: "high", rootReach: 500, shape: "capturing-delegate"),
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    LoopOnly = true,
                    MinConfidence = "High",
                    Shapes = ["capturing-delegate"],
                    Top = 1
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["LoopHighDelegateLowReach"], filtered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_AppliesWherePredicates()
    {
        var opportunities = new[]
        {
            Opp("BoxLoop", inLoop: true, confidence: "high", rootReach: 1, shape: "box-value-type") with
            {
                RuntimeAllocationType = "boxed System.Int32",
                PathContext = "loop body",
            },
            Opp("ArrayLoop", inLoop: true, confidence: "high", rootReach: 100, shape: "small-array") with
            {
                RuntimeAllocationType = "System.Int32[]",
                PathContext = "loop body",
            },
            Opp("BoxCold", inLoop: false, confidence: "medium", rootReach: 1000, shape: "box-value-type") with
            {
                RuntimeAllocationType = "boxed System.Guid",
                PathContext = "straight-line",
            },
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    Where =
                    [
                        "Allocation=boxed *",
                        "Path=loop body",
                        "Confidence>=medium",
                    ],
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["BoxLoop"], filtered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_DoesNotMatchMissingNumericFields()
    {
        var opportunities = new[]
        {
            Opp("Fanout", inLoop: false, confidence: "high", rootReach: 1, shape: "allocation-fanout") with
            {
                OnceAllocationPaths = 4,
            },
            Opp("Local", inLoop: false, confidence: "high", rootReach: 1, shape: "allocation-hotspot"),
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions { Where = ["OncePaths!=5"] })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["Fanout"], filtered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_FiltersByWeight()
    {
        var opportunities = new[]
        {
            Opp("HighWeight", inLoop: false, confidence: "medium", rootReach: 1, shape: "small-array", weight: "high"),
            Opp("MediumWeight", inLoop: false, confidence: "medium", rootReach: 1, shape: "small-array", weight: "medium"),
            Opp("NoWeight", inLoop: false, confidence: "medium", rootReach: 1000, shape: "linq-scan-in-loop"),
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    Where = ["Weight>=medium"],
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(2, filtered.Count);
        Assert.Contains("HighWeight", filtered);
        Assert.Contains("MediumWeight", filtered);
        Assert.DoesNotContain("NoWeight", filtered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_FiltersPrioritySeparatelyFromConfidence()
    {
        var opportunities = new[]
        {
            Opp("AlgorithmicLowConfidence", inLoop: true, confidence: "low", rootReach: 1, shape: "scan-method-in-recursive-traversal"),
            Opp("CacheFactoryHighConfidence", inLoop: false, confidence: "high", rootReach: 1, shape: "cache-lookup-factory-delegate"),
            Opp("GenericLoopHighConfidence", inLoop: true, confidence: "high", rootReach: 1, shape: "capturing-delegate"),
            Opp("OneShotHighConfidence", inLoop: false, confidence: "high", rootReach: 100, shape: "stackalloc-candidate"),
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions { Where = ["Priority>=medium"] })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(
            ["CacheFactoryHighConfidence", "GenericLoopHighConfidence", "AlgorithmicLowConfidence"],
            filtered);
    }

    [Fact]
    public void TriagePriority_DoesNotPromoteEscapeUnknownSmallArrayByWeightAlone()
    {
        var opportunity = Opp(
            "ReflectionParamsArray",
            inLoop: true,
            confidence: "medium",
            rootReach: 59,
            shape: "small-array",
            weight: "high");

        Assert.Equal("medium", LibraryMetadataService.TriagePriority(opportunity));
    }

    [Fact]
    public void TriagePriority_RecursiveScanWithoutSourceIdentity_IsMedium()
    {
        var opportunity = Opp(
            "RecursiveScan",
            inLoop: true,
            confidence: "low",
            rootReach: 1,
            shape: "scan-method-in-recursive-traversal");

        Assert.Equal("medium", LibraryMetadataService.TriagePriority(opportunity));
    }

    [Fact]
    public void TriagePriority_GenericObjectBoxRequiresLoopEvidenceForHigh()
    {
        var once = Opp(
            "GenericEqualsOnce",
            inLoop: false,
            confidence: "medium",
            rootReach: 100,
            shape: "generic-parameter-object-box");
        var repeated = once with
        {
            Method = once.Method with { Name = "GenericEqualsRepeated" },
            InLoop = true,
            Multiplicity = "loop",
        };

        Assert.Equal("medium", LibraryMetadataService.TriagePriority(once));
        Assert.Equal("high", LibraryMetadataService.TriagePriority(repeated));
    }

    [Fact]
    public void TriagePriority_CallerLoopEvidenceDoesNotChangeRanking()
    {
        var baseline = Opp(
            "GenericEquals",
            inLoop: false,
            confidence: "medium",
            rootReach: 100,
            shape: "generic-parameter-object-box");
        var callerLoop = baseline with
        {
            CallerLoop = new CallerLoopEvidence(
                1,
                []),
        };

        Assert.Equal(
            LibraryMetadataService.TriagePriority(baseline),
            LibraryMetadataService.TriagePriority(callerLoop));

        var ordinary = baseline with
        {
            Shape = "capturing-delegate",
        };
        Assert.Equal(
            LibraryMetadataService.TriagePriority(ordinary),
            LibraryMetadataService.TriagePriority(
                ordinary with
                {
                    CallerLoop = callerLoop.CallerLoop,
                }));
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_AllowsOperatorsInsidePredicateValues()
    {
        var opportunities = new[]
        {
            Opp("ComparisonEvidence", inLoop: false, confidence: "medium", rootReach: 1, shape: "small-array") with
            {
                Evidence = "value >= threshold",
            },
            Opp("OtherEvidence", inLoop: false, confidence: "medium", rootReach: 1, shape: "small-array") with
            {
                Evidence = "plain value",
            },
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    Where = ["Evidence=*>=*"],
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["ComparisonEvidence"], filtered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_AppliesExplicitOrderBeforeTop()
    {
        var opportunities = new[]
        {
            Opp("LowReach", inLoop: true, confidence: "high", rootReach: 1, shape: "box-value-type"),
            Opp("HighReach", inLoop: false, confidence: "low", rootReach: 100, shape: "box-value-type"),
            Opp("MediumReach", inLoop: true, confidence: "medium", rootReach: 50, shape: "small-array"),
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    Where = ["Shape=box-value-type"],
                    OrderBy = "RootReach desc",
                    Top = 1,
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["HighReach"], filtered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_OrdersByWeight()
    {
        var opportunities = new[]
        {
            Opp("HighWeightHighReach", inLoop: false, confidence: "medium", rootReach: 10, shape: "small-array", weight: "high"),
            Opp("LowWeightHighReach", inLoop: false, confidence: "medium", rootReach: 100, shape: "small-array", weight: "low"),
            Opp("HighWeightLowReach", inLoop: false, confidence: "medium", rootReach: 1, shape: "small-array", weight: "high"),
            Opp("NoWeight", inLoop: false, confidence: "medium", rootReach: 1000, shape: "linq-scan-in-loop"),
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    OrderBy = "Weight desc,RootReach desc",
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["HighWeightHighReach", "HighWeightLowReach", "LowWeightHighReach", "NoWeight"], filtered);
    }

    [Fact]
    public void FilterAndOrderTriageOpportunities_OrdersIlNumerically()
    {
        var opportunities = new[]
        {
            Opp("OffsetLarge", inLoop: false, confidence: "medium", rootReach: 1, shape: "small-array") with { ILOffset = 0x10000 },
            Opp("OffsetSmall", inLoop: false, confidence: "medium", rootReach: 1, shape: "small-array") with { ILOffset = 0x2000 },
        };

        var filtered = LibraryMetadataService.FilterAndOrderTriageOpportunities(
                opportunities,
                new PerformanceTriageOptions
                {
                    OrderBy = "IL asc",
                })
            .Select(opportunity => opportunity.Method.Name)
            .ToList();

        Assert.Equal(["OffsetSmall", "OffsetLarge"], filtered);
    }

    static ILInspector.Analysis.OptimizationOpportunity Opp(string name, bool inLoop, string confidence, int rootReach, string shape, string? multiplicity = null, string? weight = null)
    {
        var declaring = ILInspector.Analysis.TypeRef.Definition("Asm", "Ns", "Type");
        var method = new ILInspector.Analysis.MethodIdentity(
            "Asm",
            System.Guid.Empty,
            declaring,
            name,
            [],
            ILInspector.Analysis.TypeRef.CoreLib("System", "Void"),
            MetadataToken: 0x06000001,
            IsStatic: true);
        return new ILInspector.Analysis.OptimizationOpportunity(
            method, shape, "evidence", "fix", confidence, inLoop, ILOffset: null, Caveat: null, RootReach: rootReach)
        {
            Multiplicity = multiplicity,
            Weight = weight,
        };
    }

    static List<OptimizationOpportunitySummary>? QueryOptimizationOpportunities(
        PerformanceTriageOptions? options = null)
    {
        string path = typeof(OutputFormatterTests).Assembly.Location;
        var result = OptimizationOpportunitiesQuery.Execute(
            LibraryBodyIndex.Open(path),
            includeAllocationFanout:
                options?.IncludesAllocationFanout == true);
        var inspection = new LibraryInspection
        {
            PerformanceTriageOptions =
                options ?? PerformanceTriageOptions.Default,
        };
        LibraryMetadataService.ApplyOptimizationOpportunitiesResult(
            path,
            inspection,
            new VerboseLogger(false),
            result);
        return inspection.OptimizationOpportunities;
    }

    [Fact]
    public void IteratesInLoop_TrustsSemanticMultiplicityOverStructuralInLoop()
    {
        // Structurally in a loop but a return/throw early-exit (Multiplicity conditional)
        // is NOT a hot loop; a genuine loop is; a null multiplicity falls back to InLoop.
        var loopEarlyExit = Opp("LoopEarlyExit", inLoop: true, confidence: "high", rootReach: 1, shape: "capturing-delegate", multiplicity: "conditional");
        var genuineLoop = Opp("GenuineLoop", inLoop: true, confidence: "high", rootReach: 1, shape: "allocation-hotspot", multiplicity: "loop");
        var unknownInLoop = Opp("UnknownInLoop", inLoop: true, confidence: "high", rootReach: 1, shape: "allocation-hotspot", multiplicity: null);
        var unknownNotInLoop = Opp("UnknownNotInLoop", inLoop: false, confidence: "high", rootReach: 1, shape: "allocation-hotspot", multiplicity: null);

        Assert.False(LibraryMetadataService.IteratesInLoop(loopEarlyExit));
        Assert.True(LibraryMetadataService.IteratesInLoop(genuineLoop));
        Assert.True(LibraryMetadataService.IteratesInLoop(unknownInLoop));
        Assert.False(LibraryMetadataService.IteratesInLoop(unknownNotInLoop));

        // The loop early-exit is deprioritized below a genuine loop despite equal confidence/reach.
        var ordered = LibraryMetadataService.OrderByTriagePriority([loopEarlyExit, genuineLoop])
            .Select(o => o.Method.Name).ToList();
        Assert.Equal(["GenuineLoop", "LoopEarlyExit"], ordered);

        // --loop keeps only genuine loops.
        var loopOnly = LibraryMetadataService.FilterAndOrderTriageOpportunities(
            [loopEarlyExit, genuineLoop],
            new PerformanceTriageOptions { LoopOnly = true })
            .Select(o => o.Method.Name).ToList();
        Assert.Equal(["GenuineLoop"], loopOnly);
    }

    [Fact]
    public void OptimizationOpportunitiesQuery_SuppressesGeneratedMethodsExceptGenericObjectBox()
    {
        var rows = QueryOptimizationOpportunities();

        Assert.NotNull(rows);
        Assert.NotEmpty(rows);
        Assert.DoesNotContain(rows, r =>
            r.Shape != "generic-parameter-object-box"
            && (r.Member.Contains("<>c")
                || r.Member.Contains(">g__")
                || r.Member.Contains(">b__")
                || r.Member.Contains(">d__")
                || r.Member.Contains("c__Display")
                || r.Member.Contains("<PrivateImplementationDetails>")));
    }

    [Fact]
    public void IncludePerformanceOpportunity_SuppressesLiftedGeneratedFrameworkMethod()
    {
        var opportunity = Opp(
            "<Build>b__0",
            inLoop: false,
            confidence: "medium",
            rootReach: 1,
            shape: "generic-parameter-object-box");
        opportunity = opportunity with
        {
            Method = opportunity.Method with
            {
                DeclaringType = ILInspector.Analysis.TypeRef.Definition(
                    "Asm",
                    "Ns",
                    "GeneratedOuter+<>c__DisplayClass0_0"),
            },
        };

        Assert.False(LibraryMetadataService.IncludePerformanceOpportunity(
            opportunity,
            new HashSet<TypeRef>
            {
                TypeRef.Definition("Asm", "Ns", "GeneratedOuter"),
            }));
    }

    [Fact]
    public void IncludePerformanceOpportunity_DoesNotTreatDisplayCollisionAsGeneratedFramework()
    {
        static TypeRef Exact(
            TypeReferenceOrigin.CurrentAssembly origin,
            string @namespace,
            params string[] segments)
        {
            MetadataTypeDefinitionName name =
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        @namespace,
                        [.. segments]))
                .Name;
            TypeRef type = TypeRef.Definition(
                "Asm",
                @namespace,
                string.Join('+', segments));
            typeof(TypeRef).GetProperty(
                    nameof(TypeRef.Resolution),
                    BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(
                    type,
                    new ResolvableTypeReference(
                        origin,
                        name));
            return type;
        }

        var origin = Assert.IsType<TypeReferenceOrigin.CurrentAssembly>(
            typeof(TypeReferenceOrigin.CurrentAssembly)
                .GetConstructors(
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(constructor =>
                    constructor.GetParameters() is
                    [
                        {
                            ParameterType:
                            var parameterType
                        },
                    ]
                    && parameterType
                        == typeof(AssemblyReferenceIdentity))
                .Invoke([null]));
        TypeRef compilerGenerated =
            Exact(
                origin,
                "Ns",
                "GeneratedOuter",
                "Leaf",
                "<>c__DisplayClass0_0");
        TypeRef collidingGenerated =
            Exact(
                origin,
                "Ns.GeneratedOuter",
                "Leaf");
        Assert.Equal(
            collidingGenerated.ToQualifiedDisplayString()
                + ".<>c__DisplayClass0_0",
            compilerGenerated.ToQualifiedDisplayString());

        var opportunity = Opp(
            "<Build>b__0",
            inLoop: false,
            confidence: "medium",
            rootReach: 1,
            shape: "generic-parameter-object-box");
        opportunity = opportunity with
        {
            Method = opportunity.Method with
            {
                DeclaringType = compilerGenerated,
            },
        };

        Assert.True(LibraryMetadataService.IncludePerformanceOpportunity(
            opportunity,
            new HashSet<TypeRef> { collidingGenerated }));
    }

    [Fact]
    public void BuildShapeView_GroupsMethodOverloadsByLogicalName()
    {
        var type = new ApiType
        {
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new() { Kind = "method", Name = "Parse", Signature = "Widget Parse(string value)" },
                new() { Kind = "method", Name = "Parse", Signature = "Widget Parse(ReadOnlySpan<char> value)" },
                new() { Kind = "method", Name = "Format", Signature = "string Format()" },
            ]
        };

        var view = ApiOutputFormatter.BuildShapeView(type, foundIn: null, packageName: null, packageVersion: null, []);

        var methods = Assert.Single(view.Members);
        Assert.Equal("Methods (2 logical, 3 overloads)", methods.Text);
        Assert.NotNull(methods.Children);
        Assert.Equal(["string Format()", "Parse (2 overloads)"], methods.Children.Select(c => c.Text));
    }

    [Fact]
    public void BuildShapeView_KeepsSingleOverloadSignature()
    {
        var type = new ApiType
        {
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new() { Kind = "method", Name = "Format", Signature = "string Format()" },
            ]
        };

        var view = ApiOutputFormatter.BuildShapeView(type, foundIn: null, packageName: null, packageVersion: null, []);

        var methods = Assert.Single(view.Members);
        Assert.Equal("Methods (1)", methods.Text);
        var child = Assert.Single(methods.Children!);
        Assert.Equal("string Format()", child.Text);
    }

    [Fact]
    public void BuildShapeView_MemberLimitCountsCollapsedOverloadGroups()
    {
        var type = new ApiType
        {
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new() { Kind = "method", Name = "Alpha", Signature = "void Alpha()" },
                new() { Kind = "method", Name = "Alpha", Signature = "void Alpha(int value)" },
                new() { Kind = "method", Name = "Beta", Signature = "void Beta()" },
            ]
        };

        var view = ApiOutputFormatter.BuildShapeView(
            type,
            foundIn: null,
            packageName: null,
            packageVersion: null,
            memberFilter: [],
            memberLimit: 1);

        var methods = Assert.Single(view.Members);
        Assert.Equal("Methods (1 logical, 2 overloads)", methods.Text);
        var child = Assert.Single(methods.Children!);
        Assert.Equal("Alpha (2 overloads)", child.Text);

        var expanded = ApiOutputFormatter.BuildShapeView(
            type,
            foundIn: null,
            packageName: null,
            packageVersion: null,
            memberFilter: [],
            verbosity: Verbosity.Normal,
            memberLimit: 1);

        var expandedMethods = Assert.Single(expanded.Members);
        Assert.Equal("Methods (1)", expandedMethods.Text);
        var expandedChild = Assert.Single(expandedMethods.Children!);
        Assert.Equal("void Alpha()", expandedChild.Text);
    }

    [Fact]
    public void BuildShapeView_ExpandedOperatorLimitUsesDisplayOrder()
    {
        var type = new ApiType
        {
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new() { Kind = "operator", Name = "op_Addition", Signature = "Widget op_Addition(Widget left, Widget right)" },
                new() { Kind = "operator", Name = "op_Explicit", Signature = "Widget op_Explicit(int value)" },
            ]
        };

        var view = ApiOutputFormatter.BuildShapeView(
            type,
            foundIn: null,
            packageName: null,
            packageVersion: null,
            memberFilter: [],
            verbosity: Verbosity.Normal,
            memberLimit: 1);

        var operators = Assert.Single(view.Members);
        var child = Assert.Single(operators.Children!);
        Assert.Equal("Widget op_Explicit(int value)", child.Text);
    }

    [Fact]
    public void GetMemberSignatureSortKey_StripsMethodGenericListOnly()
    {
        var member = new ApiMember
        {
            Kind = "method",
            Name = "Task",
            Signature = "System.Threading.Tasks.Task<T> Task<T>(T value)"
        };

        Assert.Equal(
            "System.Threading.Tasks.Task<T> Task(T value)",
            ApiOutputFormatter.GetMemberSignatureSortKey(member));
    }

    [Fact]
    public void PopulateMemberSignature_EscapesKeywordMethodAndQualifiedTypeNames()
    {
        var keywordMethodType = new ApiType
        {
            Namespace = "Probe",
            Name = "KeywordMethods",
            Kind = "class",
            Members =
            [
                new ApiMember { Kind = "method", Name = "return", Signature = "int return(int value)" },
            ]
        };
        var keywordTypeReturn = new ApiType
        {
            Namespace = "Probe",
            Name = "KeywordMethods",
            Kind = "class",
            Members =
            [
                new ApiMember { Kind = "method", Name = "CreateKeywordType", Signature = "Probe.class CreateKeywordType()" },
            ]
        };
        var defaultStringLiteral = new ApiType
        {
            Namespace = "Probe",
            Name = "KeywordMethods",
            Kind = "class",
            Members =
            [
                new ApiMember { Kind = "method", Name = "DefaultPath", Signature = "void DefaultPath(string value = \"config.in.txt\")" },
            ]
        };
        var options = new ApiOptions { Verbosity = Verbosity.Normal };
        var methodView = new TypeView();
        var returnTypeView = new TypeView();
        var defaultStringView = new TypeView();

        ApiOutputFormatter.PopulateMemberSignature(methodView, keywordMethodType, options);
        ApiOutputFormatter.PopulateMemberSignature(returnTypeView, keywordTypeReturn, options);
        ApiOutputFormatter.PopulateMemberSignature(defaultStringView, defaultStringLiteral, options);

        var methodSignature = Assert.Single(Assert.IsType<List<MemberSignatureRow>>(methodView.SignatureRows));
        var returnTypeSignature = Assert.Single(Assert.IsType<List<MemberSignatureRow>>(returnTypeView.SignatureRows));
        var defaultStringSignature = Assert.Single(Assert.IsType<List<MemberSignatureRow>>(defaultStringView.SignatureRows));
        Assert.Equal("<code>public int @return(int value)</code>", methodSignature.Signature);
        Assert.Equal("<code>public Probe.@class CreateKeywordType()</code>", returnTypeSignature.Signature);
        Assert.Equal("<code>public void DefaultPath(string value = \"config.in.txt\")</code>", defaultStringSignature.Signature);
    }

    [Fact]
    public void TypeViewSchema_DoesNotOwnFirstClassMemberRows()
    {
        var schema = ApiViewContext.Default.GetSchemaInfo<TypeView>()!.ToDocumentSchema();

        Assert.Null(schema.GetSection("Method Groups"));
        Assert.Null(schema.GetSection("Methods"));
        Assert.Null(schema.GetSection("Operators"));
        Assert.Null(schema.GetSection("Explicit Interface Implementations"));
        Assert.Null(schema.GetSection("Extension Methods"));
        Assert.Null(schema.GetSection("Events"));
    }

    [Fact]
    public void TypeDocumentSchema_MergesFirstClassMemberViews()
    {
        var schema = ApiCommand.GetTypeDocumentSchema(new MemberOptions());

        Assert.NotNull(schema.GetSection("Method Groups"));
        Assert.NotNull(schema.GetSection("Methods"));
        Assert.NotNull(schema.GetSection("Operators"));
        Assert.NotNull(schema.GetSection("Explicit Interface Implementations"));
        Assert.NotNull(schema.GetSection("Extension Methods"));
        Assert.NotNull(schema.GetSection("Events"));
    }

    [Fact]
    public void RenderManifestFormatter_CapturesStructuredSectionsColumnsAndFields()
    {
        var schema = new DocumentSchema()
            .Add("Methods", "column", "Field", "Signature | Display")
            .Add("Library Info", "field", "Assembly Version")
            .Add("Other Section", "field", "Methods");
        var formatter = new RenderManifestFormatter(schema);
        var options = MarkoutWriterOptions.Default;
        var sectionLevel = Math.Clamp(2 + options.HeadingLevelOffset, 1, 6);
        var nestedLevel = Math.Clamp(4 + options.HeadingLevelOffset, 1, 6);
        formatter.BeginDocument(options);

        formatter.FormatHeading(TextWriter.Null, sectionLevel, "Methods", context: null);
        formatter.FormatHeading(TextWriter.Null, nestedLevel, "Method Details", context: null);
        formatter.FormatTable(
            TextWriter.Null,
            ["Field", "Signature | Display"],
            [["Run", "void Run()"]],
            skippedRows: 0,
            MarkoutWriterOptions.Default);
        formatter.FormatHeading(TextWriter.Null, sectionLevel, "Library Info", context: null);
        formatter.BeginTable(
            TextWriter.Null,
            ["Property", "Contents"],
            MarkoutWriterOptions.Default);
        formatter.WriteRow(TextWriter.Null, ["Assembly Version", "1.0.0.0"]);
        formatter.EndTable(TextWriter.Null, skippedRows: 0);
        formatter.FormatHeading(TextWriter.Null, sectionLevel, "Other Section", context: null);
        formatter.FormatFields(
            TextWriter.Null,
            [new MarkoutField("Methods", "polluting value")],
            bold: false);
        formatter.BeginDocument(options);
        formatter.FormatFields(
            TextWriter.Null,
            [new MarkoutField("Kind", "class")],
            bold: false);

        var columns = Assert.IsAssignableFrom<IReadOnlySet<string>>(
            formatter.Manifest.GetTableColumns("Methods"));
        Assert.Contains("Field", columns);
        Assert.Contains("Signature | Display", columns);
        Assert.Null(formatter.Manifest.GetTableColumns("Method Details"));
        Assert.Null(formatter.Manifest.GetFields("Methods"));
        var fields = Assert.IsAssignableFrom<IReadOnlySet<string>>(
            formatter.Manifest.GetFields("Library Info"));
        Assert.Contains("Assembly Version", fields);
        Assert.DoesNotContain("Methods", fields);
        var otherFields = Assert.IsAssignableFrom<IReadOnlySet<string>>(
            formatter.Manifest.GetFields("Other Section"));
        Assert.Contains("Methods", otherFields);
        Assert.DoesNotContain("Kind", otherFields);
    }

    [Fact]
    public void RenderManifestFormatter_DoesNotTreatTitleTextAsAField()
    {
        var result = new InspectionResult
        {
            PackageName = "Test.Published.PackageInfoDiscovery",
            Version = "1.0.0",
            Authors = "tests"
        };
        var writerOptions = new MarkoutWriterOptions
        {
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PackageSections.PackageInfo
            }
        };
        var schema = new DocumentSchema()
            .Add(PackageSections.PackageInfo, "field", "Authors", "Published", "Version");
        var manifest = RenderManifestFormatter.Capture(
            new InspectionResultView(result),
            InspectionContext.Default,
            writerOptions,
            schema);

        var fields = Assert.IsAssignableFrom<IReadOnlySet<string>>(
            manifest.GetFields(PackageSections.PackageInfo));
        Assert.Contains("Authors", fields);
        Assert.DoesNotContain("Published", fields);
    }

    [Fact]
    public void MemberSignature_ShowsDegradedDecodeMarker()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Worker",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = "Run",
                    Signature = "object Run()",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "object",
                        MemberName = "Run"
                    },
                    SignatureDecodeStatus = SignatureDecodeStatus.Degraded
                }
            ]
        };
        var view = new TypeView();

        ApiOutputFormatter.PopulateMemberSignature(view, type, new MemberOptions());

        var row = Assert.Single(Assert.IsType<List<MemberSignatureRow>>(view.SignatureRows));
        Assert.Equal("degraded", row.Decode);
    }

    [Fact]
    public void SignatureDecodeIsEmpty_HidesColumnOnlyWhenNoMemberDegraded()
    {
        Assert.True(TypeView.SignatureDecodeIsEmpty(null));
        Assert.True(TypeView.SignatureDecodeIsEmpty(
        [
            new MemberSignatureRow("void A()", "aaaa", "M:A", null, null),
            new MemberSignatureRow("void B()", "bbbb", "M:B", "", null),
        ]));

        // A degraded member must keep the Decode column so the failure marker stays visible.
        Assert.False(TypeView.SignatureDecodeIsEmpty(
        [
            new MemberSignatureRow("void A()", "aaaa", "M:A", null, null),
            new MemberSignatureRow("void B()", "bbbb", "M:B", "degraded", null),
        ]));
    }

    [Fact]
    public void MemberIndexDecodeIsEmpty_HidesColumnOnlyWhenNoMemberDegraded()
    {
        Assert.True(MemberIndexView.DecodeIsEmpty(null));
        Assert.True(MemberIndexView.DecodeIsEmpty(
        [
            new MemberIndexRow("A:0", "A~0", "M:A", null, "d0"),
            new MemberIndexRow("B:0", "B~0", "M:B", "", "d1"),
        ]));

        // A degraded member must keep the Decode column so the failure marker stays visible.
        Assert.False(MemberIndexView.DecodeIsEmpty(
        [
            new MemberIndexRow("A:0", "A~0", "M:A", null, "d0"),
            new MemberIndexRow("B:0", "B~0", "M:B", "degraded", "d1"),
        ]));
    }

    [Fact]
    public async Task PopulateMemberSections_CollectsDegradedSignaturesForStderrWarning()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Worker",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = "Run",
                    Signature = "object Run()",
                    SignatureModel = new ApiSignature { ReturnType = "object", MemberName = "Run" },
                    SignatureDecodeStatus = SignatureDecodeStatus.Degraded
                },
                new ApiMember
                {
                    Kind = "method",
                    Name = "Ok",
                    Signature = "void Ok()",
                    SignatureModel = new ApiSignature { ReturnType = "void", MemberName = "Ok" }
                }
            ]
        };
        var view = new TypeView();

        ApiOutputFormatter.PopulateMemberSections(
            view, new MethodsView(), new OperatorsView(), new ExplicitInterfaceImplementationsView(),
            new ExtensionMethodsView(), new EventsView(), type, new MemberOptions());

        // Only the degraded member is recorded; the healthy member is not.
        var degraded = Assert.Single(view.DegradedSignatureMembers!);
        Assert.Contains("Run", degraded);
        Assert.DoesNotContain("Ok", degraded);

        var warning = await CaptureErrorAsync(() => ApiOutputFormatter.WriteSignatureDecodeWarning(view));
        Assert.Contains("could not be fully decoded", warning);
        Assert.Contains("Run", warning);
    }

    [Fact]
    public async Task WriteSignatureDecodeWarning_EmitsNothingWhenNoMemberDegraded()
    {
        Assert.Empty(await CaptureErrorAsync(() => ApiOutputFormatter.WriteSignatureDecodeWarning(new TypeView())));
    }

    [Fact]
    public void MemberRow_HasNoDecodeColumnInDefaultMemberTables()
    {
        // The Decode degradation marker is reported via stderr, never as a table column.
        Assert.DoesNotContain(
            typeof(MemberRow).GetProperties(),
            p => p.Name == "Decode");
    }

    [Fact]
    public void BuildTypeRenderManifest_CapturesActualMemberTable()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Worker",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Kind = "method",
                    Name = "Run",
                    Signature = "void Run()"
                }
            ]
        };
        var options = new TypeOptions
        {
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.Methods
            }
        };

        var manifest = ApiCommand.BuildTypeRenderManifest(type, options);

        var columns = Assert.IsAssignableFrom<IReadOnlySet<string>>(
            manifest.GetTableColumns(SectionNames.Methods));
        Assert.Contains("Name", columns);
        Assert.Contains("Signature", columns);
    }

    [Fact]
    public async Task DiscoverOutput_Tsv_RendersHeaderedTsvRows()
    {
        var schema = new DocumentSchema()
            .Add("Results", "column", "Pattern", "Type", "Sim");

        var (exit, output, _) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(DiscoverOutput.Execute(["Results"], schema, tsv: true)));

        Assert.Equal(0, exit);
        Assert.Equal(
            "name\tkind\nPattern\tcolumn\nType\tcolumn\nSim\tcolumn\n",
            output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task DiscoverOutput_Jsonl_RendersJsonLineRows()
    {
        var schema = new DocumentSchema()
            .Add("Results", "column", "Pattern", "Type");

        var (exit, output, _) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(DiscoverOutput.Execute(["Results"], schema, jsonl: true)));

        Assert.Equal(0, exit);
        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);

        using var document = JsonDocument.Parse(lines[0]);
        Assert.Equal("Pattern", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("column", document.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task DiscoverOutput_Json_RendersJsonRows()
    {
        var schema = new DocumentSchema()
            .Add("Results", "column", "Pattern", "Type");

        var (exit, output, _) = await ConsoleCapture.RunAsync(() =>
            Task.FromResult(DiscoverOutput.Execute(["Results"], schema, json: true)));

        Assert.Equal(0, exit);
        Assert.Contains("\"name\":\"Pattern\"", output);
        Assert.Contains("\"kind\":\"column\"", output);
    }

    [Fact]
    public void CountProjection_CapturesTableRowsBySection()
    {
        var projection = CountProjectionFormatter.Capture(writer =>
        {
            writer.WriteHeading(1, "Title");
            writer.WriteHeading(2, "Methods");
            writer.WriteTable(
                ["Name"],
                ["name"],
                [new[] { "Read" }, new[] { "Write" }]);
            writer.WriteHeading(2, "Fields");
            writer.WriteTable(
                ["Name"],
                ["name"],
                [new[] { "Value" }]);
        }, new MarkoutWriterOptions());

        Assert.Equal(3, projection.Total);
        Assert.Equal(2, projection.SectionCounts["Methods"]);
        Assert.Equal(1, projection.SectionCounts["Fields"]);
    }

    [Fact]
    public void CountProjection_AppliesRowWindowBeforeReduction()
    {
        var projection = CountProjectionFormatter.Capture(writer =>
        {
            writer.WriteHeading(2, "Methods");
            writer.WriteTable(
                ["Name"],
                ["name"],
                [
                    new[] { "One" },
                    new[] { "Two" },
                    new[] { "Three" }
                ]);
        }, OutputFormatter.CreateWindowedOptions(RowWindow.Head(2)));

        Assert.Equal(2, projection.Total);
        Assert.Equal(2, projection.SectionCounts["Methods"]);
    }

    [Fact]
    public void CountProjection_DoesNotCountNonTableContent()
    {
        var projection = CountProjectionFormatter.Capture(writer =>
        {
            writer.WriteHeading(2, "Notes");
            writer.WriteParagraph("Not a row.");
            writer.WriteCodeStart("md");
            writer.WriteCodeEnd();
        }, new MarkoutWriterOptions());

        Assert.Equal(0, projection.Total);
        Assert.True(projection.WroteAnyContent);
    }

    [Theory]
    [InlineData(OutputFormat.Markdown)]
    [InlineData(OutputFormat.Json)]
    [InlineData(OutputFormat.Tsv)]
    [InlineData(OutputFormat.Jsonl)]
    [InlineData(OutputFormat.Table)]
    [InlineData(OutputFormat.PlainText)]
    public void CountProjection_SectionRowsRenderThroughEveryCompatibleFormat(
        OutputFormat format)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Methods"] = 17,
            ["Fields"] = 23
        };

        var output = CountOutput.RenderSectionCounts(
            counts, ["Methods", "Fields"], format);

        Assert.Contains("Methods", output, StringComparison.Ordinal);
        Assert.Contains("17", output, StringComparison.Ordinal);
        Assert.Contains("Fields", output, StringComparison.Ordinal);
        Assert.Contains("23", output, StringComparison.Ordinal);

        if (format == OutputFormat.Json)
        {
            using var document = JsonDocument.Parse(output);
            Assert.Equal(17, document.RootElement[0].GetProperty("count").GetInt32());
            Assert.Equal(23, document.RootElement[1].GetProperty("count").GetInt32());
        }
        else if (format == OutputFormat.Jsonl)
        {
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            using var first = JsonDocument.Parse(lines[0]);
            using var second = JsonDocument.Parse(lines[1]);
            Assert.Equal(17, first.RootElement.GetProperty("count").GetInt32());
            Assert.Equal(23, second.RootElement.GetProperty("count").GetInt32());
        }
    }

    [Fact]
    public void AssertMarkdownTablesHaveUniformColumnCounts_CatchesMalformedRows()
    {
        const string markdown = """
        | Name | Value |
        | ---- | ----- |
        | A | 1 |
        | B |
        """;

        Assert.Throws<InvalidOperationException>(() => AssertMarkdownTablesHaveUniformColumnCounts(markdown));
    }

    [Fact]
    public void RepresentativeMarkdownTables_HaveUniformColumnCounts()
    {
        var packageResult = new InspectionResult
        {
            PackageName = "Test.Package",
            Version = "1.0.0",
            LibraryFiles = ["lib/net10.0/Test.Package.dll"],
            SignatureResult = new SignatureVerificationResult
            {
                AuthorVerified = true,
                Publisher = "Example Publisher",
                Repository = "nuget.org",
                RepositoryVerified = true,
                StatusMessage = "Valid"
            },
            AuditSignals =
            [
                new AuditSignal("Package", "README", "Yes", "nuspec")
            ]
        };
        var packageOptions = new InspectionOptions
        {
            IncludeSections =
            [
                PackageSections.PackageInfo,
                PackageSections.Signature,
                PackageSections.Signals
            ]
        };
        var packageOutput = OutputFormatter.FormatResult(
            packageResult, packageOptions, PackageSectionDescriptors.CreatePipeline());

        var libraryInspection = CreateTestAudit("Test.dll", "net9.0");
        libraryInspection.OpenTelemetryInspection = MetadataFindings.InspectOpenTelemetrySignals(
            [
                new OpenTelemetrySignalInfo("Tracing", "System.Diagnostics.ActivitySource"),
                new OpenTelemetrySignalInfo("Metrics", "System.Diagnostics.Metrics.UpDownCounter<T>"),
            ],
            FindingTestData.Subject);
        libraryInspection.SourceIntegrityChecked = true;
        libraryInspection.SourceIntegrityMismatched = 1;
        libraryInspection.SourceIntegrityMismatches = ["/_/src/A.cs"];
        var libraryOptions = new LibraryOptions
        {
            Verbosity = Verbosity.Normal,
            IncludeSections =
            [
                "Library Info",
                "Integration: OpenTelemetry",
                "SourceLink: Integrity"
            ]
        };
        var libraryOutput = SerializeWithInclude(
            libraryInspection,
            LibrarySections.CreatePipeline().ComputeIncludeSections(
                libraryInspection, libraryOptions.Verbosity, libraryOptions.IncludeSections));

        AssertMarkdownTablesHaveUniformColumnCounts(packageOutput);
        AssertMarkdownTablesHaveUniformColumnCounts(libraryOutput);
    }

    [Fact]
    public void MarkdownSectionOrderer_ReordersH2SectionsAndKeepsFenceHeadings()
    {
        const string markdown = """
        # Title

        intro

        ## Zebra

        ```md
        ## Not a section
        ```

        ## Alpha

        A

        ## Beta

        B
        """;

        var output = MarkdownSectionOrderer.Apply(markdown, ["Beta", "Alpha", "Zebra"]);

        Assert.True(output.IndexOf("## Beta", StringComparison.Ordinal) < output.IndexOf("## Alpha", StringComparison.Ordinal));
        Assert.True(output.IndexOf("## Alpha", StringComparison.Ordinal) < output.IndexOf("## Zebra", StringComparison.Ordinal));
        Assert.Contains("## Not a section", output);
        // This claim is about which lines end up adjacent, so it is asserted against
        // normalized text. Line endings are the separate claim below; a raw string
        // literal carries whatever ending this source file is checked out with, so
        // spelling "\n" here would silently assert the platform rather than the shape.
        Assert.Contains("B\n\n## Alpha", output.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// This is the gate for line-ending preservation in <c>MarkdownSectionOrderer</c>.
    /// Reordering selects the order sections appear in; it is not licensed to rewrite CRLF
    /// to LF, for the same reason row limiting is not — the same document would otherwise
    /// differ byte for byte depending on whether a section order was supplied. The orderer
    /// used to rejoin on a hardcoded '\n', which on Windows silently converted the whole
    /// document and broke every caller that split it on <see cref="Environment.NewLine"/>.
    /// </summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void MarkdownSectionOrderer_PreservesDocumentLineEndings(string newline)
    {
        string markdown = string.Join(newline, ["# Title", "", "intro", "", "## Zebra", "", "Z", "", "## Alpha", "", "A"]);

        var output = MarkdownSectionOrderer.Apply(markdown, ["Alpha", "Zebra"]);

        Assert.True(output.IndexOf("## Alpha", StringComparison.Ordinal) < output.IndexOf("## Zebra", StringComparison.Ordinal));
        Assert.Equal(newline, MarkdownScan.DetectNewline(output));
        Assert.Equal(
            output.Split('\n').Length - 1,
            output.Split(newline).Length - 1);
    }

    [Fact]
    public void LimitRenderedTableRows_TsvKeepsHeaderAndLimitsDataRows()
    {
        var tsv = "name\tcount\nA\t1\nB\t2\nC\t3\n";

        var output = OutputFormatter.LimitRenderedTableRows(tsv, RowWindow.Head(2), hasHeader: true).ReplaceLineEndings("\n");

        Assert.Equal("name\tcount\nA\t1\nB\t2\n", output);
    }

    [Fact]
    public void LimitRenderedTableRows_PreservesTheLineEndingsItWasGiven()
    {
        // Row limiting selects which rows survive. It is not licensed to rewrite the
        // line endings, which would make the same output differ byte for byte between
        // a run with a row window and one without -- the unlimited fast path returns
        // the string untouched.
        var crlf = "name\tcount\r\nA\t1\r\nB\t2\r\nC\t3\r\n";

        var windowed = OutputFormatter.LimitRenderedTableRows(crlf, RowWindow.Head(2), hasHeader: true);
        Assert.Equal("name\tcount\r\nA\t1\r\nB\t2\r\n", windowed);

        // An open-ended range keeps every row, so it must be byte-identical to the input.
        var openEnded = OutputFormatter.LimitRenderedTableRows(crlf, RowWindow.Range(1, null), hasHeader: true);
        Assert.Equal(crlf, openEnded);

        var lf = "name\tcount\nA\t1\nB\t2\nC\t3\n";
        Assert.Equal("name\tcount\nA\t1\nB\t2\n", OutputFormatter.LimitRenderedTableRows(lf, RowWindow.Head(2), hasHeader: true));
    }

    [Fact]
    public void LimitRenderedTableRows_MarkdownPreservesTheLineEndingsItWasGiven()
    {
        var markdown = "| name | count |\r\n| --- | --- |\r\n| A | 1 |\r\n| B | 2 |\r\n| C | 3 |\r\n";

        var output = OutputFormatter.LimitRenderedTableRows(markdown, RowWindow.Head(2), hasHeader: true);

        Assert.Equal("| name | count |\r\n| --- | --- |\r\n| A | 1 |\r\n| B | 2 |\r\n", output);
    }

    [Fact]
    public void LimitRenderedTableRows_TsvWithoutHeaderLimitsFromFirstLine()
    {
        var tsv = "A\t1\nB\t2\nC\t3\n";

        var output = OutputFormatter.LimitRenderedTableRows(tsv, RowWindow.Head(2), hasHeader: false).ReplaceLineEndings("\n");

        Assert.Equal("A\t1\nB\t2\n", output);
    }

    [Fact]
    public void LimitRenderedTableRows_JsonlHasNoHeaderLineEvenWhenHeaderRequested()
    {
        var jsonl = "{\"name\":\"A\"}\n{\"name\":\"B\"}\n{\"name\":\"C\"}\n";

        // hasHeader is true (callers pass !--no-header) but jsonl rows are self-describing.
        var output = OutputFormatter.LimitRenderedTableRows(jsonl, RowWindow.Head(2), hasHeader: true).ReplaceLineEndings("\n");

        Assert.Equal("{\"name\":\"A\"}\n{\"name\":\"B\"}\n", output);
    }

    [Fact]
    public void LimitRenderedTableRows_MarkdownDelegatesToMarkdownLimiter()
    {
        var markdown = "| Name |\n| ---- |\n| A |\n| B |\n| C |\n";

        var output = OutputFormatter.LimitRenderedTableRows(markdown, RowWindow.Head(2), hasHeader: true).ReplaceLineEndings("\n");

        Assert.Contains("| A |", output);
        Assert.Contains("| B |", output);
        Assert.DoesNotContain("| C |", output);
    }

    [Fact]
    public void LimitRenderedTableRows_NullLimitIsUnchanged()
    {
        var tsv = "name\tcount\nA\t1\nB\t2\n";

        Assert.Equal(tsv, OutputFormatter.LimitRenderedTableRows(tsv, null, hasHeader: true));
    }

    [Fact]
    public void LimitMarkdownTableRows_LimitsEachTable()
    {
        const string markdown = """
        # Title

        ## First

        | Name |
        | ---- |
        | A |
        | B |
        | C |

        ## Second

        | Value |
        | ----- |
        | 1 |
        | 2 |
        """;

        var output = MarkdownTableRowLimiter.Apply(markdown, RowWindow.Head(2));

        Assert.Contains("| A |", output);
        Assert.Contains("| B |", output);
        Assert.DoesNotContain("| C |", output);
        Assert.Contains("| 1 |", output);
        Assert.Contains("| 2 |", output);
    }

    [Fact]
    public void LimitMarkdownTableRows_IgnoresCodeFences()
    {
        const string markdown = """
        ```md
        | Name |
        | ---- |
        | A |
        | B |
        ```

        | Name |
        | ---- |
        | A |
        | B |
        """;

        var output = MarkdownTableRowLimiter.Apply(markdown, RowWindow.Head(1));

        Assert.Contains("| B |\n```", output.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("| B |\n", output.ReplaceLineEndings("\n").Split("```")[2]);
    }

    [Fact]
    public void LimitMarkdownTableRows_TailKeepsLastRowsAndHeader()
    {
        var markdown = "| Name |\n| ---- |\n| A |\n| B |\n| C |\n";

        var output = MarkdownTableRowLimiter.Apply(markdown, RowWindow.Tail(2)).ReplaceLineEndings("\n");

        Assert.Contains("| Name |", output);
        Assert.Contains("| ---- |", output);
        Assert.DoesNotContain("| A |", output);
        Assert.Contains("| B |", output);
        Assert.Contains("| C |", output);
    }

    [Fact]
    public void LimitMarkdownTableRows_TailWiderThanTableKeepsAllRows()
    {
        var markdown = "| Name |\n| ---- |\n| A |\n| B |\n";

        var output = MarkdownTableRowLimiter.Apply(markdown, RowWindow.Tail(10)).ReplaceLineEndings("\n");

        Assert.Contains("| A |", output);
        Assert.Contains("| B |", output);
    }

    [Fact]
    public void LimitRenderedTableRows_TsvTailKeepsHeaderAndLastRows()
    {
        var tsv = "name\tcount\nA\t1\nB\t2\nC\t3\n";

        var output = OutputFormatter.LimitRenderedTableRows(tsv, RowWindow.Tail(2), hasHeader: true).ReplaceLineEndings("\n");

        Assert.Equal("name\tcount\nB\t2\nC\t3\n", output);
    }

    [Fact]
    public void LimitRenderedTableRows_JsonlTailKeepsLastRows()
    {
        var jsonl = "{\"name\":\"A\"}\n{\"name\":\"B\"}\n{\"name\":\"C\"}\n";

        var output = OutputFormatter.LimitRenderedTableRows(jsonl, RowWindow.Tail(2), hasHeader: true).ReplaceLineEndings("\n");

        Assert.Equal("{\"name\":\"B\"}\n{\"name\":\"C\"}\n", output);
    }

    [Fact]
    public void LimitRenderedTableRows_JsonlZeroWindowEmitsNothing()
    {
        var jsonl = "{\"name\":\"A\"}\n{\"name\":\"B\"}\n{\"name\":\"C\"}\n";

        var tail = OutputFormatter.LimitRenderedTableRows(jsonl, RowWindow.Tail(0), hasHeader: true);
        var head = OutputFormatter.LimitRenderedTableRows(jsonl, RowWindow.Head(0), hasHeader: true);

        Assert.Equal(string.Empty, tail);
        Assert.Equal(string.Empty, head);
    }

    [Fact]
    public void LimitRenderedTableRows_TsvNoHeaderZeroWindowEmitsNothing()
    {
        var tsv = "A\t1\nB\t2\nC\t3\n";

        var output = OutputFormatter.LimitRenderedTableRows(tsv, RowWindow.Tail(0), hasHeader: false);

        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void LimitRenderedTableRows_TsvHeaderZeroWindowKeepsHeader()
    {
        var tsv = "name\tcount\nA\t1\nB\t2\n";

        var output = OutputFormatter.LimitRenderedTableRows(tsv, RowWindow.Tail(0), hasHeader: true).ReplaceLineEndings("\n");

        Assert.Equal("name\tcount\n", output);
    }

    [Theory]
    [InlineData("FIRST", RowSelectorKind.First)]
    [InlineData("last", RowSelectorKind.Last)]
    [InlineData("Last", RowSelectorKind.Last)]
    [InlineData("3", RowSelectorKind.Index)]
    public void RowSelector_ParsesKeywordsAndIndex(string token, RowSelectorKind expected)
    {
        Assert.True(RowSelector.TryParse(token, out var selector));
        Assert.Equal(expected, selector.Kind);
    }

    [Theory]
    [InlineData("firstish")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("abc")]
    [InlineData(null)]
    public void RowSelector_RejectsInvalidTokens(string? token)
    {
        Assert.False(RowSelector.TryParse(token, out _));
    }

    [Fact]
    public void RowSelector_ResolvesFirstLastAndIndex()
    {
        int[] contiguous = [1, 2, 3, 4, 5, 6, 7];
        Assert.Equal(1, RowSelector.First.Resolve(contiguous));
        Assert.Equal(7, RowSelector.Last.Resolve(contiguous));
        Assert.Equal(3, RowSelector.FromIndex(3).Resolve(contiguous));
    }

    [Fact]
    public void RowSelector_ResolvesEndpointsOfGappedRows()
    {
        // first/last name the endpoints of the rendered sequence, not 1 and the count.
        int[] gapped = [2, 5, 9];
        Assert.Equal(2, RowSelector.First.Resolve(gapped));
        Assert.Equal(9, RowSelector.Last.Resolve(gapped));
        Assert.Equal(5, RowSelector.FromIndex(5).Resolve(gapped));
    }

    [Fact]
    public void RowNumbering_DescribesContiguousAndGappedRows()
    {
        Assert.Equal("1 through 4", RowNumbering.Describe([1, 2, 3, 4]));
        Assert.Equal("2, 5, 9", RowNumbering.Describe([2, 5, 9]));
        Assert.Equal("7", RowNumbering.Describe([7]));
        Assert.Equal("none", RowNumbering.Describe([]));
    }

    [Fact]
    public void RowNumbering_IndexOfFindsRowByDisplayedNumber()
    {
        int[] gapped = [2, 5, 9];
        Assert.Equal(1, RowNumbering.IndexOf(gapped, 5));
        Assert.Equal(-1, RowNumbering.IndexOf(gapped, 3));
    }

    [Fact]
    public void BuildRowWindow_CountWithoutDirectionIsLeadingWindow()
    {
        var window = SharedOptions.BuildRowWindow("3", fromEnd: false);
        Assert.Equal(RowWindow.Head(3), window);
    }

    [Fact]
    public void BuildRowWindow_CountWithTailIsTrailingWindow()
    {
        var window = SharedOptions.BuildRowWindow("3", fromEnd: true);
        Assert.Equal(RowWindow.Tail(3), window);
    }

    [Fact]
    public void BuildRowWindow_WithoutRowsIsNull()
    {
        Assert.Null(SharedOptions.BuildRowWindow(null, fromEnd: false));
        Assert.Null(SharedOptions.BuildRowWindow(null, fromEnd: true));
    }

    [Fact]
    public void BuildRowWindow_RangeIsAbsolute_NotACountFromAnEnd()
    {
        // The distinction the grammar exists for: 2..10 names rows, 9 counts them.
        // A window built from the range must not collapse into a count, or a table
        // shorter than 10 rows would silently return a different set of rows.
        Assert.Equal(RowWindow.Range(2, 10), SharedOptions.BuildRowWindow("2..10", fromEnd: false));
        Assert.NotEqual(RowWindow.Head(9), SharedOptions.BuildRowWindow("2..10", fromEnd: false));
    }

    [Fact]
    public void BuildRowWindow_OpenRangeHasNoEnd()
        => Assert.Equal(RowWindow.Range(10, null), SharedOptions.BuildRowWindow("10..", fromEnd: false));

    [Fact]
    public void BuildRowWindow_StartPlusCountResolvesToItsInclusiveEnd()
        => Assert.Equal(RowWindow.Range(2, 11), SharedOptions.BuildRowWindow("2+10", fromEnd: false));

    [Fact]
    public void BuildRowWindow_RejectsADirectionOnARange()
    {
        var ex = Assert.Throws<RowWindowValidationException>(
            () => SharedOptions.BuildRowWindow("2..10", fromEnd: true));
        Assert.Contains("already names which rows to keep", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRowWindow_RejectsAMalformedSpec()
    {
        var ex = Assert.Throws<RowWindowValidationException>(
            () => SharedOptions.BuildRowWindow("2:10", fromEnd: false));
        Assert.Contains("':'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LimitMarkdownTableRows_KeepsInteriorSeparatorInPlace()
    {
        // A pathological table with an interior separator: the separator must stay in
        // its original position rather than being hoisted after the windowed rows.
        var markdown = "| Name |\n| ---- |\n| A |\n| ---- |\n| B |\n| C |\n";

        var output = MarkdownTableRowLimiter.Apply(markdown, RowWindow.Head(2)).ReplaceLineEndings("\n");

        // Head window of 2 data rows keeps A and B; the interior separator sits between them.
        Assert.Equal("| Name |\n| ---- |\n| A |\n| ---- |\n| B |\n", output);
    }

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
    /// The aggregate <c>--all-libraries</c> sections declare a <see cref="MarkoutTable"/> rather
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

internal static class OutputFormatterAsyncSiblingFixture
{
    public static int ReadValue(int value) => value;

    public static Task<int> ReadValueAsync(
        int value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(value);
    }

    public static async Task<int> CallsSyncSiblingFromAsync(
        int value)
    {
        await Task.Yield();
        return ReadValue(value);
    }
}
