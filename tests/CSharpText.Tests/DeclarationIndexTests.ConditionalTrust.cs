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
    /// A conditional group compiles exactly one branch, or none. If every branch returns to the
    /// brace depth the <c>#if</c> started at, the depth after the <c>#endif</c> is that depth
    /// whichever branch the compiler keeps — so declarations below the group are unaffected by it
    /// and their spans are knowable. Recovering that is #3668.
    /// <para>
    /// Before it, a conditional directive set the lexer's untracked flag and nothing cleared it, so
    /// the place was lost for the rest of the file rather than for the region the directive guards.
    /// On dotnet/runtime's libraries at revision <c>e614b717a9d</c> a conditional appears in 8.3% of
    /// files but cost 12.1% of declarations, because the loss ran to end of file.
    /// </para>
    /// <para>
    /// Rows are asserted through both emit paths. A bodiless row — a field, an interface method —
    /// takes a separate <c>SpanKnown</c> assignment in <c>EmitBodiless</c> from the one a
    /// body-bearing row takes, and a fixture of methods with bodies leaves that assignment ungated:
    /// hard-coding it to <see langword="true"/> passed the entire suite before this test named
    /// <c>Field</c> and <c>Bodiless</c>.
    /// </para>
    /// <para>
    /// What stays lost is asserted by
    /// <see cref="AnUnbalancedConditional_StillLosesEveryLaterRow"/> and
    /// <see cref="AConditionalInitializer_ReportsUnknownRatherThanOneBranchsEnd"/>. Brace balance
    /// makes a *following* span knowable; it says nothing about a span whose own terminator sits
    /// inside a branch.
    /// </para>
    /// </summary>
    [Fact]
    public void ABalancedConditional_CostsOnlyTheRowsInsideIt()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
            #if DEBUG
                void Debug() { }
            #endif
                void Always() { }
                int Field;
            }
            interface I
            {
                void Bodiless();
            }
            """);

        // Named rather than counted: a row that vanished would otherwise pass an Assert.All, and
        // Field and Bodiless are the two that reach EmitBodiless.
        foreach (var name in new[] { "C", "Always", "Field", "I", "Bodiless" })
        {
            var row = Assert.Single(index.Declarations, s => s.Name == name);
            Assert.True(row.SpanKnown, $"'{name}' sits outside the group and should resolve");
        }

        // The row *inside* the branch is still withheld. Its text is indexed -- the index is
        // lexical and reports what is written -- but whether it compiles depends on a symbol the
        // index does not know, and #3672 is where that question is answered.
        var conditional = Assert.Single(index.Declarations, s => s.Name == "Debug");
        Assert.False(conditional.SpanKnown, "a row inside a branch is not known to compile");

        Assert.Equal("Always", index.FindByLine(6)?.Name);

        // The same declarations without the directive resolve identically, so the group now costs
        // nothing outside itself.
        var plain = DeclarationIndex.Build("""
            class C
            {
                void Always() { }
                int Field;
            }
            interface I
            {
                void Bodiless();
            }
            """);

        Assert.All(plain.Declarations, s => Assert.True(s.SpanKnown));
        Assert.Equal("Always", plain.FindByLine(3)?.Name);
    }

    /// <summary>
    /// A branch-local attribute makes the branch-local declaration unknown, but that declaration
    /// consumes the attribute in every build where either exists. Its unknownness must not leak
    /// past a balanced <c>#endif</c> and withhold the next unconditional declaration. Serilog
    /// 4.2.0's <c>Logger.Write(LogEvent)</c> has this exact shape immediately after a
    /// FEATURE_SPAN-only attributed overload.
    /// </summary>
    [Fact]
    public void ABranchLocalAttributedDeclaration_DoesNotPoisonTheFollowingRow()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
            #if FEATURE
                [Obsolete]
                void Conditional() { }
            #endif
                void Always() { }
            }
            """);

        var conditional = Assert.Single(index.Declarations, row => row.Name == "Conditional");
        Assert.False(conditional.SpanKnown);

        var always = Assert.Single(index.Declarations, row => row.Name == "Always");
        Assert.True(always.SpanKnown);
        Assert.Equal(always, index.FindByLine(7));
    }

    /// <summary>
    /// A conditional attribute can attach to an unconditional declaration, making that
    /// declaration's attribute set unknown. Its unconditional terminator still consumes the
    /// complete declaration in every build, so the next header starts clean.
    /// </summary>
    [Fact]
    public void AConditionalAttributeConsumedByAnUnconditionalDeclaration_DoesNotPoisonTheFollowingRow()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
            #if FEATURE
                [Obsolete]
            #endif
                int Field;
                void Always() { }
            }
            """);

        var field = Assert.Single(index.Declarations, row => row.Name == "Field");
        Assert.False(field.SpanKnown);

        var always = Assert.Single(index.Declarations, row => row.Name == "Always");
        Assert.True(always.SpanKnown);
        Assert.Equal(always, index.FindByLine(7));
    }

    /// <summary>
    /// The positive case above is safe only when the attribute and declaration are consumed
    /// together. If another branch consumes the attribute, one build still carries it to the
    /// declaration after <c>#endif</c>, so that declaration must remain unknown.
    /// </summary>
    [Fact]
    public void AnAttributeConsumedOnlyInAnotherBranch_StillPoisonsTheFollowingRow()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
            #if FEATURE
                [Obsolete]
            #else
                void Conditional() { }
            #endif
                void Always() { }
            }
            """);

        var always = Assert.Single(index.Declarations, row => row.Name == "Always");
        Assert.False(always.SpanKnown);
        Assert.Equal(DeclarationKind.Class, index.FindByLine(8)?.Kind);
    }

    /// <summary>
    /// The negative half, and the reason balance is measured per branch rather than over the group
    /// as scanned. A structural conditional — one that opens a brace in one branch and closes it in
    /// another, or declares a different signature per branch — leaves a depth after its
    /// <c>#endif</c> that really does depend on which branch compiles, so the loss must stand.
    /// These are 26.6% of the directive groups in dotnet/runtime's libraries and are what #3672
    /// addresses with the PDB; the remaining 0.8% are body-only and unbalanced, and are undecidable
    /// without knowing the symbol set.
    /// </summary>
    [Theory]
    // A brace opened in one branch and closed after the #endif.
    [InlineData("class C\n{\n#if NET8\n    void M() {\n#else\n    void M() {\n#endif\n    }\n    void After() { }\n}")]
    // A brace opened inside a body in one branch only.
    [InlineData("class C\n{\n    void M()\n    {\n#if DEBUG\n        if (x) {\n#endif\n        }\n    }\n    void After() { }\n}")]
    // Balance judged over the last branch too: the #else arm is the one that does not return.
    [InlineData("class C\n{\n#if A\n    void M() { }\n#else\n    void M() {\n#endif\n    }\n    void After() { }\n}")]
    // An unbalanced group followed by a balanced one. This is coverage of a real shape, NOT a gate
    // on the stickiness of the loss: an unbalanced group mangles the brace structure enough that
    // these rows are unknown for other reasons too, so the fixture passes whether or not a
    // balanced close clears the flag. Stickiness is gated by the two hidden-directive tests, which
    // set the flag inside a group that then closes balanced. Recorded because two successive
    // attempts to cite this test for stickiness were wrong (adversarial review round 3).
    [InlineData("class C\n{\n#if A\n    void M() {\n#else\n    void M() {\n#endif\n    }\n#if B\n    void N() { }\n#endif\n    void After() { }\n}")]
    public void AnUnbalancedConditional_StillLosesEveryLaterRow(string source)
    {
        var index = DeclarationIndex.Build(source.Split('\n'));

        // Asserted over every row rather than over "After" alone: an unbalanced group can mangle
        // the brace structure badly enough that the trailing declaration is never emitted as a row
        // at all, and a test naming it would then fail for the wrong reason. What must hold is that
        // nothing in the file claims a span the scan cannot vouch for.
        Assert.NotEmpty(index.Declarations);
        Assert.All(index.Declarations, s => Assert.False(s.SpanKnown));
    }

    /// <summary>
    /// <para>
    /// In preprocessor-disabled text the compiler does not lex code: <c>/*</c> opens no comment
    /// and a quote opens no string, but directives are still recognized and still nest. So a
    /// conditional directive sitting in what this lexical scan believes is a comment is real if
    /// the surrounding branch is disabled, and skipping it makes a later <c>#endif</c> close the
    /// wrong group.
    /// </para>
    /// <para>
    /// That is the one failure the index may not have: with <c>OUTER</c> undefined the class ends
    /// on the last line, but skipping the commented <c>#if</c> closes the outer group early, makes
    /// the brace on line 8 look live, and vouches for a span two lines short. Found by adversarial
    /// review; the rule refuses instead, and only while a group is open, since outside one the
    /// text cannot be disabled and <c>#if</c> in a comment is unambiguously prose.
    /// </para>
    /// </summary>
    [Fact]
    public void ADirectiveHiddenInsideACommentWithinAGroup_LosesTheDepth()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
            #if OUTER
            /*
            #if INNER
            */
            #endif
            }
            #endif
            }
            """);

        Assert.False(
            Assert.Single(index.Declarations, s => s.Name == "C").SpanKnown,
            "a directive the scan could only skip by believing itself in a comment is ambiguous");

        // The same shape with no group open is not ambiguous at all, and must keep working --
        // this repository's own sources write "#if" inside comments and would otherwise be lost.
        var prose = DeclarationIndex.Build("""
            class C
            {
            /*
            #if INNER
            */
                void M() { }
            }
            """);

        Assert.True(Assert.Single(prose.Declarations, s => s.Name == "M").SpanKnown);
    }

    /// <summary>
    /// The comment fixture above exercises only the <c>InBlockComment</c> half of that rule. A
    /// directive hidden inside a <em>literal</em> is the same ambiguity -- in disabled text a quote
    /// opens no string either -- and deleting just that arm silently restores the wrong-span defect
    /// the rule exists to prevent (adversarial review round 2, GPT-5.6 Sol).
    /// </summary>
    [Fact]
    public void ADirectiveHiddenInsideALiteralWithinAGroup_LosesTheDepth()
    {
        var index = DeclarationIndex.Build(""""
            #if OUTER
            class A
            {
                string s = """
                    #if INNER
                    """;
            }
            #endif
            class C { }
            """");

        Assert.False(
            Assert.Single(index.Declarations, s => s.Name == "C").SpanKnown,
            "a directive the scan could only skip by believing itself in a literal is ambiguous");
    }

    /// <summary>
    /// A skipped section is the one place the compiler processes <em>only</em> conditional
    /// directives: <c>#pragma</c>, <c>#region</c>, <c>#nullable</c> and <c>#line</c> are text
    /// whichever way the branch falls, and cannot open, close or renumber a group. Refusing them
    /// would poison a whole file for a directive that changes nothing, so the ambiguity rule tests
    /// which directive it found rather than that it found one (adversarial review round 2,
    /// Gemini 3.1 Pro).
    /// </summary>
    [Fact]
    public void ANonConditionalDirectiveHiddenInsideALiteral_KeepsTheDepth()
    {
        var index = DeclarationIndex.Build("""
            class C {
            #if DEBUG
                string s = @"
            #pragma warning disable
                ";
            #endif
                void After() { }
            }
            """);

        Assert.True(
            Assert.Single(index.Declarations, s => s.Name == "After").SpanKnown,
            "a #pragma inside a literal cannot change a group's structure in either build");
    }

    /// <summary>
    /// Trivia opens a row's span, and <c>SpanKnown</c> is a claim about the row's lines. A doc
    /// comment written inside a conditional group leads a declaration outside it with a
    /// branch-dependent start line: below, the row's trivia is line 2 in one build and line 4 in
    /// the other. Comment tokens never reach the pending-signature list, so neither
    /// <c>SpanKnown</c> expression consulted them (adversarial review round 2, GPT-5.6 Sol).
    /// </summary>
    [Fact]
    public void AConditionalDocComment_LosesTheRowItLeads()
    {
        var index = DeclarationIndex.Build("""
            #if X
            /// X docs
            #else
            /// Y docs
            #endif
            class C { }
            """);

        Assert.False(
            Assert.Single(index.Declarations, s => s.Name == "C").SpanKnown,
            "the row's trivia starts on a line only one build compiles");

        // A declaration with no scope of its own is emitted by a different site, with its own
        // SpanKnown expression. Covering only the braced one left that site ungated: the mutation
        // that drops the trivia term from it survived until this fixture existed.
        var bodiless = DeclarationIndex.Build("""
            class C
            {
            #if X
                /// X docs
            #else
                /// Y docs
            #endif
                int F;
            }
            """);

        Assert.False(
            Assert.Single(bodiless.Declarations, s => s.Name == "F").SpanKnown,
            "a field's trivia starts on a line only one build compiles");
    }

    /// <summary>
    /// An attribute list is leading trivia too, and its tokens do not reach the pending-signature
    /// list either, so it is the second door to the same defect as
    /// <see cref="AConditionalDocComment_LosesTheRowItLeads"/>.
    /// </summary>
    [Fact]
    public void AConditionalAttributeList_LosesTheRowItLeads()
    {
        var index = DeclarationIndex.Build("""
            #if X
            [Foo]
            #endif
            class C { }
            """);

        Assert.False(
            Assert.Single(index.Declarations, s => s.Name == "C").SpanKnown,
            "the row's trivia starts on a line only one build compiles");
    }

    /// <summary>
    /// <para>
    /// A file-scoped namespace is the one scope opener in C# that uses no brace, so neither the
    /// balance rule nor the opening-depth floor can see it. A group whose branches declare
    /// different file-scoped namespaces opens and closes at depth 0 and is judged balanced, while
    /// the enclosing declaration of every row below the <c>#endif</c> differs by build -- and the
    /// scope runs to end of file, so no <c>#endif</c> repairs it.
    /// </para>
    /// <para>
    /// Found independently by both reviewers in round 2. This is the third distinct way branches
    /// can agree on depth and disagree on meaning, after the comment-hidden directive and the
    /// opening-depth floor, which is why the rule is stated as a refusal rather than as a depth
    /// correction.
    /// </para>
    /// </summary>
    [Fact]
    public void ConditionalFileScopedNamespaces_LoseEveryRowBelowThem()
    {
        var index = DeclarationIndex.Build("""
            #if X
            namespace B;
            #else
            namespace C;
            #endif

            class D { }
            """);

        Assert.False(
            Assert.Single(index.Declarations, s => s.Name == "D").SpanKnown,
            "D is in namespace B in one build and C in the other");

        // An unconditional file-scoped namespace is the overwhelmingly common case and must keep
        // vouching for what it encloses, including across a balanced group below it.
        var plain = DeclarationIndex.Build("""
            namespace B;

            #if X
            class Inner { }
            #endif

            class D { }
            """);

        Assert.True(Assert.Single(plain.Declarations, s => s.Name == "D").SpanKnown);

        // One conditional namespace, no alternative. The refusal starts at the row after the
        // namespace, and with two namespaces the alternative occupies that row -- so a fixture
        // with two masks an off-by-one that this one catches, because here the refusal's first
        // row is D itself (adversarial review round 3, GPT-5.6 Sol).
        var single = DeclarationIndex.Build("""
            #if X
            namespace B;
            #endif

            class D { }
            """);

        Assert.False(
            Assert.Single(single.Declarations, s => s.Name == "D").SpanKnown,
            "D is in namespace B in one build and at file scope in the other");
    }

    /// <summary>
    /// An "assembly:" or "module:" attribute list belongs to the compilation unit, so it ends the
    /// trivia run above it rather than carrying it down: Roslyn reports the next declaration's
    /// leading trivia as starting after such a list. The knownness of the trivia it consumed must
    /// be dropped with it, or a conditional comment above a unit attribute would refuse a
    /// declaration whose own trivia begins below it. This is the one path that resets trivia
    /// knownness rather than setting it, and nothing else reaches it (adversarial review round 3,
    /// GPT-5.6 Sol).
    /// </summary>
    [Fact]
    public void AUnitAttributeAfterConditionalTrivia_StillVouchesForWhatFollows()
    {
        var index = DeclarationIndex.Build("""
            #if X
            /// conditional assembly docs
            #endif
            [assembly: System.CLSCompliant(true)]

            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.True(c.SpanKnown, "C's own trivia starts below the unit attribute");
        Assert.Equal(6, c.TriviaStartLine);
    }

    /// <summary>
    /// <para>
    /// An attribute list inside a conditional group is reported in <c>AttributeLists</c> even
    /// though only one build compiles it. When an unconditional list comes first the row's lines
    /// do not move -- trivia still starts at the first list, and the conditional one falls inside
    /// the range -- so the line-based rule alone vouches for the row while its list set is
    /// build-dependent. Unlike a comment, a list is not merely a line inside the range: it is a
    /// claim about what is applied to the declaration, so knownness is intersected over every
    /// list rather than taken from the one that opened the trivia.
    /// </para>
    /// <para>
    /// The contrasting comment case is deliberately <em>not</em> refused, and
    /// <see cref="AnUnconditionalCommentAboveAConditionalOne_StillVouchesForTheRow"/> holds that
    /// line (adversarial review round 3, Gemini 3.1 Pro).
    /// </para>
    /// </summary>
    [Fact]
    public void AConditionalAttributeListAfterAnUnconditionalOne_LosesTheRow()
    {
        var index = DeclarationIndex.Build("""
            [Attr1]
            #if X
            [Attr2]
            #endif
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.Equal(1, c.TriviaStartLine);
        Assert.False(c.SpanKnown, "only one build applies Attr2, and the row reports it either way");
    }

    /// <summary>
    /// The comment counterpart of
    /// <see cref="AConditionalAttributeListAfterAnUnconditionalOne_LosesTheRow"/>, and the reason
    /// the two are treated differently. A conditional comment below an unconditional one changes
    /// no line this row reports: trivia still starts at line 1, the row still ends at line 5, and
    /// the conditional comment's lines already fall inside that range. <c>SpanKnown</c> is a claim
    /// about the row's lines, so refusing here would cost recall for nothing. Verified by
    /// rendering both builds with line numbers preserved: every reported line is identical
    /// (adversarial review round 3, reported by Gemini 3.1 Pro as a defect and dismissed by
    /// measurement).
    /// </summary>
    [Fact]
    public void AnUnconditionalCommentAboveAConditionalOne_StillVouchesForTheRow()
    {
        var index = DeclarationIndex.Build("""
            // comment 1
            #if X
            // comment 2
            #endif
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.Equal(1, c.TriviaStartLine);
        Assert.Equal(5, c.EndLine);
        Assert.True(c.SpanKnown, "both builds report the same first and last line for this row");
    }

    /// <summary>
    /// <para>
    /// An attribute list can CROSS a conditional group, and the tokens inside the group can decide
    /// whether the list binds to this declaration at all. With <c>X</c> the list is a
    /// compilation-unit attribute and <c>C</c> starts on line 6 with no attributes; without
    /// <c>X</c> the same list is <c>C</c>'s own and <c>C</c> starts on line 1 (confirmed against
    /// Roslyn in both symbol configurations). Knownness sampled at the <c>[</c> found that token
    /// outside the group and vouched for the first answer.
    /// </para>
    /// <para>
    /// So knownness is accumulated over every token of the list, not just its opener
    /// (adversarial review round 4, GPT-5.6 Terra).
    /// </para>
    /// </summary>
    [Fact]
    public void AConditionalAttributeTarget_LosesTheRowBelowIt()
    {
        var index = DeclarationIndex.Build("""
            [
            #if X
            assembly:
            #endif
            System.CLSCompliant(true)]
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.False(c.SpanKnown, "the target decides whether this list is C's own trivia");
    }

    /// <summary>
    /// The bodiless-emit counterpart of
    /// <see cref="AConditionalAttributeTarget_LosesTheRowBelowIt"/>. There are two
    /// <c>SpanKnown</c> expressions, and a fixture written with <c>class C { }</c> reaches only
    /// the braced one -- which is how a round-2 fix shipped with the bodiless site ungated. A
    /// field is emitted through the other.
    /// </summary>
    [Fact]
    public void AConditionalAttributeTarget_LosesABodilessRowBelowIt()
    {
        var index = DeclarationIndex.Build("""
            class Outer {
            [
            #if X
            field:
            #endif
            System.Obsolete]
            int F;
            }
            """);

        var f = Assert.Single(index.Declarations, s => s.Name == "F");
        Assert.False(f.SpanKnown, "the bodiless emit site must consult the same knownness");
    }

    /// <summary>
    /// A literal inside a conditional group inside an attribute list. This is the negative that
    /// bounds the accumulation: a broader placement that also consumed comment and literal tokens
    /// refused this row, and refusing it costs recall for nothing, because the list's ends are
    /// punctuators and its target is a word, so the literal changes no line this row reports
    /// (adversarial review round 4; the broad placement was written first and this fixture is
    /// what falsified it).
    /// </summary>
    [Fact]
    public void AConditionalLiteralInsideAnAttributeList_StillVouchesForTheRowBelowIt()
    {
        var index = DeclarationIndex.Build("""
            [System.Obsolete(
            #if X
            "a"
            #else
            "b"
            #endif
            )]
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.Equal(1, c.TriviaStartLine);
        Assert.Equal(8, c.EndLine);
        Assert.True(c.SpanKnown, "both builds report the same first and last line for this row");
    }

    /// <summary>
    /// The negative that bounds the three above. An unconditional compilation-unit attribute still
    /// resets the trivia and still vouches for what follows, so the round-4 rule refuses crossing
    /// lists rather than attribute lists in conditional files generally.
    /// </summary>
    [Fact]
    public void AnUnconditionalUnitAttributeInAConditionalFile_StillVouchesForWhatFollows()
    {
        var index = DeclarationIndex.Build("""
            #if X
            #endif
            [assembly: System.CLSCompliant(true)]
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.Equal(4, c.TriviaStartLine);
        Assert.True(c.SpanKnown, "nothing about this list crosses the group");
    }

    /// <summary>
    /// A directive name is an identifier, so <c>#endif_foo</c> spells <c>endif_foo</c> and is not
    /// the <c>#endif</c> directive: Roslyn reports CS1024 and CS1027 and leaves the group open in
    /// every symbol configuration. Reading it as an <c>#endif</c> closed the group, recovered the
    /// depth, and vouched for what followed. <c>char.IsLetterOrDigit</c> misses underscore, which
    /// is the whole gap -- <c>#endif-</c> and <c>#endif//note</c> are recognized by Roslyn and are
    /// still recognized here (adversarial review round 3, Gemini 3.1 Pro). Round 5 added the rest
    /// of what C# allows to continue an identifier: a combining mark (U+0301), connector
    /// punctuation (U+203F) and a format character (U+200C) all behave exactly like
    /// <c>_foo</c> for Roslyn, and letters/digits/underscore alone missed all three
    /// (adversarial review round 5, GPT-5.6 Sol).
    /// </summary>
    [Fact]
    public void ADirectiveNameRunningIntoAnIdentifier_DoesNotCloseTheGroup()
    {
        // Every suffix here was checked against Roslyn: the first four report CS1024 and CS1027
        // in every symbol configuration, and the last two are accepted.
        foreach (var open in (string[])["#endif_foo", "#endif\u0301", "#endif\u203F", "#endif\u200C"])
        {
            var index = DeclarationIndex.Build($"#if X\nclass C {{ }}\n{open}\nclass D {{ }}");
            Assert.False(
                Assert.Single(index.Declarations, s => s.Name == "D").SpanKnown,
                $"'{open}' is not an #endif, so the group is still open");
        }

        // The forms Roslyn does accept must keep closing the group, or this costs real recovery.
        foreach (var closer in (string[])["#endif", "#endif//note", "#endif /* note */", "#endif-"])
        {
            var closed = DeclarationIndex.Build($"#if X\nclass C {{ }}\n{closer}\nclass D {{ }}");
            Assert.True(
                Assert.Single(closed.Declarations, s => s.Name == "D").SpanKnown,
                $"'{closer}' closes the group for Roslyn and must close it here");
        }
    }

    /// <summary>
    /// <para>
    /// A terminator in one branch discards trivia recorded in another, and the row below the group
    /// then reports a trivia start only one build agrees with: with <c>X</c> the comment documents
    /// <c>C</c>, and without it the <c>using</c> ends a declaration and <c>C</c> has no
    /// documentation at all. Confirmed against Roslyn in both configurations.
    /// </para>
    /// <para>
    /// Resetting the header unconditionally forgot the comment <em>and</em> restored knownness
    /// (adversarial review round 5, GPT-5.6 Sol). This is the sixth distinct way two branches can
    /// agree on brace depth and disagree on meaning.
    /// </para>
    /// </summary>
    [Fact]
    public void ATerminatorInOneBranchDiscardingAnothersTrivia_LosesTheRowBelow()
    {
        var index = DeclarationIndex.Build("""
            #if X
            // X docs
            #else
            using System;
            #endif
            class C { }
            """);

        Assert.False(
            Assert.Single(index.Declarations, s => s.Name == "C").SpanKnown,
            "one build documents C and the other does not");
    }

    /// <summary>
    /// The attribute form of <see cref="ATerminatorInOneBranchDiscardingAnothersTrivia_LosesTheRowBelow"/>.
    /// With <c>X</c> Roslyn reports one attribute list on <c>C</c>; without it, none.
    /// </summary>
    [Fact]
    public void ATerminatorInOneBranchDiscardingAnothersAttribute_LosesTheRowBelow()
    {
        var index = DeclarationIndex.Build("""
            #if X
            [System.Obsolete]
            #else
            using System;
            #endif
            class C { }
            """);

        Assert.False(
            Assert.Single(index.Declarations, s => s.Name == "C").SpanKnown,
            "one build applies the attribute and the other does not");
    }

    /// <summary>
    /// The negative that bounds the two above. A terminator inside a group discards nothing when
    /// no trivia was recorded, and both builds report the same first line for <c>C</c>, so the
    /// rule refuses branch-dependent discards rather than conditional terminators generally.
    /// </summary>
    [Fact]
    public void ATerminatorInsideAGroupDiscardingNothing_StillVouchesForTheRowBelow()
    {
        var index = DeclarationIndex.Build("""
            #if X
            using System;
            #endif
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.Equal(4, c.TriviaStartLine);
        Assert.True(c.SpanKnown, "nothing branch-dependent was discarded");
    }

    /// <summary>
    /// The poison from a branch-dependent discard must survive a later trivia record. Here the
    /// comment below the <c>#endif</c> is on a known line, so assigning knownness at that point
    /// rather than intersecting it restored a vouch the discard had just removed. Roslyn reports
    /// <c>C</c>'s first comment on line 6 without <c>X</c> and line 2 with it.
    /// </summary>
    [Fact]
    public void TriviaRecordedAfterABranchDependentDiscard_DoesNotRestoreTheVouch()
    {
        var index = DeclarationIndex.Build("""
            #if X
            // X docs
            #else
            using System;
            #endif
            // C docs
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.Equal(6, c.TriviaStartLine);
        Assert.False(c.SpanKnown, "with X the comment on line 2 is C's documentation instead");
    }

    /// <summary>
    /// The recovered closing line of a type that CONTAINS a balanced group -- the row the whole
    /// rule exists to keep. The corpus gate skipped every such declaration until round 5, so a
    /// mutation reporting their end one line short passed it (adversarial review round 5,
    /// GPT-5.6 Sol); this pins the same property on a fixture, and the gate's
    /// <c>containingVouched</c> floor keeps the corpus path non-vacuous.
    /// </summary>
    [Fact]
    public void ATypeContainingABalancedGroup_ReportsItsRealClosingLine()
    {
        var index = DeclarationIndex.Build("""
            class Outer
            {
            #if X
                void M() { }
            #else
                void M() { }
            #endif
            }
            """);

        var outer = Assert.Single(index.Declarations, s => s.Name == "Outer");
        Assert.Equal(1, outer.SignatureStartLine);
        Assert.Equal(8, outer.EndLine);
        Assert.True(outer.SpanKnown, "every branch returns to the depth the group opened at");
    }

    /// <summary>
    /// The unit-attribute close path ends a header too, so inside a group it may only take
    /// knownness away. The line-3 list poisons; <c>t1</c>'s reset empties the list set but rightly
    /// keeps the poison; then the <c>[assembly:]</c> path ASSIGNED it away. Without <c>Y</c> Roslyn
    /// binds the line-3 list to <c>Tail</c> and the unit list with it (CS0657), so <c>Tail</c>'s
    /// trivia is line 3 and it carries two lists; with <c>Y</c> its trivia is line 9 and it carries
    /// none. Found by the differential fuzzer after the <c>ResetHeader</c> fix had cut its flag
    /// count from 3,146 to 6, every survivor this one site (adversarial review round 6,
    /// Claude Opus 4.8).
    /// </summary>
    [Fact]
    public void AUnitAttributeAfterADiscardedAttribute_DoesNotRestoreTheVouch()
    {
        var index = DeclarationIndex.Build("""
            #if Y
            #else
            [System.Obsolete]
            #endif
            #if X
            class t1 { }
            #endif
            [assembly: System.CLSCompliant(true)]
            class Tail { }
            """);

        var tail = Assert.Single(index.Declarations, s => s.Name == "Tail");
        Assert.False(tail.SpanKnown, "without Y the line-3 list binds to Tail, and the unit list with it");
    }

    /// <summary>
    /// A poison raised inside a group must survive every later reset until the group closes. The
    /// comment is discarded by <c>struct s {</c>, which poisons; then <c>}</c> resets again with
    /// nothing recorded and nothing crossing, and ASSIGNING knownness there declared the header
    /// clean while still inside the group. But with <c>X</c> there is no <c>struct s</c> to have
    /// eaten the comment, so it is <c>Tail</c>'s documentation: Roslyn reports trivia line 2 with
    /// <c>X</c> and line 6 without, both configurations parsing with zero errors. Found by a
    /// differential fuzzer over 16,673 fair cases (adversarial review round 6, Claude Opus 4.8).
    /// </summary>
    [Fact]
    public void ADiscardedHeaderInsideAGroup_StaysLostAcrossALaterCleanReset()
    {
        var index = DeclarationIndex.Build("""
            #if X
            // doc
            #else
            struct s { }
            #endif
            class Tail { }
            """);

        var tail = Assert.Single(index.Declarations, s => s.Name == "Tail");
        Assert.False(tail.SpanKnown, "with X the comment on line 2 is Tail's documentation");
    }

    /// <summary>
    /// The same restore, reached through an attribute list rather than a comment, and costing a
    /// semantic claim rather than a line: with <c>Y</c> the class carries TWO <c>[Obsolete]</c>
    /// lists and the row reported one. The brace scope that resets a second time is a property's
    /// <c>{ get; set; }</c> here, which is why round 5's single-reset fixture missed the shape
    /// (adversarial review round 6, Claude Opus 4.8).
    /// </summary>
    [Fact]
    public void ADiscardedAttributeInsideAGroup_StaysLostAcrossALaterCleanReset()
    {
        var index = DeclarationIndex.Build("""
            #if Y
            [System.Obsolete]
            #else
            int p0 { get; set; }
            #endif
            [System.Obsolete]
            class Tail { }
            """);

        var tail = Assert.Single(index.Declarations, s => s.Name == "Tail");
        Assert.False(tail.SpanKnown, "with Y the class carries the line-2 list as well");
    }

    /// <summary>
    /// Sections must advance at the <c>#if</c>, not only at the <c>#else</c>. Here the discarded
    /// header sits BEFORE the group at a known depth -- so nothing else in the builder condemns it
    /// -- and only the opening directive separates it from the terminator that eats it. Roslyn
    /// reports <c>C</c>'s trivia at line 4 with <c>X</c> and line 1 without, both configurations
    /// parsing with zero errors. Verified by mutation: without the increment at <c>#if</c> this is
    /// the only test that fails.
    /// </summary>
    [Fact]
    public void AHeaderBeforeAGroupEatenInsideIt_LosesTheRowBelow()
    {
        var index = DeclarationIndex.Build("""
            // docs
            #if X
            using System;
            #endif
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.False(c.SpanKnown, "without X the comment on line 1 is C's documentation");
    }

    /// <summary>
    /// A terminator in one branch discarding a MODIFIER recorded in another. Round 5 keyed the
    /// rule on recorded trivia alone, so with nothing to lose on the trivia side the reset
    /// declared the row below known -- and got its SIGNATURE start wrong instead. Compiled in both
    /// configurations: with <c>X</c>, <c>C</c>'s signature starts at line 2 (<c>public</c>);
    /// without, at line 6 (adversarial review round 6, GPT-5.6 Sol).
    /// </summary>
    [Fact]
    public void ATerminatorInOneBranchDiscardingAnothersModifier_LosesTheRowBelow()
    {
        var index = DeclarationIndex.Build("""
            #if X
            public
            #else
            using System;
            #endif
            class C { }
            """);

        var c = Assert.Single(index.Declarations, s => s.Name == "C");
        Assert.False(c.SpanKnown, "with X the modifier on line 2 is part of C's signature");
    }

    /// <summary>
    /// The same rule at the OTHER reset site: a property initializer's terminator. The reset there
    /// was ungated -- neutralizing it left all 398 tests passing -- while its doc comment claimed
    /// otherwise (adversarial review round 6, GPT-5.6 Sol). With <c>X</c> the property has no
    /// initializer and the comment on line 4 documents <c>D</c>; without it the <c>= 1;</c>
    /// terminator eats that comment and <c>D</c> has none. Both configurations compile.
    /// </summary>
    [Fact]
    public void AnInitializerInOneBranchDiscardingAnothersTrivia_LosesTheRowBelow()
    {
        var index = DeclarationIndex.Build("""
            class Outer {
                int P { get; }
            #if X
                // X docs
            #else
                = 1;
            #endif
                class D { }
            }
            """);

        var d = Assert.Single(index.Declarations, s => s.Name == "D");
        Assert.False(d.SpanKnown, "with X the comment on line 4 is D's documentation");
    }

    /// <summary>
    /// The over-refusal guard for the round-6 widening. A header and the terminator that discards
    /// it, written in the SAME branch, lose nothing: whichever build compiles, either both are
    /// present or neither is. Keying the rule on the terminator's <c>DepthKnown</c> instead of on
    /// section identity would condemn every declaration below every group containing a statement,
    /// which is most of a conditional file.
    /// </summary>
    [Fact]
    public void AStatementInsideAGroup_StillVouchesForTheRowBelow()
    {
        var index = DeclarationIndex.Build("""
            class Outer {
            #if X
                int Conditional = 1;
                void M() { }
            #endif
                class D { }
            }
            """);

        var d = Assert.Single(index.Declarations, s => s.Name == "D");
        Assert.Equal(6, d.SignatureStartLine);
        Assert.True(d.SpanKnown, "nothing crossed a branch boundary");
    }

    /// <summary>
    /// A UTF-8 byte order mark is not whitespace, so trimming left it in front of the <c>#</c> and
    /// the opening directive was scanned as code. Roslyn strips the preamble and reports no error
    /// for this file, selecting <c>C</c> on line 3 with <c>X</c> and line 5 without it, so the
    /// misread vouched for one branch's declaration (adversarial review round 5, GPT-5.6 Sol).
    /// </summary>
    [Fact]
    public void AByteOrderMarkBeforeAnOpeningDirective_DoesNotHideTheGroup()
    {
        var index = DeclarationIndex.Build("\uFEFF#if X\nusing System;\nclass C { }\n#else\nclass C { }\n#endif\n");

        Assert.All(
            index.Declarations.Where(s => s.Name == "C"),
            s => Assert.False(s.SpanKnown, "both C rows are inside a conditional group"));
    }

    /// <summary>
    /// <para>
    /// Equal brace depth does not prove equal enclosing declaration. A branch that closes a brace
    /// its group did not open is closing a scope from outside the group, so the branches can agree
    /// on the depth after the <c>#endif</c> while disagreeing about which type encloses the text
    /// there -- below, the trailing member is inside <c>B</c> in one build and <c>C</c> in the
    /// other, at identical depth.
    /// </para>
    /// <para>
    /// Both reviewers found this independently, from opposite ends. The balance rule measures
    /// depth, so the fix is a floor: a group may not reach below its own opening depth.
    /// </para>
    /// </summary>
    [Fact]
    public void ABranchThatClosesAScopeItsGroupDidNotOpen_LosesTheDepth()
    {
        var index = DeclarationIndex.Build("""
            class A {
            #if X
            }
            class B {
            #else
            }
            class C {
            #endif
                void M() { }
            }
            """);

        Assert.NotEmpty(index.Declarations);
        Assert.All(index.Declarations, s => Assert.False(s.SpanKnown));

        // Reaching below the opening depth is the discriminator, not nesting: a group opened
        // inside a nested type that stays at or above its own opening depth still recovers.
        var nested = DeclarationIndex.Build("""
            class C {
                class D {
            #if X
                    void Inner() { }
            #endif
                    void After() { }
                }
            }
            """);

        Assert.True(Assert.Single(nested.Declarations, s => s.Name == "After").SpanKnown);
    }

    /// <summary>
    /// <para>
    /// A branch that does not return to the depth its group opened at makes the group unbalanced,
    /// even when the group's <em>closing</em> depth is right. This is the direction that matters:
    /// each branch is measured from the opening depth and the depth is reset at every branch
    /// boundary, so by the time the <c>#endif</c> is reached the discrepancy has been erased and
    /// the group looks balanced. The flag raised at the branch boundary is the only record that
    /// it was not, and it is what stops a span being vouched for on the strength of one branch.
    /// </para>
    /// <para>
    /// Written because the mutation battery found the flag ungated: deleting it left the suite
    /// green while turning every such group into a false <c>SpanKnown</c>, which is the one
    /// outcome the index is not allowed to produce.
    /// </para>
    /// </summary>
    [Fact]
    public void ABranchThatDoesNotReturnToTheOpeningDepth_UnbalancesTheGroup()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
            #if A
                void Extra() {
            #else
                void Extra() { }
            #endif
                void After() { }
            }
            }
            """);

        // The group closes at the depth it opened at -- the reset saw to that -- so nothing the
        // #endif can measure distinguishes this from a group whose branches balance.
        //
        // The trailing brace balances the file for the branch that leaves one open, so that a row
        // after the #endif closes cleanly and would be vouched for if the group were judged
        // balanced. Without it the imbalance wrecks the row structure instead, and every row
        // reports unknown for a reason that has nothing to do with the rule under test -- which
        // is how the first draft of this fixture passed against a build with the rule deleted.
        //
        // Asserted over the whole file rather than by naming the trailing declaration, since the
        // row set differs between the two readings and a test naming one row can fail on its
        // absence rather than on its knownness.
        Assert.NotEmpty(index.Declarations);
        Assert.All(index.Declarations, s => Assert.False(s.SpanKnown));
    }

    /// <summary>
    /// An unbalanced group nested inside a balanced one poisons the outer group: the enclosing
    /// group cannot return to its own opening depth if something inside it did not. Asserted
    /// because propagation is a separate line from the balance check and a fixture with one level
    /// of nesting leaves it ungated.
    /// </summary>
    [Fact]
    public void AnUnbalancedInnerConditional_PoisonsTheGroupAroundIt()
    {
        var nested = DeclarationIndex.Build("""
            class C
            {
            #if A
            #if B
                void X() {
            #endif
                }
            #endif
                void After() { }
            }
            """);

        Assert.False(
            Assert.Single(nested.Declarations, s => s.Name == "After").SpanKnown,
            "an unbalanced inner group must not be forgotten when the outer one closes");

        // The fixture above does not actually reach the propagation line: the inner group's stray
        // brace also drives the outer group off its own opening depth, so the outer #endif catches
        // it unaided. Propagation is only load-bearing when the outer group looks balanced on its
        // own, which needs the inner group to have two branches -- the branch reset returns the
        // depth to the inner group's base, hiding the discrepancy from every later measurement, so
        // the flag raised at the #else is the only surviving evidence.
        var masked = DeclarationIndex.Build("""
            class C
            {
            #if A
            #if B
                void X() {
            #else
                void X() { }
            #endif
            #endif
                void After() { }
            }
            }
            """);

        // Balanced for the open branch, for the same reason as the fixture above.
        Assert.NotEmpty(masked.Declarations);
        Assert.All(masked.Declarations, s => Assert.False(s.SpanKnown));

        // The same nesting with both groups balanced resolves, so nesting alone is not the cause.
        var balanced = DeclarationIndex.Build("""
            class C
            {
            #if A
            #if B
                void X() { }
            #endif
            #endif
                void After() { }
            }
            """);

        Assert.True(Assert.Single(balanced.Declarations, s => s.Name == "After").SpanKnown);
    }

    /// <summary>
    /// <para>
    /// A group with more than one branch, each of which balances, is still balanced — most groups
    /// have a second branch, so a rule that only handled <c>#if</c>/<c>#endif</c> would recover
    /// almost nothing. <c>#elif</c> needs no separate handling: it ends the branch above it and
    /// starts another, exactly as <c>#else</c> does, which is why both spellings are asserted
    /// against one fixture.
    /// </para>
    /// <para>
    /// This does <em>not</em> gate the depth reset at the branch boundary, and an earlier version
    /// of this comment claimed it did. The reset is unobservable: a branch that fails to return to
    /// the group's opening depth raises the unbalanced flag in the same breath, and the flag
    /// condemns the group whatever the depth counter goes on to say, so deleting the reset leaves
    /// every assertion in this suite green. It is recorded as an equivalent mutation and kept for
    /// the invariant, not for the answer — without it a later branch's check would be measured
    /// against an earlier branch's leftovers, which is a worse thing for the code to mean.
    /// <see cref="ABranchThatDoesNotReturnToTheOpeningDepth_UnbalancesTheGroup"/> is what gates
    /// the flag.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("#else")]
    [InlineData("#elif OTHER")]
    public void AMultiBranchConditional_IsBalancedWhenEveryBranchIs(string middle)
    {
        var index = DeclarationIndex.Build(string.Join('\n',
        [
            "class C",
            "{",
            "    void M()",
            "    {",
            "#if FEATURE",
            "        if (a) { X(); }",
            middle,
            "        if (b) { Y(); }",
            "#endif",
            "    }",
            "    void After() { }",
            "}",
        ]));

        Assert.True(Assert.Single(index.Declarations, s => s.Name == "After").SpanKnown);
        Assert.True(Assert.Single(index.Declarations, s => s.Name == "M").SpanKnown);
    }

    /// <summary>
    /// A stray <c>#else</c>, <c>#elif</c> or <c>#endif</c> with no group open is malformed source.
    /// The scan has no opening depth to measure against, so it refuses rather than guessing — the
    /// alternative is an index-out-of-range on the frame stack.
    /// </summary>
    [Theory]
    [InlineData("#endif")]
    [InlineData("#else")]
    [InlineData("#elif X")]
    public void AConditionalDirectiveWithNoGroupOpen_LosesTheDepth(string stray)
    {
        var index = DeclarationIndex.Build(string.Join('\n',
            ["class C", "{", stray, "    void After() { }", "}"]));

        Assert.False(Assert.Single(index.Declarations, s => s.Name == "After").SpanKnown);
    }

    /// <summary>
    /// A property's initializer is terminated after its accessor block has already closed, so that
    /// terminator <i>extends</i> a span that was measured and marked known a moment earlier. It is
    /// the only path that mutates an already-measured span, and so the only one that can turn a
    /// known span into a wrong one rather than into a lost one.
    /// <para>
    /// A conditional between the accessor block and the initializer puts each candidate terminator
    /// in a different branch, so the end this reads is one branch's end. Before this was gated the
    /// row below reported a span ending at line 5 and claimed it was known, which is the answer for
    /// a <c>FEATURE</c> build and off by two lines for every other build. That falsified this
    /// suite's standing claim that an unresolvable conditional costs rows rather than corrupting
    /// them, so it is asserted here rather than left to the sibling test's <c>Assert.All</c>: the
    /// row is present and known-looking, which is exactly what a sweep over rows cannot catch.
    /// </para>
    /// </summary>
    [Fact]
    public void AConditionalInitializer_ReportsUnknownRatherThanOneBranchsEnd()
    {
        var index = DeclarationIndex.Build("""
            class C
            {
                int P { get; }
            #if FEATURE
                = 1;
            #else
                = 2;
            #endif
            }
            """);

        var p = Assert.Single(index.Declarations, s => s.Name == "P");
        Assert.False(p.SpanKnown, "a span whose end is one branch's must not report as known");

        // The property is withheld, so line 3 selects the enclosing class instead. The class is
        // legitimately known: its own braces sit outside the group, and the group's branches each
        // balance, so its end does not depend on which branch compiles. Brace balance is what
        // makes a *following* span knowable; it says nothing about a span whose terminator is
        // inside a branch, which is why P stays lost while C does not.
        Assert.Equal("C", index.FindByLine(3)?.Name);

        // Without the conditional the same shape resolves, so the directive is the whole cause and
        // the trailing-initializer path still extends the span it belongs to.
        var plain = DeclarationIndex.Build("""
            class C
            {
                int P { get; }
                = 1;
            }
            """);

        var q = Assert.Single(plain.Declarations, s => s.Name == "P");
        Assert.True(q.SpanKnown);
        Assert.Equal(4, q.EndLine);
    }
}
