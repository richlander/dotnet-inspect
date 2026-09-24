using ILInspector.Metadata;

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

        await library.RetireAsync();
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

    private static InspectionEnvelope<
        MemberOverloadPopulationInspectionOutcome> Execute(
            LibraryInspectionTestLibrary library,
            string methodName,
            bool count,
            MemberOverloadRowsRequest? rows,
            ApiSurfaceExtractionBounds? bounds = null,
            MetadataTypeDefinitionName? declaringType = null) =>
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
                        rows),
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
}
