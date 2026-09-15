using System.Collections.Immutable;
using System.Reflection;

using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class AssemblyBindingCandidateDomainTests
{
    [Fact]
    public void Create_RequiresNonemptyExactRegistrationDomain()
    {
        ResolvedAssemblyReference candidate = Descriptor(
            "Candidate",
            path: "Candidate.dll");
        ResolvedAssemblyReference substituted =
            candidate.WithoutLocalPath();

        Assert.Throws<ArgumentException>(
            () => AssemblyBindingCandidateDomain.Create(default));
        Assert.Throws<ArgumentException>(
            () => AssemblyBindingCandidateDomain.Create([]));
        Assert.Throws<ArgumentException>(
            () => AssemblyBindingCandidateDomain.Create([null!]));
        Assert.Throws<ArgumentException>(
            () => AssemblyBindingCandidateDomain.Create(
                [candidate, substituted]));
    }

    [Fact]
    public void Create_PreservesIssuerOrderAndExactDescriptors()
    {
        ResolvedAssemblyReference first = Descriptor("First");
        ResolvedAssemblyReference second = Descriptor("Second");
        ResolvedAssemblyReference third = Descriptor("Third");

        AssemblyBindingCandidateDomain domain =
            AssemblyBindingCandidateDomain.Create(
                [third, first, second]);

        Assert.Equal([third, first, second], domain.Candidates);
    }

    [Fact]
    public void CompositionRequired_CarriesExactDomainInPolicySnapshot()
    {
        AssemblyBindingCandidateDomain domain =
            AssemblyBindingCandidateDomain.Create(
                [Descriptor("Candidate")]);
        var version = new AssemblyBindingPolicyVersion();
        var snapshot = new AssemblyBindingSelectionSnapshot(
            version,
            AssemblyBindingSelection.RequireComposition(domain));

        Assert.Same(version, snapshot.Version);
        Assert.Same(
            domain,
            Assert.IsType<
                AssemblyBindingSelection.CompositionRequired>(
                    snapshot.Selection).Domain);
    }

    [Fact]
    public void Finalize_SingletonPreservesDecisionAndCompletePartition()
    {
        ResolvedAssemblyReference first = Descriptor("First");
        ResolvedAssemblyReference selected = Descriptor("Selected");
        ResolvedAssemblyReference third = Descriptor("Third");
        AssemblyBindingCandidateDomain domain =
            AssemblyBindingCandidateDomain.Create(
                [first, selected, third]);

        var result = Assert.IsType<AssemblyBindingSelection.Selected>(
            domain.Finalize([selected]));

        Assert.Same(selected, result.Assembly);
        Assert.Equal([first, third], result.ShadowedAssemblies);
    }

    [Fact]
    public void Finalize_AmbiguityUsesDomainOrderForBothProjections()
    {
        ResolvedAssemblyReference first = Descriptor("First");
        ResolvedAssemblyReference inactive = Descriptor("Inactive");
        ResolvedAssemblyReference third = Descriptor("Third");
        AssemblyBindingCandidateDomain domain =
            AssemblyBindingCandidateDomain.Create(
                [first, inactive, third]);

        var result = Assert.IsType<AssemblyBindingSelection.Ambiguous>(
            domain.Finalize([third, first]));

        Assert.Equal([first, third], result.Assemblies);
        Assert.Equal([inactive], result.ShadowedAssemblies);
    }

    [Fact]
    public void Finalize_SelectedOccurrencePreservesResolverLineage()
    {
        ResolvedAssemblyReference inactive = Descriptor("Inactive");
        ResolvedAssemblyReference selected = Descriptor("Selected");
        AssemblyBindingCandidateDomain domain =
            AssemblyBindingCandidateDomain.Create(
                [inactive, selected]);
        var lineage = new TestLineage(
            new AssemblyBindingPolicyVersion());
        AssemblyBindingOccurrence occurrence =
            lineage.Issue(selected);

        var result = Assert.IsType<AssemblyBindingSelection.Selected>(
            domain.Finalize(occurrence));

        Assert.Same(occurrence, result.Occurrence);
        Assert.Equal([inactive], result.ShadowedAssemblies);
    }

    [Fact]
    public void Finalize_RejectsEmptyDuplicateForeignAndSubstitutedDecisions()
    {
        ResolvedAssemblyReference first = Descriptor(
            "First",
            path: "First.dll");
        ResolvedAssemblyReference second = Descriptor("Second");
        ResolvedAssemblyReference foreign = Descriptor("Foreign");
        ResolvedAssemblyReference substituted =
            first.WithoutLocalPath();
        AssemblyBindingCandidateDomain domain =
            AssemblyBindingCandidateDomain.Create([first, second]);

        AssemblyBindingSelection[] invalid =
        [
            domain.Finalize(
                default(
                    ImmutableArray<ResolvedAssemblyReference>)),
            domain.Finalize([]),
            domain.Finalize([first, first]),
            domain.Finalize([foreign]),
            domain.Finalize([substituted]),
        ];

        Assert.All(invalid, selection =>
            Assert.Equal(
                AssemblyBindingFailureKind.InvalidCompositionResult,
                Assert.IsType<AssemblyBindingSelection.Rejected>(
                    selection).Failure.Kind));
    }

    [Fact]
    public void TerminalFactoriesCannotMintInactiveShadowEvidence()
    {
        ResolvedAssemblyReference first = Descriptor("First");
        ResolvedAssemblyReference second = Descriptor("Second");

        Assert.Empty(
            Assert.IsType<AssemblyBindingSelection.Selected>(
                AssemblyBindingSelection.Found(first))
                .ShadowedAssemblies);
        Assert.Empty(
            Assert.IsType<AssemblyBindingSelection.Ambiguous>(
                AssemblyBindingSelection.Multiple([first, second]))
                .ShadowedAssemblies);

        MethodInfo found = Assert.Single(
            typeof(AssemblyBindingSelection)
                .GetMethods(BindingFlags.Public | BindingFlags.Static),
            method =>
                method.Name
                    == nameof(AssemblyBindingSelection.Found));
        MethodInfo foundOccurrence = Assert.Single(
            typeof(AssemblyBindingSelection)
                .GetMethods(BindingFlags.Public | BindingFlags.Static),
            method =>
                method.Name
                    == nameof(
                        AssemblyBindingSelection.FoundOccurrence));
        MethodInfo multiple = Assert.Single(
            typeof(AssemblyBindingSelection)
                .GetMethods(BindingFlags.Public | BindingFlags.Static),
            method =>
                method.Name
                    == nameof(AssemblyBindingSelection.Multiple));
        Assert.Single(found.GetParameters());
        Assert.Single(foundOccurrence.GetParameters());
        Assert.Single(multiple.GetParameters());
    }

    [Fact]
    public void RequestValidationPreservesHandoffUntilMetadataBoundary()
    {
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(
                new AssemblyReferenceIdentity(
                    "Candidate",
                    new Version(1, 0, 0, 0),
                    Culture: null,
                    PublicKeyToken: null)),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);
        AssemblyBindingSelection handoff =
            AssemblyBindingSelection.RequireComposition(
                AssemblyBindingCandidateDomain.Create(
                    [Descriptor("Candidate")]));

        Assert.Same(
            handoff,
            AssemblyBindingSelection.ValidateForRequest(
                request,
                handoff));
        Assert.Equal(
            AssemblyBindingFailureKind.InvalidCompositionResult,
            Assert.IsType<AssemblyBindingSelection.Rejected>(
                AssemblyBindingSelection.ValidateForMetadataRequest(
                    request,
                    handoff)).Failure.Kind);
    }

    [Fact]
    public void MetadataBoundaryRejectsUnfinalizedHandoffWithoutOpeningDomain()
    {
        int opens = 0;
        ResolvedAssemblyReference candidate = Descriptor(
            "Candidate",
            openRead: () =>
            {
                opens++;
                throw new InvalidOperationException(
                    "An unfinalized domain must not be opened.");
            });
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(candidate.Identity),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Any);
        var policy = new FixedPolicy(
            AssemblyBindingSelection.RequireComposition(
                AssemblyBindingCandidateDomain.Create([candidate])));
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            policy,
            roots: [],
            bindingRequests: [request],
            requests: []);

        var rejected = Assert.IsType<AssemblyBindingOutcome.Rejected>(
            context.Bind(request));

        Assert.Equal(
            AssemblyBindingFailureKind.InvalidCompositionResult,
            rejected.Failure.Kind);
        Assert.Equal(0, opens);
    }

    static ResolvedAssemblyReference Descriptor(
        string name,
        string? path = null,
        Func<Stream>? openRead = null) =>
        ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                name,
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path,
            openRead
                ?? (() => throw new InvalidOperationException(
                    "Candidate domains do not open descriptors.")),
            AssemblyResolutionProvenance.Local(
                "binding composition currency test"));

    sealed class FixedPolicy(
        AssemblyBindingSelection selection) : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(Version, selection);
    }

    sealed record TestLineage : AssemblyBindingLineage
    {
        internal TestLineage(
            AssemblyBindingPolicyVersion version)
            : base(version)
        {
        }

        internal AssemblyBindingOccurrence Issue(
            ResolvedAssemblyReference assembly) =>
            CreateOccurrence(assembly);
    }
}
