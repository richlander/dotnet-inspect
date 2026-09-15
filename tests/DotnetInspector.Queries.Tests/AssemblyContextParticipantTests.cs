using DotnetInspector.Services;
using ILInspector.Metadata;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextParticipantTests
{
    [Fact]
    public void OccurrenceRootedParticipant_TranslatesSeedAndContinuation()
    {
        ResolvedAssemblyReference root = Descriptor("Root");
        ResolvedAssemblyReference dependency = Descriptor("Dependency");
        var inner = new RecordingPolicy();
        AssemblyBindingOccurrence rootOccurrence =
            new TestLineage(inner.Version).Issue(root);
        AssemblyBindingOccurrence dependencyOccurrence =
            new TestLineage(inner.Version).Issue(dependency);
        inner.Selection = AssemblyBindingSelection.FoundOccurrence(
            dependencyOccurrence);
        var participant = new AssemblyContextParticipant(
            rootOccurrence,
            inner);

        Assert.NotSame(inner.Version, participant.BindingPolicy.Version);
        var first = Assert.IsType<AssemblyBindingSelection.Selected>(
            participant.BindingPolicy.Select(
                    Request(AssemblyBindingOrigin.FromAssembly(root)))
                .Selection);
        Assert.Same(
            rootOccurrence,
            Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(
                    inner.LastRequest!.Origin)
                .Occurrence);

        participant.BindingPolicy.Select(
            Request(
                AssemblyBindingOrigin.FromOccurrence(
                    first.Occurrence)));
        Assert.Same(
            dependencyOccurrence,
            Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(
                    inner.LastRequest!.Origin)
                .Occurrence);
    }

    [Fact]
    public void OccurrenceRootedParticipant_RejectsAnotherSeed()
    {
        ResolvedAssemblyReference root = Descriptor("Root");
        ResolvedAssemblyReference other = Descriptor("Other");
        var inner = new RecordingPolicy();
        var participant = new AssemblyContextParticipant(
            new TestLineage(inner.Version).Issue(root),
            inner);

        AssemblyBindingSelectionSnapshot snapshot =
            participant.BindingPolicy.Select(
                Request(AssemblyBindingOrigin.FromAssembly(other)));

        var rejected =
            Assert.IsType<AssemblyBindingSelection.Rejected>(
                snapshot.Selection);
        Assert.Equal(
            AssemblyBindingFailureKind.InvalidBindingOrigin,
            rejected.Failure.Kind);
        Assert.Equal(0, inner.SelectionCount);
    }

    [Fact]
    public void OccurrenceRootedParticipants_ComposeUnderOneRoutingVersion()
    {
        ResolvedAssemblyReference root = Descriptor("Root");
        ResolvedAssemblyReference dependency = Descriptor("Dependency");
        var inner = new RecordingPolicy();
        var lineage = new TestLineage(inner.Version);
        AssemblyBindingOccurrence rootOccurrence = lineage.Issue(root);
        AssemblyBindingOccurrence dependencyOccurrence =
            lineage.Issue(dependency);
        inner.Selection = AssemblyBindingSelection.NotFound();
        var rootRoute = new AssemblyContextParticipant(
            rootOccurrence,
            inner);
        var dependencyRoute = new AssemblyContextParticipant(
            dependencyOccurrence,
            inner);
        var policy =
            SourceRelativeAssemblyGroupBindingPolicy.CreateRoutingOnly(
                [
                    (rootRoute.Assembly, rootRoute.BindingPolicy),
                    (dependencyRoute.Assembly,
                        dependencyRoute.BindingPolicy),
                ]);
        var rootParticipant = new AssemblyContextParticipant(
            root,
            policy);
        var dependencyParticipant = new AssemblyContextParticipant(
            dependency,
            policy);

        policy.Select(
            Request(
                AssemblyBindingOrigin.FromAssembly(dependency)));

        Assert.Same(
            dependencyOccurrence,
            Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(
                    inner.LastRequest!.Origin)
                .Occurrence);
        Assert.Same(
            rootParticipant.BindingPolicy.Version,
            dependencyParticipant.BindingPolicy.Version);
    }

    [Fact]
    public void OccurrenceRootedParticipant_PreservesRoutingCompositionContinuation()
    {
        ResolvedAssemblyReference fallback = Descriptor("Fallback");
        ResolvedAssemblyReference owner = Descriptor("Owner");
        ResolvedAssemblyReference selected = Descriptor(
            "Platform.Library",
            AssemblyResolutionProvenance.Designated("selected overlay"));
        ResolvedAssemblyReference dependency = Descriptor("Dependency");
        var missing = new SelectingPolicy(_ =>
            AssemblyBindingSelection.NameNotOwned());
        var selecting = new SelectingPolicy(request =>
            request.Target is AssemblyBindingTarget.AssemblyReference
                { Identity.Name: "Platform.Library" }
                ? AssemblyBindingSelection.RequireComposition(
                    AssemblyBindingCandidateDomain.Create([selected]))
                : AssemblyBindingSelection.Found(dependency));
        IAssemblyBindingPolicy routing =
            SourceRelativeAssemblyGroupBindingPolicy.CreateRoutingOnly(
                [
                    (fallback, (IAssemblyBindingPolicy)missing),
                    (owner, (IAssemblyBindingPolicy)selecting),
                ]);
        IAssemblyBindingPolicy participant =
            new AssemblyContextParticipant(
                AssemblyBindingOccurrence.Seed(owner),
                routing)
                .BindingPolicy;
        var outer = new SourceRelativeAssemblyGroupBindingPolicy(
            [
                (fallback, participant),
                (owner, participant),
            ]);

        var first = Assert.IsType<AssemblyBindingSelection.Selected>(
            outer.Select(
                    Request(
                        selected,
                        AssemblyBindingOrigin.FromAssembly(owner)))
                .Selection);
        var continued = Assert.IsType<AssemblyBindingSelection.Selected>(
            outer.Select(
                    Request(
                        dependency,
                        AssemblyBindingOrigin.FromOccurrence(
                            first.Occurrence)))
                .Selection);

        Assert.Same(selected, first.Assembly);
        Assert.Same(dependency, continued.Assembly);
    }

    [Fact]
    public void OccurrenceRootedParticipant_PreservesGlobalCompositionContinuation()
    {
        ResolvedAssemblyReference owner = Descriptor("Owner");
        ResolvedAssemblyReference selected = Descriptor(
            "Selected",
            AssemblyResolutionProvenance.Designated("selected overlay"));
        ResolvedAssemblyReference dependency = Descriptor("Dependency");
        var selecting = new SelectingPolicy(request =>
            request.Target is AssemblyBindingTarget.AssemblyReference
                { Identity.Name: "Selected" }
                ? AssemblyBindingSelection.RequireComposition(
                    AssemblyBindingCandidateDomain.Create([selected]))
                : AssemblyBindingSelection.Found(dependency));
        IAssemblyBindingPolicy routing =
            SourceRelativeAssemblyGroupBindingPolicy.CreateRoutingOnly(
                [(owner, (IAssemblyBindingPolicy)selecting)]);
        IAssemblyBindingPolicy participant =
            new AssemblyContextParticipant(
                AssemblyBindingOccurrence.Seed(owner),
                routing)
                .BindingPolicy;
        var outer = new SourceRelativeAssemblyGroupBindingPolicy(
            [(owner, participant)]);

        var first = Assert.IsType<AssemblyBindingSelection.Selected>(
            outer.Select(
                    Request(
                        selected,
                        AssemblyBindingOrigin.Global()))
                .Selection);
        var continued = Assert.IsType<AssemblyBindingSelection.Selected>(
            outer.Select(
                    Request(
                        dependency,
                        AssemblyBindingOrigin.FromOccurrence(
                            first.Occurrence)))
                .Selection);

        Assert.Same(selected, first.Assembly);
        Assert.Same(dependency, continued.Assembly);
    }

    [Fact]
    public void OccurrenceRootedParticipant_RealPackageTopologyPreservesGlobalCompositionContinuation()
    {
        const string packageVersion = "11.0.0-preview.7.26381.103";
        ResolvedAssemblyReference owner = RealAsset(
            "package",
            "System.Memory.Data.dll",
            AssemblyResolutionProvenance.Package(
                "System.Memory.Data",
                packageVersion,
                "net10.0",
                rid: null));
        ResolvedAssemblyReference selectedJson = RealAsset(
            "package",
            "System.Text.Json.dll",
            AssemblyResolutionProvenance.Designated(
                $"System.Text.Json@{packageVersion} package compile asset"));
        ResolvedAssemblyReference platformJson = RealAsset(
            "platform",
            "System.Text.Json.dll",
            AssemblyResolutionProvenance.Platform(
                "Microsoft.NETCore.App.Ref",
                packageVersion,
                "nuget.org reference pack"));
        ResolvedAssemblyReference selectedEncoding = RealAsset(
            "package",
            "System.Text.Encodings.Web.dll",
            AssemblyResolutionProvenance.Designated(
                $"System.Text.Encodings.Web@{packageVersion} package compile asset"));
        ResolvedAssemblyReference platformEncoding = RealAsset(
            "platform",
            "System.Text.Encodings.Web.dll",
            AssemblyResolutionProvenance.Platform(
                "Microsoft.NETCore.App.Ref",
                packageVersion,
                "nuget.org reference pack"));

        AssemblyReferenceIdentity jsonReference =
            Reference(owner, selectedJson.Identity.Name);
        AssemblyReferenceIdentity encodingReference =
            Reference(selectedJson, selectedEncoding.Identity.Name);
        Assert.True(jsonReference.IsEquivalentTo(selectedJson.Identity));
        Assert.True(encodingReference.IsEquivalentTo(selectedEncoding.Identity));
        Assert.True(selectedJson.Identity.IsEquivalentTo(platformJson.Identity));
        Assert.True(
            selectedEncoding.Identity.IsEquivalentTo(
                platformEncoding.Identity));
        Assert.NotEqual(ModuleVersionId(selectedJson), ModuleVersionId(platformJson));
        Assert.NotEqual(
            ModuleVersionId(selectedEncoding),
            ModuleVersionId(platformEncoding));

        var selecting = new SelectingPolicy(request =>
        {
            if (request.Target is not AssemblyBindingTarget.AssemblyReference target)
                return AssemblyBindingSelection.NameNotOwned();

            if (target.Identity.IsEquivalentTo(jsonReference))
            {
                return AssemblyBindingSelection.RequireComposition(
                    AssemblyBindingCandidateDomain.Create(
                        [platformJson, selectedJson]));
            }

            return target.Identity.IsEquivalentTo(encodingReference)
                ? AssemblyBindingSelection.RequireComposition(
                    AssemblyBindingCandidateDomain.Create(
                        [selectedEncoding, platformEncoding]))
                : AssemblyBindingSelection.NameNotOwned();
        });
        IAssemblyBindingPolicy routing =
            SourceRelativeAssemblyGroupBindingPolicy.CreateRoutingOnly(
                [(owner, (IAssemblyBindingPolicy)selecting)]);
        IAssemblyBindingPolicy participant =
            new AssemblyContextParticipant(
                AssemblyBindingOccurrence.Seed(owner),
                routing)
                .BindingPolicy;
        var outer = new SourceRelativeAssemblyGroupBindingPolicy(
            [(owner, participant)]);

        var first = Assert.IsType<AssemblyBindingSelection.Selected>(
            outer.Select(
                    Request(
                        selectedJson,
                        AssemblyBindingOrigin.Global()))
                .Selection);
        var continued = Assert.IsType<AssemblyBindingSelection.Selected>(
            outer.Select(
                    Request(
                        selectedEncoding,
                        AssemblyBindingOrigin.FromOccurrence(
                            first.Occurrence)))
                .Selection);

        Assert.Same(selectedJson, first.Assembly);
        Assert.Same(platformJson, Assert.Single(first.ShadowedAssemblies));
        Assert.Same(selectedEncoding, continued.Assembly);
        Assert.Same(
            platformEncoding,
            Assert.Single(continued.ShadowedAssemblies));
    }

    static AssemblyBindingRequest Request(
        AssemblyBindingOrigin origin) =>
        new(
            AssemblyBindingTarget.Reference(
                new AssemblyReferenceIdentity(
                    "Dependency",
                    new Version(1, 0, 0, 0),
                    null,
                    null)),
            origin,
            AssemblyResolutionScope.Any);

    static AssemblyBindingRequest Request(
        ResolvedAssemblyReference target,
        AssemblyBindingOrigin origin) =>
        new(
            AssemblyBindingTarget.Reference(target.Identity),
            origin,
            AssemblyResolutionScope.Any);

    static ResolvedAssemblyReference Descriptor(
        string name,
        AssemblyResolutionProvenance? provenance = null) =>
        ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                name,
                new Version(1, 0, 0, 0),
                null,
                null),
            path: null,
            static () => new MemoryStream(),
            provenance ?? AssemblyResolutionProvenance.Local("test"));

    static ResolvedAssemblyReference RealAsset(
        string role,
        string fileName,
        AssemblyResolutionProvenance provenance) =>
        ResolvedAssemblyReference.CreateFromPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "BindingComposition",
                role,
                fileName),
            provenance);

    static AssemblyReferenceIdentity Reference(
        ResolvedAssemblyReference assembly,
        string name)
    {
        using Stream stream = assembly.OpenRead();
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        return reader.AssemblyReferences
            .Select(handle => AssemblyReferenceIdentity.From(reader, handle))
            .Single(reference => string.Equals(
                reference.Name,
                name,
                StringComparison.Ordinal));
    }

    static Guid ModuleVersionId(ResolvedAssemblyReference assembly)
    {
        using Stream stream = assembly.OpenRead();
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid);
    }

    sealed class RecordingPolicy : IAssemblyBindingPolicy
    {
        internal AssemblyBindingSelection Selection { get; set; } =
            AssemblyBindingSelection.NotFound();
        internal AssemblyBindingRequest? LastRequest { get; private set; }
        internal int SelectionCount { get; private set; }

        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            LastRequest = request;
            SelectionCount++;
            return new(Version, Selection);
        }
    }

    sealed class SelectingPolicy(
        Func<AssemblyBindingRequest, AssemblyBindingSelection> select)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(Version, select(request));
    }

    sealed record TestLineage(
        AssemblyBindingPolicyVersion Version)
        : AssemblyBindingLineage(Version)
    {
        internal AssemblyBindingOccurrence Issue(
            ResolvedAssemblyReference assembly) =>
            CreateOccurrence(assembly);
    }
}
