using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis;

/// <summary>The execution disposition of one structural-clone discovery run.</summary>
public enum StructuralCloneDiscoveryDisposition
{
    Completed,
    LimitReached,
    Failed,
}

/// <summary>Typed reasons why structural-clone discovery did not complete.</summary>
public enum StructuralCloneDiscoveryBlockerKind
{
    MetadataReadFailure,
    MethodLimit,
    MethodUnsupported,
    MethodProductionLimit,
    MethodProductionFailure,
    CandidateComparisonLimit,
    CandidateVerificationLimit,
    CandidateVerificationFailure,
    CandidateReproductionFailure,
}

/// <summary>One visible discovery failure or limit.</summary>
public sealed record StructuralCloneDiscoveryBlocker(
    StructuralCloneDiscoveryBlockerKind Kind,
    string Detail)
{
    public MetadataRootMalformedReason? MetadataRootReason { get; init; }
}

/// <summary>Bounded production receipt for one method.</summary>
public sealed record StructuralCloneMethodReceipt(
    int BodyBytes,
    int Instructions,
    int Blocks,
    int Edges,
    int Locals);

/// <summary>Side-free production outcome for one admitted method.</summary>
public sealed record StructuralCloneMethodOutcome(
    MetadataMethodAddress Method,
    StructuralCloneDisposition Disposition,
    ImmutableArray<StructuralCloneDiscoveryBlocker> Blockers,
    StructuralCloneMethodReceipt Receipt);

/// <summary>
/// Deterministic exact-cluster identity within one admitted PE and population.
/// </summary>
public sealed class StructuralCloneClusterIdentity
    : IEquatable<StructuralCloneClusterIdentity>
{
    internal StructuralCloneClusterIdentity(
        Guid moduleVersionId,
        ImmutableArray<int> methodTokens)
    {
        ModuleVersionId = moduleVersionId;
        MethodTokens = methodTokens;
    }

    public Guid ModuleVersionId { get; }
    public ImmutableArray<int> MethodTokens { get; }

    public bool Equals(StructuralCloneClusterIdentity? other)
        => other is not null
            && ModuleVersionId == other.ModuleVersionId
            && MethodTokens.AsSpan().SequenceEqual(
                other.MethodTokens.AsSpan());

    public override bool Equals(object? obj)
        => obj is StructuralCloneClusterIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ModuleVersionId);
        foreach (int token in MethodTokens)
            hash.Add(token);
        return hash.ToHashCode();
    }

    public override string ToString()
        => $"{ModuleVersionId:N}/"
            + string.Join(
                ",",
                MethodTokens.Select(static token => $"{token:X8}"));
}

/// <summary>
/// One completely verified exact cluster. Evidence compares every non-anchor
/// member with the first member.
/// </summary>
public sealed record StructuralCloneCluster(
    StructuralCloneClusterIdentity Identity,
    ImmutableArray<MetadataMethodAddress> Members,
    ImmutableArray<StructuralCloneComparison> Evidence);

/// <summary>
/// One candidate bucket that discovery could not completely partition.
/// Absence from a cluster is not negative evidence for these methods.
/// </summary>
public sealed record StructuralCloneSuppressedBucket(
    ImmutableArray<MetadataMethodAddress> Methods,
    StructuralCloneDiscoveryBlocker Reason);

/// <summary>Bounded-work receipt for one discovery run.</summary>
public sealed record StructuralCloneDiscoveryReceipt(
    int InputMethods,
    int ProcessedMethods,
    int SuppressedMethods,
    int EligibleMethods,
    int UnsupportedMethods,
    int LimitReachedMethods,
    int FailedMethods,
    int CandidateBuckets,
    int CompletedCandidateBuckets,
    int SuppressedCandidateBuckets,
    int CandidateComparisons,
    int ExactComparisons,
    int DifferentComparisons,
    int UnresolvedComparisons,
    int BodyProductions);

/// <summary>Resource limits for exact structural-clone discovery.</summary>
public sealed record StructuralCloneDiscoveryLimits(
    int MaximumMethods = 50_000,
    int MaximumCandidateComparisons = 100_000,
    StructuralCloneComparisonLimits? ComparisonLimits = null);

/// <summary>Product-owned result for one bounded A-vs-A discovery run.</summary>
public sealed record StructuralCloneDiscoveryResult
{
    internal StructuralCloneDiscoveryResult(
        StructuralCloneDiscoveryDisposition disposition,
        ImmutableArray<StructuralCloneCluster> clusters,
        ImmutableArray<StructuralCloneMethodOutcome> methods,
        ImmutableArray<StructuralCloneSuppressedBucket> suppressedBuckets,
        ImmutableArray<StructuralCloneComparison> unresolvedComparisons,
        ImmutableArray<StructuralCloneDiscoveryBlocker> blockers,
        StructuralCloneDiscoveryReceipt receipt)
    {
        if (disposition == StructuralCloneDiscoveryDisposition.Completed
            && (!blockers.IsEmpty
                || !suppressedBuckets.IsEmpty
                || !unresolvedComparisons.IsEmpty))
        {
            throw new ArgumentException(
                "Completed discovery cannot carry blockers or suppressed work.");
        }
        if (disposition != StructuralCloneDiscoveryDisposition.Completed
            && blockers.IsEmpty)
        {
            throw new ArgumentException(
                "Non-completed discovery requires a blocker.",
                nameof(blockers));
        }

        Disposition = disposition;
        Clusters = clusters;
        Methods = methods;
        SuppressedBuckets = suppressedBuckets;
        UnresolvedComparisons = unresolvedComparisons;
        Blockers = blockers;
        Receipt = receipt;
    }

    public StructuralCloneDiscoveryDisposition Disposition { get; }
    public ImmutableArray<StructuralCloneCluster> Clusters { get; }
    public ImmutableArray<StructuralCloneMethodOutcome> Methods { get; }
    public ImmutableArray<StructuralCloneSuppressedBucket> SuppressedBuckets
    {
        get;
    }
    public ImmutableArray<StructuralCloneComparison> UnresolvedComparisons
    {
        get;
    }
    public ImmutableArray<StructuralCloneDiscoveryBlocker> Blockers { get; }
    public StructuralCloneDiscoveryReceipt Receipt { get; }
}

public static partial class StructuralCloneAnalysis
{
    /// <summary>
    /// Discovers completely verified exact clusters in one retained PE image.
    /// Retrieval fingerprints only select candidates; they never establish
    /// cluster identity or relation.
    /// </summary>
    public static StructuralCloneDiscoveryResult Discover(
        PEReader image,
        ImmutableArray<MethodDefinitionHandle> methods,
        StructuralCloneDiscoveryLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (methods.IsDefault)
        {
            throw new ArgumentException(
                "The discovery population must be initialized.",
                nameof(methods));
        }

        limits ??= new StructuralCloneDiscoveryLimits();
        ValidateDiscoveryLimits(limits);
        StructuralCloneComparisonLimits comparisonLimits =
            limits.ComparisonLimits
            ?? new StructuralCloneComparisonLimits();
        ValidateLimits(comparisonLimits);

        if (methods.Length > limits.MaximumMethods)
        {
            StructuralCloneDiscoveryBlocker blocker = new(
                StructuralCloneDiscoveryBlockerKind.MethodLimit,
                $"Method population {methods.Length} exceeds "
                    + $"{limits.MaximumMethods}.");
            return new StructuralCloneDiscoveryResult(
                StructuralCloneDiscoveryDisposition.LimitReached,
                [],
                [],
                [],
                [],
                [blocker],
                EmptyDiscoveryReceipt(
                    methods.Length,
                    suppressedMethods: methods.Length));
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
                    "The discovery population cannot contain duplicate handles.",
                    nameof(methods));
            }
        }

        if (!TryGetMetadataReader(
                image,
                nameof(image),
                out MetadataReader reader,
                out StructuralCloneMetadataFailure metadataFailure))
        {
            return FailedDiscovery(
                methods.Length,
                metadataFailure);
        }
        foreach (MethodDefinitionHandle method in orderedMethods)
            ValidateHandle(reader, method, nameof(methods));
        if (!TryGetModuleVersionId(
                reader,
                out Guid moduleVersionId,
                out metadataFailure))
        {
            return FailedDiscovery(
                methods.Length,
                metadataFailure);
        }

        var buckets =
            new Dictionary<StructuralCloneCandidateKey,
                List<MethodDefinitionHandle>>();
        ImmutableArray<StructuralCloneMethodOutcome>.Builder methodOutcomes =
            ImmutableArray.CreateBuilder<StructuralCloneMethodOutcome>(
                orderedMethods.Length);
        ImmutableArray<StructuralCloneDiscoveryBlocker>.Builder blockers =
            ImmutableArray.CreateBuilder<StructuralCloneDiscoveryBlocker>();
        int eligibleMethods = 0;
        int unsupportedMethods = 0;
        int limitReachedMethods = 0;
        int failedMethods = 0;
        int bodyProductions = 0;

        foreach (MethodDefinitionHandle handle in orderedMethods)
        {
            MetadataMethodAddress address = new(moduleVersionId, handle);
            BodyProduction production = Produce(
                image,
                reader,
                address,
                StructuralCloneSide.Both,
                comparisonLimits);
            bodyProductions++;
            methodOutcomes.Add(MethodOutcome(address, production));
            switch (production.Disposition)
            {
                case StructuralCloneDisposition.Completed:
                    eligibleMethods++;
                    StructuralCloneCandidateKey key =
                        StructuralCloneCandidateKey.Create(
                            production.Facts!);
                    if (!buckets.TryGetValue(key, out List<MethodDefinitionHandle>? bucket))
                    {
                        bucket = [];
                        buckets.Add(key, bucket);
                    }
                    bucket.Add(handle);
                    break;
                case StructuralCloneDisposition.Unsupported:
                    unsupportedMethods++;
                    break;
                case StructuralCloneDisposition.LimitReached:
                    limitReachedMethods++;
                    break;
                case StructuralCloneDisposition.Failed:
                    failedMethods++;
                    break;
            }
        }

        if (limitReachedMethods > 0)
        {
            blockers.Add(
                new StructuralCloneDiscoveryBlocker(
                    StructuralCloneDiscoveryBlockerKind.MethodProductionLimit,
                    $"{limitReachedMethods} methods exceeded comparison "
                        + "production limits."));
        }
        if (failedMethods > 0)
        {
            blockers.Add(
                new StructuralCloneDiscoveryBlocker(
                    StructuralCloneDiscoveryBlockerKind.MethodProductionFailure,
                    $"{failedMethods} methods failed body production."));
        }

        List<List<MethodDefinitionHandle>> candidateBuckets =
        [
            .. buckets.Values
                .Where(static bucket => bucket.Count > 1)
                .OrderBy(static bucket =>
                    MetadataTokens.GetRowNumber(bucket[0])),
        ];
        ImmutableArray<StructuralCloneCluster>.Builder clusters =
            ImmutableArray.CreateBuilder<StructuralCloneCluster>();
        ImmutableArray<StructuralCloneSuppressedBucket>.Builder suppressed =
            ImmutableArray.CreateBuilder<StructuralCloneSuppressedBucket>();
        ImmutableArray<StructuralCloneComparison>.Builder unresolved =
            ImmutableArray.CreateBuilder<StructuralCloneComparison>();
        int completedBuckets = 0;
        int comparisons = 0;
        int exactComparisons = 0;
        int differentComparisons = 0;
        bool comparisonBudgetExhausted = false;
        bool verificationFailed = false;

        for (int bucketIndex = 0;
            bucketIndex < candidateBuckets.Count;
            bucketIndex++)
        {
            List<MethodDefinitionHandle> bucket = candidateBuckets[bucketIndex];
            if (comparisonBudgetExhausted
                || comparisons >= limits.MaximumCandidateComparisons)
            {
                StructuralCloneDiscoveryBlocker reason = new(
                    StructuralCloneDiscoveryBlockerKind
                        .CandidateComparisonLimit,
                    "The global candidate-comparison budget was exhausted.");
                SuppressRemainingBuckets(
                    candidateBuckets,
                    bucketIndex,
                    moduleVersionId,
                    reason,
                    suppressed);
                blockers.Add(reason);
                break;
            }

            List<StructuralCloneBodyFacts> facts = [];
            StructuralCloneDiscoveryBlocker? bucketBlocker = null;
            foreach (MethodDefinitionHandle handle in bucket)
            {
                MetadataMethodAddress address = new(moduleVersionId, handle);
                BodyProduction production = Produce(
                    image,
                    reader,
                    address,
                    StructuralCloneSide.Both,
                    comparisonLimits);
                bodyProductions++;
                if (production.Disposition
                    != StructuralCloneDisposition.Completed)
                {
                    bucketBlocker = new(
                        StructuralCloneDiscoveryBlockerKind
                            .CandidateReproductionFailure,
                        $"Candidate method 0x{MetadataTokens.GetToken(handle):X8} "
                            + $"re-produced as {production.Disposition}.");
                    if (production.Disposition
                        == StructuralCloneDisposition.LimitReached)
                    {
                        bucketBlocker = new(
                            StructuralCloneDiscoveryBlockerKind
                                .CandidateVerificationLimit,
                            $"Candidate method 0x{MetadataTokens.GetToken(handle):X8} "
                                + "exceeded a limit when re-produced.");
                    }
                    else
                    {
                        verificationFailed = true;
                    }
                    break;
                }
                facts.Add(production.Facts!);
            }
            if (bucketBlocker is not null)
            {
                suppressed.Add(
                    SuppressedBucket(
                        bucket,
                        moduleVersionId,
                        bucketBlocker));
                blockers.Add(bucketBlocker);
                continue;
            }

            List<StructuralCloneDiscoveryGroup> groups = [];
            bool bucketComplete = true;
            foreach (StructuralCloneBodyFacts candidate in facts)
            {
                bool matched = false;
                foreach (StructuralCloneDiscoveryGroup group in groups)
                {
                    if (comparisons
                        >= limits.MaximumCandidateComparisons)
                    {
                        comparisonBudgetExhausted = true;
                        bucketComplete = false;
                        bucketBlocker = new(
                            StructuralCloneDiscoveryBlockerKind
                                .CandidateComparisonLimit,
                            $"Candidate comparison count {comparisons} reached "
                                + $"{limits.MaximumCandidateComparisons}.");
                        break;
                    }

                    StructuralCloneComparison comparison = CompareExact(
                        group.Representative,
                        candidate,
                        comparisonLimits);
                    comparisons++;
                    if (comparison.Disposition
                        != StructuralCloneDisposition.Completed)
                    {
                        unresolved.Add(comparison);
                        bucketComplete = false;
                        if (comparison.Disposition
                            == StructuralCloneDisposition.Failed)
                        {
                            verificationFailed = true;
                            bucketBlocker = new(
                                StructuralCloneDiscoveryBlockerKind
                                    .CandidateVerificationFailure,
                                "An exact candidate comparison failed.");
                        }
                        else
                        {
                            bucketBlocker = new(
                                StructuralCloneDiscoveryBlockerKind
                                    .CandidateVerificationLimit,
                                "An exact candidate comparison did not "
                                    + $"complete: {comparison.Disposition}.");
                        }
                        break;
                    }
                    if (comparison.Relation
                        == StructuralCloneRelation.Exact)
                    {
                        group.Members.Add(candidate.Method);
                        group.Evidence.Add(comparison);
                        exactComparisons++;
                        matched = true;
                        break;
                    }
                    differentComparisons++;
                }
                if (!bucketComplete)
                    break;
                if (!matched)
                    groups.Add(new StructuralCloneDiscoveryGroup(candidate));
            }

            if (!bucketComplete)
            {
                StructuralCloneDiscoveryBlocker reason =
                    bucketBlocker
                    ?? throw new InvalidOperationException(
                        "Incomplete discovery bucket has no blocker.");
                suppressed.Add(
                    SuppressedBucket(
                        bucket,
                        moduleVersionId,
                        reason));
                blockers.Add(reason);
                continue;
            }

            completedBuckets++;
            foreach (StructuralCloneDiscoveryGroup group in groups)
            {
                if (group.Members.Count < 2)
                    continue;
                ImmutableArray<MetadataMethodAddress> members =
                [
                    .. group.Members.OrderBy(static member =>
                        MetadataTokens.GetRowNumber(member.Handle)),
                ];
                clusters.Add(
                    new StructuralCloneCluster(
                        new StructuralCloneClusterIdentity(
                            moduleVersionId,
                            [
                                .. members.Select(static member =>
                                    MetadataTokens.GetToken(member.Handle)),
                            ]),
                        members,
                        [.. group.Evidence]));
            }
        }

        StructuralCloneDiscoveryDisposition disposition =
            failedMethods > 0 || verificationFailed
                ? StructuralCloneDiscoveryDisposition.Failed
                : limitReachedMethods > 0
                    || suppressed.Count > 0
                    || unresolved.Count > 0
                    ? StructuralCloneDiscoveryDisposition.LimitReached
                    : StructuralCloneDiscoveryDisposition.Completed;
        ImmutableArray<StructuralCloneDiscoveryBlocker> resultBlockers =
            disposition == StructuralCloneDiscoveryDisposition.Completed
                ? []
                : blockers
                    .Distinct()
                    .ToImmutableArray();

        return new StructuralCloneDiscoveryResult(
            disposition,
            [
                .. clusters.OrderBy(static cluster =>
                    cluster.Identity.MethodTokens[0]),
            ],
            methodOutcomes.ToImmutable(),
            suppressed.ToImmutable(),
            unresolved.ToImmutable(),
            resultBlockers,
            new StructuralCloneDiscoveryReceipt(
                methods.Length,
                methods.Length,
                SuppressedMethods(suppressed),
                eligibleMethods,
                unsupportedMethods,
                limitReachedMethods,
                failedMethods,
                candidateBuckets.Count,
                completedBuckets,
                suppressed.Count,
                comparisons,
                exactComparisons,
                differentComparisons,
                unresolved.Count,
                bodyProductions));
    }

    static void ValidateDiscoveryLimits(
        StructuralCloneDiscoveryLimits limits)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            limits.MaximumMethods,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            limits.MaximumCandidateComparisons,
            1);
    }

    static StructuralCloneDiscoveryResult FailedDiscovery(
        int inputMethods,
        StructuralCloneMetadataFailure failure)
    {
        StructuralCloneDiscoveryBlocker blocker = new(
            StructuralCloneDiscoveryBlockerKind.MetadataReadFailure,
            $"The {failure.Subject} is invalid: "
                + $"{failure.Exception.GetType().Name}: "
                + failure.Exception.Message)
        {
            MetadataRootReason = MalformedRootReason(failure.Exception),
        };
        return new StructuralCloneDiscoveryResult(
            StructuralCloneDiscoveryDisposition.Failed,
            [],
            [],
            [],
            [],
            [blocker],
            EmptyDiscoveryReceipt(inputMethods));
    }

    static StructuralCloneDiscoveryReceipt EmptyDiscoveryReceipt(
        int inputMethods,
        int suppressedMethods = 0)
        => new(
            inputMethods,
            ProcessedMethods: 0,
            suppressedMethods,
            EligibleMethods: 0,
            UnsupportedMethods: 0,
            LimitReachedMethods: 0,
            FailedMethods: 0,
            CandidateBuckets: 0,
            CompletedCandidateBuckets: 0,
            SuppressedCandidateBuckets: 0,
            CandidateComparisons: 0,
            ExactComparisons: 0,
            DifferentComparisons: 0,
            UnresolvedComparisons: 0,
            BodyProductions: 0);

    static StructuralCloneMethodOutcome MethodOutcome(
        MetadataMethodAddress address,
        BodyProduction production)
        => new(
            address,
            production.Disposition,
            [
                .. production.Blockers.Select(blocker =>
                    new StructuralCloneDiscoveryBlocker(
                        production.Disposition switch
                        {
                            StructuralCloneDisposition.Unsupported =>
                                StructuralCloneDiscoveryBlockerKind
                                    .MethodUnsupported,
                            StructuralCloneDisposition.LimitReached =>
                                StructuralCloneDiscoveryBlockerKind
                                    .MethodProductionLimit,
                            StructuralCloneDisposition.Failed =>
                                StructuralCloneDiscoveryBlockerKind
                                    .MethodProductionFailure,
                            _ => throw new InvalidOperationException(
                                "Completed method production cannot have "
                                    + "blockers."),
                        },
                        $"{blocker.Kind}: {blocker.Detail}")),
            ],
            new StructuralCloneMethodReceipt(
                production.Measurements.BodyBytes,
                production.Measurements.InstructionCount,
                production.Measurements.BlockCount,
                production.Measurements.EdgeCount,
                production.Measurements.LocalCount));

    static StructuralCloneSuppressedBucket SuppressedBucket(
        IEnumerable<MethodDefinitionHandle> methods,
        Guid moduleVersionId,
        StructuralCloneDiscoveryBlocker reason)
        => new(
            [
                .. methods
                    .OrderBy(static method =>
                        MetadataTokens.GetRowNumber(method))
                    .Select(method =>
                        new MetadataMethodAddress(moduleVersionId, method)),
            ],
            reason);

    static void SuppressRemainingBuckets(
        IReadOnlyList<List<MethodDefinitionHandle>> buckets,
        int start,
        Guid moduleVersionId,
        StructuralCloneDiscoveryBlocker reason,
        ImmutableArray<StructuralCloneSuppressedBucket>.Builder suppressed)
    {
        for (int index = start; index < buckets.Count; index++)
        {
            suppressed.Add(
                SuppressedBucket(
                    buckets[index],
                    moduleVersionId,
                    reason));
        }
    }

    static int SuppressedMethods(
        ImmutableArray<StructuralCloneSuppressedBucket>.Builder suppressed)
    {
        HashSet<MetadataMethodAddress> methods = [];
        foreach (StructuralCloneSuppressedBucket bucket in suppressed)
            methods.UnionWith(bucket.Methods);
        return methods.Count;
    }

    sealed class StructuralCloneDiscoveryGroup(
        StructuralCloneBodyFacts representative)
    {
        public StructuralCloneBodyFacts Representative { get; } =
            representative;
        public List<MetadataMethodAddress> Members { get; } =
            [representative.Method];
        public List<StructuralCloneComparison> Evidence { get; } = [];
    }

    sealed class StructuralCloneCandidateKey
        : IEquatable<StructuralCloneCandidateKey>
    {
        StructuralCloneCandidateKey(
            StructuralCloneMethodSignature signature,
            bool initLocals,
            int localCount,
            int blockCount,
            int instructionCount,
            int edgeCount,
            ImmutableArray<byte> fingerprint)
        {
            Signature = signature;
            InitLocals = initLocals;
            LocalCount = localCount;
            BlockCount = blockCount;
            InstructionCount = instructionCount;
            EdgeCount = edgeCount;
            Fingerprint = fingerprint;
        }

        StructuralCloneMethodSignature Signature { get; }
        bool InitLocals { get; }
        int LocalCount { get; }
        int BlockCount { get; }
        int InstructionCount { get; }
        int EdgeCount { get; }
        ImmutableArray<byte> Fingerprint { get; }

        public static StructuralCloneCandidateKey Create(
            StructuralCloneBodyFacts facts)
        {
            List<byte[]> blockHashes =
            [
                .. facts.Graph.Blocks.Select(BlockHash),
            ];
            blockHashes.Sort(ByteArrayComparer.Instance);
            int[] localHashes =
            [
                .. facts.Locals
                    .Select(static local => local.GetHashCode())
                    .Order(),
            ];

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(localHashes.Length);
            foreach (int hash in localHashes)
                writer.Write(hash);
            writer.Write(blockHashes.Count);
            foreach (byte[] hash in blockHashes)
                writer.Write(hash);
            WriteRoles(
                writer,
                facts.Graph.Blocks.SelectMany(static block =>
                    block.Outgoing.Select(static edge => edge.Role)));
            WriteRoles(
                writer,
                facts.Graph.Blocks.SelectMany(static block =>
                    block.Incoming.Select(static edge => edge.Role)));
            writer.Flush();

            return new StructuralCloneCandidateKey(
                facts.Signature,
                facts.InitLocals,
                facts.Locals.Length,
                facts.Graph.Blocks.Length,
                facts.InstructionCount,
                facts.Graph.Blocks.Sum(
                    static block => block.Outgoing.Length),
                SHA256.HashData(stream.GetBuffer().AsSpan(
                    0,
                    checked((int)stream.Length))).ToImmutableArray());
        }

        public bool Equals(StructuralCloneCandidateKey? other)
            => other is not null
                && Signature == other.Signature
                && InitLocals == other.InitLocals
                && LocalCount == other.LocalCount
                && BlockCount == other.BlockCount
                && InstructionCount == other.InstructionCount
                && EdgeCount == other.EdgeCount
                && Fingerprint.AsSpan().SequenceEqual(
                    other.Fingerprint.AsSpan());

        public override bool Equals(object? obj)
            => obj is StructuralCloneCandidateKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(
                Signature,
                InitLocals,
                LocalCount,
                BlockCount,
                InstructionCount,
                EdgeCount,
                BinaryPrimitives.ReadInt32LittleEndian(
                    Fingerprint.AsSpan()));

        static byte[] BlockHash(StructuralCloneBlock block)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(block.ExitsMethod);
            writer.Write(block.Operations.Length);
            foreach (StructuralCloneOperation operation in block.Operations)
            {
                writer.Write((int)operation.OpCode);
                writer.Write((int)operation.OperandKind);
                writer.Write(
                    operation.OperandKind == StructuralCloneOperandKind.Local
                        ? 0
                        : operation.Value);
            }
            writer.Flush();
            return SHA256.HashData(stream.GetBuffer().AsSpan(
                0,
                checked((int)stream.Length)));
        }

        static void WriteRoles(
            BinaryWriter writer,
            IEnumerable<StructuralCloneEdgeRole> edgeRoles)
        {
            StructuralCloneEdgeRole[] roles =
            [
                .. edgeRoles
                    .OrderBy(static role => role.Kind)
                    .ThenBy(static role => role.Ordinal),
            ];
            writer.Write(roles.Length);
            foreach (StructuralCloneEdgeRole role in roles)
            {
                writer.Write((int)role.Kind);
                writer.Write(role.Ordinal);
            }
        }
    }

    sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();

        public int Compare(byte[]? left, byte[]? right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left is null)
                return -1;
            if (right is null)
                return 1;
            return left.AsSpan().SequenceCompareTo(right);
        }
    }
}

readonly record struct StructuralCloneMetadataFailure(
    string Subject,
    Exception Exception);
