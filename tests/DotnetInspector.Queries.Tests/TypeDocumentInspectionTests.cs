using System.Collections.Immutable;

using DotnetInspector.Sections;
using DotnetInspector.SourceHouse;
using ILInspector.Decompiler;

using QueryHost =
    DotnetInspector.Queries.Tests.AssemblyContextSourceQueryTests.QueryHost;
using TestAssembly =
    DotnetInspector.Queries.Tests.AssemblyContextSourceQueryTests.TestAssembly;

namespace DotnetInspector.Queries.Tests;

public sealed class TypeDocumentInspectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        ExactTypeSettlementPreservesDocumentProvenanceAndEnvelope(
            bool supplyPdb)
    {
        TestAssembly assembly = TestAssembly.Create();
        AssemblyTypeSourceRequest request =
            assembly.TypeRequest("SourceFixture");
        AssemblyContextLibraryPortablePdb? portablePdb = supplyPdb
            ? new(
                ImmutableArray.CreateRange(
                    File.ReadAllBytes(assembly.PdbPath)),
                new AssemblySourcePdbProvenance(
                    assembly.Assembly.Registration,
                    Identity: null,
                    Location: assembly.PdbPath,
                    Path: assembly.PdbPath,
                    SymbolServer: null))
            : null;
        using var host = QueryHost.WithoutPdb();
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeDocumentEntry entry =
            await AssemblyContextSourceQuery
                .ExecuteTypeDocumentAsync(
                    group,
                    assembly.Participant,
                    request,
                    host.Context,
                    portablePdb,
                    TestContext.Current.CancellationToken);

        var settled =
            Assert.IsType<AssemblyTypeDocumentEntry.Settled>(
                entry);
        var house =
            Assert.IsType<SourceHouseDecompilationOutcome.Completed>(
                settled.HouseOutcome);
        Assert.Same(settled.Outcome, house.TypeDocument);
        Assert.Equal(
            SourceHouseDecompilationProduct.StructuredTypeDocument,
            house.Request.Product);
        CSharpTypeDocument document =
            Assert.IsType<CSharpTypeDocumentOutcome.Available>(
                settled.Outcome)
                .Document;
        Assert.Equal(request.Type, document.TypeName);
        Assert.Equal(supplyPdb, document.Source.PdbSupplied);
        Assert.Equal(
            supplyPdb
                ? SourceHousePdbContributionKind.SuppliedCompanion
                : SourceHousePdbContributionKind.Unavailable,
            house.PdbContribution.Kind);
        Assert.Equal(
            settled.Outcome.BodyProjectionsAttempted,
            house.Work.BodyProjectionsAttempted);
        Assert.Equal(
            SourceHouseLibraryLeaseConsumer.SourceHouse,
            house.LeaseSettlement.Consumer);

        string json = CSharpTypeDocumentJson.Serialize(document);
        CSharpTypeDocument replay =
            CSharpTypeDocumentJson.Deserialize(json);
        Assert.Equal(document.Revision, replay.Revision);
        Assert.Equal(document.TypeAddress, replay.TypeAddress);

        InspectionEnvelope<CSharpTypeDocumentOutcome> inspection =
            await TypeDocumentInspection.ExecuteAsync(
                group,
                assembly.Participant,
                request,
                host.Context,
                portablePdb,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<CSharpTypeDocumentOutcome.Available>(
                inspection.Content);
        Assert.Equal(document.Revision, available.Document.Revision);
        var share =
            Assert.IsType<InspectionShare.NonProjectable>(
                inspection.Share);
        Assert.Equal("type-document/share", share.Path);
        Assert.Contains(
            inspection.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == (supplyPdb
                        ? "type-document.portable-pdb.supplied"
                        : "type-document.portable-pdb.unavailable"));
        Assert.Empty(host.SymbolRequests);
        Assert.Empty(host.SourceRequests);
    }

    [Fact]
    public async Task
        ZeroBodyBudgetPreservesIncompleteOutcomeAndDiagnostics()
    {
        TestAssembly assembly = TestAssembly.Create();
        using var host = QueryHost.WithoutPdb(
            maxDecompilerBodyProjections: 0);
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        InspectionEnvelope<CSharpTypeDocumentOutcome> inspection =
            await TypeDocumentInspection.ExecuteAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest("SourceFixture"),
                host.Context,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var incomplete =
            Assert.IsType<CSharpTypeDocumentOutcome.Incomplete>(
                inspection.Content);
        Assert.Equal(0, incomplete.BodyProjectionsAttempted);
        Assert.Contains(
            incomplete.Document.Bodies,
            body =>
                body.Diagnostics.Any(diagnostic =>
                    diagnostic.Id
                        == DiagnosticIds.CompositionBudgetExceeded));
        Assert.Contains(
            inspection.Diagnostics,
            diagnostic =>
                diagnostic.Code == "type-document.incomplete"
                && diagnostic.Severity
                    == InspectionDiagnosticSeverity.Warning);
        Assert.Contains(
            inspection.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == "type-document.portable-pdb.unavailable");
    }

    [Fact]
    public async Task
        TerminalLibraryAdmissionRemainsVisibleInEnvelope()
    {
        TestAssembly assembly = TestAssembly.Create();
        using var host = QueryHost.WithoutPdb();
        SourceHouseDecompilationLimits defaults =
            host.Context.TypeDecompilationLimits;
        var context =
            new AssemblyContextSourceQueryContext(
                host.Context.SymbolClient,
                host.Context.PdbStore,
                host.Context.PackageSourceAuthorization,
                host.Context.SourceFetch)
            {
                TypeDecompilationLimits = new(
                    maximumAssemblyBytes: 1,
                    maximumPortablePdbBytes: 1,
                    defaults.TargetBounds,
                    defaults.EmbeddedPdbReadLimits),
            };
        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [assembly.Participant]);

        AssemblyTypeDocumentEntry entry =
            await AssemblyContextSourceQuery
                .ExecuteTypeDocumentAsync(
                    group,
                    assembly.Participant,
                    assembly.TypeRequest("SourceFixture"),
                    context,
                    cancellationToken:
                        TestContext.Current.CancellationToken);
        var unavailable =
            Assert.IsType<AssemblyTypeDocumentEntry.Unavailable>(
                entry);
        Assert.IsType<
            AssemblyContextLibraryAdapterResult.Incomplete>(
                unavailable.LibraryFailure);

        InspectionEnvelope<CSharpTypeDocumentOutcome> inspection =
            await TypeDocumentInspection.ExecuteAsync(
                group,
                assembly.Participant,
                assembly.TypeRequest("SourceFixture"),
                context,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        Assert.IsType<CSharpTypeDocumentOutcome.Unavailable>(
            inspection.Content);
        Assert.Contains(
            inspection.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == "type-document.inspection-unavailable"
                && diagnostic.Severity
                    == InspectionDiagnosticSeverity.Error);
        Assert.Contains(
            inspection.Diagnostics,
            diagnostic =>
                diagnostic.Code == "type-document.unavailable");
    }
}
