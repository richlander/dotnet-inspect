using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

public sealed record LeakTriageFinding(
    MethodIdentity Method,
    string Shape,
    string Evidence,
    string Severity,
    int RentOffset,
    int? ILOffset);

public sealed record LeakTriageCandidate(
    MethodIdentity Method,
    string Shape,
    string Evidence,
    int? RentOffset,
    int? ILOffset);

public enum LeakTriageFailureKind
{
    InstructionDecoding,
    MethodResolution,
    MethodMetadata,
    BodyAcquisition,
    ControlFlowAnalysis,
}

public readonly record struct LeakTriageFailure(
    int MethodToken,
    LeakTriageFailureKind Kind,
    string Reason);

public readonly record struct ArrayPoolExceptionBoundary(
    int ILOffset,
    MemberRef Operation);

public sealed record ArrayPoolExceptionPathCandidate
{
    public ArrayPoolExceptionPathCandidate(
        MethodIdentity Method,
        int RentOffset,
        ImmutableArray<ArrayPoolExceptionBoundary> Boundaries)
    {
        this.Method = Method ?? throw new ArgumentNullException(nameof(Method));
        if (RentOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(RentOffset));
        this.RentOffset = RentOffset;
        this.Boundaries = Boundaries.IsDefault ? [] : Boundaries;
    }

    public MethodIdentity Method { get; }
    public int RentOffset { get; }
    public ImmutableArray<ArrayPoolExceptionBoundary> Boundaries { get; }

    public bool Equals(ArrayPoolExceptionPathCandidate? other)
        => other is not null
            && Method == other.Method
            && RentOffset == other.RentOffset
            && ImmutableArrayValueEquality.SequenceEqual(Boundaries, other.Boundaries);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Method);
        hash.Add(RentOffset);
        ImmutableArrayValueEquality.AddToHash(ref hash, Boundaries);
        return hash.ToHashCode();
    }
}

public sealed record LeakTriageResult(
    ImmutableArray<LeakTriageFinding> Findings,
    ImmutableArray<LeakTriageCandidate> Candidates)
{
    public ImmutableArray<ArrayPoolExceptionPathCandidate> ExceptionPathCandidates { get; init; } = [];
    /// <summary>
    /// Method-local failures that make the assembly result incomplete.
    /// <c>DetailedAnalysis_MethodResolutionBudgetFailureIsTyped</c> and
    /// <c>ResourceLifecycleAnalysis_MalformedMethodTokenIsVisible</c> gate
    /// production and presentation propagation.
    /// </summary>
    public ImmutableArray<LeakTriageFailure> Failures { get; init; } = [];
}

public static class LeakTriageAnalyzer
{
    public static ImmutableArray<LeakTriageFinding> AnalyzeAssembly(string path)
    {
        LeakTriageResult result = AnalyzeAssemblyDetailed(path);
        ThrowIfFailed(result);
        return result.Findings;
    }

    public static LeakTriageResult AnalyzeAssemblyDetailed(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return LibraryBodyIndex.Open(
            path,
            LibraryBodyAnalysisFeatures.LeakTriage).LeakTriage;
    }

    internal static MethodIdentity CreateAssemblyScanMethodIdentity(
        MethodIdentity method)
    {
        const byte GenericSignature = 0x10;

        ArgumentNullException.ThrowIfNull(method);
        return method with
        {
            IsExtension = false,
            CallerUnsafeMode = CallerUnsafeMode.None,
            GenericArity = 0,
            GenericParameterNames = [],
            SignatureHeader =
                (byte)(method.SignatureHeader & ~GenericSignature),
        };
    }

    public static ImmutableArray<LeakTriageFinding> AnalyzeMethod(
        MethodIdentity method,
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        Func<int, MemberRef> resolveMethod)
    {
        LeakTriageResult result = AnalyzeMethodDetailed(
            method,
            il,
            exceptionRegions,
            resolveMethod,
            null);
        ThrowIfFailed(result);
        return result.Findings;
    }

    public static ImmutableArray<LeakTriageFinding> AnalyzeMethod(
        MethodIdentity method,
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        Func<int, MemberRef> resolveMethod,
        Func<int, TypeRef?>? resolveCatchType)
    {
        LeakTriageResult result = AnalyzeMethodDetailed(
            method,
            il,
            exceptionRegions,
            resolveMethod,
            resolveCatchType);
        ThrowIfFailed(result);
        return result.Findings;
    }

    public static LeakTriageResult AnalyzeMethodDetailed(
        MethodIdentity method,
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        Func<int, MemberRef> resolveMethod)
        => AnalyzeMethodDetailed(method, il, exceptionRegions, resolveMethod, null);

    public static LeakTriageResult AnalyzeMethodDetailed(
        MethodIdentity method,
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        Func<int, MemberRef> resolveMethod,
        Func<int, TypeRef?>? resolveCatchType)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(il);
        ArgumentNullException.ThrowIfNull(exceptionRegions);
        ArgumentNullException.ThrowIfNull(resolveMethod);

        if (il.Length == 0)
            return Empty;

        ImmutableArray<DecodedInstruction> instructions;
        try
        {
            instructions = InstructionDecoder.Decode(il);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return Failed(
                method,
                LeakTriageFailureKind.InstructionDecoding,
                ex);
        }

        IReadOnlyDictionary<int, MemberRef> calls;
        try
        {
            calls = ArrayPoolUseClassifier.BuildCallMap(
                instructions,
                resolveMethod);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return Failed(
                method,
                LeakTriageFailureKind.MethodResolution,
                ex);
        }

        if (!calls.Values.Any(
                ArrayPoolUseClassifier.IsArrayPoolRent))
            return Empty;

        IReadOnlySet<(
            int TryOffset,
            int TryLength,
            int HandlerOffset)> catchAllCleanup;
        try
        {
            catchAllCleanup =
                ResourceExceptionPathAnalyzer
                    .ComputeCreditableCatchCleanup(
                        exceptionRegions,
                        resolveCatchType);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return Failed(
                method,
                LeakTriageFailureKind.MethodResolution,
                ex);
        }

        try
        {
            var graph = BlockGraph.Build(il.Length, instructions, exceptionRegions);
            if (!graph.IsComplete)
                return Failed(
                    method.MetadataToken,
                    LeakTriageFailureKind.ControlFlowAnalysis,
                    "IncompleteControlFlow");

            var reaching = ReachingDefinitions.Analyze(il, ArgumentSlotCount(method), exceptionRegions);
            if (!reaching.IsComplete)
                return Failed(
                    method.MetadataToken,
                    LeakTriageFailureKind.ControlFlowAnalysis,
                    "IncompleteReachingDefinitions");
            return AnalyzeDecoded(
                method,
                instructions,
                graph,
                reaching,
                calls,
                (releases, boundaries) =>
                    ResourceExceptionPathAnalyzer
                        .UnprotectedThrowingBoundaries(
                            graph,
                            exceptionRegions,
                            catchAllCleanup,
                            releases,
                            boundaries));
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return Failed(
                method,
                LeakTriageFailureKind.ControlFlowAnalysis,
                ex);
        }
    }

    internal static LeakTriageResult AnalyzeMethodDetailed(
        MethodIdentity method,
        MethodBodyData body,
        Func<int, MemberRef> resolveMethod,
        Func<int, TypeRef?>? resolveCatchType)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(resolveMethod);

        if (body.EvidenceId.Method.ModuleVersionId
                != method.ModuleVersionId
            || body.EvidenceId.Method.Token
                != method.MetadataToken)
        {
            return Failed(
                method.MetadataToken,
                LeakTriageFailureKind.BodyAcquisition,
                "BodyIdentityMismatch");
        }

        if (body.IL.IsEmpty)
            return Empty;

        MethodInstructions decoded = MethodInstructions.Decode(body);
        if (!decoded.IsComplete)
        {
            return Failed(
                method.MetadataToken,
                decoded.Instructions.IsEmpty
                    ? LeakTriageFailureKind.InstructionDecoding
                    : LeakTriageFailureKind.ControlFlowAnalysis,
                decoded.Blocks.IncompleteReason
                    ?? "IncompleteControlFlow");
        }

        IReadOnlyDictionary<int, MemberRef> calls;
        try
        {
            calls = ArrayPoolUseClassifier.BuildCallMap(
                decoded.Instructions,
                resolveMethod);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return Failed(
                method,
                LeakTriageFailureKind.MethodResolution,
                ex);
        }

        if (!calls.Values.Any(
                ArrayPoolUseClassifier.IsArrayPoolRent))
        {
            return Empty;
        }

        if (decoded.ExceptionFlow
            is not InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Available availableFlow)
        {
            string reason = decoded.ExceptionFlow switch
            {
                InstructionExceptionFlowResult<
                    InstructionExceptionFlowFacts>.Unavailable unavailable =>
                    $"ExceptionFlowUnavailable:{unavailable.Reason}",
                InstructionExceptionFlowResult<
                    InstructionExceptionFlowFacts>.Ambiguous =>
                    "ExceptionFlowAmbiguous",
                _ => "UnknownExceptionFlowResult",
            };
            return Failed(
                method.MetadataToken,
                LeakTriageFailureKind.ControlFlowAnalysis,
                reason);
        }

        IReadOnlySet<MethodExceptionClauseId> catchAllCleanup;
        try
        {
            catchAllCleanup =
                ResourceExceptionPathAnalyzer
                    .ComputeCreditableCatchCleanup(
                        body.ExceptionRegionCatalog,
                        resolveCatchType);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return Failed(
                method,
                LeakTriageFailureKind.MethodResolution,
                ex);
        }

        try
        {
            ReachingDefinitionsResult reaching =
                ReachingDefinitions.Analyze(
                    decoded,
                    ArgumentSlotCount(method));
            if (!reaching.IsComplete)
            {
                return Failed(
                    method.MetadataToken,
                    LeakTriageFailureKind.ControlFlowAnalysis,
                    "IncompleteReachingDefinitions");
            }

            InstructionExceptionFlowFacts exceptionFlow =
                availableFlow.Value;
            return AnalyzeDecoded(
                method,
                decoded.Instructions,
                decoded.Blocks,
                reaching,
                calls,
                (releases, boundaries) =>
                    ResourceExceptionPathAnalyzer
                        .UnprotectedThrowingBoundaries(
                            decoded.Blocks,
                            exceptionFlow,
                            catchAllCleanup,
                            releases,
                            boundaries));
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return Failed(
                method,
                LeakTriageFailureKind.ControlFlowAnalysis,
                ex);
        }
    }

    delegate ImmutableArray<ArrayPoolExceptionBoundary>
        FindUnprotectedBoundaries(
            ImmutableArray<int> releases,
            ImmutableArray<ArrayPoolExceptionBoundary> boundaries);

    static LeakTriageResult AnalyzeDecoded(
        MethodIdentity method,
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph graph,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, MemberRef> calls,
        FindUnprotectedBoundaries findUnprotectedBoundaries)
    {
        var candidates =
            ImmutableArray.CreateBuilder<LeakTriageCandidate>();
        var exceptionPathCandidates =
            ImmutableArray.CreateBuilder<
                ArrayPoolExceptionPathCandidate>();
        ImmutableArray<ArrayPoolUseClassifier.RentedLocal> rents =
            ArrayPoolUseClassifier.FindRents(
                method,
                instructions,
                graph,
                reaching,
                calls,
                candidates).ToImmutableArray();
        if (rents.IsEmpty)
            return new LeakTriageResult([], candidates.ToImmutable());

        var findings =
            ImmutableArray.CreateBuilder<LeakTriageFinding>();
        foreach (ArrayPoolUseClassifier.RentedLocal rent in rents)
        {
            AnalyzeRent(
                method,
                instructions,
                graph,
                reaching,
                calls,
                findUnprotectedBoundaries,
                rent,
                findings,
                candidates,
                exceptionPathCandidates);
        }

        return new LeakTriageResult(
            findings.ToImmutable(),
            candidates.ToImmutable())
        {
            ExceptionPathCandidates =
                exceptionPathCandidates.ToImmutable(),
        };
    }

    static LeakTriageResult Empty { get; } = new([], []);

    static LeakTriageResult Suppressed(MethodIdentity method, string shape, string evidence, int? rentOffset, int? ilOffset)
        => new([], [new LeakTriageCandidate(method, shape, evidence, rentOffset, ilOffset)]);

    internal static LeakTriageResult Failed(
        MethodIdentity method,
        LeakTriageFailureKind kind,
        Exception exception) =>
        Failed(
            method.MetadataToken,
            kind,
            exception.GetType().Name);

    internal static LeakTriageResult Failed(
        int methodToken,
        LeakTriageFailureKind kind,
        string reason) =>
        new([], [])
        {
            Failures =
            [
                new LeakTriageFailure(
                    methodToken,
                    kind,
                    reason),
            ],
        };

    static void ThrowIfFailed(LeakTriageResult result)
    {
        if (result.Failures.IsEmpty)
            return;
        LeakTriageFailure first = result.Failures[0];
        throw new InvalidOperationException(
            $"Leak analysis was incomplete at method token "
            + $"0x{first.MethodToken:X8} during {first.Kind} "
            + $"({first.Reason}).");
    }

    static void AnalyzeRent(
        MethodIdentity method,
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph graph,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, MemberRef> calls,
        FindUnprotectedBoundaries findUnprotectedBoundaries,
        ArrayPoolUseClassifier.RentedLocal rent,
        ImmutableArray<LeakTriageFinding>.Builder findings,
        ImmutableArray<LeakTriageCandidate>.Builder candidates,
        ImmutableArray<ArrayPoolExceptionPathCandidate>.Builder exceptionPathCandidates)
    {
        var releases = ImmutableArray.CreateBuilder<int>();
        var safeUses = ImmutableArray.CreateBuilder<int>();
        var throwingBoundaries = ImmutableArray.CreateBuilder<ArrayPoolExceptionBoundary>();
        var directThrowingBoundaries =
            ImmutableArray.CreateBuilder<ArrayPoolExceptionBoundary>();
        var directUseOffsets = new Dictionary<int, int>();
        AmbiguousUse? firstAmbiguous = null;

        foreach (var use in reaching.UsesOf(rent.Definition))
        {
            if (use.Address)
            {
                if (firstAmbiguous is null)
                {
                    const string shape = "alias-or-field-suppressed";
                    ArrayPoolUseClassifier.AddCandidate(candidates, method, shape, "Rented array address is observed.", rent.RentOffset, use.Offset);
                    firstAmbiguous = new AmbiguousUse(use.Offset, shape);
                }
                continue;
            }

            var classification = ArrayPoolUseClassifier.ClassifyUse(
                instructions,
                calls,
                use.Offset,
                rent.Slot,
                rent.Type);
            switch (classification.Kind)
            {
                case ArrayPoolUseClassifier.UseKind.Release:
                    releases.Add(use.Offset);
                    break;
                case ArrayPoolUseClassifier.UseKind.LocalUse:
                    safeUses.Add(use.Offset);
                    break;
                default:
                    if (firstAmbiguous is null)
                    {
                        ArrayPoolUseClassifier.AddCandidate(candidates, method, classification.CandidateShape, classification.Evidence, rent.RentOffset, use.Offset);
                        firstAmbiguous = new AmbiguousUse(use.Offset, classification.CandidateShape);
                    }
                    if (classification.CandidateShape == "cross-method-suppressed"
                        && classification.Boundary is { } boundary)
                    {
                        if (!classification.NonThrowingSetupBoundary)
                        {
                            throwingBoundaries.Add(boundary);
                            directThrowingBoundaries.Add(boundary);
                            directUseOffsets.TryAdd(boundary.ILOffset, use.Offset);
                        }
                        if ((classification.NonThrowingSetupBoundary
                                || ArrayPoolUseClassifier.IsTransparentWrapperBoundary(
                                    boundary.Operation))
                            &&                             ResourceExceptionPathAnalyzer.FindBoundaryAfterSetup(
                            instructions,
                            reaching,
                            calls,
                            boundary) is { } downstream)
                        {
                            throwingBoundaries.Add(downstream);
                        }
                    }
                    break;
            }
        }

        var releaseOffsets = releases.ToImmutable();
        var safeUseOffsets = safeUses.ToImmutable();

        if (firstAmbiguous is { } ambiguous)
        {
            if (ambiguous.Shape == "cross-method-suppressed"
                && findUnprotectedBoundaries(
                    releaseOffsets,
                    throwingBoundaries.ToImmutable()) is { Length: > 0 } unprotectedBoundaries)
            {
                var directUnprotectedBoundaries =
                    findUnprotectedBoundaries(
                        releaseOffsets,
                        directThrowingBoundaries.ToImmutable());
                if (directUnprotectedBoundaries is { Length: > 0 })
                {
                    int throwingBoundary =
                        directUnprotectedBoundaries[0].ILOffset;
                    int candidateOffset =
                        directUseOffsets.GetValueOrDefault(
                            throwingBoundary,
                            throwingBoundary);
                    ArrayPoolUseClassifier.AddCandidate(
                        candidates,
                        method,
                        "exception-path-leak-candidate",
                        $"Rented array crosses a method boundary at IL_{candidateOffset:X4} before a modeled cleanup; an exception can bypass Return.",
                        rent.RentOffset,
                        candidateOffset);
                }
                exceptionPathCandidates.Add(
                    new ArrayPoolExceptionPathCandidate(
                        method,
                        rent.RentOffset,
                        unprotectedBoundaries));
            }
            return;
        }

        // Multiple releases often encode correlated branch predicates (`if (c) return; if (!c) return`).
        // Without predicate facts, fail closed on leaks and only keep same-block misuse shapes below.
        var exitKind = releaseOffsets.Length <= 1
            ? ResourceExceptionPathAnalyzer.PathExitsWithoutRelease(instructions, graph, calls, rent.StoreOffset, releaseOffsets)
            : ResourceExceptionPathAnalyzer.LeakExitKind.None;
        if (exitKind != ResourceExceptionPathAnalyzer.LeakExitKind.None)
        {
            ArrayPoolUseClassifier.AddCandidate(
                candidates,
                method,
                exitKind == ResourceExceptionPathAnalyzer.LeakExitKind.Exception ? "exception-path-leak-candidate" : "normal-path-leak-candidate",
                $"ArrayPool<T>.Shared.Rent at IL_{rent.RentOffset:X4} reaches an unreleased {(exitKind == ResourceExceptionPathAnalyzer.LeakExitKind.Exception ? "exception" : "normal")} exit.",
                rent.RentOffset,
                rent.RentOffset);
            if (exitKind == ResourceExceptionPathAnalyzer.LeakExitKind.Exception)
            {
                exceptionPathCandidates.Add(
                    new ArrayPoolExceptionPathCandidate(method, rent.RentOffset, []));
            }
            findings.Add(new LeakTriageFinding(
                method,
                "arraypool-rent-not-returned",
                $"ArrayPool<T>.Shared.Rent at IL_{rent.RentOffset:X4} is not returned on every modeled path.",
                "high",
                rent.RentOffset,
                rent.RentOffset));
        }

        // Same-block reachability avoids inventing impossible paths across correlated branches.
        if (releaseOffsets.Length > 0
            && safeUseOffsets.Any(use => releaseOffsets.Any(release => ResourceExceptionPathAnalyzer.ReachesInSameBlock(graph, release, use))))
        {
            int useAfterReturn = safeUseOffsets.Where(use => releaseOffsets.Any(release => ResourceExceptionPathAnalyzer.ReachesInSameBlock(graph, release, use))).Min();
            ArrayPoolUseClassifier.AddCandidate(
                candidates,
                method,
                "use-after-return-candidate",
                $"Use of rented array reaches past Return at IL_{releaseOffsets.Min():X4}.",
                rent.RentOffset,
                useAfterReturn);
            findings.Add(new LeakTriageFinding(
                method,
                "arraypool-use-after-return",
                $"Use of rented array reaches past Return at IL_{releaseOffsets.Min():X4}.",
                "high",
                rent.RentOffset,
                useAfterReturn));
        }

        if (releaseOffsets.Length > 1
            && releaseOffsets.Any(first => releaseOffsets.Any(second => first != second && ResourceExceptionPathAnalyzer.ReachesInSameBlock(graph, first, second))))
        {
            ArrayPoolUseClassifier.AddCandidate(
                candidates,
                method,
                "double-return-candidate",
                $"Rented array can reach a second Return after IL_{releaseOffsets.Min():X4}.",
                rent.RentOffset,
                releaseOffsets.Skip(1).DefaultIfEmpty(releaseOffsets[0]).Min());
            findings.Add(new LeakTriageFinding(
                method,
                "arraypool-double-return",
                $"Rented array can reach a second Return after IL_{releaseOffsets.Min():X4}.",
                "high",
                rent.RentOffset,
                releaseOffsets.Skip(1).DefaultIfEmpty(releaseOffsets[0]).Min()));
        }
    }

    static int ArgumentSlotCount(MethodIdentity method)
        => method.ParameterTypes.Length + (method.IsStatic ? 0 : 1);

    internal static bool IsRecoverable(Exception ex)
        => ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException;

    sealed record AmbiguousUse(int Offset, string Shape);

}
