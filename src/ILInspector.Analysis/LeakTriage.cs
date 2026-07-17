using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

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
}

public static class LeakTriageAnalyzer
{
    public static ImmutableArray<LeakTriageFinding> AnalyzeAssembly(string path)
        => AnalyzeAssemblyDetailed(path).Findings;

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
        ArgumentNullException.ThrowIfNull(method);
        return method with
        {
            IsExtension = false,
            CallerUnsafeMode = CallerUnsafeMode.None,
            GenericArity = 0,
            GenericParameterNames = [],
        };
    }

    public static ImmutableArray<LeakTriageFinding> AnalyzeMethod(
        MethodIdentity method,
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        Func<int, MemberRef> resolveMethod)
        => AnalyzeMethodDetailed(method, il, exceptionRegions, resolveMethod, null).Findings;

    public static ImmutableArray<LeakTriageFinding> AnalyzeMethod(
        MethodIdentity method,
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        Func<int, MemberRef> resolveMethod,
        Func<int, TypeRef?>? resolveCatchType)
        => AnalyzeMethodDetailed(method, il, exceptionRegions, resolveMethod, resolveCatchType).Findings;

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

        bool hasArrayPoolRent = false;
        try
        {
            var instructions = InstructionDecoder.Decode(il);
            var calls = BuildCallMap(instructions, resolveMethod);
            hasArrayPoolRent = calls.Values.Any(IsArrayPoolRent);
            if (!hasArrayPoolRent)
                return Empty;

            var graph = BlockGraph.Build(il.Length, instructions, exceptionRegions);
            if (!graph.IsComplete)
                return Suppressed(method, "incomplete-cfg-or-rd-suppressed", graph.IncompleteReason ?? "Incomplete CFG/RD evidence.", null, null);

            var reaching = ReachingDefinitions.Analyze(il, ArgumentSlotCount(method), exceptionRegions);
            if (!reaching.IsComplete)
                return Suppressed(method, "incomplete-cfg-or-rd-suppressed", reaching.IncompleteReason ?? "Incomplete CFG/RD evidence.", null, null);

            var catchAllCleanup = ComputeCreditableCatchCleanup(exceptionRegions, resolveCatchType);
            var candidates = ImmutableArray.CreateBuilder<LeakTriageCandidate>();
            var exceptionPathCandidates = ImmutableArray.CreateBuilder<ArrayPoolExceptionPathCandidate>();
            var rents = FindRents(method, instructions, graph, reaching, calls, candidates).ToImmutableArray();
            if (rents.Length == 0)
                return new LeakTriageResult([], candidates.ToImmutable());

            var findings = ImmutableArray.CreateBuilder<LeakTriageFinding>();
            foreach (var rent in rents)
            {
                AnalyzeRent(
                    method,
                    instructions,
                    graph,
                    reaching,
                    calls,
                    exceptionRegions,
                    catchAllCleanup,
                    rent,
                    findings,
                    candidates,
                    exceptionPathCandidates);
            }

            return new LeakTriageResult(findings.ToImmutable(), candidates.ToImmutable())
            {
                ExceptionPathCandidates = exceptionPathCandidates.ToImmutable(),
            };
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return hasArrayPoolRent
                ? Suppressed(method, "incomplete-cfg-or-rd-suppressed", ex.Message, null, null)
                : Empty;
        }
    }

    static LeakTriageResult Empty { get; } = new([], []);

    static LeakTriageResult Suppressed(MethodIdentity method, string shape, string evidence, int? rentOffset, int? ilOffset)
        => new([], [new LeakTriageCandidate(method, shape, evidence, rentOffset, ilOffset)]);

    static void AnalyzeRent(
        MethodIdentity method,
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph graph,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, MemberRef> calls,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        IReadOnlySet<(int TryOffset, int TryLength, int HandlerOffset)> catchAllCleanup,
        RentedLocal rent,
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
                    AddCandidate(candidates, method, shape, "Rented array address is observed.", rent.RentOffset, use.Offset);
                    firstAmbiguous = new AmbiguousUse(use.Offset, shape);
                }
                continue;
            }

            var classification = ClassifyUse(
                instructions,
                calls,
                use.Offset,
                rent.Slot,
                rent.Type);
            switch (classification.Kind)
            {
                case UseKind.Release:
                    releases.Add(use.Offset);
                    break;
                case UseKind.LocalUse:
                    safeUses.Add(use.Offset);
                    break;
                default:
                    if (firstAmbiguous is null)
                    {
                        AddCandidate(candidates, method, classification.CandidateShape, classification.Evidence, rent.RentOffset, use.Offset);
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
                                || IsTransparentWrapperBoundary(
                                    boundary.Operation))
                            && FindBoundaryAfterSetup(
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
                && UnprotectedThrowingBoundaries(
                    graph,
                    exceptionRegions,
                    catchAllCleanup,
                    releaseOffsets,
                    throwingBoundaries.ToImmutable()) is { Length: > 0 } unprotectedBoundaries)
            {
                var directUnprotectedBoundaries =
                    UnprotectedThrowingBoundaries(
                        graph,
                        exceptionRegions,
                        catchAllCleanup,
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
                    AddCandidate(
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
            ? PathExitsWithoutRelease(instructions, graph, calls, rent.StoreOffset, releaseOffsets)
            : LeakExitKind.None;
        if (exitKind != LeakExitKind.None)
        {
            AddCandidate(
                candidates,
                method,
                exitKind == LeakExitKind.Exception ? "exception-path-leak-candidate" : "normal-path-leak-candidate",
                $"ArrayPool<T>.Shared.Rent at IL_{rent.RentOffset:X4} reaches an unreleased {(exitKind == LeakExitKind.Exception ? "exception" : "normal")} exit.",
                rent.RentOffset,
                rent.RentOffset);
            if (exitKind == LeakExitKind.Exception)
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
            && safeUseOffsets.Any(use => releaseOffsets.Any(release => ReachesInSameBlock(graph, release, use))))
        {
            int useAfterReturn = safeUseOffsets.Where(use => releaseOffsets.Any(release => ReachesInSameBlock(graph, release, use))).Min();
            AddCandidate(
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
            && releaseOffsets.Any(first => releaseOffsets.Any(second => first != second && ReachesInSameBlock(graph, first, second))))
        {
            AddCandidate(
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

    static void AddCandidate(
        ImmutableArray<LeakTriageCandidate>.Builder candidates,
        MethodIdentity method,
        string shape,
        string evidence,
        int? rentOffset,
        int? ilOffset)
        => candidates.Add(new LeakTriageCandidate(method, shape, evidence, rentOffset, ilOffset));

    static LeakExitKind PathExitsWithoutRelease(
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph graph,
        IReadOnlyDictionary<int, MemberRef> calls,
        int startOffset,
        ImmutableArray<int> releases)
    {
        int startBlock = graph.BlockIndexAt(startOffset);
        if (startBlock < 0)
            return LeakExitKind.None;

        var releaseSet = releases.ToHashSet();
        var visited = new HashSet<(int Block, bool Released)>();
        var stack = new Stack<(int Block, bool Released, int StartOffset)>();
        stack.Push((startBlock, Released: false, StartOffset: startOffset));
        bool sawExceptionExit = false;

        while (stack.Count > 0)
        {
            var (blockIndex, releasedIn, blockStartOffset) = stack.Pop();
            if (!visited.Add((blockIndex, releasedIn)))
                continue;

            var block = graph.Blocks[blockIndex];
            bool released = ProcessBlockForRelease(instructions, calls, block, blockStartOffset, releaseSet, releasedIn);
            var successors = block.Edges.Successors;
            if (!released && block.Edges.ExitsMethod && successors.Count == 0)
            {
                if (BlockExitsByException(instructions, calls, block))
                    sawExceptionExit = true;
                else
                    return LeakExitKind.Normal;
            }
            foreach (int successor in successors)
                stack.Push((successor, released, graph.Blocks[successor].Start));
        }

        return sawExceptionExit ? LeakExitKind.Exception : LeakExitKind.None;
    }

    static bool ProcessBlockForRelease(
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyDictionary<int, MemberRef> calls,
        InstructionBlock block,
        int startOffset,
        IReadOnlySet<int> releases,
        bool released)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Offset < block.Start || instruction.Offset >= block.End)
                continue;
            if (instruction.Offset < startOffset)
                continue;
            if (releases.Contains(instruction.Offset))
                released = true;
            if (!released && IsDefinitelyThrowingCall(instruction, calls))
                return false;
        }
        return released;
    }

    static bool IsDefinitelyThrowingCall(DecodedInstruction instruction, IReadOnlyDictionary<int, MemberRef> calls)
        => instruction.OpCode is ILOpCode.Throw or ILOpCode.Rethrow
           || (calls.TryGetValue(instruction.Offset, out var callee)
               && FrameworkIdentity.IsCoreLibraryType(callee.DeclaringType, "System", "ThrowHelper"));

    static bool BlockExitsByException(
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyDictionary<int, MemberRef> calls,
        InstructionBlock block)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Offset < block.Start || instruction.Offset >= block.End)
                continue;
            if (IsDefinitelyThrowingCall(instruction, calls))
                return true;
        }

        return false;
    }

    static bool ReachesInSameBlock(BlockGraph graph, int fromOffset, int toOffset)
    {
        int fromBlock = graph.BlockIndexAt(fromOffset);
        int toBlock = graph.BlockIndexAt(toOffset);
        return fromBlock >= 0 && fromBlock == toBlock && fromOffset < toOffset;
    }

    static bool ReleasedBeforeUseInSameBlock(BlockGraph graph, ImmutableArray<int> releases, int useOffset)
        => releases.Any(release => ReachesInSameBlock(graph, release, useOffset));

    static ImmutableArray<ArrayPoolExceptionBoundary> UnprotectedThrowingBoundaries(
        BlockGraph graph,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        IReadOnlySet<(int TryOffset, int TryLength, int HandlerOffset)> catchAllCleanup,
        ImmutableArray<int> releases,
        ImmutableArray<ArrayPoolExceptionBoundary> throwingBoundaries)
    {
        var result = ImmutableArray.CreateBuilder<ArrayPoolExceptionBoundary>();
        var seenOffsets = new HashSet<int>();
        foreach (var boundary in throwingBoundaries.OrderBy(static boundary => boundary.ILOffset))
        {
            if (seenOffsets.Add(boundary.ILOffset)
                && !ReleasedBeforeUseInSameBlock(graph, releases, boundary.ILOffset)
                && !HasCleanupReleaseForUse(
                    exceptionRegions,
                    catchAllCleanup,
                    releases,
                    boundary.ILOffset))
            {
                result.Add(boundary);
            }
        }

        return result.ToImmutable();
    }

    static bool HasCleanupReleaseForUse(
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        IReadOnlySet<(int TryOffset, int TryLength, int HandlerOffset)> catchAllCleanup,
        ImmutableArray<int> releases,
        int useOffset)
    {
        // Exception regions are ordered innermost-first (ECMA-335 II.19: most-nested clauses
        // precede enclosing ones), which is also EH search order. Walk the handlers that cover the
        // boundary in that order: a `finally`/`fault` always runs, so a release inside one protects
        // the boundary. A catch-all (`catch {}` / `catch (Exception)`) protects the boundary only
        // if it is the FIRST catch/filter that covers it - any earlier catch/filter (a sibling
        // typed catch, or an inner catch on a nested try) would handle the exception first and, if
        // it does not release, the array still leaks.
        bool interceptingCatchSeen = false;
        foreach (var region in exceptionRegions)
        {
            if (!ContainsOffset(region.TryOffset, region.TryLength, useOffset))
                continue;

            if (region.Kind is ExceptionRegionKind.Finally or ExceptionRegionKind.Fault)
            {
                if (releases.Any(release => ContainsOffset(region.HandlerOffset, region.HandlerLength, release)))
                    return true;
                continue;
            }

            if (region.Kind is not (ExceptionRegionKind.Catch or ExceptionRegionKind.Filter))
                continue;

            if (!interceptingCatchSeen
                && catchAllCleanup.Contains((region.TryOffset, region.TryLength, region.HandlerOffset))
                && releases.Any(release => ContainsOffset(region.HandlerOffset, region.HandlerLength, release)))
                return true;

            interceptingCatchSeen = true;
        }

        return false;
    }

    // Try-range/handler identity of catch-all clauses (`catch {}` = `System.Object`, or
    // `catch (Exception)`) - the catch shapes that catch every managed exception (non-CLS throws
    // are wrapped as RuntimeWrappedException by the default
    // RuntimeCompatibility(WrapNonExceptionThrows=true)). Whether such a clause actually protects a
    // given boundary is decided per-boundary in HasCleanupReleaseForUse, which fails closed if an
    // earlier catch/filter could intercept the exception first. Empty when no catch-type resolver
    // is supplied (e.g. direct AnalyzeMethod callers), preserving the prior finally/fault-only
    // behavior. Conditional releases inside the handler are credited at the same fidelity as the
    // existing finally/fault check (containment, not post-domination).
    static IReadOnlySet<(int TryOffset, int TryLength, int HandlerOffset)> ComputeCreditableCatchCleanup(
        IReadOnlyCollection<ExceptionRegion> exceptionRegions,
        Func<int, TypeRef?>? resolveCatchType)
    {
        if (resolveCatchType is null)
            return EmptyCatchCleanup;

        HashSet<(int, int, int)>? creditable = null;
        foreach (var region in exceptionRegions)
        {
            if (region.Kind is not ExceptionRegionKind.Catch || region.CatchType.IsNil)
                continue;
            var catchType = resolveCatchType(MetadataTokens.GetToken(region.CatchType));
            if (catchType is null
                || !(FrameworkIdentity.IsCoreLibraryType(catchType, "System", "Object")
                    || FrameworkIdentity.IsCoreLibraryType(catchType, "System", "Exception")))
                continue;
            (creditable ??= []).Add((region.TryOffset, region.TryLength, region.HandlerOffset));
        }

        return creditable is null ? EmptyCatchCleanup : creditable;
    }

    static readonly IReadOnlySet<(int TryOffset, int TryLength, int HandlerOffset)> EmptyCatchCleanup =
        ImmutableHashSet<(int, int, int)>.Empty;

    internal static TypeRef? ResolveCatchTypeRef(
        MetadataReader reader,
        EntityHandle handle,
        GenericScope scope)
        => handle.Kind switch
    {
        HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(reader, scope, (TypeSpecificationHandle)handle, 0),
        _ => null,
    };

    static bool ContainsOffset(int start, int length, int offset)
        => offset >= start && offset < start + length;

    static IEnumerable<RentedLocal> FindRents(
        MethodIdentity method,
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph graph,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, MemberRef> calls,
        ImmutableArray<LeakTriageCandidate>.Builder candidates)
    {
        foreach (var instruction in instructions)
        {
            if (!calls.TryGetValue(instruction.Offset, out var callee) || !IsArrayPoolRent(callee))
                continue;
            if (!RentUsesSharedReceiver(instructions, graph, calls, instruction.Offset))
            {
                AddCandidate(
                    candidates,
                    method,
                    "ownership-transfer-suppressed",
                    $"ArrayPool<T>.Rent at IL_{instruction.Offset:X4} does not use the Shared receiver.",
                    instruction.Offset,
                    instruction.Offset);
                continue;
            }
            if (!TryFindNextNonNop(instructions, instruction.NextOffset, out var store)
                || !TryReadStoreLocal(store, out int slot))
            {
                AddCandidate(
                    candidates,
                    method,
                    "ownership-transfer-suppressed",
                    $"ArrayPool<T>.Shared.Rent at IL_{instruction.Offset:X4} is not stored to a modeled local.",
                    instruction.Offset,
                    instruction.Offset);
                continue;
            }

            var definition = reaching.Definitions.FirstOrDefault(d =>
                !d.IsArgument && d.Slot == slot && d.Offset == store.Offset);
            if (definition is null)
            {
                AddCandidate(
                    candidates,
                    method,
                    "incomplete-cfg-or-rd-suppressed",
                    $"ArrayPool<T>.Shared.Rent local definition at IL_{store.Offset:X4} is missing from reaching definitions.",
                    instruction.Offset,
                    store.Offset);
                continue;
            }

            yield return new RentedLocal(
                instruction.Offset,
                store.Offset,
                slot,
                definition,
                callee.ReturnType);
        }
    }

    static bool RentUsesSharedReceiver(
        ImmutableArray<DecodedInstruction> instructions,
        BlockGraph graph,
        IReadOnlyDictionary<int, MemberRef> calls,
        int rentOffset)
    {
        int blockIndex = graph.BlockIndexAt(rentOffset);
        if (blockIndex < 0)
            return false;
        var block = graph.Blocks[blockIndex];
        for (int i = instructions.Length - 1, inspected = 0; i >= 0; i--)
        {
            var instruction = instructions[i];
            if (instruction.Offset < block.Start)
                return false;
            if (instruction.Offset >= rentOffset || instruction.OpCode == ILOpCode.Nop)
                continue;
            if (calls.TryGetValue(instruction.Offset, out var callee))
                return IsArrayPoolSharedGetter(callee);
            if (!IsSimpleArgumentPush(instruction.OpCode))
                return false;
            if (++inspected > 4)
                return false;
        }
        return false;
    }

    static UseClassification ClassifyUse(
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyDictionary<int, MemberRef> calls,
        int loadOffset,
        int slot,
        TypeRef? valueType = null)
    {
        if (!TryFindInstruction(instructions, loadOffset, out int index, out var load)
            || !IsLoadLocalOrAddress(load, slot))
            return UseClassification.OwnershipTransfer("Rented array use could not be classified.");

        int extra = 0;
        for (int i = index + 1; i < instructions.Length; i++)
        {
            var instruction = instructions[i];
            var opcode = instruction.OpCode;
            if (IsSimpleArgumentPush(opcode))
            {
                extra++;
                continue;
            }
            if (opcode == ILOpCode.Ldlen)
                return extra == 0 ? UseClassification.LocalUse : UseClassification.OwnershipTransfer("Rented array length use is ambiguous.");
            if (IsElementRead(opcode))
                return extra == 1 ? UseClassification.LocalUse : UseClassification.OwnershipTransfer("Rented array element read is ambiguous.");
            if (IsElementStore(opcode))
                return extra == 2 ? UseClassification.LocalUse : UseClassification.OwnershipTransfer("Rented array element store is ambiguous.");
            if (IsFieldStore(opcode) || IsLocalStore(opcode))
                return UseClassification.AliasOrField("Rented array is stored into another location.");
            if (opcode == ILOpCode.Ret)
                return UseClassification.OwnershipTransfer("Rented array escapes through the return value.");
            if (calls.TryGetValue(instruction.Offset, out var callee))
            {
                if (IsArrayPoolReturn(callee) && extra < callee.ParameterTypes.Length)
                    return UseClassification.Release;
                int consumedArguments =
                    callee.ParameterTypes.Length
                    + (instruction.OpCode != ILOpCode.Newobj && callee.HasThis
                        ? 1
                        : 0);
                if (consumedArguments <= extra)
                {
                    extra -= consumedArguments;
                    if (instruction.OpCode == ILOpCode.Newobj
                        || !FrameworkIdentity.IsCoreLibraryType(
                            callee.ReturnType,
                            "System",
                            "Void"))
                    {
                        extra++;
                    }
                    continue;
                }
                return UseClassification.CrossMethod(
                    $"Rented array is passed to {callee.DeclaringType.Name}::{callee.Name}.",
                    IsNonThrowingSetupBoundary(callee, valueType),
                    new ArrayPoolExceptionBoundary(instruction.Offset, callee));
            }
            return UseClassification.OwnershipTransfer($"Rented array reaches unsupported opcode {opcode}.");
        }

        return UseClassification.OwnershipTransfer("Rented array use reaches the end of the instruction stream.");
    }

    static IReadOnlyDictionary<int, MemberRef> BuildCallMap(
        ImmutableArray<DecodedInstruction> instructions,
        Func<int, MemberRef> resolveMethod)
    {
        var calls = new Dictionary<int, MemberRef>();
        foreach (var instruction in instructions)
        {
            if (instruction.OpCode is not (ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj))
                continue;
            calls[instruction.Offset] = resolveMethod(checked((int)instruction.OperandValue));
        }
        return calls;
    }

    static ArrayPoolExceptionBoundary? FindBoundaryAfterSetup(
        ImmutableArray<DecodedInstruction> instructions,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, MemberRef> calls,
        ArrayPoolExceptionBoundary setup,
        int depth = 0)
    {
        if (depth >= 4)
            return null;

        if (!TryFindInstruction(
            instructions,
            setup.ILOffset,
            out int setupIndex,
            out var setupInstruction))
        {
            return null;
        }

        // Constructor signatures return void, but newobj pushes the constructed value.
        if (setupInstruction.OpCode != ILOpCode.Newobj
            && FrameworkIdentity.IsCoreLibraryType(
                setup.Operation.ReturnType,
                "System",
                "Void"))
        {
            // Roslyn can initialize a value-type wrapper local through
            // ldloca + call .ctor instead of newobj + stloc.
            if (TryFindInPlaceWrapperLocal(
                    instructions,
                    setupIndex,
                    setup.Operation,
                    out int slot))
            {
                return FindBoundaryAfterInPlaceSetup(
                    instructions,
                    calls,
                    setupIndex,
                    slot);
            }

            return null;
        }

        int extraArguments = 0;
        for (int i = setupIndex + 1;
            i < instructions.Length && i <= setupIndex + 16;
            i++)
        {
            var instruction = instructions[i];
            if (instruction.OpCode == ILOpCode.Nop)
                continue;
            if (IsSetupArgumentPush(instruction.OpCode))
            {
                extraArguments++;
                continue;
            }

            if (extraArguments == 0
                && TryReadStoreLocal(instruction, out int slot))
            {
                var definition = reaching.Definitions.FirstOrDefault(candidate =>
                    !candidate.IsArgument
                    && candidate.Slot == slot
                    && candidate.Offset == instruction.Offset);
                if (definition is null)
                    return null;

                foreach (var use in reaching.UsesOf(definition)
                    .OrderBy(static use => use.Offset))
                {
                    var classification = ClassifyUse(
                        instructions,
                        calls,
                        use.Offset,
                        slot);
                    if (classification.CandidateShape
                            != "cross-method-suppressed"
                        || classification.Boundary is not { } localBoundary)
                    {
                        continue;
                    }

                    if (!classification.NonThrowingSetupBoundary)
                        return localBoundary;

                    if (FindBoundaryAfterSetup(
                            instructions,
                            reaching,
                            calls,
                            localBoundary,
                            depth + 1) is { } downstream)
                    {
                        return downstream;
                    }
                }

                return null;
            }

            if (!calls.TryGetValue(instruction.Offset, out var callee))
                return null;

            int consumedArguments =
                callee.ParameterTypes.Length
                + (instruction.OpCode != ILOpCode.Newobj && callee.HasThis
                    ? 1
                    : 0);
            if (consumedArguments <= extraArguments)
            {
                extraArguments -= consumedArguments;
                if (instruction.OpCode == ILOpCode.Newobj
                    || !FrameworkIdentity.IsCoreLibraryType(
                        callee.ReturnType,
                        "System",
                        "Void"))
                {
                    extraArguments++;
                }

                continue;
            }

            var boundary =
                new ArrayPoolExceptionBoundary(instruction.Offset, callee);
            if (!IsNonThrowingSetupBoundary(callee))
            {
                return boundary;
            }

            if (FrameworkIdentity.IsCoreLibraryType(
                callee.ReturnType,
                "System",
                "Void"))
            {
                return null;
            }

            extraArguments = 0;
        }

        return null;
    }

    static ArrayPoolExceptionBoundary? FindBoundaryAfterInPlaceSetup(
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyDictionary<int, MemberRef> calls,
        int setupIndex,
        int slot)
    {
        for (int i = setupIndex + 1;
            i < instructions.Length && i <= setupIndex + 16;
            i++)
        {
            var instruction = instructions[i];
            if (instruction.OpCode == ILOpCode.Nop)
                continue;
            if (TryReadStoreLocal(instruction, out int storedSlot)
                && storedSlot == slot)
            {
                return null;
            }
            if (!IsLoadLocalOrAddress(instruction, slot))
                continue;

            var classification = ClassifyUse(
                instructions,
                calls,
                instruction.Offset,
                slot);
            return classification.CandidateShape == "cross-method-suppressed"
                ? classification.Boundary
                : null;
        }

        return null;
    }

    static bool TryFindInPlaceWrapperLocal(
        ImmutableArray<DecodedInstruction> instructions,
        int setupIndex,
        MemberRef member,
        out int slot)
    {
        slot = -1;
        if (instructions[setupIndex].OpCode != ILOpCode.Call
            || !member.HasThis
            || !IsTransparentWrapperBoundary(member))
        {
            return false;
        }

        int index = setupIndex - 1;
        for (int remaining = member.ParameterTypes.Length;
            remaining > 0;
            remaining--)
        {
            while (index >= 0 && instructions[index].OpCode == ILOpCode.Nop)
                index--;
            if (index < 0
                || !IsSetupArgumentPush(instructions[index].OpCode))
            {
                return false;
            }
            index--;
        }

        while (index >= 0 && instructions[index].OpCode == ILOpCode.Nop)
            index--;
        return index >= 0
            && TryReadLoadLocalAddress(instructions[index], out slot);
    }

    static bool TryFindNextNonNop(ImmutableArray<DecodedInstruction> instructions, int offset, out DecodedInstruction instruction)
    {
        foreach (var candidate in instructions)
        {
            if (candidate.Offset < offset || candidate.OpCode == ILOpCode.Nop)
                continue;
            instruction = candidate;
            return true;
        }
        instruction = default!;
        return false;
    }

    static bool TryFindInstruction(ImmutableArray<DecodedInstruction> instructions, int offset, out int index, out DecodedInstruction instruction)
    {
        for (int i = 0; i < instructions.Length; i++)
        {
            if (instructions[i].Offset != offset)
                continue;
            index = i;
            instruction = instructions[i];
            return true;
        }
        index = -1;
        instruction = default!;
        return false;
    }

    static bool TryReadStoreLocal(DecodedInstruction instruction, out int slot)
    {
        slot = instruction.OpCode switch
        {
            ILOpCode.Stloc_0 => 0,
            ILOpCode.Stloc_1 => 1,
            ILOpCode.Stloc_2 => 2,
            ILOpCode.Stloc_3 => 3,
            ILOpCode.Stloc_s or ILOpCode.Stloc => checked((int)instruction.OperandValue),
            _ => -1,
        };
        return slot >= 0;
    }

    static bool IsLoadLocal(DecodedInstruction instruction, int slot)
        => instruction.OpCode switch
        {
            ILOpCode.Ldloc_0 => slot == 0,
            ILOpCode.Ldloc_1 => slot == 1,
            ILOpCode.Ldloc_2 => slot == 2,
            ILOpCode.Ldloc_3 => slot == 3,
            ILOpCode.Ldloc_s or ILOpCode.Ldloc => instruction.OperandValue == slot,
            _ => false,
        };

    static bool IsLoadLocalOrAddress(
        DecodedInstruction instruction,
        int slot)
        => IsLoadLocal(instruction, slot)
            || (TryReadLoadLocalAddress(instruction, out int addressSlot)
                && addressSlot == slot);

    static bool TryReadLoadLocalAddress(
        DecodedInstruction instruction,
        out int slot)
    {
        slot = instruction.OpCode switch
        {
            ILOpCode.Ldloca_s or ILOpCode.Ldloca =>
                checked((int)instruction.OperandValue),
            _ => -1,
        };
        return slot >= 0;
    }

    static bool IsSimpleArgumentPush(ILOpCode opcode)
        => opcode is ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
            or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6
            or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8 or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4
            or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3
            or ILOpCode.Ldarg_s or ILOpCode.Ldarg
            or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3
            or ILOpCode.Ldloc_s or ILOpCode.Ldloc
            or ILOpCode.Ldnull;

    static bool IsSetupArgumentPush(ILOpCode opcode)
        => IsSimpleArgumentPush(opcode)
            || opcode is ILOpCode.Ldc_i8 or ILOpCode.Ldc_r4 or ILOpCode.Ldc_r8
                or ILOpCode.Ldarga_s or ILOpCode.Ldarga
                or ILOpCode.Ldloca_s or ILOpCode.Ldloca
                or ILOpCode.Ldstr
                or ILOpCode.Ldsfld or ILOpCode.Ldsflda
                or ILOpCode.Ldtoken or ILOpCode.Ldftn
                or ILOpCode.Sizeof or ILOpCode.Arglist;

    static bool IsElementRead(ILOpCode opcode)
        => opcode is ILOpCode.Ldelem or ILOpCode.Ldelem_i or ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_i2
            or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8
            or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4 or ILOpCode.Ldelem_ref;

    static bool IsElementStore(ILOpCode opcode)
        => opcode is ILOpCode.Stelem or ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2
            or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8
            or ILOpCode.Stelem_ref;

    static bool IsFieldStore(ILOpCode opcode)
        => opcode is ILOpCode.Stfld or ILOpCode.Stsfld;

    static bool IsLocalStore(ILOpCode opcode)
        => opcode is ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3
            or ILOpCode.Stloc_s or ILOpCode.Stloc;

    static bool IsArrayPoolRent(MemberRef member)
        => member.Kind != MemberKind.Unsupported
           && member.Name == "Rent"
           && member.HasThis
           && member.ParameterTypes.Length == 1
           && FrameworkIdentity.IsCoreLibraryType(member.ParameterTypes[0], "System", "Int32")
           && member.ReturnType.Kind == TypeRefKind.SzArray
           && IsArrayPoolType(member.DeclaringType);

    static bool IsArrayPoolReturn(MemberRef member)
        => member.Kind != MemberKind.Unsupported
           && member.Name == "Return"
           && member.HasThis
           && member.ReturnType.Equals(TypeRef.CoreLib("System", "Void"))
           && member.ParameterTypes.Length is 1 or 2
           && member.ParameterTypes[0].Kind == TypeRefKind.SzArray
           && IsArrayPoolType(member.DeclaringType);

    static bool IsArrayPoolSharedGetter(MemberRef member)
        => member.Kind != MemberKind.Unsupported
           && member.Name == "get_Shared"
           && !member.HasThis
           && member.ParameterTypes.Length == 0
           && IsArrayPoolType(member.DeclaringType)
           && IsArrayPoolType(member.ReturnType);

    static bool IsArrayPoolType(TypeRef type)
        => FrameworkIdentity.IsKnownFrameworkType(type, "System.Buffers", "System.Buffers", "ArrayPool`1")
           || FrameworkIdentity.IsCoreLibraryType(type, "System.Buffers", "ArrayPool`1");

    static bool IsNonThrowingSetupBoundary(
        MemberRef member,
        TypeRef? valueType = null)
    {
        if (member.Kind == MemberKind.Unsupported)
            return false;

        if (member.Name == "KeepAlive" && FrameworkIdentity.IsCoreLibraryType(member.DeclaringType, "System", "GC"))
            return true;

        if (member.Name == "Copy" && FrameworkIdentity.IsCoreLibraryType(member.DeclaringType, "System", "Array"))
            return true;

        if (member.Name == "Clear" && FrameworkIdentity.IsCoreLibraryType(member.DeclaringType, "System", "Array"))
            return true;

        if (member.Name == "CopyTo" && IsSpanType(member.DeclaringType))
            return true;

        if (member.Name == "AsSpan"
            && (FrameworkIdentity.IsCoreLibraryType(member.DeclaringType, "System", "MemoryExtensions")
                || FrameworkIdentity.IsKnownFrameworkType(member.DeclaringType, "System.Memory", "System", "MemoryExtensions")))
        {
            return true;
        }

        if (IsArrayToSpanConversion(member)
            && valueType?.Equals(member.ParameterTypes[0]) == true)
        {
            return true;
        }

        return member.Name is "Clear" or "Fill"
            && IsSpanType(member.DeclaringType);
    }

    static bool IsTransparentWrapperBoundary(MemberRef member)
        => member.Name == ".ctor"
            && member.ParameterTypes is [{ Kind: TypeRefKind.SzArray }, ..]
            && (IsFrameworkGenericType(member.DeclaringType, "Memory`1")
                || IsFrameworkGenericType(
                    member.DeclaringType,
                    "ReadOnlyMemory`1"));

    static bool IsArrayToSpanConversion(MemberRef member)
        => member.Name == "op_Implicit"
            && member.ParameterTypes is [{ Kind: TypeRefKind.SzArray }]
            && (IsSpanType(member.ReturnType)
                || IsReadOnlySpanType(member.ReturnType));

    static bool IsSpanType(TypeRef type)
        => IsFrameworkGenericType(type, "Span`1");

    static bool IsReadOnlySpanType(TypeRef type)
        => IsFrameworkGenericType(type, "ReadOnlySpan`1");

    static bool IsFrameworkGenericType(TypeRef type, string name)
    {
        if (type.Kind != TypeRefKind.GenericInstance || type.ElementType is not { } definition)
            return false;

        return FrameworkIdentity.IsCoreLibraryType(definition, "System", name)
            || FrameworkIdentity.IsKnownFrameworkType(
                definition,
                "System.Memory",
                "System",
                name);
    }

    static int ArgumentSlotCount(MethodIdentity method)
        => method.ParameterTypes.Length + (method.IsStatic ? 0 : 1);

    internal static bool IsRecoverable(Exception ex)
        => ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException;

    sealed record RentedLocal(
        int RentOffset,
        int StoreOffset,
        int Slot,
        LocalDefinition Definition,
        TypeRef Type);

    sealed record AmbiguousUse(int Offset, string Shape);

    enum UseKind
    {
        Release,
        LocalUse,
        Ambiguous,
    }

    readonly record struct UseClassification(
        UseKind Kind,
        string CandidateShape,
        string Evidence,
        bool NonThrowingSetupBoundary = false,
        ArrayPoolExceptionBoundary? Boundary = null)
    {
        public static UseClassification Release { get; } = new(UseKind.Release, "", "");
        public static UseClassification LocalUse { get; } = new(UseKind.LocalUse, "", "");
        public static UseClassification AliasOrField(string evidence) => new(UseKind.Ambiguous, "alias-or-field-suppressed", evidence);
        public static UseClassification CrossMethod(
            string evidence,
            bool nonThrowingSetupBoundary,
            ArrayPoolExceptionBoundary boundary)
            => new(
                UseKind.Ambiguous,
                "cross-method-suppressed",
                evidence,
                nonThrowingSetupBoundary,
                boundary);
        public static UseClassification OwnershipTransfer(string evidence) => new(UseKind.Ambiguous, "ownership-transfer-suppressed", evidence);
    }

    enum LeakExitKind
    {
        None,
        Normal,
        Exception,
    }
}
