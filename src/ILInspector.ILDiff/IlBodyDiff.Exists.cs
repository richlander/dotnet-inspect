using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.ILDiff;

// SPIKE (exp/staged-diff-spike): an early-exit form of the canonical body
// comparison's Exact decision, for eng/measure-staged-diff.cs only. It reuses
// the canonical comparison's own primitives (decode, TryToOperation,
// CanonicalEquals, BranchTargetsMatch, MetadataOperandResolver) and decides
// exactly "IlBodyDiff.Compare(...).Outcome == Exact" without building the LCS:
// Exact means a full-length common subsequence, which for two sequences of one
// length is the identity alignment, so the decision is "same length, each
// operation canonically equal in lockstep, each branch target at the same
// index". It stops at the first operation that differs. Product behaviour is
// unchanged; nothing in the product calls it.
internal enum IlExistsOutcome
{
    Exact,
    Changed,
    Unavailable,
}

// SPIKE (exp/unified-diff-exists): optimizations to the Exists comparison, each
// measured on and off in eng/measure-unified-diff.cs.
[Flags]
internal enum IlExistsSpikeOptions
{
    None = 0,

    /// <summary>
    /// Instructions-only decode for a body with no exception regions, plus the exact
    /// completeness rule the block graph applies to such a body (every branch target an
    /// admitted instruction start inside the body); MethodInstructions.Decode otherwise.
    /// </summary>
    CheckedDecode = 1 << 0,

    /// <summary>
    /// Compare each instruction pair without building CanonicalIlOperation records:
    /// cached opcode family, immediate and slot values as numbers, tokens through the
    /// memoised identities, branch targets by binary search. Same rule as CanonicalEquals.
    /// </summary>
    KeyCompare = 1 << 1,

    /// <summary>
    /// When both bodies have byte-identical IL and identical exception-region extents, decode
    /// and check completeness once (both are functions of those bytes and extents alone) and
    /// compare only token operands, still by their symbolic identities on each side: equal
    /// bytes make every opcode, immediate, slot, and branch offset equal, never the tokens.
    /// </summary>
    SharedDecode = 1 << 2,

    /// <summary>
    /// With CheckedDecode, build the region topology without normal-transfer validation, which
    /// never contributes to block-graph completeness (SPIKE in ILInspector.Instructions).
    /// </summary>
    BlockGraphTopology = 1 << 3,
}

internal sealed class IlBodyExistsComparer
{
    public IlExistsSpikeOptions Options { get; set; }

    /// <summary>SPIKE: memoise Type spellings in both operand resolvers.</summary>
    public bool MemoizeTypes
    {
        set
        {
            _old.SpikeMemoizeTypes = value;
            _new.SpikeMemoizeTypes = value;
        }
    }
    public long CheckedFallbacks { get; private set; }
    readonly IlBodyDiff.MetadataOperandResolver _old;
    readonly IlBodyDiff.MetadataOperandResolver _new;
    readonly IlBodyDiffNormalization _normalization;

    public IlBodyExistsComparer(
        MetadataReader oldReader,
        MetadataReader newReader,
        IlBodyDiffNormalization normalization)
    {
        if ((normalization & IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals) != 0)
            throw new ArgumentOutOfRangeException(nameof(normalization), "Spike does not build ordinal correspondence.");
        _old = IlBodyDiff.CreateSpikeResolver(oldReader, normalization);
        _new = IlBodyDiff.CreateSpikeResolver(newReader, normalization);
        _normalization = normalization;
    }

    // SPIKE (exp/unified-diff-exists): a comparer over a correspondence built once per image pair.
    public IlBodyExistsComparer(
        MetadataReader oldReader,
        MetadataReader newReader,
        IlBodyDiffNormalization normalization,
        CompilerGeneratedOrdinalCorrespondence oldCorrespondence,
        CompilerGeneratedOrdinalCorrespondence newCorrespondence)
    {
        _old = new IlBodyDiff.MetadataOperandResolver(oldReader, normalization, oldCorrespondence);
        _new = new IlBodyDiff.MetadataOperandResolver(newReader, normalization, newCorrespondence);
        _normalization = normalization;
    }

    // SPIKE (exp/unified-diff-exists): attribution counters, read only when Instrument is set.
    public bool Instrument { get; set; }
    public long DecodeTicks { get; set; }
    public long ResolveTicks { get; set; }
    public long ResolveMisses { get; set; }
    public long InstructionsCompared { get; set; }
    public long TopologyTicks { get; set; }
    public long RegionBodies { get; set; }

    /// <summary>Tokens resolved so far (both sides), for work accounting.</summary>
    public long TokensResolved { get; private set; }

    /// <summary>
    /// Lockstep canonical comparison with early exit. When
    /// <paramref name="rawFastPath"/> is set and the raw IL bytes are equal,
    /// only token operands are resolved and compared: every other operand,
    /// opcode, and branch target is byte-identical, so equal token identities
    /// make the bodies canonically Exact.
    /// </summary>
    public IlExistsOutcome Compare(
        MethodBodyBlock oldBody,
        MethodBodyBlock newBody,
        bool rawFastPath,
        out bool rawEqual,
        out string? failure)
        => Compare(oldBody, newBody, rawFastPath, memoTokens: false, liteDecode: false, out rawEqual, out failure);

    /// <param name="memoTokens">Memoize each side's token identities by (operand kind, token).</param>
    /// <param name="liteDecode">
    /// Decode instructions only (InstructionDecoder), skipping the EH-aware block graph that
    /// MethodInstructions.Decode builds and the canonical comparison uses to reject a body as
    /// incomplete. Not equivalent: a body whose block graph is incomplete is compared anyway.
    /// </param>
    public IlExistsOutcome Compare(
        MethodBodyBlock oldBody,
        MethodBodyBlock newBody,
        bool rawFastPath,
        bool memoTokens,
        bool liteDecode,
        out bool rawEqual,
        out string? failure)
    {
        byte[] oldIl = oldBody.GetILBytes() ?? [];
        byte[] newIl = newBody.GetILBytes() ?? [];
        rawEqual = oldIl.AsSpan().SequenceEqual(newIl);
        long decodeStart = Instrument ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        ImmutableArray<DecodedInstruction> oldInstructions, newInstructions;
        if (!liteDecode && (Options & IlExistsSpikeOptions.SharedDecode) != 0 && (Options & IlExistsSpikeOptions.CheckedDecode) != 0
            && rawEqual && RegionExtentsEqual(oldBody, newBody))
        {
            if (!TryCheckedDecode(oldIl, oldBody, "old", out oldInstructions, out failure))
                return IlExistsOutcome.Unavailable;
            if (Instrument)
            {
                DecodeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - decodeStart;
                SharedDecodes++;
            }
            return CompareTokensOnly(oldInstructions, out failure);
        }
        if (!liteDecode && (Options & IlExistsSpikeOptions.CheckedDecode) != 0)
        {
            if (!TryCheckedDecode(oldIl, oldBody, "old", out oldInstructions, out failure)
                || !TryCheckedDecode(newIl, newBody, "new", out newInstructions, out failure))
            {
                return IlExistsOutcome.Unavailable;
            }
        }
        else if (liteDecode)
        {
            try
            {
                oldInstructions = InstructionDecoder.Decode(oldIl);
                newInstructions = InstructionDecoder.Decode(newIl);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidProgramException)
            {
                failure = ex.Message;
                return IlExistsOutcome.Unavailable;
            }
        }
        else
        {
            var oldDecoded = MethodInstructions.Decode(oldIl, oldIl.Length, oldBody.ExceptionRegions);
            if (!oldDecoded.IsComplete)
            {
                failure = oldDecoded.Blocks.IncompleteReason ?? "old body decode failed";
                return IlExistsOutcome.Unavailable;
            }
            var newDecoded = MethodInstructions.Decode(newIl, newIl.Length, newBody.ExceptionRegions);
            if (!newDecoded.IsComplete)
            {
                failure = newDecoded.Blocks.IncompleteReason ?? "new body decode failed";
                return IlExistsOutcome.Unavailable;
            }
            oldInstructions = oldDecoded.Instructions;
            newInstructions = newDecoded.Instructions;
        }

        if (Instrument)
            DecodeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - decodeStart;
        if ((Options & IlExistsSpikeOptions.KeyCompare) != 0 && memoTokens && !rawFastPath)
            return CompareKeys(oldInstructions, newInstructions, out failure);
        return CompareDecoded(oldInstructions, newInstructions, rawFastPath && rawEqual, memoTokens, out failure);
    }

    public long SharedDecodes { get; private set; }

    static bool RegionExtentsEqual(MethodBodyBlock x, MethodBodyBlock y)
    {
        var a = x.ExceptionRegions;
        var b = y.ExceptionRegions;
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].Kind != b[i].Kind || a[i].TryOffset != b[i].TryOffset || a[i].TryLength != b[i].TryLength
                || a[i].HandlerOffset != b[i].HandlerOffset || a[i].HandlerLength != b[i].HandlerLength
                || a[i].FilterOffset != b[i].FilterOffset)
                return false;
        }
        return true;
    }

    /// <summary>One decoded stream of byte-identical bodies: only token identities can differ.</summary>
    IlExistsOutcome CompareTokensOnly(ImmutableArray<DecodedInstruction> instructions, out string? failure)
    {
        failure = null;
        foreach (var instruction in instructions)
        {
            if (!IlBodyDiff.SpikeIsTokenOperand(instruction.Operand))
                continue;
            InstructionsCompared++;
            if (!TryToken(instruction, _old, _oldMemo, out var ot, out failure)
                || !TryToken(instruction, _new, _newMemo, out var nt, out failure))
                return IlExistsOutcome.Unavailable;
            TokensResolved += 2;
            if (ot != nt)
                return IlExistsOutcome.Changed;
        }
        return IlExistsOutcome.Exact;
    }

    bool[] _admitted = new bool[256];

    /// <summary>
    /// Decode one side. A body with exception regions takes MethodInstructions.Decode. A body
    /// without them is complete under BlockGraph exactly when decode succeeds and every branch
    /// target lies in [0, length) on an admitted instruction start (not after a prefix): an
    /// in-range unadmitted target throws in BuildBlocks, an out-of-range target becomes an
    /// external edge, and an empty region list gives the topology no block-graph reason.
    /// An instruction with targets that does not end its block falls back to the full decode.
    /// </summary>
    bool TryCheckedDecode(byte[] il, MethodBodyBlock body, string side, out ImmutableArray<DecodedInstruction> instructions, out string? failure)
    {
        failure = null;
        // A body with regions also takes this path: its block graph is complete exactly when
        // the region topology gives no block-graph reason and every target is an admitted
        // in-range start. Leave redirection and endfinally edges only add targets that are
        // handler starts or the leave's own in-range target, both leaders. A body with an
        // out-of-range target falls back to the full decode.
        {
            try
            {
                instructions = InstructionDecoder.Decode(il);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidProgramException)
            {
                instructions = default;
                failure = ex.Message;
                return false;
            }
            int length = il.Length;
            if (_admitted.Length < length + 1)
                _admitted = new bool[Math.Max(length + 1, _admitted.Length * 2)];
            else
                Array.Clear(_admitted, 0, length + 1);
            bool fallback = false;
            for (int i = 0; i < instructions.Length; i++)
            {
                if (i == 0 || !instructions[i - 1].OpCode.IsPrefix())
                    _admitted[instructions[i].Offset] = true;
                if (!instructions[i].BranchTargets.IsDefaultOrEmpty && !instructions[i].Branches)
                    fallback = true;
            }
            bool regions = body.ExceptionRegions.Length > 0;
            if (!fallback)
            {
                foreach (var instruction in instructions)
                {
                    foreach (int target in instruction.BranchTargets)
                    {
                        if (target < 0 || target >= length)
                        {
                            if (regions)
                            {
                                fallback = true;
                                break;
                            }
                            failure = "External control-flow targets are not modeled.";
                            return false;
                        }
                        if (!_admitted[target])
                        {
                            failure = $"Branch target IL_{target:X4} does not align with an instruction.";
                            return false;
                        }
                    }
                    if (fallback)
                        break;
                }
            }
            if (!fallback)
            {
                if (regions)
                {
                    ExceptionFlowTopology topology;
                    try
                    {
                        long topologyStart = Instrument ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                        topology = ExceptionFlowTopology.Create(length, instructions, body.ExceptionRegions,
                            validateTransfers: (Options & IlExistsSpikeOptions.BlockGraphTopology) == 0);
                        if (Instrument)
                        {
                            TopologyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - topologyStart;
                            RegionBodies++;
                        }
                    }
                    catch (Exception ex) when (ex is BadImageFormatException or InvalidProgramException)
                    {
                        failure = ex.Message;
                        return false;
                    }
                    if (topology.BlockGraphIncompleteReason is { } reason)
                    {
                        failure = reason;
                        return false;
                    }
                }
                return true;
            }
            CheckedFallbacks++;
        }
        var decoded = MethodInstructions.Decode(il, il.Length, body.ExceptionRegions);
        instructions = decoded.Instructions;
        if (!decoded.IsComplete)
        {
            failure = decoded.Blocks.IncompleteReason ?? side + " body decode failed";
            return false;
        }
        return true;
    }

    /// <summary>Gate helper: whether the checked decode's completeness equals MethodInstructions.Decode's.</summary>
    public bool CheckedCompletenessAgrees(MethodBodyBlock body)
    {
        byte[] il = body.GetILBytes() ?? [];
        bool mine = TryCheckedDecode(il, body, "gate", out _, out _);
        return mine == MethodInstructions.Decode(il, il.Length, body.ExceptionRegions).IsComplete;
    }

    bool TryToken(
        DecodedInstruction instruction,
        IlBodyDiff.MetadataOperandResolver resolver,
        Dictionary<(OperandKind, long), IlOperandIdentity> memo,
        out IlOperandIdentity operand,
        out string? failure)
    {
        failure = null;
        var key = (instruction.Operand, instruction.OperandValue);
        if (memo.TryGetValue(key, out operand!))
            return true;
        long resolveStart = Instrument ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        bool ok = resolver.TryResolve(instruction, out var resolved, out failure);
        if (Instrument)
        {
            ResolveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - resolveStart;
            ResolveMisses++;
        }
        if (!ok)
            return false;
        operand = resolved!;
        memo[key] = operand;
        return true;
    }

    /// <summary>
    /// CanonicalEquals without materialising operations. Equal families imply one operand
    /// shape (the folds are ldc.i4 forms, all immediate; short and long branches, all
    /// targets; and under NormalizeVariableLayout the variable macros, all operand-free),
    /// so operands compare as numbers or memoised token identities.
    /// </summary>
    IlExistsOutcome CompareKeys(
        ImmutableArray<DecodedInstruction> oldInstructions,
        ImmutableArray<DecodedInstruction> newInstructions,
        out string? failure)
    {
        failure = null;
        if (oldInstructions.Length != newInstructions.Length)
            return IlExistsOutcome.Changed;
        bool variableLayout = (_normalization & IlBodyDiffNormalization.NormalizeVariableLayout) != 0;
        for (int i = 0; i < oldInstructions.Length; i++)
        {
            var o = oldInstructions[i];
            var n = newInstructions[i];
            InstructionsCompared++;
            if (IlBodyDiff.SpikeIsTokenOperand(o.Operand))
            {
                if (!TryToken(o, _old, _oldMemo, out var ot, out failure))
                    return IlExistsOutcome.Unavailable;
                if (!IlBodyDiff.SpikeIsTokenOperand(n.Operand))
                    return IlBodyDiff.SpikeOpcodeFamily(o, _normalization) == IlBodyDiff.SpikeOpcodeFamily(n, _normalization)
                        ? throw new InvalidOperationException("Equal families with different operand shapes.")
                        : IlExistsOutcome.Changed;
                if (!TryToken(n, _new, _newMemo, out var nt, out failure))
                    return IlExistsOutcome.Unavailable;
                TokensResolved += 2;
                if (!ReferenceEquals(IlBodyDiff.SpikeOpcodeFamily(o, _normalization), IlBodyDiff.SpikeOpcodeFamily(n, _normalization))
                    && IlBodyDiff.SpikeOpcodeFamily(o, _normalization) != IlBodyDiff.SpikeOpcodeFamily(n, _normalization))
                    return IlExistsOutcome.Changed;
                if (ot != nt)
                    return IlExistsOutcome.Changed;
                continue;
            }
            if (IlBodyDiff.SpikeIsTokenOperand(n.Operand))
            {
                // Resolve the new token as canonicalisation would before deciding.
                if (!TryToken(n, _new, _newMemo, out _, out failure))
                    return IlExistsOutcome.Unavailable;
                return IlExistsOutcome.Changed;
            }
            string fo = IlBodyDiff.SpikeOpcodeFamily(o, _normalization);
            string fn = IlBodyDiff.SpikeOpcodeFamily(n, _normalization);
            if (!ReferenceEquals(fo, fn) && fo != fn)
                return IlExistsOutcome.Changed;
            switch (o.Operand)
            {
                case OperandKind.ShortInlineBrTarget or OperandKind.InlineBrTarget:
                    if (!BranchTargetsAtSameIndex(oldInstructions, o, newInstructions, n))
                        return IlExistsOutcome.Changed;
                    break;
                case OperandKind.InlineSwitch:
                    if (o.BranchTargets.Length != n.BranchTargets.Length
                        || !BranchTargetsAtSameIndex(oldInstructions, o, newInstructions, n))
                        return IlExistsOutcome.Changed;
                    break;
                default:
                    if (Immediate(o, variableLayout, out long ov) != Immediate(n, variableLayout, out long nv) || ov != nv)
                        return IlExistsOutcome.Changed;
                    break;
            }
        }
        return IlExistsOutcome.Exact;
    }

    /// <summary>The operand identity tag and value IlBodyDiff spells as text: 0 none, 1 immediate, 2 slot.</summary>
    static int Immediate(DecodedInstruction instruction, bool variableLayout, out long value)
    {
        switch (instruction.OpCode)
        {
            case ILOpCode.Ldc_i4_m1: value = -1; return 1;
            case ILOpCode.Ldc_i4_0: value = 0; return 1;
            case ILOpCode.Ldc_i4_1: value = 1; return 1;
            case ILOpCode.Ldc_i4_2: value = 2; return 1;
            case ILOpCode.Ldc_i4_3: value = 3; return 1;
            case ILOpCode.Ldc_i4_4: value = 4; return 1;
            case ILOpCode.Ldc_i4_5: value = 5; return 1;
            case ILOpCode.Ldc_i4_6: value = 6; return 1;
            case ILOpCode.Ldc_i4_7: value = 7; return 1;
            case ILOpCode.Ldc_i4_8: value = 8; return 1;
        }
        switch (instruction.Operand)
        {
            case OperandKind.ShortInlineI or OperandKind.InlineI or OperandKind.InlineI8
                or OperandKind.ShortInlineR or OperandKind.InlineR:
                value = instruction.OperandValue;
                return 1;
            case OperandKind.ShortInlineVar or OperandKind.InlineVar when !variableLayout:
                value = instruction.OperandValue;
                return 2;
            default:
                value = 0;
                return 0;
        }
    }

    /// <summary>
    /// BranchTargetsMatch under the identity alignment, with the target index found by binary
    /// search (offsets strictly increase) rather than a linear scan.
    /// </summary>
    static bool BranchTargetsAtSameIndex(
        ImmutableArray<DecodedInstruction> oldInstructions,
        DecodedInstruction o,
        ImmutableArray<DecodedInstruction> newInstructions,
        DecodedInstruction n)
    {
        if (o.BranchTargets.Length != n.BranchTargets.Length)
            return false;
        for (int t = 0; t < o.BranchTargets.Length; t++)
        {
            int oi = IndexAt(oldInstructions, o.BranchTargets[t]);
            int ni = IndexAt(newInstructions, n.BranchTargets[t]);
            if (oi < 0 || ni < 0)
            {
                if (o.BranchTargets[t] != n.BranchTargets[t])
                    return false;
                // BranchTargetsMatch returns on the first unresolved target.
                return true;
            }
            if (oi != ni)
                return false;
        }
        return true;
    }

    static int IndexAt(ImmutableArray<DecodedInstruction> instructions, int offset)
    {
        int lo = 0, hi = instructions.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int at = instructions[mid].Offset;
            if (at == offset) return mid;
            if (at < offset) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }

    readonly Dictionary<(OperandKind, long), IlOperandIdentity> _oldMemo = [];
    readonly Dictionary<(OperandKind, long), IlOperandIdentity> _newMemo = [];

    bool ToOperation(
        DecodedInstruction instruction,
        IlBodyDiff.MetadataOperandResolver resolver,
        Dictionary<(OperandKind, long), IlOperandIdentity>? memo,
        out CanonicalIlOperation operation,
        out string? failure)
    {
        if (memo is null || !IlBodyDiff.SpikeIsTokenOperand(instruction.Operand))
            return IlBodyDiff.SpikeToOperation(instruction, resolver, _normalization, out operation, out failure);
        var key = (instruction.Operand, instruction.OperandValue);
        if (!memo.TryGetValue(key, out var operand))
        {
            long resolveStart = Instrument ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            bool ok = resolver.TryResolve(instruction, out var resolved, out failure);
            if (Instrument)
            {
                ResolveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - resolveStart;
                ResolveMisses++;
            }
            if (!ok)
            {
                operation = default!;
                return false;
            }
            operand = resolved!;
            memo[key] = operand;
        }
        operation = new CanonicalIlOperation(instruction.Offset, IlBodyDiff.SpikeOpcodeFamily(instruction, _normalization), operand);
        failure = null;
        return true;
    }

    IlExistsOutcome CompareDecoded(
        ImmutableArray<DecodedInstruction> oldInstructions,
        ImmutableArray<DecodedInstruction> newInstructions,
        bool tokensOnly,
        bool memoTokens,
        out string? failure)
    {
        failure = null;
        if (oldInstructions.Length != newInstructions.Length)
            return IlExistsOutcome.Changed;

        var identity = new IdentityMap(oldInstructions.Length);
        for (int i = 0; i < oldInstructions.Length; i++)
        {
            var oldInstruction = oldInstructions[i];
            var newInstruction = newInstructions[i];
            if (tokensOnly && !IlBodyDiff.SpikeIsTokenOperand(oldInstruction.Operand))
                continue;

            InstructionsCompared++;
            if (!ToOperation(oldInstruction, _old, memoTokens ? _oldMemo : null, out var oldOperation, out failure)
                || !ToOperation(newInstruction, _new, memoTokens ? _newMemo : null, out var newOperation, out failure))
            {
                return IlExistsOutcome.Unavailable;
            }
            if (IlBodyDiff.SpikeIsTokenOperand(oldInstruction.Operand))
                TokensResolved += 2;
            if (!IlBodyDiff.SpikeCanonicalEquals(oldOperation, newOperation))
                return IlExistsOutcome.Changed;
            if (!tokensOnly
                && !IlBodyDiff.BranchTargetsMatch(oldInstructions, i, newInstructions, i, identity))
            {
                return IlExistsOutcome.Changed;
            }
        }

        return IlExistsOutcome.Exact;
    }

    sealed class IdentityMap(int count) : IReadOnlyDictionary<int, int>
    {
        public int this[int key] => TryGetValue(key, out int value) ? value : throw new KeyNotFoundException();
        public IEnumerable<int> Keys => Enumerable.Range(0, count);
        public IEnumerable<int> Values => Keys;
        public int Count => count;
        public bool ContainsKey(int key) => (uint)key < (uint)count;
        public bool TryGetValue(int key, [MaybeNullWhen(false)] out int value)
        {
            value = key;
            return ContainsKey(key);
        }
        public IEnumerator<KeyValuePair<int, int>> GetEnumerator()
            => Keys.Select(key => KeyValuePair.Create(key, key)).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

public static partial class IlBodyDiff
{
    internal static MetadataOperandResolver CreateSpikeResolver(MetadataReader reader, IlBodyDiffNormalization normalization)
        => new(reader, normalization, CompilerGeneratedOrdinalCorrespondence.Empty);

    internal static bool SpikeToOperation(
        DecodedInstruction instruction,
        MetadataOperandResolver resolver,
        IlBodyDiffNormalization normalization,
        out CanonicalIlOperation operation,
        out string? failure)
        => TryToOperation(instruction, resolver, normalization, out operation, out failure);

    internal static bool SpikeCanonicalEquals(CanonicalIlOperation oldOperation, CanonicalIlOperation newOperation)
        => CanonicalEquals(oldOperation, newOperation);

    internal static string SpikeOpcodeFamily(DecodedInstruction instruction, IlBodyDiffNormalization normalization)
        => OpcodeFamily(instruction, normalization);

    internal static bool SpikeIsTokenOperand(OperandKind kind)
        => IsMetadataTokenOperand(kind);
}
