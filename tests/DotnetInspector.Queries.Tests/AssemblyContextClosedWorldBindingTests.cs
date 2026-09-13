using DotnetInspector.Services;
using ILInspector.Metadata;

using static DotnetInspector.Queries.Tests.WorkspaceResearchTargetFixture;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextClosedWorldBindingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void WorkspaceResearchTarget_RejectsAcquiringPolicyBeforeDiscoveryOrOpen(int participant)
    {
        byte[] omitted = BuildAssembly("Omitted");
        using var fixture = new WorkspaceResearchTargetFixture(
            BuildAssembly("Facade", false, Identity(omitted)), BuildAssembly("Unused", false));
        var policy = new AcquiringPolicy(fixture.Group.BindingPolicyVersion, omitted);
        using var group = fixture.CreateGroup(fixture.Nodes.Select((node, index) =>
            new AssemblyContextParticipant(node.Assembly,
                index == participant ? policy : node.Policy)));
        var plan = fixture.Plan();
        int[] admittedOpens = fixture.Nodes.Select(node => node.Opens).ToArray();

        var query = Assert.IsType<AssemblyContextTypeResolutionResult.UnsupportedBindingPolicy>(
            AssemblyContextTypeResolutionQuery.Execute(
                group, fixture.Nodes[0].Participant, TypeName, AssemblyResolutionScope.Any));
        Assert.Same(fixture.Nodes[participant].Assembly, query.Assembly);
        AssertNoAcquisition();

        var result = Assert.IsType<WorkspaceResearchTargetCompositionResult.Rejected>(
            WorkspaceResearchTargetCompositionQuery.Execute(
                plan.Request(fixture, group: group), TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceResearchTargetCompositionRejection.UnsupportedBindingPolicy, result.Reason);
        var evidence = Assert.IsType<WorkspaceTypeResolutionEvidence.UnsupportedBindingPolicy>(result.Evidence);
        Assert.Same(plan.Population.Before[participant].Id, evidence.Input);
        Assert.Null(result.TerminalAttempt);
        AssertNoAcquisition();

        // This policy acquires the omitted image inside Select, before returning
        // any descriptor. A filter around its returned candidates is too late.
        var control = Assert.IsType<AssemblyBindingSelection.Selected>(policy.Select(
            Request(Identity(omitted), fixture.Nodes[0].Assembly)).Selection);
        Assert.Equal(Identity(omitted), control.Assembly.Identity);
        Assert.Equal(1, policy.Selections);
        Assert.Equal(1, policy.Discoveries);
        Assert.Equal(1, policy.SourceOpens);
        Assert.Equal(1, policy.Acquisitions);

        void AssertNoAcquisition()
        {
            Assert.Equal(0, policy.Selections);
            Assert.Equal(0, policy.Discoveries);
            Assert.Equal(0, policy.SourceOpens);
            Assert.Equal(0, policy.Acquisitions);
            Assert.Equal(admittedOpens, fixture.Nodes.Select(node => node.Opens));
            Assert.All(fixture.Nodes, node => Assert.Equal(0, node.Policy.Selections));
        }
    }

    [Fact]
    public void WorkspaceResearchTarget_RejectsDependencyResolverBeforeItAcquiresOmittedSibling()
    {
        byte[] omitted = BuildAssembly("Omitted");
        byte[] facade = BuildAssembly("Facade", false, Identity(omitted));
        string scratch = Path.Combine(AppContext.BaseDirectory,
            "workspace-acquisition-gate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            string facadePath = Path.Combine(scratch, "Facade.dll");
            string omittedPath = Path.Combine(scratch, "Omitted.dll");
            File.WriteAllBytes(facadePath, facade);
            File.WriteAllBytes(omittedPath, omitted);
            var resolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(facadePath)
                {
                    IncludeTrustedPlatformAssemblies = false,
                    IncludeAspNetCoreSharedFramework = false,
                    IncludeDepsJsonAssets = false,
                    IncludeSiblingAssemblies = true,
                    SnapshotAssemblyImages = true,
                });
            var probe = new SelectionProbe(resolver);
            using var fixture = new WorkspaceResearchTargetFixture(facade);
            using var group = fixture.CreateGroup(
                [new AssemblyContextParticipant(fixture.Nodes[0].Assembly, probe)]);
            var plan = fixture.Plan();
            int admittedOpens = fixture.Nodes[0].Opens;

            Assert.IsType<AssemblyContextTypeResolutionResult.UnsupportedBindingPolicy>(
                AssemblyContextTypeResolutionQuery.Execute(
                    group, fixture.Nodes[0].Participant, TypeName, AssemblyResolutionScope.Any));
            Assert.Equal(0, probe.Selections);
            var result = Assert.IsType<WorkspaceResearchTargetCompositionResult.Rejected>(
                WorkspaceResearchTargetCompositionQuery.Execute(
                    plan.Request(fixture, group: group), TestContext.Current.CancellationToken));
            Assert.Equal(WorkspaceResearchTargetCompositionRejection.UnsupportedBindingPolicy, result.Reason);
            Assert.Equal(0, probe.Selections);
            Assert.Equal(admittedOpens, fixture.Nodes[0].Opens);

            var control = Assert.IsType<AssemblyBindingSelection.Selected>(
                probe.Select(Request(Identity(omitted), fixture.Nodes[0].Assembly)).Selection);
            Assert.Equal(1, probe.Selections);
            Assert.Equal(Identity(omitted), control.Assembly.Identity);
            // The real resolver has already discovered and acquired the bytes
            // inside Select: the descriptor remains readable without its source.
            File.Delete(omittedPath);
            using Stream retained = control.Assembly.OpenRead();
            using var content = new MemoryStream();
            retained.CopyTo(content);
            Assert.Equal(omitted, content.ToArray());
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkspaceResearchTarget_ClosedWorldPreservesTypedBindingMiss(bool unavailable)
    {
        byte[] omitted = BuildAssembly("Omitted");
        using var fixture = new WorkspaceResearchTargetFixture(
            BuildAssembly("Facade", false, Identity(omitted)));
        fixture.Nodes[0].Policy.SelectOverride = _ => unavailable
            ? AssemblyBindingSelection.CannotSelect(new(AssemblyBindingFailureKind.CandidateUnavailable))
            : AssemblyBindingSelection.NameOwnedButNoMatch();
        var plan = fixture.Plan();
        var query = Assert.IsType<AssemblyContextTypeResolutionResult.Available>(
            AssemblyContextTypeResolutionQuery.Execute(
                fixture.Group, fixture.Nodes[0].Participant, TypeName, AssemblyResolutionScope.Any));
        var result = Assert.IsType<WorkspaceResearchTargetCompositionResult.Unavailable>(
            WorkspaceResearchTargetCompositionQuery.Execute(
                plan.Request(fixture), TestContext.Current.CancellationToken));
        var evidence = Assert.IsType<WorkspaceTypeResolutionEvidence.Available>(result.Evidence);
        Assert.Single(query.Outcome.Hops);
        Assert.Single(evidence.Outcome.Hops);
        if (unavailable)
        {
            Assert.Equal(AssemblyBindingFailureKind.CandidateUnavailable,
                Assert.IsType<TypeResolutionOutcome.Unavailable>(query.Outcome).Failure.Kind);
            Assert.Equal(AssemblyBindingFailureKind.CandidateUnavailable,
                Assert.IsType<WorkspaceMetadataEvidence.Outcome.Unavailable>(evidence.Outcome).Failure.Kind);
        }
        else
        {
            Assert.IsType<TypeResolutionOutcome.UnboundBinding>(query.Outcome);
            Assert.IsType<WorkspaceMetadataEvidence.Outcome.UnboundBinding>(evidence.Outcome);
        }
    }

    [Fact]
    public void WorkspaceResearchTarget_ClosedWorldPreservesAdmittedVersionPolicyWithoutReopening()
    {
        byte[] contract = BuildAssembly("Terminal");
        using var fixture = new WorkspaceResearchTargetFixture(
            BuildAssembly("Facade", false, Identity(contract)),
            BuildAssembly("Terminal", version: new Version(2, 0, 0, 0)));
        fixture.Nodes[0].Policy.Target = fixture.Nodes[1].Assembly;
        var plan = fixture.Plan();
        fixture.RetainAll();
        int[] admittedOpens = fixture.Nodes.Select(node => node.Opens).ToArray();

        var query = Assert.IsType<AssemblyContextTypeResolutionResult.Available>(
            AssemblyContextTypeResolutionQuery.Execute(
                fixture.Group, fixture.Nodes[0].Participant, TypeName, AssemblyResolutionScope.Any));
        var resolved = Assert.IsType<TypeResolutionOutcome.Resolved>(query.Outcome);
        Assert.Same(fixture.Nodes[1].Assembly.Registration, resolved.Definition.Assembly.Assembly.Registration);
        Assert.Equal(new Version(1, 0, 0, 0), Assert.Single(resolved.Hops).TargetReference.Version);
        Assert.Equal(new Version(2, 0, 0, 0), resolved.Definition.Assembly.Assembly.Identity.Version);

        var result = Assert.IsType<WorkspaceResearchTargetCompositionResult.Composed>(
            WorkspaceResearchTargetCompositionQuery.Execute(
                plan.Request(fixture, terminal: 1), TestContext.Current.CancellationToken));
        Assert.Same(plan.Population.Before[1].Id, result.Receipt.TerminalInput);
        Assert.Equal(admittedOpens, fixture.Nodes.Select(node => node.Opens));
        Assert.True(fixture.Nodes[0].Policy.Selections > 0);
    }

    [Fact]
    public void WorkspaceResearchTarget_ClosedWorldPreservesInGroupAmbiguity()
    {
        byte[] terminal = BuildAssembly("Terminal");
        using var fixture = new WorkspaceResearchTargetFixture(
            BuildAssembly("Facade", false, Identity(terminal)), terminal, terminal);
        var plan = fixture.Plan();
        var query = Assert.IsType<AssemblyContextTypeResolutionResult.Available>(
            AssemblyContextTypeResolutionQuery.Execute(
                fixture.Group, fixture.Nodes[0].Participant, TypeName, AssemblyResolutionScope.Any));
        Assert.IsType<TypeResolutionOutcome.Ambiguous>(query.Outcome);
        var result = Assert.IsType<WorkspaceResearchTargetCompositionResult.Unavailable>(
            WorkspaceResearchTargetCompositionQuery.Execute(
                plan.Request(fixture, terminal: 1), TestContext.Current.CancellationToken));
        Assert.IsType<WorkspaceMetadataEvidence.Outcome.Ambiguous>(
            Assert.IsType<WorkspaceTypeResolutionEvidence.Available>(result.Evidence).Outcome);
        Assert.Null(result.TerminalAttempt);
        Assert.All(fixture.Nodes, node => Assert.Equal(0, node.Policy.Selections));
    }

    static AssemblyBindingRequest Request(
        AssemblyReferenceIdentity target, ResolvedAssemblyReference origin) =>
        new(AssemblyBindingTarget.Reference(target), AssemblyBindingOrigin.FromAssembly(origin),
            AssemblyResolutionScope.Any);

    sealed class SelectionProbe(IAssemblyBindingPolicy inner) : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version => inner.Version;
        internal int Selections { get; private set; }
        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            Selections++;
            return inner.Select(request);
        }
    }

    sealed class AcquiringPolicy(AssemblyBindingPolicyVersion version, byte[] image) : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version => version;
        internal int Selections { get; private set; }
        internal int Discoveries { get; private set; }
        internal int SourceOpens { get; private set; }
        internal int Acquisitions { get; private set; }

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            Selections++;
            Discoveries++;
            SourceOpens++;
            using var source = new MemoryStream(image, writable: false);
            using var retained = new MemoryStream();
            source.CopyTo(retained);
            Acquisitions++;
            return new(version, AssemblyBindingSelection.Found(Descriptor(retained.ToArray())));
        }
    }
}
