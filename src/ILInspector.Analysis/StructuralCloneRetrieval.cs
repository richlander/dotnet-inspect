using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis;

/// <summary>The execution disposition of one seeded clone-retrieval run.</summary>
public enum StructuralCloneRetrievalDisposition
{
    Completed,
    Unsupported,
    LimitReached,
    Failed,
}

/// <summary>Typed reasons why seeded clone retrieval did not complete.</summary>
public enum StructuralCloneRetrievalBlockerKind
{
    MetadataReadFailure,
    MethodLimit,
    SeedUnsupported,
    SeedProductionLimit,
    SeedProductionFailure,
    CandidateProductionLimit,
    CandidateProductionFailure,
}

/// <summary>One visible retrieval blocker.</summary>
public sealed record StructuralCloneRetrievalBlocker(
    StructuralCloneRetrievalBlockerKind Kind,
    string Detail);

/// <summary>One side-free body-production outcome from retrieval.</summary>
public sealed record StructuralCloneRetrievalMethodOutcome(
    MetadataMethodAddress Method,
    StructuralCloneDisposition Disposition,
    ImmutableArray<StructuralCloneBlocker> Blockers,
    StructuralCloneMethodReceipt Receipt);

/// <summary>
/// Product-owned similarity evidence. Scores range from zero through 10,000.
/// Similarity selects candidates and never establishes a clone relation.
/// </summary>
public sealed record StructuralCloneSimilarityEvidence(
    int Score,
    int OperationScore,
    int PositionScore,
    int BlockScore,
    int EdgeScore,
    int LocalScore,
    int SeedInstructions,
    int CandidateInstructions,
    int SeedBlocks,
    int CandidateBlocks,
    int SeedEdges,
    int CandidateEdges,
    int SeedLocals,
    int CandidateLocals);

/// <summary>One deterministically ranked same-PE clone candidate.</summary>
public sealed record StructuralCloneRetrievalCandidate(
    int Rank,
    MetadataMethodAddress Method,
    StructuralCloneSimilarityEvidence Similarity);

/// <summary>Bounded-work receipt for one seeded retrieval run.</summary>
public sealed record StructuralCloneRetrievalReceipt(
    int InputMethods,
    int ProcessedMethods,
    int SuppressedCandidates,
    int EligibleMethods,
    int UnsupportedMethods,
    int LimitReachedMethods,
    int FailedMethods,
    int RankedCandidates,
    int ReturnedCandidates,
    int BodyProductions);

/// <summary>Resource limits for seeded structural-clone retrieval.</summary>
public sealed record StructuralCloneRetrievalLimits(
    int MaximumMethods = 50_000,
    int MaximumResults = 100,
    StructuralCloneComparisonLimits? ComparisonLimits = null);

/// <summary>Product-owned result for one bounded seeded A-vs-A retrieval.</summary>
public sealed record StructuralCloneRetrievalResult
{
    internal StructuralCloneRetrievalResult(
        StructuralCloneRetrievalDisposition disposition,
        StructuralCloneRetrievalMethodOutcome seed,
        ImmutableArray<StructuralCloneRetrievalCandidate> candidates,
        ImmutableArray<StructuralCloneRetrievalMethodOutcome> methods,
        ImmutableArray<StructuralCloneRetrievalBlocker> blockers,
        StructuralCloneRetrievalReceipt receipt)
    {
        if (disposition == StructuralCloneRetrievalDisposition.Completed
            && !blockers.IsEmpty)
        {
            throw new ArgumentException(
                "Completed retrieval cannot carry blockers.",
                nameof(blockers));
        }
        if (disposition != StructuralCloneRetrievalDisposition.Completed
            && blockers.IsEmpty)
        {
            throw new ArgumentException(
                "Non-completed retrieval requires a blocker.",
                nameof(blockers));
        }
        Disposition = disposition;
        Seed = seed;
        Candidates = candidates;
        Methods = methods;
        Blockers = blockers;
        Receipt = receipt;
    }

    public StructuralCloneRetrievalDisposition Disposition { get; }
    public StructuralCloneRetrievalMethodOutcome Seed { get; }
    public ImmutableArray<StructuralCloneRetrievalCandidate> Candidates
    {
        get;
    }
    public ImmutableArray<StructuralCloneRetrievalMethodOutcome> Methods
    {
        get;
    }
    public ImmutableArray<StructuralCloneRetrievalBlocker> Blockers { get; }
    public StructuralCloneRetrievalReceipt Receipt { get; }
}

public static partial class StructuralCloneAnalysis
{
    /// <summary>
    /// Ranks likely structural-clone peers for one seed in a caller-supplied
    /// same-PE population. Similarity is retrieval evidence only; callers use
    /// <see cref="Compare(PEReader, MethodDefinitionHandle, MethodDefinitionHandle, StructuralCloneComparisonLimits?)"/>
    /// to establish a relationship.
    /// </summary>
    public static StructuralCloneRetrievalResult RetrieveSimilar(
        PEReader image,
        MethodDefinitionHandle seed,
        ImmutableArray<MethodDefinitionHandle> methods,
        StructuralCloneRetrievalLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (methods.IsDefault)
        {
            throw new ArgumentException(
                "The retrieval population must be initialized.",
                nameof(methods));
        }

        limits ??= new StructuralCloneRetrievalLimits();
        ValidateRetrievalLimits(limits);
        int potentialCandidates =
            methods.Length - (methods.Contains(seed) ? 1 : 0);
        StructuralCloneComparisonLimits comparisonLimits =
            limits.ComparisonLimits
            ?? new StructuralCloneComparisonLimits();
        ValidateLimits(comparisonLimits);

        if (methods.Length > limits.MaximumMethods)
        {
            return EmptyRetrieval(
                seed,
                methods.Length,
                potentialCandidates,
                StructuralCloneRetrievalDisposition.LimitReached,
                new StructuralCloneRetrievalBlocker(
                    StructuralCloneRetrievalBlockerKind.MethodLimit,
                    $"Method population {methods.Length} exceeds "
                        + $"{limits.MaximumMethods}."));
        }

        MethodDefinitionHandle[] orderedMethods =
        [
            .. methods.OrderBy(static method =>
                MetadataTokens.GetRowNumber(method)),
        ];
        for (int index = 1; index < orderedMethods.Length; index++)
        {
            if (orderedMethods[index] == orderedMethods[index - 1])
            {
                throw new ArgumentException(
                    "The retrieval population cannot contain duplicate handles.",
                    nameof(methods));
            }
        }

        if (!TryGetMetadataReader(
                image,
                out MetadataReader reader,
                out StructuralCloneMetadataFailure metadataFailure))
        {
            return FailedRetrieval(
                seed,
                methods.Length,
                potentialCandidates,
                metadataFailure);
        }
        ValidateHandle(reader, seed, nameof(seed));
        foreach (MethodDefinitionHandle method in orderedMethods)
            ValidateHandle(reader, method, nameof(methods));
        if (!TryGetModuleVersionId(
                reader,
                out Guid moduleVersionId,
                out metadataFailure))
        {
            return FailedRetrieval(
                seed,
                methods.Length,
                potentialCandidates,
                metadataFailure);
        }

        MetadataMethodAddress seedAddress = new(moduleVersionId, seed);
        BodyProduction seedProduction = Produce(
            image,
            reader,
            seedAddress,
            StructuralCloneSide.Both,
            comparisonLimits);
        StructuralCloneRetrievalMethodOutcome seedOutcome =
            RetrievalMethodOutcome(seedAddress, seedProduction);
        if (seedProduction.Disposition
            != StructuralCloneDisposition.Completed)
        {
            StructuralCloneRetrievalDisposition disposition =
                seedProduction.Disposition switch
                {
                    StructuralCloneDisposition.Unsupported =>
                        StructuralCloneRetrievalDisposition.Unsupported,
                    StructuralCloneDisposition.LimitReached =>
                        StructuralCloneRetrievalDisposition.LimitReached,
                    _ => StructuralCloneRetrievalDisposition.Failed,
                };
            StructuralCloneRetrievalBlockerKind kind =
                seedProduction.Disposition switch
                {
                    StructuralCloneDisposition.Unsupported =>
                        StructuralCloneRetrievalBlockerKind.SeedUnsupported,
                    StructuralCloneDisposition.LimitReached =>
                        StructuralCloneRetrievalBlockerKind
                            .SeedProductionLimit,
                    _ => StructuralCloneRetrievalBlockerKind
                        .SeedProductionFailure,
                };
            return new StructuralCloneRetrievalResult(
                disposition,
                seedOutcome,
                [],
                [],
                [
                    new StructuralCloneRetrievalBlocker(
                        kind,
                        $"Seed method 0x{MetadataTokens.GetToken(seed):X8} "
                            + $"produced as {seedProduction.Disposition}."),
                ],
                new StructuralCloneRetrievalReceipt(
                    methods.Length,
                    ProcessedMethods: 0,
                    SuppressedCandidates: potentialCandidates,
                    EligibleMethods: 0,
                    UnsupportedMethods: 0,
                    LimitReachedMethods: 0,
                    FailedMethods: 0,
                    RankedCandidates: 0,
                    ReturnedCandidates: 0,
                    BodyProductions: 1));
        }

        StructuralCloneRetrievalProfile seedProfile =
            StructuralCloneRetrievalProfile.Create(seedProduction.Facts!);
        ImmutableArray<StructuralCloneRetrievalMethodOutcome>.Builder outcomes =
            ImmutableArray.CreateBuilder<StructuralCloneRetrievalMethodOutcome>(
                orderedMethods.Length);
        List<StructuralCloneRetrievalCandidate> ranked = [];
        int eligible = 0;
        int unsupported = 0;
        int limited = 0;
        int failed = 0;
        int bodyProductions = 1;

        foreach (MethodDefinitionHandle handle in orderedMethods)
        {
            if (handle == seed)
                continue;
            MetadataMethodAddress address = new(moduleVersionId, handle);
            BodyProduction production = Produce(
                image,
                reader,
                address,
                StructuralCloneSide.Both,
                comparisonLimits);
            bodyProductions++;
            outcomes.Add(RetrievalMethodOutcome(address, production));
            switch (production.Disposition)
            {
                case StructuralCloneDisposition.Completed:
                    eligible++;
                    StructuralCloneRetrievalProfile profile =
                        StructuralCloneRetrievalProfile.Create(
                            production.Facts!);
                    if (seedProduction.Facts!.Signature
                        != production.Facts!.Signature)
                    {
                        continue;
                    }
                    StructuralCloneSimilarityEvidence similarity =
                        Similarity(seedProfile, profile);
                    ranked.Add(
                        new StructuralCloneRetrievalCandidate(
                            Rank: 0,
                            address,
                            similarity));
                    break;
                case StructuralCloneDisposition.Unsupported:
                    unsupported++;
                    break;
                case StructuralCloneDisposition.LimitReached:
                    limited++;
                    break;
                case StructuralCloneDisposition.Failed:
                    failed++;
                    break;
            }
        }

        StructuralCloneRetrievalCandidate[] orderedCandidates =
        [
            .. ranked
                .OrderByDescending(static candidate =>
                    candidate.Similarity.Score)
                .ThenByDescending(static candidate =>
                    candidate.Similarity.OperationScore)
                .ThenByDescending(static candidate =>
                    candidate.Similarity.PositionScore)
                .ThenByDescending(static candidate =>
                    candidate.Similarity.BlockScore)
                .ThenByDescending(static candidate =>
                    candidate.Similarity.EdgeScore)
                .ThenByDescending(static candidate =>
                    candidate.Similarity.LocalScore)
                .ThenBy(static candidate =>
                    MetadataTokens.GetToken(candidate.Method.Handle)),
        ];
        ImmutableArray<StructuralCloneRetrievalCandidate> candidates =
        [
            .. orderedCandidates
                .Take(limits.MaximumResults)
                .Select((candidate, index) =>
                    candidate with { Rank = index + 1 }),
        ];
        StructuralCloneRetrievalDisposition resultDisposition =
            failed > 0
                ? StructuralCloneRetrievalDisposition.Failed
                : limited > 0
                    ? StructuralCloneRetrievalDisposition.LimitReached
                    : StructuralCloneRetrievalDisposition.Completed;
        ImmutableArray<StructuralCloneRetrievalBlocker>.Builder blockers =
            ImmutableArray.CreateBuilder<StructuralCloneRetrievalBlocker>();
        if (limited > 0)
        {
            blockers.Add(
                new StructuralCloneRetrievalBlocker(
                    StructuralCloneRetrievalBlockerKind
                        .CandidateProductionLimit,
                    $"{limited} candidate methods exceeded body-production "
                        + "limits; returned ranks exclude those methods."));
        }
        if (failed > 0)
        {
            blockers.Add(
                new StructuralCloneRetrievalBlocker(
                    StructuralCloneRetrievalBlockerKind
                        .CandidateProductionFailure,
                    $"{failed} candidate methods failed body production; "
                        + "returned ranks exclude those methods."));
        }
        return new StructuralCloneRetrievalResult(
            resultDisposition,
            seedOutcome,
            candidates,
            outcomes.ToImmutable(),
            blockers.ToImmutable(),
            new StructuralCloneRetrievalReceipt(
                methods.Length,
                potentialCandidates,
                SuppressedCandidates:
                    orderedCandidates.Length
                    - candidates.Length
                    + limited
                    + failed,
                eligible,
                unsupported,
                limited,
                failed,
                RankedCandidates: orderedCandidates.Length,
                ReturnedCandidates: candidates.Length,
                bodyProductions));
    }

    static StructuralCloneSimilarityEvidence Similarity(
        StructuralCloneRetrievalProfile seed,
        StructuralCloneRetrievalProfile candidate)
    {
        int operations = Dice(
            seed.Operations,
            candidate.Operations);
        int positions = Dice(
            seed.Positions,
            candidate.Positions);
        int blocks = Dice(seed.Blocks, candidate.Blocks);
        int edges = Dice(seed.Edges, candidate.Edges);
        int locals = Dice(seed.Locals, candidate.Locals);
        int score = (
            35 * operations
            + 20 * positions
            + 20 * blocks
            + 20 * edges
            + 5 * locals) / 100;
        return new StructuralCloneSimilarityEvidence(
            score,
            operations,
            positions,
            blocks,
            edges,
            locals,
            seed.Instructions,
            candidate.Instructions,
            seed.BlockCount,
            candidate.BlockCount,
            seed.EdgeCount,
            candidate.EdgeCount,
            seed.LocalCount,
            candidate.LocalCount);
    }

    static int Dice<T>(
        ImmutableArray<T> left,
        ImmutableArray<T> right)
        where T : notnull
    {
        if (left.IsEmpty && right.IsEmpty)
            return 10_000;
        var counts = new Dictionary<T, int>();
        foreach (T item in left)
            counts[item] = counts.GetValueOrDefault(item) + 1;
        int shared = 0;
        foreach (T item in right)
        {
            int count = counts.GetValueOrDefault(item);
            if (count == 0)
                continue;
            shared++;
            counts[item] = count - 1;
        }
        return checked(
            (int)(20_000L * shared / (left.Length + right.Length)));
    }

    static void ValidateRetrievalLimits(
        StructuralCloneRetrievalLimits limits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            limits.MaximumMethods,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            limits.MaximumResults,
            1);
    }

    static StructuralCloneRetrievalResult EmptyRetrieval(
        MethodDefinitionHandle seed,
        int inputMethods,
        int potentialCandidates,
        StructuralCloneRetrievalDisposition disposition,
        StructuralCloneRetrievalBlocker blocker)
    {
        MetadataMethodAddress address = new(Guid.Empty, seed);
        return new StructuralCloneRetrievalResult(
            disposition,
            new StructuralCloneRetrievalMethodOutcome(
                address,
                StructuralCloneDisposition.LimitReached,
                [],
                new StructuralCloneMethodReceipt(0, 0, 0, 0, 0)),
            [],
            [],
            [blocker],
            new StructuralCloneRetrievalReceipt(
                inputMethods,
                ProcessedMethods: 0,
                SuppressedCandidates: potentialCandidates,
                EligibleMethods: 0,
                UnsupportedMethods: 0,
                LimitReachedMethods: 0,
                FailedMethods: 0,
                RankedCandidates: 0,
                ReturnedCandidates: 0,
                BodyProductions: 0));
    }

    static StructuralCloneRetrievalResult FailedRetrieval(
        MethodDefinitionHandle seed,
        int inputMethods,
        int potentialCandidates,
        StructuralCloneMetadataFailure failure)
    {
        MetadataMethodAddress address = new(Guid.Empty, seed);
        StructuralCloneRetrievalBlocker blocker = new(
            StructuralCloneRetrievalBlockerKind.MetadataReadFailure,
            $"The {failure.Subject} is invalid: "
                + $"{failure.Exception.GetType().Name}: "
                + failure.Exception.Message);
        return new StructuralCloneRetrievalResult(
            StructuralCloneRetrievalDisposition.Failed,
            new StructuralCloneRetrievalMethodOutcome(
                address,
                StructuralCloneDisposition.Failed,
                [],
                new StructuralCloneMethodReceipt(0, 0, 0, 0, 0)),
            [],
            [],
            [blocker],
            new StructuralCloneRetrievalReceipt(
                inputMethods,
                ProcessedMethods: 0,
                SuppressedCandidates: potentialCandidates,
                EligibleMethods: 0,
                UnsupportedMethods: 0,
                LimitReachedMethods: 0,
                FailedMethods: 0,
                RankedCandidates: 0,
                ReturnedCandidates: 0,
                BodyProductions: 0));
    }

    static StructuralCloneRetrievalMethodOutcome RetrievalMethodOutcome(
        MetadataMethodAddress address,
        BodyProduction production)
        => new(
            address,
            production.Disposition,
            production.Blockers,
            new StructuralCloneMethodReceipt(
                production.Measurements.BodyBytes,
                production.Measurements.InstructionCount,
                production.Measurements.BlockCount,
                production.Measurements.EdgeCount,
                production.Measurements.LocalCount));

    sealed record StructuralCloneRetrievalProfile(
        ImmutableArray<RetrievalOperationFeature> Operations,
        ImmutableArray<RetrievalPositionFeature> Positions,
        ImmutableArray<RetrievalBlockFeature> Blocks,
        ImmutableArray<RetrievalEdgeFeature> Edges,
        ImmutableArray<StructuralCloneTypeIdentity> Locals,
        int Instructions,
        int BlockCount,
        int EdgeCount,
        int LocalCount)
    {
        public static StructuralCloneRetrievalProfile Create(
            StructuralCloneBodyFacts facts)
        {
            ImmutableArray<RetrievalOperationFeature>.Builder operations =
                ImmutableArray.CreateBuilder<RetrievalOperationFeature>();
            ImmutableArray<RetrievalPositionFeature>.Builder positions =
                ImmutableArray.CreateBuilder<RetrievalPositionFeature>();
            ImmutableArray<RetrievalBlockFeature>.Builder blocks =
                ImmutableArray.CreateBuilder<RetrievalBlockFeature>();
            ImmutableArray<RetrievalEdgeFeature>.Builder edges =
                ImmutableArray.CreateBuilder<RetrievalEdgeFeature>();

            foreach (StructuralCloneBlock block in facts.Graph.Blocks)
            {
                blocks.Add(
                    new RetrievalBlockFeature(
                        block.ExitsMethod,
                        block.Operations.Length,
                        block.Incoming.Length,
                        block.Outgoing.Length));
                for (int ordinal = 0;
                    ordinal < block.Operations.Length;
                    ordinal++)
                {
                    StructuralCloneOperation operation =
                        block.Operations[ordinal];
                    StructuralCloneTypeIdentity? localType =
                        operation.OperandKind
                            == StructuralCloneOperandKind.Local
                            ? facts.Locals[checked((int)operation.Value)]
                            : null;
                    operations.Add(
                        new RetrievalOperationFeature(
                            operation.OpCode,
                            operation.OperandKind,
                            localType is null ? operation.Value : 0,
                            localType));
                    positions.Add(
                        new RetrievalPositionFeature(
                            ordinal,
                            operation.OpCode,
                            operation.OperandKind));
                }
                foreach (StructuralCloneEdge edge in block.Outgoing)
                {
                    StructuralCloneBlock target =
                        facts.Graph.Blocks[edge.Target];
                    edges.Add(
                        new RetrievalEdgeFeature(
                            edge.Role.Kind,
                            edge.Role.Ordinal,
                            block.ExitsMethod,
                            block.Operations.Length,
                            target.ExitsMethod,
                            target.Operations.Length));
                }
            }

            return new StructuralCloneRetrievalProfile(
                operations.ToImmutable(),
                positions.ToImmutable(),
                blocks.ToImmutable(),
                edges.ToImmutable(),
                facts.Locals,
                facts.InstructionCount,
                facts.Graph.Blocks.Length,
                facts.Graph.Blocks.Sum(
                    static block => block.Outgoing.Length),
                facts.Locals.Length);
        }
    }

    readonly record struct RetrievalOperationFeature(
        ILOpCode OpCode,
        StructuralCloneOperandKind OperandKind,
        long Value,
        StructuralCloneTypeIdentity? LocalType);

    readonly record struct RetrievalPositionFeature(
        int Ordinal,
        ILOpCode OpCode,
        StructuralCloneOperandKind OperandKind);

    readonly record struct RetrievalBlockFeature(
        bool ExitsMethod,
        int Operations,
        int Incoming,
        int Outgoing);

    readonly record struct RetrievalEdgeFeature(
        StructuralCloneEdgeKind Kind,
        int Ordinal,
        bool SourceExits,
        int SourceOperations,
        bool TargetExits,
        int TargetOperations);
}
