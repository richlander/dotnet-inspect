using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Analysis.StructuralCloneFixtures;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextStructuralCloneRetrievalQueryTests
{
    [Fact]
    public void Execute_SameAssemblyExactMemberPreservesProductResult()
    {
        ImmutableArray<byte> image =
            Image(typeof(StructuralCloneFixture).Assembly.Location);
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        StructuralCloneQuerySeed.Member seed =
            MemberSeed(
                image,
                typeof(StructuralCloneFixture).FullName!,
                nameof(StructuralCloneFixture.ExactPositiveA));
        var input =
            new AssemblyContextStructuralCloneRetrievalInput(
                group,
                participant,
                group,
                participant,
                seed,
                new StructuralCloneQueryPopulation.Type(
                    TypeName(
                        typeof(StructuralCloneFixture)
                            .FullName!)));

        AssemblyContextStructuralCloneRetrievalResult result =
            Execute(input);

        var available = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Available>(
                result);
        Assert.Same(
            participant.Assembly.Registration,
            available.SeedSubject.Registration);
        Assert.Same(
            participant.Assembly.Registration,
            available.CandidateSubject.Registration);
        Assert.Same(
            input.Population,
            available.CandidatePopulation);
        Assert.Equal(
            StructuralCloneRetrievalDisposition.Completed,
            available.Retrieval.Disposition);
        StructuralCloneRetrievalCandidate exact =
            Candidate(
                image,
                available.Retrieval,
                nameof(StructuralCloneFixture.ExactPositiveB));
        StructuralCloneRetrievalCandidate closeNegative =
            Candidate(
                image,
                available.Retrieval,
                nameof(StructuralCloneFixture.EdgeRoleNegativeA));
        Assert.Equal(1, exact.Rank);
        Assert.Equal(10_000, exact.Similarity.Score);
        Assert.True(
            exact.Similarity.Score
                > closeNegative.Similarity.Score);

        using var directImage = new PEReader(image);
        MetadataReader reader = directImage.GetMetadataReader();
        StructuralCloneRetrievalResult expected =
            StructuralCloneAnalysis.RetrieveSimilar(
                directImage,
                Method(
                    reader,
                    typeof(StructuralCloneFixture).FullName!,
                    nameof(StructuralCloneFixture.ExactPositiveA)),
                Methods(
                    reader,
                    typeof(StructuralCloneFixture).FullName!));
        AssertProductResult(expected, available.Retrieval);
    }

    [Fact]
    public void Execute_CrossAssemblyTokenAndTypeScopePreserveProductResult()
    {
        ImmutableArray<byte> seedImage =
            Image(FixtureCatalog.DiffPair.OldAssemblyPath());
        ImmutableArray<byte> candidateImage =
            Image(FixtureCatalog.DiffPair.NewAssemblyPath());
        var seedPolicy = new TestBindingPolicy();
        var candidatePolicy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup seedGroup =
            Group(workspace, seedImage, seedPolicy);
        using AssemblyContextGroup candidateGroup =
            Group(workspace, candidateImage, candidatePolicy);
        AssemblyContextParticipant seedParticipant =
            Assert.Single(seedGroup.Participants);
        AssemblyContextParticipant candidateParticipant =
            Assert.Single(candidateGroup.Participants);
        int seedToken = MetadataTokens.GetToken(
            Method(
                seedImage,
                "DiffFixtureSample.DiffSample",
                "Stable"));
        var input =
            new AssemblyContextStructuralCloneRetrievalInput(
                seedGroup,
                seedParticipant,
                candidateGroup,
                candidateParticipant,
                new StructuralCloneQuerySeed
                    .MethodDefinitionToken(seedToken),
                new StructuralCloneQueryPopulation.Type(
                    TypeName("DiffFixtureSample.DiffSample")));

        AssemblyContextStructuralCloneRetrievalResult result =
            Execute(input);

        var available = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Available>(
                result);
        Assert.NotSame(
            available.SeedSubject.Registration,
            available.CandidateSubject.Registration);
        Assert.Equal(
            StructuralCloneRetrievalDisposition.Completed,
            available.Retrieval.Disposition);
        using var directSeed = new PEReader(seedImage);
        using var directCandidate = new PEReader(candidateImage);
        MetadataReader candidateReader =
            directCandidate.GetMetadataReader();
        StructuralCloneRetrievalResult expected =
            StructuralCloneAnalysis.RetrieveSimilar(
                directSeed,
                MetadataTokens.MethodDefinitionHandle(
                    MetadataTokens.GetRowNumber(
                        MetadataTokens.EntityHandle(seedToken))),
                directCandidate,
                Methods(
                    candidateReader,
                    "DiffFixtureSample.DiffSample"));
        AssertProductResult(expected, available.Retrieval);

        StructuralCloneRetrievalCandidate stable =
            Candidate(
                candidateImage,
                available.Retrieval,
                "Stable");
        Assert.Equal(10_000, stable.Similarity.Score);
        Assert.Equal(
            ContentMvid(candidateImage),
            stable.Method.ModuleVersionId);
        Assert.Equal(
            ContentMvid(seedImage),
            available.Retrieval.Seed.Method.ModuleVersionId);
    }

    [Fact]
    public void Execute_WholeAssemblyPopulationLeavesMethodLimitToProduct()
    {
        ImmutableArray<byte> image =
            Image(typeof(StructuralCloneFixture).Assembly.Location);
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        var input =
            new AssemblyContextStructuralCloneRetrievalInput(
                group,
                participant,
                group,
                participant,
                new StructuralCloneQuerySeed.MethodDefinitionToken(
                    MetadataTokens.GetToken(
                        Method(
                            image,
                            typeof(StructuralCloneFixture)
                                .FullName!,
                            nameof(
                                StructuralCloneFixture
                                    .ExactPositiveA)))),
                new StructuralCloneQueryPopulation.WholeAssembly(),
                new StructuralCloneRetrievalLimits(
                    MaximumMethods: 1));

        var available = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Available>(
                Execute(input));

        Assert.Equal(
            StructuralCloneRetrievalDisposition.LimitReached,
            available.Retrieval.Disposition);
        Assert.Contains(
            available.Retrieval.Blockers,
            blocker =>
                blocker.Kind
                == StructuralCloneRetrievalBlockerKind.MethodLimit);
        Assert.Equal(0, available.Retrieval.Receipt.BodyProductions);
        Assert.Equal(
            MethodCount(image),
            available.Retrieval.Receipt.InputMethods);
    }

    [Fact]
    public void Execute_ResultAndBodyProductionLimitsRemainDistinct()
    {
        ImmutableArray<byte> image =
            Image(typeof(StructuralCloneFixture).Assembly.Location);
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        StructuralCloneQuerySeed seed =
            new StructuralCloneQuerySeed.MethodDefinitionToken(
                MetadataTokens.GetToken(
                    Method(
                        image,
                        typeof(StructuralCloneFixture).FullName!,
                        nameof(StructuralCloneFixture.NearConstantA))));
        StructuralCloneQueryPopulation population =
            new StructuralCloneQueryPopulation.Type(
                TypeName(
                    typeof(StructuralCloneFixture).FullName!));

        var resultLimited = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Available>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        seed,
                        population,
                        new StructuralCloneRetrievalLimits(
                            MaximumResults: 2))));
        var bodyLimited = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Available>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        seed,
                        population,
                        new StructuralCloneRetrievalLimits(
                            ComparisonLimits:
                                new StructuralCloneComparisonLimits(
                                    MaximumBlocks: 1)))));

        Assert.Equal(
            StructuralCloneRetrievalDisposition.Completed,
            resultLimited.Retrieval.Disposition);
        Assert.Equal(2, resultLimited.Retrieval.Candidates.Length);
        Assert.True(
            resultLimited.Retrieval.Receipt.SuppressedCandidates
                > 0);
        Assert.Equal(
            StructuralCloneRetrievalDisposition.LimitReached,
            bodyLimited.Retrieval.Disposition);
        Assert.True(
            bodyLimited.Retrieval.Receipt.LimitReachedMethods
                > 0);
        Assert.Contains(
            bodyLimited.Retrieval.Blockers,
            blocker =>
                blocker.Kind
                == StructuralCloneRetrievalBlockerKind
                    .CandidateProductionLimit);
    }

    [Fact]
    public void Execute_UnsupportedSeedRemainsAProductOutcome()
    {
        ImmutableArray<byte> image =
            Image(typeof(StructuralCloneFixture).Assembly.Location);
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        var available = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Available>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    Method(
                                        image,
                                        typeof(
                                            StructuralCloneFixture)
                                            .FullName!,
                                        nameof(
                                            StructuralCloneFixture
                                                .ExceptionHandlingA)))),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName(
                                typeof(StructuralCloneFixture)
                                    .FullName!)))));

        Assert.Equal(
            StructuralCloneRetrievalDisposition.Unsupported,
            available.Retrieval.Disposition);
        Assert.Equal(
            StructuralCloneDisposition.Unsupported,
            available.Retrieval.Seed.Disposition);
        Assert.Contains(
            available.Retrieval.Blockers,
            blocker =>
                blocker.Kind
                == StructuralCloneRetrievalBlockerKind
                    .SeedUnsupported);
    }

    [Fact]
    public void Execute_UnknownTargetsFailInsteadOfReturningEmptyResults()
    {
        ImmutableArray<byte> image =
            Image(typeof(StructuralCloneFixture).Assembly.Location);
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        MemberAnchor existing =
            MemberSeed(
                    image,
                    typeof(StructuralCloneFixture).FullName!,
                    nameof(StructuralCloneFixture.ExactPositiveA))
                .MemberIdentity;
        var missingMember =
            new StructuralCloneQuerySeed.Member(
                TypeName(
                    typeof(StructuralCloneFixture).FullName!),
                existing with
                {
                    StableSelector =
                        existing.StableSelector + "-missing",
                });
        var missingType =
            new StructuralCloneQueryPopulation.Type(
                TypeName("Missing.StructuralCloneFixture"));

        var memberFailure = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        missingMember,
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));
        var typeFailure = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    Method(
                                        image,
                                        typeof(
                                            StructuralCloneFixture)
                                            .FullName!,
                                        nameof(
                                            StructuralCloneFixture
                                                .ExactPositiveA)))),
                        missingType)));

        Assert.Equal(
            StructuralCloneQueryFailureKind.SeedMemberNotFound,
            memberFailure.Failure.Kind);
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Seed,
            memberFailure.Failure.Role);
        Assert.Equal(
            StructuralCloneQueryFailureKind.CandidateTypeNotFound,
            typeFailure.Failure.Kind);
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Candidate,
            typeFailure.Failure.Role);
    }

    [Fact]
    public void Execute_AmbiguousExactMemberIsAnExplicitFailure()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildAmbiguousSeedAssembly());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        StructuralCloneQuerySeed.Member seed =
            MemberSeed(
                image,
                "N.Fixture",
                "Seed",
                requireUnique: false);

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        seed,
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));

        Assert.Equal(
            StructuralCloneQueryFailureKind.SeedMemberAmbiguous,
            failed.Failure.Kind);
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Seed,
            failed.Failure.Role);
    }

    [Fact]
    public void Execute_ExtensionMemberUsesItsProductOwnedExactAnchor()
    {
        ImmutableArray<byte> image =
            Image(FixtureCatalog.DiffPair.OldAssemblyPath());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        const string TypeNameText =
            "DiffFixtureSample.ExtensionSample";
        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle type =
            Type(reader, TypeNameText);
        MethodDefinitionHandle method =
            Method(reader, type, "Twice");
        ExtensionMemberAnchorInfo anchor =
            ApiMemberIdentity.CreateExtensionMethodAnchorInfo(
                reader,
                type,
                reader.GetMethodDefinition(method));

        var available = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Available>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed.Member(
                            TypeName(TypeNameText),
                            anchor.Anchor),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName(TypeNameText)))));

        Assert.Equal(
            method,
            available.Retrieval.Seed.Method.Handle);
        Assert.Equal(
            StructuralCloneRetrievalDisposition.Completed,
            available.Retrieval.Disposition);
    }

    [Fact]
    public void Execute_SeedFailurePrecedesCandidateAcquisition()
    {
        ImmutableArray<byte> image =
            Image(typeof(StructuralCloneFixture).Assembly.Location);
        ImmutableArray<byte> malformed = [0, 1, 2, 3];
        var seedPolicy = new TestBindingPolicy();
        var candidatePolicy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup seedGroup =
            Group(workspace, image, seedPolicy);
        using AssemblyContextGroup candidateGroup =
            Group(
                workspace,
                malformed,
                candidatePolicy,
                ContentIdentity(image));
        AssemblyContextParticipant seedParticipant =
            Assert.Single(seedGroup.Participants);
        AssemblyContextParticipant candidateParticipant =
            Assert.Single(candidateGroup.Participants);
        int missingToken = MetadataTokens.GetToken(
            MetadataTokens.MethodDefinitionHandle(
                MethodCount(image) + 1));

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        seedGroup,
                        seedParticipant,
                        candidateGroup,
                        candidateParticipant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(missingToken),
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));

        Assert.Equal(
            StructuralCloneQueryFailureKind.SeedMethodNotFound,
            failed.Failure.Kind);
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Seed,
            failed.Failure.Role);
        Assert.Equal(0, candidateGroup.RetainedImageBytes);
    }

    [Fact]
    public void Execute_VirtualMethodTokenIsATypedMissingSeed()
    {
        ImmutableArray<byte> image =
            Image(typeof(StructuralCloneFixture).Assembly.Location);
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                unchecked((int)0x86000001)),
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));

        Assert.Equal(
            StructuralCloneQueryFailureKind.SeedMethodNotFound,
            failed.Failure.Kind);
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Seed,
            failed.Failure.Role);
    }

    [Fact]
    public void Execute_RepeatedLongLeafTypeLookupFailsAtAggregateBudget()
    {
        const string Namespace = "N";
        string longLeaf = new('T', 4_000);
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildRepeatedTypeNameAssembly(
                    Namespace,
                    longLeaf,
                    typeCount: 1_100));
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(0x06000001),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName(
                                $"{Namespace}.{longLeaf}")))));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Candidate,
            failed.Failure.Role);
        Assert.Contains(
            "structural-name work budget",
            failed.Failure.Detail);
    }

    [Fact]
    public void Execute_RepeatedUnequalLongLeafTypeLookupFailsAtAggregateBudget()
    {
        const string Namespace = "N";
        string longLeaf = new('T', 4_000);
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildRepeatedTypeNameAssembly(
                    Namespace,
                    longLeaf + "X",
                    typeCount: 1_100));
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(0x06000001),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName(
                                $"{Namespace}.{longLeaf}")))));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Candidate,
            failed.Failure.Role);
        Assert.Contains(
            "structural-name work budget",
            failed.Failure.Detail);
    }

    [Fact]
    public void Execute_RepeatedMalformedTypeLeavesFailAtDecodeBudget()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildMalformedTypeNameAssembly(
                    malformedTypes: 100_000));
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(0x06000001),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("N.Missing")))));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread()
            - allocatedBefore;

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Candidate,
            failed.Failure.Role);
        Assert.Contains(
            "type-name decode failure budget",
            failed.Failure.Detail);
        Assert.True(
            allocated < 2 * 1024 * 1024,
            $"Malformed TypeDef lookup allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Execute_NearLimitMemberAnchorsShareOneWorkBudget()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildHostileMemberIdentityAssembly(
                    methodCount: 64,
                    parameterCount: 30,
                    genericArity: 2_030));
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        StructuralCloneQuerySeed.Member seed =
            MemberSeed(
                Image(typeof(StructuralCloneFixture).Assembly.Location),
                typeof(StructuralCloneFixture).FullName!,
                nameof(StructuralCloneFixture.ExactPositiveA));

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed.Member(
                            TypeName("C"),
                            seed.MemberIdentity),
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread()
            - allocatedBefore;

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Contains(
            "anchor-signature work budget",
            failed.Failure.Detail);
        Assert.True(
            allocated < 24 * 1024 * 1024,
            $"Exact member lookup allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Execute_ExtensionContainerAttributesAreInspectedOnce()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildAttributedMethodAssembly(
                    methodCount: 32,
                    attributeCount: 128,
                    attributeTypeNameLength: 3_000));
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        StructuralCloneQuerySeed.Member seed =
            MemberSeed(
                Image(typeof(StructuralCloneFixture).Assembly.Location),
                typeof(StructuralCloneFixture).FullName!,
                nameof(StructuralCloneFixture.ExactPositiveA));

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed.Member(
                            TypeName("C"),
                            seed.MemberIdentity),
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread()
            - allocatedBefore;

        Assert.Equal(
            StructuralCloneQueryFailureKind.SeedMemberNotFound,
            failed.Failure.Kind);
        Assert.True(
            allocated < 4 * 1024 * 1024,
            $"Extension-container lookup allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void Execute_TypeSpecificationAttributeBudgetExhaustionIsVisible()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildAttributedMethodAssembly(
                    methodCount: 1,
                    attributeCount: 1_500,
                    attributeTypeNameLength: 3_000,
                    useTypeSpecificationParent: true));
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        StructuralCloneQuerySeed.Member seed =
            MemberSeed(
                Image(typeof(StructuralCloneFixture).Assembly.Location),
                typeof(StructuralCloneFixture).FullName!,
                nameof(StructuralCloneFixture.ExactPositiveA));

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed.Member(
                            TypeName("C"),
                            seed.MemberIdentity),
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Seed,
            failed.Failure.Role);
        Assert.Contains(
            "custom attribute work budget",
            failed.Failure.Detail);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_RejectedTypeSpecificationAttributeIsVisible(
        bool attributeOnMethod)
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildRejectedTypeSpecificationAttributeAssembly(
                    attributeOnMethod));
        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle type =
            MetadataTokens.TypeDefinitionHandle(2);
        MethodDefinition method =
            reader.GetMethodDefinition(
                MetadataTokens.MethodDefinitionHandle(1));
        MemberAnchor anchor =
            ApiMemberIdentity.CreateMethodAnchorInfo(
                reader,
                type,
                method,
                isExtensionMethod: false).Anchor;
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed.Member(
                            TypeName("C"),
                            anchor),
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Seed,
            failed.Failure.Role);
    }

    [Fact]
    public void Execute_HealthyExactTypeSurvivesMalformedNeighbor()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildMalformedNeighborAssembly());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle type =
            MetadataTokens.TypeDefinitionHandle(2);
        MethodDefinitionHandle seed =
            MetadataTokens.MethodDefinitionHandle(1);
        MemberAnchor anchor =
            ApiMemberIdentity.CreateMethodAnchor(
                reader,
                type,
                reader.GetMethodDefinition(seed));

        var available = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Available>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed.Member(
                            TypeName("N.Fixture"),
                            anchor),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("N.Fixture")))));
        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(seed)),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("Missing.Fixture")))));

        Assert.Equal(
            StructuralCloneRetrievalDisposition.Completed,
            available.Retrieval.Disposition);
        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Candidate,
            failed.Failure.Role);
    }

    [Fact]
    public void Execute_TypeNameDecodeFailuresBelowTheCeilingStillResolve()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildMalformedTypeNameAssembly(malformedTypes: 2));
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        var available = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Available>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(1))),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("N.Fixture")))));

        Assert.Equal(
            StructuralCloneRetrievalDisposition.Completed,
            available.Retrieval.Disposition);
    }

    [Fact]
    public void Execute_TypeNameDecodeFailureCeilingIsAVisibleRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildMalformedTypeNameAssembly(malformedTypes: 3));
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(1))),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("N.Fixture")))));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Candidate,
            failed.Failure.Role);
    }

    [Fact]
    public void Execute_DuplicateProjectedMethodRowIsAVisibleRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildDuplicateMethodPtrAssembly());
        using (var peReader = new PEReader(image))
        {
            MetadataReader reader = peReader.GetMetadataReader();
            Assert.Equal(
                2,
                reader.GetTableRowCount(TableIndex.MethodPtr));
        }

        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(1))),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("N.Fixture")))));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        // A same-image query validates the one shared image under the
        // seed role; the cross-image case gates candidate attribution.
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Seed,
            failed.Failure.Role);
    }

    [Fact]
    public void Execute_WholeAssemblyDuplicateProjectionIsAVisibleRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildDuplicateMethodPtrAssembly());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(1))),
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        // A same-image query validates the one shared image under the
        // seed role; the cross-image case gates candidate attribution.
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Seed,
            failed.Failure.Role);
    }

    [Fact]
    public void Execute_OutOfRangeMethodProjectionIsAVisibleRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildMethodPtrAssembly(1, 99));
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(1))),
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        // A same-image query validates the one shared image under the
        // seed role; the cross-image case gates candidate attribution.
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Seed,
            failed.Failure.Role);
    }

    [Fact]
    public void Execute_DuplicateProjectionSeedMemberIsAMetadataRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildDuplicateMethodPtrAssembly());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        var anchor =
            ApiMemberIdentity.CreateMethodAnchor(
                reader,
                Type(reader, "N.Fixture"),
                reader.GetMethodDefinition(
                    MetadataTokens.MethodDefinitionHandle(1)));

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed.Member(
                            TypeName("N.Fixture"),
                            anchor),
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));

        // A repeated projection is a corrupt image, not a user-visible
        // ambiguity between two distinct members.
        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Seed,
            failed.Failure.Role);
    }

    [Fact]
    public void Execute_CrossImageWholeAssemblyDuplicateProjectionIsAVisibleRejection()
    {
        ImmutableArray<byte> seedImage =
            Image(FixtureCatalog.DiffPair.OldAssemblyPath());
        ImmutableArray<byte> candidateImage =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildDuplicateMethodPtrAssembly());
        var seedPolicy = new TestBindingPolicy();
        var candidatePolicy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup seedGroup =
            Group(workspace, seedImage, seedPolicy);
        using AssemblyContextGroup candidateGroup =
            Group(workspace, candidateImage, candidatePolicy);
        int seedToken = MetadataTokens.GetToken(
            Method(
                seedImage,
                "DiffFixtureSample.DiffSample",
                "Stable"));

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        seedGroup,
                        Assert.Single(seedGroup.Participants),
                        candidateGroup,
                        Assert.Single(candidateGroup.Participants),
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(seedToken),
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Candidate,
            failed.Failure.Role);
    }

    [Fact]
    public void Execute_MalformedMethodRangeIsAVisibleRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildMalformedMethodListAssembly());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        // Without the whole-image ownership check this returns Available
        // with an empty population: corrupt metadata shaped as success.
        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(1))),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("N.Fixture")))));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
    }

    [Fact]
    public void Execute_AliasedMethodPtrAcrossTypesIsAVisibleRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildAliasedMethodPtrAssembly());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        // Each type projects MethodDef row 1 once, so a per-projection
        // duplicate set cannot see the alias.
        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(1))),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("N.Fixture")))));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
    }

    [Fact]
    public void Execute_UncoveredMethodPtrRowIsAVisibleRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildUncoveredMethodPtrAssembly());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        // The projected rows still cover MethodDef exactly once, so
        // coverage alone accepts an image carrying MethodPtr rows that
        // no TypeDef range reaches.
        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(1))),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("N.Fixture")))));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Contains(
            "not a permutation",
            failed.Failure.Detail);
    }

    [Fact]
    public void Execute_DescendingMethodListIsAVisibleRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildDescendingMethodListAssembly());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        // The descending range reports a negative length and enumerates
        // nothing, so the later type covers both rows on its own and
        // coverage alone accepts a MethodList column that is not a
        // partition. Only the range-length check rejects this image.
        Assert.Equal(
            [-1, 2],
            MeasureMethodRangeLengths(image));

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(1))),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("N.Fixture")))));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Contains(
            "non-decreasing",
            failed.Failure.Detail);
    }

    [Fact]
    public void Execute_DescendingMethodListWithoutMethodPtrIsAVisibleRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildDescendingMethodListWithoutMethodPtrAssembly());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        // No MethodPtr table stands between the column and the methods
        // it names, so this is the descending shape on a plain image.
        // The throw fires at the module row, before any row is
        // projected; pinned so the comment cannot drift from the
        // fixture.
        Assert.Equal(
            [-1, 2],
            MeasureMethodRangeLengths(image));

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(1))),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("N.Fixture")))));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Contains(
            "non-decreasing",
            failed.Failure.Detail);
    }

    [Fact]
    public void Execute_DescendingReorderedMethodPtrIsAVisibleRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildDescendingReorderedMethodPtrAssembly());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        // The same column on an unoptimized image whose MethodPtr rows
        // reverse MethodDef order, so the guard is exercised on the
        // other metadata shape. Like the plain case this throws at the
        // module row, so the reordering itself is never projected; what
        // it gates is that a MethodPtr image reaches the check at all.
        Assert.Equal(
            [-1, 2],
            MeasureMethodRangeLengths(image));

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(1))),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("N.Fixture")))));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Contains(
            "non-decreasing",
            failed.Failure.Detail);
    }

    [Fact]
    public void Execute_MethodListStartPastProjectedTableIsAVisibleRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildStartPastProjectedTableAssembly());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        // Every start sits past the end of the projected table, so the
        // earlier ranges report length zero and enumerate nothing and
        // the final range is the one that reports a negative length.
        // This is the only case that reaches the end of the derived
        // bound: the descending cases throw at the module row first.
        // Coverage also rejects this shape if the range-length check is
        // removed, so this pins the bound's end, not a case that would
        // otherwise be silently accepted.
        // Pinned rather than asserted, so the fixture cannot drift into
        // rejecting for some earlier reason.
        Assert.Equal(
            [0, -1],
            MeasureMethodRangeLengths(image));

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(1))),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("N.Fixture")))));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Contains(
            "non-decreasing",
            failed.Failure.Detail);
    }

    [Fact]
    public void Execute_FirstMethodListStartPastRowOneIsAVisibleRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildFirstStartPastRowOneAssembly());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        // Every range here reports a non-negative length, so the
        // range-length check accepts the column and coverage is the only
        // thing that rejects it. Ordering is a joint property of the two
        // checks; this pins the half that the length check cannot see.
        Assert.Equal(
            [0, 1],
            MeasureMethodRangeLengths(image));

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(2))),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("N.Fixture")))));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Contains(
            "cover the MethodDef table exactly once",
            failed.Failure.Detail);
    }

    [Fact]
    public void Execute_NullMethodListAfterPopulatedRunIsAVisibleRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildNullMethodListAfterPopulatedRunAssembly());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        // A *leading* null is legal, but a null after a populated run is
        // not: ECMA-335 II.22.37 ends each run where the next TypeDef's
        // run begins, so a null start at row i+1 would end row i's run
        // before it began. The damage lands on the preceding row, which
        // is why the negative length appears at row 0 rather than at the
        // null itself.
        Assert.Equal(
            [-1, 0],
            MeasureMethodRangeLengths(image));

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(1))),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("N.Fixture")))));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Contains(
            "non-decreasing",
            failed.Failure.Detail);
    }

    [Fact]
    public void Execute_NullMethodListIsNotRejected()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildNullMethodListAssembly());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        // A null run is legal metadata, not malformed metadata. The
        // ordering check must not turn a valid package into a typed
        // inspection failure; coverage remains the guard that a null
        // run does not silently drop methods.
        //
        // The null start reports length zero rather than the difference
        // to the next start, so the first *non-null* start is the one
        // coverage pins to row 1. Pinned, because this is the shape
        // that makes the weaker claim the necessary one.
        Assert.Equal(
            [0, 2],
            MeasureMethodRangeLengths(image));
        AssemblyContextStructuralCloneRetrievalResult result =
            Execute(
                new(
                    group,
                    participant,
                    group,
                    participant,
                    new StructuralCloneQuerySeed
                        .MethodDefinitionToken(
                            MetadataTokens.GetToken(
                                MetadataTokens
                                    .MethodDefinitionHandle(1))),
                    new StructuralCloneQueryPopulation.Type(
                        TypeName("N.Fixture"))));

        if (result
            is AssemblyContextStructuralCloneRetrievalResult.Failed
                rejected)
        {
            Assert.NotEqual(
                StructuralCloneQueryFailureKind
                    .MetadataInspectionFailed,
                rejected.Failure.Kind);
        }
    }

    [Fact]
    public void Execute_RepeatedLeafNameChargesChainTraversal()
    {
        // Every candidate clears the leaf comparison and reaches the
        // declaring-chain walk. Charged only for the names involved,
        // 17,000 candidates spend well under the budget while doing far
        // more work than the budget claims to bound.
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildRepeatedLeafNameAssembly(17_000, depth: 64));

        // Pin the fixture's declaring depth. A depth-1 fixture would
        // still exhaust the budget and so would still pass, leaving
        // the traversal this charge exists to bound unexercised.
        Assert.Equal(64, MeasureLeafDeclaringDepth(image, "C"));

        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(1))),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName("C")))));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Contains(
            "structural-name work budget",
            failed.Failure.Detail);
    }

    [Fact]
    public void Execute_TypeDeflessImageIsAVisibleRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildTypeDeflessAssembly());
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        // Empty ranges over an empty MethodDef table satisfy coverage
        // vacuously, so the seed would otherwise read as merely absent.
        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    MetadataTokens
                                        .MethodDefinitionHandle(1))),
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Contains(
            "no TypeDef rows",
            failed.Failure.Detail);
    }

    [Fact]
    public void Execute_RejectedSeedSiblingIsAVisibleRejection()
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(
                BuildBrokenSiblingAssembly());
        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        MemberAnchor anchor =
            ApiMemberIdentity.CreateMethodAnchorInfo(
                reader,
                MetadataTokens.TypeDefinitionHandle(2),
                reader.GetMethodDefinition(
                    MetadataTokens.MethodDefinitionHandle(1)),
                isExtensionMethod: false).Anchor;
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        // The healthy method matches, but the rejected sibling might
        // have decoded to the same anchor, so a single match does not
        // establish uniqueness and must not read as resolved.
        var failed = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Failed>(
                Execute(
                    new(
                        group,
                        participant,
                        group,
                        participant,
                        new StructuralCloneQuerySeed.Member(
                            TypeName("C"),
                            anchor),
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));

        Assert.Equal(
            StructuralCloneQueryFailureKind.MetadataInspectionFailed,
            failed.Failure.Kind);
        Assert.Equal(
            StructuralCloneQueryParticipantRole.Seed,
            failed.Failure.Role);
        Assert.Contains(
            "could not be inspected",
            failed.Failure.Detail);
    }

    [Fact]
    public void Execute_MalformedSeedImageIsAVisibleSeedRejection()
    {
        ImmutableArray<byte> candidateImage =
            Image(typeof(StructuralCloneFixture).Assembly.Location);
        ImmutableArray<byte> malformed = [0, 1, 2, 3];
        var seedPolicy = new TestBindingPolicy();
        var candidatePolicy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup seedGroup =
            Group(
                workspace,
                malformed,
                seedPolicy,
                ContentIdentity(candidateImage));
        using AssemblyContextGroup candidateGroup =
            Group(workspace, candidateImage, candidatePolicy);
        AssemblyContextParticipant seedParticipant =
            Assert.Single(seedGroup.Participants);
        AssemblyContextParticipant candidateParticipant =
            Assert.Single(candidateGroup.Participants);

        var rejected = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Rejected>(
                Execute(
                    new(
                        seedGroup,
                        seedParticipant,
                        candidateGroup,
                        candidateParticipant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(0x06000001),
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));

        Assert.Equal(
            StructuralCloneQueryParticipantRole.Seed,
            rejected.Role);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
    }

    [Fact]
    public void Execute_MalformedCandidateImageIsAVisibleRejection()
    {
        ImmutableArray<byte> seedImage =
            Image(typeof(StructuralCloneFixture).Assembly.Location);
        ImmutableArray<byte> malformed = [0, 1, 2, 3];
        var seedPolicy = new TestBindingPolicy();
        var candidatePolicy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup seedGroup =
            Group(workspace, seedImage, seedPolicy);
        using AssemblyContextGroup candidateGroup =
            Group(
                workspace,
                malformed,
                candidatePolicy,
                ContentIdentity(seedImage));
        AssemblyContextParticipant seedParticipant =
            Assert.Single(seedGroup.Participants);
        AssemblyContextParticipant candidateParticipant =
            Assert.Single(candidateGroup.Participants);

        var rejected = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Rejected>(
                Execute(
                    new(
                        seedGroup,
                        seedParticipant,
                        candidateGroup,
                        candidateParticipant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(
                                    Method(
                                        seedImage,
                                        typeof(
                                            StructuralCloneFixture)
                                            .FullName!,
                                        nameof(
                                            StructuralCloneFixture
                                                .ExactPositiveA)))),
                        new StructuralCloneQueryPopulation
                            .WholeAssembly())));

        Assert.Equal(
            StructuralCloneQueryParticipantRole.Candidate,
            rejected.Role);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
        Assert.Same(
            seedParticipant.Assembly.Registration,
            rejected.SeedSubject.Registration);
        Assert.Same(
            candidateParticipant.Assembly.Registration,
            rejected.CandidateSubject.Registration);
    }

    [Fact]
    public void Execute_SeparateRegistrationsWithSameMvidRemainCrossImage()
    {
        ImmutableArray<byte> image =
            Image(typeof(StructuralCloneFixture).Assembly.Location);
        var seedPolicy = new TestBindingPolicy();
        var candidatePolicy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup seedGroup =
            Group(workspace, image, seedPolicy);
        using AssemblyContextGroup candidateGroup =
            Group(workspace, image, candidatePolicy);
        AssemblyContextParticipant seedParticipant =
            Assert.Single(seedGroup.Participants);
        AssemblyContextParticipant candidateParticipant =
            Assert.Single(candidateGroup.Participants);
        MethodDefinitionHandle seed = Method(
            image,
            typeof(StructuralCloneFixture).FullName!,
            nameof(StructuralCloneFixture.ExactPositiveA));

        var available = Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Available>(
                Execute(
                    new(
                        seedGroup,
                        seedParticipant,
                        candidateGroup,
                        candidateParticipant,
                        new StructuralCloneQuerySeed
                            .MethodDefinitionToken(
                                MetadataTokens.GetToken(seed)),
                        new StructuralCloneQueryPopulation.Type(
                            TypeName(
                                typeof(StructuralCloneFixture)
                                    .FullName!)))));

        Assert.NotSame(
            seedParticipant.Assembly.Registration,
            candidateParticipant.Assembly.Registration);
        Assert.Contains(
            available.Retrieval.Candidates,
            candidate => candidate.Method.Handle == seed);
        Assert.Equal(
            available.Retrieval.Receipt.InputMethods,
            available.Retrieval.Receipt.ProcessedMethods);
        Assert.Equal(
            available.Retrieval.Receipt.InputMethods + 1,
            available.Retrieval.Receipt.BodyProductions);
    }

    [Fact]
    public void Definition_IsUnboundedAndRunsThroughTheTypedRegistry()
    {
        ImmutableArray<byte> image =
            Image(typeof(StructuralCloneFixture).Assembly.Location);
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            Group(workspace, image, policy);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        var input =
            new AssemblyContextStructuralCloneRetrievalInput(
                group,
                participant,
                group,
                participant,
                new StructuralCloneQuerySeed.MethodDefinitionToken(
                    MetadataTokens.GetToken(
                        Method(
                            image,
                            typeof(StructuralCloneFixture)
                                .FullName!,
                            nameof(
                                StructuralCloneFixture
                                    .ExactPositiveA)))),
                new StructuralCloneQueryPopulation.Type(
                    TypeName(
                        typeof(StructuralCloneFixture)
                            .FullName!)));
        var registry =
            new InspectionQueryRegistry<
                AssemblyContextStructuralCloneRetrievalInput>()
                .Add(
                    AssemblyContextStructuralCloneRetrievalQuery
                        .Definition,
                    AssemblyContextStructuralCloneRetrievalQuery
                        .Execute);

        AssemblyContextStructuralCloneRetrievalResult result =
            registry.Run(
                    [
                        AssemblyContextStructuralCloneRetrievalQuery
                            .Definition,
                    ],
                    input)
                .Get(
                    AssemblyContextStructuralCloneRetrievalQuery
                        .Definition);

        Assert.Equal(
            InspectionCost.Unbounded,
            AssemblyContextStructuralCloneRetrievalQuery
                .Definition.Cost);
        Assert.IsType<
            AssemblyContextStructuralCloneRetrievalResult.Available>(
                result);
    }

    static AssemblyContextStructuralCloneRetrievalResult Execute(
        AssemblyContextStructuralCloneRetrievalInput input) =>
        AssemblyContextStructuralCloneRetrievalQuery.Execute(
            input,
            TestContext.Current.CancellationToken);

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        ImmutableArray<byte> image,
        IAssemblyBindingPolicy policy,
        AssemblyReferenceIdentity? identity = null)
        => workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.Create(
                        identity ?? ContentIdentity(image),
                        path: null,
                        () => new MemoryStream(
                            ImmutableCollectionsMarshal
                                .AsArray(image)!,
                            writable: false),
                        AssemblyResolutionProvenance.Local(
                            "structural-clone query tests")),
                    policy),
            ]);

    static ImmutableArray<byte> Image(string path) =>
        ImmutableCollectionsMarshal.AsImmutableArray(
            File.ReadAllBytes(path));

    static AssemblyReferenceIdentity ContentIdentity(
        ImmutableArray<byte> image)
    {
        using var reader = new PEReader(image);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

    static Guid ContentMvid(ImmutableArray<byte> image)
    {
        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        return reader.GetGuid(
            reader.GetModuleDefinition().Mvid);
    }

    static int MethodCount(ImmutableArray<byte> image)
    {
        using var peReader = new PEReader(image);
        return peReader.GetMetadataReader()
            .GetTableRowCount(TableIndex.MethodDef);
    }

    static MetadataTypeDefinitionName TypeName(string serializedName)
        => Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.ParseSerialized(
                    serializedName))
            .Name;

    static StructuralCloneQuerySeed.Member MemberSeed(
        ImmutableArray<byte> image,
        string typeName,
        string methodName,
        bool requireUnique = true)
    {
        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle type =
            Type(reader, typeName);
        MethodDefinitionHandle method =
            Method(
                reader,
                type,
                methodName,
                requireUnique);
        return new StructuralCloneQuerySeed.Member(
            TypeName(typeName),
            ApiMemberIdentity.CreateMethodAnchor(
                reader,
                type,
                reader.GetMethodDefinition(method)));
    }

    static MethodDefinitionHandle Method(
        ImmutableArray<byte> image,
        string typeName,
        string methodName)
    {
        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        return Method(
            reader,
            Type(reader, typeName),
            methodName);
    }

    static MethodDefinitionHandle Method(
        MetadataReader reader,
        string typeName,
        string methodName)
        => Method(
            reader,
            Type(reader, typeName),
            methodName);

    static MethodDefinitionHandle Method(
        MetadataReader reader,
        TypeDefinitionHandle type,
        string methodName,
        bool requireUnique = true)
    {
        MethodDefinitionHandle match = default;
        foreach (MethodDefinitionHandle method
            in reader.GetTypeDefinition(type).GetMethods())
        {
            if (reader.GetString(
                    reader.GetMethodDefinition(method).Name)
                != methodName)
            {
                continue;
            }

            if (!match.IsNil)
            {
                Assert.False(requireUnique);
                continue;
            }

            match = method;
        }

        Assert.False(match.IsNil);
        return match;
    }

    static ImmutableArray<MethodDefinitionHandle> Methods(
        MetadataReader reader,
        string typeName)
        => reader.GetTypeDefinition(Type(reader, typeName))
            .GetMethods()
            .ToImmutableArray();

    static TypeDefinitionHandle Type(
        MetadataReader reader,
        string serializedName)
    {
        MetadataTypeDefinitionName name =
            TypeName(serializedName);
        var index = MetadataTypeDefinitionIndex.Create(reader);
        Assert.True(
            index.TryGetUniqueDefinition(name, out var handle));
        return handle;
    }

    static StructuralCloneRetrievalCandidate Candidate(
        ImmutableArray<byte> image,
        StructuralCloneRetrievalResult result,
        string methodName)
    {
        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        return Assert.Single(
            result.Candidates,
            candidate =>
                reader.GetString(
                    reader.GetMethodDefinition(
                            candidate.Method.Handle)
                        .Name)
                == methodName);
    }

    static void AssertProductResult(
        StructuralCloneRetrievalResult expected,
        StructuralCloneRetrievalResult actual)
    {
        Assert.Equal(expected.Disposition, actual.Disposition);
        AssertMethodOutcome(expected.Seed, actual.Seed);
        Assert.Equal(
            expected.Candidates.ToArray(),
            actual.Candidates.ToArray());
        Assert.Equal(expected.Methods.Length, actual.Methods.Length);
        for (int index = 0;
            index < expected.Methods.Length;
            index++)
        {
            AssertMethodOutcome(
                expected.Methods[index],
                actual.Methods[index]);
        }
        Assert.Equal(
            expected.Blockers.ToArray(),
            actual.Blockers.ToArray());
        Assert.Equal(expected.Receipt, actual.Receipt);
    }

    static void AssertMethodOutcome(
        StructuralCloneRetrievalMethodOutcome expected,
        StructuralCloneRetrievalMethodOutcome actual)
    {
        Assert.Equal(expected.Method, actual.Method);
        Assert.Equal(expected.Disposition, actual.Disposition);
        Assert.Equal(
            expected.Blockers.ToArray(),
            actual.Blockers.ToArray());
        Assert.Equal(expected.Receipt, actual.Receipt);
    }

    static byte[] BuildAmbiguousSeedAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("AmbiguousSeed.dll"),
            metadata.GetOrAddGuid(
                new Guid(
                    "852EFD23-F1E5-4D8D-B879-C97A5484D24A")),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("AmbiguousSeed"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Fixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        AddSyntheticMethod(metadata, encoder, "Seed");
        AddSyntheticMethod(metadata, encoder, "Seed");

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildMalformedNeighborAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("MalformedNeighbor.dll"),
            metadata.GetOrAddGuid(
                new Guid(
                    "4B5E8C41-45BE-4C50-920D-A5F0F01FDC9D")),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MalformedNeighbor"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Fixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Fixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(3));
        metadata.AddNestedType(
            MetadataTokens.TypeDefinitionHandle(3),
            MetadataTokens.TypeDefinitionHandle(3));

        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        AddSyntheticMethod(metadata, encoder, "Seed");
        AddSyntheticMethod(metadata, encoder, "Candidate");

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    /// <summary>
    /// Builds an assembly whose leading TypeDef rows carry out-of-range
    /// Name string handles, so inspecting them fails to decode while a
    /// healthy <c>N.Fixture</c> row remains resolvable.
    /// </summary>
    static byte[] BuildMalformedTypeNameAssembly(int malformedTypes)
    {
        MetadataBuilder metadata = CreateMetadata(
            "MalformedTypeNames",
            new Guid("7A0B1C2D-3E4F-5061-7283-94A5B6C7D8E9"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        for (int i = 0; i < malformedTypes; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Broken"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        }

        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Fixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        AddSyntheticMethod(metadata, encoder, "Seed");

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        byte[] bytes = image.ToArray();
        CorruptTypeDefinitionNames(bytes, malformedTypes);
        return bytes;
    }

    /// <summary>
    /// Rewrites the Name column of the TypeDef rows that follow
    /// <c>&lt;Module&gt;</c> so each points past the end of the string
    /// heap. The edit is size-neutral, so the surrounding PE stays valid.
    /// </summary>
    static void CorruptTypeDefinitionNames(
        byte[] image,
        int malformedTypes)
    {
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        int tableOffset =
            peReader.PEHeaders.MetadataStartOffset
            + reader.GetTableMetadataOffset(TableIndex.TypeDef);
        int rowSize = reader.GetTableRowSize(TableIndex.TypeDef);
        int stringIndexSize =
            reader.GetHeapSize(HeapIndex.String)
                <= ushort.MaxValue
                ? sizeof(ushort)
                : sizeof(uint);

        for (int index = 0; index < malformedTypes; index++)
        {
            int nameOffset =
                tableOffset
                + ((index + 1) * rowSize)
                + sizeof(uint);
            if (stringIndexSize == sizeof(ushort))
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    image.AsSpan(nameOffset, sizeof(ushort)),
                    ushort.MaxValue);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    image.AsSpan(nameOffset, sizeof(uint)),
                    uint.MaxValue);
            }
        }
    }

    /// <summary>
    /// Returns the offset of the first TypeDef row and the row size,
    /// using the reader's own table geometry rather than re-deriving the
    /// metadata header layout.
    /// </summary>
    static (int Offset, int RowSize) LocateTypeDefinitionTable(
        byte[] image)
    {
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        return (
            peReader.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(TableIndex.TypeDef),
            reader.GetTableRowSize(TableIndex.TypeDef));
    }

    /// <summary>
    /// Overwrites a TypeDef row's MethodList start. MethodList is the
    /// final TypeDef column, so it is addressed from the end of the row.
    /// </summary>
    static void WriteMethodListStart(
        byte[] image,
        int typeDefRow,
        ushort start)
    {
        (int offset, int rowSize) = LocateTypeDefinitionTable(image);
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(
                offset + (typeDefRow * rowSize) + rowSize - sizeof(ushort),
                sizeof(ushort)),
            start);
    }

    /// <summary>
    /// Builds an assembly where <c>N.Fixture</c> and the TypeDef that
    /// follows it declare the same out-of-range MethodList start. Because
    /// the two starts are equal, SRM reports <c>N.Fixture</c>'s range as
    /// empty rather than failing, so the type silently projects no
    /// methods and nothing about the read itself looks wrong.
    /// </summary>
    static byte[] BuildMalformedMethodListAssembly()
    {
        MetadataBuilder metadata = CreateMetadata(
            "MalformedMethodList",
            new Guid("5C4D3E2F-1A0B-4C9D-8E7F-6A5B4C3D2E1F"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Fixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Tail"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        AddSyntheticMethod(metadata, encoder, "Seed");

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        byte[] bytes = image.ToArray();
        WriteMethodListStart(bytes, typeDefRow: 1, start: 0xFFF0);
        WriteMethodListStart(bytes, typeDefRow: 2, start: 0xFFF0);
        return bytes;
    }

    /// <summary>
    /// Builds the duplicate MethodPtr image, then splits the two pointer
    /// rows across the two TypeDefs so each type projects MethodDef row 1
    /// exactly once. No single projection repeats, so only a whole-image
    /// ownership check can see the alias.
    /// </summary>
    static byte[] BuildAliasedMethodPtrAssembly()
    {
        byte[] bytes = BuildMethodPtrAssembly(1, 1);
        WriteMethodListStart(bytes, typeDefRow: 1, start: 2);
        return bytes;
    }

    static byte[] BuildDuplicateMethodPtrAssembly() =>
        BuildMethodPtrAssembly(1, 1);

    /// <summary>
    /// Builds an assembly whose metadata uses the unoptimized <c>#-</c>
    /// tables stream and carries a MethodPtr table selecting the two
    /// supplied MethodDef rows, so <c>N.Fixture</c> projects them in
    /// order. MetadataBuilder cannot emit MethodPtr, so the serialized
    /// image is patched.
    /// </summary>
    static byte[] BuildMethodPtrAssembly(
        ushort firstRow,
        ushort secondRow)
        => BuildMethodPtrAssembly([firstRow, secondRow]);

    static byte[] BuildMethodPtrAssembly(ushort[] rows)
    {
        var metadata = new MetadataBuilder();
        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        MethodDefinitionHandle first =
            AddSyntheticMethod(metadata, encoder, "M0");
        AddSyntheticMethod(metadata, encoder, "M1");
        metadata.AddAssembly(
            metadata.GetOrAddString("DuplicateMethodPtr"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("DuplicateMethodPtr.dll"),
            metadata.GetOrAddGuid(
                new Guid("3F2A6C18-9D74-4E51-B0C3-6E8A1D2F4B77")),
            encId: default,
            encBaseId: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: first);

        // N.Fixture is the last TypeDef, so its method range runs to the
        // end of the projection and spans both MethodPtr rows.
        metadata.AddTypeDefinition(
            TypeAttributes.Class | TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Fixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: first);

        // Unreferenced user string, reclaimed to keep the patch
        // size-neutral. #US preserves insertion order, so it lands last.
        metadata.GetOrAddUserString("PADPADPADPADPADPAD");

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return InsertMethodPtrTable(image.ToArray(), rows);
    }

    /// <summary>
    /// Rewrites the metadata so the tables stream is named <c>#-</c> and
    /// declares a MethodPtr table. The eight bytes this adds are
    /// reclaimed from the <c>#US</c> tail, keeping the image length and
    /// therefore every PE header and section size valid.
    /// </summary>
    static byte[] InsertMethodPtrTable(
        byte[] image,
        ushort[] rows)
    {
        int growth = 4 + (2 * rows.Length);
        Assert.Equal(0, growth % 4);
        int metadataStart;
        int metadataSize;
        using (var peReader = new PEReader(
            ImmutableCollectionsMarshal.AsImmutableArray(image)))
        {
            metadataStart = peReader.PEHeaders.MetadataStartOffset;
            metadataSize = peReader.PEHeaders.MetadataSize;
        }

        Assert.Equal(
            0x424A5342u,
            ReadUInt32At(image, metadataStart));
        int versionLength =
            ReadInt32At(image, metadataStart + 12);
        int cursor =
            metadataStart + 16 + AlignTo4(versionLength) + 2;
        int streamCount = ReadUInt16At(image, cursor);
        cursor += 2;

        var streams = new List<MetadataStreamHeader>();
        for (int i = 0; i < streamCount; i++)
        {
            var header = new MetadataStreamHeader
            {
                HeaderPosition = cursor,
                Offset = ReadInt32At(image, cursor),
                Size = ReadInt32At(image, cursor + 4),
            };
            cursor += 8;
            int nameStart = cursor;
            while (image[cursor] != 0)
            {
                cursor++;
            }

            header.Name = System.Text.Encoding.ASCII.GetString(
                image,
                nameStart,
                cursor - nameStart);
            cursor = nameStart + AlignTo4(cursor - nameStart + 1);
            streams.Add(header);
        }

        MetadataStreamHeader tables =
            streams.Single(s => s.Name == "#~");
        MetadataStreamHeader userStrings =
            streams.Single(s => s.Name == "#US");
        Assert.True(
            userStrings.Offset > tables.Offset,
            "#US must follow the tables stream.");

        var contents = new Dictionary<string, byte[]>();
        foreach (MetadataStreamHeader stream in streams)
        {
            var body = new byte[stream.Size];
            Array.Copy(
                image,
                metadataStart + stream.Offset,
                body,
                0,
                stream.Size);
            contents[stream.Name] = body;
        }

        contents["#~"] = GrowTablesWithMethodPtr(
            contents["#~"],
            rows);
        byte[] originalUserStrings = contents["#US"];
        Assert.True(
            originalUserStrings.Length >= growth + 4,
            "#US is too small to reclaim padding from.");
        var trimmed = new byte[originalUserStrings.Length - growth];
        Array.Copy(
            originalUserStrings,
            trimmed,
            trimmed.Length);
        contents["#US"] = trimmed;

        List<MetadataStreamHeader> ordered =
            streams.OrderBy(s => s.Offset).ToList();
        int next = ordered[0].Offset;
        foreach (MetadataStreamHeader stream in ordered)
        {
            next = AlignTo4(next);
            stream.Offset = next;
            stream.Size = contents[stream.Name].Length;
            next += stream.Size;
        }

        Assert.Equal(metadataSize, next);

        byte[] patched = (byte[])image.Clone();
        foreach (MetadataStreamHeader stream in streams)
        {
            WriteInt32At(
                patched,
                stream.HeaderPosition,
                stream.Offset);
            WriteInt32At(
                patched,
                stream.HeaderPosition + 4,
                stream.Size);
        }

        // "#~" and "#-" are the same length, so the rename is in place.
        patched[tables.HeaderPosition + 9] = (byte)'-';
        foreach (MetadataStreamHeader stream in ordered)
        {
            byte[] body = contents[stream.Name];
            Array.Copy(
                body,
                0,
                patched,
                metadataStart + stream.Offset,
                body.Length);
        }

        return patched;
    }

    /// <summary>
    /// Returns a tables-stream body that additionally declares a
    /// MethodPtr table holding two rows that both select MethodDef 1.
    /// </summary>
    static byte[] GrowTablesWithMethodPtr(
        byte[] tables,
        ushort[] rows)
    {
        Assert.Equal(0, tables[6] & 0x07);
        ulong valid = ReadUInt64At(tables, 8);
        Assert.Equal(
            0UL,
            valid & (1UL << (int)TableIndex.MethodPtr));

        var present = new List<int>();
        for (int table = 0; table < 64; table++)
        {
            if ((valid & (1UL << table)) != 0)
            {
                present.Add(table);
            }
        }

        var counts = new Dictionary<int, int>();
        for (int i = 0; i < present.Count; i++)
        {
            counts[present[i]] = ReadInt32At(tables, 24 + (4 * i));
        }

        foreach (KeyValuePair<int, int> entry in counts)
        {
            Assert.True(
                entry.Value < 0x10000,
                "The fixture must keep every table index two bytes.");
        }

        int insertAt =
            present.Count(t => t < (int)TableIndex.MethodPtr);
        int methodDefStart = 0;
        foreach (int table in present)
        {
            if (table >= (int)TableIndex.MethodDef)
            {
                break;
            }

            methodDefStart +=
                MethodPtrFixtureRowSize(table, counts) * counts[table];
        }

        var grown = new byte[tables.Length + 4 + (2 * rows.Length)];
        Array.Copy(tables, 0, grown, 0, 24);
        WriteUInt64At(
            grown,
            8,
            valid | (1UL << (int)TableIndex.MethodPtr));

        int write = 24;
        for (int i = 0; i < present.Count; i++)
        {
            if (i == insertAt)
            {
                WriteInt32At(grown, write, rows.Length);
                write += 4;
            }

            WriteInt32At(
                grown,
                write,
                ReadInt32At(tables, 24 + (4 * i)));
            write += 4;
        }

        if (insertAt == present.Count)
        {
            WriteInt32At(grown, write, rows.Length);
            write += 4;
        }

        int source = 24 + (4 * present.Count);
        Array.Copy(tables, source, grown, write, methodDefStart);
        int inserted = write + methodDefStart;
        for (int i = 0; i < rows.Length; i++)
        {
            WriteUInt16At(grown, inserted + (2 * i), rows[i]);
        }

        Array.Copy(
            tables,
            source + methodDefStart,
            grown,
            inserted + (2 * rows.Length),
            tables.Length - source - methodDefStart);
        return grown;
    }

    /// <summary>
    /// Sizes the tables that can precede MethodDef in this fixture. Every
    /// heap and coded index is two bytes, which the caller asserts.
    /// </summary>
    static int MethodPtrFixtureRowSize(
        int table,
        Dictionary<int, int> counts)
    {
        const int HeapIndex = 2;
        const int TableIndexSize = 2;
        const int CodedTypeDefOrRef = 2;
        foreach (TableIndex related in (TableIndex[])
            [
                TableIndex.TypeDef,
                TableIndex.TypeRef,
                TableIndex.TypeSpec,
            ])
        {
            Assert.True(
                !counts.TryGetValue((int)related, out int rows)
                    || rows < 1 << 14,
                "TypeDefOrRef must stay a two-byte coded index.");
        }

        return table switch
        {
            (int)TableIndex.Module =>
                2 + HeapIndex + HeapIndex + HeapIndex + HeapIndex,
            (int)TableIndex.TypeDef =>
                4
                    + HeapIndex
                    + HeapIndex
                    + CodedTypeDefOrRef
                    + TableIndexSize
                    + TableIndexSize,
            (int)TableIndex.Field => 2 + HeapIndex + HeapIndex,
            _ => throw new InvalidOperationException(
                $"Unexpected table 0x{table:X2} before MethodDef."),
        };
    }

    sealed class MetadataStreamHeader
    {
        public int HeaderPosition { get; init; }

        public int Offset { get; set; }

        public int Size { get; set; }

        public string Name { get; set; } = "";
    }

    static int AlignTo4(int value) => (value + 3) & ~3;

    static ushort ReadUInt16At(byte[] buffer, int position) =>
        (ushort)(buffer[position] | (buffer[position + 1] << 8));

    static uint ReadUInt32At(byte[] buffer, int position) =>
        (uint)(buffer[position]
            | (buffer[position + 1] << 8)
            | (buffer[position + 2] << 16)
            | (buffer[position + 3] << 24));

    static int ReadInt32At(byte[] buffer, int position) =>
        (int)ReadUInt32At(buffer, position);

    static ulong ReadUInt64At(byte[] buffer, int position) =>
        ReadUInt32At(buffer, position)
            | ((ulong)ReadUInt32At(buffer, position + 4) << 32);

    static void WriteUInt16At(byte[] buffer, int position, ushort value)
    {
        buffer[position] = (byte)value;
        buffer[position + 1] = (byte)(value >> 8);
    }

    static void WriteInt32At(byte[] buffer, int position, int value)
    {
        buffer[position] = (byte)value;
        buffer[position + 1] = (byte)(value >> 8);
        buffer[position + 2] = (byte)(value >> 16);
        buffer[position + 3] = (byte)(value >> 24);
    }

    static void WriteUInt64At(byte[] buffer, int position, ulong value)
    {
        WriteInt32At(buffer, position, (int)value);
        WriteInt32At(buffer, position + 4, (int)(value >> 32));
    }

    static byte[] BuildRepeatedTypeNameAssembly(
        string @namespace,
        string name,
        int typeCount)
    {
        var metadata = CreateMetadata(
            "RepeatedTypeNames",
            new Guid(
                "25043D73-D794-4DBE-8738-B52F7888F720"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("SeedHost"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        for (int index = 0; index < typeCount; index++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString(@namespace),
                metadata.GetOrAddString(name),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(2));
        }

        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        AddSyntheticMethod(metadata, encoder, "Seed");
        return Serialize(metadata, bodies);
    }

    static byte[] BuildHostileMemberIdentityAssembly(
        int methodCount,
        int parameterCount,
        int genericArity)
    {
        var metadata = CreateMetadata(
            "HostileMemberIdentity",
            new Guid(
                "AFB9127C-3340-48C3-B6E4-94C3D38370D2"));
        AssemblyReferenceHandle assembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Dependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("T"));
        metadata.AddTypeReference(
            assembly,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("G"));

        var typeSpecSignature = new BlobBuilder();
        typeSpecSignature.WriteByte(0x15);
        typeSpecSignature.WriteByte(0x12);
        typeSpecSignature.WriteCompressedInteger((2 << 2) | 1);
        typeSpecSignature.WriteCompressedInteger(genericArity);
        for (int index = 0; index < genericArity; index++)
        {
            typeSpecSignature.WriteByte(0x12);
            typeSpecSignature.WriteCompressedInteger((1 << 2) | 1);
        }
        TypeSpecificationHandle typeSpec =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(typeSpecSignature));
        int typeSpecCodedIndex =
            (MetadataTokens.GetRowNumber(typeSpec) << 2) | 2;

        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteCompressedInteger(parameterCount);
        signature.WriteByte(0x01);
        for (int index = 0; index < parameterCount; index++)
        {
            signature.WriteByte(0x20);
            signature.WriteCompressedInteger(typeSpecCodedIndex);
            signature.WriteByte(0x08);
        }
        BlobHandle signatureBlob =
            metadata.GetOrAddBlob(signature);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int index = 0; index < methodCount; index++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"M{index}"),
                signatureBlob,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        }

        return Serialize(metadata, new BlobBuilder());
    }

    static byte[] BuildAttributedMethodAssembly(
        int methodCount,
        int attributeCount,
        int attributeTypeNameLength,
        bool useTypeSpecificationParent = false)
    {
        var metadata = CreateMetadata(
            "AttributedMethods",
            new Guid(
                "00EAA672-B296-413F-B968-F4D2F02D94C1"));
        AssemblyReferenceHandle assembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Dependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle attributeType =
            metadata.AddTypeReference(
                assembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString(
                    new string(
                        'A',
                        attributeTypeNameLength)));
        EntityHandle constructorParent = attributeType;
        if (useTypeSpecificationParent)
        {
            var typeSpecSignature = new BlobBuilder();
            typeSpecSignature.WriteByte(0x12);
            typeSpecSignature.WriteCompressedInteger(
                (MetadataTokens.GetRowNumber(attributeType) << 2) | 1);
            constructorParent =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(typeSpecSignature));
        }
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature)
            .MethodSignature(
                SignatureCallingConvention.Default,
                genericParameterCount: 0,
                isInstanceMethod: true)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                parameters => { });
        MemberReferenceHandle constructor =
            metadata.AddMemberReference(
                constructorParent,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(
                    constructorSignature));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString("C"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        BlobHandle attributeValue =
            metadata.GetOrAddBlob(
                new byte[] { 1, 0, 0, 0 });
        for (int index = 0; index < attributeCount; index++)
        {
            metadata.AddCustomAttribute(
                type,
                constructor,
                attributeValue);
        }

        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        for (int index = 0; index < methodCount; index++)
        {
            AddSyntheticMethod(
                metadata,
                encoder,
                $"M{index}");
        }
        return Serialize(metadata, bodies);
    }

    static byte[] BuildRejectedTypeSpecificationAttributeAssembly(
        bool attributeOnMethod)
    {
        var metadata = CreateMetadata(
            "RejectedTypeSpecificationAttribute",
            new Guid(
                "A295F112-21D2-4DF9-A649-D55759429E98"));
        AssemblyReferenceHandle assembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Dependency"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle extensionAttribute =
            metadata.AddTypeReference(
                assembly,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString("ExtensionAttribute"));
        var malformedTypeSpecSignature = new BlobBuilder();
        malformedTypeSpecSignature.WriteByte(0x15);
        TypeSpecificationHandle malformedTypeSpec =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(
                    malformedTypeSpecSignature));
        var constructorSignature = new BlobBuilder();
        new BlobEncoder(constructorSignature)
            .MethodSignature(
                SignatureCallingConvention.Default,
                genericParameterCount: 0,
                isInstanceMethod: true)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                parameters => { });
        BlobHandle constructorSignatureBlob =
            metadata.GetOrAddBlob(constructorSignature);
        MemberReferenceHandle validConstructor =
            metadata.AddMemberReference(
                extensionAttribute,
                metadata.GetOrAddString(".ctor"),
                constructorSignatureBlob);
        MemberReferenceHandle rejectedConstructor =
            metadata.AddMemberReference(
                malformedTypeSpec,
                metadata.GetOrAddString(".ctor"),
                constructorSignatureBlob);

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Sealed,
                default,
                metadata.GetOrAddString("C"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        MethodDefinitionHandle method =
            AddSyntheticMethod(metadata, encoder, "M");
        BlobHandle attributeValue =
            metadata.GetOrAddBlob(
                new byte[] { 1, 0, 0, 0 });
        if (attributeOnMethod)
        {
            metadata.AddCustomAttribute(
                type,
                validConstructor,
                attributeValue);
            metadata.AddCustomAttribute(
                method,
                rejectedConstructor,
                attributeValue);
        }
        else
        {
            metadata.AddCustomAttribute(
                type,
                rejectedConstructor,
                attributeValue);
        }

        return Serialize(metadata, bodies);
    }

    static MetadataBuilder CreateMetadata(
        string assemblyName,
        Guid moduleVersionId)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(moduleVersionId),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        return metadata;
    }

    static byte[] Serialize(
        MetadataBuilder metadata,
        BlobBuilder bodies)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static MethodDefinitionHandle AddSyntheticMethod(
        MetadataBuilder metadata,
        MethodBodyStreamEncoder bodies,
        string name)
    {
        var code = new BlobBuilder();
        code.WriteByte(0x2A);
        int body = bodies.AddMethodBody(
            new InstructionEncoder(code),
            maxStack: 0);
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                parameterCount: 0,
                returnType => returnType.Void(),
                parameters => { });
        return metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            metadata.GetOrAddBlob(signature),
            body,
            MetadataTokens.ParameterHandle(1));
    }

    /// <summary>
    /// Builds an assembly whose module TypeDef starts after the type
    /// that follows it, so its range descends. SRM reports that range
    /// with a negative length while enumerating nothing, which is why
    /// the later type can still cover every MethodDef row on its own.
    /// </summary>
    static byte[] BuildDescendingMethodListAssembly()
    {
        byte[] bytes = BuildMethodPtrAssembly(1, 2);
        WriteMethodListStart(bytes, typeDefRow: 0, start: 2);
        return bytes;
    }

    /// <summary>
    /// Builds the descending column on an image carrying no MethodPtr
    /// table, so the guard is exercised on the plain metadata shape.
    /// The descent throws at the module row, before any row is
    /// projected.
    /// </summary>
    static byte[] BuildDescendingMethodListWithoutMethodPtrAssembly()
    {
        byte[] bytes = BuildTwoTypeTwoMethodAssembly();
        WriteMethodListStart(bytes, typeDefRow: 0, start: 2);
        return bytes;
    }

    /// <summary>
    /// Builds a column that starts past MethodDef row 1 while staying
    /// non-decreasing and in range, so every method range reports a
    /// non-negative length and only the coverage requirement rejects
    /// the image. This is the half of the ordering proof that the
    /// range-length check does not supply.
    /// </summary>
    static byte[] BuildFirstStartPastRowOneAssembly()
    {
        byte[] bytes = BuildTwoTypeTwoMethodAssembly();
        WriteMethodListStart(bytes, typeDefRow: 0, start: 2);
        WriteMethodListStart(bytes, typeDefRow: 1, start: 2);
        return bytes;
    }

    /// <summary>
    /// Reports the length SRM computes for each TypeDef's method range,
    /// so a fixture can pin which row carries the negative length rather
    /// than leaving that to a comment.
    /// </summary>
    static int[] MeasureMethodRangeLengths(ImmutableArray<byte> image)
    {
        using var peReader = new PEReader(image);
        MetadataReader reader = peReader.GetMetadataReader();
        return [.. reader.TypeDefinitions.Select(
            handle => reader.GetTypeDefinition(handle)
                .GetMethods()
                .Count)];
    }

    /// <summary>
    /// Builds a column whose starts all sit two rows past the end of the
    /// projected method table. Every earlier range then reports length
    /// zero and enumerates nothing, so the *final* range is the one that
    /// reports a negative length. This isolates the end of the derived
    /// bound, which the descending fixtures never reach because they
    /// throw at the module row before any row is projected.
    /// </summary>
    static byte[] BuildStartPastProjectedTableAssembly()
    {
        byte[] bytes = BuildTwoTypeTwoMethodAssembly();
        WriteMethodListStart(bytes, typeDefRow: 0, start: 4);
        WriteMethodListStart(bytes, typeDefRow: 1, start: 4);
        return bytes;
    }

    /// <summary>
    /// Builds a well-formed image whose module TypeDef and
    /// <c>N.Fixture</c> both start at MethodDef row 1, carrying no
    /// MethodPtr table.
    /// </summary>
    static byte[] BuildTwoTypeTwoMethodAssembly()
    {
        MetadataBuilder metadata = CreateMetadata(
            "DescendingNoMethodPtr",
            new Guid("2B7E1C4A-58D3-4F60-9A21-7C0E5D8B3F94"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Class | TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Fixture"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        AddSyntheticMethod(metadata, encoder, "M0");
        AddSyntheticMethod(metadata, encoder, "M1");

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    /// <summary>
    /// Builds the descending column on a MethodPtr image whose pointer
    /// rows reverse the MethodDef order, so the guard is exercised on
    /// the unoptimized metadata shape. The descent still throws at the
    /// module row, so the reordering itself is never projected.
    /// </summary>
    static byte[] BuildDescendingReorderedMethodPtrAssembly()
    {
        byte[] bytes = BuildMethodPtrAssembly(2, 1);
        WriteMethodListStart(bytes, typeDefRow: 0, start: 2);
        return bytes;
    }

    static int MeasureLeafDeclaringDepth(
        ImmutableArray<byte> image,
        string leafName)
    {
        using var reader = new PEReader(image);
        MetadataReader metadata = reader.GetMetadataReader();
        foreach (TypeDefinitionHandle handle in
            metadata.TypeDefinitions)
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);
            if (!metadata.GetString(type.Name).Equals(
                    leafName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            int depth = 1;
            TypeDefinition current = type;
            while (true)
            {
                TypeDefinitionHandle declaring =
                    current.GetDeclaringType();
                if (declaring.IsNil)
                {
                    return depth;
                }

                current = metadata.GetTypeDefinition(declaring);
                depth++;
            }
        }

        return 0;
    }

    /// <summary>
    /// Builds a column carrying a null start *after* a populated run.
    /// ECMA-335 II.22.37 delimits each run by the following TypeDef's
    /// start, so a null there truncates the preceding run rather than
    /// describing an empty one. Only a leading null is expressible.
    /// </summary>
    static byte[] BuildNullMethodListAfterPopulatedRunAssembly()
    {
        byte[] bytes = BuildTwoTypeTwoMethodAssembly();
        WriteMethodListStart(bytes, typeDefRow: 1, start: 0);
        return bytes;
    }

    static byte[] BuildNullMethodListAssembly()
    {
        // ECMA-335 II.22.37 permits a null MethodList. The module
        // pseudo-type owns no methods, so the run is genuinely empty
        // and the following type still covers the table exactly.
        byte[] bytes = BuildMethodPtrAssembly(1, 2);
        WriteMethodListStart(bytes, typeDefRow: 0, start: 0);
        return bytes;
    }

    /// <summary>
    /// Builds an assembly whose types repeat one short leaf name, so
    /// every candidate passes the cheap leaf comparison and reaches the
    /// declaring-chain walk.
    /// </summary>
    static byte[] BuildRepeatedLeafNameAssembly(
        int leaves,
        int depth = 1)
    {
        MetadataBuilder metadata = CreateMetadata(
            "RepeatedLeafName",
            new Guid("0C7A1E93-5B26-4D8F-A410-2F6B93E7C051"));
        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        MethodDefinitionHandle seed =
            AddSyntheticMethod(metadata, encoder, "Seed");
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: seed);
        // Enclose the leaves in a chain so a leaf match actually walks
        // a declaring chain. A depth-1 fixture leaves the traversal
        // this budget is meant to bound completely unexercised.
        StringHandle enclosing = metadata.GetOrAddString("E");
        var chain = new List<TypeDefinitionHandle>();
        for (int d = 0; d < depth - 1; d++)
        {
            chain.Add(
                metadata.AddTypeDefinition(
                    d == 0
                        ? TypeAttributes.Public
                        : TypeAttributes.NestedPublic,
                    default,
                    enclosing,
                    baseType: default,
                    fieldList: MetadataTokens.FieldDefinitionHandle(1),
                    methodList: seed));
        }

        StringHandle leaf = metadata.GetOrAddString("C");
        var nested = new List<TypeDefinitionHandle>();
        for (int i = 0; i < leaves; i++)
        {
            nested.Add(
                metadata.AddTypeDefinition(
                    chain.Count == 0
                        ? TypeAttributes.Public
                        : TypeAttributes.NestedPublic,
                    default,
                    leaf,
                    baseType: default,
                    fieldList: MetadataTokens.FieldDefinitionHandle(1),
                    methodList: seed));
        }

        // NestedClass rows must ascend by nested type, and every rid
        // added above already ascends.
        for (int d = 1; d < chain.Count; d++)
        {
            metadata.AddNestedType(chain[d], chain[d - 1]);
        }

        if (chain.Count > 0)
        {
            foreach (TypeDefinitionHandle type in nested)
            {
                metadata.AddNestedType(type, chain[^1]);
            }
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildUncoveredMethodPtrAssembly() =>
        BuildMethodPtrAssembly([1, 2, 1, 2]);

    /// <summary>
    /// Builds metadata with no TypeDef rows at all, so it lacks the
    /// mandatory module pseudo-type.
    /// </summary>
    static byte[] BuildTypeDeflessAssembly()
    {
        MetadataBuilder metadata = CreateMetadata(
            "TypeDefless",
            new Guid("6B1C0A55-2E4D-4F0A-9C77-13D8E5A96C42"));
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    /// <summary>
    /// Builds a type holding one healthy method and one sibling whose
    /// signature blob cannot be decoded, so the seed scan sees a match
    /// and a rejection in the same type.
    /// </summary>
    static byte[] BuildBrokenSiblingAssembly()
    {
        MetadataBuilder metadata = CreateMetadata(
            "BrokenSibling",
            new Guid("D2F4A0E7-88B3-4C6E-9A15-7C0E3B45D918"));
        var bodies = new BlobBuilder();
        var encoder = new MethodBodyStreamEncoder(bodies);
        MethodDefinitionHandle healthy =
            AddSyntheticMethod(metadata, encoder, "Seed");

        var code = new BlobBuilder();
        code.WriteByte(0x2A);
        int body = encoder.AddMethodBody(
            new InstructionEncoder(code),
            maxStack: 0);

        // A method signature that claims one parameter and then ends,
        // so decoding the sibling throws while the healthy method above
        // still matches the seed anchor.
        var malformed = new BlobBuilder();
        malformed.WriteByte(0x00);
        malformed.WriteByte(0x01);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Broken"),
            metadata.GetOrAddBlob(malformed),
            body,
            MetadataTokens.ParameterHandle(1));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: healthy);
        metadata.AddTypeDefinition(
            TypeAttributes.Class | TypeAttributes.Public,
            default,
            metadata.GetOrAddString("C"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: healthy);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                AssemblyBindingFailureKind
                .CandidateUnavailable));
        }
    }
}
