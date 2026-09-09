using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

public sealed class ClosedWorldAssemblyGroupBindingPolicyTests
{
    [Fact]
    public void CreateClosedWorld_DelegateReceivesCanonicalRetainedSeedImage()
    {
        var root = new RetainedImage(FixtureCatalog.ServicesRouteLearningConsumer.AssemblyPath());
        var select = new Policy(request =>
        {
            var origin = Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(request.Origin);
            Assert.Same(root.Retained, origin.Assembly);
            Assert.Null(origin.Occurrence);
            using Stream image = origin.Assembly.OpenRead();
            Assert.True(image.Length > 0);
            return AssemblyBindingSelection.NameNotOwned();
        });
        var policy = SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld([(root.Retained, select)]);

        Assert.IsType<AssemblyBindingSelection.Missing>(policy.Select(
            Request(Named("Missing").Identity, AssemblyBindingOrigin.FromAssembly(root.Source))).Selection);
        Assert.Equal(1, select.Selections);
        Assert.Equal(0, root.SourceOpens);
    }

    [Fact]
    public void CreateClosedWorld_PreservesVersionPolicyAndCanonicalResolverLineage()
    {
        var root = Named("Root");
        var peer = new RetainedImage(FixtureCatalog.ServicesRouteLearningConsumer.AssemblyPath());
        var terminal = Named("Terminal");
        var rootPolicy = new Policy(_ => AssemblyBindingSelection.Found(peer.Source));
        var peerPolicy = new Policy(request =>
        {
            Assert.Same(peer.Retained,
                Assert.IsType<AssemblyBindingOrigin.RequestingAssembly>(request.Origin).Assembly);
            return AssemblyBindingSelection.Found(terminal);
        });
        var policy = SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld(
            [(root, rootPolicy), (peer.Retained, peerPolicy), (terminal, peerPolicy)]);
        Assert.Equal(0, rootPolicy.Selections);
        Assert.Equal(0, peerPolicy.Selections);

        var request = Request(Skewed(peer.Source.Identity), AssemblyBindingOrigin.FromAssembly(root));
        var peerSelection = Selected(policy, request);
        var repeated = Selected(policy, request);
        var terminalSelection = Selected(policy,
            Request(Skewed(terminal.Identity), AssemblyBindingOrigin.FromOccurrence(peerSelection.Occurrence)));

        Assert.Same(peer.Retained, peerSelection.Assembly);
        Assert.Same(terminal, terminalSelection.Assembly);
        Assert.Equal(peerSelection.Occurrence.Lineage, repeated.Occurrence.Lineage);
        Assert.NotEqual(AssemblyBindingLineage.Seed, peerSelection.Occurrence.Lineage);
        Assert.Same(policy.Version, terminalSelection.Occurrence.Lineage.Version);
        Assert.Equal(2, rootPolicy.Selections);
        Assert.Equal(1, peerPolicy.Selections);
        Assert.Equal(0, peer.SourceOpens);
    }

    [Theory]
    [InlineData("selected")]
    [InlineData("ambiguous")]
    [InlineData("shadow")]
    public void CreateClosedWorld_UsesRetainedImagesForEveryCandidateArm(string arm)
    {
        var root = Named("Root");
        var first = new RetainedImage(FixtureCatalog.ServicesRouteLearningConsumer.AssemblyPath());
        var second = new RetainedImage(FixtureCatalog.ServicesRouteLearningConsumer.AssemblyPath());
        var select = new Policy(_ => arm switch
        {
            "selected" => AssemblyBindingSelection.Found(first.Source),
            "ambiguous" => AssemblyBindingSelection.Multiple([first.Source, second.Source]),
            "shadow" => AssemblyBindingSelection.Found(first.Source, [second.Source]),
            _ => throw new ArgumentOutOfRangeException(nameof(arm)),
        });
        var policy = SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld(
            [(root, select), (first.Retained, select), (second.Retained, select)]);
        AssemblyBindingSelection selection = policy.Select(
            Request(Skewed(first.Source.Identity), AssemblyBindingOrigin.FromAssembly(root))).Selection;

        if (arm == "ambiguous")
        {
            Assert.Equal([first.Retained, second.Retained],
                Assert.IsType<AssemblyBindingSelection.Ambiguous>(selection).Assemblies);
        }
        else
        {
            var selected = Assert.IsType<AssemblyBindingSelection.Selected>(selection);
            Assert.Same(first.Retained, selected.Assembly);
            if (arm == "shadow")
                Assert.Same(second.Retained, Assert.Single(selected.ShadowedAssemblies));
        }
        using Stream image = first.Retained.OpenRead();
        Assert.True(image.Length > 0);
        Assert.Equal(0, first.SourceOpens);
        Assert.Equal(0, second.SourceOpens);
    }

    [Theory]
    [InlineData("selected")]
    [InlineData("ambiguous")]
    [InlineData("shadow")]
    public void CreateClosedWorld_RejectsEveryOutsideCandidateArmWithoutOpening(string arm)
    {
        var root = Named("Root");
        int opens = 0;
        var outside = ResolvedAssemblyReference.Create(
            new("Outside", new Version(1, 0, 0, 0), null, null), null,
            () =>
            {
                opens++;
                throw new InvalidOperationException("An omitted candidate must not open.");
            },
            AssemblyResolutionProvenance.Local("closed-world gate"));
        var select = new Policy(_ => arm switch
        {
            "selected" => AssemblyBindingSelection.Found(outside),
            "ambiguous" => AssemblyBindingSelection.Multiple([root, outside]),
            "shadow" => AssemblyBindingSelection.Found(root, [outside]),
            _ => throw new ArgumentOutOfRangeException(nameof(arm)),
        });
        var policy = SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld([(root, select)]);
        Assert.Equal(0, select.Selections);
        var result = Assert.IsType<AssemblyBindingSelection.Unavailable>(policy.Select(
            Request(outside.Identity, AssemblyBindingOrigin.FromAssembly(root))).Selection);
        Assert.Equal(AssemblyBindingFailureKind.CandidateUnavailable, result.Failure.Kind);
        Assert.Equal(1, select.Selections);
        Assert.Equal(0, opens);
    }

    [Theory]
    [InlineData("undifferentiated")]
    [InlineData("no-name-owner")]
    [InlineData("name-owned")]
    [InlineData("unavailable")]
    [InlineData("rejected")]
    public void CreateClosedWorld_PreservesTypedNonSelections(string arm)
    {
        var root = Named("Root");
        AssemblyBindingSelection expected = arm switch
        {
            "undifferentiated" => AssemblyBindingSelection.NotFound(),
            "no-name-owner" => AssemblyBindingSelection.NameNotOwned(),
            "name-owned" => AssemblyBindingSelection.NameOwnedButNoMatch(),
            "unavailable" => AssemblyBindingSelection.CannotSelect(
                new(AssemblyBindingFailureKind.IdentityPolicyRequired)),
            "rejected" => AssemblyBindingSelection.Invalid(
                new(AssemblyBindingFailureKind.InvalidPolicyResult)),
            _ => throw new ArgumentOutOfRangeException(nameof(arm)),
        };
        var select = new Policy(_ => expected);
        var policy = SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld([(root, select)]);

        Assert.Same(expected, policy.Select(
            Request(Named("Missing").Identity, AssemblyBindingOrigin.FromAssembly(root))).Selection);
    }

    [Fact]
    public void CreateClosedWorld_IntrinsicReadsCanonicalImageAndRejectsForeignOrigins()
    {
        var core = new RetainedImage(typeof(object).Assembly.Location);
        var policy = SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld(
            [(core.Retained, NoResolverAssemblyBindingPolicy.Instance)]);
        var selected = Selected(policy, new(
            AssemblyBindingTarget.CoreLibrary(), AssemblyBindingOrigin.FromAssembly(core.Source),
            AssemblyResolutionScope.Any));
        Assert.Same(core.Retained, selected.Assembly);
        Assert.Equal(0, core.SourceOpens);

        var rejected = Assert.IsType<AssemblyBindingSelection.Rejected>(policy.Select(new(
            AssemblyBindingTarget.CoreLibrary(), AssemblyBindingOrigin.FromAssembly(Named("Foreign")),
            AssemblyResolutionScope.Any)).Selection);
        Assert.Equal(AssemblyBindingFailureKind.InvalidBindingOrigin, rejected.Failure.Kind);
    }

    [Fact]
    public void CreateClosedWorld_RejectsForeignAndRetiredLineages()
    {
        var root = Named("Root");
        var peer = Named("Peer");
        var select = new Policy(_ => AssemblyBindingSelection.NameNotOwned());
        var policy = SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld([(root, select), (peer, select)]);
        var foreign = SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld([(root, select), (peer, select)]);
        var selected = Selected(policy, Request(peer.Identity, AssemblyBindingOrigin.FromAssembly(root)));
        var continuation = Request(root.Identity, AssemblyBindingOrigin.FromOccurrence(selected.Occurrence));

        Assert.Equal(AssemblyBindingFailureKind.InvalidBindingOrigin,
            Assert.IsType<AssemblyBindingSelection.Rejected>(foreign.Select(continuation).Selection).Failure.Kind);
        AssemblyBindingPolicyVersion version = policy.Version;
        select.ReplaceVersion();
        Assert.NotSame(version, policy.Version);
        Assert.Equal(AssemblyBindingFailureKind.InvalidBindingOrigin,
            Assert.IsType<AssemblyBindingSelection.Rejected>(policy.Select(continuation).Selection).Failure.Kind);
        Assert.Same(peer, Selected(policy, Request(peer.Identity, AssemblyBindingOrigin.FromAssembly(root))).Assembly);
    }

    [Fact]
    public void CreateClosedWorld_PreservesForeignSnapshotBeforeCandidateEffects()
    {
        var root = Named("Root");
        var outside = Named("Outside");
        var foreign = new AssemblyBindingSelectionSnapshot(new(), AssemblyBindingSelection.Found(outside));
        var select = new ForeignPolicy(foreign);
        var policy = SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld([(root, select)]);
        AssemblyBindingPolicyVersion version = policy.Version;

        Assert.Same(foreign, policy.Select(Request(outside.Identity, AssemblyBindingOrigin.FromAssembly(root))));
        Assert.NotSame(version, policy.Version);
    }

    static AssemblyBindingSelection.Selected Selected(
        IAssemblyBindingPolicy policy, AssemblyBindingRequest request) =>
        Assert.IsType<AssemblyBindingSelection.Selected>(policy.Select(request).Selection);

    static AssemblyBindingRequest Request(AssemblyReferenceIdentity target, AssemblyBindingOrigin origin) =>
        new(AssemblyBindingTarget.Reference(target), origin, AssemblyResolutionScope.Any);

    static AssemblyReferenceIdentity Skewed(AssemblyReferenceIdentity identity) =>
        new(identity.Name, new Version(99, 0, 0, 0), identity.Culture, identity.PublicKeyToken);

    static ResolvedAssemblyReference Named(string name) =>
        ResolvedAssemblyReference.Create(
            new(name, new Version(1, 0, 0, 0), null, null), null,
            () => throw new InvalidOperationException("Descriptor-only selection must not open an image."),
            AssemblyResolutionProvenance.Local("closed-world gate"));

    sealed class Policy(Func<AssemblyBindingRequest, AssemblyBindingSelection> select)
        : IAcquisitionFreeAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; private set; } = new();
        internal int Selections { get; private set; }

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            Selections++;
            return new(Version, select(request));
        }

        internal void ReplaceVersion() => Version = new();
    }

    sealed class ForeignPolicy(AssemblyBindingSelectionSnapshot snapshot)
        : IAcquisitionFreeAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();
        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request) => snapshot;
    }

    sealed class RetainedImage
    {
        internal RetainedImage(string fixture)
        {
            byte[] bytes = File.ReadAllBytes(fixture);
            using var pe = new PEReader(new MemoryStream(bytes, writable: false));
            Source = ResolvedAssemblyReference.Create(
                AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader()), null,
                () =>
                {
                    SourceOpens++;
                    throw new InvalidOperationException("Selection must use the canonical retained image.");
                },
                AssemblyResolutionProvenance.Local("closed-world retained gate"));
            var snapshot = Assert.IsType<AssemblyImageSnapshotResult.Ready>(
                AssemblyImageSnapshot.FromRetainedContent(Source, [.. bytes])).Snapshot;
            Retained = snapshot.RetainAssemblyReference(Source);
        }

        internal ResolvedAssemblyReference Source { get; }
        internal ResolvedAssemblyReference Retained { get; }
        internal int SourceOpens { get; private set; }
    }
}
