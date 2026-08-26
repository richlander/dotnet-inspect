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

    static void AddSyntheticMethod(
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
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            metadata.GetOrAddBlob(signature),
            body,
            MetadataTokens.ParameterHandle(1));
    }

    sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind
                        .CandidateUnavailable));
    }
}
