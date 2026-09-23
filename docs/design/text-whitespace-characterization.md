# Text whitespace characterization

## Status and ownership

This document defines the `Inspector.Text`-owned whitespace characterization
of text differences for
[#8393](https://github.com/richlander/dotnet-inspect/issues/8393), within the
structured-diff delivery tracker
[#5526](https://github.com/richlander/dotnet-inspect/issues/5526).

The normative claim is:

> For two texts, and for a completed line `AnalysisDiff<string>` over them,
> `Inspector.Text` issues deterministic, typed facts stating whether each
> difference is whitespace-only and exactly where each whitespace edit is, so
> a consumer can present a whitespace-only difference as no difference, as a
> marked line, or as highlighted ranges without re-deriving the comparison.

`Inspector.Text` owns:

- the whitespace vocabulary and the whitespace-only predicate;
- the pair characterization of two texts;
- the region, line, and document characterization of a line diff;
- the edit coordinates and edit kinds;
- determinism, soundness, bounds, and failure outcomes.

Supporting owners remain authoritative:

| Owner | Contract consumed |
| --- | --- |
| [Analysis diff](analysis-diff.md) | Relations, correspondence facets, and coordinates. This design does not change them. |
| `TextFindings` logical lines | CRLF, CR, and LF boundaries and final-terminator semantics |
| Markout `MappedTextDiff` | Replacement changes, single-line inner-mapping spans, annotations |
| [Member source diff presentation](member-source-diff-presentation.md) | First adopter: lowering, statistics, and CLI Source Diff |
| [Inspect Web source-diff transport](inspect-web-source-diff-transport.md) | Later typed Worker transport of the characterization |

Presentation lowering, host disclosure, and interaction belong to their
adopters. Whether a whitespace edit is *significant* belongs to a consumer or a
later language-aware certifier. This design states facts only.

## Why

`AnalysisDiff<T>` correctly says `Changed` for any difference under the
producer's comparison contract. A consumer can't tell a reformatted line from
a rewritten one, and nothing records where the whitespace edit is.

Whitespace-only is not "trivial." In YAML, indentation is structure. In
Markdown, two trailing spaces are a hard line break, a blank line ends a
paragraph, and indentation creates a code block. In C#, whitespace inside a
string literal is data. A diff should therefore report whitespace-only changes
precisely rather than ignore them, and consumers decide how to present them.

Undeclared whitespace policies already exist in comparison paths:

| Path | Current policy |
| --- | --- |
| `TextFindings` | CRLF, CR, and LF are the same logical boundary |
| `CSharpBodyDiff.GetLineIdentityKey` | `Trim()`: indentation-only line changes match |
| RTS `NormalizeBody` (`tools/DecompilerHarness`) | Removes every whitespace run before comparing |
| `MemberTextSlicer` member projection | Removes the member's common indentation, so a uniform re-indent (for example, a file-scoped-namespace conversion) is `Exact` |

The first three silently decide equivalence. This design replaces "decide
equivalence" with "report the whitespace facts." Moving those paths onto the
characterization is tracked by #8393 and is not part of this contract. The
member projection is a declared canonicalization of member placement, and it
stays. The characterization sees only the whitespace that survives it.

### Motivating assets

**Cross-version authored source.** In Scrutor 4.2.2 → 5.0.0,
`ServiceCollectionExtensions.Decorate<TService>(IServiceCollection,
Func<TService, IServiceProvider, TService>)` changed only in authored-source
whitespace. Upstream commit
[`75bbe6b`](https://github.com/khellang/Scrutor/commit/75bbe6b9500dcdf1ce80bfb17a82746f3b1606ad)
("Whitespace and line break tweaks"), in
`src/Scrutor/ServiceCollectionExtensions.Decoration.cs`, emptied a blank line
that held trailing spaces:

```bash
dnx dotnet-inspect -y -- diff --package Scrutor@4.2.2..5.0.0 \
  -S "Implementation Diff" --pdb-source \
  -t Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions \
  -m 'Decorate~9be53e5991' -v:d
```

```text
| Summary | 1 selected member; 0 decompiled C#, 0 IL, and 2 PDB Source evidence rows. |
| ...ServiceCollectionExtensions.Decorate | PDB Source |  | removed | -      |
| ...ServiceCollectionExtensions.Decorate | PDB Source |  | added | +  |
```

The C# and IL lanes are unchanged. The only evidence is an invisible removal
and addition with no classification, which is exactly the question a reviewer
asks: did the source really change? `TryDecorate<TService>` in the same pair
has the same shape. This is `BlankLineContent`.

**PDB versus decompiled source.** Newtonsoft.Json 13.0.3 `JToken.Remove()`,
with SourceLink commit
`0a2e291c0d9c0c7675d445703e51750363a549ef`, path
`Src/Newtonsoft.Json/Linq/JToken.cs`:

```bash
dnx dotnet-inspect -y -- member Newtonsoft.Json.Linq.JToken \
  --package Newtonsoft.Json@13.0.3 Remove:1 -S "Source Diff" -v:d
```

```diff
 public void Remove()
 {
-    if (_parent == null)
+    if (_parent is null)
     {
         throw new InvalidOperationException("The parent is missing.");
     }
-
     _parent.RemoveItem(this);
 }
```

The same member diff holds one real change and one whitespace-only change: a
removed blank line. Today both are only `Changed`. Authored code has blank
lines and the decompiler emits none, so this shape recurs across PDB and
decompiled comparisons. `JToken.Annotation<T>()` in the same package shows a
removed blank line inside a larger changed region, which motivates
[localization](#localization-inside-changed-regions).

S1 retains the pathological cases below as deterministic `Inspector.Text`
fixtures. Each adopting slice retains its motivating member as pinned-package
evidence: Newtonsoft.Json for member Source Diff, and Scrutor for the
implementation-diff PDB Source lane.

## Vocabulary

### Whitespace

Whitespace is exactly:

- U+0020 SPACE and U+0009 CHARACTER TABULATION within a logical line; and
- logical line boundaries, as `TextFindings` defines them.

Every other character is non-whitespace. That includes U+00A0 NO-BREAK SPACE,
other `Zs` characters, U+000B, U+000C, zero-width characters, and a byte
order mark. Text is untrusted, so replacing a visible space with a lookalike
must never qualify as whitespace-only. The narrow set is also sound for every
consumer: a language that treats more characters as whitespace can still
refine a `Changed` result, but no consumer can safely widen a whitespace-only
claim.

### Whitespace-only

Two texts differ *whitespace-only* when they are not ordinal-equal and
removing every whitespace character and line boundary from each leaves
ordinal-equal strings.

When that holds, the non-whitespace characters of the two texts correspond
one-to-one in order. This *canonical alignment* is unique. A *gap* is the
whitespace between two consecutive aligned non-whitespace characters, or
before the first or after the last. An *edit* is a gap whose Before and After
contents differ. The edits of a whitespace-only difference are therefore
unique and complete. No heuristic chooses them.

### Edit coordinates

Each edit carries a Before range and an After range. A range is a pair of
zero-based `(line, column)` positions over logical lines, with columns in UTF-16
code units. A range may span lines and may be empty (an insertion point). An
edit boundary is always adjacent to a non-whitespace character or to a text or
region boundary, so it never splits a surrogate pair.

### Edit kinds

An edit carries one or more kinds, derived only from its two gap contents and
its position.

Line boundaries split a gap into *segments*. Each segment has exactly one
position:

| Position | Preceded by | Followed by |
| --- | --- | --- |
| *interior* | a non-whitespace character | a non-whitespace character |
| *leading* | a boundary, or the start of the text | a non-whitespace character |
| *trailing* | a non-whitespace character | a boundary, or the end of the text |
| *blank* | a boundary, or the start of the text | a boundary, or the end of the text |

Inside a line-diff region, the edge of an adjacent anchor's content counts as
a non-whitespace neighbor. A gap without boundaries is one segment. A gap with
*n* boundaries has *n*+1 segments. When both gaps of an edit have equal boundary counts, their segments
correspond by index and have the same positions.

| Kind | The edit… |
| --- | --- |
| `LineBreaks` | has gaps with different boundary counts: a split or joined line, or blank lines added or removed. Only `FinalLineTerminator` may accompany it. |
| `FinalLineTerminator` | ends the text, and its gaps differ in whether a boundary is last |
| `TerminatorSpelling` | has equal boundary counts, and some corresponding boundary is spelled differently (CRLF, CR, or LF) |
| `Indentation` | has equal boundary counts, and some corresponding *leading* segment differs |
| `Trailing` | has equal boundary counts, and some corresponding *trailing* segment differs |
| `BlankLineContent` | has equal boundary counts, and some corresponding *blank* segment differs |
| `Spacing` | has an *interior* segment on both sides, both non-empty and different |
| `Separation` | has an *interior* segment on both sides, exactly one empty, so two non-whitespace characters become adjacent or separated |

The kinds under equal boundary counts come from the differing segments, so an
edit whose single differing segment is leading is exactly `Indentation`.

Kinds are facts about characters, not judgements. For example, `Separation`
applies equally to `foo( x )` → `foo(x)` and to `x - -y` → `x--y`. The first
is layout in C#; the second changes tokens. Telling them apart requires a
language certifier, which is a [non-claim](#non-claims).

## Pair characterization

Given two texts, the pair characterization issues exactly one outcome:

| Outcome | Meaning |
| --- | --- |
| `Identical` | The texts are ordinal-equal. |
| `WhitespaceOnly` | The texts differ whitespace-only. The complete ordered edits and their kinds are issued. |
| `Changed` | The whitespace-stripped texts differ. |

The outcome is decisive and linear in text size. `WhitespaceOnly` never
depends on a bound or heuristic.

This is the reusable primitive. Any correspondence producer can apply it to
two related spans, including a matched node pair from an identity-based
differ, where a *moved and whitespace-only* node is expressible. Such
adoptions are separate efforts.

## Line-diff characterization

### Input

The input is a completed `AnalysisDiff<string>` and the two texts it was
computed from. Each text's logical lines must equal the diff's sequence for
that side under the `TextFindings` analysis-line model:

- CRLF, CR, and LF are boundaries;
- empty text has no lines; and
- a final boundary ends the last line and does not start another.

A mismatch is an argument failure. The relations are consumed unchanged.

### Regions

The stable, unchanged, one-to-one correspondences are *anchors*. They
partition the two line sequences into *regions*: maximal runs of non-anchor
lines between consecutive anchors. The partition is the same one that turns a
diff into `MappedTextDiff` changes, so each region corresponds to exactly one
mapped change.

On each side, a region's *text* is the span from the end of the preceding
anchor's content (or the start of the text) to the start of the following
anchor's content (or the end of the text). It therefore includes the boundary
after the preceding anchor. Each boundary between two regions' lines belongs
to exactly one region, and every boundary difference is located. For example,
`a⏎␠` → `a` gives the region texts `⏎␠` and the empty string, so the edit is
`LineBreaks`. A region of added blank lines likewise differs from an empty
region by `LineBreaks`.

Each region receives one outcome:

| Outcome | Condition |
| --- | --- |
| `WhitespaceOnly` | The region texts differ whitespace-only, and no `Moved` correspondence has an endpoint in the region. Its edits are issued. |
| `Changed` | Otherwise. Movement is never whitespace. |

### Localization inside changed regions

A `Changed` region may still contain lines whose only differences are
whitespace. Examples include a reflowed brace next to a real edit, and a
removed blank line inside a rewritten block. For each `Changed` region, the
owner issues either:

- `Localized`: a partial, monotone alignment that pairs some of the region's
  non-whitespace characters with equal characters on the other side; or
- `NotLocalized`: the region exceeded the localization budget.

A `WhitespaceOnly` region is always localized, by its canonical alignment.

Consecutive aligned pairs, together with the region start and end, split the
region texts into corresponding *intervals*. An interval is *clean* when both
of its sides contain only whitespace. It is *dirty* when either side contains
an unaligned non-whitespace character. A clean interval whose sides differ is
an issued whitespace edit, with kinds as defined above. Dirty intervals are
content changes, and their whitespace is not issued separately.

Localization is best-effort in precision but sound. A validator can check the
facts below from the texts and the issued alignment alone, without trusting
the aligner. How many characters get aligned is quality, not contract.
Localization is deterministic for the same inputs.

### Line facts

Every Before and After line inside a region receives exactly one fact:

| Fact | Meaning |
| --- | --- |
| `Changed` | The line contains an unaligned non-whitespace character. |
| `WhitespaceOnly` | Every non-whitespace character on the line is aligned to an equal character, and a differing interval (a whitespace edit or a dirty interval) overlaps the line. The line holds no content change, although its placement or surrounding whitespace changed. |
| `Unaffected` | Every non-whitespace character on the line is aligned, and every interval that overlaps the line is clean and identical on both sides. |
| `NotLocalized` | The line is in a `NotLocalized` region. |

The first three facts partition the lines of every localized region by their
content. For example, a blank line removed inside a rewritten block has no
non-whitespace characters, lies in a dirty interval, and is `WhitespaceOnly`.
Anchor lines are outside every region and carry no fact.

### Document summary

| Summary | Condition |
| --- | --- |
| `NoDifference` | The two input texts are ordinal-equal. |
| `WhitespaceOnly` | The texts differ, and every region is `WhitespaceOnly`. This includes texts that differ only in line-terminator spelling and so have no region. |
| `Changed` | At least one region is `Changed`. |

A line diff's logical lines don't retain terminator spelling. The summary
therefore reports a CRLF-versus-LF-only difference without locating it. The
pair characterization, which sees both texts, locates it as
`TerminatorSpelling`.

## Consumer presentation

Consumers choose the presentation. The characterization supports three modes
without re-comparison:

1. **Suppress:** show whitespace-only regions as unchanged context.
2. **Mark:** show them as changes, with a whitespace-only indicator on each
   affected line.
3. **Highlight:** show them as changes, with the exact edits as intraline
   ranges.

Every mode must preserve these facts:

- Only `NoDifference` may be called identical or "no difference." Suppress
  mode discloses what it hid, for example "no differences except
  whitespace" together with the hidden region count.
- `AnalysisDiff<T>` statistics remain available unchanged. A host may add
  whitespace-only counts, but never subtracts them from `Changed` without
  saying so.
- `Changed`, `NotLocalized`, and `Changed`-fact lines always present as
  ordinary changes.
- A host must never describe a whitespace-only difference as insignificant.
  Only a language certifier may make that claim.

In highlight mode, a lowering to Markout maps edits to
`TextDiffInnerMapping` span pairs. Markout admits inner mappings only on
replacement changes and only as single-line spans, so line-boundary edits
lower through the line structure plus empty (insertion-point) spans or
annotations. Addition-only and removal-only regions, such as blank lines,
present at line level. The first adopter owns the lowering's exact shape and
must take it from the issued edits rather than re-derive it.

Markout is the rendering substrate, not a consumer. S2 requires no Markout
change: annotations serve mark mode in every formatter, and the Unicode and
Spectre formatters render inner mappings for highlight mode. The Markdown and
plain-text unified diffs cannot express intraline ranges, so hosts using them
fall back from highlight to mark. Two Markout additions would improve
highlight mode and are optional follow-ons under the
[Markout co-development loop](../markout-co-development.md): visible glyphs
for spaces, tabs, and boundaries inside changed spans, and a typed category on
inner mappings so formatters can style whitespace edits differently from
content edits.

## Bounds and failure

- The whitespace-only predicate and canonical edits are linear. They run
  within the bounds the input diff already satisfied.
- Localization runs under an owner-defined budget. Exceeding the budget yields
  `NotLocalized` for that region only. The owner never guesses, never omits
  the region, and never downgrades the document summary.
- Inputs that don't match the diff endpoints fail visibly and never produce an
  empty characterization.

## Pathological demonstration

Each row becomes an S1 Release test in `tests/Inspector.Text.Tests`. Rows
marked *doc* are observed at the document summary. The others are observed on
their region.

| Case | Before → After | Expected |
| --- | --- | --- |
| Brace reflow | `class Foo {` → `class Foo` / `{` | `WhitespaceOnly`; one edit, `" "` → a boundary, `LineBreaks` |
| Line join | `f(a,` / `␠␠b)` → `f(a, b)` | `WhitespaceOnly`, `LineBreaks` |
| Re-indentation | block indented one level deeper | `WhitespaceOnly`, `Indentation` on each line |
| Tabs versus spaces | `⇥x` → `␠␠␠␠x` | `WhitespaceOnly`, `Indentation` |
| Blank-line spaces (Scrutor) | a blank line with spaces becomes empty | `WhitespaceOnly`, `BlankLineContent` |
| Blank line removed (JToken.Remove) | blank line between statements removed | own region `WhitespaceOnly`, `LineBreaks` |
| Blank line in rewrite (JToken.Annotation) | blank line removed inside a changed region | region `Changed`; blank line localized `WhitespaceOnly` |
| Mixed reflow and edit | `class Foo {` / `int x;` → `class Foo` / `{` / `int y;` | region `Changed`; reflow lines `WhitespaceOnly`; `int` lines `Changed` |
| Separation | `foo( x )` → `foo(x)` | `WhitespaceOnly`, `Separation` |
| Token fusion | `x - -y` → `x--y` | `WhitespaceOnly`, `Separation`; the text layer makes no layout claim |
| Literal whitespace | `"a b"` → `"a  b"` | `WhitespaceOnly`, `Spacing`; significance left to a certifier |
| Markdown hard break | `line␠␠` → `line` | `WhitespaceOnly`, `Trailing` |
| YAML nesting | a key indented under a sibling | `WhitespaceOnly`, `Indentation`; structural meaning is not claimed |
| No-break space | `a b` → `a`U+00A0`b` | `Changed` |
| Word merge | `foo bar` → `foobar` | `WhitespaceOnly`, `Separation` |
| Swapped lines | `a` / `b` → `b` / `a` | `Changed` (movement) |
| Trailing line after an anchor | `a⏎␠` → `a` (unterminated text) | region `WhitespaceOnly`, `LineBreaks` covering the anchor's boundary |
| Final newline | `x` → `x⏎` | `WhitespaceOnly`, `LineBreaks` and `FinalLineTerminator` |
| Terminator spelling, line diff | `a⏎b` with CRLF → LF | *doc* `WhitespaceOnly`, no region |
| Terminator spelling, pair | `a⏎b` with CRLF → LF | `WhitespaceOnly`, `TerminatorSpelling` |
| Surrogate adjacency | `😀 x` → `😀x` | edit spans are valid UTF-16 boundaries |
| Localization budget | a region above the budget with one real edit | region `Changed`, `NotLocalized`; summary `Changed` |

Soundness gates, planned in S1:

- a property test checks every `WhitespaceOnly` outcome by comparing the
  whitespace-stripped texts; and
- an independent validator checks every localized line against the
  [localization obligations](#localization-inside-changed-regions), over the
  fixtures and a pinned real-source corpus.

## Precedent

| Implementation | Lesson |
| --- | --- |
| git `-w`/`-b`/`--ignore-blank-lines` | Whitespace handled as an equivalence switch: turning it on erases the difference. This design keeps the difference and reports it. |
| git `--color-moved-ws=allow-indentation-change` | Moved-and-re-indented blocks are a recognized need; that pairing is left to identity-bearing producers. |
| git `--word-diff`, `diff-highlight` | Intraline highlighting; `diff-highlight` pairs lines by position within equal-count hunks and can't show a brace reflow. |
| GitHub | Intraline highlighting plus a hide-whitespace view filter. |
| VS Code diff editor | Line-range mappings with character-range inner changes, the same shape as Markout inner mappings. |
| difftastic, SemanticDiff | Language-aware formatting-insensitivity with visible fallback, which is the later certifier's role. |
| ChangeDistiller | A change-kind taxonomy; its significance levels are the judgement this design leaves out. |

## Non-claims

This design does not define:

- whitespace significance, token preservation, or comment-only or
  literal-only classification (a separate `CSharpText` certifier design);
- a change to `AnalysisDiff<T>`, to `TextFindings` relations, or to any
  producer's statistics;
- move detection across whitespace edits for line diffs;
- intraline ranges for non-whitespace changes, though a follow-on may reuse the
  localization alignment;
- whitespace beyond U+0020, U+0009, and logical line boundaries;
- locating line-terminator spelling changes in the line-diff characterization
  (the pair characterization locates them);
- host rendering, gestures, or interaction; or
- adoption by any owner other than the first adopter.

## Production adoption

[#8393](https://github.com/richlander/dotnet-inspect/issues/8393) tracks the
counted plan:

| Step | Owner | Slice |
| --- | --- | --- |
| S0 | `Inspector.Text` | This design |
| S1 | `Inspector.Text` | Pair and line-diff characterization, fixtures, soundness gates |
| S2 | `DotnetInspector.Presentation` | First adopter: lowering and statistics for member source diffs; CLI member Source Diff |
| S3 | `ILInspector.Research` | Implementation-diff PDB Source lane classification; CLI `diff --pdb-source` |
| S4 | Inspect Web source-diff transport | Typed Worker transport of the characterization |
| S5 | Inspect Web diff viewer | The three presentation modes |

The CLI is first reached at S2, which is three steps. Inspect Web is reached at
S5, which follows S0–S2 and S4. S3 is independent of S4 and S5.

Follow-on owners track separately in #8393:

- `CSharpText` lexical certification;
- decompiler structural-diff adoption of the pair primitive; and
- retirement of the RTS `NormalizeBody` policy and the undeclared
  `CSharpBodyDiff` line-identity trim.
