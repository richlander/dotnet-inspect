using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpText.Tests;

public partial class DeclarationIndexTests
{
    /// <summary>
    /// Which declaration an initializer extends is itself branch-dependent when a conditional
    /// group sits between the accessor block and the <c>=</c>. Roslyn reports <c>P</c> as
    /// lines 2-2 with <c>X</c> and 2-6 without, both configurations parsing with zero errors,
    /// so the row before the group cannot be vouched for. Every other conditional rule asks
    /// whether a header written BEFORE a group survives it; this is a token written after the
    /// group reaching back through it. Found by adversarial review round 7 (Gemini 3.1 Pro).
    /// </summary>
    [Fact]
    public void AnInitializerReachingBackThroughAGroup_LosesTheDeclarationBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C {
                public int P { get; }
            #if X
                public int Q { get; }
            #endif
                = 1;
            }
            """);

        var p = Assert.Single(index.Declarations, s => s.Name == "P");
        Assert.False(p.SpanKnown, "without X the initializer belongs to P, which then ends on line 6");
    }

    /// <summary>
    /// An <c>#elif</c> chain offers more than one alternative target, so the refusal has to take
    /// the whole preceding sibling run rather than only the nearest one: the initializer belongs
    /// to <c>Q</c> with <c>X</c>, to <c>R</c> with <c>Y</c>, and to <c>P</c> with neither.
    /// </summary>
    [Fact]
    public void AnInitializerReachingBackThroughAnElifChain_LosesEverySiblingItCouldBindTo()
    {
        var index = DeclarationIndex.Build("""
            class C {
                public int P { get; }
            #if X
                public int Q { get; }
            #elif Y
                public int R { get; }
            #endif
                = 1;
            }
            """);

        Assert.All(
            index.Declarations.Where(s => s.Name is "P" or "Q" or "R"),
            s => Assert.False(s.SpanKnown, $"{s.Name} is a possible target of the line-8 initializer"));
    }

    /// <summary>
    /// The guard against over-refusing: an initializer that shares its branch with the block it
    /// closes reaches back through nothing, so the siblings around it keep their vouch. Without
    /// this the ninth-way rule would condemn every declaration preceding any conditional
    /// initializer in a file.
    /// </summary>
    [Fact]
    public void AnInitializerSharingItsBranch_StillVouchesForTheDeclarationBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C {
                public int A { get; } = 1;
            #if X
                public int Q { get; } = 2;
            #endif
            }
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.True(a.SpanKnown, "the line-4 initializer never reaches past its own branch");
    }

    /// <summary>
    /// The tenth way. A branch can carry a complete declaration of its own, whose <c>=</c> is an
    /// ordinary same-section initializer, while the branch beside it carries a bare tail that binds
    /// to the row ABOVE the group. Roslyn ends <c>P</c> at line 3 with <c>X</c> and at line 9
    /// without it, both with zero errors, so no single span is right.
    ///
    /// The point of this test is the FIRST <c>=</c>: it belongs to <c>Q</c> and disqualifies
    /// itself, and a search that stopped there never examined the <c>= 1</c> behind it. The
    /// builder therefore takes the first <c>=</c> that qualifies, not the first that exists.
    /// Neutralize that by restoring the <c>break</c> on the first <c>=</c> and this fails, as do
    /// all five <see cref="ConditionalRecoveryFuzzTests"/> seeds. Found by adversarial review
    /// round 8 (GPT-5.6 Sol).
    /// </summary>
    [Fact]
    public void AnInitializerMaskedByAnotherBranchsInitializer_LosesTheDeclarationBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                int P { get; }
            #if X
                int Q = 0
            #else
                = 1
            #endif
                ;
            }
            """);

        var p = Assert.Single(index.Declarations, s => s.Name == "P");
        Assert.False(p.SpanKnown, "P ends on line 3 with X and line 9 without it");
    }

    /// <summary>
    /// The mirror of the shape above, with the reaching-back tail spelled first and the branch
    /// carrying its own complete declaration second.
    ///
    /// This shape passes BEFORE the round-8 fix as well: round 7 already handled a bare tail at
    /// <c>pending[0]</c>, and each of these two sources contains exactly one QUALIFYING <c>=</c>,
    /// so taking the first or the last is indistinguishable here. It is therefore coverage of the
    /// mirror ordering, not a gate on the round-8 change -- the gate is the test above, and
    /// <see cref="ConditionalRecoveryFuzzTests"/> generates both orderings.
    /// </summary>
    [Fact]
    public void AnInitializerMaskingAnotherBranchsInitializer_LosesTheDeclarationBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                int P { get; }
            #if X
                = 1
            #else
                int Q = 0
            #endif
                ;
            }
            """);

        var p = Assert.Single(index.Declarations, s => s.Name == "P");
        Assert.False(p.SpanKnown, "P ends on line 9 with X and line 3 without it");
    }

    /// <summary>
    /// The over-refusal guard for the pair above. Both branches carry a complete declaration with
    /// its own initializer, so nothing reaches back and <c>P</c> keeps its vouch. Without this,
    /// "refuse whenever a group contains an <c>=</c>" would pass both tests above and cost the
    /// corpus every conditionally-declared field.
    /// </summary>
    [Fact]
    public void TwoBranchesWithTheirOwnInitializers_StillVouchForTheDeclarationBeforeThem()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                int P { get; }
            #if X
                int Q = 0;
            #else
                int R = 1;
            #endif
            }
            """);

        var p = Assert.Single(index.Declarations, s => s.Name == "P");
        Assert.True(p.SpanKnown, "neither branch's initializer reaches past its own section");
    }

    /// <summary>
    /// The eleventh way. An enum member's initializer reaches back exactly as a field's does, but
    /// an enum member is terminated by <c>,</c> or <c>}</c> and so never passes through the
    /// <c>;</c> path that refuses it. Roslyn ends <c>A</c> at line 2 with <c>X</c> and line 6
    /// without it, both with zero errors, and the product had already emitted and vouched
    /// <c>A</c> at the branch-local <c>,</c> before the <c>=</c> was ever read.
    ///
    /// Neutralize either <c>ReachingBackEquals</c> call in the enum paths and this fails. Found by
    /// adversarial review round 8 (Gemini 3.1 Pro).
    /// </summary>
    [Fact]
    public void AnEnumInitializerReachingBackThroughAGroup_LosesTheMemberBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            enum E {
                A
            #if X
                , B
            #endif
                = 1
            }
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.False(a.SpanKnown, "A ends on line 2 with X and line 6 without it");
    }

    /// <summary>
    /// The same reaching-back enum initializer, but terminated by a following <c>,</c> rather than
    /// by the enum's closing brace, so it pins the comma path rather than the brace path.
    /// </summary>
    [Fact]
    public void AnEnumInitializerReachingBackBeforeAnotherMember_LosesTheMemberBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            enum E {
                A
            #if X
                , B
            #endif
                = 1, C
            }
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.False(a.SpanKnown, "A absorbs the line-6 initializer when X is undefined");
    }

    /// <summary>
    /// The over-refusal guard for the two enum tests above, and the reason they test for a
    /// reaching-back <c>=</c> rather than simply for a conditional comma. Here the group carries a
    /// member and its comma but no initializer reaches back, so every build ends <c>A</c> on
    /// line 2 and the vouch stands. Refusing on the conditional comma alone would pass both tests
    /// above while costing the corpus every conditionally-extended enum.
    /// </summary>
    [Fact]
    public void AConditionalEnumMemberWithNoInitializer_StillVouchesForTheMemberBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            enum E {
                A
            #if X
                , B
            #endif
            }
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.True(a.SpanKnown, "A ends on line 2 in both builds");
    }

    /// <summary>
    /// The twelfth way, and the third direction: ways 1-8 ask whether a header written before a
    /// group survives it, ways 9-11 are a tail reaching back through one, and this is a closed and
    /// already-vouched declaration reaching FORWARD to claim a terminator written after it.
    ///
    /// A type declaration takes an optional trailing <c>;</c>. With <c>Y</c> it belongs to
    /// <c>B</c> and <c>A</c> ends at its own brace on line 6; without <c>Y</c> there is no
    /// <c>B</c>, the <c>;</c> is <c>A</c>'s own, and <c>A</c> ends on line 10. All four symbol
    /// configurations parse with zero errors.
    ///
    /// This is the one defect in the series that the PR itself introduced: before balanced-group
    /// recovery the leading group poisoned the rest of the file, so <c>A</c> was declined for an
    /// unrelated reason. Found by adversarial review round 9 (Claude Opus 5).
    /// </summary>
    [Fact]
    public void ATrailingSemicolonAfterAGroup_LosesTheTypeItCouldBelongTo()
    {
        var index = DeclarationIndex.Build("""
            #if X
            class Z { }
            #endif
            class A
            {
            }
            #if Y
            class B { }
            #endif
            ;
            class Tail { }
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.False(a.SpanKnown, "A ends on line 6 with Y and line 10 without it");
    }

    /// <summary>
    /// The same token with no conditional anywhere, which is where the defect actually lived: the
    /// scan did not model the optional trailing <c>;</c> at all, so it reported a span that was
    /// simply wrong rather than branch-dependent. Roslyn ends <c>A</c> on line 4.
    ///
    /// This one is a pre-existing wrong span rather than a wrong vouch, and it is why the
    /// conditional case above exists. It is also not a deliberate convention: every bodiless row
    /// the scan emits already includes its terminating <c>;</c>.
    /// </summary>
    [Fact]
    public void ATrailingSemicolonAfterAType_ExtendsThatTypesSpan()
    {
        var index = DeclarationIndex.Build("""
            class A
            {
            }
            ;
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.Equal(4, a.EndLine);
        Assert.True(a.SpanKnown, "no conditional is involved, so the span is provable");
    }

    /// <summary>
    /// The both-branches-symmetric form, which is worse than the asymmetric one: the scan's answer
    /// matches neither build. With <c>X</c> the <c>;</c> is on line 5 and <c>A</c> ends there;
    /// without it the <c>;</c> is on line 7. The unfixed scan reported line 3.
    /// </summary>
    [Fact]
    public void ATrailingSemicolonSpelledInBothBranches_LosesTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class A
            {
            }
            #if X
                ;
            #else
                ;
            #endif
            class Tail { }
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.False(a.SpanKnown, "A ends on line 5 with X and line 7 without it");
    }

    /// <summary>
    /// The thirteenth way, and it shields the twelfth: the trailing-<c>;</c> rule requires an
    /// empty pending run, but the scan lexes every branch, so a declaration written in a branch
    /// this build discards is still pending when the <c>;</c> arrives and hides it. With <c>X</c>
    /// the <c>;</c> terminates <c>Field</c> and <c>Sy</c> ends on line 2; without <c>X</c> there
    /// is no <c>Field</c>, so the <c>;</c> is <c>Sy</c>'s own optional trailer and <c>Sy</c> ends
    /// on line 6. Both builds parse with zero errors. Found by adversarial review round 10
    /// (Gemini 3.1 Pro).
    /// </summary>
    [Fact]
    public void ATrailingSemicolonShieldedByAConditionalMember_LosesTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C {
                class Sy { }
            #if X
                int Field = 1
            #endif
                ;
            }
            """);

        var sy = Assert.Single(index.Declarations, s => s.Name == "Sy");
        Assert.False(sy.SpanKnown, "Sy ends on line 2 with X and line 6 without it");
    }

    /// <summary>
    /// The same shield spelled with a delegate, which reaches the terminator through a different
    /// classification path than a field does. At file scope, where the enclosing type cannot be
    /// what supplies the refusal.
    /// </summary>
    [Fact]
    public void ATrailingSemicolonShieldedByAConditionalDelegate_LosesTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class Sy { }
            #if X
            delegate void D()
            #endif
            ;
            """);

        var sy = Assert.Single(index.Declarations, s => s.Name == "Sy");
        Assert.False(sy.SpanKnown, "Sy ends on line 1 with X and line 5 without it");
    }

    /// <summary>
    /// The twelfth way's refusal set was too narrow, and a brace-less scope opener is what
    /// exposed it. A file-scoped namespace inside the group re-parents the row the <c>;</c>
    /// appears to follow, so a walk over that row's siblings never visits <c>A</c> at the outer
    /// scope. Without <c>Y</c> the file is <c>class A {\n}\n;</c> — a legal program with zero
    /// errors in which <c>A</c> ends at the <c>;</c> on line 10 — and <c>A</c> was vouched at
    /// 1..3. Found by adversarial review round 10 (Claude Opus 5).
    /// </summary>
    [Fact]
    public void ATrailingSemicolonBehindAConditionalFileScopedNamespace_LosesTheTypeAtTheOuterScope()
    {
        var index = DeclarationIndex.Build("""
            class A
            {
            }
            #if Y
            namespace NS;
            class B
            {
            }
            #endif
            ;
            """);

        var a = Assert.Single(index.Declarations, s => s.Name == "A");
        Assert.False(a.SpanKnown, "A ends at the \";\" on line 10 in the build without Y");
    }

    /// <summary>
    /// The same hole one step further out, and the arm round 10 predicted but could not build a
    /// parsing case for. A file-scoped namespace ends a declaration without closing a block, so
    /// <c>lastClosed</c> is <c>-1</c> and the trailing-<c>;</c> test does not run at all. Without
    /// <c>X</c> the file is <c>class Sr { }\n;</c> and <c>Sr</c> ends on line 5; the build WITH
    /// <c>X</c> does not parse, so only one configuration is fair and the pairwise build-vs-build
    /// gate cannot see this at all — the product gate caught it. Found by the widened generator
    /// (adversarial review round 10, Claude Opus 5).
    /// </summary>
    [Fact]
    public void ATrailingSemicolonAfterAConditionalBracelessDeclaration_LosesTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class Sr { }
            #if X
            namespace Nr;
            #endif
            ;
            """);

        var sr = Assert.Single(index.Declarations, s => s.Name == "Sr");
        Assert.False(sr.SpanKnown, "Sr ends at the \";\" on line 5 in the build without X");
    }

    /// <summary>
    /// The same arm where the branch-dependent declaration leaves NO ROW AT ALL: a namespace
    /// inside a type is not an allowed row, so a test on the last row's vouch cannot see it and
    /// the rule reads the last terminator's section instead. This is the shape the widened
    /// generator produced that the file-scope form did not cover.
    /// </summary>
    [Fact]
    public void ATrailingSemicolonAfterABracelessDeclarationThatEmitsNoRow_LosesTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class Outer
            {
            class Sr { }
            #if X
            namespace Nr;
            #endif
            ;
            }
            """);

        var sr = Assert.Single(index.Declarations, s => s.Name == "Sr");
        Assert.False(sr.SpanKnown, "Sr ends at the \";\" on line 7 in the build without X");
    }

    /// <summary>
    /// The recall side of that arm, and the reason it compares sections rather than simply
    /// refusing whenever <c>lastClosed</c> is <c>-1</c>. A stray <c>;</c> written in the same
    /// branch as the terminator before it can reach nothing new, even in a file that has an
    /// unrelated group earlier in it.
    /// </summary>
    [Fact]
    public void AStraySemicolonInTheSameBranchAsItsPredecessor_KeepsItsNeighboursVouches()
    {
        var index = DeclarationIndex.Build("""
            #if X
            #endif
            class C
            {
                int Keep;
                ;
            }
            """);

        var keep = Assert.Single(index.Declarations, s => s.Name == "Keep");
        Assert.True(keep.SpanKnown, "the stray \";\" shares its predecessor's branch");
    }

    /// <summary>
    /// The recall side of the outward walk, and the reason it stops at a VOUCHED parent.
    /// <c>Outer</c>
    /// exists identically in every build, so the scope it opens exists in every build and no
    /// terminator inside it can reach a declaration outside it. <c>Before</c> must keep its vouch.
    /// </summary>
    [Fact]
    public void ARefusalInsideAVouchedScope_DoesNotEscapeToTheScopeAboveIt()
    {
        var index = DeclarationIndex.Build("""
            class Before { }
            class Outer
            {
                class A { }
            #if Y
                class B { }
            #endif
                ;
            }
            """);

        var before = Assert.Single(index.Declarations, s => s.Name == "Before");
        Assert.True(before.SpanKnown, "Outer exists in every build, so the \";\" cannot escape it");
    }

    /// <summary>
    /// The shield with the terminator in a DIFFERENT group, which is why the rule compares
    /// sections instead of asking whether the <c>;</c> itself is at a known depth. With <c>X</c>
    /// and <c>Y</c> the <c>;</c> terminates <c>F</c> and <c>Sy</c> ends on line 1; with <c>Y</c>
    /// alone there is no <c>F</c>, so the <c>;</c> is <c>Sy</c>'s trailer and <c>Sy</c> ends on
    /// line 6. Both parse cleanly. The <c>;</c> is inside a group here, so a <c>DepthKnown</c>
    /// test on it would have let this through.
    /// </summary>
    [Fact]
    public void ATrailingSemicolonShieldedFromAnotherGroup_LosesTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class Sy { }
            #if X
            int F = 1
            #endif
            #if Y
            ;
            #endif
            """);

        var sy = Assert.Single(index.Declarations, s => s.Name == "Sy");
        Assert.False(sy.SpanKnown, "Sy ends on line 1 with X and Y, and line 6 with Y alone");
    }

    /// <summary>
    /// The recall side of the same rule, and the reason it is not simply "a conditional member
    /// precedes the <c>;</c>". A declaration and its terminator written in ONE branch vanish
    /// together, so nothing can reach past them: <c>Sy</c> ends on line 1 in every build. This is
    /// the overwhelmingly common shape of a conditional member, and refusing it would cost the
    /// corpus far more than the defect it guards against.
    /// </summary>
    [Fact]
    public void AConditionalMemberCarryingItsOwnTerminator_KeepsTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class Sy { }
            #if X
            int F = 1;
            #endif
            """);

        var sy = Assert.Single(index.Declarations, s => s.Name == "Sy");
        Assert.True(sy.SpanKnown, "F and its \";\" are in one branch and vanish together");
    }

    /// <summary>
    /// The benign counterpart, and the gate on the <c>DepthKnown</c> term of the shield test.
    /// <c>Field</c>'s header is written outside the group, so it exists in every build and owns
    /// the <c>;</c> in every build; only a second declarator is conditional. <c>Sy</c> ends on
    /// line 2 in both builds and must keep its vouch. Dropping the <c>DepthKnown</c> term and
    /// leaving only the section comparison fails this test, because the header, the group and the
    /// <c>;</c> are all in different sections.
    /// </summary>
    [Fact]
    public void AConditionalDeclaratorOnAnUnconditionalMember_KeepsTheTypeBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C {
                class Sy { }
                int Field
            #if X
                    , Other
            #endif
                ;
            }
            """);

        var sy = Assert.Single(index.Declarations, s => s.Name == "Sy");
        Assert.True(sy.SpanKnown, "Field exists in every build, so the \";\" never reaches Sy");
    }

    /// <summary>
    /// A trivia poison that crosses no branch is spent once the declaration that raised it ends,
    /// and <c>ResetHeader</c> discharging it is what keeps the rest of the file vouched. With
    /// <c>X</c> the comment documents <c>s</c>; without <c>X</c> neither exists, so <c>Tail</c>
    /// starts on its own signature line in every build. This is the over-refusal side of the
    /// round-6 stickiness rule, and it is the only gate on the discharge: round 7 (Gemini 3.1
    /// Pro) showed all 410 tests passing with the assignment neutralized.
    /// </summary>
    [Fact]
    public void ATriviaPoisonCrossingNoBranch_IsDischargedByTheNextReset()
    {
        var index = DeclarationIndex.Build("""
            #if X
            // doc
            class s { }
            #endif
            class Tail { }
            """);

        var tail = Assert.Single(index.Declarations, s => s.Name == "Tail");
        Assert.True(tail.SpanKnown, "the comment is consumed inside the branch that contains it");
    }

    /// <summary>
    /// The second shape of the ninth way, and the one that walks past
    /// <c>AConditionalInitializer_ReportsUnknownRatherThanOneBranchsEnd</c> entirely: the
    /// initializer sits INSIDE the group, and the sibling branch has already consumed the row it
    /// would have extended, so the guarded path is never entered at all. Roslyn reports <c>P</c>
    /// as lines 3-3 with <c>X</c> and 3-7 without. Found independently by adversarial review
    /// round 7 (Gemini 3.1 Pro via a widened fuzzer, Claude Opus 5 as repro B).
    /// </summary>
    [Fact]
    public void AnInitializerConsumedByAnotherBranch_LosesTheDeclarationBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                int P { get; set; }
            #if X
                int Q;
            #else
                = 5;
            #endif
            }
            """);

        var p = Assert.Single(index.Declarations, s => s.Name == "P");
        Assert.False(p.SpanKnown, "without X the line-7 initializer belongs to P, which then ends there");
    }

    /// <summary>
    /// The bodiless emit path consults its terminator's depth flag, and nothing enforced it: the
    /// whole suite, differential fuzzer included, stayed green with the term deleted, because the
    /// shipped generator never placed a group inside a type body and so had never compared a
    /// field, method, property or event row at all. Roslyn reports <c>f</c> as lines 3-5 with
    /// <c>X</c> and 3-7 without. Found by adversarial review round 7 (Claude Opus 5).
    /// </summary>
    [Fact]
    public void ABodilessRowWhoseTerminatorIsInABranch_IsNotVouchedFor()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                int f
            #if X
                ;
            #else
                ;
            #endif
            }
            """);

        var f = Assert.Single(index.Declarations, s => s.Name == "f");
        Assert.False(f.SpanKnown, "which \";\" terminates the field depends on the branch");
    }

    /// <summary>
    /// The other half of the bodiless emit path: a signature token left in a branch moves the
    /// row's start, and that term was ungated for the same reason. Roslyn reports <c>f</c> as
    /// lines 4-6 with <c>X</c> and 6-6 without. Found by adversarial review round 7
    /// (Claude Opus 5).
    /// </summary>
    [Fact]
    public void ABodilessRowWhoseModifierIsInABranch_IsNotVouchedFor()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
            #if X
                public
            #endif
                int f;
            }
            """);

        var f = Assert.Single(index.Declarations, s => s.Name == "f");
        Assert.False(f.SpanKnown, "without X the field's signature starts on line 6, not line 4");
    }

    /// <summary>
    /// The fourteenth way, and it masks the twelfth's own brace-less arm exactly as the
    /// thirteenth masked the twelfth. Two groups bypass both round-10 rules at once: the
    /// brace-less <c>namespace Nr;</c> leaves <c>lastClosed</c> at -1, which is what the
    /// round-10 arm exists for, while the pending <c>Field</c> defeats that arm's demand for an
    /// EMPTY pending run -- and the thirteenth-way rule cannot cover for it because that rule
    /// needs <c>lastClosed &gt;= 0</c>. With <c>{!X,!Y}</c> the file is
    /// <c>class C { class Sr { } ; }</c> and <c>Sr</c> ends at the <c>;</c> on line 9; with
    /// <c>{!X,Y}</c> the <c>;</c> is the field's and <c>Sr</c> ends at its brace on line 2.
    /// Both parse. Found by adversarial review round 11 (Gemini 3.1 Pro).
    /// </summary>
    [Fact]
    public void ATrailingSemicolonMaskedAfterABracelessOpener_DoesNotVouchTheRowBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C {
                class Sr { }
            #if X
                namespace Nr;
            #endif
            #if Y
                int Field = 1
            #endif
                ;
            }
            """);

        var sr = Assert.Single(index.Declarations, s => s.Name == "Sr");
        Assert.False(sr.SpanKnown, "without either symbol the \";\" is Sr's own trailer, ending it on line 9");
    }

    /// <summary>
    /// The masking term of the fourteenth-way fix, isolated. Dropping the second group leaves
    /// the round-10 arm's original empty-run case, so this is the gate that fails if the
    /// <c>PendingCanVanishBefore</c> term is removed while the empty-run term is kept.
    /// </summary>
    [Fact]
    public void ABracelessOpenerBeforeABareSemicolon_StillRefusesWithNoPendingRun()
    {
        var index = DeclarationIndex.Build("""
            class C {
                class Sr { }
            #if X
                namespace Nr;
            #endif
                ;
            }
            """);

        var sr = Assert.Single(index.Declarations, s => s.Name == "Sr");
        Assert.False(sr.SpanKnown, "without X the \";\" is Sr's own trailer");
    }

    /// <summary>
    /// Recall for the fourteenth-way fix. The widened arm must not refuse a row whose trailing
    /// <c>;</c> is unconditional and whose enclosing scope is opened by a brace, which is the
    /// ordinary shape; only the row the <c>;</c> could reach is at stake, so the enclosing type
    /// stays vouched for in the refusal gates above as well.
    /// </summary>
    [Fact]
    public void AnUnconditionalTrailingSemicolon_KeepsTheRowBeforeIt()
    {
        var index = DeclarationIndex.Build("""
            class C {
                class Sr { };
                int After;
            }
            """);

        var sr = Assert.Single(index.Declarations, s => s.Name == "Sr");
        Assert.True(sr.SpanKnown, "nothing here is branch-dependent");
        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.True(c.SpanKnown, "the enclosing type is unaffected");
    }

    /// <summary>
    /// The premise round 10 stated for <c>PendingCanVanishBefore</c> -- that an unknown depth
    /// means a token inside a conditional group -- is false, and this is the witness. The group
    /// never balances, so the scan stops knowing the depth and does not recover, and
    /// <c>Tail</c> is refused although it lies outside every group. The rule stays sound because
    /// it needs only the converse. Found by adversarial review round 11 (Gemini 3.1 Pro).
    /// </summary>
    [Fact]
    public void AfterAnUnbalancedGroup_EvenARowOutsideEveryGroup_IsNotVouchedFor()
    {
        var index = DeclarationIndex.Build("""
            #if A
            class Opened {
            #endif
            class Tail { }
            """);

        var tail = Assert.Single(index.Declarations, s => s.Name == "Tail");
        Assert.False(tail.SpanKnown, "the scan never regains the depth after an unbalanced group");
    }

    /// <summary>
    /// The fifteenth way defeats all three trailing-<c>;</c> arms at once. A brace-bodied
    /// non-type member leaves <c>lastClosed &gt;= 0</c> but is not a trailer target, while a
    /// pending field defeats the empty-run arm. With <c>X</c>, the final <c>;</c> terminates
    /// <c>Fh</c> and <c>Sh</c> ends on line 3; without <c>X</c>, both members vanish and the
    /// <c>;</c> is <c>Sh</c>'s optional trailer on line 8. All four X/Y configurations compile.
    /// Found by adversarial review round 12 (Claude Opus 5).
    /// </summary>
    [Fact]
    public void ATrailingSemicolonMaskedByANonTypeBlock_DoesNotVouchTheEarlierType()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                class Sh { }
            #if X
                void Mh() { }
                int Fh = 1
            #endif
                ;
            }
            """);

        var sh = Assert.Single(index.Declarations, s => s.Name == "Sh");
        Assert.False(sh.SpanKnown, "without X the trailing \";\" belongs to Sh");
        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.True(c.SpanKnown, "the reach stays inside C");
    }

    /// <summary>
    /// The same non-type mask without a pending declaration. A property closed inside the group
    /// used to leave <c>lastClosed</c> nonnegative, but its kind made <c>trailerTarget</c> false
    /// while the nonnegative index made the brace-less arm false. Without <c>X</c>, the
    /// <c>;</c> reaches <c>Sh</c>; with <c>X</c>, the property stands between them.
    /// </summary>
    [Fact]
    public void ATrailingSemicolonAfterAConditionalProperty_DoesNotVouchTheEarlierType()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                class Sh { }
            #if X
                int P { get; set; }
            #endif
                ;
            }
            """);

        var sh = Assert.Single(index.Declarations, s => s.Name == "Sh");
        Assert.False(sh.SpanKnown, "without X the trailing \";\" belongs to Sh");
    }

    /// <summary>
    /// An extension block is transparent for parenting but its braces do not close the enclosing
    /// type. Treating the block's carried parent index as the row its <c>}</c> closed made the
    /// trailing-<c>;</c> refusal start one scope too high: it declined <c>C</c> and left
    /// <c>Sy</c> vouched. With <c>X</c>, the <c>;</c> follows the extension block and
    /// <c>Sy</c> ends on line 3; without it, the <c>;</c> is <c>Sy</c>'s trailer on line 9.
    /// All four X/Y configurations compile. Found by adversarial review round 12
    /// (Claude Opus 5).
    /// </summary>
    [Fact]
    public void ATrailingSemicolonAfterAConditionalExtensionBlock_RefusesItsSiblingNotItsParent()
    {
        var index = DeclarationIndex.Build("""
            static class C
            {
                class Sy { }
            #if X
                extension(int x)
                {
                }
            #endif
                ;
            }
            """);

        var sy = Assert.Single(index.Declarations, s => s.Name == "Sy");
        Assert.False(sy.SpanKnown, "without X the trailing \";\" belongs to Sy");
        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.True(c.SpanKnown, "closing the extension block does not close C");
        Assert.Equal(10, c.EndLine);
    }

    /// <summary>
    /// The seventeenth way: a file-scoped namespace owns a row and opens a scope, but no brace
    /// closes it. When one is written conditionally inside nested block namespaces, its stranded
    /// scope entry used to consume <c>Mid</c>'s physical <c>}</c>; every outer close then shifted
    /// one scope, leaving <c>Mid</c> vouched through line 10 where Roslyn's two compilable
    /// configurations end it on line 9. The existing file-scoped-namespace refusal begins at the
    /// namespace row and cannot repair enclosing rows emitted before it. Found by adversarial
    /// review round 13 (Claude Opus 5).
    /// </summary>
    [Fact]
    public void AConditionalFileScopedNamespace_DoesNotStealAnEnclosingNamespacesClosingBrace()
    {
        var index = DeclarationIndex.Build("""
            namespace Outer
            {
                namespace Mid
                {
                    class Sy { }
            #if X
                    namespace Inner;
            #endif
                }
            }
            """);

        var outer = Assert.Single(index.Declarations, s => s.Name == "Outer");
        Assert.True(outer.SpanKnown);
        Assert.Equal(10, outer.EndLine);

        var mid = Assert.Single(index.Declarations, s => s.Name == "Mid");
        Assert.True(mid.SpanKnown);
        Assert.Equal(9, mid.EndLine);

        var inner = Assert.Single(index.Declarations, s => s.Name == "Inner");
        Assert.False(inner.SpanKnown, "the file-scoped namespace exists only with X");
    }

    /// <summary>
    /// A legitimate file-scoped namespace still owns every declaration below it through end of
    /// file. Marking it as brace-less must prevent unrelated type braces from popping it without
    /// losing its transparent nesting or end-line calculation.
    /// </summary>
    [Fact]
    public void AFileScopedNamespace_RemainsOpenAcrossNestedTypeBraces()
    {
        var index = DeclarationIndex.Build("""
            namespace N;
            class A
            {
                class B { }
            }
            class C { }
            """);

        var ns = Assert.Single(index.Declarations, s => s.Name == "N");
        Assert.True(ns.SpanKnown);
        Assert.Equal(6, ns.EndLine);
        Assert.All(
            index.Declarations.Where(s => s.Name is "A" or "C"),
            row => Assert.Equal(ns, index.ParentOf(row)));
    }
}
