using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;

namespace DotnetInspector.Sections.Tests;

public sealed class TypeMemberGroupPopulationInspectionOperationTests
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
    public void QuerySpace_RegistersDeclaredMemberGroupsOnce()
    {
        IQueryOperationRoute route =
            TypeMemberGroupPopulationQuery.OperationRoute;
        QuerySpaceDescriptor descriptor =
            TypeMemberGroupPopulationQuery.QuerySpace.Descriptor;

        Assert.Equal(
            TypeMemberGroupPopulationQuery.OperationIdentity,
            route.OperationIdentity);
        Assert.Equal(
            TypeMemberGroupPopulationQuery.OperationRouteIdentity,
            route.Identity);
        Assert.Equal(
            TypeMemberGroupPopulationQuery.SubjectRole,
            route.SubjectRole);
        Assert.Equal(
            TypeMemberGroupPopulationQuery.ResultGrain,
            route.ResultGrain);
        Assert.Equal(
            [TypeMemberGroupPopulationQuery.RowSet],
            route.RowSets);
        Assert.Equal(
            [
                TypeMemberGroupPopulationQuery.SpellingTermKey,
                TypeMemberGroupPopulationQuery.AccessibilityTermKey,
                TypeMemberGroupPopulationQuery.ReceiverTermKey,
                TypeMemberGroupPopulationQuery.IncludeHiddenTermKey,
            ],
            route.Capabilities.Terms.Select(term =>
                term.Binding.Key));
        Assert.Empty(route.Capabilities.Orders);
        Assert.Empty(route.Capabilities.Dimensions);
        Assert.Empty(route.Capabilities.Stages);

        Assert.Same(
            TypeMemberGroupPopulationQuery.OperationRoute,
            TypeMemberGroupPopulationQuery.QuerySpace.Operation);
        Assert.Equal(
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            descriptor.Terminals);
        Assert.True(descriptor.AcceptsContinuation);
        Assert.Equal(
            [
                TypeMemberGroupPopulationQuery.RowsResultContract,
                TypeMemberGroupPopulationQuery.CountResultContract,
            ],
            descriptor.ResultContracts.Select(contract =>
                contract.Identity));
        Assert.Equal(
            TypeMemberGroupPopulationQuery.RowScopeIdentity,
            Assert.Single(descriptor.RowScopes).Identity);
    }

    [Theory]
    [InlineData(
        TypeMemberGroupSpelling.CSharp,
        TypeMemberGroupAccessibilityFilter.Public,
        TypeMemberGroupReceiverFilter.All,
        false,
        QuerySpaceTerminalRequirement.Count,
        0)]
    [InlineData(
        TypeMemberGroupSpelling.Metadata,
        TypeMemberGroupAccessibilityFilter.All,
        TypeMemberGroupReceiverFilter.NonExtension,
        true,
        QuerySpaceTerminalRequirement.Rows,
        4)]
    public void QuerySpaceRequest_ResolvesOwnerIntentAndTerminal(
        TypeMemberGroupSpelling spelling,
        TypeMemberGroupAccessibilityFilter accessibility,
        TypeMemberGroupReceiverFilter receiver,
        bool includeHidden,
        QuerySpaceTerminalRequirement terminal,
        int requestTermCount)
    {
        QuerySpaceRequest request =
            TypeMemberGroupPopulationQuery.CreateRequest(
                spelling,
                accessibility,
                receiver,
                includeHidden,
                TypeMemberGroupOrdering.Metadata,
                terminal);

        TypeMemberGroupPopulationQueryRequestResult.Accepted accepted =
            Assert.IsType<
                    TypeMemberGroupPopulationQueryRequestResult.Accepted>(
                    TypeMemberGroupPopulationQuery.ResolveRequest(
                        request,
                        TestContext.Current.CancellationToken));
        TypeMemberGroupPopulationQueryPlan plan = accepted.Plan;

        Assert.Equal(spelling, plan.Spelling);
        Assert.Equal(accessibility, plan.Accessibility);
        Assert.Equal(receiver, plan.Receiver);
        Assert.Equal(includeHidden, plan.IncludeHidden);
        Assert.Equal(TypeMemberGroupOrdering.Metadata, plan.Ordering);
        Assert.Equal(terminal, accepted.Terminal);
        Assert.Equal(4, plan.Intent.Terms.Count);
        Assert.Empty(plan.Intent.Bounds);
        Assert.Empty(plan.Intent.Stages);
        Assert.Empty(plan.Intent.Order);
        Assert.Equal(requestTermCount, request.Operation.Terms.Count);
        Assert.Equal(
            TypeMemberGroupPopulationQuery.QuerySpaceIdentity,
            request.QuerySpace);
        Assert.Equal(
            [TypeMemberGroupPopulationQuery.RowSet],
            request.ParticipatingRowSets);
    }

    [Fact]
    public void QuerySpaceRequest_RejectsUnknownAndForeignIntent()
    {
        QuerySpaceRequest unknown =
            QuerySpaceRequest.Create(
                TypeMemberGroupPopulationQuery.QuerySpace.Descriptor,
                PortableQueryIntent.Create(
                    [
                        new(
                            TypeMemberGroupPopulationQuery
                                .AccessibilityTermKey,
                            PortableQueryOperator.Equal,
                            "family"),
                    ],
                    [],
                    [],
                    []),
                [TypeMemberGroupPopulationQuery.RowSet],
                [],
                QuerySpaceTerminalRequirement.Count);
        var rejectedIntent = Assert.IsType<
            TypeMemberGroupPopulationQueryRequestResult.IntentRejected>(
                TypeMemberGroupPopulationQuery.ResolveRequest(
                    unknown,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            PortableQueryFailureReason.ValueRejected,
            rejectedIntent.Failure.Reason);

        QuerySpaceRequest foreign =
            PackageFileInventoryQuery.CreateRequest(
                RowSelectionIntent<string>.Empty,
                QuerySpaceTerminalRequirement.Count);
        var rejectedQuery = Assert.IsType<
            TypeMemberGroupPopulationQueryRequestResult.Rejected>(
                TypeMemberGroupPopulationQuery.ResolveRequest(
                    foreign,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            TypeMemberGroupPopulationQueryRequestRejectionKind
                .QuerySpaceMismatch,
            rejectedQuery.Kind);
    }

    [Fact]
    public void PopulationRequest_RequiresOneParentTerminal()
    {
        Assert.Throws<ArgumentException>(
            () => new TypeMemberGroupPopulationRequest(
                new TypeMemberGroupCountRequest(),
                new TypeMemberGroupRowsRequest(maximumRows: 1)));

        Assert.Throws<ArgumentException>(
            () => new TypeMemberGroupPopulationRequest(
                count: null));
    }

    [Fact]
    public void ExactOverloadPlan_StillRejectsNonMethodGroups()
    {
        Assert.Throws<ArgumentException>(
            () => new MemberOverloadPopulationInspectionPlan(
                new(
                    Name("System.Text.Json", "JsonSerializer"),
                    "Options",
                    MemberGroupCategory.Property),
                new(
                    new MemberOverloadCountRequest()),
                s_bounds));
    }

    [Fact]
    public async Task
        RealJsonSerializer_CountAvoidsRowsAndRetainedText()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        var zeroText = new ApiSurfaceExtractionBounds(
            s_bounds.MaxTypes,
            s_bounds.MaxMembers,
            s_bounds.MaxInspectionFailures,
            s_bounds.MaxTypeForwarders,
            s_bounds.MaxMetadataRows,
            maxRetainedTextCharacters: 0);

        TypeMemberGroupPopulationContent result =
            Available(
                Execute(
                    library,
                    count: new(),
                    includeHidden: true,
                    bounds: zeroText));

        Assert.Equal(
            10,
            Assert.IsType<TypeMemberGroupCountOutcome.Counted>(
                    result.Members.Count)
                .Value);
        Assert.Null(result.Members.Rows);
        Assert.Null(result.Members.Composition);
        Assert.Null(result.Members.SelectorCounts);
        Assert.Equal("System.Text.Json", result.Assembly.Name.ToString());
        Assert.Equal(result.Assembly, result.Members.Binding.Assembly);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RealJsonSerializer_BoundedRowsResumeOneExactPopulation()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        TypeMemberGroupPopulationContent first =
            Available(
                Execute(
                    library,
                    rows: new(maximumRows: 4),
                    includeHidden: true));
        var segment =
            Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                first.Members.Rows);
        var rows = segment.Items.ToList();
        TypeMemberGroupContinuation? continuation =
            segment.Continuation;
        TypeMemberGroupContinuation firstContinuation =
            Assert.IsType<TypeMemberGroupContinuation>(
                continuation);
        while (continuation is not null)
        {
            TypeMemberGroupPopulationContent next =
                Available(
                    Execute(
                        library,
                        rows: new(
                            maximumRows: 3,
                            continuation: continuation),
                        includeHidden: true));
            Assert.Equal(
                first.Members.Binding,
                next.Members.Binding);
            Assert.Null(next.Members.Count);
            segment =
                Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                    next.Members.Rows);
            rows.AddRange(segment.Items);
            continuation = segment.Continuation;
        }

        Assert.Equal(10, rows.Count);
        Assert.Equal(
            Enumerable.Range(1, rows.Count),
            rows.Select(row => row.BaselineOrdinal));
        Assert.All(
            rows,
            row =>
            {
                Assert.Equal(
                    first.Members.Binding,
                    row.Binding.Population);
                Assert.Equal(
                    MemberGroupRole.Declared,
                    row.Binding.Role);
                Assert.True(row.ExactMemberCount > 0);
            });
        Assert.Equal(
            104,
            rows.Sum(row =>
                Assert.IsType<int>(row.ExactMemberCount)));
        Assert.Equal(
            103,
            rows
                .Where(row =>
                    row.Binding.Category
                        is MemberGroupCategory.Method)
                .Sum(row =>
                    Assert.IsType<int>(
                        row.ExactMemberCount)));
        Assert.Equal(
            9,
            rows.Count(row =>
                row.Binding.Category
                    is MemberGroupCategory.Method));
        Assert.Single(
            rows,
            row => row.Binding.Category
                is MemberGroupCategory.Property);

        TypeMemberGroupPopulationContent outOfRange =
            Available(
                Execute(
                    library,
                    rows: new(
                        maximumRows: 2,
                        continuation: new(
                            firstContinuation.Binding,
                            rows.Count,
                            includeExactMemberCount: true)),
                    includeHidden: true));
        Assert.Equal(
            TypeMemberGroupRowsRejection.ContinuationOutOfRange,
            Assert.IsType<TypeMemberGroupRowsOutcome.Rejected>(
                    outOfRange.Members.Rows)
                .Reason);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        ContinuationBindsAssemblyTypeTermsAndNestedCountDemand()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        TypeMemberGroupContinuation continuation =
            Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                    Available(
                            Execute(
                                library,
                                rows: new(maximumRows: 2)))
                        .Members.Rows)
                .Continuation!;

        AssertRejected(
            Execute(
                library,
                rows: new(
                    maximumRows: 2,
                    continuation: continuation),
                spelling: TypeMemberGroupSpelling.Metadata),
            TypeMemberGroupPopulationInspectionRejection
                .IncompatibleContinuation);
        AssertRejected(
            Execute(
                library,
                rows: new(
                    maximumRows: 2,
                    continuation: continuation),
                accessibility:
                    TypeMemberGroupAccessibilityFilter.All),
            TypeMemberGroupPopulationInspectionRejection
                .IncompatibleContinuation);
        AssertRejected(
            Execute(
                library,
                rows: new(
                    maximumRows: 2,
                    continuation: continuation),
                receiver: TypeMemberGroupReceiverFilter.Static),
            TypeMemberGroupPopulationInspectionRejection
                .IncompatibleContinuation);
        AssertRejected(
            Execute(
                library,
                rows: new(
                    maximumRows: 2,
                    continuation: continuation),
                includeHidden: true),
            TypeMemberGroupPopulationInspectionRejection
                .IncompatibleContinuation);
        AssertRejected(
            Execute(
                library,
                rows: new(
                    maximumRows: 2,
                    includeExactMemberCount: false,
                    continuation: continuation)),
            TypeMemberGroupPopulationInspectionRejection
                .IncompatibleContinuation);

        LibraryAssemblyIdentity otherAssembly =
            new(
                continuation.Binding.Assembly.Name,
                new Version(99, 0, 0, 0),
                continuation.Binding.Assembly.Culture,
                continuation.Binding.Assembly.PublicKeyToken);
        AssertRejected(
            Execute(
                library,
                rows: new(
                    maximumRows: 2,
                    continuation: Copy(
                        continuation,
                        assembly: otherAssembly))),
            TypeMemberGroupPopulationInspectionRejection
                .IncompatibleContinuation);
        AssertRejected(
            Execute(
                library,
                rows: new(
                    maximumRows: 2,
                    continuation: Copy(
                        continuation,
                        moduleVersionId: Guid.NewGuid()))),
            TypeMemberGroupPopulationInspectionRejection
                .StaleContinuation);
        AssertRejected(
            Execute(
                library,
                rows: new(
                    maximumRows: 2,
                    continuation: Copy(
                        continuation,
                        typeDefinitionToken:
                            checked(
                                continuation.Binding
                                    .TypeDefinitionToken
                                + 1)))),
            TypeMemberGroupPopulationInspectionRejection
                .StaleContinuation);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RealJsonDocument_CompositionRunsWithoutParentTerminal()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        TypeMemberGroupPopulationContent result =
            Available(
                Execute(
                    library,
                    type:
                        Name(
                            "System.Text.Json",
                            "JsonDocument"),
                    composition: new(),
                    accessibility:
                        TypeMemberGroupAccessibilityFilter.All,
                    includeHidden: true));
        TypeMemberCompositionCount composition =
            Assert.IsType<TypeMemberCompositionCount>(
                result.Members.Composition);

        Assert.Equal(
            (11, 0, 44, 27),
            (
                composition.Public,
                composition.Protected,
                composition.Internal,
                composition.Private));
        Assert.Equal(
            82,
            checked(
                composition.Public
                + composition.Protected
                + composition.Internal
                + composition.Private));
        Assert.Null(result.Members.Count);
        Assert.Null(result.Members.Rows);
        Assert.Null(result.Members.SelectorCounts);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RealJsonDocument_SelectorCountsRunWithoutGrouping()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        TypeMemberGroupPopulationContent result =
            Available(
                Execute(
                    library,
                    type:
                        Name(
                            "System.Text.Json",
                            "JsonDocument"),
                    selectorCounts: new(),
                    accessibility:
                        TypeMemberGroupAccessibilityFilter.All,
                    includeHidden: true));
        TypeMemberSelectorCounts selectors =
            Assert.IsType<TypeMemberSelectorCounts>(
                result.Members.SelectorCounts);

        Assert.Equal(82, selectors.Traits.All);
        Assert.Equal(
            selectors.Traits.All,
            selectors.Kinds.Sum(count => count.Count));
        Assert.Contains(
            selectors.Kinds,
            count => count.Kind is MemberGroupCategory.Method);
        Assert.Contains(
            selectors.Kinds,
            count => count.Kind is MemberGroupCategory.Property);
        Assert.Contains(
            selectors.Kinds,
            count => count.Kind is MemberGroupCategory.Field);
        Assert.Null(result.Members.Count);
        Assert.Null(result.Members.Rows);
        Assert.Null(result.Members.Composition);

        await library.RetireAsync();
    }

    [Fact]
    public async Task
        RealDeserialize_ReceiverFiltersPreserveGroupIdentity()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        TypeMemberGroupShape declared =
            Assert.Single(
                Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                        Available(
                                Execute(
                                    library,
                                    rows: new(
                                        maximumRows:
                                            int.MaxValue),
                                    receiver:
                                        TypeMemberGroupReceiverFilter
                                            .Static))
                            .Members.Rows)
                    .Items,
                row => row.Binding.Name.ToString()
                    == "Deserialize");
        TypeMemberGroupShape extension =
            Assert.Single(
                Assert.IsType<TypeMemberGroupRowsOutcome.Read>(
                        Available(
                                Execute(
                                    library,
                                    rows: new(
                                        maximumRows:
                                            int.MaxValue),
                                    receiver:
                                        TypeMemberGroupReceiverFilter
                                            .Extension))
                            .Members.Rows)
                    .Items,
                row => row.Binding.Name.ToString()
                    == "Deserialize");

        Assert.Equal(25, declared.ExactMemberCount);
        Assert.Equal(
            MemberGroupReceiverForms.Static,
            declared.Receivers);
        Assert.Equal(15, extension.ExactMemberCount);
        Assert.Equal(
            MemberGroupReceiverForms.Extension,
            extension.Receivers);
        Assert.Equal(
            (
                declared.Binding.Name,
                declared.Binding.Category,
                declared.Binding.Role),
            (
                extension.Binding.Name,
                extension.Binding.Category,
                extension.Binding.Role));

        await library.RetireAsync();
    }

    [Fact]
    public async Task BoundsRemainTypedAndVisible()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        var memberBound = new ApiSurfaceExtractionBounds(
            s_bounds.MaxTypes,
            maxMembers: 1,
            s_bounds.MaxInspectionFailures,
            s_bounds.MaxTypeForwarders,
            s_bounds.MaxMetadataRows,
            s_bounds.MaxRetainedTextCharacters);
        var members = Assert.IsType<
            TypeMemberGroupPopulationInspectionOutcome.Incomplete>(
                Execute(
                    library,
                    count: new(),
                    bounds: memberBound).Content);
        Assert.Equal(
            TypeMemberGroupPopulationBound.Members,
            members.Bound);
        Assert.Equal(1, members.Limit);
        Assert.Equal(2, members.Measured);

        var metadataBound = new ApiSurfaceExtractionBounds(
            s_bounds.MaxTypes,
            s_bounds.MaxMembers,
            s_bounds.MaxInspectionFailures,
            s_bounds.MaxTypeForwarders,
            maxMetadataRows: 0,
            s_bounds.MaxRetainedTextCharacters);
        var metadata = Assert.IsType<
            TypeMemberGroupPopulationInspectionOutcome.Incomplete>(
                Execute(
                    library,
                    count: new(),
                    bounds: metadataBound).Content);
        Assert.Equal(
            TypeMemberGroupPopulationBound.MetadataRows,
            metadata.Bound);
        Assert.Equal(0, metadata.Limit);
        Assert.True(metadata.Measured > 0);

        var textBound = new ApiSurfaceExtractionBounds(
            s_bounds.MaxTypes,
            s_bounds.MaxMembers,
            s_bounds.MaxInspectionFailures,
            s_bounds.MaxTypeForwarders,
            s_bounds.MaxMetadataRows,
            maxRetainedTextCharacters: 0);
        TypeMemberGroupRowsOutcome.Incomplete text =
            Assert.IsType<TypeMemberGroupRowsOutcome.Incomplete>(
                Available(
                        Execute(
                            library,
                            rows: new(maximumRows: 1),
                            bounds: textBound))
                    .Members.Rows);
        Assert.Equal(
            TypeMemberGroupPopulationBound.RetainedTextCharacters,
            text.Bound);
        Assert.Equal(0, text.Limit);
        Assert.True(text.Measured > 0);

        await library.RetireAsync();
    }

    [Fact]
    public async Task LeaseAndAssemblyIdentityMismatchesAreTyped()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary first =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        await using LibraryInspectionTestLibrary second =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));

        AssertRejected(
            TypeMemberGroupPopulationInspectionOperation.Execute(
                InspectionRequest(
                    first.Reference,
                    count: new()),
                second.IssueOperation(),
                TestContext.Current.CancellationToken),
            TypeMemberGroupPopulationInspectionRejection
                .LeaseReferenceMismatch);

        var wrongIdentity = new ManagedMetadataIdentity.Assembly(
            new AssemblyReferenceIdentity(
                "System.Text.Json",
                new Version(99, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null));
        await using LibraryInspectionTestLibrary mismatched =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                wrongIdentity);
        AssertRejected(
            Execute(
                mismatched,
                count: new()),
            TypeMemberGroupPopulationInspectionRejection
                .AssemblyIdentityMismatch);

        await first.RetireAsync();
        await second.RetireAsync();
        await mismatched.RetireAsync();
    }

    [Fact]
    public async Task MissingAndAmbiguousTypesAreTyped()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.Identity(content));
        AssertRejected(
            Execute(
                library,
                type:
                    Name(
                        "System.Text.Json",
                        "Missing"),
                count: new()),
            TypeMemberGroupPopulationInspectionRejection.TypeNotFound);

        byte[] duplicate =
            LibraryInspectionTestLibrary.BuildMetadataImage(
                duplicatePublicType: true);
        await using LibraryInspectionTestLibrary ambiguous =
            await LibraryInspectionTestLibrary.CreateAsync(
                duplicate,
                LibraryInspectionTestLibrary.Identity(duplicate));
        AssertRejected(
            Execute(
                ambiguous,
                type: Name("N", "C"),
                count: new()),
            TypeMemberGroupPopulationInspectionRejection.TypeAmbiguous);

        await library.RetireAsync();
        await ambiguous.RetireAsync();
    }

    [Theory]
    [MemberData(nameof(UnsupportedImages))]
    public async Task UnsupportedContentReturnsTypedFailure(
        byte[] content,
        TypeMemberGroupPopulationInspectionFailure expected)
    {
        await using LibraryInspectionTestLibrary library =
            await LibraryInspectionTestLibrary.CreateAsync(
                content,
                LibraryInspectionTestLibrary.ProbeIdentity());

        TypeMemberGroupPopulationInspectionOutcome.Failed failed =
            Assert.IsType<
                TypeMemberGroupPopulationInspectionOutcome.Failed>(
                    Execute(
                        library,
                        type: Name("Probe", "Type"),
                        count: new()).Content);

        Assert.Equal(expected, failed.Reason);
        await library.RetireAsync();
    }

    public static TheoryData<
        byte[],
        TypeMemberGroupPopulationInspectionFailure> UnsupportedImages()
    {
        byte[] windowsMetadata =
            LibraryInspectionTestLibrary.BuildMetadataImage(
                metadataVersion:
                    "WindowsRuntime 1.4;CLR v4.0.30319");
        return new TheoryData<
            byte[],
            TypeMemberGroupPopulationInspectionFailure>
        {
            {
                [1, 2, 3],
                TypeMemberGroupPopulationInspectionFailure
                    .MalformedMetadata
            },
            {
                LibraryInspectionTestLibrary.BuildMetadataImage(
                    includeAssembly: false),
                TypeMemberGroupPopulationInspectionFailure
                    .ManagedModule
            },
            {
                windowsMetadata,
                TypeMemberGroupPopulationInspectionFailure
                    .UnsupportedWindowsMetadata
            },
            {
                LibraryInspectionTestLibrary.BuildMetadataImage(
                    emptyModuleVersionId: true),
                TypeMemberGroupPopulationInspectionFailure
                    .EmptyModuleVersionId
            },
        };
    }

    private static InspectionEnvelope<
        TypeMemberGroupPopulationInspectionOutcome> Execute(
            LibraryInspectionTestLibrary library,
            MetadataTypeDefinitionName? type = null,
            TypeMemberGroupCountRequest? count = null,
            TypeMemberGroupRowsRequest? rows = null,
            TypeMemberCompositionCountRequest? composition = null,
            TypeMemberSelectorCountsRequest? selectorCounts = null,
            TypeMemberGroupSpelling spelling =
                TypeMemberGroupSpelling.CSharp,
            TypeMemberGroupAccessibilityFilter accessibility =
                TypeMemberGroupAccessibilityFilter.Public,
            TypeMemberGroupReceiverFilter receiver =
                TypeMemberGroupReceiverFilter.All,
            bool includeHidden = false,
            ApiSurfaceExtractionBounds? bounds = null) =>
        TypeMemberGroupPopulationInspectionOperation.Execute(
            InspectionRequest(
                library.Reference,
                type,
                count,
                rows,
                composition,
                selectorCounts,
                spelling,
                accessibility,
                receiver,
                includeHidden,
                bounds),
            library.IssueOperation(),
            TestContext.Current.CancellationToken);

    private static TypeMemberGroupPopulationInspectionRequest
        InspectionRequest(
            LibraryReference library,
            MetadataTypeDefinitionName? type = null,
            TypeMemberGroupCountRequest? count = null,
            TypeMemberGroupRowsRequest? rows = null,
            TypeMemberCompositionCountRequest? composition = null,
            TypeMemberSelectorCountsRequest? selectorCounts = null,
            TypeMemberGroupSpelling spelling =
                TypeMemberGroupSpelling.CSharp,
            TypeMemberGroupAccessibilityFilter accessibility =
                TypeMemberGroupAccessibilityFilter.Public,
            TypeMemberGroupReceiverFilter receiver =
                TypeMemberGroupReceiverFilter.All,
            bool includeHidden = false,
            ApiSurfaceExtractionBounds? bounds = null) =>
        new(
            library,
            new(
                type
                    ?? Name(
                        "System.Text.Json",
                        "JsonSerializer"),
                new(
                    count,
                    rows,
                    composition,
                    selectorCounts,
                    spelling,
                    accessibility,
                    receiver,
                    includeHidden),
                bounds ?? s_bounds));

    private static TypeMemberGroupPopulationContent Available(
        InspectionEnvelope<
            TypeMemberGroupPopulationInspectionOutcome> envelope) =>
        Assert.IsType<
                TypeMemberGroupPopulationInspectionOutcome.Available>(
                envelope.Content)
            .Content;

    private static void AssertRejected(
        InspectionEnvelope<
            TypeMemberGroupPopulationInspectionOutcome> envelope,
        TypeMemberGroupPopulationInspectionRejection expected) =>
        Assert.Equal(
            expected,
            Assert.IsType<
                    TypeMemberGroupPopulationInspectionOutcome.Rejected>(
                    envelope.Content)
                .Reason);

    private static TypeMemberGroupContinuation Copy(
        TypeMemberGroupContinuation continuation,
        LibraryAssemblyIdentity? assembly = null,
        Guid? moduleVersionId = null,
        int? typeDefinitionToken = null) =>
        new(
            new(
                assembly ?? continuation.Binding.Assembly,
                moduleVersionId
                    ?? continuation.Binding.ModuleVersionId,
                continuation.Binding.Type,
                typeDefinitionToken
                    ?? continuation.Binding.TypeDefinitionToken,
                continuation.Binding.Spelling,
                continuation.Binding.IncludeHidden,
                continuation.Binding.Accessibility,
                continuation.Binding.Receiver,
                continuation.Binding.Ordering),
            continuation.NextOrdinal,
            continuation.IncludeExactMemberCount);

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;

}
