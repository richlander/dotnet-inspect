using System.Diagnostics;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Text;

using CSharpText;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.DocumentationHouse.Tests;

public sealed class CompiledXmlDocumentationHouseTests
{
    private const string DeserializeIdentity =
        "M:System.Text.Json.JsonSerializer.Deserialize``1(System.Text.Json.JsonDocument,System.Text.Json.JsonSerializerOptions)";
    private static readonly Lazy<MetadataSubject> s_metadataSubject =
        new(LoadMetadataSubject);

    [Fact]
    public async Task
        RealSystemTextJsonMember_SettlesAvailableDetachedDocumentation()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            RealAsset("System.Text.Json.dll"),
            TestContext.Current.CancellationToken);
        byte[] xml = await File.ReadAllBytesAsync(
            RealAsset("System.Text.Json.xml"),
            TestContext.Current.CancellationToken);
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync(assembly, xml);
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
            await LibraryFixture.CreateAsync([1], xml);
        await using LibraryFixture foreign =
            await LibraryFixture.CreateAsync([2], xml);
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
            await LibraryFixture.CreateAsync([1], xml);
        await using LibraryFixture foreign =
            await LibraryFixture.CreateAsync([2], xml);
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
            await LibraryFixture.CreateAsync([1], xml);
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
            await LibraryFixture.CreateAsync([1]);
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
            await LibraryFixture.CreateAsync([2]);
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
            await LibraryFixture.CreateAsync([1], first, second);
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
            await LibraryFixture.CreateAsync([1], xml);
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
            await LibraryFixture.CreateAsync([1], preferred, other);
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
            await LibraryFixture.CreateAsync([1], xml);
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
                [1],
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
            await LibraryFixture.CreateAsync([1], xml);
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
            await LibraryFixture.CreateAsync([1], xml);
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
            await LibraryFixture.CreateAsync([2]);
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
            await LibraryFixture.CreateAsync([1]);
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
            await LibraryFixture.CreateAsync([1], xml);
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
    public async Task Cancellation_SettlesTransferredLeaseBeforePropagating()
    {
        byte[] xml = Xml(DeserializeIdentity, "selected");
        await using LibraryFixture library =
            await LibraryFixture.CreateAsync([1], xml);
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
            await LibraryFixture.CreateAsync([1], xml);
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
            await LibraryFixture.CreateAsync([1], xml);
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
            await LibraryFixture.CreateAsync([1]);
        MetadataSubject metadata = LoadMetadataSubject();

        DocumentationSubjectReference subject =
            DocumentationSubjectReference.ForMember(
                metadata.Surface,
                metadata.Type,
                metadata.Member,
                library.Reference,
                library.Reference.ApiAssembly);
        MetadataTypeDefinitionName typeIdentity = subject.TypeIdentity;
        string selector = subject.MemberIdentity!.StableSelector;
        metadata.Type.Name = "Changed";
        metadata.Member.Name = "Changed";

        Assert.Equal(DeserializeIdentity, subject.CompiledXmlIdentity.Value);
        Assert.Same(typeIdentity, subject.TypeIdentity);
        Assert.Equal(selector, subject.MemberIdentity.StableSelector);
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
        MetadataSubject metadata = s_metadataSubject.Value;
        return DocumentationSubjectReference.ForMember(
            metadata.Surface,
            metadata.Type,
            metadata.Member,
            library.Reference,
            library.Reference.ApiAssembly);
    }

    private static MetadataSubject LoadMetadataSubject()
    {
        using var stream = File.OpenRead(
            RealAsset("System.Text.Json.dll"));
        using var reader = new PEReader(stream);
        ApiSurface surface =
            ApiSurfaceExtractor.Extract(reader, includeAll: true);
        ApiType type = Assert.Single(
            surface.Types,
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
        return new MetadataSubject(surface, type, member);
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

        public LibraryOperationLease IssueOperation() =>
            Assert.IsType<
                LibraryOperationLeaseIssueOutcome.Issued>(
                    _owner.IssueOperationLease(Reference))
                .Lease;

        public Task BeginRetirement() =>
            _owner.DisposeAsync().AsTask();

        public static async Task<LibraryFixture> CreateAsync(
            byte[] assembly,
            params byte[][] compiledXml)
        {
            byte[][] contents = [assembly, .. compiledXml];
            ArtifactFixture artifacts =
                await ArtifactFixture.CreateAsync(contents);
            try
            {
                ApiAssemblyIdentity metadataAssembly =
                    s_metadataSubject.Value.Surface.AssemblyIdentity!;
                var identity = new ManagedMetadataIdentity.Assembly(
                    new AssemblyReferenceIdentity(
                        metadataAssembly.Name,
                        metadataAssembly.Version,
                        metadataAssembly.Culture,
                        metadataAssembly.PublicKeyToken));
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
        ApiSurface Surface,
        ApiType Type,
        ApiMember Member);
}
