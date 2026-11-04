using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class LibraryTypeCatalogInspectionOperationTests
{
    [Fact]
    public async Task BoundedCatalogAndPreparedCompletionDoNotAuthorizeResolutionOrReinspection()
    {
        string path = typeof(LibraryTypeCatalogInspectionOperation).Assembly.Location;
        ResolvedAssemblyReference original = ResolvedAssemblyReference.CreateFromPath(
            path, AssemblyResolutionProvenance.Local("catalog bounded test"));
        int opens = 0;
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
            original.Identity, null,
            () =>
            {
                opens++;
                return File.OpenRead(path);
            },
            original.Provenance);
        await using var workspace = new InspectionWorkspace();
        var participant = new AssemblyContextParticipant(assembly, new RefusingPolicy());
        AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([participant]);

        var inspection = LibraryTypeCatalogInspectionOperation.Execute(
            group, ApiSurfaceScope.PublicWithNonPublicTypes,
            new(1, 1, 1, 10, 10, 10_000), [participant]);
        int inspectedOpens = opens;
        var prepared = LibraryTypeCatalogInspectionOperation.Complete(inspection.Content);

        Assert.True(inspectedOpens > 0);
        Assert.Equal(inspectedOpens, opens);
        Assert.Same(inspection.Content, prepared.Content);
        Assert.False(prepared.Content.IsComplete);
        Assert.NotNull(prepared.Content.Truncation);
        Assert.Equal(inspection.Diagnostics, prepared.Diagnostics);
        Assert.Contains(prepared.Diagnostics,
            diagnostic => diagnostic.Code == "library-type-catalog.truncated");
        Assert.IsType<InspectionShare.NonProjectable>(prepared.Share);
    }

    [Fact]
    public async Task UnavailableRootDoesNotBecomeAnEmptySuccessfulCatalog()
    {
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
            new("Unavailable.Catalog", new Version(1, 0), null, null), null,
            () => new MemoryStream([0, 1, 2, 3], writable: false),
            AssemblyResolutionProvenance.Local("catalog invalid test"));
        var inspection = await LibraryTypeCatalogInspectionOperation.ExecuteWithLiveSourcesAsync(
            assembly, NoResolverAssemblyBindingPolicy.Instance, ApiSurfaceScope.Public,
            (envelope, sources, _) =>
            {
                Assert.Empty(sources);
                return ValueTask.FromResult(envelope);
            },
            TestContext.Current.CancellationToken);

        Assert.False(inspection.Content.IsComplete);
        Assert.DoesNotContain(inspection.Content.Assemblies.Assemblies,
            entry => entry is AssemblyContextEntry<AssemblyApiSurface>.Available);
        Assert.Contains(inspection.Diagnostics,
            diagnostic => diagnostic.Severity == InspectionDiagnosticSeverity.Error);
    }

    sealed class RefusingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request) =>
            throw new InvalidOperationException("Bounded catalog must not resolve dependencies.");
    }
}
