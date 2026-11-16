using System.Diagnostics;
using System.Text;

using CSharpText;
using DotnetInspector.DocumentationHouse.Direct;
using DotnetInspector.Libraries;

namespace DotnetInspector.DocumentationHouse.Tests;

public sealed partial class CompiledXmlDocumentationHouseTests
{
    [Fact]
    public async Task LibrarySummariesEqualPerSubjectSettlementForRealSystemTextJson()
    {
        byte[] xml = await File.ReadAllBytesAsync(
            RealAsset("System.Text.Json.xml"),
            TestContext.Current.CancellationToken);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        IReadOnlyList<CompiledXmlContribution> contributions =
            DirectLibraryDocumentationHouseAdapter
                .CreateLibraryCompiledXmlContributions(library.Reference);
        CompiledXmlContribution contribution = Assert.Single(contributions);
        Assert.Null(contribution.Subject);
        Assert.Equal(CompiledXmlContributionKind.Candidate, contribution.Kind);

        using LibraryOperationLease lease = library.IssueOperation();
        var available = Assert.IsType<DocumentationLibrarySummaryOutcome.Available>(
            DocumentationHouse.ReadLibrarySummaries(
                LibraryRequest(contributions),
                lease,
                TestContext.Current.CancellationToken));
        Assert.Same(contribution, available.Selected);
        Assert.Equal(xml.Length, available.CompiledXmlBytes);
        Assert.True(available.Summaries.Count > 1_000);

        // The same entry's text as per-subject settlement reports it.
        DocumentationSubjectReference subject = Subject(library);
        DocumentationHouseOutcome.Completed completed =
            await ExecuteCompletedAsync(
                library,
                Request(
                    subject,
                    DirectLibraryDocumentationHouseAdapter
                        .CreateCompiledXmlContributions(library.Reference, subject)));
        var settled = Assert.IsType<DocumentationCompiledXmlAttempt.Available>(
            completed.CompiledXmlAttempt);
        Assert.Equal(settled.Documentation.Summary, available.Summaries[DeserializeIdentity]);

        // The lease is borrowed, not settled: the caller keeps reading.
        Assert.IsType<DocumentationLibrarySummaryOutcome.Available>(
            DocumentationHouse.ReadLibrarySummaries(
                LibraryRequest(contributions),
                lease,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LibrarySummariesKeepEntriesWithoutASummary()
    {
        byte[] xml = Encoding.UTF8.GetBytes(
            $"""
             <?xml version="1.0"?>
             <doc>
               <members>
                 <member name="{DeserializeIdentity}">
                   <summary>documented</summary>
                 </member>
                 <member name="T:System.Text.Json.JsonDocument">
                   <remarks>remarks only</remarks>
                 </member>
                 <member name="T:System.Text.Json.JsonElement" />
               </members>
             </doc>
             """);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);

        var available = Assert.IsType<DocumentationLibrarySummaryOutcome.Available>(
            Read(library, LibraryContributions(library)));

        Assert.Equal(
            [
                ("M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.JsonDocument,System.Text.Json.JsonSerializerOptions)", "documented"),
                ("T:System.Text.Json.JsonDocument", ""),
                ("T:System.Text.Json.JsonElement", ""),
            ],
            available.Summaries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => (entry.Key, entry.Value)));
    }

    [Fact]
    public async Task LibrarySummarySelectionClassifiesLikeSubjectSettlement()
    {
        await using LibraryFixture none = await LibraryFixture.CreateAsync();
        Assert.IsType<DocumentationLibrarySummaryOutcome.Unavailable>(
            Read(none, LibraryContributions(none)));
        Assert.IsType<DocumentationLibrarySummaryOutcome.Absent>(
            Read(
                none,
                [
                    CompiledXmlContribution.Absent(
                        subject: null,
                        none.Reference,
                        none.Reference.ApiAssembly,
                        Source("absent")),
                ]));
        Assert.Equal(
            DocumentationIncompleteBoundary.CompanionSelectionPartial,
            Assert.IsType<DocumentationLibrarySummaryOutcome.Incomplete>(
                Read(
                    none,
                    [
                        CompiledXmlContribution.Partial(
                            subject: null,
                            none.Reference,
                            none.Reference.ApiAssembly,
                            Source("partial")),
                    ])).Boundary);

        await using LibraryFixture two = await LibraryFixture.CreateAsync(
            Xml(DeserializeIdentity, "first"),
            Xml(DeserializeIdentity, "second"));
        Assert.Equal(
            2,
            Assert.IsType<DocumentationLibrarySummaryOutcome.Ambiguous>(
                Read(two, LibraryContributions(two))).Candidates.Count);
        Assert.Equal(
            DocumentationIncompleteBoundary.ContributionLimit,
            Assert.IsType<DocumentationLibrarySummaryOutcome.Incomplete>(
                Read(two, LibraryContributions(two), maximumContributions: 1)).Boundary);
    }

    [Fact]
    public async Task LibrarySummariesRejectEvidenceForAnotherLibraryOrASubject()
    {
        byte[] xml = Xml(DeserializeIdentity, "selected");
        await using LibraryFixture selected = await LibraryFixture.CreateAsync(xml);
        await using LibraryFixture foreign = await LibraryFixture.CreateAsync(xml);

        var foreignRejected = Assert.IsType<DocumentationLibrarySummaryOutcome.Rejected>(
            Read(selected, LibraryContributions(foreign)));
        Assert.Equal(
            DocumentationCompiledXmlRejectionKind.LibraryMismatch,
            Assert.Single(foreignRejected.Rejections).Kind);

        // Per-subject evidence cannot stand in for Library evidence, and
        // Library evidence cannot satisfy a per-subject request.
        DocumentationSubjectReference subject = Subject(selected);
        var subjectRejected = Assert.IsType<DocumentationLibrarySummaryOutcome.Rejected>(
            Read(
                selected,
                DirectLibraryDocumentationHouseAdapter
                    .CreateCompiledXmlContributions(selected.Reference, subject)));
        Assert.Equal(
            DocumentationCompiledXmlRejectionKind.SubjectMismatch,
            Assert.Single(subjectRejected.Rejections).Kind);

        DocumentationHouseOutcome.Completed completed =
            await ExecuteCompletedAsync(
                selected,
                Request(subject, LibraryContributions(selected)));
        Assert.Equal(
            DocumentationCompiledXmlRejectionKind.SubjectMismatch,
            Assert.Single(
                Assert.IsType<DocumentationCompiledXmlAttempt.Rejected>(
                    completed.CompiledXmlAttempt).Rejections).Kind);
    }

    [Fact]
    public async Task LibrarySummariesFailVisiblyOnMalformedOrBoundedXml()
    {
        await using LibraryFixture malformed = await LibraryFixture.CreateAsync(
            Encoding.UTF8.GetBytes("<doc><members><member name=\"T:X\"><summary>open"));
        Assert.Equal(
            DocumentationCompiledXmlFailureKind.MalformedOrUnreadableDocument,
            Assert.IsType<DocumentationLibrarySummaryOutcome.Failed>(
                Read(malformed, LibraryContributions(malformed))).Kind);

        byte[] xml = Xml(DeserializeIdentity, "bounded");
        await using LibraryFixture library = await LibraryFixture.CreateAsync(xml);
        Assert.Equal(
            DocumentationIncompleteBoundary.CompiledXmlByteLimit,
            Assert.IsType<DocumentationLibrarySummaryOutcome.Incomplete>(
                Read(library, LibraryContributions(library), maximumXmlBytes: xml.Length - 1)).Boundary);
        Assert.Equal(
            DocumentationCompiledXmlFailureKind.MalformedOrUnreadableDocument,
            Assert.IsType<DocumentationLibrarySummaryOutcome.Failed>(
                Read(
                    library,
                    LibraryContributions(library),
                    xmlReadLimits: new XmlDocumentationReadLimits { MaxRetainedTextCharacters = 3 })).Kind);
        Assert.Equal(
            DocumentationIncompleteBoundary.Deadline,
            Assert.IsType<DocumentationLibrarySummaryOutcome.Incomplete>(
                Read(library, LibraryContributions(library), deadline: DateTimeOffset.UtcNow.AddSeconds(-1))).Boundary);
    }

    [Fact]
    public async Task LibrarySummaryParseOutlivingTheDeadlineIsIncompleteEvenWhenMalformed()
    {
        // A large companion that fails only at its end: the parse runs past
        // a deadline set inside the parse window, and its failure must not
        // outrank the deadline.
        byte[] valid = LargeXml(48 * 1024 * 1024);
        byte[] xml = valid[..^"</members></doc>".Length];
        await using LibraryFixture library = await LibraryFixture.CreateAsync(xml);
        IReadOnlyList<CompiledXmlContribution> contributions = LibraryContributions(library);
        var xmlReadLimits = XmlDocumentationReadLimits.Default with
        {
            MaxCharactersInDocument = xml.Length + 1024L,
            MaxRetainedTextCharacters = xml.Length + 1024L,
        };

        using LibraryOperationLease calibration = library.IssueOperation();
        Stopwatch snapshotTime = Stopwatch.StartNew();
        calibration.Snapshot(
            library.XmlContents[0],
            static (view, _) => view.Content.ToArray(),
            TestContext.Current.CancellationToken);
        snapshotTime.Stop();
        Stopwatch operationTime = Stopwatch.StartNew();
        Assert.Equal(
            DocumentationCompiledXmlFailureKind.MalformedOrUnreadableDocument,
            Assert.IsType<DocumentationLibrarySummaryOutcome.Failed>(
                Read(
                    library,
                    contributions,
                    maximumXmlBytes: xml.Length + 1,
                    xmlReadLimits: xmlReadLimits)).Kind);
        operationTime.Stop();
        TimeSpan parseWindow = operationTime.Elapsed - snapshotTime.Elapsed;

        bool crossedBoundary = false;
        for (double fraction = 0.9; fraction >= 0.1; fraction -= 0.1)
        {
            TimeSpan executionBudget =
                snapshotTime.Elapsed
                    + TimeSpan.FromTicks((long)(parseWindow.Ticks * fraction));
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(500);
            while (deadline - DateTimeOffset.UtcNow > executionBudget)
                Thread.SpinWait(10_000);

            Stopwatch executionTime = Stopwatch.StartNew();
            DocumentationLibrarySummaryOutcome outcome = Read(
                library,
                contributions,
                maximumXmlBytes: xml.Length + 1,
                xmlReadLimits: xmlReadLimits,
                deadline: deadline);
            executionTime.Stop();
            // Only a read that ran past its deadline is evidence; one that
            // finished first is correctly Failed.
            if (executionTime.Elapsed < executionBudget + TimeSpan.FromMilliseconds(2))
                continue;

            crossedBoundary = true;
            Assert.Equal(
                DocumentationIncompleteBoundary.Deadline,
                Assert.IsType<DocumentationLibrarySummaryOutcome.Incomplete>(outcome).Boundary);
            break;
        }

        Assert.True(
            crossedBoundary,
            $"No parse crossed its calibrated deadline; the parse window was {parseWindow.TotalMilliseconds:F1} ms.");
    }

    [Theory]
    [InlineData(CompiledXmlContributionKind.Unavailable)]
    [InlineData(CompiledXmlContributionKind.Partial)]
    public async Task LibrarySummarySelectionOutlivingTheDeadlineIsIncomplete(
        CompiledXmlContributionKind kind)
    {
        // Classifying four million contributions takes long enough for a
        // calibrated deadline to pass during selection; the classification
        // must not outrank it.
        const int contributionCount = 4_000_000;
        await using LibraryFixture library = await LibraryFixture.CreateAsync();
        // The classifying contribution comes last, so finding it means
        // scanning every other one.
        CompiledXmlContribution unavailable = CompiledXmlContribution.Unavailable(
            subject: null,
            library.Reference,
            library.Reference.ApiAssembly,
            Source("unavailable"));
        CompiledXmlContribution last = kind == CompiledXmlContributionKind.Partial
            ? CompiledXmlContribution.Partial(
                subject: null,
                library.Reference,
                library.Reference.ApiAssembly,
                Source("partial"))
            : unavailable;
        CompiledXmlContribution[] contributions =
        [
            .. Enumerable.Repeat(unavailable, contributionCount - 1),
            last,
        ];

        // Only the House call is timed: the request copies the
        // contributions, which is not selection work.
        TimeSpan baselineTime = TimeSpan.MaxValue;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            DocumentationLibrarySummaryRequest request =
                LibraryRequest(contributions, maximumContributions: contributionCount);
            using LibraryOperationLease lease = library.IssueOperation();
            Stopwatch measurement = Stopwatch.StartNew();
            DocumentationLibrarySummaryOutcome baseline =
                DocumentationHouse.ReadLibrarySummaries(
                    request,
                    lease,
                    TestContext.Current.CancellationToken);
            measurement.Stop();
            if (kind == CompiledXmlContributionKind.Partial)
            {
                Assert.Equal(
                    DocumentationIncompleteBoundary.CompanionSelectionPartial,
                    Assert.IsType<DocumentationLibrarySummaryOutcome.Incomplete>(baseline).Boundary);
            }
            else
            {
                Assert.IsType<DocumentationLibrarySummaryOutcome.Unavailable>(baseline);
            }
            baselineTime = TimeSpan.FromTicks(Math.Min(baselineTime.Ticks, measurement.Elapsed.Ticks));
        }

        bool crossedDeadline = false;
        for (double fraction = 0.95; fraction >= 0.5; fraction -= 0.05)
        {
            TimeSpan executionBudget = TimeSpan.FromTicks((long)(baselineTime.Ticks * fraction));
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(250);
            DocumentationLibrarySummaryRequest request = LibraryRequest(
                contributions,
                maximumContributions: contributionCount,
                deadline: deadline);
            using LibraryOperationLease lease = library.IssueOperation();
            while (deadline - DateTimeOffset.UtcNow > executionBudget)
                Thread.SpinWait(10_000);

            Stopwatch executionTime = Stopwatch.StartNew();
            DocumentationLibrarySummaryOutcome outcome =
                DocumentationHouse.ReadLibrarySummaries(
                    request,
                    lease,
                    TestContext.Current.CancellationToken);
            executionTime.Stop();
            if (executionTime.Elapsed < executionBudget + TimeSpan.FromMilliseconds(2))
                continue;

            crossedDeadline = true;
            Assert.Equal(
                DocumentationIncompleteBoundary.Deadline,
                Assert.IsType<DocumentationLibrarySummaryOutcome.Incomplete>(outcome).Boundary);
            break;
        }

        Assert.True(
            crossedDeadline,
            $"No selection crossed its calibrated deadline; baseline was {baselineTime.TotalMilliseconds:F1} ms.");
    }

    private static IReadOnlyList<CompiledXmlContribution> LibraryContributions(
        LibraryFixture library) =>
        DirectLibraryDocumentationHouseAdapter
            .CreateLibraryCompiledXmlContributions(library.Reference);

    private static DocumentationLibrarySummaryOutcome Read(
        LibraryFixture library,
        IReadOnlyList<CompiledXmlContribution> contributions,
        int maximumContributions = 8,
        int maximumXmlBytes = 8 * 1024 * 1024,
        XmlDocumentationReadLimits? xmlReadLimits = null,
        DateTimeOffset? deadline = null)
    {
        using LibraryOperationLease lease = library.IssueOperation();
        return DocumentationHouse.ReadLibrarySummaries(
            LibraryRequest(
                contributions,
                maximumContributions,
                maximumXmlBytes,
                xmlReadLimits,
                deadline),
            lease,
            TestContext.Current.CancellationToken);
    }

    private static DocumentationLibrarySummaryRequest LibraryRequest(
        IReadOnlyList<CompiledXmlContribution> contributions,
        int maximumContributions = 8,
        int maximumXmlBytes = 8 * 1024 * 1024,
        XmlDocumentationReadLimits? xmlReadLimits = null,
        DateTimeOffset? deadline = null) =>
        new(
            contributions,
            new DocumentationHouseLimits(
                maximumContributions,
                maximumXmlBytes,
                xmlReadLimits ?? XmlDocumentationReadLimits.Default),
            deadline ?? DateTimeOffset.UtcNow.AddMinutes(1));
}
