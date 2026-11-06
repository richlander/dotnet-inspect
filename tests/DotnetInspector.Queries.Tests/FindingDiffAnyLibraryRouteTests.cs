using System.Reflection.PortableExecutable;
using System.Text.Json;

using DotnetInspector.Diff;
using DotnetInspector.Fixtures;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Finding Diff Any's Library level as a registered route with typed Table
/// and Document outputs (finding-diff-any.md#adoption, step 9).
/// </summary>
public sealed class FindingDiffAnyLibraryRouteTests
{
    [Fact]
    public void ProductModule_RegistersOneDocumentAndInspectionRouteRequiringBothHosts()
    {
        InspectionCapabilityCatalog catalog = InspectionCapabilityCatalog.Create(
            [FindingDiffAnyLibraryCapability.ProductModule]);

        Assert.Same(FindingDiffAnyLibraryCapability.Document, Assert.Single(catalog.Documents));
        InspectionRouteRegistration route = Assert.Single(catalog.Routes);
        Assert.Same(FindingDiffAnyLibraryCapability.Route, route);
        Assert.Same(FindingDiffAnyLibraryCapability.Document, route.Document);
        Assert.Equal(InspectionRouteExecution.Inspection, route.Execution);
        Assert.Null(route.AnalysisOperation);
        Assert.Equal(
            FindingDiffAnyLibraryInspection.ResultContractIdentity,
            route.Document.Descriptor.ResultContract);
        // The product module alone has no consumer: both hosts are adoption gaps.
        Assert.Equal(
            [InspectionConsumerKind.Cli, InspectionConsumerKind.Browser],
            catalog.AdoptionGaps.Select(static gap => gap.ConsumerKind));
        Assert.Equal(
            ["finding-diff-any", "finding-diff-any/routes/library"],
            FindingDiffAnyLibraryCapabilityResourcePaths.Create(catalog)
                .Select(static registration => registration.Path.Value));
    }

    [Fact]
    public async Task Route_ReportsExactlyTheChangedAndIndeterminateTypesOfTheProducer()
    {
        FindingDiffAnyResult expected = CompareDirectly(FindingDiffAnyAxes.ApiAndBody);

        InspectionEnvelope<FindingDiffAnyLibraryDocument> envelope =
            await ExecuteAsync(FindingDiffAnyAxes.ApiAndBody);

        FindingDiffAnyLibraryDocument document = envelope.Content;
        Assert.Equal(FindingDiffAnyLibraryOutcomeKind.Compared, document.Outcome);
        Assert.Empty(envelope.Diagnostics);
        Assert.Equal(
            "comparison/endpoints",
            Assert.IsType<InspectionShare.NonProjectable>(envelope.Share).Path);
        Assert.Equal(
            new FindingDiffAnyLibraryComparison("Fixture", "1.0", "2.0", FindingDiffAnyAxes.ApiAndBody),
            document.Comparison);
        Assert.Equal(
            expected.Types
                .Where(static type =>
                    type.Api is FindingDiffAnyState.Changed or FindingDiffAnyState.Indeterminate
                    || type.Body is FindingDiffAnyState.Changed or FindingDiffAnyState.Indeterminate)
                .Select(static type => new FindingDiffAnyLibraryRow(
                    type.FullName, type.Api, type.Body, type.Identifier)),
            document.Rows);

        Assert.Contains(
            new FindingDiffAnyLibraryRow(
                "FindingDiffAnyFixture.BodyOnly",
                FindingDiffAnyState.Unchanged,
                FindingDiffAnyState.Changed,
                "FindingDiffAnyFixture.BodyOnly"),
            document.Rows);
        Assert.Contains(
            new FindingDiffAnyLibraryRow(
                "FindingDiffAnyFixture.Outer.Inner",
                FindingDiffAnyState.Unchanged,
                FindingDiffAnyState.Changed,
                "FindingDiffAnyFixture.Outer+Inner"),
            document.Rows);
        Assert.Contains(
            new FindingDiffAnyLibraryRow(
                "FindingDiffAnyFixture.ParameterName",
                FindingDiffAnyState.Changed,
                FindingDiffAnyState.Unchanged,
                "FindingDiffAnyFixture.ParameterName"),
            document.Rows);
        Assert.DoesNotContain(document.Rows, static row =>
            row.Type is "FindingDiffAnyFixture.Unchanged"
                or "FindingDiffAnyFixture.ConstantValue"
                or "FindingDiffAnyFixture.Outer");

        FindingDiffAnyLibraryCounts counts = Assert.IsType<FindingDiffAnyLibraryCounts>(document.Counts);
        Assert.Equal(document.Rows.Length, counts.ReportedTypes);
        Assert.Equal(
            document.Rows.Count(static row => row.Api == FindingDiffAnyState.Changed),
            counts.ApiChanged);
        Assert.Equal(
            document.Rows.Count(static row => row.Body == FindingDiffAnyState.Changed),
            counts.BodyChanged);
        Assert.Equal(0, counts.ApiIndeterminate);
        Assert.Equal(0, counts.BodyIndeterminate);

        FindingDiffAnyLibraryReceipt receipt = Assert.IsType<FindingDiffAnyLibraryReceipt>(document.Receipt);
        Assert.Equal(expected.Types.Length, receipt.ComparedTypes);
        Assert.True(receipt.ComparedTypes > counts.ReportedTypes);
        Assert.Equal(expected.Receipt.BodiesCompared, receipt.BodiesCompared);
        Assert.Equal(expected.Receipt.IlBytesCompared, receipt.IlBytesCompared);
        Assert.Equal(expected.Receipt.ApiSurfacesBuilt, receipt.ApiSurfacesBuilt);
    }

    [Fact]
    public async Task Table_IsTheDocumentRowsWithItsShareAndDiagnostics()
    {
        InspectionEnvelope<FindingDiffAnyLibraryDocument> document =
            await ExecuteAsync(FindingDiffAnyAxes.ApiAndBody);

        InspectionEnvelope<FindingDiffAnyLibraryTable> table =
            FindingDiffAnyLibraryInspection.Table(document);

        Assert.Equal(document.Content.Rows, table.Content.Rows);
        Assert.Same(document.Share, table.Share);
        Assert.Equal(document.Diagnostics, table.Diagnostics);
    }

    [Fact]
    public async Task ApiAxis_LeavesEveryBodyNotCompared()
    {
        FindingDiffAnyLibraryDocument document =
            (await ExecuteAsync(FindingDiffAnyAxes.Api)).Content;

        Assert.Equal(FindingDiffAnyAxes.Api, document.Comparison.Axes);
        Assert.NotEmpty(document.Rows);
        Assert.All(document.Rows, static row =>
        {
            Assert.Equal(FindingDiffAnyState.Changed, row.Api);
            Assert.Equal(FindingDiffAnyState.NotCompared, row.Body);
        });
        Assert.Equal(0, document.Receipt!.BodiesCompared);
        Assert.Equal(0, document.Counts!.BodyChanged);
    }

    [Fact]
    public void Project_OfASlicedComparisonEqualsTheRouteDocument()
    {
        // A host that slices the comparison, such as the Browser Worker,
        // projects the completed result through the same document.
        FindingDiffAnyResult result = CompareDirectly(FindingDiffAnyAxes.ApiAndBody);
        var comparison = new FindingDiffAnyLibraryComparison(
            "Fixture", "1.0", "2.0", FindingDiffAnyAxes.ApiAndBody);

        FindingDiffAnyLibraryDocument projected =
            FindingDiffAnyLibraryInspection.Project(comparison, result).Content;

        Assert.Equal(
            result.Types.Where(FindingDiffAnyLibraryInspection.IsReported)
                .Select(static type => type.Identifier),
            projected.Rows.Select(static row => row.Identifier));
        Assert.Equal(result.Types.Length, projected.Receipt!.ComparedTypes);
    }

    [Fact]
    public async Task Document_SerializesStatesAndAxesAsStrings()
    {
        FindingDiffAnyLibraryDocument document =
            (await ExecuteAsync(FindingDiffAnyAxes.ApiAndBody)).Content;

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(
            document,
            FindingDiffAnyLibraryJsonContext.Default.FindingDiffAnyLibraryDocument));

        JsonElement root = json.RootElement;
        Assert.Equal("Compared", root.GetProperty("outcome").GetString());
        Assert.Equal("ApiAndBody", root.GetProperty("comparison").GetProperty("axes").GetString());
        JsonElement row = root.GetProperty("rows").EnumerateArray().First(static row =>
            row.GetProperty("type").GetString() == "FindingDiffAnyFixture.BodyOnly");
        Assert.Equal("Unchanged", row.GetProperty("api").GetString());
        Assert.Equal("Changed", row.GetProperty("body").GetString());
        Assert.Equal(
            document.Receipt!.ComparedTypes,
            root.GetProperty("receipt").GetProperty("comparedTypes").GetInt32());
        Assert.False(root.TryGetProperty("detail", out _));
    }

    static FindingDiffAnyResult CompareDirectly(FindingDiffAnyAxes axes)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
        return FindingDiffAny.Compare(before, after, axes);
    }

    static async Task<InspectionEnvelope<FindingDiffAnyLibraryDocument>> ExecuteAsync(
        FindingDiffAnyAxes axes)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        await using var workspace = new InspectionWorkspace();
        AssemblyContextParticipant before = Participant(pair.OldAssemblyPath(), "Before");
        AssemblyContextParticipant after = Participant(pair.NewAssemblyPath(), "After");
        using AssemblyContextGroup beforeGroup = workspace.CreateAssemblyContextGroup([before]);
        using AssemblyContextGroup afterGroup = workspace.CreateAssemblyContextGroup([after]);
        return await FindingDiffAnyLibraryCapability.Route.ExecuteAsync(
            new FindingDiffAnyLibraryRequest(
                "Fixture",
                "1.0",
                "2.0",
                new DiffLibraryPair(beforeGroup, before, afterGroup, after),
                axes));
    }

    static AssemblyContextParticipant Participant(string path, string label)
        => new(
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(label)),
            new NoDependencyPolicy());

    // Finding Diff Any compares names symbolically and binds no dependency.
    sealed class NoDependencyPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
            => new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}
