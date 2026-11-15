using System.Reflection.PortableExecutable;
using System.Text.Json;

using DotnetInspector.Diff;
using DotnetInspector.Fixtures;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Finding Diff Any's Type level as a registered route with typed Table and
/// Document outputs (finding-diff-any.md#type, diff-capabilities.md ladder
/// step 6.3).
/// </summary>
public sealed class FindingDiffAnyTypeRouteTests
{
    [Fact]
    public async Task EveryFixtureType_RouteStatesAgreeWithTheLibraryLevel()
    {
        // The coverage relationship between rungs: a Type's T1 states are its
        // L1 states, and it has reported rows exactly when L1 reports it.
        FindingDiffAnyResult library = CompareLibrary();
        FindingDiffAnyTypeState[] addressable =
            [.. library.Types.Where(static type => !type.Identifier.StartsWith('!'))];
        Assert.True(addressable.Length > 10);
        foreach (FindingDiffAnyTypeState type in addressable)
        {
            InspectionEnvelope<FindingDiffAnyTypeDocument> envelope = await ExecuteAsync(type.Identifier);
            FindingDiffAnyTypeDocument document = envelope.Content;
            Assert.Equal(FindingDiffAnyTypeOutcomeKind.Compared, document.Outcome);
            Assert.Empty(envelope.Diagnostics);
            Assert.Equal(type.FullName, document.FullName);
            FindingDiffAnyTypeCounts counts = document.Counts!;
            Assert.Equal((type.Identifier, type.Api, type.Body), (type.Identifier, counts.Api, counts.Body));
            Assert.Equal(FindingDiffAnyLibraryInspection.IsReported(type), document.Rows.Length > 0);
            Assert.Equal(document.Rows.Length, counts.ReportedRows);
            Assert.All(document.Rows.SkipLast(1), static row =>
                Assert.Equal(FindingDiffAnyTypeOwner.Member, row.Owner));
            Assert.All(document.Rows, static row =>
            {
                Assert.True(row.Api is FindingDiffAnyState.Changed or FindingDiffAnyState.Indeterminate
                    || row.Body is FindingDiffAnyState.Changed or FindingDiffAnyState.Indeterminate);
                if (row.Owner == FindingDiffAnyTypeOwner.Member)
                {
                    Assert.False(string.IsNullOrEmpty(row.Member));
                    Assert.False(string.IsNullOrEmpty(row.Selector));
                    Assert.True(row.BeforeFingerprint is not null || row.AfterFingerprint is not null);
                }
                else
                {
                    Assert.Null(row.Member);
                    Assert.Null(row.Selector);
                }
            });

            // Demand-shaped: the receipt names only the addressed Type on each
            // side where it is present.
            FindingDiffAnyTypeLevelReceipt receipt = document.Receipt!;
            Assert.All(receipt.TypesBefore.Concat(receipt.TypesAfter), name => Assert.Equal(type.FullName, name));
            Assert.True(receipt.TypesBefore.Length + receipt.TypesAfter.Length > 0);
        }
    }

    [Fact]
    public async Task BodyOnlyType_ReportsTheMemberWhoseBodyChanged()
    {
        FindingDiffAnyTypeDocument document = (await ExecuteAsync("FindingDiffAnyFixture.BodyOnly")).Content;

        Assert.Equal(
            new FindingDiffAnyTypeComparison("Fixture", "1.0", "2.0", "FindingDiffAnyFixture.BodyOnly"),
            document.Comparison);
        FindingDiffAnyTypeRow row = Assert.Single(document.Rows);
        Assert.Equal(FindingDiffAnyTypeOwner.Member, row.Owner);
        Assert.Equal(FindingDiffAnyState.Unchanged, row.Api);
        Assert.Equal(FindingDiffAnyState.Changed, row.Body);
        Assert.Equal(row.BeforeFingerprint, row.AfterFingerprint);
        Assert.Equal(1, document.Counts!.BodyChanged);
        Assert.Equal(0, document.Counts.ApiChanged);
        Assert.True(document.Receipt!.ComparedMembers >= 1);
        Assert.True(document.Receipt.BodiesCompared >= 1);
    }

    [Fact]
    public async Task NestedType_IsAddressedByItsEscapedIdentity()
    {
        FindingDiffAnyTypeDocument document = (await ExecuteAsync("FindingDiffAnyFixture.Outer+Inner")).Content;

        Assert.Equal(FindingDiffAnyTypeOutcomeKind.Compared, document.Outcome);
        Assert.Equal("FindingDiffAnyFixture.Outer.Inner", document.FullName);
        Assert.Equal(FindingDiffAnyState.Changed, document.Counts!.Body);
        Assert.NotEmpty(document.Rows);
    }

    [Fact]
    public async Task UnchangedType_HasNoRows()
    {
        FindingDiffAnyTypeDocument document = (await ExecuteAsync("FindingDiffAnyFixture.Unchanged")).Content;

        Assert.Equal(FindingDiffAnyTypeOutcomeKind.Compared, document.Outcome);
        Assert.Empty(document.Rows);
        Assert.Equal(FindingDiffAnyState.Unchanged, document.Counts!.Api);
        Assert.Equal(FindingDiffAnyState.Unchanged, document.Counts.Body);
        Assert.True(document.Receipt!.ComparedMembers > 0);
    }

    [Fact]
    public async Task AbsentType_IsRejectedWithADiagnostic()
    {
        InspectionEnvelope<FindingDiffAnyTypeDocument> envelope =
            await ExecuteAsync("FindingDiffAnyFixture.DoesNotExist");

        Assert.Equal(FindingDiffAnyTypeOutcomeKind.Rejected, envelope.Content.Outcome);
        Assert.Empty(envelope.Content.Rows);
        Assert.Null(envelope.Content.Counts);
        Assert.Null(envelope.Content.Receipt);
        Assert.Contains("absent from both", envelope.Content.Detail);
        InspectionDiagnostic diagnostic = Assert.Single(envelope.Diagnostics);
        Assert.Equal(FindingDiffAnyLibraryInspection.RejectedDiagnosticCode, diagnostic.Code);
    }

    [Fact]
    public async Task Table_IsTheDocumentRowsWithItsShareAndDiagnostics()
    {
        InspectionEnvelope<FindingDiffAnyTypeDocument> document =
            await ExecuteAsync("FindingDiffAnyFixture.BodyOnly");
        InspectionEnvelope<FindingDiffAnyTypeTable> table =
            FindingDiffAnyTypeInspection.Table(document);

        Assert.Equal(document.Content.Rows, table.Content.Rows);
        Assert.Same(document.Share, table.Share);
        Assert.Equal(document.Diagnostics, table.Diagnostics);
    }

    [Fact]
    public async Task Document_SerializesOwnersAndStatesAsStrings()
    {
        FindingDiffAnyTypeDocument document =
            (await ExecuteAsync("FindingDiffAnyFixture.BodyOnly")).Content;
        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(
            document,
            FindingDiffAnyTypeJsonContext.Default.FindingDiffAnyTypeDocument));
        JsonElement root = json.RootElement;

        Assert.Equal("Compared", root.GetProperty("outcome").GetString());
        Assert.Equal("FindingDiffAnyFixture.BodyOnly", root.GetProperty("comparison").GetProperty("type").GetString());
        JsonElement row = Assert.Single(root.GetProperty("rows").EnumerateArray());
        Assert.Equal("Member", row.GetProperty("owner").GetString());
        Assert.Equal("Unchanged", row.GetProperty("api").GetString());
        Assert.Equal("Changed", row.GetProperty("body").GetString());
        Assert.Equal(
            ["FindingDiffAnyFixture.BodyOnly"],
            root.GetProperty("receipt").GetProperty("typesBefore").EnumerateArray().Select(static name => name.GetString()));
        Assert.False(root.TryGetProperty("detail", out _));
    }

    static FindingDiffAnyResult CompareLibrary()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
        return FindingDiffAny.Compare(before, after);
    }

    static async Task<InspectionEnvelope<FindingDiffAnyTypeDocument>> ExecuteAsync(string type)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        await using var workspace = new InspectionWorkspace();
        AssemblyContextParticipant before = Participant(pair.OldAssemblyPath(), "Before");
        AssemblyContextParticipant after = Participant(pair.NewAssemblyPath(), "After");
        using AssemblyContextGroup beforeGroup = workspace.CreateAssemblyContextGroup([before]);
        using AssemblyContextGroup afterGroup = workspace.CreateAssemblyContextGroup([after]);
        return await FindingDiffAnyTypeCapability.Route.ExecuteAsync(
            new FindingDiffAnyTypeRequest(
                "Fixture",
                "1.0",
                "2.0",
                new DiffLibraryPair(beforeGroup, before, afterGroup, after),
                type));
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
