using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Findings;

namespace ILInspector.Instructions.Tests;

/// <summary>
/// Pilot gates for the finding spine (#2564): the domain-free alignment engine plus the IL
/// adapter. The engine's committed core is an LCS (so it reproduces the existing IL sequence
/// diff on move-free inputs); the move pass recovers relocations; equivalence is a consumer fold.
/// </summary>
public class FindingPilotTests
{
    // ---- Leaf engine: synthetic occurrence streams -------------------------------------------

    static ImmutableArray<FindingOccurrence<string>> Stream(params string[] keys)
        => [.. keys.Select(k => new FindingOccurrence<string>(k))];

    static readonly FindingSubject Subject = new("test", "test");
    static readonly FindingDescriptor Descriptor = new("test.item", "item");

    static int Count(FindingMatch c, FindingEdgeKind kind)
        => c.Edges.Count(l => l.Kind == kind);

    [Fact]
    public void CommittedCore_IsAnOptimalLcs_OnManyShapes()
    {
        (string[] Old, string[] New)[] cases =
        [
            (["a", "b", "c"], ["a", "b", "c"]),
            (["a", "b", "c"], ["a", "x", "c"]),
            (["a", "b", "c", "d"], ["a", "c"]),
            (["a"], ["b", "a", "c"]),
            (["a", "b", "c", "d", "e"], ["e", "d", "c", "b", "a"]),
            ([], ["a", "b"]),
            (["a", "b"], []),
            (["a", "a", "b", "a"], ["a", "b", "a", "a"]),
        ];

        foreach (var (old, @new) in cases)
        {
            var match = FindingMatcher.Match(Stream(old).ToKeys(), Stream(@new).ToKeys());
            var matched = match.Edges.Where(l => l.Kind == FindingEdgeKind.Matched).ToArray();

            // Optimality: as long as a classic LCS.
            Assert.Equal(ReferenceLcsLength(old, @new), matched.Length);

            // It is a real common subsequence: strictly increasing on both sides, keys agree.
            for (int i = 1; i < matched.Length; i++)
            {
                Assert.True(matched[i].OldIndex > matched[i - 1].OldIndex);
                Assert.True(matched[i].NewIndex > matched[i - 1].NewIndex);
            }

            foreach (var edge in matched)
                Assert.Equal(old[edge.OldIndex], @new[edge.NewIndex]);
        }
    }

    [Fact]
    public void MovePass_RelocatedBlock_IsDetectedAsMoved()
    {
        // The [m1,m2] block moves ahead of the stable A,B,C spine; the longer spine stays put
        // via LCS and the block surfaces as a contiguous residual run -> a committed move.
        var old = Stream("A", "B", "C", "m1", "m2", "D", "E");
        var @new = Stream("m1", "m2", "A", "B", "C", "D", "E");

        var match = FindingMatcher.Match(old.ToKeys(), @new.ToKeys());

        Assert.Equal(2, Count(match, FindingEdgeKind.Moved));
        Assert.Equal(0, Count(match, FindingEdgeKind.Added));
        Assert.Equal(0, Count(match, FindingEdgeKind.Removed));
    }

    [Fact]
    public void MismatchResistance_CoincidentalSingleton_StaysAddedRemoved()
    {
        // A lone relocated occurrence (run length 1) is NOT silently called a move by default.
        var old = Stream("c0", "c1", "c2");
        var @new = Stream("c1", "c2", "c0");

        var match = FindingMatcher.Match(old.ToKeys(), @new.ToKeys());

        Assert.Equal(0, Count(match, FindingEdgeKind.Moved));
        Assert.Equal(1, Count(match, FindingEdgeKind.Added));
        Assert.Equal(1, Count(match, FindingEdgeKind.Removed));
        var candidate = Assert.Single(match.MoveCandidates);
        Assert.Equal(50, candidate.Confidence);
    }

    [Fact]
    public void Acceptance_IsAConsumerFold_LoweringThresholdPromotesFringeToMove()
    {
        var old = Stream("c0", "c1", "c2");
        var @new = Stream("c1", "c2", "c0");
        var match = FindingMatcher.Match(old.ToKeys(), @new.ToKeys());

        var strict = FindingFold.ToRows(match, old, @new, Subject, Descriptor);
        Assert.Equal(0, strict.Count(r => r.DifferenceKind == FindingDifferenceKind.Moved));
        Assert.Equal(1, strict.Count(r => r.Kind == FindingKind.Added));
        Assert.Equal(1, strict.Count(r => r.Kind == FindingKind.Removed));

        var recall = FindingFold.ToRows(match, old, @new, Subject, Descriptor, acceptanceThreshold: 50);
        Assert.Equal(1, recall.Count(r => r.DifferenceKind == FindingDifferenceKind.Moved));
        Assert.Equal(0, recall.Count(r => r.Kind == FindingKind.Added));
        Assert.Equal(0, recall.Count(r => r.Kind == FindingKind.Removed));
    }

    [Fact]
    public void ScopeCorroboration_RaisesFringeScore()
    {
        var old = ImmutableArray.Create(
            new FindingOccurrence<string>("c0", ScopeKey: "loop1"),
            new FindingOccurrence<string>("c1"),
            new FindingOccurrence<string>("c2"));
        var @new = ImmutableArray.Create(
            new FindingOccurrence<string>("c1"),
            new FindingOccurrence<string>("c2"),
            new FindingOccurrence<string>("c0", ScopeKey: "loop1"));

        var match = FindingMatcher.Match(old.ToKeys(), @new.ToKeys());
        var candidate = Assert.Single(match.MoveCandidates);
        Assert.Equal(75, candidate.Confidence);
        Assert.Equal("content+scope", candidate.Reason);
    }

    [Fact]
    public void EquivalenceFolds_MoveIsExactDiffButMultisetEquivalent()
    {
        var old = Stream("A", "B", "C", "m1", "m2", "D", "E");
        var @new = Stream("m1", "m2", "A", "B", "C", "D", "E");
        var match = FindingMatcher.Match(old.ToKeys(), @new.ToKeys());
        var rows = FindingFold.ToRows(match, old, @new, Subject, Descriptor);

        // Order is semantic under the fidelity fold: a reorder is a real difference.
        Assert.False(FindingEquivalence.Exact.IsEquivalent(rows));
        // The multiset of operations is unchanged: a reorder is forgiven.
        Assert.True(FindingEquivalence.Multiset.IsEquivalent(rows));
    }

    [Fact]
    public void IdenticalStreams_AreExact()
    {
        var stream = Stream("a", "b", "c", "d");
        var match = FindingMatcher.Match(stream.ToKeys(), stream.ToKeys());
        var rows = FindingFold.ToRows(match, stream, stream, Subject, Descriptor);

        Assert.All(rows, r => Assert.Equal(FindingKind.Present, r.Kind));
        Assert.True(FindingEquivalence.Exact.IsEquivalent(rows));
    }

    [Fact]
    public void MovePass_NonContiguousSingletons_AreNotAFalseBlockMove()
    {
        // Two isolated relocations that become residual-adjacent after LCS removes the A,B anchors
        // must NOT be committed as a length-2 block move: they were not contiguous in either stream.
        var old = Stream("M1", "A", "M2", "B");
        var @new = Stream("A", "B", "M1", "M2");

        var match = FindingMatcher.Match(old.ToKeys(), @new.ToKeys());

        Assert.Equal(0, Count(match, FindingEdgeKind.Moved));
        Assert.Equal(2, Count(match, FindingEdgeKind.Added));
        Assert.Equal(2, Count(match, FindingEdgeKind.Removed));
    }

    [Fact]
    public void Fringe_EmitsFullCandidateGraph_NotIndexOrderThinned()
    {
        // The residual has one old "A" and two new "A"s. The fringe must offer BOTH candidate
        // pairings (so ApplyAcceptance can resolve the matching by score), not just the first.
        var old = Stream("A", "B");
        var @new = Stream("B", "A", "A");

        var match = FindingMatcher.Match(old.ToKeys(), @new.ToKeys());

        Assert.Equal(2, match.MoveCandidates.Length);
        Assert.All(match.MoveCandidates, c => Assert.Equal("A", old[c.OldIndex].IdentityKey));
        Assert.All(match.MoveCandidates, c => Assert.Equal("A", @new[c.NewIndex].IdentityKey));
    }

    [Fact]
    public void Match_IsDeterministic_UnderAmbiguity()
    {
        var old = Stream("x", "a", "y", "a", "z", "a");
        var @new = Stream("a", "z", "a", "x", "a", "y");

        var first = FindingMatcher.Match(old.ToKeys(), @new.ToKeys());
        var second = FindingMatcher.Match(old.ToKeys(), @new.ToKeys());

        Assert.Equal(first.Edges, second.Edges);
        Assert.Equal(first.MoveCandidates, second.MoveCandidates);
    }

    [Fact]
    public void Match_RejectsStreamsTooLargeForOrderedMatrix()
    {
        // The ordered LCS matrix is O(N*M); guard it so an assembly-scale stream fails loudly
        // instead of attempting a multi-gigabyte allocation (issue #2585).
        var big = Enumerable.Range(0, 8000)
            .Select(i => new FindingOccurrence<string>(i.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();

        var ex = Assert.Throws<ArgumentException>(() => FindingMatcher.Match(big.ToKeys(), big.ToKeys()));
        Assert.Contains("identity-set", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SkeletonConsumer_ClassifiesFromPolarityAndDifferenceAlone()
    {
        // A stream-agnostic consumer: it reads only the skeleton, so the same rows could have
        // come from IL, C#, or a fact producer. Here we drive it from the IL adapter.
        var identical = Body(Ldc0, Ldc1, Ret);
        var identicalRows = IlFindings.Compare(identical, null, identical, null, IlSubject).Rows;
        Assert.Equal(DiffShape.Identical, FindingSummary.Summarize(identicalRows).Shape);

        var old = Body(Ldc0, Ldc1, Ldc2, Ldc3, Ldc4, Ldc5, Ldc6, Ret);
        var reordered = Body(Ldc3, Ldc4, Ldc0, Ldc1, Ldc2, Ldc5, Ldc6, Ret);
        var reorderRows = IlFindings.Compare(old, null, reordered, null, IlSubject).Rows;
        var reorderSummary = FindingSummary.Summarize(reorderRows);
        Assert.Equal(DiffShape.ReorderOnly, reorderSummary.Shape);
        Assert.Equal(2, reorderSummary.Moved);

        var changed = Body(Ldc0, Ldc2, Ldc3, Ret);
        var changedRows = IlFindings.Compare(old, null, changed, null, IlSubject).Rows;
        Assert.Equal(DiffShape.Structural, FindingSummary.Summarize(changedRows).Shape);
    }

    [Fact]
    public void SkeletonConsumer_IsStreamAgnostic_OverHandBuiltRows()
    {
        // Rows fabricated as if from an arbitrary (non-IL) stream: the consumer does not care.
        var subject = new FindingSubject("s", "s");
        var descriptor = new FindingDescriptor("any.kind", "any");
        Finding<string>[] rows =
        [
            new(subject, descriptor, FindingKind.Present, new FindingAnchor("k0", 0, 0)),
            new(subject, descriptor, FindingKind.Added, new FindingAnchor("k1", -1, 1)),
        ];

        var summary = FindingSummary.Summarize(rows);
        Assert.Equal(DiffShape.Structural, summary.Shape);
        Assert.Equal(1, summary.Added);
        Assert.Equal(1, summary.Present);
    }

    static int ReferenceLcsLength(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var dp = new int[a.Count + 1, b.Count + 1];
        for (int i = a.Count - 1; i >= 0; i--)
        {
            for (int j = b.Count - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        }

        return dp[0, 0];
    }

    // ---- IL adapter: real decode + raw-byte fixtures -----------------------------------------

    static MethodInstructions Body(params byte[] il)
        => MethodInstructions.Decode(il, il.Length, Array.Empty<ExceptionRegion>());

    // Single-byte, operand-free opcodes so a body is a clean sequence of distinct content keys.
    const byte Ldc0 = 0x16, Ldc1 = 0x17, Ldc2 = 0x18, Ldc3 = 0x19, Ldc4 = 0x1A, Ldc5 = 0x1B, Ldc6 = 0x1C, Ret = 0x2A;
    const byte BrS = 0x2B, Nop = 0x00;

    static readonly FindingSubject IlSubject = new("M", "M");

    [Fact]
    public void IlFindings_PathologicallyLargeBody_FailsClosed()
    {
        // A body large enough to exceed the ordered matcher's cell guard must fail closed
        // (return a failure result), not throw at the caller — consistent with the decode path.
        var il = new byte[9000];
        il[^1] = Ret; // 8999 nops + ret
        var body = Body(il);

        var result = IlFindings.Compare(body, null, body, null, IlSubject);

        Assert.NotNull(result.Failure);
        Assert.False(result.IsExact);
    }

    [Fact]
    public void IlFindings_BranchRetarget_IsChangedNotExact()
    {
        // br.s to IL_0005 (second ret) vs br.s to IL_0003 (first ret): a real control-flow retarget.
        // The branch operation's content key ignores its target, so without target validation this
        // would read as exact. It must surface as Changed and agree with IlBodyDiff's non-exactness.
        var old = Body(BrS, 0x03, Nop, Ret, Nop, Ret);
        var @new = Body(BrS, 0x01, Nop, Ret, Nop, Ret);

        var result = IlFindings.Compare(old, null, @new, null, IlSubject);

        Assert.Null(result.Failure);
        Assert.False(result.IsExact);
        Assert.Contains(result.Rows, r => r.Kind == FindingKind.Changed);
        Assert.False(IlBodyDiff.Compare(old, @new).IsExact);
    }

    [Fact]
    public void IlFindings_BranchInsideMovedBlock_IsReorderNotRetarget()
    {
        // The [ldc.i4.0, br.s] block (branch targets the ldc just before it) relocates ahead of the
        // NOP spine. The branch is byte-identical and still targets its (moved) ldc, so it must read
        // as a move, not a retarget. Requires overlaying the finding Moved mappings onto the
        // IlBodyDiff alignment map (which is move-blind).
        var old = Body(Nop, Nop, Nop, Ldc0, BrS, 0xFD, Ret);
        var @new = Body(Ldc0, BrS, 0xFD, Nop, Nop, Nop, Ret);

        var result = IlFindings.Compare(old, null, @new, null, IlSubject);

        Assert.Null(result.Failure);
        Assert.DoesNotContain(result.Rows, r => r.Kind == FindingKind.Changed);
        Assert.Equal(2, result.Rows.Count(r => r.DifferenceKind == FindingDifferenceKind.Moved));
        Assert.True(FindingEquivalence.Multiset.IsEquivalent(result.Rows));
    }

    [Fact]
    public void IlFindings_Exactness_AgreesWithIlBodyDiff_AcrossFixtures()
    {
        (byte[] Old, byte[] New)[] pairs =
        [
            ([Ldc0, Ldc1, Ret], [Ldc0, Ldc1, Ret]),               // identical -> both exact
            ([Ldc0, Ldc1, Ret], [Ldc0, Ldc2, Ret]),               // content change -> both non-exact
            ([BrS, 0x03, Nop, Ret, Nop, Ret], [BrS, 0x01, Nop, Ret, Nop, Ret]), // retarget -> both non-exact
            ([BrS, 0x00, Ldc0, Ret], [BrS, 0x00, Ldc1, Ret]),     // target content change, slot stable
            ([Ldc0, Ldc1, Ldc2, Ret], [Ldc0, Ldc2, Ret]),         // deletion -> both non-exact
        ];

        foreach (var (oldBytes, newBytes) in pairs)
        {
            var old = Body(oldBytes);
            var @new = Body(newBytes);
            bool findingExact = IlFindings.Compare(old, null, @new, null, IlSubject).IsExact;
            bool classicExact = IlBodyDiff.Compare(old, @new).IsExact;
            Assert.Equal(classicExact, findingExact);
        }
    }

    [Fact]
    public void IlFindings_BranchTargetContentChangedButSlotStable_BranchIsNotRetargeted()
    {
        // br.s targets the instruction right after it; that instruction's content changes
        // (ldc.i4.0 -> ldc.i4.1) but stays in the same slot. IlBodyDiff's equal-size gap-pair
        // alignment forgives the branch; the adapter must reuse that map and NOT flag the branch.
        var old = Body(BrS, 0x00, Ldc0, Ret);
        var @new = Body(BrS, 0x00, Ldc1, Ret);

        var result = IlFindings.Compare(old, null, @new, null, IlSubject);

        Assert.Null(result.Failure);
        Assert.DoesNotContain(result.Rows, r => r.Kind == FindingKind.Changed);
        Assert.Equal(1, result.Rows.Count(r => r.Kind == FindingKind.Added));
        Assert.Equal(1, result.Rows.Count(r => r.Kind == FindingKind.Removed));
    }

    [Fact]
    public void IlFindings_SameBranch_IsExact()
    {
        // Branch validation must not false-positive: an identical body with a branch is exact.
        var body = Body(BrS, 0x01, Nop, Ret, Nop, Ret);
        var result = IlFindings.Compare(body, null, body, null, IlSubject);

        Assert.Null(result.Failure);
        Assert.True(result.IsExact);
    }

    [Fact]
    public void IlFindings_SameBody_IsExact()
    {
        var body = Body(Ldc0, Ldc1, Ldc2, Ret);
        var result = IlFindings.Compare(body, null, body, null, IlSubject);

        Assert.Null(result.Failure);
        Assert.True(result.IsExact);
        Assert.All(result.Rows, r => Assert.Equal(FindingKind.Present, r.Kind));
    }

    [Fact]
    public void IlFindings_RelocatedBlock_IsDetectedAsMoved()
    {
        // old: c0 c1 c2 c3 c4 c5 c6 ret ; new moves the [c3,c4] block to the front.
        var old = Body(Ldc0, Ldc1, Ldc2, Ldc3, Ldc4, Ldc5, Ldc6, Ret);
        var @new = Body(Ldc3, Ldc4, Ldc0, Ldc1, Ldc2, Ldc5, Ldc6, Ret);

        var result = IlFindings.Compare(old, null, @new, null, IlSubject);

        Assert.Null(result.Failure);
        Assert.Equal(2, result.Rows.Count(r => r.DifferenceKind == FindingDifferenceKind.Moved));
        Assert.Equal(0, result.Rows.Count(r => r.Kind is FindingKind.Added or FindingKind.Removed));
        Assert.False(result.IsExact);
        Assert.True(FindingEquivalence.Multiset.IsEquivalent(result.Rows));
    }

    [Fact]
    public void IlFindings_CommittedCore_ReproducesIlBodyDiffResidual()
    {
        // A body pair with no moves: pure insert/delete. The finding adapter's Added/Removed
        // operations must match IlBodyDiff's Add/Remove rows (the committed core is the LCS).
        var old = Body(Ldc0, Ldc1, Ldc2, Ret);
        var @new = Body(Ldc0, Ldc2, Ldc3, Ret);

        var classic = IlBodyDiff.Compare(old, @new);
        var finding = IlFindings.Compare(old, null, @new, null, IlSubject);

        Assert.Equal(0, finding.Rows.Count(r => r.DifferenceKind == FindingDifferenceKind.Moved));

        var classicRemoved = classic.Rows
            .Where(r => r.Kind == IlDiffKind.Remove)
            .Select(r => IlFindings.GetIdentityKey(r.Operation))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        var findingRemoved = ResidualKeys(finding, FindingKind.Removed);
        Assert.Equal(classicRemoved, findingRemoved);

        var classicAdded = classic.Rows
            .Where(r => r.Kind == IlDiffKind.Add)
            .Select(r => IlFindings.GetIdentityKey(r.Operation))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        var findingAdded = ResidualKeys(finding, FindingKind.Added);
        Assert.Equal(classicAdded, findingAdded);
    }

    [Fact]
    public void IlFindings_MismatchResistance_SingletonStaysAddedRemoved()
    {
        var old = Body(Ldc0, Ldc1, Ldc2, Ret);
        var @new = Body(Ldc1, Ldc2, Ldc0, Ret);

        var result = IlFindings.Compare(old, null, @new, null, IlSubject);

        Assert.Equal(0, result.Rows.Count(r => r.DifferenceKind == FindingDifferenceKind.Moved));
        Assert.Equal(1, result.Rows.Count(r => r.Kind == FindingKind.Added));
        Assert.Equal(1, result.Rows.Count(r => r.Kind == FindingKind.Removed));
    }

    static string[] ResidualKeys(IlFindingsResult result, FindingKind polarity)
        => result.Rows
            .Where(r => r.Kind == polarity)
            .Select(r => r.Anchor.IdentityKey)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

    // ---- IL adapter: real-assembly corpus ----------------------------------------------------

    [Theory]
    [InlineData(typeof(IlBodyDiff))]                    // ILInspector.Instructions: diverse real IL.
    [InlineData(typeof(FindingPilotTests))]            // the test assembly itself.
    [InlineData(typeof(System.Linq.Enumerable))]        // System.Linq: a larger BCL corpus.
    public void IlFindings_SelfCompare_IsExactAcrossWholeAssembly(Type anchorType)
    {
        using var stream = File.OpenRead(anchorType.Assembly.Location);
        using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
        var reader = pe.GetMetadataReader();

        int canonicalized = 0;
        int declined = 0;
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue; // abstract/extern/pinvoke: no body.

            MethodInstructions body;
            try
            {
                body = MethodInstructions.Decode(pe.GetMethodBody(method.RelativeVirtualAddress));
            }
            catch (System.BadImageFormatException)
            {
                continue;
            }

            if (!body.IsComplete)
                continue;

            var subject = new FindingSubject(handle.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture), "m");
            var finding = IlFindings.Compare(body, reader, body, reader, subject);
            if (finding.Failure is not null)
            {
                declined++; // rare token-resolution edge cases; tracked, not asserted.
                continue;
            }

            // A body compared to itself must be exact: all Present, no moves, no add/remove.
            Assert.True(finding.IsExact, $"IlFindings self-compare not exact for RVA {method.RelativeVirtualAddress}");
            canonicalized++;
        }

        // The adapter canonicalizes the overwhelming majority of real IL (a real reader resolves
        // tokens): declines must be a small tail, not the common case.
        Assert.True(canonicalized > 50, $"expected a substantial corpus; canonicalized={canonicalized} declined={declined}");
        Assert.True(declined <= canonicalized / 10, $"too many canonicalization declines: canonicalized={canonicalized} declined={declined}");
    }
}
