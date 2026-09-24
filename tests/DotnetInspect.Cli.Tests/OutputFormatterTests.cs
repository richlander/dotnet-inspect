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

// Captures Console.Error, which is process-wide state.
[Collection("Console")]
public partial class OutputFormatterTests
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
    public async Task ExactByteDestination_PullsProgressivelyToAFile()
    {
        byte[] expected = new byte[1024 * 1024];
        for (int index = 0; index < expected.Length; index++)
            expected[index] = (byte)(index % 241);
        var tempDirectory =
            Directory.CreateTempSubdirectory("exact-byte-destination-");
        try
        {
            string path = Path.Combine(
                tempDirectory.FullName,
                "payload.bin");
            await using var input = new ObservedReadStream(expected);

            await ProjectionDestinationWriter.WriteExactBytesAsync(
                new ProjectionDestination(path, ExactTransfer: true),
                input,
                TestContext.Current.CancellationToken);

            Assert.Equal(expected, await File.ReadAllBytesAsync(
                path,
                TestContext.Current.CancellationToken));
            Assert.InRange(
                input.LargestReadRequest,
                1,
                64 * 1024);
            Assert.True(input.ReadCalls > 1);
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ExactByteDestination_PreservesExistingFileWhenPullFailsAtEof()
    {
        byte[] sentinel = "existing"u8.ToArray();
        var tempDirectory =
            Directory.CreateTempSubdirectory("exact-byte-destination-failure-");
        try
        {
            string path = Path.Combine(
                tempDirectory.FullName,
                "payload.bin");
            await File.WriteAllBytesAsync(
                path,
                sentinel,
                TestContext.Current.CancellationToken);
            await using var input = new ThrowAtEofStream("replacement"u8.ToArray());

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ProjectionDestinationWriter.WriteExactBytesAsync(
                    new ProjectionDestination(path, ExactTransfer: true),
                    input,
                    TestContext.Current.CancellationToken));

            Assert.Equal(
                sentinel,
                await File.ReadAllBytesAsync(
                    path,
                    TestContext.Current.CancellationToken));
            Assert.Single(
                Directory.EnumerateFiles(tempDirectory.FullName));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    private sealed class ThrowAtEofStream(byte[] content) :
        MemoryStream(content, writable: false)
    {
        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            if (Position == Length)
            {
                throw new InvalidDataException(
                    "Payload validation failed at EOF.");
            }

            return base.Read(buffer, offset, count);
        }
    }

    private sealed class ObservedReadStream(byte[] content) : Stream
    {
        private int _position;

        public int LargestReadRequest { get; private set; }

        public int ReadCalls { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => content.LongLength;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            LargestReadRequest =
                Math.Max(LargestReadRequest, buffer.Length);
            ReadCalls++;
            int count = Math.Min(
                buffer.Length,
                content.Length - _position);
            content.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override void Flush()
        {
        }

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();
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
}
