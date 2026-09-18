using DotnetInspector.Packages;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.QueriesConsumer;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class CompleteRestorationExecutionTests
{
    private const string MultiBindingContext =
        """{"k":"member","l":["Avalonia.Base","12.1.2.0",null,"c8d484a7012f9a8b"],"y":"Avalonia.Data.MultiBinding","s":"M:Avalonia.Data.MultiBinding.#ctor()"}""";

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("workspace", "workspace.overview", StructuralSubjectKind.Workspace)]
    [InlineData("package", "package.overview", StructuralSubjectKind.Package)]
    [InlineData("library", "library.references", StructuralSubjectKind.Library)]
    [InlineData("type", "type.metadata", StructuralSubjectKind.Type)]
    [InlineData("type", "type.api", StructuralSubjectKind.Type)]
    [InlineData("type", null, StructuralSubjectKind.Type)]
    [InlineData("member", "member.overview", StructuralSubjectKind.Member)]
    public async Task Version4_AvaloniaRetainedConstructor_SelectsOnlyRequestedNode(
        string subject,
        string? facet,
        StructuralSubjectKind expected)
    {
        WorkspaceSharePacket packet = DescendantPacket(subject, facet);
        var authority = new TestIntentAuthority();
        var ready = Assert.IsType<CompleteRestorationPreparationResult.Ready>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                WorkspaceSharePacketCodec.Encode(packet), authority));
        Assert.IsType<CompleteRestorationRecipe.Version4>(ready.Plan.Recipe);
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());

        var result = await WorkspaceDefinitionConsumer.RestoreAsync(
            ready, authority, host,
            Options(client, await AvaloniaStoreAsync()),
            TestContext.Current.CancellationToken);
        try
        {
            var activated = Assert.IsType<
                CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
            var resolved = Assert.IsType<CompleteRestorationResolvedState.Version4>(
                activated.Workspace.Snapshot.Resolved);
            CompleteRestorationResolvedViewState state =
                resolved.States[resolved.ActiveStateIndex!.Value];
            Assert.Equal(expected, state.Initialization!.Subject!.Kind);
            Assert.Equal(facet, state.Initialization.Lens?.Facet.Value);
            NavigationRetainedSubjectContext retained = state.Initialization.Context!;
            Assert.NotNull(retained.Member);
            Assert.Equal(retained.Type, retained.Member.DeclaringType);
            Assert.Equal(retained.Library, retained.Type!.Library);
            StructuralSubjectIdentity requested = expected switch
            {
                StructuralSubjectKind.Workspace =>
                    StructuralSubjectIdentity.ForWorkspace(activated.Workspace.Workspace),
                StructuralSubjectKind.Package => retained.Package,
                StructuralSubjectKind.Library => retained.Library!,
                StructuralSubjectKind.Type => retained.Type,
                StructuralSubjectKind.Member => retained.Member,
                _ => throw new InvalidOperationException(),
            };
            Assert.Equal(requested, state.Initialization.Subject);
            NavigationWorkspaceSnapshot installed =
                activated.Workspace.Snapshot.Navigation.State.InstalledSnapshot;
            Assert.Equal(requested, installed.ActiveSubject);
            Assert.Equal(retained, installed.RetainedContext);
            Assert.Equal(facet ?? "type.api",
                installed.LensOutcome.EffectiveLens!.Facet.Value);
            if (facet is null)
                Assert.IsType<NavigationLensEvaluationBasis.Recommendation>(
                    installed.LensOutcome.Basis);
            else
                Assert.IsType<NavigationLensEvaluationBasis.ExactRequest>(
                    installed.LensOutcome.Basis);
            Assert.Equal(
                WorkspaceSharePacketCodec.Encode(packet),
                Assert.IsType<CompleteRestorationProjection.Projectable>(
                    activated.Workspace.Projection).CanonicalPacket);
        }
        finally
        {
            if (host.Workspace is { } workspace)
                Assert.True((await workspace.CloseAsync()).Succeeded);
        }
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Version4_DefinitionRestoration_PreservesInactiveIntent(
        bool exact)
    {
        WorkspaceSharePacket packet = DescendantPacket(
            "type", exact ? "type.metadata" : null, active: false);
        CommittedScenarioDefinitionSet definitions =
            WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                packet, TestContext.Current.CancellationToken);
        var registry = new InspectionDefinitionRegistry();
        foreach (InspectionDefinitionRecord record in definitions.Records)
            registry.Add(record);
        Assert.Equal(
            InspectionDefinitionSchema.Version4,
            WorkspaceDefinitionConsumer.GetCommittedDefinitions(
                registry, definitions.Scenario.Id).Scenario.SchemaVersion);
        var authority = new TestIntentAuthority();
        var ready = Assert.IsType<CompleteRestorationPreparationResult.Ready>(
            WorkspaceDefinitionConsumer.PrepareRestoration(
                registry, definitions.Scenario.Id, authority));
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());
        var result = await WorkspaceDefinitionConsumer.RestoreAsync(
            ready, authority, host,
            Options(client, await AvaloniaStoreAsync()),
            TestContext.Current.CancellationToken);
        try
        {
            var activated = Assert.IsType<
                CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
            var resolved = Assert.IsType<CompleteRestorationResolvedState.Version4>(
                activated.Workspace.Snapshot.Resolved);
            Assert.Equal(0, resolved.ActiveStateIndex);
            Assert.Equal(StructuralSubjectKind.Workspace,
                activated.Workspace.Snapshot.Navigation.State.InstalledSnapshot
                    .ActiveSubject.Kind);
            Assert.Equal(StructuralSubjectKind.Type,
                resolved.States[1].Initialization!.Subject!.Kind);
            Assert.NotNull(resolved.States[1].Initialization!.Context!.Member);
            Assert.Equal(exact, resolved.States[1].Initialization!.Lens is not null);
            Assert.Single(activated.Workspace.Snapshot.Definition.Plan.Registrations);
            Assert.Equal(
                WorkspaceSharePacketCodec.Encode(packet),
                Assert.IsType<CompleteRestorationProjection.Projectable>(
                    activated.Workspace.Projection).CanonicalPacket);
        }
        finally
        {
            if (host.Workspace is { } workspace)
                Assert.True((await workspace.CloseAsync()).Succeeded);
        }
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Version4_UnavailableOrFailedInspector_PreservesExactRequest(
        bool failed)
    {
        var authority = new TestIntentAuthority();
        WorkspaceSharePacket packet = DescendantPacket("type", "type.metadata");
        var ready = WorkspaceDefinitionConsumer.PrepareRestoration(
            WorkspaceSharePacketCodec.Encode(packet), authority);
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());
        CompleteRestorationExecutionOptions options =
            Options(client, await AvaloniaStoreAsync());
        var availability = new ViewFacetAvailabilitySnapshot(
            options.Facets.Descriptors.Select(descriptor =>
                new ViewFacetAvailabilityFact(
                    descriptor.Id,
                    descriptor.Id.Value != "type.metadata"
                        ? ViewFacetAvailability.Available.Instance
                        : failed
                            ? new ViewFacetAvailability.Failed(
                                "Metadata failed.", new RestorationFacetDiagnostic())
                            : new ViewFacetAvailability.Unavailable(
                                ViewFacetUnavailableReason.CapabilityAbsent(
                                    "Metadata unavailable.")))));
        var result = await WorkspaceDefinitionConsumer.RestoreAsync(
            ready, authority, host,
            options with { FacetAvailability = (_, _) => availability },
            TestContext.Current.CancellationToken);
        try
        {
            var activated = Assert.IsType<
                CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
            var resolved = Assert.IsType<CompleteRestorationResolvedState.Version4>(
                activated.Workspace.Snapshot.Resolved);
            CompleteRestorationResolvedViewState state = resolved.States[1];
            Assert.Equal("type.metadata", state.Initialization!.Lens!.Facet.Value);
            NavigationWorkspaceSnapshot installed =
                activated.Workspace.Snapshot.Navigation.State.InstalledSnapshot;
            Assert.Null(installed.LensOutcome.EffectiveLens);
            var exact = Assert.IsType<NavigationLensEvaluationBasis.ExactRequest>(
                installed.LensOutcome.Basis);
            Assert.Equal(state.Initialization.Lens, exact.Request);
            if (failed)
            {
                Assert.IsType<NavigationLensActivationResult.Failed>(state.LensResolution);
                Assert.IsType<ViewFacetResolution.Failed>(exact.Result);
            }
            else
            {
                Assert.IsType<NavigationLensActivationResult.Unavailable>(state.LensResolution);
                Assert.IsType<ViewFacetResolution.Unavailable>(exact.Result);
            }
        }
        finally
        {
            if (host.Workspace is { } workspace)
                Assert.True((await workspace.CloseAsync()).Succeeded);
        }
    }

    [Theory]
    [InlineData("member.overview", true)]
    [InlineData("type.unknown", false)]
    public async Task Version4_InvalidActiveKindInspector_FailsBeforeConstruction(
        string facet, bool active)
    {
        var authority = new TestIntentAuthority();
        var ready = WorkspaceDefinitionConsumer.PrepareRestoration(
            WorkspaceSharePacketCodec.Encode(
                DescendantPacket("type", facet, active: active)), authority);
        using var client = new HttpClient(new RejectingHandler());
        var result = await WorkspaceDefinitionConsumer.RestoreAsync(
            ready, authority, new NeverConstructHost(),
            Options(client, new InMemoryPackageStore()),
            TestContext.Current.CancellationToken);
        var failure = Assert.IsType<
            CompleteRestorationFailure.SelectorResolutionFailed>(
                Assert.IsType<CompleteRestorationResult<InspectionWorkspace>.Failed>(
                    result).Failure);
        Assert.Equal(CommittedSelectorResolutionFailureKind.InvalidFacet,
            failure.Failure.Kind);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Version4_AggregateLibrary_SelectsExactPackageAggregate()
    {
        var authority = new TestIntentAuthority();
        var ready = WorkspaceDefinitionConsumer.PrepareRestoration(
            WorkspaceSharePacketCodec.Encode(
                DescendantPacket("library", "library.references",
                    context: """{"k":"all-libraries"}""")), authority);
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());
        var result = await WorkspaceDefinitionConsumer.RestoreAsync(
            ready, authority, host,
            Options(client, await AvaloniaStoreAsync()),
            TestContext.Current.CancellationToken);
        try
        {
            var activated = Assert.IsType<
                CompleteRestorationResult<InspectionWorkspace>.Activated>(result);
            var resolved = Assert.IsType<CompleteRestorationResolvedState.Version4>(
                activated.Workspace.Snapshot.Resolved);
            NavigationInitialization state = resolved.States[1].Initialization!;
            var library = Assert.IsType<StructuralSubjectIdentity.AllLibrariesSubject>(
                state.Subject);
            Assert.Equal(library, state.Context!.Library);
            Assert.Null(state.Context.Type);
            Assert.Null(state.Context.Member);
            Assert.Equal(library, activated.Workspace.Snapshot.Navigation.State
                .InstalledSnapshot.ActiveSubject);
        }
        finally
        {
            if (host.Workspace is { } workspace)
                Assert.True((await workspace.CloseAsync()).Succeeded);
        }
    }

    [Fact]
    public async Task Version4_StaleForwardingLibrary_IsNotRepaired()
    {
        var authority = new TestIntentAuthority();
        var ready = WorkspaceDefinitionConsumer.PrepareRestoration(
            WorkspaceSharePacketCodec.Encode(
                DescendantPacket("type", "type.metadata",
                    context: MultiBindingContext.Replace(
                        "Avalonia.Base", "Avalonia.Markup", StringComparison.Ordinal))),
            authority);
        var host = new TestHost();
        using var client = new HttpClient(new RejectingHandler());
        var result = await WorkspaceDefinitionConsumer.RestoreAsync(
            ready, authority, host,
            Options(client, await AvaloniaStoreAsync()),
            TestContext.Current.CancellationToken);
        var failure = Assert.IsType<
            CompleteRestorationFailure.SelectorResolutionFailed>(
                Assert.IsType<CompleteRestorationResult<InspectionWorkspace>.Failed>(
                    result).Failure);
        Assert.Equal(CommittedSelectorResolutionFailureKind.TypeMissing,
            failure.Failure.Kind);
        Assert.True(host.CloseReport!.Succeeded);
    }

    private static WorkspaceSharePacket DescendantPacket(
        string subject,
        string? facet,
        string context = MultiBindingContext,
        bool active = true)
    {
        string json =
            """{"f":4,"t":[["Avalonia","12.1.2","net8.0",null]],"g":[[0]],"r":[["p","Microsoft.Extensions."]],"a":"""
            + (active ? "0" : "null")
            + ""","x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"r":"""
            + context + ",\"u\":{\"k\":\"" + subject + "\"}"
            + (facet is null ? "" : ",\"f\":\"" + facet + "\"")
            + "}]}";
        return WorkspaceSharePacketCodec.ParseJson(json);
    }

    private static async Task<IPackageStore> AvaloniaStoreAsync()
    {
        string path = Path.Combine(FindRepositoryRoot(),
            "fixtures", "cli", "package-archives", "avalonia.12.1.2.nupkg");
        await using FileStream content = File.OpenRead(path);
        var store = new InMemoryPackageStore();
        await store.CommitAsync("Avalonia", "12.1.2",
            NuGetCache.GetSourceKey("https://api.nuget.org/v3/index.json"),
            content, TestContext.Current.CancellationToken);
        return store;
    }

    private sealed record RestorationFacetDiagnostic : IViewFacetDiagnosticEvidence;
}
