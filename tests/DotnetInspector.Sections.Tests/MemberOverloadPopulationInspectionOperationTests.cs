using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.DocumentationHouse;
using DotnetInspector.Fixtures;
using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;

namespace DotnetInspector.Sections.Tests;

public sealed class MemberOverloadPopulationInspectionOperationTests
{
    private static readonly ApiSurfaceExtractionBounds s_bounds =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    [Fact]
    public void QuerySpace_RegistersExactPopulationOnce()
    {
        IQueryOperationRoute route =
            MemberOverloadPopulationQuery.OperationRoute;
        QuerySpaceDescriptor descriptor =
            MemberOverloadPopulationQuery.QuerySpace.Descriptor;

        Assert.Equal(
            MemberOverloadPopulationQuery.OperationIdentity,
            route.OperationIdentity);
        Assert.Equal(
            MemberOverloadPopulationQuery.OperationRouteIdentity,
            route.Identity);
        Assert.Equal(
            MemberOverloadPopulationQuery.SubjectRole,
            route.SubjectRole);
        Assert.Equal(
            MemberOverloadPopulationQuery.ResultGrain,
            route.ResultGrain);
        Assert.Equal(
            [MemberOverloadPopulationQuery.RowSet],
            route.RowSets);
        Assert.Equal(
            [
                MemberOverloadPopulationQuery.AccessibilityTermKey,
                MemberOverloadPopulationQuery.ReceiverTermKey,
                MemberOverloadPopulationQuery.IncludeHiddenTermKey,
            ],
            route.Capabilities.Terms.Select(term =>
                term.Binding.Key));
        Assert.Empty(route.Capabilities.Orders);
        Assert.Empty(route.Capabilities.Dimensions);
        Assert.Empty(route.Capabilities.Stages);

        Assert.Same(
            MemberOverloadPopulationQuery.OperationRoute,
            MemberOverloadPopulationQuery.QuerySpace.Operation);
        Assert.Equal(
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            descriptor.Terminals);
        Assert.True(descriptor.AcceptsContinuation);
        Assert.Equal(
            [
                MemberOverloadPopulationQuery.RowsResultContract,
                MemberOverloadPopulationQuery.CountResultContract,
            ],
            descriptor.ResultContracts.Select(contract =>
                contract.Identity));
        Assert.Equal(
            MemberOverloadPopulationQuery.RowScopeIdentity,
            Assert.Single(descriptor.RowScopes).Identity);
    }

    [Theory]
    [InlineData(
        MemberOverloadAccessibilityFilter.Public,
        MemberOverloadReceiverFilter.All,
        false,
        QuerySpaceTerminalRequirement.Count)]
    [InlineData(
        MemberOverloadAccessibilityFilter.Private,
        MemberOverloadReceiverFilter.This,
        true,
        QuerySpaceTerminalRequirement.Rows)]
    [InlineData(
        MemberOverloadAccessibilityFilter.All,
        MemberOverloadReceiverFilter.Extension,
        false,
        QuerySpaceTerminalRequirement.Rows)]
    public void QuerySpaceRequest_ResolvesAcquisitionAndTerminal(
        MemberOverloadAccessibilityFilter accessibility,
        MemberOverloadReceiverFilter receiver,
        bool includeHidden,
        QuerySpaceTerminalRequirement terminal)
    {
        QuerySpaceRequest request =
            MemberOverloadPopulationQuery.CreateRequest(
                accessibility,
                receiver,
                includeHidden,
                MemberOverloadOrdering.Metadata,
                terminal);

        MemberOverloadPopulationQueryRequestResult.Accepted accepted =
            Assert.IsType<
                    MemberOverloadPopulationQueryRequestResult.Accepted>(
                    MemberOverloadPopulationQuery.ResolveRequest(
                        request,
                        TestContext.Current.CancellationToken));
        MemberOverloadPopulationQueryPlan plan = accepted.Plan;

        Assert.Equal(accessibility, plan.Accessibility);
        Assert.Equal(receiver, plan.Receiver);
        Assert.Equal(includeHidden, plan.IncludeHidden);
        Assert.Equal(MemberOverloadOrdering.Metadata, plan.Ordering);
        Assert.Equal(terminal, accepted.Terminal);
        Assert.Equal(3, plan.Intent.Terms.Count);
        Assert.Empty(plan.Intent.Bounds);
        Assert.Empty(plan.Intent.Stages);
        Assert.Empty(plan.Intent.Order);
        Assert.Equal(
            (accessibility is MemberOverloadAccessibilityFilter.Public
                ? 0
                : 1)
            + (receiver is MemberOverloadReceiverFilter.All ? 0 : 1)
            + (includeHidden ? 1 : 0),
            request.Operation.Terms.Count);
        Assert.Equal(
            MemberOverloadPopulationQuery.QuerySpaceIdentity,
            request.QuerySpace);
        Assert.Equal(
            [MemberOverloadPopulationQuery.RowSet],
            request.ParticipatingRowSets);
    }

    [Fact]
    public void QuerySpaceRequest_RejectsUnknownAcquisitionValue()
    {
        QuerySpaceRequest request =
            QuerySpaceRequest.Create(
                MemberOverloadPopulationQuery.QuerySpace.Descriptor,
                PortableQueryIntent.Create(
                    [
                        new(
                            MemberOverloadPopulationQuery
                                .AccessibilityTermKey,
                            PortableQueryOperator.Equal,
                            "family"),
                    ],
                    [],
                    [],
                    []),
                [MemberOverloadPopulationQuery.RowSet],
                [],
                QuerySpaceTerminalRequirement.Count);

        MemberOverloadPopulationQueryRequestResult.IntentRejected rejected =
            Assert.IsType<
                MemberOverloadPopulationQueryRequestResult.IntentRejected>(
                MemberOverloadPopulationQuery.ResolveRequest(
                    request,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PortableQueryFailureReason.ValueRejected,
            rejected.Failure.Reason);
    }

    [Fact]
    public void QuerySpaceRequest_RejectsForeignQuerySpace()
    {
        QuerySpaceRequest foreign =
            PackageFileInventoryQuery.CreateRequest(
                RowSelectionIntent<string>.Empty,
                QuerySpaceTerminalRequirement.Count);

        MemberOverloadPopulationQueryRequestResult.Rejected rejected =
            Assert.IsType<
                MemberOverloadPopulationQueryRequestResult.Rejected>(
                MemberOverloadPopulationQuery.ResolveRequest(
                    foreign,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MemberOverloadPopulationQueryRequestRejectionKind
                .QuerySpaceMismatch,
            rejected.Kind);
    }

    [Fact]
    public void CountAndRowsPlanning_ResolvesAcquisitionOnce()
    {
        var subject = new MemberGroupSubject(
            Name("System.Text.Json", "JsonSerializer"),
            "Deserialize");
        var count = new MemberOverloadPopulationRequest(
            new MemberOverloadCountRequest(),
            rows: null,
            MemberOverloadAccessibilityFilter.Public,
            MemberOverloadReceiverFilter.All,
            includeHidden: false);
        var countAndRows = new MemberOverloadPopulationRequest(
            new MemberOverloadCountRequest(),
            new(maximumRows: 20),
            MemberOverloadAccessibilityFilter.Public,
            MemberOverloadReceiverFilter.All,
            includeHidden: false);

        _ = PlanningAllocations(subject, count, iterations: 10);
        _ = PlanningAllocations(subject, countAndRows, iterations: 10);

        const int Iterations = 100;
        long countAllocation =
            PlanningAllocations(subject, count, Iterations);
        long countAndRowsAllocation =
            PlanningAllocations(subject, countAndRows, Iterations);

        Assert.True(
            countAndRowsAllocation <= countAllocation * 3 / 2,
            $"Combined Count and Rows repeated operation planning: "
                + $"count={countAllocation:N0} bytes, "
                + $"combined={countAndRowsAllocation:N0} bytes.");
    }

    [Fact]
    public async Task
        RealSerialize_CountAndBoundedRowsShareOneExactPopulation()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        MemberOverloadPopulationContent first =
            Available(
                Execute(
                    library,
                    "Serialize",
                    count: true,
                    new(maximumRows: 4)));
        var count =
            Assert.IsType<MemberOverloadCountOutcome.Counted>(
                first.Overloads.Count);
        var segment =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                first.Overloads.Rows);
        var rows = segment.Items.ToList();
        MemberOverloadContinuation? continuation =
            segment.Continuation;
        MemberOverloadContinuation firstContinuation =
            Assert.IsType<MemberOverloadContinuation>(
                continuation);
        while (continuation is not null)
        {
            MemberOverloadPopulationContent next =
                Available(
                    Execute(
                        library,
                        "Serialize",
                        count: false,
                        new(
                            maximumRows: 3,
                            continuation:
                                continuation)));
            Assert.Equal(
                first.Overloads.Binding,
                next.Overloads.Binding);
            Assert.Null(next.Overloads.Count);
            segment =
                Assert.IsType<MemberOverloadRowsOutcome.Read>(
                    next.Overloads.Rows);
            rows.AddRange(segment.Items);
            continuation = segment.Continuation;
        }

        Assert.Equal(15, count.Value);
        Assert.Equal(count.Value, rows.Count);
        Assert.Equal(
            Enumerable.Range(1, count.Value),
            rows.Select(static row => row.BaselineOrdinal));
        Assert.Equal(
            count.Value,
            rows.Select(static row => row.MetadataToken)
                .Distinct()
                .Count());
        Assert.All(
            rows,
            row =>
            {
                Assert.Equal(
                    first.Overloads.Binding,
                    row.Binding);
                Assert.Equal(
                    MemberReceiver.Static,
                    row.Receiver);
                Assert.Equal(
                    MemberGroupRole.Declared,
                    row.Role);
                Assert.Contains(
                    "Serialize",
                    row.DisplaySignature.ToString());
            });

        MemberOverloadPopulationBinding binding =
            firstContinuation.Binding;
        MemberOverloadPopulationContent incompatible =
            Available(
                Execute(
                    library,
                    "Serialize",
                    count: false,
                    new(
                        maximumRows: 3,
                        continuation:
                            new(
                                new(
                                    binding.Assembly,
                                    binding.ModuleVersionId,
                                    binding.DeclaringType,
                                    checked(
                                        binding.TypeDefinitionToken
                                            + 1),
                                    binding.Name,
                                    binding.Category,
                                    binding.Role,
                                    binding.Ordering),
                                firstContinuation
                                    .NextOrdinal))));
        Assert.Equal(
            MemberOverloadRowsRejection.IncompatibleContinuation,
            Assert.IsType<MemberOverloadRowsOutcome.Rejected>(
                    incompatible.Overloads.Rows)
                .Reason);

        MemberOverloadPopulationContent changedFilter =
            Available(
                Execute(
                    library,
                    "Serialize",
                    count: false,
                    new(
                        maximumRows: 3,
                        continuation:
                            firstContinuation),
                    accessibility:
                        MemberOverloadAccessibilityFilter.All));
        Assert.Equal(
            MemberOverloadRowsRejection.IncompatibleContinuation,
            Assert.IsType<MemberOverloadRowsOutcome.Rejected>(
                    changedFilter.Overloads.Rows)
                .Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RealSerialize_MemberGroupDocumentOwnsCountAndExactRows()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        InspectionEnvelope<MemberGroupDocumentInspectionOutcome> envelope =
            ExecuteDocument(library, "Serialize");
        MemberGroupDocument document =
            Assert.IsType<MemberGroupDocumentInspectionOutcome.Available>(
                    envelope.Content)
                .Document;

        Assert.Equal("Serialize", document.Subject.Name);
        var count =
            Assert.IsType<MemberOverloadCountOutcome.Counted>(
                document.Overloads.Count);
        var rows =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                document.Overloads.Rows);
        Assert.Equal(15, count.Value);
        Assert.Equal(count.Value, rows.Items.Length);
        Assert.Null(rows.Continuation);
        Assert.Equal(
            Enumerable.Range(1, count.Value),
            rows.Items.Select(static row => row.BaselineOrdinal));

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RealSerialize_MemberDocumentSelectsOneBoundExactDeclaration()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        MemberGroupDocument group =
            Assert.IsType<MemberGroupDocumentInspectionOutcome.Available>(
                    ExecuteDocument(library, "Serialize").Content)
                .Document;
        MemberOverloadShape expected =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                    group.Overloads.Rows)
                .Items[5];

        MemberDocument ordinal =
            Assert.IsType<MemberDocumentInspectionOutcome.Available>(
                    ExecuteMemberDocument(
                            library,
                            "Serialize",
                            new(baselineOrdinal: expected.BaselineOrdinal))
                        .Content)
                .Document;
        MemberDocument fingerprint =
            Assert.IsType<MemberDocumentInspectionOutcome.Available>(
                    ExecuteMemberDocument(
                            library,
                            "Serialize",
                            new(
                                fingerprintPrefix:
                                    expected.Fingerprint.ToString()))
                        .Content)
                .Document;

        Assert.Equal(expected.MetadataToken, ordinal.Subject.MetadataToken);
        Assert.Equal(expected.Anchor, ordinal.Subject.Anchor);
        Assert.Equal(
            expected.BaselineOrdinal,
            ordinal.Subject.BaselineOrdinal);
        Assert.Equal(expected.Fingerprint, ordinal.Subject.Fingerprint);
        Assert.Equal(expected.Binding, ordinal.Subject.Population);
        Assert.Equal(expected.DisplaySignature, ordinal.DisplaySignature);
        Assert.Equal(expected.CanonicalSignature, ordinal.CanonicalSignature);
        Assert.Equal(ordinal, fingerprint);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RealSerialize_MemberDocumentPreservesUnavailableSourceOutcome()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        MemberOverloadShape expected =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                    Assert.IsType<
                            MemberGroupDocumentInspectionOutcome.Available>(
                            ExecuteDocument(library, "Serialize").Content)
                        .Document.Overloads.Rows)
                .Items[5];
        int calls = 0;

        using LibraryOperationLease operation = library.IssueOperation();
        InspectionEnvelope<MemberDocumentInspectionOutcome> inspection =
            await MemberDocumentInspectionOperation.ExecuteAsync(
                new(
                    library.Reference,
                    new(
                        new(
                            Name(
                                "System.Text.Json",
                                "JsonSerializer"),
                            "Serialize"),
                        new(
                            baselineOrdinal:
                                expected.BaselineOrdinal),
                        s_bounds,
                        documentation:
                            new(DocumentationDemand.CompiledXml),
                        source:
                            new(
                                includeAuthoredParts: true,
                                allowDecompiledFallback: false))),
                operation,
                (ids, _, _) =>
                {
                    string id = Assert.Single(ids);
                    DocumentationQueryOutcome documentation =
                        new DocumentationQueryOutcome.RequestRejected(
                            DocumentationSubject(
                                expected.Binding,
                                id),
                            DocumentationQueryRequestRejectionReason
                                .LeaseReferenceMismatch);
                    IReadOnlyDictionary<
                        string,
                        DocumentationQueryOutcome> outcomes =
                            new Dictionary<
                                string,
                                DocumentationQueryOutcome>
                            {
                                [id] = documentation,
                            };
                    return ValueTask.FromResult(outcomes);
                },
                (request, _) =>
                {
                    calls++;
                    Assert.Equal(expected.Anchor, request.Member);
                    Assert.Equal(
                        expected.MetadataToken,
                        request.MetadataToken);
                    Assert.True(request.IncludeAuthoredParts);
                    Assert.False(request.AllowDecompiledFallback);
                    AssemblyMemberSourceEntry outcome =
                        new AssemblyMemberSourceEntry.Unavailable(
                            SourceSubject(expected.Binding),
                            request,
                            new(
                                AssemblySourceFailureKind
                                    .AuthoredMemberUnavailable,
                                "Source unavailable."));
                    return ValueTask.FromResult(outcome);
                },
                TestContext.Current.CancellationToken);

        MemberDocument document =
            Assert.IsType<MemberDocumentInspectionOutcome.Available>(
                    inspection.Content)
                .Document;
        MemberSourceAttachment attachment =
            Assert.IsType<MemberSourceAttachment>(document.Source);
        Assert.Equal(1, calls);
        Assert.Equal(document.Subject, attachment.Subject);
        Assert.IsType<MemberDocumentationAttachment>(
            document.Documentation);
        Assert.IsType<AssemblyMemberSourceEntry.Unavailable>(
            attachment.Outcome);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        MemberDocumentRejectsSourceForAnotherExactSubject()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        MemberDocument expected =
            Assert.IsType<MemberDocumentInspectionOutcome.Available>(
                    ExecuteMemberDocument(
                            library,
                            "Serialize",
                            new(baselineOrdinal: 1))
                        .Content)
                .Document;

        using LibraryOperationLease operation = library.IssueOperation();
        InspectionEnvelope<MemberDocumentInspectionOutcome> inspection =
            await MemberDocumentInspectionOperation.ExecuteAsync(
                new(
                    library.Reference,
                    new(
                        new(
                            Name(
                                "System.Text.Json",
                                "JsonSerializer"),
                            "Serialize"),
                        new(baselineOrdinal: 1),
                        s_bounds,
                        source: new())),
                operation,
                documentationProvider: null,
                (request, _) =>
                {
                    AssemblyMemberSourceRequest mismatched =
                        new(
                            request.Type,
                            request.Member with
                            {
                                StableSelector =
                                    $"Other~{request.Member.Fingerprint}",
                            },
                            request.MetadataToken,
                            request.PrinterOptions);
                    AssemblyMemberSourceEntry outcome =
                        new AssemblyMemberSourceEntry.Unavailable(
                            SourceSubject(
                                expected.Subject.Population),
                            mismatched,
                            new(
                                AssemblySourceFailureKind
                                    .AuthoredMemberUnavailable,
                                "Source unavailable."));
                    return ValueTask.FromResult(outcome);
                },
                TestContext.Current.CancellationToken);

        Assert.Equal(
            MemberDocumentInspectionFailure.SourceSubjectMismatch,
            Assert.IsType<MemberDocumentInspectionOutcome.Failed>(
                    inspection.Content)
                .Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RealSerialize_MemberDocumentAttachesOneExactDocumentationOutcome()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        MemberOverloadShape expected =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                    Assert.IsType<
                            MemberGroupDocumentInspectionOutcome.Available>(
                            ExecuteDocument(library, "Serialize").Content)
                        .Document.Overloads.Rows)
                .Items[5];
        int calls = 0;

        using LibraryOperationLease operation = library.IssueOperation();
        InspectionEnvelope<MemberDocumentInspectionOutcome> inspection =
            await MemberDocumentInspectionOperation.ExecuteAsync(
                new(
                    library.Reference,
                    new(
                        new(
                            Name(
                                "System.Text.Json",
                                "JsonSerializer"),
                            "Serialize"),
                        new(
                            baselineOrdinal:
                                expected.BaselineOrdinal),
                        s_bounds,
                        documentation:
                            new(DocumentationDemand.CompiledXml))),
                operation,
                (ids, demand, _) =>
                {
                    calls++;
                    Assert.Equal(
                        DocumentationDemand.CompiledXml,
                        demand);
                    string id = Assert.Single(ids);
                    Assert.Equal(
                        expected.DocumentationId.ToString(),
                        id);
                    DocumentationQueryOutcome outcome =
                        new DocumentationQueryOutcome.RequestRejected(
                            DocumentationSubject(expected.Binding, id),
                            DocumentationQueryRequestRejectionReason
                                .LeaseReferenceMismatch);
                    IReadOnlyDictionary<
                        string,
                        DocumentationQueryOutcome> outcomes =
                            new Dictionary<
                                string,
                                DocumentationQueryOutcome>
                            {
                                [id] = outcome,
                            };
                    return ValueTask.FromResult(outcomes);
                },
                TestContext.Current.CancellationToken);

        MemberDocument document =
            Assert.IsType<MemberDocumentInspectionOutcome.Available>(
                    inspection.Content)
                .Document;
        MemberDocumentationAttachment attachment =
            Assert.IsType<MemberDocumentationAttachment>(
                document.Documentation);
        Assert.Equal(1, calls);
        Assert.Equal(document.Subject, attachment.Subject);
        Assert.Equal(
            expected.DocumentationId.ToString(),
            attachment.Outcome.Subject.DocumentationId);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RealSerialize_MemberGroupBatchesDocumentationForReturnedRowsOnly()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var plan = new MemberOverloadPopulationInspectionPlan(
            new(
                Name("System.Text.Json", "JsonSerializer"),
                "Serialize"),
            new(
                new MemberOverloadCountRequest(),
                new MemberOverloadRowsRequest(maximumRows: 2)),
            s_bounds);
        MemberOverloadRowsOutcome.Read expectedRows =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                Available(
                        Execute(
                            library,
                            "Serialize",
                            count: false,
                            new(maximumRows: 2)))
                    .Overloads.Rows);
        MemberOverloadPopulationBinding expectedBinding =
            expectedRows.Items[0].Binding;
        var request = new MemberGroupDocumentInspectionRequest(
            library.Reference,
            plan,
            new(DocumentationDemand.CompiledXml));
        int calls = 0;

        using LibraryOperationLease operation = library.IssueOperation();
        InspectionEnvelope<MemberGroupDocumentInspectionOutcome> inspection =
            await MemberGroupDocumentInspectionOperation.ExecuteAsync(
                request,
                operation,
                (ids, demand, _) =>
                {
                    calls++;
                    Assert.Equal(
                        DocumentationDemand.CompiledXml,
                        demand);
                    Assert.Equal(2, ids.Count);
                    IReadOnlyDictionary<
                        string,
                        DocumentationQueryOutcome> outcomes =
                            ids.ToDictionary(
                                static id => id,
                                id =>
                                    (DocumentationQueryOutcome)
                                        new DocumentationQueryOutcome
                                            .RequestRejected(
                                                DocumentationSubject(
                                                    expectedBinding,
                                                    id),
                                                DocumentationQueryRequestRejectionReason
                                                    .LeaseReferenceMismatch),
                                StringComparer.Ordinal);
                    return ValueTask.FromResult(outcomes);
                },
                TestContext.Current.CancellationToken);

        MemberGroupDocument document =
            Assert.IsType<MemberGroupDocumentInspectionOutcome.Available>(
                    inspection.Content)
                .Document;
        MemberOverloadRowsOutcome.Read rows =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                document.Overloads.Rows);
        Assert.Equal(1, calls);
        Assert.Equal(2, rows.Items.Length);
        Assert.Equal(2, document.ReturnedRowDocumentation.Length);
        Assert.Equal(
            rows.Items.Select(
                static row => row.DocumentationId.ToString()),
            document.ReturnedRowDocumentation.Select(
                static attachment =>
                    attachment.Outcome.Subject.DocumentationId));
        Assert.All(
            document.ReturnedRowDocumentation,
            attachment =>
                Assert.Equal(
                    document.Subject,
                    attachment.Subject.Group));

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        CountOnlyMemberGroupCannotRequestRowDocumentation()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var plan = new MemberOverloadPopulationInspectionPlan(
            new(Name("N", "C"), "M"),
            new(new MemberOverloadCountRequest()),
            s_bounds);

        Assert.Throws<ArgumentException>(() =>
            new MemberGroupDocumentInspectionRequest(
                library.Reference,
                plan,
                new(DocumentationDemand.CompiledXml)));

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        MemberDocumentRejectsDocumentationForAnotherExactSubject()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        using LibraryOperationLease operation = library.IssueOperation();
        InspectionEnvelope<MemberDocumentInspectionOutcome> inspection =
            await MemberDocumentInspectionOperation.ExecuteAsync(
                new(
                    library.Reference,
                    new(
                        new(
                            Name(
                                "System.Text.Json",
                                "JsonSerializer"),
                            "Serialize"),
                        new(baselineOrdinal: 1),
                        s_bounds,
                        documentation:
                            new(DocumentationDemand.CompiledXml))),
                operation,
                (ids, _, _) =>
                {
                    string id = Assert.Single(ids);
                    DocumentationQueryOutcome outcome =
                        new DocumentationQueryOutcome.RequestRejected(
                            new(
                                new(
                                    "Another.Assembly",
                                    "1.0.0.0",
                                    null,
                                    null),
                                id),
                            DocumentationQueryRequestRejectionReason
                                .LibraryReferenceMismatch);
                    IReadOnlyDictionary<
                        string,
                        DocumentationQueryOutcome> outcomes =
                            new Dictionary<
                                string,
                                DocumentationQueryOutcome>
                            {
                                [id] = outcome,
                            };
                    return ValueTask.FromResult(outcomes);
                },
                TestContext.Current.CancellationToken);

        Assert.Equal(
            MemberDocumentInspectionFailure
                .DocumentationSubjectMismatch,
            Assert.IsType<MemberDocumentInspectionOutcome.Failed>(
                    inspection.Content)
                .Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        MemberDocumentRejectsOutOfRangeAndAmbiguousSelectors()
    {
        byte[] content =
            BuildMethodGroupImage(
                "Selectors",
                new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                overloadCount: 20);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        MetadataTypeDefinitionName declaringType = Name("N", "C");
        MemberOverloadPopulationContent population =
            Available(
                Execute(
                    library,
                    "M",
                    count: false,
                    new(maximumRows: 20),
                    declaringType: declaringType));
        ImmutableArray<MemberOverloadShape> rows =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                    population.Overloads.Rows)
                .Items;
        string ambiguousPrefix =
            Assert.Single(
                    rows.GroupBy(
                            static row =>
                                row.Fingerprint.ToString()[..1],
                            StringComparer.Ordinal)
                        .Where(static group => group.Count() > 1)
                        .Take(1))
                .Key;

        var outOfRange =
            Assert.IsType<MemberDocumentInspectionOutcome.Rejected>(
                ExecuteMemberDocument(
                        library,
                        "M",
                        new(baselineOrdinal: 21),
                        declaringType)
                    .Content);
        var ambiguous =
            Assert.IsType<MemberDocumentInspectionOutcome.Rejected>(
                ExecuteMemberDocument(
                        library,
                        "M",
                        new(fingerprintPrefix: ambiguousPrefix),
                        declaringType)
                    .Content);

        Assert.Equal(
            MemberDocumentInspectionRejection.BaselineOrdinalOutOfRange,
            outOfRange.Reason);
        Assert.Equal(
            MemberDocumentInspectionRejection.FingerprintAmbiguous,
            ambiguous.Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        MemberDocumentOrdinalMaterializesOnlyThroughSelectedRow()
    {
        byte[] content =
            BuildMethodGroupImage(
                "Selectors",
                new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                overloadCount: 20);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        MetadataTypeDefinitionName declaringType = Name("N", "C");
        MemberOverloadShape first =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                    Available(
                            Execute(
                                library,
                                "M",
                                count: false,
                                new(maximumRows: 1),
                                declaringType: declaringType))
                        .Overloads.Rows)
                .Items[0];
        ApiSurfaceExtractionBounds oneRowBounds =
            Bounds(
                first.DisplaySignature.Length
                + first.CanonicalSignature.Length
                + first.Anchor.StableSelector.Length
                + first.Anchor.TypeFullName.Length
                + first.Anchor.MemberName.Length
                + first.Fingerprint.Length
                + first.Accessibility.Length);

        MemberDocument exact =
            Assert.IsType<MemberDocumentInspectionOutcome.Available>(
                    ExecuteMemberDocument(
                            library,
                            "M",
                            new(baselineOrdinal: 1),
                            declaringType,
                            oneRowBounds)
                        .Content)
                .Document;
        var fingerprint =
            Assert.IsType<MemberDocumentInspectionOutcome.Incomplete>(
                ExecuteMemberDocument(
                        library,
                        "M",
                        new(
                            fingerprintPrefix:
                                first.Fingerprint.ToString()),
                        declaringType,
                        oneRowBounds)
                    .Content);

        Assert.Equal(first.MetadataToken, exact.Subject.MetadataToken);
        Assert.Equal(
            MemberOverloadPopulationBound.RetainedTextCharacters,
            fingerprint.Bound);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        ContinuationRejectsDifferentAssemblyWithSameModuleVersionId()
    {
        Guid moduleVersionId =
            new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        byte[] firstContent =
            BuildMethodGroupImage(
                "First",
                moduleVersionId,
                overloadCount: 2);
        byte[] secondContent =
            BuildMethodGroupImage(
                "Second",
                moduleVersionId,
                overloadCount: 3);
        await using LibraryInspectionTestLibrary first =
            await LibraryInspectionTestLibrary.CreateAsync(
                firstContent,
                LibraryInspectionTestLibrary.Identity(firstContent));
        await using LibraryInspectionTestLibrary second =
            await LibraryInspectionTestLibrary.CreateAsync(
                secondContent,
                LibraryInspectionTestLibrary.Identity(secondContent));

        MemberOverloadPopulationContent firstPage =
            Available(
                Execute(
                    first,
                    "M",
                    count: false,
                    new(maximumRows: 1),
                    declaringType: Name("N", "C")));
        MemberOverloadContinuation continuation =
            Assert.IsType<MemberOverloadContinuation>(
                Assert.IsType<MemberOverloadRowsOutcome.Read>(
                        firstPage.Overloads.Rows)
                    .Continuation);

        MemberOverloadPopulationContent secondPage =
            Available(
                Execute(
                    second,
                    "M",
                    count: false,
                    new(
                        maximumRows: 1,
                        continuation:
                            continuation),
                    declaringType: Name("N", "C")));

        Assert.NotEqual(firstPage.Assembly, secondPage.Assembly);
        Assert.Equal(
            firstPage.Assembly,
            continuation.Binding.Assembly);
        Assert.Equal(
            secondPage.Assembly,
            secondPage.Overloads.Binding.Assembly);
        Assert.Equal(
            MemberOverloadRowsRejection.IncompatibleContinuation,
            Assert.IsType<MemberOverloadRowsOutcome.Rejected>(
                    secondPage.Overloads.Rows)
                .Reason);

        await first.RetireAsync();
        await second.RetireAsync();
    }

    [Fact]
    public async Task
        RealDeserialize_ReceiverClassificationIsExhaustive()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        MemberOverloadPopulationContent contentResult =
            Available(
                Execute(
                    library,
                    "Deserialize",
                    count: true,
                    new(maximumRows: 100)));
        var count =
            Assert.IsType<MemberOverloadCountOutcome.Counted>(
                contentResult.Overloads.Count);
        var rows =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                contentResult.Overloads.Rows);

        Assert.Equal(40, count.Value);
        Assert.True(rows.IsComplete);
        Assert.Equal(40, rows.Items.Length);
        Assert.Equal(
            25,
            rows.Items.Count(
                static row =>
                    row.Receiver
                        is MemberReceiver.Static));
        Assert.Equal(
            15,
            rows.Items.Count(
                static row =>
                    row.Receiver
                        is MemberReceiver.Extension));
        Assert.DoesNotContain(
            rows.Items,
            static row =>
                row.Receiver
                    is MemberReceiver.This);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RealDeserialize_ReceiverFiltersApplyToCountAndRows()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        foreach ((
            MemberOverloadReceiverFilter filter,
            int expectedCount) in new[]
            {
                (MemberOverloadReceiverFilter.All, 40),
                (MemberOverloadReceiverFilter.Static, 25),
                (MemberOverloadReceiverFilter.Extension, 15),
                (MemberOverloadReceiverFilter.This, 0),
            })
        {
            MemberOverloadPopulationContent counted =
                Available(
                    Execute(
                        library,
                        "Deserialize",
                        count: true,
                        rows: null,
                        receiver: filter));
            Assert.Equal(
                expectedCount,
                Assert.IsType<MemberOverloadCountOutcome.Counted>(
                        counted.Overloads.Count)
                    .Value);
            Assert.Null(counted.Overloads.Rows);
            Assert.Equal(
                filter,
                counted.Overloads.Binding.Receiver);

            MemberOverloadPopulationContent read =
                Available(
                    Execute(
                        library,
                        "Deserialize",
                        count: false,
                        new(maximumRows: 100),
                        receiver: filter));
            Assert.Null(read.Overloads.Count);
            MemberOverloadRowsOutcome.Read rows =
                Assert.IsType<MemberOverloadRowsOutcome.Read>(
                    read.Overloads.Rows);
            Assert.True(rows.IsComplete);
            Assert.Equal(expectedCount, rows.Items.Length);
            Assert.Equal(
                filter,
                read.Overloads.Binding.Receiver);
            if (filter is not MemberOverloadReceiverFilter.All)
            {
                MemberReceiver expectedReceiver = filter switch
                {
                    MemberOverloadReceiverFilter.Static =>
                        MemberReceiver.Static,
                    MemberOverloadReceiverFilter.This =>
                        MemberReceiver.This,
                    MemberOverloadReceiverFilter.Extension =>
                        MemberReceiver.Extension,
                    _ => throw new InvalidOperationException(),
                };
                Assert.All(
                    rows.Items,
                    row => Assert.Equal(
                        expectedReceiver,
                        row.Receiver));
            }
        }

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RealSystemTextJson_AccessibilityFiltersApplyToCountAndRows()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        (
            MetadataTypeDefinitionName DeclaringType,
            string MethodName,
            MemberOverloadAccessibilityFilter Accessibility,
            int ExpectedCount)[] cases =
        {
            (
                Name("System.Text.Json", "JsonDocument"),
                "Parse",
                MemberOverloadAccessibilityFilter.Public,
                5),
            (
                Name(
                    "System.Text.Json.Serialization.Metadata",
                    "JsonTypeInfo"),
                "CreateJsonPropertyInfo",
                MemberOverloadAccessibilityFilter.Protected,
                1),
            (
                Name("System.Text.Json", "JsonDocument"),
                "ParseValue",
                MemberOverloadAccessibilityFilter.Internal,
                5),
            (
                Name("System.Text.Json", "JsonDocument"),
                "Parse",
                MemberOverloadAccessibilityFilter.Private,
                2),
            (
                Name("System.Text.Json", "JsonDocument"),
                "Parse",
                MemberOverloadAccessibilityFilter.All,
                7),
        };
        foreach (var @case in cases)
        {
            MemberOverloadPopulationContent counted =
                Available(
                    Execute(
                        library,
                        @case.MethodName,
                        count: true,
                        rows: null,
                        accessibility: @case.Accessibility,
                        declaringType: @case.DeclaringType));
            Assert.Equal(
                @case.ExpectedCount,
                Assert.IsType<MemberOverloadCountOutcome.Counted>(
                        counted.Overloads.Count)
                    .Value);
            Assert.Null(counted.Overloads.Rows);
            Assert.Equal(
                @case.Accessibility,
                counted.Overloads.Binding.Accessibility);

            MemberOverloadPopulationContent read =
                Available(
                    Execute(
                        library,
                        @case.MethodName,
                        count: false,
                        new(maximumRows: 100),
                        accessibility: @case.Accessibility,
                        declaringType: @case.DeclaringType));
            Assert.Null(read.Overloads.Count);
            MemberOverloadRowsOutcome.Read rows =
                Assert.IsType<MemberOverloadRowsOutcome.Read>(
                    read.Overloads.Rows);
            Assert.True(rows.IsComplete);
            Assert.Equal(@case.ExpectedCount, rows.Items.Length);
            Assert.Equal(
                @case.Accessibility,
                read.Overloads.Binding.Accessibility);
        }

        await library.RetireAsync();
    }

    /// <summary>
    /// VB's <c>Collection.IListAdd</c> is a private body whose MethodImpl
    /// implements the referenced <c>IList.Add</c>. The shared member admission
    /// places it in the public bucket, so its overload population is public
    /// too, as the Type's Member inventory lists it.
    /// </summary>
    [Fact]
    public async Task
        RealVisualBasicCollection_InterfaceImplementationTakesInterfaceBucket()
    {
        byte[] content = await File.ReadAllBytesAsync(
            Path.Combine(
                Path.GetDirectoryName(typeof(object).Assembly.Location)!,
                "Microsoft.VisualBasic.Core.dll"),
            TestContext.Current.CancellationToken);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        MetadataTypeDefinitionName collection =
            Name("Microsoft.VisualBasic", "Collection");

        (MemberOverloadAccessibilityFilter Accessibility, int Expected)[]
            cases =
            [
                (MemberOverloadAccessibilityFilter.Public, 1),
                (MemberOverloadAccessibilityFilter.Private, 0),
                (MemberOverloadAccessibilityFilter.All, 1),
            ];
        foreach (var @case in cases)
        {
            MemberOverloadPopulationContent counted =
                Available(
                    Execute(
                        library,
                        "IListAdd",
                        count: true,
                        rows: null,
                        accessibility: @case.Accessibility,
                        declaringType: collection));
            Assert.Equal(
                @case.Expected,
                Assert.IsType<MemberOverloadCountOutcome.Counted>(
                        counted.Overloads.Count)
                    .Value);
        }

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        HiddenAdmissionAppliesToCountRowsAndContinuationBinding()
    {
        byte[] content =
            await File.ReadAllBytesAsync(
                FixtureCatalog.MetadataPublicMethodRoots.AssemblyPath(),
                TestContext.Current.CancellationToken);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        MetadataTypeDefinitionName declaringType =
            Name(
                "ILInspector.Metadata.PublicMethodRootFixtures",
                "PublicTopLevel");

        MemberOverloadPopulationContent visible =
            Available(
                Execute(
                    library,
                    "VisibilityOverload",
                    count: true,
                    new(maximumRows: 10),
                    declaringType: declaringType));
        Assert.Equal(
            1,
            Assert.IsType<MemberOverloadCountOutcome.Counted>(
                    visible.Overloads.Count)
                .Value);
        Assert.Single(
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                    visible.Overloads.Rows)
                .Items);
        Assert.False(visible.Overloads.Binding.IncludeHidden);

        MemberOverloadPopulationContent allAccessibility =
            Available(
                Execute(
                    library,
                    "VisibilityOverload",
                    count: true,
                    new(maximumRows: 10),
                    declaringType: declaringType,
                    accessibility:
                        MemberOverloadAccessibilityFilter.All));
        Assert.Equal(
            1,
            Assert.IsType<MemberOverloadCountOutcome.Counted>(
                    allAccessibility.Overloads.Count)
                .Value);
        Assert.Single(
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                    allAccessibility.Overloads.Rows)
                .Items);
        Assert.False(
            allAccessibility.Overloads.Binding.IncludeHidden);

        MemberOverloadPopulationContent complete =
            Available(
                Execute(
                    library,
                    "VisibilityOverload",
                    count: true,
                    new(maximumRows: 10),
                    declaringType: declaringType,
                    includeHidden: true));
        Assert.Equal(
            2,
            Assert.IsType<MemberOverloadCountOutcome.Counted>(
                    complete.Overloads.Count)
                .Value);
        Assert.Equal(
            2,
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                    complete.Overloads.Rows)
                .Items.Length);
        Assert.True(complete.Overloads.Binding.IncludeHidden);

        MemberOverloadContinuation continuation =
            Assert.IsType<MemberOverloadContinuation>(
                Assert.IsType<MemberOverloadRowsOutcome.Read>(
                        Available(
                                Execute(
                                    library,
                                    "VisibilityOverload",
                                    count: false,
                                    new(maximumRows: 1),
                                    declaringType: declaringType,
                                    includeHidden: true))
                            .Overloads.Rows)
                    .Continuation);
        MemberOverloadPopulationContent changedAdmission =
            Available(
                Execute(
                    library,
                    "VisibilityOverload",
                    count: false,
                    new(
                        maximumRows: 1,
                        continuation: continuation),
                    declaringType: declaringType));
        Assert.Equal(
            MemberOverloadRowsRejection.IncompatibleContinuation,
            Assert.IsType<MemberOverloadRowsOutcome.Rejected>(
                    changedAdmission.Overloads.Rows)
                .Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        CountDoesNotRequireExactMemberRowMaterialization()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var zeroTextBounds = new ApiSurfaceExtractionBounds(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 0);

        MemberOverloadPopulationContent result =
            Available(
                Execute(
                    library,
                    "Deserialize",
                    count: true,
                    rows: null,
                    zeroTextBounds));

        Assert.Equal(
            40,
            Assert.IsType<MemberOverloadCountOutcome.Counted>(
                    result.Overloads.Count)
                .Value);
        Assert.Null(result.Overloads.Rows);

        MemberOverloadPopulationContent mixed =
            Available(
                Execute(
                    library,
                    "Deserialize",
                    count: true,
                    new(maximumRows: 1),
                    zeroTextBounds));
        Assert.Equal(
            40,
            Assert.IsType<MemberOverloadCountOutcome.Counted>(
                    mixed.Overloads.Count)
                .Value);
        MemberOverloadRowsOutcome.Incomplete rows =
            Assert.IsType<MemberOverloadRowsOutcome.Incomplete>(
                mixed.Overloads.Rows);
        Assert.Equal(
            MemberOverloadPopulationBound
                .RetainedTextCharacters,
            rows.Bound);
        Assert.Equal(0, rows.Limit);
        Assert.True(rows.Measured > 0);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        CountDoesNotRetainUnrelatedTypeAccessorPopulation()
    {
        const int accessorCount = 4_096;
        const int iterations = 8;
        byte[] targetAccessorContent =
            BuildAccessorHeavyMethodGroupImage(
                accessorCount,
                accessorsOnTarget: true);
        byte[] neighboringAccessorContent =
            BuildAccessorHeavyMethodGroupImage(
                accessorCount,
                accessorsOnTarget: false);
        await using LibraryInspectionTestLibrary targetAccessors =
            await LibraryInspectionTestLibrary.CreateAsync(
                targetAccessorContent,
                LibraryInspectionTestLibrary.Identity(
                    targetAccessorContent));
        await using LibraryInspectionTestLibrary neighboringAccessors =
            await LibraryInspectionTestLibrary.CreateAsync(
                neighboringAccessorContent,
                LibraryInspectionTestLibrary.Identity(
                    neighboringAccessorContent));

        _ = CountAllocations(targetAccessors, iterations: 2);
        _ = CountAllocations(neighboringAccessors, iterations: 2);

        long neighboringAllocation =
            CountAllocations(neighboringAccessors, iterations);
        long targetAllocation =
            CountAllocations(targetAccessors, iterations);

        Assert.True(
            targetAllocation
                <= neighboringAllocation + 512 * 1024,
            $"Count retained the unrelated Type accessor population: "
                + $"target={targetAllocation:N0} bytes, "
                + $"neighbor={neighboringAllocation:N0} bytes.");

        await targetAccessors.RetireAsync();
        await neighboringAccessors.RetireAsync();
    }

    [Fact]
    public async Task
        BoundedRowsRetainOnlyTheRequestedSegment()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        MemberOverloadShape probe =
            Assert.Single(
                Assert.IsType<MemberOverloadRowsOutcome.Read>(
                        Available(
                                Execute(
                                    library,
                                    "Serialize",
                                    count: false,
                                    new(maximumRows: 1)))
                            .Overloads.Rows)
                    .Items);
        int oneRowCharacters =
            probe.DisplaySignature.Length
            + probe.CanonicalSignature.Length
            + probe.Anchor.StableSelector.Length
            + probe.Anchor.TypeFullName.Length
            + probe.Anchor.MemberName.Length
            + probe.Fingerprint.Length
            + probe.Accessibility.Length;
        ApiSurfaceExtractionBounds oneRowBounds =
            Bounds(oneRowCharacters);

        MemberOverloadRowsOutcome.Read bounded =
            Assert.IsType<MemberOverloadRowsOutcome.Read>(
                Available(
                        Execute(
                            library,
                            "Serialize",
                            count: false,
                            new(maximumRows: 1),
                            oneRowBounds))
                    .Overloads.Rows);
        Assert.Single(bounded.Items);
        Assert.NotNull(bounded.Continuation);

        MemberOverloadRowsOutcome.Incomplete exhaustive =
            Assert.IsType<MemberOverloadRowsOutcome.Incomplete>(
                Available(
                        Execute(
                            library,
                            "Serialize",
                            count: false,
                            new(maximumRows: 15),
                            oneRowBounds))
                    .Overloads.Rows);
        Assert.Equal(
            MemberOverloadPopulationBound
                .RetainedTextCharacters,
            exhaustive.Bound);
        Assert.Equal(oneRowCharacters, exhaustive.Limit);
        Assert.True(exhaustive.Measured > exhaustive.Limit);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        MissingTypeAndMemberGroupAreTypedNonSuccess()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        Assert.Equal(
            MemberOverloadPopulationInspectionRejection
                .MemberGroupNotFound,
            Assert.IsType<
                    MemberOverloadPopulationInspectionOutcome.Rejected>(
                    Execute(
                        library,
                        "NotAMember",
                        count: true,
                        rows: null)
                        .Content)
                .Reason);
        Assert.Equal(
            MemberOverloadPopulationInspectionRejection.TypeNotFound,
            Assert.IsType<
                    MemberOverloadPopulationInspectionOutcome.Rejected>(
                    Execute(
                        library,
                        "Serialize",
                        count: true,
                        rows: null,
                        declaringType:
                            Name(
                                "System.Text.Json",
                                "NotAType"))
                        .Content)
                .Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        MalformedSelectedRowPreservesIndependentCount()
    {
        byte[] content =
            BuildMethodGroupImage(
                "M",
                DeepMethodSignature());
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        MemberOverloadPopulationContent result =
            Available(
                Execute(
                    library,
                    "M",
                    count: true,
                    new(maximumRows: 1),
                    declaringType: Name("N", "C")));

        Assert.Equal(
            1,
            Assert.IsType<MemberOverloadCountOutcome.Counted>(
                    result.Overloads.Count)
                .Value);
        Assert.Equal(
            MemberOverloadRowsFailure.MalformedMetadata,
            Assert.IsType<MemberOverloadRowsOutcome.Failed>(
                    result.Overloads.Rows)
                .Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        MalformedSelectedMethodAttributePreservesIndependentCount()
    {
        byte[] content =
            BuildMalformedExtensionAttributeImage();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        MemberOverloadPopulationContent result =
            Available(
                Execute(
                    library,
                    "M",
                    count: true,
                    new(maximumRows: 1),
                    declaringType: Name("N", "C"),
                    includeHidden: true));

        Assert.Equal(
            1,
            Assert.IsType<MemberOverloadCountOutcome.Counted>(
                    result.Overloads.Count)
                .Value);
        Assert.Equal(
            MemberOverloadRowsFailure.MalformedMetadata,
            Assert.IsType<MemberOverloadRowsOutcome.Failed>(
                    result.Overloads.Rows)
                .Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        MalformedExtensionContainerAttributePreservesIndependentCount()
    {
        byte[] content =
            BuildMalformedExtensionAttributeImage(
                malformedContainer: true);
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        MemberOverloadPopulationContent result =
            Available(
                Execute(
                    library,
                    "M",
                    count: true,
                    new(maximumRows: 1),
                    declaringType: Name("N", "C")));

        Assert.Equal(
            1,
            Assert.IsType<MemberOverloadCountOutcome.Counted>(
                    result.Overloads.Count)
                .Value);
        Assert.Equal(
            MemberOverloadRowsFailure.MalformedMetadata,
            Assert.IsType<MemberOverloadRowsOutcome.Failed>(
                    result.Overloads.Rows)
                .Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        ReceiverFilteredCountRequiresValidExtensionEvidence()
    {
        byte[] content =
            BuildMalformedExtensionAttributeImage();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        Assert.Equal(
            MemberOverloadPopulationInspectionFailure.MalformedMetadata,
            Assert.IsType<
                    MemberOverloadPopulationInspectionOutcome.Failed>(
                    Execute(
                        library,
                        "M",
                        count: true,
                        rows: null,
                        declaringType: Name("N", "C"),
                        receiver:
                            MemberOverloadReceiverFilter.Extension)
                        .Content)
                .Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        DuplicateStandardAccessorRolesFailThePopulation()
    {
        byte[] content = BuildDuplicateGetterImage();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        Assert.Equal(
            MemberOverloadPopulationInspectionFailure.MalformedMetadata,
            Assert.IsType<
                    MemberOverloadPopulationInspectionOutcome.Failed>(
                    Execute(
                        library,
                        "M",
                        count: true,
                        new(maximumRows: 2),
                        declaringType: Name("N", "C"))
                        .Content)
                .Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        CrossTypeAccessorAssociationFailsThePopulation()
    {
        byte[] content = BuildCrossTypeGetterImage();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        Assert.Equal(
            MemberOverloadPopulationInspectionFailure.MalformedMetadata,
            Assert.IsType<
                    MemberOverloadPopulationInspectionOutcome.Failed>(
                    Execute(
                        library,
                        "M",
                        count: true,
                        new(maximumRows: 1),
                        declaringType: Name("N", "C"))
                        .Content)
                .Reason);

        await library.RetireAsync();
    }

    [Theory]
    [InlineData(".ctor")]
    [InlineData(".cctor")]
    [InlineData("op_Addition")]
    [InlineData("IFoo.M")]
    public async Task
        OrdinaryMethodCategoryRejectsOtherMetadataCategories(
            string metadataName)
    {
        byte[] content =
            BuildMethodGroupImage(
                metadataName,
                VoidMethodSignature());
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        Assert.Equal(
            MemberOverloadPopulationInspectionRejection
                .MemberGroupNotFound,
            Assert.IsType<
                    MemberOverloadPopulationInspectionOutcome.Rejected>(
                    Execute(
                        library,
                        metadataName,
                        count: true,
                        rows: null,
                        declaringType: Name("N", "C"))
                        .Content)
                .Reason);

        await library.RetireAsync();
    }

    private static InspectionEnvelope<
        MemberOverloadPopulationInspectionOutcome> Execute(
            LibraryInspectionTestLibrary library,
            string methodName,
            bool count,
            MemberOverloadRowsRequest? rows,
            ApiSurfaceExtractionBounds? bounds = null,
            MetadataTypeDefinitionName? declaringType = null,
            MemberOverloadAccessibilityFilter accessibility =
                MemberOverloadAccessibilityFilter.Public,
            MemberOverloadReceiverFilter receiver =
                MemberOverloadReceiverFilter.All,
            bool includeHidden = false) =>
        MemberOverloadPopulationInspectionOperation.Execute(
            new(
                library.Reference,
                new(
                    new(
                        declaringType
                            ?? Name(
                                "System.Text.Json",
                                "JsonSerializer"),
                        methodName),
                    new(
                        count
                            ? new MemberOverloadCountRequest()
                            : null,
                        rows,
                        accessibility,
                        receiver,
                        includeHidden),
                    bounds ?? s_bounds)),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static InspectionEnvelope<
        MemberGroupDocumentInspectionOutcome> ExecuteDocument(
            LibraryInspectionTestLibrary library,
            string methodName) =>
        MemberGroupDocumentInspectionOperation.Execute(
            new(
                library.Reference,
                new(
                    new(
                        Name(
                            "System.Text.Json",
                            "JsonSerializer"),
                        methodName),
                    new(
                        new MemberOverloadCountRequest(),
                        new(maximumRows: s_bounds.MaxMembers),
                        MemberOverloadAccessibilityFilter.Public,
                        MemberOverloadReceiverFilter.All,
                        includeHidden: false),
                    s_bounds)),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static InspectionEnvelope<
        MemberDocumentInspectionOutcome> ExecuteMemberDocument(
            LibraryInspectionTestLibrary library,
            string methodName,
            MemberDocumentSelector selector,
            MetadataTypeDefinitionName? declaringType = null,
            ApiSurfaceExtractionBounds? bounds = null) =>
        MemberDocumentInspectionOperation.Execute(
            new(
                library.Reference,
                new(
                    new(
                        declaringType
                            ?? Name(
                                "System.Text.Json",
                                "JsonSerializer"),
                        methodName),
                    selector,
                    bounds ?? s_bounds)),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static MemberOverloadPopulationContent Available(
        InspectionEnvelope<
            MemberOverloadPopulationInspectionOutcome> envelope) =>
        Assert.IsType<
                MemberOverloadPopulationInspectionOutcome.Available>(
                envelope.Content)
            .Content;

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;

    private static CompiledDocumentationSubject DocumentationSubject(
        MemberOverloadPopulationBinding binding,
        string documentationId) =>
        new(
            new(
                binding.Assembly.Name.ToString(),
                binding.Assembly.Version.ToString(),
                binding.Assembly.Culture?.ToString(),
                binding.Assembly.PublicKeyToken?.ToString()),
            documentationId);

    private static AssemblyContextSubject SourceSubject(
        MemberOverloadPopulationBinding binding) =>
        new(
            ResolvedAssemblyReference.Create(
                new(
                    binding.Assembly.Name.ToString(),
                    binding.Assembly.Version,
                    binding.Assembly.Culture?.ToString(),
                    binding.Assembly.PublicKeyToken?.ToString()),
                path: null,
                static () => new MemoryStream(),
                AssemblyResolutionProvenance.Local(
                    "Member source attachment test")));

    private static ApiSurfaceExtractionBounds Bounds(
        int maximumRetainedTextCharacters) =>
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters:
                maximumRetainedTextCharacters);

    private static long PlanningAllocations(
        MemberGroupSubject subject,
        MemberOverloadPopulationRequest request,
        int iterations)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < iterations; index++)
        {
            GC.KeepAlive(
                new MemberOverloadPopulationInspectionPlan(
                    subject,
                    request,
                    s_bounds));
        }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static byte[] BuildMethodGroupImage(
        string methodName,
        BlobBuilder signature)
    {
        var metadata = new MetadataBuilder();
        AddAssembly(metadata);
        AddModuleType(metadata);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(methodName),
            metadata.GetOrAddBlob(signature),
            bodyOffset: -1,
            parameterList: MetadataTokens.ParameterHandle(1));

        return Serialize(metadata);
    }

    private static byte[] BuildMethodGroupImage(
        string assemblyName,
        Guid moduleVersionId,
        int overloadCount)
    {
        var metadata = new MetadataBuilder();
        AddAssembly(
            metadata,
            assemblyName,
            moduleVersionId);
        AddModuleType(metadata);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int parameterCount = 0;
            parameterCount < overloadCount;
            parameterCount++)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x00);
            signature.WriteByte((byte)parameterCount);
            signature.WriteByte(0x01);
            for (int parameter = 0;
                parameter < parameterCount;
                parameter++)
            {
                signature.WriteByte(0x08);
            }
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(signature),
                bodyOffset: -1,
                parameterList:
                    MetadataTokens.ParameterHandle(1));
        }

        return Serialize(metadata);
    }

    private static byte[] BuildMalformedExtensionAttributeImage(
        bool malformedContainer = false)
    {
        var metadata = new MetadataBuilder();
        AddAssembly(metadata);
        AssemblyReferenceHandle coreLibrary =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Private.CoreLib"),
                new Version(11, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle extensionAttribute =
            metadata.AddTypeReference(
                coreLibrary,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("ExtensionAttribute"));
        MemberReferenceHandle constructor =
            metadata.AddMemberReference(
                extensionAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x20, 0x00, 0x01 }));
        BlobHandle attributeValue =
            metadata.GetOrAddBlob(
                new byte[] { 0x01, 0x00, 0x00, 0x00 });
        AddModuleType(metadata);
        TypeDefinitionHandle type =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("C"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        MethodDefinitionHandle method =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(
                    VoidMethodSignature()),
                bodyOffset: -1,
                parameterList:
                    MetadataTokens.ParameterHandle(1));
        metadata.AddCustomAttribute(
            type,
            malformedContainer
                ? MetadataTokens.MemberReferenceHandle(2)
                : constructor,
            attributeValue);
        metadata.AddCustomAttribute(
            method,
            malformedContainer
                ? constructor
                : MetadataTokens.MemberReferenceHandle(2),
            attributeValue);
        return Serialize(metadata);
    }

    private static byte[] BuildAccessorHeavyMethodGroupImage(
        int accessorCount,
        bool accessorsOnTarget)
    {
        var metadata = new MetadataBuilder();
        AddAssembly(metadata);
        AddModuleType(metadata);
        int secondTypeFirstMethod =
            accessorsOnTarget
                ? accessorCount + 2
                : 2;
        TypeDefinitionHandle target =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("C"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle neighbor =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("D"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(
                    secondTypeFirstMethod));
        var getterSignature = new BlobBuilder();
        getterSignature.WriteByte(0x20);
        getterSignature.WriteByte(0x00);
        getterSignature.WriteByte(0x08);
        BlobHandle getterSignatureHandle =
            metadata.GetOrAddBlob(getterSignature);
        BlobHandle methodSignatureHandle =
            metadata.GetOrAddBlob(VoidMethodSignature());
        var getters =
            new MethodDefinitionHandle[accessorCount];
        if (accessorsOnTarget)
        {
            AddGetters(
                metadata,
                getters,
                getterSignatureHandle);
            AddMethod(
                metadata,
                "M",
                methodSignatureHandle);
        }
        else
        {
            AddMethod(
                metadata,
                "M",
                methodSignatureHandle);
            AddGetters(
                metadata,
                getters,
                getterSignatureHandle);
        }

        var propertySignature = new BlobBuilder();
        propertySignature.WriteByte(0x28);
        propertySignature.WriteByte(0x00);
        propertySignature.WriteByte(0x08);
        BlobHandle propertySignatureHandle =
            metadata.GetOrAddBlob(propertySignature);
        PropertyDefinitionHandle firstProperty = default;
        for (int index = 0; index < accessorCount; index++)
        {
            PropertyDefinitionHandle property =
                metadata.AddProperty(
                    PropertyAttributes.None,
                    metadata.GetOrAddString($"P{index}"),
                    propertySignatureHandle);
            if (index == 0)
                firstProperty = property;
            metadata.AddMethodSemantics(
                property,
                MethodSemanticsAttributes.Getter,
                getters[index]);
        }
        metadata.AddPropertyMap(
            accessorsOnTarget ? target : neighbor,
            firstProperty);
        return Serialize(metadata);
    }

    private static void AddGetters(
        MetadataBuilder metadata,
        MethodDefinitionHandle[] getters,
        BlobHandle signature)
    {
        for (int index = 0; index < getters.Length; index++)
        {
            getters[index] =
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.SpecialName
                        | MethodAttributes.HideBySig,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString($"get_P{index}"),
                    signature,
                    bodyOffset: -1,
                    parameterList:
                        MetadataTokens.ParameterHandle(1));
        }
    }

    private static void AddMethod(
        MetadataBuilder metadata,
        string name,
        BlobHandle signature) =>
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            signature,
            bodyOffset: -1,
            parameterList:
                MetadataTokens.ParameterHandle(1));

    private static long CountAllocations(
        LibraryInspectionTestLibrary library,
        int iterations)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        int total = 0;
        for (int index = 0; index < iterations; index++)
        {
            MemberOverloadPopulationContent result =
                Available(
                    Execute(
                        library,
                        "M",
                        count: true,
                        rows: null,
                        bounds: new(
                            maxTypes: 5_000,
                            maxMembers: 1,
                            maxInspectionFailures: 1_000,
                            maxTypeForwarders: 10_000,
                            maxMetadataRows: 100_000,
                            maxRetainedTextCharacters: 0),
                        declaringType: Name("N", "C")));
            total +=
                Assert.IsType<MemberOverloadCountOutcome.Counted>(
                        result.Overloads.Count)
                    .Value;
        }
        Assert.Equal(iterations, total);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static byte[] BuildDuplicateGetterImage()
    {
        var metadata = new MetadataBuilder();
        AddAssembly(metadata);
        AddModuleType(metadata);
        TypeDefinitionHandle type =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("C"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        MethodDefinitionHandle first =
            metadata.AddMethodDefinition(
                MethodAttributes.Public,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(
                    VoidMethodSignature()),
                bodyOffset: -1,
                parameterList:
                    MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle second =
            metadata.AddMethodDefinition(
                MethodAttributes.Public,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(
                    VoidMethodSignature()),
                bodyOffset: -1,
                parameterList:
                    MetadataTokens.ParameterHandle(1));
        PropertyDefinitionHandle property =
            metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("P"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x28, 0x00, 0x01 }));
        metadata.AddPropertyMap(type, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            first);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            second);
        return Serialize(metadata);
    }

    private static byte[] BuildCrossTypeGetterImage()
    {
        var metadata = new MetadataBuilder();
        AddAssembly(metadata);
        AddModuleType(metadata);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle propertyOwner =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("D"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        MethodDefinitionHandle method =
            metadata.AddMethodDefinition(
                MethodAttributes.Public,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(
                    VoidMethodSignature()),
                bodyOffset: -1,
                parameterList:
                    MetadataTokens.ParameterHandle(1));
        PropertyDefinitionHandle property =
            metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString("P"),
                metadata.GetOrAddBlob(
                    new byte[] { 0x28, 0x00, 0x01 }));
        metadata.AddPropertyMap(propertyOwner, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            method);
        return Serialize(metadata);
    }

    private static void AddAssembly(
        MetadataBuilder metadata,
        string assemblyName = "Synthetic",
        Guid? moduleVersionId = null)
    {
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(
                moduleVersionId ?? Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
    }

    private static void AddModuleType(MetadataBuilder metadata) =>
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

    private static byte[] Serialize(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    private static BlobBuilder VoidMethodSignature()
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteByte(0x00);
        signature.WriteByte(0x01);
        return signature;
    }

    private static BlobBuilder DeepMethodSignature()
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteByte(0x00);
        for (int index = 0; index < 100_000; index++)
            signature.WriteByte(0x1d);
        signature.WriteByte(0x08);
        return signature;
    }
}
