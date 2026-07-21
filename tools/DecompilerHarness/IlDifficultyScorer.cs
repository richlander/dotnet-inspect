using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// A per-method IL difficulty profile plus a single composite <see cref="Score"/>
/// used to rank the "diabolical" methods for the EVIL corpus. Every component
/// is a structural fact read off the shared <see cref="MethodInstructions"/>
/// substrate (decode + EH-aware block graph) — no inspected-assembly loading, no
/// abstract interpretation. Ranking, not the absolute scale, is what matters; the
/// components are retained so a selection pass can re-weight or filter on any
/// single axis.
/// </summary>
internal sealed record IlDifficulty(
    int IlSize,
    int BlockCount,
    int BranchCount,
    int SwitchCount,
    int ExceptionRegionCount,
    int ExceptionNestingDepth,
    int RareOpcodeCount,
    int LocalCount,
    int MaxStack,
    double Score)
{
    public static readonly IlDifficulty Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0.0);
}

/// <summary>
/// Scores a method body's IL difficulty from the shared instruction substrate.
/// The composite weighting deliberately biases toward the shapes that stress the
/// decompiler's raise the most — exception-handler nesting, jump tables, and rare
/// unsafe/typed-reference lowerings — rather than raw size, so the EVIL corpus
/// fills with genuinely hard puzzles instead of merely long trivial methods.
/// </summary>
static class IlDifficultyScorer
{
    // Genuinely rare / hard-to-raise opcodes: unsafe block memory, stack
    // allocation, function pointers, typed references, varargs, and other
    // low-frequency shapes that carry unusual lowerings. Common opcodes with
    // well-understood raises (calls, field access, arithmetic, ordinary
    // branches, box/unbox, generic constrained calls) are intentionally absent.
    static readonly ImmutableHashSet<ILOpCode> RareOpcodes = ImmutableHashSet.Create(
        ILOpCode.Calli,
        ILOpCode.Cpblk,
        ILOpCode.Initblk,
        ILOpCode.Localloc,
        ILOpCode.Sizeof,
        ILOpCode.Mkrefany,
        ILOpCode.Refanyval,
        ILOpCode.Refanytype,
        ILOpCode.Arglist,
        ILOpCode.Ckfinite,
        ILOpCode.Jmp,
        ILOpCode.Cpobj,
        ILOpCode.Endfilter,
        ILOpCode.Unaligned,
        ILOpCode.Tail);

    /// <summary>
    /// Computes the difficulty profile for a decoded method body. When the body
    /// could not be decoded, the size/local/stack scalars are still recorded and
    /// the control-flow components stay zero, so a decode failure never inflates
    /// a method's rank.
    /// </summary>
    public static IlDifficulty Score(MethodInstructions decoded, int ilSize, int localCount, int maxStack)
    {
        // A body that did not fully decode yields unreliable control-flow facts
        // (empty or partial blocks and instructions). Scoring it on raw size
        // alone would float undecodable junk to the top of the EVIL ranking,
        // so it is recorded with its true size/local/stack scalars but a zero
        // score to sink it — a decode failure must never inflate a rank.
        if (!decoded.IsComplete)
            return new IlDifficulty(ilSize, 0, 0, 0, 0, 0, 0, localCount, maxStack, 0.0);

        int branchCount = 0;
        int switchCount = 0;
        int rareOpcodeCount = 0;
        foreach (var instruction in decoded.Instructions)
        {
            if (instruction.Branches)
                branchCount++;
            if (instruction.OpCode == ILOpCode.Switch)
                switchCount++;
            if (RareOpcodes.Contains(instruction.OpCode))
                rareOpcodeCount++;
        }

        int blockCount = decoded.Blocks.Blocks.Length;
        int ehRegionCount = decoded.Blocks.Regions.Length;
        int ehNestingDepth = ExceptionNestingDepth(decoded.Blocks.Regions);

        // Weighting rationale (ranking-only; absolute magnitude is irrelevant).
        // The size-like axes (IL bytes, block count, branch count, locals) grow
        // without bound on large methods, so they are damped with a square root:
        // a 6 KB dispatcher should rank high, but its size must not swamp a small,
        // genuinely diabolical body. The per-feature structural signals the raise
        // most often fails on stay linear so they can dominate:
        //   - EH nesting depth (nested try/catch/finally/filter) — highest weight.
        //   - Rare unsafe / typed-reference / function-pointer opcodes.
        //   - Switch tables and the raw count of EH regions.
        double score =
              Math.Sqrt(ilSize) * 0.6
            + Math.Sqrt(blockCount) * 2.0
            + Math.Sqrt(branchCount) * 2.0
            + switchCount * 5.0
            + ehRegionCount * 4.0
            + ehNestingDepth * 10.0
            + rareOpcodeCount * 6.0
            + Math.Sqrt(localCount) * 0.5
            + maxStack * 0.3;

        return new IlDifficulty(
            ilSize,
            blockCount,
            branchCount,
            switchCount,
            ehRegionCount,
            ehNestingDepth,
            rareOpcodeCount,
            localCount,
            maxStack,
            Math.Round(score, 1));
    }

    /// <summary>
    /// Maximum exception-handler nesting depth: the longest chain of protected
    /// <c>try</c> blocks where each is wholly contained inside another region's
    /// try, handler, or filter span. Nesting is measured over <em>distinct</em>
    /// try spans, so the multiple sibling handlers a single <c>try</c> emits
    /// (each a region sharing the same try span) collapse to one level. A flat
    /// method with no EH scores 0; a single try/catch scores 1; a try nested
    /// inside another try/handler/filter scores 2, and so on. A
    /// <c>try/catch/finally</c> nested inside a <c>try/catch/finally</c> scores 4
    /// because the compiler emits the <c>finally</c> as an outer region whose try
    /// span also protects its sibling <c>catch</c>.
    /// </summary>
    static int ExceptionNestingDepth(ImmutableArray<ExceptionRegionModel> regions)
    {
        if (regions.Length == 0)
            return 0;

        // Collapse sibling handlers: multiple catch/filter clauses on one try
        // each emit a region with an identical try span, but together they are a
        // single level of protection, so nesting is counted over distinct try
        // spans only.
        var tryScopes = new HashSet<(int Start, int End)>();
        foreach (var region in regions)
            tryScopes.Add((region.TryStart, region.TryEnd));

        // A try can also be nested inside a handler body or a filter block. These
        // spans are distinct per region, so no deduplication is needed; the
        // filter span exists only for filter-kind regions.
        var enclosingScopes = new HashSet<(int Start, int End)>();
        foreach (var region in regions)
        {
            enclosingScopes.Add((region.HandlerStart, region.HandlerEnd));
            if (region.Kind == HandlerKind.Filter)
                enclosingScopes.Add((region.FilterStart, region.FilterEnd));
        }

        int maxDepth = 0;
        foreach (var inner in tryScopes)
        {
            int depth = 1;
            foreach (var outer in tryScopes)
            {
                if (outer.Start == inner.Start && outer.End == inner.End)
                    continue;

                // Distinct spans: containment here is necessarily strict.
                if (outer.Start <= inner.Start && inner.End <= outer.End)
                    depth++;
            }

            foreach (var scope in enclosingScopes)
            {
                if (scope.Start <= inner.Start && inner.End <= scope.End
                    && (scope.Start < inner.Start || inner.End < scope.End))
                    depth++;
            }

            if (depth > maxDepth)
                maxDepth = depth;
        }

        return maxDepth;
    }
}
