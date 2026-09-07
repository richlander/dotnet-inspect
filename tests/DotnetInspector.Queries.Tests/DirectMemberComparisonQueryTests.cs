using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;

namespace DotnetInspector.Queries.Tests;

public sealed class DirectMemberComparisonQueryTests
{
    [Theory]
    [InlineData("ConstantValue")]
    [InlineData("Stable")]
    public void DirectMemberComparison_PreservesDesignatedPair(string afterName)
    {
        using var fixture = new Fixture(Image());
        DirectMemberComparisonEndpoint before = fixture.Endpoint("Stable");
        DirectMemberComparisonEndpoint after = fixture.Endpoint(afterName);

        LocalComparisonQueryResult.Published result = Published(
            Compare(fixture.Group,
                new(before, after, ResearchProducerCatalog.Kinds)));
        ResearchProducerCompletion completion = Completed(result);
        LocalComparisonQueryIdentity identity = Assert.IsType<LocalComparisonQueryIdentity>(result.Identity);
        QueryToResearchPopulationReceipt receipt = Assert.IsType<QueryToResearchPopulationReceipt>(result.Receipt);

        Assert.NotSame(identity.Before, identity.After);
        Assert.Equal(QueryComparisonSide.Before, identity.Before.Side);
        Assert.Equal(QueryComparisonSide.After, identity.After.Side);
        Assert.Same(identity.Operation, identity.Question.Operation);
        Assert.Same(identity.Question, identity.Before.Question);
        Assert.Same(identity.Question, identity.After.Question);
        Assert.Same(receipt.Operation.Research, completion.Operation);
        Assert.Same(identity.Operation, receipt.Operation.Query);
        Assert.Equal(2, completion.Results.Length);
        Assert.All(completion.WorkItems, item =>
        {
            ResearchDesignatedPair pair =
                Assert.IsType<ResearchProducerWorkBasis.DesignatedPair>(item.Basis).Pair;
            Assert.Same(receipt.Questions[identity.Question], pair.Question);
            Assert.Same(receipt.Inputs[identity.Before].Research, pair.Before.Request.Input);
            Assert.Same(receipt.Inputs[identity.After].Research, pair.After.Request.Input);
            Assert.Equal(before.Address, Target(pair.Before).Address);
            Assert.Equal(after.Address, Target(pair.After).Address);
            Assert.Contains("Stable", Target(pair.Before).Anchor.CanonicalSignature);
            Assert.Contains(afterName, Target(pair.After).Anchor.CanonicalSignature);
        });
        var nativePair = Assert.IsType<ResearchProducerWorkBasis.DesignatedPair>(
            completion.WorkItems[0].Basis).Pair;
        var csharp = Assert.IsType<ResearchProducerWorkOutcome.ProducedCSharp>(
            completion.Results[0].Outcome).Result;
        var il = Assert.IsType<ResearchProducerWorkOutcome.ProducedIlBody>(
            completion.Results[1].Outcome).Result;
        Assert.Equal(Target(nativePair.Before).Anchor.CanonicalSignature, csharp.Old.Key);
        Assert.Equal(Target(nativePair.After).Anchor.CanonicalSignature, csharp.New.Key);
        Assert.Equal(csharp.Old.Key, il.Old.Identity);
        Assert.Equal(csharp.New.Key, il.New.Identity);
        Assert.All(completion.Cleanup, item =>
            Assert.IsType<ResearchProducerCleanupOutcome.Succeeded>(item));
    }

    [Fact]
    public void DirectMemberComparison_DoesNotSubstituteEndpoint()
    {
        using var fixture = new Fixture(Image(), Image(beforeVersion: true));
        DirectMemberComparisonEndpoint selected = fixture.Endpoint("Stable");
        DirectMemberComparisonEndpoint wrongImage = fixture.Endpoint("Stable", 1);
        Assert.Equal(selected.Address!.Value.Token, wrongImage.Address!.Value.Token);
        Assert.NotEqual(selected.Address, wrongImage.Address);

        var wrong = Assert.IsType<LocalComparisonQueryResult.NonSuccess>(
            Compare(fixture.Group,
                new(selected with { Address = wrongImage.Address }, selected, ResearchProducerCatalog.Kinds)));
        Assert.Equal(QueryComparisonSide.Before, wrong.Side);
        Assert.NotNull(wrong.Identity);
        Assert.NotNull(wrong.Receipt);
        ResearchDesignatedPairUnavailable unavailable = Assert.Single(
            Assert.IsType<LocalComparisonQueryFailure.DesignationUnavailable>(wrong.Failure)
                .Cause.Endpoints);
        Assert.Equal(ResearchTargetDiagnosticKind.AddressEvidenceMismatch,
            Assert.IsType<ResearchTargetOutcome.Failed>(unavailable.Attempt.Outcome).Diagnostic.Kind);

        var missing = Assert.IsType<LocalComparisonQueryResult.NonSuccess>(
            Compare(fixture.Group,
                new(selected, selected with
                {
                    Address = selected.Address.Value with
                    {
                        Handle = MetadataTokens.MethodDefinitionHandle(0x00ffffff),
                    },
                }, ResearchProducerCatalog.Kinds)));
        Assert.Equal(QueryComparisonSide.After, missing.Side);
        Assert.Equal(DirectMemberDesignationFailureKind.MissingMethod,
            Assert.IsType<LocalComparisonQueryFailure.InvalidDesignation>(missing.Failure).Kind);
        Assert.NotNull(missing.Identity);
        Assert.NotNull(missing.Receipt);
    }

    [Fact]
    public void DirectMemberComparison_RetainsNativeNonSuccess()
    {
        using var bodyless = new Fixture(Image(beforeVersion: true));
        ResearchProducerCompletion completion = Completed(Published(
            Compare(bodyless.Group,
                new(bodyless.Endpoint("BodyState"), bodyless.Endpoint("Stable"),
                    ResearchProducerCatalog.Kinds))));
        var csharp = Assert.IsType<ResearchProducerWorkOutcome.ProducedCSharp>(
            completion.Results[0].Outcome).Result;
        var il = Assert.IsType<ResearchProducerWorkOutcome.ProducedIlBody>(
            completion.Results[1].Outcome).Result;
        Assert.Null(csharp.BodyDiff);
        Assert.Null(il.MemberDiff);
        Assert.Equal(FindingInspectionState.NoApplicableInput,
            Assert.IsType<FindingComparison<CSharpCanonicalLine>.Complete>(
                csharp.Findings.Value).Transition.Old);
        Assert.Equal(FindingInspectionState.NoApplicableInput,
            Assert.IsType<FindingComparison<CanonicalIlOperation>.Complete>(
                il.Findings.Value).Transition.Old);

        byte[] broken = Image();
        BreakBody(broken, "Stable");
        using var failing = new Fixture(broken);
        ResearchProducerCompletion failed = Completed(Published(
            Compare(failing.Group,
                new(failing.Endpoint("Stable"), failing.Endpoint("ConstantValue"),
                    ResearchProducerCatalog.Kinds))));
        var failedCSharp = Assert.IsType<ResearchProducerWorkOutcome.ProducedCSharp>(
            failed.Results[0].Outcome).Result;
        var failedIl = Assert.IsType<ResearchProducerWorkOutcome.ProducedIlBody>(
            failed.Results[1].Outcome).Result;
        Assert.IsType<FindingComparison<CSharpCanonicalLine>.Failed>(failedCSharp.Findings.Value);
        Assert.IsType<FindingComparison<CanonicalIlOperation>.Failed>(failedIl.Findings.Value);
        Assert.Null(failedCSharp.BodyDiff);
        Assert.Null(failedIl.MemberDiff);
    }

    [Fact]
    public void DirectMemberComparison_UsesSharedPublication()
    {
        using var fixture = new Fixture(Image());
        var request = new DirectMemberComparisonRequest(
            fixture.Endpoint("Stable"), fixture.Endpoint("Stable"), [ResearchProducerKind.IlBody]);
        LocalComparisonQueryResult result = Compare(fixture.Group, request);
        ResearchProducerCompletion completion = Completed(Published(result));
        Assert.Single(completion.Results);
        Assert.IsType<ResearchProducerWorkOutcome.ProducedIlBody>(completion.Results.Single().Outcome);
        Assert.NotNull(result.Receipt);
    }

    [Theory]
    [InlineData("get_Value", ResearchTargetRelationshipRole.Getter)]
    [InlineData(".ctor", ResearchTargetRelationshipRole.Method)]
    [InlineData("op_Implicit", ResearchTargetRelationshipRole.Method)]
    [InlineData("GenericIdentity", ResearchTargetRelationshipRole.Method)]
    public void DirectMemberComparison_PreservesPhysicalAccessorAndMethodRoles(
        string name,
        ResearchTargetRelationshipRole expectedRole)
    {
        using var fixture = new Fixture(Image());
        DirectMemberComparisonEndpoint endpoint = fixture.Endpoint(name);
        ResearchProducerCompletion completion = Completed(Published(
            Compare(fixture.Group,
                new(endpoint, endpoint, ResearchProducerCatalog.Kinds))));
        ResearchDesignatedPair pair = Assert.IsType<ResearchProducerWorkBasis.DesignatedPair>(
            completion.WorkItems[0].Basis).Pair;
        Assert.Equal(endpoint.Address, Target(pair.Before).Address);
        Assert.Equal(endpoint.Address, Target(pair.After).Address);
        Assert.Equal(expectedRole, Target(pair.Before).Role);
        Assert.Equal(expectedRole, Target(pair.After).Role);
        Assert.All(completion.Results, result => Assert.True(
            result.Outcome is ResearchProducerWorkOutcome.ProducedCSharp
                or ResearchProducerWorkOutcome.ProducedIlBody));
    }

    [Theory]
    [InlineData("GenericTypeAritySample`1")]
    [InlineData("GenericTypeAritySample`2")]
    [InlineData("Inner`1")]
    public void DirectMemberComparison_PreservesGenericDeclaringTypes(string declaringType)
    {
        using var fixture = new Fixture(Image());
        DirectMemberComparisonEndpoint endpoint = fixture.Endpoint("M", declaringType: declaringType);
        ResearchProducerCompletion completion = Completed(Published(
            Compare(fixture.Group, new(endpoint, endpoint, ResearchProducerCatalog.Kinds))));
        AssertExactPhysicalPair(completion, endpoint.Address);
    }

    [Theory]
    [InlineData("<SharedLambdaOrdinalOwner>b__")]
    [InlineData("<CallsThroughLocalFunction>g__Core|")]
    public void DirectMemberComparison_PreservesCompilerGeneratedMethods(string prefix)
    {
        byte[] image = File.ReadAllBytes(typeof(ClassicAsyncSiblingFixture).Assembly.Location);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        string name = reader.MethodDefinitions
            .Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name))
            .Single(name => name.StartsWith(prefix, StringComparison.Ordinal));
        using var fixture = new Fixture(image);
        DirectMemberComparisonEndpoint endpoint = fixture.Endpoint(name);
        ResearchProducerCompletion completion = Completed(Published(
            Compare(fixture.Group, new(endpoint, endpoint, ResearchProducerCatalog.Kinds))));
        AssertExactPhysicalPair(completion, endpoint.Address);
    }

    static void AssertExactPhysicalPair(
        ResearchProducerCompletion completion,
        MetadataMethodAddress? address)
    {
        Assert.Equal(2, completion.Results.Length);
        Assert.All(completion.WorkItems, item =>
        {
            ResearchDesignatedPair pair = Assert.IsType<ResearchProducerWorkBasis.DesignatedPair>(item.Basis).Pair;
            Assert.Equal(address, Target(pair.Before).Address);
            Assert.Equal(address, Target(pair.After).Address);
        });
        var csharp = Assert.IsType<ResearchProducerWorkOutcome.ProducedCSharp>(completion.Results[0].Outcome).Result;
        Assert.NotNull(csharp.BodyDiff);
        Assert.True(csharp.BodyDiff.IsExact);
        var il = Assert.IsType<ResearchProducerWorkOutcome.ProducedIlBody>(completion.Results[1].Outcome).Result;
        Assert.NotNull(il.MemberDiff);
        Assert.True(il.MemberDiff.Diff.IsExact);
    }

    [Fact]
    public void LocalComparisonPublication_RetainsExactInvocation()
    {
        using var fixture = new Fixture(Image());
        var request = new DirectMemberComparisonRequest(
            fixture.Endpoint("Stable"), fixture.Endpoint("Stable"), ResearchProducerCatalog.Kinds);
        LocalComparisonQueryResult.Published first = Published(
            Compare(fixture.Group, request));
        LocalComparisonQueryResult.Published second = Published(
            Compare(fixture.Group, request));

        Assert.NotSame(first.Identity!.Operation, second.Identity!.Operation);
        Assert.NotSame(first.Identity.Question, second.Identity.Question);
        Assert.NotSame(first.Identity.Before, second.Identity.Before);
        Assert.NotSame(first.Identity.After, second.Identity.After);
        Assert.NotSame(Completed(first).Operation, Completed(second).Operation);
        Assert.NotSame(first.Receipt, second.Receipt);
        Assert.Equal(2, first.Receipt!.Inputs.Count);
        Assert.Equal(2, second.Receipt!.Inputs.Count);
        Assert.Same(first.Receipt.Operation.Research, Completed(first).Operation);
        Assert.Same(second.Receipt.Operation.Research, Completed(second).Operation);
    }

    [Fact]
    public void LocalComparisonPublication_PreservesQueryNonSuccess()
    {
        using var fixture = new Fixture(Image());
        DirectMemberComparisonEndpoint endpoint = fixture.Endpoint("Stable");
        var missing = Assert.IsType<LocalComparisonQueryResult.NonSuccess>(
            Compare(fixture.Group,
                new(endpoint with { Address = null }, endpoint, ResearchProducerCatalog.Kinds)));
        Assert.Null(missing.Identity);
        Assert.Null(missing.Receipt);
        Assert.Equal(QueryComparisonSide.Before, missing.Side);
        Assert.Equal(DirectMemberDesignationFailureKind.MissingAddress,
            Assert.IsType<LocalComparisonQueryFailure.InvalidDesignation>(missing.Failure).Kind);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = Assert.IsType<LocalComparisonQueryResult.NonSuccess>(
            DirectMemberComparisonQuery.Execute(fixture.Group,
                new(endpoint, endpoint, ResearchProducerCatalog.Kinds), cancellation.Token));
        Assert.Null(cancelled.Identity);
        Assert.Null(cancelled.Receipt);
        Assert.Null(cancelled.Side);
        Assert.Equal(cancellation.Token,
            Assert.IsType<LocalComparisonQueryFailure.Cancelled>(cancelled.Failure).Cause.CancellationToken);

        using var rejectedInput = new Fixture(Image(), maxImageBytes: 1);
        var rejectedEndpoint = new DirectMemberComparisonEndpoint(
            rejectedInput.Group.Participants.Single(), endpoint.Address);
        var rejected = Assert.IsType<LocalComparisonQueryResult.NonSuccess>(
            Compare(rejectedInput.Group,
                new(rejectedEndpoint, rejectedEndpoint, ResearchProducerCatalog.Kinds)));
        Assert.Null(rejected.Identity);
        Assert.Equal(QueryComparisonSide.Before, rejected.Side);
        Assert.Equal(CandidateOpenFailureKind.ResourceBudget,
            Assert.IsType<LocalComparisonQueryFailure.AccessRejected>(rejected.Failure).Cause.Kind);

        using var foreign = new Fixture(Image());
        var failed = Assert.IsType<LocalComparisonQueryResult.NonSuccess>(
            Compare(fixture.Group,
                new(endpoint, foreign.Endpoint("Stable"), ResearchProducerCatalog.Kinds)));
        Assert.Null(failed.Identity);
        Assert.Equal(QueryComparisonSide.After, failed.Side);
        Assert.IsType<ArgumentException>(
            Assert.IsType<LocalComparisonQueryFailure.Failed>(failed.Failure).Cause);
    }

    [Fact]
    public void LocalComparisonPublication_RemainsUsableAfterInputScopeCloses()
    {
        LocalComparisonQueryResult.Published result;
        LocalComparisonQueryResult.NonSuccess failure;
        using (var fixture = new Fixture(Image(), Image(beforeVersion: true)))
        {
            DirectMemberComparisonEndpoint endpoint = fixture.Endpoint("Stable");
            result = Published(Compare(fixture.Group,
                new(endpoint, endpoint, ResearchProducerCatalog.Kinds)));
            failure = Assert.IsType<LocalComparisonQueryResult.NonSuccess>(
                Compare(fixture.Group,
                    new(endpoint with { Address = fixture.Endpoint("Stable", 1).Address },
                        endpoint, ResearchProducerCatalog.Kinds)));
        }

        ResearchProducerCompletion completion = Completed(result);
        Assert.Same(result.Identity!.Operation, result.Identity.Before.Operation);
        Assert.Same(result.Receipt!.Operation.Research, completion.Operation);
        Assert.NotNull(Assert.IsType<ResearchProducerWorkOutcome.ProducedCSharp>(
            completion.Results[0].Outcome).Result.BodyDiff);
        Assert.NotNull(Assert.IsType<ResearchProducerWorkOutcome.ProducedIlBody>(
            completion.Results[1].Outcome).Result.MemberDiff);
        Assert.NotEmpty(completion.Cleanup);
        Assert.NotNull(failure.Identity);
        Assert.NotNull(failure.Receipt);
        Assert.IsType<ResearchTargetOutcome.Failed>(
            Assert.Single(Assert.IsType<LocalComparisonQueryFailure.DesignationUnavailable>(
                failure.Failure).Cause.Endpoints).Attempt.Outcome);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("rejected")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public void LocalComparisonPublication_PreservesTerminalEvidence(string terminal)
    {
        using var fixture = new PublicationFixture();
        using var cancellation = new CancellationTokenSource();
        if (terminal == "cancelled")
            cancellation.Cancel();
        fixture.FailCleanup = terminal == "failed";
        ResearchProducerKind[] producers = terminal == "rejected"
            ? [] : [ResearchProducerKind.CSharp, ResearchProducerKind.IlBody];
        LocalComparisonQueryResult.Published result = fixture.Publication.Run(
            new(fixture.Projected.Admission, fixture.Pair, producers), cancellation.Token);

        Assert.Same(fixture.Identity, result.Identity);
        Assert.Same(fixture.Projected.Receipt, result.Receipt);
        switch (terminal)
        {
            case "completed":
                ResearchProducerCompletion completion = Completed(result);
                Assert.Same(result.Receipt!.Operation.Research, completion.Operation);
                Assert.Same(fixture.Pair, Assert.IsType<ResearchProducerWorkBasis.DesignatedPair>(
                    completion.WorkItems[0].Basis).Pair);
                Assert.Same(completion.WorkItems[0], completion.Results[0].Item);
                Assert.Equal(2, completion.Results.Length);
                break;
            case "rejected":
                Assert.Equal(ResearchProducerRejectionKind.EmptyProducerSelection,
                    Assert.IsType<ResearchProducerSessionOutcome.Rejected>(result.Outcome).Rejection.Kind);
                break;
            case "failed":
                var failed = Assert.IsType<ResearchProducerSessionOutcome.Failed>(result.Outcome);
                Assert.Equal(ResearchProducerDiagnosticKind.CleanupFailed, failed.Diagnostic.Kind);
                Assert.NotEmpty(failed.Cleanup);
                Assert.All(failed.Cleanup, item =>
                    Assert.IsType<ResearchProducerCleanupOutcome.Failed>(item));
                break;
            case "cancelled":
                Assert.Empty(Assert.IsType<ResearchProducerSessionOutcome.Cancelled>(result.Outcome).Cleanup);
                break;
        }
    }

    [Fact]
    public void LocalComparisonPublication_RejectsAnotherProjectedPopulation()
    {
        using var first = new PublicationFixture();
        using var second = new PublicationFixture();
        Assert.Throws<ArgumentException>(() => first.Publication.Run(
            new(second.Projected.Admission, second.Pair, ResearchProducerCatalog.Kinds),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void DirectMemberComparison_RequiresExplicitNonemptyProducers()
    {
        using var fixture = new Fixture(Image());
        DirectMemberComparisonEndpoint endpoint = fixture.Endpoint("Stable");
        Assert.Throws<ArgumentException>(() => new DirectMemberComparisonRequest(endpoint, endpoint, []));
        Assert.Throws<ArgumentException>(() => new DirectMemberComparisonRequest(endpoint, endpoint,
            [ResearchProducerKind.CSharp, ResearchProducerKind.CSharp]));
        Assert.Throws<ArgumentException>(() => new DirectMemberComparisonRequest(endpoint, endpoint,
            [(ResearchProducerKind)99]));
    }

    static LocalComparisonQueryResult Compare(
        AssemblyContextGroup group,
        DirectMemberComparisonRequest request)
        => DirectMemberComparisonQuery.Execute(group, request, TestContext.Current.CancellationToken);

    static LocalComparisonQueryResult.Published Published(LocalComparisonQueryResult result)
        => Assert.IsType<LocalComparisonQueryResult.Published>(result);

    static ResearchProducerCompletion Completed(LocalComparisonQueryResult.Published result)
        => Assert.IsType<ResearchProducerSessionOutcome.Completed>(result.Outcome).Completion;

    static ResearchTargetOutcome.Resolved Target(ResearchTargetAttempt attempt)
        => Assert.IsType<ResearchTargetOutcome.Resolved>(attempt.Outcome);

    static byte[] Image(bool beforeVersion = false)
        => File.ReadAllBytes((beforeVersion ? FixtureCatalog.DiffV1 : FixtureCatalog.DiffV2).AssemblyPath());

    static int Token(byte[] image, string name, string? declaringType = null)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        return MetadataTokens.GetToken(reader.MethodDefinitions.First(handle =>
        {
            MethodDefinition method = reader.GetMethodDefinition(handle);
            return reader.GetString(method.Name) == name
                && (declaringType is null
                    || reader.GetString(reader.GetTypeDefinition(method.GetDeclaringType()).Name) == declaringType);
        }));
    }

    static void BreakBody(byte[] image, string name)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        int rva = reader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(Token(image, name))).RelativeVirtualAddress;
        SectionHeader section = pe.PEHeaders.SectionHeaders.Single(section =>
            rva >= section.VirtualAddress && rva < section.VirtualAddress + section.SizeOfRawData);
        image[section.PointerToRawData + rva - section.VirtualAddress] = 0;
    }

    sealed class Fixture : IDisposable
    {
        readonly InspectionWorkspace _workspace = new();
        readonly byte[][] _images;

        internal Fixture(params byte[][] images) : this(images, null) { }

        internal Fixture(byte[] image, long maxImageBytes) : this([image], maxImageBytes) { }

        Fixture(byte[][] images, long? maxImageBytes)
        {
            _images = images;
            var policy = new MissingBindingPolicy();
            Group = _workspace.CreateAssemblyContextGroup(
                images.Select(image => new AssemblyContextParticipant(Assembly(image), policy)),
                maxImageBytes is { } limit ? new() { MaxRetainedImageBytes = limit } : null);
        }

        internal AssemblyContextGroup Group { get; }

        internal DirectMemberComparisonEndpoint Endpoint(
            string name, int image = 0, string? declaringType = null)
        {
            AssemblyContextParticipant participant = Group.Participants[image];
            using var pe = new PEReader(new MemoryStream(_images[image], writable: false));
            MetadataMethodAddress address = MetadataMethodAddress.Create(
                pe.GetMetadataReader(),
                (MethodDefinitionHandle)MetadataTokens.EntityHandle(Token(_images[image], name, declaringType)));
            return new(participant, address);
        }

        public void Dispose() => _workspace.Dispose();
    }

    sealed class PublicationFixture : IDisposable
    {
        readonly LibraryBodyIndex _index;

        internal PublicationFixture()
        {
            byte[] image = Image();
            _index = LibraryBodyIndex.OpenFromPrefetchedImage(
                "fixture", [.. image], LibraryBodyAnalysisFeatures.MethodEvidence);
            var binding = new ImplementationComparisonBinding(
                Assembly(image, () => new CleanupStream(image, FailCleanup)), new NullResolver(), _index);
            var population = (QueryComparisonPopulation<ImplementationComparisonBinding>)
                Assert.IsType<QueryPopulationSealingOutcome.Sealed>(
                    QueryComparisonPopulationSealer.Execute(
                        new ImplementationComparisonPopulationRequest([binding], [binding]))).Population;
            Identity = new(population);
            Projected = Assert.IsType<QueryPopulationProjectionOutcome.Projected>(
                QueryPopulationProjection.Execute(population)).Population;
            var resolution = Assert.IsType<ResearchTargetPlanningOutcome.Planned>(
                ResearchTargetResolver.Resolve(new(Projected.Admission,
                    Projected.Admission.Inputs.Select(input =>
                        new ResearchTargetInputRoleAssignment(input, ResearchTargetInputRole.Implementation)),
                    [new ResearchCarriedMemberSelection(Projected.Receipt.Questions[Identity.Question],
                        "DiffFixtureSample.DiffSample", MemberTargetSelector.Parse("Stable"))]))).Resolution;
            Pair = Assert.IsType<ResearchDesignatedPairOutcome.Admitted>(
                ResearchDesignatedPairAdmission.Admit(Projected.Admission, resolution,
                    resolution.Attempts.Single(attempt => ReferenceEquals(attempt.Request.Input,
                        Projected.Receipt.Inputs[Identity.Before].Research)),
                    resolution.Attempts.Single(attempt => ReferenceEquals(attempt.Request.Input,
                        Projected.Receipt.Inputs[Identity.After].Research)))).Pair;
            Publication = new(Identity, Projected);
        }

        internal bool FailCleanup { get; set; }
        internal LocalComparisonQueryIdentity Identity { get; }
        internal ProjectedQueryPopulation Projected { get; }
        internal ResearchDesignatedPair Pair { get; }
        internal LocalComparisonPublication Publication { get; }
        public void Dispose() => _index.ReleaseCallGraphCaches();
    }

    static ResolvedAssemblyReference Assembly(byte[] image, Func<Stream>? open = null)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        return ResolvedAssemblyReference.Create(
            AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader()),
            path: null, open ?? (() => new MemoryStream(image, writable: false)),
            AssemblyResolutionProvenance.Local("direct-member-fixture"));
    }

    sealed class CleanupStream(byte[] image, bool failCleanup) : MemoryStream(image, writable: false)
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && failCleanup)
                throw new IOException("Fixture stream cleanup failed.");
        }
    }

    sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();
        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request) =>
            new(Version, AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(AssemblyBindingFailureKind.CandidateUnavailable)));
    }

    sealed class NullResolver : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity, AssemblyResolutionScope scope) => null;
    }
}
