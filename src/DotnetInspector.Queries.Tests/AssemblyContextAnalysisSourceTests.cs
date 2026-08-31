using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextAnalysisSourceTests
{
    [Fact]
    public void BindingPolicyResolver_PreservesDelegatedNonSelectedResults()
    {
        AssemblyBindingSelection[] terminalResults =
        [
            AssemblyBindingSelection.NotFound(),
            AssemblyBindingSelection.NameNotOwned(),
            AssemblyBindingSelection.NameOwnedButNoMatch(),
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable)),
            AssemblyBindingSelection.Invalid(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.InvalidPolicyResult)),
        ];

        foreach (AssemblyBindingSelection terminal in terminalResults)
        {
            var policy = new FixedPolicy(terminal);
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.Create(
                    new AssemblyReferenceIdentity(
                        "Root",
                        new Version(1, 0, 0, 0),
                        null,
                        null),
                    path: null,
                    openRead: () => new MemoryStream(),
                    AssemblyResolutionProvenance.Local("test"));
            using var workspace = new InspectionWorkspace();
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup(
                    [new AssemblyContextParticipant(assembly, policy)]);
            var subject = new AssemblyContextSubject(assembly);
            IAssemblyReferenceResolver resolver =
                AssemblyContextAnalysisSource.Resolver(group, subject);
            var bindingPolicy =
                Assert.IsAssignableFrom<IAssemblyBindingPolicy>(resolver);
            ResolvedAssemblyReference retainedRoot = Descriptor();
            var request = new AssemblyBindingRequest(
                AssemblyBindingTarget.Reference(
                    new AssemblyReferenceIdentity(
                        "Dependency",
                        new Version(1, 0, 0, 0),
                        null,
                        null)),
                AssemblyBindingOrigin.FromAssembly(retainedRoot),
                AssemblyResolutionScope.Any);

            Assert.Same(policy.Version, bindingPolicy.Version);
            Assert.Same(terminal, bindingPolicy.Select(request));
            Assert.Same(
                assembly.Registration,
                Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(
                    policy.LastRequest!.Origin).Registration);
            Assert.Same(
                terminal,
                new AssemblyReferenceBindingPolicy(resolver)
                    .Select(request));
        }
    }

    [Fact]
    public void BindingPolicyResolver_RetainsSelectedDescriptorAndShadows()
    {
        ResolvedAssemblyReference root = Descriptor();
        ResolvedAssemblyReference selected = Descriptor();
        ResolvedAssemblyReference shadow = Descriptor();
        var policy = new FixedPolicy(
            AssemblyBindingSelection.Found(selected, [shadow]));
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(root, policy),
                    new AssemblyContextParticipant(selected, policy),
                    new AssemblyContextParticipant(shadow, policy),
                ]);
        var subject = new AssemblyContextSubject(root);
        var bindingPolicy = Assert.IsAssignableFrom<IAssemblyBindingPolicy>(
            AssemblyContextAnalysisSource.Resolver(group, subject));

        var retained = Assert.IsType<AssemblyBindingSelection.Selected>(
            bindingPolicy.Select(Request(root)));

        Assert.Same(
            selected.Registration,
            retained.Assembly.Registration);
        Assert.NotSame(selected, retained.Assembly);
        ResolvedAssemblyReference retainedShadow =
            Assert.Single(retained.ShadowedAssemblies);
        Assert.Same(
            shadow.Registration,
            retainedShadow.Registration);
        Assert.NotSame(shadow, retainedShadow);
    }

    [Fact]
    public void BindingPolicyResolver_RetainsAmbiguousDescriptors()
    {
        ResolvedAssemblyReference root = Descriptor();
        ResolvedAssemblyReference first = Descriptor();
        ResolvedAssemblyReference second = Descriptor();
        var policy = new FixedPolicy(
            AssemblyBindingSelection.Multiple([first, second]));
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(root, policy),
                    new AssemblyContextParticipant(first, policy),
                    new AssemblyContextParticipant(second, policy),
                ]);
        var subject = new AssemblyContextSubject(root);
        var bindingPolicy = Assert.IsAssignableFrom<IAssemblyBindingPolicy>(
            AssemblyContextAnalysisSource.Resolver(group, subject));

        var retained = Assert.IsType<AssemblyBindingSelection.Ambiguous>(
            bindingPolicy.Select(Request(root)));

        Assert.Equal(
            [first.Registration, second.Registration],
            retained.Assemblies.Select(
                assembly => assembly.Registration));
        Assert.DoesNotContain(first, retained.Assemblies);
        Assert.DoesNotContain(second, retained.Assemblies);
    }

    static ResolvedAssemblyReference Descriptor() =>
        ResolvedAssemblyReference.CreateFromPath(
            typeof(AssemblyContextAnalysisSourceTests).Assembly.Location,
            AssemblyResolutionProvenance.Local("test"));

    static AssemblyBindingRequest Request(
        ResolvedAssemblyReference origin) =>
        new(
            AssemblyBindingTarget.Reference(
                new AssemblyReferenceIdentity(
                    "Dependency",
                    new Version(1, 0, 0, 0),
                    null,
                    null)),
            AssemblyBindingOrigin.FromAssembly(origin),
            AssemblyResolutionScope.Any);

    sealed class FixedPolicy(AssemblyBindingSelection selection)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();
        internal AssemblyBindingRequest? LastRequest { get; private set; }

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            LastRequest = request;
            return selection;
        }
    }
}
