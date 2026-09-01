using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Analysis.StructuralCloneFixtures;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis.Tests;

public class StructuralCloneRetrievalTests
{
    [Fact]
    public void RetrieveSimilar_NullImageNamesPublicParameter()
    {
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(() =>
                StructuralCloneAnalysis.RetrieveSimilar(
                    null!,
                    default,
                    []));

        Assert.Equal("image", exception.ParamName);
    }

    [Fact]
    public void RetrieveSimilar_MetadataFreeImagesNameCrossImageParameters()
    {
        using var metadataFree = new PEReader(
            new MemoryStream(BuildMetadataFreeImage()));
        using PEReader managed = OpenFixture();

        ArgumentException seedException =
            Assert.Throws<ArgumentException>(() =>
                StructuralCloneAnalysis.RetrieveSimilar(
                    metadataFree,
                    default,
                    managed,
                    []));
        Assert.Equal("seedImage", seedException.ParamName);

        ArgumentException candidateException =
            Assert.Throws<ArgumentException>(() =>
                StructuralCloneAnalysis.RetrieveSimilar(
                    managed,
                    Method(nameof(StructuralCloneFixture.ExactPositiveA)),
                    metadataFree,
                    []));
        Assert.Equal("candidateImage", candidateException.ParamName);
    }

    [Fact]
    public void RetrieveSimilar_MalformedMetadataRootPreservesReason()
    {
        byte[] bytes = File.ReadAllBytes(
            typeof(StructuralCloneFixture).Assembly.Location);
        using (var valid = new PEReader(
            new MemoryStream(bytes, writable: false)))
        {
            bytes[valid.PEHeaders.MetadataStartOffset] = 0;
        }
        using var image = new PEReader(
            new MemoryStream(bytes, writable: false));

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                MetadataTokens.MethodDefinitionHandle(1),
                [MetadataTokens.MethodDefinitionHandle(2)]);

        StructuralCloneRetrievalBlocker blocker =
            Assert.Single(result.Blockers);
        Assert.Equal(
            MetadataRootMalformedReason.InvalidSignature,
            blocker.MetadataRootReason);
    }

    [Fact]
    public void RetrieveSimilar_RanksExactAndNearPeersAboveHardNegative()
    {
        using PEReader image = OpenFixture();
        ImmutableArray<MethodDefinitionHandle> population = Population();

        StructuralCloneRetrievalResult exact =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.ExactPositiveA)),
                population);
        StructuralCloneRetrievalResult near =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.NearConstantA)),
                population);

        Assert.Equal(
            StructuralCloneRetrievalDisposition.Completed,
            exact.Disposition);
        StructuralCloneRetrievalCandidate exactPeer = Candidate(
            exact,
            nameof(StructuralCloneFixture.ExactPositiveB));
        Assert.Equal(1, exactPeer.Rank);
        Assert.Equal(10_000, exactPeer.Similarity.Score);

        StructuralCloneRetrievalCandidate nearPeer = Candidate(
            near,
            nameof(StructuralCloneFixture.NearConstantB));
        StructuralCloneRetrievalCandidate hardNegative = Candidate(
            near,
            nameof(StructuralCloneFixture.NearHardNegativeA));
        Assert.InRange(nearPeer.Rank, 1, 5);
        Assert.True(
            nearPeer.Similarity.Score
                > hardNegative.Similarity.Score);
        Assert.Equal(
            near.Receipt.ReturnedCandidates,
            near.Candidates.Length);
        Assert.True(near.Receipt.UnsupportedMethods > 0);
    }

    [Fact]
    public void RetrieveSimilar_InputOrderDoesNotChangeRanking()
    {
        using PEReader image = OpenFixture();
        ImmutableArray<MethodDefinitionHandle> population = Population();
        StructuralCloneRetrievalResult forward =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.NearCallTargetA)),
                population);
        StructuralCloneRetrievalResult reverse =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.NearCallTargetA)),
                [.. population.Reverse()]);

        Assert.Equal(
            forward.Candidates.Select(CandidateKey),
            reverse.Candidates.Select(CandidateKey));
    }

    [Fact]
    public void RetrieveSimilar_ResultLimitIsDeterministicAndVisible()
    {
        using PEReader image = OpenFixture();

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.NearConstantA)),
                Population(),
                new StructuralCloneRetrievalLimits(
                    MaximumResults: 2));

        Assert.Equal(
            StructuralCloneRetrievalDisposition.Completed,
            result.Disposition);
        Assert.Equal(2, result.Candidates.Length);
        Assert.Equal([1, 2], result.Candidates.Select(static item =>
            item.Rank));
        Assert.Equal(
            result.Receipt.RankedCandidates
                - result.Receipt.ReturnedCandidates,
            result.Receipt.SuppressedCandidates);
    }

    [Fact]
    public void RetrieveSimilar_MethodLimitIsAtomic()
    {
        using PEReader image = OpenFixture();

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.NearConstantA)),
                Population(),
                new StructuralCloneRetrievalLimits(
                    MaximumMethods: 1));

        Assert.Equal(
            StructuralCloneRetrievalDisposition.LimitReached,
            result.Disposition);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, result.Receipt.BodyProductions);
        Assert.Equal(
            Population().Length - 1,
            result.Receipt.SuppressedCandidates);
        Assert.Contains(
            result.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneRetrievalBlockerKind.MethodLimit);
    }

    [Fact]
    public void RetrieveSimilar_UnsupportedSeedIsExplicit()
    {
        using PEReader image = OpenFixture();

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.ExceptionHandlingA)),
                Population());

        Assert.Equal(
            StructuralCloneRetrievalDisposition.Unsupported,
            result.Disposition);
        Assert.Equal(
            StructuralCloneDisposition.Unsupported,
            result.Seed.Disposition);
        Assert.Empty(result.Candidates);
        Assert.Equal(
            Population().Length - 1,
            result.Receipt.SuppressedCandidates);
        Assert.Contains(
            result.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneRetrievalBlockerKind.SeedUnsupported);
    }

    [Fact]
    public void RetrieveSimilar_PartialRankingRetainsVisibleLimit()
    {
        using PEReader image = OpenFixture();

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.NearConstantA)),
                Population(),
                new StructuralCloneRetrievalLimits(
                    ComparisonLimits:
                        new StructuralCloneComparisonLimits(
                            MaximumBlocks: 1)));

        Assert.Equal(
            StructuralCloneRetrievalDisposition.LimitReached,
            result.Disposition);
        Assert.NotEmpty(result.Candidates);
        Assert.True(result.Receipt.LimitReachedMethods > 0);
        Assert.True(
            result.Receipt.SuppressedCandidates
                >= result.Receipt.LimitReachedMethods);
        Assert.Contains(
            result.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneRetrievalBlockerKind
                        .CandidateProductionLimit);
    }

    [Fact]
    public void RetrieveSimilar_SeedNeedNotBeInPopulation()
    {
        using PEReader image = OpenFixture();
        MethodDefinitionHandle seed =
            Method(nameof(StructuralCloneFixture.ExactPositiveA));
        ImmutableArray<MethodDefinitionHandle> population =
        [
            Method(nameof(StructuralCloneFixture.ExactPositiveB)),
        ];

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                seed,
                population);

        Assert.Equal(
            StructuralCloneRetrievalDisposition.Completed,
            result.Disposition);
        Assert.Equal(1, result.Receipt.InputMethods);
        Assert.Equal(1, result.Receipt.ProcessedMethods);
        Assert.Equal(2, result.Receipt.BodyProductions);
        Assert.Equal(
            Method(nameof(StructuralCloneFixture.ExactPositiveB)),
            Assert.Single(result.Candidates).Method.Handle);
    }

    [Fact]
    public void RetrieveSimilar_CrossAssemblyScopesCandidateIdentity()
    {
        using PEReader seedImage = OpenDiffFixture(
            FixtureCatalog.DiffPair.Old);
        using PEReader candidateImage = OpenDiffFixture(
            FixtureCatalog.DiffPair.New);
        MetadataReader seedReader = seedImage.GetMetadataReader();
        MetadataReader candidateReader =
            candidateImage.GetMetadataReader();
        MethodDefinitionHandle seed = DiffMethod(
            seedReader,
            "Stable");
        MethodDefinitionHandle expected = DiffMethod(
            candidateReader,
            "Stable");

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                seedImage,
                seed,
                candidateImage,
                DiffPopulation(candidateReader));

        Assert.Equal(
            StructuralCloneRetrievalDisposition.Completed,
            result.Disposition);
        Assert.NotEqual(
            result.Seed.Method.ModuleVersionId,
            candidateReader.GetGuid(
                candidateReader.GetModuleDefinition().Mvid));
        StructuralCloneRetrievalCandidate candidate =
            Assert.Single(
                result.Candidates,
                item => item.Method.Handle == expected);
        Assert.Equal(
            candidateReader.GetGuid(
                candidateReader.GetModuleDefinition().Mvid),
            candidate.Method.ModuleVersionId);
        Assert.Equal(10_000, candidate.Similarity.Score);
    }

    [Fact]
    public void RetrieveSimilar_CrossAssemblyInputOrderIsDeterministic()
    {
        using PEReader seedImage = OpenDiffFixture(
            FixtureCatalog.DiffPair.Old);
        using PEReader candidateImage = OpenDiffFixture(
            FixtureCatalog.DiffPair.New);
        MetadataReader seedReader = seedImage.GetMetadataReader();
        MetadataReader candidateReader =
            candidateImage.GetMetadataReader();
        MethodDefinitionHandle seed = DiffMethod(
            seedReader,
            "MultipleHunks");
        ImmutableArray<MethodDefinitionHandle> population =
            DiffPopulation(candidateReader);

        StructuralCloneRetrievalResult forward =
            StructuralCloneAnalysis.RetrieveSimilar(
                seedImage,
                seed,
                candidateImage,
                population);
        StructuralCloneRetrievalResult reverse =
            StructuralCloneAnalysis.RetrieveSimilar(
                seedImage,
                seed,
                candidateImage,
                [.. population.Reverse()]);

        Assert.Equal(
            forward.Candidates.Select(CandidateKey),
            reverse.Candidates.Select(CandidateKey));
    }

    [Fact]
    public void RetrieveSimilar_CrossAssemblyCandidateFailurePreservesSeed()
    {
        using PEReader seedImage = OpenDiffFixture(
            FixtureCatalog.DiffPair.Old);
        using var candidateImage = new PEReader(
            new MemoryStream([1, 2, 3, 4]));
        MetadataReader seedReader = seedImage.GetMetadataReader();
        MethodDefinitionHandle seed = DiffMethod(
            seedReader,
            "Stable");

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                seedImage,
                seed,
                candidateImage,
                [MetadataTokens.MethodDefinitionHandle(1)]);

        Assert.Equal(
            StructuralCloneRetrievalDisposition.Failed,
            result.Disposition);
        Assert.Equal(
            StructuralCloneDisposition.Completed,
            result.Seed.Disposition);
        Assert.Equal(
            seedReader.GetGuid(
                seedReader.GetModuleDefinition().Mvid),
            result.Seed.Method.ModuleVersionId);
        Assert.Equal(1, result.Receipt.BodyProductions);
        Assert.Contains(
            result.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneRetrievalBlockerKind
                        .MetadataReadFailure
                && blocker.Detail.StartsWith(
                    "The candidate ",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void RetrieveSimilar_InvalidCrossAssemblyPopulationPrecedesUnsupportedSeed()
    {
        using PEReader seedImage = OpenFixture();
        using PEReader candidateImage = OpenDiffFixture(
            FixtureCatalog.DiffPair.New);
        MetadataReader candidateReader =
            candidateImage.GetMetadataReader();
        MethodDefinitionHandle invalidCandidate =
            MetadataTokens.MethodDefinitionHandle(
                candidateReader.GetTableRowCount(TableIndex.MethodDef) + 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StructuralCloneAnalysis.RetrieveSimilar(
                seedImage,
                Method(nameof(StructuralCloneFixture.ExceptionHandlingA)),
                candidateImage,
                [invalidCandidate]));
    }

    [Fact]
    public void RetrieveSimilar_InvalidPopulationPrecedesMalformedCandidateMvid()
    {
        using PEReader seedImage = OpenFixture();
        using var candidateImage = new PEReader(
            new MemoryStream(
                BuildZeroScoreAssembly(malformedModuleIdentity: true)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StructuralCloneAnalysis.RetrieveSimilar(
                seedImage,
                Method(nameof(StructuralCloneFixture.ExceptionHandlingA)),
                candidateImage,
                [MetadataTokens.MethodDefinitionHandle(3)]));
    }

    [Fact]
    public void RetrieveSimilar_InvalidPopulationPrecedesMalformedSeedMvid()
    {
        using var seedImage = new PEReader(
            new MemoryStream(
                BuildZeroScoreAssembly(malformedModuleIdentity: true)));
        MethodDefinitionHandle invalid =
            MetadataTokens.MethodDefinitionHandle(3);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StructuralCloneAnalysis.RetrieveSimilar(
                seedImage,
                MetadataTokens.MethodDefinitionHandle(1),
                [invalid]));

        using PEReader candidateImage = OpenDiffFixture(
            FixtureCatalog.DiffPair.New);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StructuralCloneAnalysis.RetrieveSimilar(
                seedImage,
                MetadataTokens.MethodDefinitionHandle(1),
                candidateImage,
                [MetadataTokens.MethodDefinitionHandle(
                    candidateImage.GetMetadataReader()
                        .GetTableRowCount(TableIndex.MethodDef)
                        + 1)]));
    }

    [Fact]
    public void RetrieveSimilar_ByteDistinctSameMvidImagesRetainCandidate()
    {
        byte[] seedBytes =
            BuildZeroScoreAssembly(assemblyName: "MvidCollisionLeft");
        byte[] candidateBytes =
            BuildZeroScoreAssembly(assemblyName: "MvidCollisionRight");
        Assert.False(seedBytes.SequenceEqual(candidateBytes));
        using var seedImage = new PEReader(new MemoryStream(seedBytes));
        using var candidateImage =
            new PEReader(new MemoryStream(candidateBytes));
        MethodDefinitionHandle seed =
            MetadataTokens.MethodDefinitionHandle(1);

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                seedImage,
                seed,
                candidateImage,
                [seed]);

        StructuralCloneRetrievalCandidate candidate =
            Assert.Single(result.Candidates);
        Assert.Equal(seed, candidate.Method.Handle);
        Assert.Equal(10_000, candidate.Similarity.Score);
        Assert.Equal(1, result.Receipt.ProcessedMethods);
    }

    [Fact]
    public void RetrieveSimilar_RejectsDuplicatePopulationHandles()
    {
        using PEReader image = OpenFixture();
        MethodDefinitionHandle method =
            Method(nameof(StructuralCloneFixture.ExactPositiveB));

        Assert.Throws<ArgumentException>(() =>
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                Method(nameof(StructuralCloneFixture.ExactPositiveA)),
                [method, method]));
    }

    [Fact]
    public void RetrieveSimilar_ScoreDoesNotEstablishRelationship()
    {
        using PEReader image = OpenFixture();
        MethodDefinitionHandle seed =
            Method(nameof(StructuralCloneFixture.NearConstantA));
        MethodDefinitionHandle hardNegative =
            Method(nameof(StructuralCloneFixture.NearHardNegativeA));

        StructuralCloneRetrievalCandidate candidate = Candidate(
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                seed,
                Population()),
            nameof(StructuralCloneFixture.NearHardNegativeA));
        StructuralCloneComparison comparison =
            StructuralCloneAnalysis.Compare(image, seed, hardNegative);

        Assert.True(candidate.Similarity.Score > 0);
        Assert.Equal(
            StructuralCloneRelation.Different,
            comparison.Relation);
    }

    [Fact]
    public void RetrieveSimilar_RanksZeroScoreEligibleCandidate()
    {
        using var image = new PEReader(
            new MemoryStream(BuildZeroScoreAssembly()));
        MethodDefinitionHandle seed =
            MetadataTokens.MethodDefinitionHandle(1);
        MethodDefinitionHandle candidate =
            MetadataTokens.MethodDefinitionHandle(2);

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                seed,
                [seed, candidate]);

        StructuralCloneRetrievalCandidate ranked =
            Assert.Single(result.Candidates);
        Assert.Equal(candidate, ranked.Method.Handle);
        Assert.Equal(1, ranked.Rank);
        Assert.Equal(0, ranked.Similarity.Score);
        Assert.Equal(1, result.Receipt.RankedCandidates);
    }

    [Fact]
    public void RetrieveSimilar_MalformedModuleIdentityFailsVisibly()
    {
        using var image = new PEReader(
            new MemoryStream(
                BuildZeroScoreAssembly(malformedModuleIdentity: true)));
        MethodDefinitionHandle seed =
            MetadataTokens.MethodDefinitionHandle(1);

        StructuralCloneRetrievalResult result =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                seed,
                [seed, MetadataTokens.MethodDefinitionHandle(2)]);

        Assert.Equal(
            StructuralCloneRetrievalDisposition.Failed,
            result.Disposition);
        Assert.Empty(result.Candidates);
        Assert.Equal(1, result.Receipt.SuppressedCandidates);
        Assert.Contains(
            result.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneRetrievalBlockerKind
                        .MetadataReadFailure);
    }

    static PEReader OpenFixture()
        => new(File.OpenRead(
            typeof(StructuralCloneFixture).Assembly.Location));

    static PEReader OpenDiffFixture(FixtureDefinition fixture)
        => new(File.OpenRead(fixture.AssemblyPath()));

    static ImmutableArray<MethodDefinitionHandle> DiffPopulation(
        MetadataReader reader)
    {
        TypeDefinition type = reader.GetTypeDefinition(
            reader.TypeDefinitions.Single(handle =>
            {
                TypeDefinition candidate =
                    reader.GetTypeDefinition(handle);
                return reader.GetString(candidate.Namespace)
                        == "DiffFixtureSample"
                    && reader.GetString(candidate.Name) == "DiffSample";
            }));
        return [.. type.GetMethods()];
    }

    static MethodDefinitionHandle DiffMethod(
        MetadataReader reader,
        string name)
        => DiffPopulation(reader).Single(handle =>
            reader.GetString(
                reader.GetMethodDefinition(handle).Name) == name);

    static ImmutableArray<MethodDefinitionHandle> Population()
        =>
        [
            .. typeof(StructuralCloneFixture)
                .GetMethods(
                    BindingFlags.Public
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)
                .Select(static method =>
                    MetadataTokens.MethodDefinitionHandle(
                        method.MetadataToken & 0x00FFFFFF)),
        ];

    static MethodDefinitionHandle Method(string name)
        => MetadataTokens.MethodDefinitionHandle(
            typeof(StructuralCloneFixture)
                .GetMethod(
                    name,
                    BindingFlags.Public
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)!
                .MetadataToken
                & 0x00FFFFFF);

    static StructuralCloneRetrievalCandidate Candidate(
        StructuralCloneRetrievalResult result,
        string name)
    {
        MethodDefinitionHandle handle = Method(name);
        return Assert.Single(
            result.Candidates,
            item => item.Method.Handle == handle);
    }

    static string CandidateKey(
        StructuralCloneRetrievalCandidate candidate)
        => $"{candidate.Rank}:"
            + $"{MetadataTokens.GetToken(candidate.Method.Handle):X8}:"
            + $"{candidate.Similarity.Score}";

    static byte[] BuildZeroScoreAssembly(
        bool malformedModuleIdentity = false,
        string assemblyName = "ZeroScore")
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString($"{assemblyName}.dll"),
            malformedModuleIdentity
                ? MetadataTokens.GuidHandle(999)
                : metadata.GetOrAddGuid(
                    new Guid(
                        "39BC1613-D15A-4792-B023-E875D0F24891")),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
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
        AddSyntheticMethod(
            metadata,
            encoder,
            "Seed",
            [0x2A],
            [0x07, 0x01, 0x08]);
        AddSyntheticMethod(
            metadata,
            encoder,
            "Candidate",
            [0x2B, 0x00, 0x14, 0x7A],
            [0x07, 0x01, 0x0E]);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: malformedModuleIdentity),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildMetadataFreeImage()
    {
        byte[] bytes = File.ReadAllBytes(
            typeof(StructuralCloneFixture).Assembly.Location);
        using var image = new PEReader(new MemoryStream(bytes));
        PEHeader header = image.PEHeaders.PEHeader!;
        int directoryBase =
            image.PEHeaders.PEHeaderStartOffset
            + (header.Magic == PEMagic.PE32Plus ? 112 : 96);
        Array.Clear(bytes, directoryBase + (14 * 8), 8);
        return bytes;
    }

    static void AddSyntheticMethod(
        MetadataBuilder metadata,
        MethodBodyStreamEncoder bodies,
        string name,
        byte[] il,
        byte[] localSignature)
    {
        StandaloneSignatureHandle locals =
            metadata.AddStandaloneSignature(
                metadata.GetOrAddBlob(localSignature));
        var code = new BlobBuilder(il.Length);
        code.WriteBytes(il);
        int body = bodies.AddMethodBody(
            new InstructionEncoder(code),
            maxStack: 1,
            localVariablesSignature: locals,
            attributes: MethodBodyAttributes.InitLocals);
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
}
