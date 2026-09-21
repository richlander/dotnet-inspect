using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

using CSharpText;
using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.DocumentationHouse.Tests;

public sealed partial class CompiledXmlDocumentationHouseTests
{
    private const string DeserializeIdentity =
        "M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.JsonDocument,System.Text.Json.JsonSerializerOptions)";
    private static readonly ApiSurfaceExtractionBounds s_apiSurfaceBounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);
    private static readonly Lazy<byte[]> s_realAssembly =
        new(() => File.ReadAllBytes(RealAsset("System.Text.Json.dll")));

    [Fact]
    public async Task
        RealSystemTextJsonMember_SettlesAvailableDetachedDocumentation()
    {
        byte[] xml = await File.ReadAllBytesAsync(
            RealAsset("System.Text.Json.xml"),
            TestContext.Current.CancellationToken);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject =
            Subject(library);
        CompiledXmlContribution contribution =
            Candidate(library, subject, xmlIndex: 0);
        DocumentationHouseRequest request =
            Request(subject, [contribution]);
        LibraryOperationLease operation = library.IssueOperation();

        DocumentationHouseOutcome.Completed completed =
            Assert.IsType<DocumentationHouseOutcome.Completed>(
                await DocumentationHouse.ExecuteAsync(
                    request,
                    operation,
                    TestContext.Current.CancellationToken));

        DocumentationCompiledXmlAttempt.Available available =
            Assert.IsType<
                DocumentationCompiledXmlAttempt.Available>(
                    completed.CompiledXmlAttempt);
        Assert.Contains(
            "Converts the JsonDocument",
            available.Documentation.Summary,
            StringComparison.Ordinal);
        Assert.Same(contribution, available.Selected);
        Assert.Same(request, completed.Receipt.Request);
        Assert.Same(
            available,
            completed.Receipt.CompiledXmlAttempt);
        Assert.Equal(xml.Length, completed.Work.CompiledXmlBytesObserved);
        Assert.True(completed.Work.ParsedCompiledXml);
        Assert.Equal(
            DocumentationLibraryLeaseConsumer.DocumentationHouse,
            completed.LeaseSettlement.Consumer);
        AssertOperationSettled(operation, library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task ForeignLibraryCompanion_IsRejectedBeforeReading()
    {
        byte[] xml = Xml(DeserializeIdentity, "selected");
        await using LibraryFixture selected =
            await LibraryFixture.CreateAsync(xml);
        await using LibraryFixture foreign =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject =
            Subject(selected);
        DocumentationSubjectReference foreignSubject =
            Subject(foreign);
        CompiledXmlContribution contribution =
            Candidate(foreign, foreignSubject, xmlIndex: 0);

        DocumentationHouseOutcome.Completed completed =
            await ExecuteCompletedAsync(
                selected,
                Request(subject, [contribution]));

        DocumentationCompiledXmlAttempt.Rejected rejected =
            Assert.IsType<
                DocumentationCompiledXmlAttempt.Rejected>(
                    completed.CompiledXmlAttempt);
        DocumentationCompiledXmlRejection rejection =
            Assert.Single(rejected.Rejections);
        Assert.Equal(
            DocumentationCompiledXmlRejectionKind.SubjectMismatch,
            rejection.Kind);
        Assert.False(completed.Work.ParsedCompiledXml);
        Assert.Equal(0, completed.Work.CompiledXmlBytesObserved);
    }

    [Fact]
    public async Task ForeignLibraryLease_RejectsAndSettlesTransferredLease()
    {
        byte[] xml = Xml(DeserializeIdentity, "selected");
        await using LibraryFixture selected =
            await LibraryFixture.CreateAsync(xml);
        await using LibraryFixture foreign =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject =
            Subject(selected);
        DocumentationHouseRequest request = Request(
            subject,
            [Candidate(selected, subject, xmlIndex: 0)]);
        LibraryOperationLease operation = foreign.IssueOperation();

        DocumentationHouseOutcome.Rejected rejected =
            Assert.IsType<DocumentationHouseOutcome.Rejected>(
                await DocumentationHouse.ExecuteAsync(
                    request,
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            DocumentationHouseRejectionKind.LeaseReferenceMismatch,
            rejected.Rejection.Kind);
        AssertOperationSettled(
            operation,
            foreign.Reference.ApiAssembly);
    }

    [Fact]
    public async Task CompleteReadableCompanionWithoutIdentity_IsAbsent()
    {
        byte[] xml = Xml("T:System.Text.Json.JsonSerializer", "type");
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject =
            Subject(library);

        DocumentationHouseOutcome.Completed completed =
            await ExecuteCompletedAsync(
                library,
                Request(
                    subject,
                    [Candidate(library, subject, xmlIndex: 0)]));

        DocumentationCompiledXmlAttempt.Absent absent =
            Assert.IsType<DocumentationCompiledXmlAttempt.Absent>(
                completed.CompiledXmlAttempt);
        Assert.NotNull(absent.Selected);
        Assert.True(completed.Work.ParsedCompiledXml);
    }

    [Fact]
    public async Task CompleteAbsenceAndNoAuthorization_SettleDistinctAttempts()
    {
        await using LibraryFixture absentLibrary =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference absentSubject =
            Subject(absentLibrary);
        CompiledXmlContribution absence =
            CompiledXmlContribution.Absent(
                absentSubject,
                absentLibrary.Reference,
                absentLibrary.Reference.ApiAssembly,
                Source("complete-absence"));

        DocumentationHouseOutcome.Completed absent =
            await ExecuteCompletedAsync(
                absentLibrary,
                Request(absentSubject, [absence]));
        Assert.IsType<DocumentationCompiledXmlAttempt.Absent>(
            absent.CompiledXmlAttempt);

        await using LibraryFixture unavailableLibrary =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference unavailableSubject =
            Subject(unavailableLibrary);
        DocumentationHouseOutcome.Completed unavailable =
            await ExecuteCompletedAsync(
                unavailableLibrary,
                Request(unavailableSubject, []));
        Assert.IsType<
            DocumentationCompiledXmlAttempt.Unavailable>(
                unavailable.CompiledXmlAttempt);
    }

    [Fact]
    public async Task MultipleCandidatesRequireExplicitUniquePrecedence()
    {
        byte[] first = Xml(DeserializeIdentity, "first");
        byte[] second = Xml(DeserializeIdentity, "second");
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(first, second);
        DocumentationSubjectReference subject =
            Subject(library);

        DocumentationHouseOutcome.Completed ambiguous =
            await ExecuteCompletedAsync(
                library,
                Request(
                    subject,
                    [
                        Candidate(library, subject, xmlIndex: 0),
                        Candidate(library, subject, xmlIndex: 1),
                    ]));
        Assert.IsType<DocumentationCompiledXmlAttempt.Ambiguous>(
            ambiguous.CompiledXmlAttempt);

        DocumentationHouseOutcome.Completed selected =
            await ExecuteCompletedAsync(
                library,
                Request(
                    subject,
                    [
                        Candidate(
                            library,
                            subject,
                            xmlIndex: 0,
                            precedence: 0),
                        Candidate(
                            library,
                            subject,
                            xmlIndex: 1,
                            precedence: 1),
                    ]));
        DocumentationCompiledXmlAttempt.Available available =
            Assert.IsType<
                DocumentationCompiledXmlAttempt.Available>(
                    selected.CompiledXmlAttempt);
        Assert.Equal("first", available.Documentation.Summary);
    }

    [Fact]
    public async Task SameExactCompanionFromMultipleSources_IsNotAmbiguous()
    {
        byte[] xml = Xml(DeserializeIdentity, "selected");
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject = Subject(library);
        LibraryContentReference content =
            Assert.Single(library.XmlContents);

        DocumentationHouseOutcome.Completed completed =
            await ExecuteCompletedAsync(
                library,
                Request(
                    subject,
                    [
                        CompiledXmlContribution.Candidate(
                            subject,
                            library.Reference,
                            library.Reference.ApiAssembly,
                            Source("package"),
                            content),
                        CompiledXmlContribution.Candidate(
                            subject,
                            library.Reference,
                            library.Reference.ApiAssembly,
                            Source("direct"),
                            content),
                    ]));

        Assert.IsType<DocumentationCompiledXmlAttempt.Available>(
            completed.CompiledXmlAttempt);
        Assert.Equal(2, completed.CompiledXmlAttempt.Contributions.Count);
    }

    [Fact]
    public async Task
        DuplicateWinningCompanionObservations_DoNotMaskUniquePrecedence()
    {
        byte[] preferred = Xml(DeserializeIdentity, "preferred");
        byte[] other = Xml(DeserializeIdentity, "other");
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(preferred, other);
        DocumentationSubjectReference subject = Subject(library);
        LibraryContentReference preferredContent =
            library.XmlContents[0];

        DocumentationHouseOutcome.Completed completed =
            await ExecuteCompletedAsync(
                library,
                Request(
                    subject,
                    [
                        CompiledXmlContribution.Candidate(
                            subject,
                            library.Reference,
                            library.Reference.ApiAssembly,
                            Source("package"),
                            preferredContent,
                            precedence: 0),
                        CompiledXmlContribution.Candidate(
                            subject,
                            library.Reference,
                            library.Reference.ApiAssembly,
                            Source("direct"),
                            preferredContent,
                            precedence: 0),
                        Candidate(
                            library,
                            subject,
                            xmlIndex: 1,
                            precedence: 1),
                    ]));

        DocumentationCompiledXmlAttempt.Available available =
            Assert.IsType<
                DocumentationCompiledXmlAttempt.Available>(
                    completed.CompiledXmlAttempt);
        Assert.Equal("preferred", available.Documentation.Summary);
        Assert.Equal(3, available.Contributions.Count);
    }

    [Fact]
    public async Task PartialSelection_IsIncompleteWithoutReadingCandidate()
    {
        byte[] xml = Xml(DeserializeIdentity, "selected");
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject =
            Subject(library);

        DocumentationHouseOutcome.Completed completed =
            await ExecuteCompletedAsync(
                library,
                Request(
                    subject,
                    [
                        Candidate(library, subject, xmlIndex: 0),
                        CompiledXmlContribution.Partial(
                            subject,
                            library.Reference,
                            library.Reference.ApiAssembly,
                            Source("partial-source")),
                    ]));

        DocumentationCompiledXmlAttempt.Incomplete incomplete =
            Assert.IsType<
                DocumentationCompiledXmlAttempt.Incomplete>(
                    completed.CompiledXmlAttempt);
        Assert.Equal(
            DocumentationIncompleteBoundary.CompanionSelectionPartial,
            incomplete.Boundary);
        Assert.Equal(0, completed.Work.CompiledXmlBytesObserved);
    }

    [Fact]
    public async Task MalformedXml_IsVisibleFailedAttempt()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(
                Encoding.UTF8.GetBytes("<doc><members>"));
        DocumentationSubjectReference subject =
            Subject(library);

        DocumentationHouseOutcome.Completed completed =
            await ExecuteCompletedAsync(
                library,
                Request(
                    subject,
                    [Candidate(library, subject, xmlIndex: 0)]));

        DocumentationCompiledXmlAttempt.Failed failed =
            Assert.IsType<DocumentationCompiledXmlAttempt.Failed>(
                completed.CompiledXmlAttempt);
        Assert.Equal(
            DocumentationCompiledXmlFailureKind
                .MalformedOrUnreadableDocument,
            failed.Failure.Kind);
    }

    [Fact]
    public async Task CSharpTextLimitExhaustion_IsVisibleFailedAttempt()
    {
        byte[] xml = Encoding.UTF8.GetBytes(
            $"""
             <doc><members>
               <member name="T:System.Text.Json.JsonSerializer"/>
               <member name="{DeserializeIdentity}">
                 <summary>selected</summary>
               </member>
             </members></doc>
             """);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject = Subject(library);

        DocumentationHouseOutcome.Completed completed =
            await ExecuteCompletedAsync(
                library,
                Request(
                    subject,
                    [Candidate(library, subject, xmlIndex: 0)],
                    xmlReadLimits:
                        XmlDocumentationReadLimits.Default
                            with
                        { MaxMembers = 1 }));

        DocumentationCompiledXmlAttempt.Failed failed =
            Assert.IsType<DocumentationCompiledXmlAttempt.Failed>(
                completed.CompiledXmlAttempt);
        Assert.Equal(
            DocumentationCompiledXmlFailureKind
                .MalformedOrUnreadableDocument,
            failed.Failure.Kind);
    }

    [Fact]
    public async Task ByteAndContributionLimits_AreVisibleIncompleteAttempts()
    {
        byte[] xml = Xml(DeserializeIdentity, "selected");
        await using LibraryFixture bytesLibrary =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference bytesSubject =
            Subject(bytesLibrary);
        DocumentationHouseOutcome.Completed bytes =
            await ExecuteCompletedAsync(
                bytesLibrary,
                Request(
                    bytesSubject,
                    [Candidate(bytesLibrary, bytesSubject, xmlIndex: 0)],
                    maximumXmlBytes: xml.Length - 1));
        Assert.Equal(
            DocumentationIncompleteBoundary.CompiledXmlByteLimit,
            Assert.IsType<
                    DocumentationCompiledXmlAttempt.Incomplete>(
                    bytes.CompiledXmlAttempt)
                .Boundary);
        Assert.False(bytes.Work.ParsedCompiledXml);

        await using LibraryFixture countLibrary =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference countSubject =
            Subject(countLibrary);
        CompiledXmlContribution unavailable =
            CompiledXmlContribution.Unavailable(
                countSubject,
                countLibrary.Reference,
                countLibrary.Reference.ApiAssembly,
                Source("unavailable"));
        DocumentationHouseOutcome.Completed count =
            await ExecuteCompletedAsync(
                countLibrary,
                Request(
                    countSubject,
                    [unavailable, unavailable],
                    maximumContributions: 1));
        Assert.Equal(
            DocumentationIncompleteBoundary.ContributionLimit,
            Assert.IsType<
                    DocumentationCompiledXmlAttempt.Incomplete>(
                    count.CompiledXmlAttempt)
                .Boundary);
        Assert.Equal(2, count.Work.ContributionsObserved);
    }

    [Fact]
    public async Task ExpiredDeadline_IsTopLevelIncomplete()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject =
            Subject(library);
        DocumentationHouseRequest request = Request(
            subject,
            [],
            deadline: DateTimeOffset.UtcNow.AddSeconds(-1));

        DocumentationHouseOutcome.Incomplete incomplete =
            Assert.IsType<DocumentationHouseOutcome.Incomplete>(
                await DocumentationHouse.ExecuteAsync(
                    request,
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            DocumentationIncompleteBoundary.Deadline,
            incomplete.Boundary);
    }

    [Fact]
    public async Task
        DeadlineReachedDuringSelection_PreventsContentSnapshot()
    {
        const int contributionCount = 4_000_000;
        byte[] xml = Xml(DeserializeIdentity, "selected");
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject = Subject(library);
        CompiledXmlContribution candidate =
            Candidate(library, subject, xmlIndex: 0, precedence: 0);
        CompiledXmlContribution[] contributions =
            Enumerable.Repeat(candidate, contributionCount).ToArray();
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(1);
        DocumentationHouseRequest request = Request(
            subject,
            contributions,
            maximumContributions: contributionCount,
            deadline: deadline);

        while (deadline - DateTimeOffset.UtcNow
            > TimeSpan.FromMilliseconds(50))
        {
            Thread.SpinWait(10_000);
        }

        DocumentationHouseOutcome.Incomplete incomplete =
            Assert.IsType<DocumentationHouseOutcome.Incomplete>(
                await DocumentationHouse.ExecuteAsync(
                    request,
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            DocumentationIncompleteBoundary.Deadline,
            incomplete.Boundary);
        Assert.Equal(
            contributionCount,
            incomplete.Work.ContributionsObserved);
        Assert.Equal(0, incomplete.Work.CompiledXmlBytesObserved);
        Assert.False(incomplete.Work.ParsedCompiledXml);
    }

    [Fact]
    public async Task
        DeadlineReachedDuringSnapshot_PreventsCompiledXmlParsing()
    {
        const int xmlSize = 48 * 1024 * 1024;
        byte[] xml = LargeXml(xmlSize);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject = Subject(library);
        var xmlReadLimits =
            XmlDocumentationReadLimits.Default with
            {
                MaxCharactersInDocument = xml.Length + 1024L,
                MaxRetainedTextCharacters = xml.Length + 1024L,
            };

        using LibraryOperationLease calibration =
            library.IssueOperation();
        Stopwatch snapshotTime = Stopwatch.StartNew();
        calibration.Snapshot(
            library.XmlContents[0],
            static (view, _) => view.Content.ToArray(),
            TestContext.Current.CancellationToken);
        snapshotTime.Stop();

        bool crossedBoundary = false;
        for (double fraction = 0.9;
            fraction >= 0.1;
            fraction -= 0.1)
        {
            DateTimeOffset deadline =
                DateTimeOffset.UtcNow.AddMilliseconds(250);
            DocumentationHouseRequest request = Request(
                subject,
                [Candidate(library, subject, xmlIndex: 0)],
                maximumXmlBytes: xml.Length + 1,
                xmlReadLimits: xmlReadLimits,
                deadline: deadline);
            TimeSpan remainingSnapshot =
                TimeSpan.FromTicks(
                    (long)(snapshotTime.Elapsed.Ticks * fraction));
            while (deadline - DateTimeOffset.UtcNow > remainingSnapshot)
                Thread.SpinWait(10_000);

            DocumentationHouseOutcome outcome =
                await DocumentationHouse.ExecuteAsync(
                    request,
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken);
            if (outcome is not DocumentationHouseOutcome.Completed completed
                || completed.CompiledXmlAttempt
                    is not DocumentationCompiledXmlAttempt.Incomplete
                    {
                        Boundary:
                            DocumentationIncompleteBoundary.Deadline,
                    }
                || completed.Work.CompiledXmlBytesObserved != xml.Length
                || completed.Work.ParsedCompiledXml)
            {
                continue;
            }

            crossedBoundary = true;
            break;
        }

        Assert.True(
            crossedBoundary,
            $"No snapshot crossed its calibrated deadline; snapshot was {snapshotTime.Elapsed.TotalMilliseconds:F1} ms.");
    }

    [Fact]
    public async Task
        SharedParseCompletedAfterFirstDeadline_IsChargedExactlyOnce()
    {
        const int xmlSize = 48 * 1024 * 1024;
        byte[] xml = LargeXml(xmlSize);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject = Subject(library);
        CompiledXmlContribution[] contributions =
            [Candidate(library, subject, xmlIndex: 0)];
        var xmlReadLimits =
            XmlDocumentationReadLimits.Default with
            {
                MaxCharactersInDocument = xml.Length + 1024L,
                MaxRetainedTextCharacters = xml.Length + 1024L,
            };

        using LibraryOperationLease snapshotCalibration =
            library.IssueOperation();
        Stopwatch snapshotTime = Stopwatch.StartNew();
        snapshotCalibration.Snapshot(
            library.XmlContents[0],
            static (view, _) => view.Content.ToArray(),
            TestContext.Current.CancellationToken);
        snapshotTime.Stop();

        Stopwatch operationTime = Stopwatch.StartNew();
        await DocumentationHouse.ExecuteAsync(
            Request(
                subject,
                contributions,
                maximumXmlBytes: xml.Length + 1,
                xmlReadLimits: xmlReadLimits),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);
        operationTime.Stop();
        TimeSpan parseWindow =
            operationTime.Elapsed - snapshotTime.Elapsed;

        bool crossedBoundary = false;
        for (double fraction = 0.9;
            fraction >= 0.1;
            fraction -= 0.1)
        {
            TimeSpan executionBudget =
                snapshotTime.Elapsed
                    + TimeSpan.FromTicks(
                        (long)(parseWindow.Ticks * fraction));
            DateTimeOffset deadline =
                DateTimeOffset.UtcNow.AddMilliseconds(500);
            while (deadline - DateTimeOffset.UtcNow > executionBudget)
                Thread.SpinWait(10_000);

            DocumentationHouseRequest first = Request(
                subject,
                contributions,
                maximumXmlBytes: xml.Length + 1,
                xmlReadLimits: xmlReadLimits,
                deadline: deadline);
            DocumentationHouseRequest second = Request(
                subject,
                contributions,
                maximumXmlBytes: xml.Length + 1,
                xmlReadLimits: xmlReadLimits);
            IReadOnlyList<DocumentationHouseOutcome> outcomes =
                await DocumentationHouse.ExecuteManyAsync(
                    [first, second],
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken);
            if (outcomes[0]
                    is not DocumentationHouseOutcome.Completed firstCompleted
                || firstCompleted.CompiledXmlAttempt
                    is not DocumentationCompiledXmlAttempt.Incomplete
                    {
                        Boundary:
                            DocumentationIncompleteBoundary.Deadline,
                    }
                || !firstCompleted.Work.ParsedCompiledXml)
            {
                continue;
            }

            crossedBoundary = true;
            Assert.Equal(
                xml.Length,
                firstCompleted.Work.CompiledXmlBytesObserved);
            DocumentationHouseOutcome.Completed secondCompleted =
                Assert.IsType<DocumentationHouseOutcome.Completed>(
                    outcomes[1]);
            Assert.IsType<DocumentationCompiledXmlAttempt.Available>(
                secondCompleted.CompiledXmlAttempt);
            Assert.False(secondCompleted.Work.ParsedCompiledXml);
            Assert.Equal(
                0,
                secondCompleted.Work.CompiledXmlBytesObserved);
            break;
        }

        Assert.True(
            crossedBoundary,
            $"No parse crossed its calibrated deadline; snapshot was {snapshotTime.Elapsed.TotalMilliseconds:F1} ms and full operation was {operationTime.Elapsed.TotalMilliseconds:F1} ms.");
    }

    [Fact]
    public async Task
        DeadlineReachedDuringAbsenceClassification_IsIncomplete()
    {
        const int contributionCount = 4_000_000;
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        DocumentationSubjectReference subject = Subject(library);
        CompiledXmlContribution unavailable =
            CompiledXmlContribution.Unavailable(
                subject,
                library.Reference,
                library.Reference.ApiAssembly,
                Source("unavailable"));
        CompiledXmlContribution[] contributions =
            Enumerable.Repeat(unavailable, contributionCount).ToArray();
        DocumentationHouseRequest baselineRequest = Request(
            subject,
            contributions,
            maximumContributions: contributionCount);
        TimeSpan baselineTime = TimeSpan.MaxValue;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            Stopwatch measurement = Stopwatch.StartNew();
            DocumentationHouseOutcome baseline =
                await DocumentationHouse.ExecuteAsync(
                    baselineRequest,
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken);
            measurement.Stop();
            Assert.IsType<DocumentationHouseOutcome.Completed>(baseline);
            baselineTime = TimeSpan.FromTicks(
                Math.Min(
                    baselineTime.Ticks,
                    measurement.Elapsed.Ticks));
        }

        bool crossedDeadline = false;
        for (double fraction = 0.95;
            fraction >= 0.5;
            fraction -= 0.05)
        {
            TimeSpan executionBudget =
                TimeSpan.FromTicks(
                    (long)(baselineTime.Ticks * fraction));
            DateTimeOffset deadline =
                DateTimeOffset.UtcNow.AddMilliseconds(250);
            DocumentationHouseRequest request = Request(
                subject,
                contributions,
                maximumContributions: contributionCount,
                deadline: deadline);
            while (deadline - DateTimeOffset.UtcNow > executionBudget)
            {
                Thread.SpinWait(10_000);
            }

            Stopwatch executionTime = Stopwatch.StartNew();
            DocumentationHouseOutcome outcome =
                await DocumentationHouse.ExecuteAsync(
                    request,
                    library.IssueOperation(),
                    TestContext.Current.CancellationToken);
            executionTime.Stop();
            if (executionTime.Elapsed
                < executionBudget + TimeSpan.FromMilliseconds(2))
            {
                continue;
            }

            crossedDeadline = true;
            DocumentationHouseOutcome.Incomplete incomplete =
                Assert.IsType<DocumentationHouseOutcome.Incomplete>(
                    outcome);
            Assert.Equal(
                DocumentationIncompleteBoundary.Deadline,
                incomplete.Boundary);
            Assert.Equal(
                contributionCount,
                incomplete.Work.ContributionsObserved);
            Assert.Equal(0, incomplete.Work.CompiledXmlBytesObserved);
            Assert.False(incomplete.Work.ParsedCompiledXml);
            break;
        }

        Assert.True(
            crossedDeadline,
            $"No execution crossed its calibrated deadline; baseline was {baselineTime.TotalMilliseconds:F1} ms.");
    }

    [Fact]
    public async Task Cancellation_SettlesTransferredLeaseBeforePropagating()
    {
        byte[] xml = Xml(DeserializeIdentity, "selected");
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject =
            Subject(library);
        DocumentationHouseRequest request = Request(
            subject,
            [Candidate(library, subject, xmlIndex: 0)]);
        LibraryOperationLease operation = library.IssueOperation();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        OperationCanceledException failure =
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () =>
                    await DocumentationHouse.ExecuteAsync(
                        request,
                        operation,
                        cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        AssertOperationSettled(operation, library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        CancellationDuringSnapshot_IsObservedBeforeParsing()
    {
        const int xmlSize = 48 * 1024 * 1024;
        byte[] xml = LargeXml(xmlSize);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject = Subject(library);
        DocumentationHouseRequest request = Request(
            subject,
            [Candidate(library, subject, xmlIndex: 0)],
            maximumXmlBytes: xml.Length + 1,
            xmlReadLimits:
                XmlDocumentationReadLimits.Default with
                {
                    MaxCharactersInDocument = xml.Length + 1024L,
                    MaxRetainedTextCharacters = xml.Length + 1024L,
                });

        using LibraryOperationLease calibration =
            library.IssueOperation();
        Stopwatch snapshotTime = Stopwatch.StartNew();
        calibration.Snapshot(
            library.XmlContents[0],
            static (view, _) => view.Content.ToArray(),
            TestContext.Current.CancellationToken);
        snapshotTime.Stop();

        using var cancellation = new CancellationTokenSource();
        Stopwatch executionTime = Stopwatch.StartNew();
        using var cancelThreadStarted = new ManualResetEventSlim();
        Thread cancelThread = new(
            () =>
            {
                cancelThreadStarted.Set();
                while (executionTime.Elapsed
                    < snapshotTime.Elapsed / 2)
                {
                    Thread.SpinWait(10_000);
                }

                cancellation.Cancel();
            });
        cancelThread.IsBackground = true;
        cancelThread.Start();
        cancelThreadStarted.Wait(
            TestContext.Current.CancellationToken);

        OperationCanceledException failure =
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () =>
                    await DocumentationHouse.ExecuteAsync(
                        request,
                        library.IssueOperation(),
                        cancellation.Token));
        executionTime.Stop();
        cancelThread.Join();

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.True(
            executionTime.Elapsed
                < snapshotTime.Elapsed * 5
                    + TimeSpan.FromMilliseconds(100),
            $"Cancellation took {executionTime.Elapsed.TotalMilliseconds:F1} ms after a {snapshotTime.Elapsed.TotalMilliseconds:F1} ms snapshot.");
    }

    [Fact]
    public async Task OwnerRetirementAfterIssuance_DrainsCompletedOperation()
    {
        byte[] xml = Xml(DeserializeIdentity, "selected");
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(xml);
        DocumentationSubjectReference subject =
            Subject(library);
        LibraryOperationLease operation = library.IssueOperation();
        Task retirement = library.BeginRetirement();
        Assert.False(retirement.IsCompleted);

        DocumentationHouseOutcome.Completed completed =
            Assert.IsType<DocumentationHouseOutcome.Completed>(
                await DocumentationHouse.ExecuteAsync(
                    Request(
                        subject,
                        [Candidate(library, subject, xmlIndex: 0)]),
                    operation,
                    TestContext.Current.CancellationToken));
        await retirement;

        Assert.IsType<DocumentationCompiledXmlAttempt.Available>(
            completed.CompiledXmlAttempt);
        Assert.Equal(
            LibraryContentOwnerState.Released,
            library.OwnerState);
    }

    [Fact]
    public async Task SubjectFactorySnapshotsMetadataIssuedIdentity()
    {
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync();
        MetadataSubject metadata =
            SelectMetadataSubject(library.ApiSurfaceCorrespondence);

        DocumentationSubjectReference subject =
            DocumentationSubjectReference.ForMember(
                metadata.Correspondence,
                metadata.Type,
                metadata.Member);
        MetadataTypeDefinitionName typeIdentity = subject.TypeIdentity;
        string selector = subject.MemberIdentity!.StableSelector;
        metadata.Type.Name = "Changed";
        metadata.Member.Name = "Changed";

        Assert.Equal(DeserializeIdentity, subject.CompiledXmlIdentity.Value);
        Assert.Same(typeIdentity, subject.TypeIdentity);
        Assert.Equal(selector, subject.MemberIdentity.StableSelector);
    }

    [Fact]
    public async Task
        EquivalentIdentityLibraries_CannotCrossPairMetadataAndLibrary()
    {
        await using LibraryFixture first =
            await LibraryFixture.CreateAsync();
        await using LibraryFixture second =
            await LibraryFixture.CreateAsync();
        LibraryApiSurfaceCorrespondence firstCorrespondence =
            first.ApiSurfaceCorrespondence;
        LibraryApiSurfaceCorrespondence secondCorrespondence =
            second.ApiSurfaceCorrespondence;
        MetadataSubject secondMetadata =
            SelectMetadataSubject(secondCorrespondence);

        Assert.True(
            firstCorrespondence.ApiContent.AssemblyIdentity!.Identity
                .IsEquivalentTo(
                    secondCorrespondence.ApiContent.AssemblyIdentity!
                        .Identity));
        Assert.NotSame(first.Reference, second.Reference);
        Assert.NotSame(
            firstCorrespondence.ApiContent,
            secondCorrespondence.ApiContent);
        Assert.NotSame(
            firstCorrespondence.ApiContent.Artifact,
            secondCorrespondence.ApiContent.Artifact);
        Assert.Throws<ArgumentException>(
            () => DocumentationSubjectReference.ForMember(
                firstCorrespondence,
                secondMetadata.Type,
                secondMetadata.Member));

        DocumentationSubjectReference subject =
            DocumentationSubjectReference.ForMember(
                secondCorrespondence,
                secondMetadata.Type,
                secondMetadata.Member);
        Assert.Same(
            secondCorrespondence,
            subject.ApiSurfaceCorrespondence);
        Assert.Same(second.Reference, subject.Library);
        Assert.Same(
            second.Reference.ApiAssembly,
            subject.ApiContent);
    }

    [Fact]
    public void PublicOutcomeClosureRetainsNoLiveResourceTypes()
    {
        var seen = new HashSet<Type>();
        foreach (Type root in new[]
        {
            typeof(DocumentationHouseRequest),
            typeof(CompiledXmlContribution),
            typeof(DocumentationCompiledXmlAttempt),
            typeof(DocumentationHouseReceipt),
            typeof(DocumentationHouseOutcome),
            typeof(DocumentationAuthoredSourceOperationBinding),
            typeof(DocumentationAuthoredSourceContribution),
            typeof(DocumentationAuthoredSourceOperationReceipt),
            typeof(DocumentationAuthoredSourceOperationOutcome),
        })
        {
            Visit(root);
        }

        Assert.DoesNotContain(typeof(LibraryContentOwner), seen);
        Assert.DoesNotContain(typeof(LibraryOperationLease), seen);
        Assert.DoesNotContain(typeof(Stream), seen);
        Assert.DoesNotContain(typeof(Delegate), seen);

        void Visit(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (!seen.Add(type)
                || type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(DateTimeOffset)
                || type == typeof(TimeSpan)
                || type == typeof(Version))
            {
                return;
            }
            if (type.IsArray)
            {
                Visit(type.GetElementType()!);
                return;
            }
            if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments())
                    Visit(argument);
                return;
            }

            Assert.False(type.IsByRefLike, type.FullName);
            Assert.False(
                typeof(IDisposable).IsAssignableFrom(type),
                type.FullName);
            Assert.False(
                typeof(IAsyncDisposable).IsAssignableFrom(type),
                type.FullName);
            Assert.False(
                typeof(Delegate).IsAssignableFrom(type),
                type.FullName);
            if (type.Assembly == typeof(object).Assembly)
                return;

            foreach (Type nested in type.GetNestedTypes(
                BindingFlags.Public))
            {
                Visit(nested);
            }
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                Visit(property.PropertyType);
            }
        }
    }

    private static async Task<DocumentationHouseOutcome.Completed>
        ExecuteCompletedAsync(
            LibraryFixture library,
            DocumentationHouseRequest request) =>
        Assert.IsType<DocumentationHouseOutcome.Completed>(
            await DocumentationHouse.ExecuteAsync(
                request,
                library.IssueOperation(),
                TestContext.Current.CancellationToken));

    private static DocumentationHouseRequest Request(
        DocumentationSubjectReference subject,
        IReadOnlyList<CompiledXmlContribution> contributions,
        int maximumContributions = 8,
        int maximumXmlBytes = 8 * 1024 * 1024,
        XmlDocumentationReadLimits? xmlReadLimits = null,
        DateTimeOffset? deadline = null)
    {
        var limits = new DocumentationHouseLimits(
            maximumContributions,
            maximumXmlBytes,
            xmlReadLimits ?? XmlDocumentationReadLimits.Default);
        var plan = new DocumentationHouseOperationPlan(
            DocumentationHouseOperationPlanIdentity.Create("compiled-plan"),
            DocumentationHousePolicyGeneration.Create("policy-1"),
            limits,
            deadline ?? DateTimeOffset.UtcNow.AddMinutes(1),
            contributions);
        return new DocumentationHouseRequest(
            DocumentationHouseRequestIdentity.Create("compiled-request"),
            subject,
            DocumentationDemand.CompiledXml,
            plan);
    }

    private static DocumentationSubjectReference Subject(
        LibraryFixture library)
    {
        MetadataSubject metadata =
            SelectMetadataSubject(library.ApiSurfaceCorrespondence);
        return DocumentationSubjectReference.ForMember(
            metadata.Correspondence,
            metadata.Type,
            metadata.Member);
    }

    private static MetadataSubject SelectMetadataSubject(
        LibraryApiSurfaceCorrespondence correspondence)
    {
        ApiType type = Assert.Single(
            correspondence.Surface.Types,
            candidate =>
                candidate.FullName
                    == "System.Text.Json.JsonSerializer");
        ApiMember member = Assert.Single(
            type.Members,
            candidate =>
                ApiMemberIdentity.TryGetXmlDocMemberIdentity(
                    type,
                    candidate,
                    out XmlDocMemberIdentity identity)
                && identity.Value == DeserializeIdentity);
        return new MetadataSubject(correspondence, type, member);
    }

    private static CompiledXmlContribution Candidate(
        LibraryFixture library,
        DocumentationSubjectReference subject,
        int xmlIndex,
        int? precedence = null) =>
        CompiledXmlContribution.Candidate(
            subject,
            library.Reference,
            library.Reference.ApiAssembly,
            Source($"xml-{xmlIndex}"),
            library.XmlContents[xmlIndex],
            precedence);

    private static DocumentationSourceReference Source(string name) =>
        DocumentationSourceReference.Create(
            DocumentationSourceKind.DirectLibrary,
            name);

    private static byte[] Xml(string identity, string summary) =>
        Encoding.UTF8.GetBytes(
            $"""
             <?xml version="1.0"?>
             <doc>
               <members>
                 <member name="{identity}">
                   <summary>{summary}</summary>
                 </member>
               </members>
             </doc>
             """);

    private static byte[] LargeXml(int size)
    {
        byte[] prefix = Encoding.UTF8.GetBytes(
            """
            <?xml version="1.0"?>
            <doc><members><!--
            """);
        byte[] suffix = Encoding.UTF8.GetBytes(
            $"""
             --><member name="{DeserializeIdentity}">
             <summary>selected</summary></member></members></doc>
             """);
        byte[] bytes = GC.AllocateUninitializedArray<byte>(size);
        Array.Fill(bytes, (byte)' ');
        prefix.CopyTo(bytes, 0);
        suffix.CopyTo(bytes, bytes.Length - suffix.Length);
        return bytes;
    }

    private static string RealAsset(string fileName) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "DocumentationHouse",
            fileName);

    private static ManagedMetadataIdentity.Assembly AssemblyIdentity(
        byte[] content)
    {
        using var peReader = new PEReader(
            new MemoryStream(content, writable: false));
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(peReader);
        return new ManagedMetadataIdentity.Assembly(
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader));
    }

    private static void AssertOperationSettled(
        LibraryOperationLease operation,
        LibraryContentReference content) =>
        Assert.Throws<ObjectDisposedException>(
            () => operation.Snapshot(
                content,
                static (_, _) => true));

    private sealed class LibraryFixture : IAsyncDisposable
    {
        private readonly ArtifactFixture _artifacts;
        private readonly LibraryContentOwner _owner;
        private LibraryApiSurfaceCorrespondence? _apiSurfaceCorrespondence;

        private LibraryFixture(
            ArtifactFixture artifacts,
            LibraryReference reference,
            LibraryContentOwner owner)
        {
            _artifacts = artifacts;
            Reference = reference;
            _owner = owner;
            XmlContents =
                reference.Contents
                    .Where(
                        static content =>
                            content.HasRole(
                                LibraryContentRole
                                    .CompiledXmlDocumentation))
                    .ToArray();
        }

        public LibraryReference Reference { get; }
        public IReadOnlyList<LibraryContentReference> XmlContents { get; }
        public LibraryContentOwnerState OwnerState => _owner.State;
        public LibraryApiSurfaceCorrespondence ApiSurfaceCorrespondence =>
            _apiSurfaceCorrespondence ??= InspectApiSurface();

        public LibraryOperationLease IssueOperation() =>
            Assert.IsType<
                LibraryOperationLeaseIssueOutcome.Issued>(
                    _owner.IssueOperationLease(Reference))
                .Lease;

        public Task BeginRetirement() =>
            _owner.DisposeAsync().AsTask();

        public static async Task<LibraryFixture> CreateAsync(
            params byte[][] compiledXml)
        {
            byte[] assembly = s_realAssembly.Value;
            byte[][] contents = [assembly, .. compiledXml];
            ArtifactFixture artifacts =
                await ArtifactFixture.CreateAsync(contents);
            try
            {
                ManagedMetadataIdentity.Assembly identity =
                    AssemblyIdentity(assembly);
                LibraryReference reference =
                    LibraryReference.CreateDirect(
                        new LibraryAssemblyCorrespondence(
                            artifacts[0],
                            identity,
                            artifacts[0],
                            identity),
                        Enumerable.Range(0, compiledXml.Length)
                            .Select(
                                index =>
                                    new LibraryCompanionCorrespondence(
                                        artifacts[index + 1],
                                        LibraryContentRole
                                            .CompiledXmlDocumentation,
                                        artifacts[0])));
                var owner = new LibraryContentOwner(
                    reference,
                    artifacts.IssueContentLeases());
                return new LibraryFixture(
                    artifacts,
                    reference,
                    owner);
            }
            catch
            {
                await artifacts.DisposeAsync();
                throw;
            }
        }

        public static async Task<LibraryFixture> CreateSourceAsync(
            byte[] assembly,
            byte[] portablePdb)
        {
            ArtifactFixture artifacts =
                await ArtifactFixture.CreateAsync(
                    [assembly, portablePdb]);
            try
            {
                ManagedMetadataIdentity.Assembly identity =
                    AssemblyIdentity(assembly);
                LibraryReference reference =
                    LibraryReference.CreateDirect(
                        new LibraryAssemblyCorrespondence(
                            artifacts[0],
                            identity,
                            artifacts[0],
                            identity),
                        [
                            new LibraryCompanionCorrespondence(
                                artifacts[1],
                                LibraryContentRole.PortablePdb,
                                artifacts[0]),
                        ]);
                var owner = new LibraryContentOwner(
                    reference,
                    artifacts.IssueContentLeases());
                return new LibraryFixture(
                    artifacts,
                    reference,
                    owner);
            }
            catch
            {
                await artifacts.DisposeAsync();
                throw;
            }
        }

        public static async Task<LibraryFixture> CreateDistinctSourceAsync(
            byte[] assembly,
            byte[] portablePdb)
        {
            ArtifactFixture artifacts =
                await ArtifactFixture.CreateAsync(
                    [assembly, assembly, portablePdb]);
            try
            {
                ManagedMetadataIdentity.Assembly identity =
                    AssemblyIdentity(assembly);
                LibraryReference reference =
                    LibraryReference.CreateDirect(
                        new LibraryAssemblyCorrespondence(
                            artifacts[0],
                            identity,
                            artifacts[1],
                            identity),
                        [
                            new LibraryCompanionCorrespondence(
                                artifacts[2],
                                LibraryContentRole.PortablePdb,
                                artifacts[1]),
                        ]);
                var owner = new LibraryContentOwner(
                    reference,
                    artifacts.IssueContentLeases());
                return new LibraryFixture(
                    artifacts,
                    reference,
                    owner);
            }
            catch
            {
                await artifacts.DisposeAsync();
                throw;
            }
        }

        private LibraryApiSurfaceCorrespondence InspectApiSurface()
        {
            using LibraryOperationLease operation = IssueOperation();
            var request = new LibraryApiSurfaceInspectionRequest(
                Reference,
                ApiSurfaceExtractionScope.Public,
                s_apiSurfaceBounds);
            return Assert.IsType<
                    LibraryApiSurfaceInspectionOutcome.Completed>(
                    LibraryApiSurfaceInspection.Execute(
                        request,
                        operation,
                        TestContext.Current.CancellationToken))
                .Correspondence;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _owner.DisposeAsync();
            }
            finally
            {
                await _artifacts.DisposeAsync();
            }
        }
    }

    private sealed class ArtifactFixture : IAsyncDisposable
    {
        private readonly ArtifactSetSession _session;
        private readonly ArtifactQueryLease _queryLease;
        private readonly IReadOnlyList<ArtifactContentReference> _references;

        private ArtifactFixture(
            ArtifactSetSession session,
            ArtifactQueryLease queryLease,
            IReadOnlyList<ArtifactContentReference> references)
        {
            _session = session;
            _queryLease = queryLease;
            _references = references;
        }

        public ArtifactContentReference this[int index] =>
            _references[index];

        public ArtifactContentLease[] IssueContentLeases() =>
            _references
                .Select(
                    reference =>
                        _session.IssueContentLease(
                            reference,
                            _queryLease))
                .ToArray();

        public static async Task<ArtifactFixture> CreateAsync(
            IReadOnlyList<byte[]> contents)
        {
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            var session = new ArtifactSetSession();
            try
            {
                for (int index = 0; index < contents.Count; index++)
                {
                    int retainedIndex = index;
                    byte[] retainedContent = contents[index];
                    await session.AddRequiredAcquisitionAsync(
                        (scope, _) =>
                        {
                            ArtifactContribution contribution =
                                scope.Register(
                                    new Provenance(
                                        $"documentation-{retainedIndex}"),
                                    _ => new MemoryStream(
                                        retainedContent,
                                        writable: false));
                            return ValueTask.FromResult<
                                ArtifactAcquisitionOutcome>(
                                    new ArtifactAcquisitionOutcome.Acquired(
                                        [contribution],
                                        ArtifactAcquisitionLeases.None));
                        },
                        cancellationToken: cancellationToken);
                }

                Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                    await session.SealAsync(cancellationToken));
                ArtifactQueryAuthorization authorization =
                    session.CreateQueryAuthorization();
                ArtifactQueryLease queryLease =
                    session.IssueLease(authorization);
                IReadOnlyList<ArtifactContentReference> references =
                    session.GetCatalog(queryLease)
                        .Select(
                            descriptor =>
                                session.GetContentReference(
                                    descriptor.Identity,
                                    queryLease))
                        .ToArray();
                return new ArtifactFixture(
                    session,
                    queryLease,
                    references);
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _queryLease.Dispose();
            await _session.DisposeAsync();
        }
    }

    private sealed record Provenance(string Name) : IArtifactProvenance;

    private sealed record MetadataSubject(
        LibraryApiSurfaceCorrespondence Correspondence,
        ApiType Type,
        ApiMember Member);
}
