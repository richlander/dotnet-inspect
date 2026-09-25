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
- the region, change, line, and document characterization of a line diff;
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
later language-aware certifier. This design states facts, plus the
[whitespace policy](#whitespace-policy) that the operator directed for every
adopter; each adopter's slice implements it.

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
characterization is tracked by #8393 (S7 for RTS) and is not part of this
contract, apart from the operator-directed policy those slices adopt. The
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
[splitting into changes](#changes).

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
edit boundary is always adjacent to a non-whitespace character or to a text,
region, or change boundary, so it never splits a surrogate pair.

### Edit kinds

An edit carries one or more kinds, derived only from its two gap contents and
its position.

Line boundaries split a gap into *segments*. Each segment has exactly one
position:

| Position | Preceded by | Followed by |
| --- | --- | --- |
| *interior* | a non-whitespace character | a non-whitespace character |
| *leading* | a boundary, or the start of the text or change | a non-whitespace character |
| *trailing* | a non-whitespace character | a boundary, or the end of the text or change |
| *blank* | a boundary, or the start of the text or change | a boundary, or the end of the text or change |

A gap without boundaries is one segment. A gap with *n* boundaries has *n*+1
segments. When both gaps of an edit have equal boundary counts, their segments
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

The relations are consumed unchanged, but the characterization accepts only a
diff whose content assertions it can build on. Two preconditions apply:

- every anchor (defined below) joins two lines with ordinal-equal content; and
- every region with no move end has region texts that are not
  ordinal-equal.

[Analysis diff](analysis-diff.md) lets a producer assert `Unchanged` or
`Changed` under its own comparison contract, for example a case- or
whitespace-insensitive line key. Such a diff is valid there but can't be
characterized here. A mismatched endpoint or a violated precondition is an
argument failure, never an empty or success-shaped characterization. The
validator checks both preconditions.

Terminators are not part of the anchor precondition. Region text, defined
below, decides where each boundary belongs: a boundary belongs to the region
whose text on that side contains it, and any difference in it is located
there. A boundary that no region text contains belongs to no region. That
happens only when an anchor is followed, with no region in between on either
side, by another anchor or by the end of the text. The document summary
reports a difference in such a boundary, in spelling or final-terminator
presence, without locating it.

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
to exactly one region, and every boundary difference inside a region is
located. For example,
with `TextAnalysisDiffPresentation.CreateAnalysisDiff`, whose texts are
unterminated and which anchors every unchanged line that did not move,
`a⏎␠` → `a` gives the region texts `⏎␠` and the empty string, so the edit is
`LineBreaks`. A region of added blank lines likewise differs from an empty
region by `LineBreaks`. Locating a difference in final-terminator presence
requires the producer to leave that line out of the anchors, as
`TextFindings.CreateAnalysisDiff` does. If a producer anchors it instead, only
the document summary reports the difference, as it does for terminator
spelling.

### Changes

The owner partitions each region into an ordered sequence of *changes*. Each
change is a contiguous run of Before lines and a contiguous run of After lines,
either of which may be empty but not both. The changes are in order on both
sides, share no
line, and together cover the region.

On each side, the change texts are consecutive cuts of the region text, so
they concatenate to it exactly. Each change's text runs from its *start* to the
next change's start, or to the region's end:

- the first change starts at the region start, whether or not it has lines on
  that side;
- a later change with lines on that side starts at the start of its first
  line; and
- a later change with no lines on that side starts where the next change on
  that side starts, or at the region end, so its text on that side is empty.

For example, `p⏎k` → `p⏎⏎K` has anchor `p` and splits into two changes:

- a blank line added, with no Before lines: its Before text is `⏎` (the
  anchor's boundary) and its After text is `⏎⏎`, so it is `WhitespaceOnly`
  with one `LineBreaks` edit; and
- `k` → `K`, `Changed`.

Each change receives exactly one outcome:

| Outcome | Condition |
| --- | --- |
| `WhitespaceOnly` | The change texts differ whitespace-only, and the change holds no move end. Its canonical edits and their kinds are issued. |
| `Changed` | The whitespace-stripped change texts differ, and the change holds no move end. |
| `Moved` | The change is exactly one end of a move, as [Text move characterization](text-move-characterization.md) defines it. Movement is never whitespace. |

A change whose texts are ordinal-equal is never issued alone. It joins a
neighboring change, and the outcome of the joined change is recomputed. When
both of its neighbors are move ends, it is split into its Before lines and its
After lines, placed on opposite sides of an adjacent move end. A move end holds
lines on one side only, so one part can always cross it.

Each region first receives a deterministic *region outcome* from its own
texts. It is `WhitespaceOnly` when the region texts differ whitespace-only and
the region holds no move end, and `Changed`
otherwise. The partition then follows two rules:

- A `WhitespaceOnly` region is issued as exactly one `WhitespaceOnly` change.
- A `Changed` region contains at least one `Changed` or `Moved` change, and
  adjacent changes in it always have different outcomes, so every change is
  maximal. `Moved` changes are exempt from that rule, as the move design
  states, and so are two adjacent changes whose merged texts would be
  identical, which keeps a merge from re-forming an identical change.
  Where the owner can isolate a whitespace-only part, that part becomes its
  own `WhitespaceOnly` change.

Region outcomes, and the document summary built from them, never depend on
how the owner splits a region.

How far a `Changed` region is split is quality, not contract. The owner finds
the split points with an alignment of the non-whitespace characters. Splitting
is deterministic for the same inputs, and bounded by an owner budget. A region
over the budget is one `Changed` change.

Soundness doesn't depend on the aligner. A validator checks that:

- the input preconditions hold;
- each region's outcome matches its own texts, and a `WhitespaceOnly` region
  is exactly one change;
- the partition is ordered, non-overlapping, complete, and maximal, and the
  change texts are the consecutive cuts defined above; and
- each change's outcome matches its own texts under the whitespace-only
  predicate, and every move end is exactly one `Moved` change.

For example, `class Foo {` / `int x;` → `class Foo` / `{` / `int y;` splits
into two changes:

- `class Foo {` → `class Foo` / `{`, `WhitespaceOnly`, with one `LineBreaks`
  edit; and
- `int x;` → `int y;`, `Changed`.

By contrast, `return (x);` → `return (T)(x);` is one `Changed` change. No part
of it is whitespace-only, and adjacent `Changed` changes would violate
maximality.

### Line facts

Every Before and After line inside a region takes its change's outcome. So a
line is `WhitespaceOnly` exactly when the change holding it is, and a line in
a `Moved` change is `Moved` with that move's id. Anchor lines
are outside every region and carry no fact. A presentation that labels changes
therefore marks lines too, without any per-line machinery.

### Document summary

| Summary | Condition |
| --- | --- |
| `NoDifference` | The two input texts are ordinal-equal. |
| `WhitespaceOnly` | The texts differ, and every region is `WhitespaceOnly`. This includes texts whose only differences are in boundaries that belong to no region. |
| `Changed` | At least one region is `Changed`. |

A boundary difference outside every region is reported by the summary without
being located. Inside a region, a spelling difference is a located
`TerminatorSpelling` edit and a presence difference a located `LineBreaks`
edit. The pair characterization, which sees both texts, locates both
everywhere.

The relationship with the pair characterization runs one way. A
`WhitespaceOnly` summary implies that the pair characterization of the same
texts is `WhitespaceOnly`. The converse doesn't hold: movement or the line
diff's anchor choices can make the summary `Changed` even when the whole texts
differ whitespace-only. An example is `ab⏎a` → `a⏎ba`.

## Consumer presentation

Consumers choose the presentation. The characterization supports three modes
without re-comparison:

1. **Suppress:** show `WhitespaceOnly` changes as unchanged context.
2. **Mark:** show them as changes, labeled whitespace-only.
3. **Highlight:** show them as changes, with the exact edits as intraline
   ranges.

Every mode must preserve these facts:

- Only `NoDifference` may be called identical or "no difference." Suppress
  mode discloses what it hid, for example "no differences except
  whitespace" together with the count of hidden changes or lines.
- `AnalysisDiff<T>` statistics remain available unchanged. A host may add
  whitespace-only counts, but never subtracts them from its changed counts
  without saying so.
- `Changed` changes always present as ordinary changes.
- A host must never describe a whitespace-only difference as insignificant.
  Only a language certifier may make that claim.

### Whitespace policy

An adopter presents a comparison under one of two policies:

- **Sensitive:** mark or highlight every `WhitespaceOnly` change.
- **Insensitive:** suppress `WhitespaceOnly` changes as far as line
  correspondence allows, and disclose what was suppressed.

The default follows the provenance of the two endpoints, not the content:

| Endpoints | Default | Reason |
| --- | --- | --- |
| Authored → authored | Sensitive | Both layouts are authored, so every whitespace change is an authored edit. |
| Decompiled ↔ authored | Insensitive | The decompiled layout is emitted by the printer, so a whitespace difference reports printer layout, not an authored edit. |
| Decompiled → decompiled | Sensitive | Both layouts are printer output, so a whitespace change is a printer change, which a decompiler developer reviews. |

This is a provenance fact, not a significance claim. An insensitive
presentation says that whitespace-only differences are not shown and counts
them; it never calls them insignificant or the texts equivalent. A
whitespace-only difference inside a string literal is still a real
difference, and only a language certifier may say otherwise. A caller may
always select the other policy explicitly.

Insensitive presentation applies the Suppress mode with two constraints from
line correspondence:

- A `WhitespaceOnly` change is suppressed when both sides have the same
  nonzero number of lines and it has no `LineBreaks` and no
  `FinalLineTerminator` edit: its lines render as unchanged context. Every
  edit then has equal boundary counts over the same lines, so line *i* of one
  side corresponds to line *i* of the other and the two differ only within
  the line or in terminator spelling, which is the `diff -w` notion of an
  unchanged line. Equal boundary counts alone are not enough: a change whose
  text is empty on one side, such as `""` against a lone `␠␠` line, has no
  boundary on either side but zero lines against one.
- Any other `WhitespaceOnly` change cannot pair its lines as context. The
  common case is a blank line that authored code has and decompiled code
  lacks; a brace moved onto its own line is another. A line re-cut keeps the
  line count but not the correspondence: `if (c) {` after a blank line,
  against `if (c)` then `{`, has two lines on each side and no line that
  matches. A change that adds or removes the final line terminator pairs its
  lines, but an unterminated final line must stay changed. Such a change is
  hidden instead: the mapped diff keeps it as a change, and formatters leave
  it out of the rendered lines. GNU `diff -B` leaves out hunks that only add
  or remove blank lines; hiding goes further, because it also hides a
  whitespace-only change next to a `Changed` change, where `-B` would show it.

Moves are unaffected: a moved block is still presented as a move, and its
whitespace-only content fact is shown only under the sensitive policy.
`Changed` changes are unaffected under both policies.

An insensitive summary is two-sided like the statistics it adjusts. It
reports the Before and After counts without the lines of `WhitespaceOnly`
changes, subtracting each line from every facet count that holds it, and
reports the subtracted lines as a separate named Before and After count, so
the subtraction is always stated. A brace moved onto its own line contributes
one Before and two After whitespace-only lines; the removed blank line in
`JToken.Remove()` contributes one Before and zero After.

### Markout lowering

Each issued change lowers to one Markout `TextDiffChange`, except a change
suppressed under the insensitive policy, which lowers to none. Markout already
accepts changes that are in order, don't overlap, and may be adjacent. Mark
mode lowers the outcome to a change label. Highlight mode lowers the edits of
a `WhitespaceOnly` replacement to `TextDiffInnerMapping` span pairs, which
Markout admits only on replacement changes and only as single-line spans.
Boundary edits lower through the line structure plus empty (insertion-point)
spans. Whitespace-only additions and removals, such as blank lines, present at
line level. The adopter takes the lowering from the issued changes and edits
rather than re-deriving it.

Suppress mode lowers a suppressed change to no Markout change at all. Its
lines correspond one-to-one, so they fall into an unchanged gap; Markout
accepts that correspondence claim and deliberately does not compare the text
of an unchanged gap. Unified output prints context from the Before
side, as `diff -w` and `git diff -w` do, so an adopter that compares decompiled
with authored text places the authored text on the Before side where its
command allows.

A hidden change needs Markout slice M2, a caller-issued hidden flag on a
change. This design needs two properties from it: a hidden change renders no
removal or addition lines, and every format discloses each hidden run so
nothing disappears silently. Markout's own design owns hunk selection and patch
shape around hidden changes. The flag carries no whitespace meaning, just as a
label carries none.

Markout is the rendering substrate, not a consumer. The operator approved
Markout slice M1, under the
[Markout co-development loop](../markout-co-development.md), for the
presentation this design needs:

- **A typed label per change,** rendered in the unified-diff hunk header after
  the closing `@@`. That is the free-form slot git uses for function context,
  and member diffs, which are already scoped to one member, leave it unused.
  Formatters without that slot render the label as a label.
- **No merging across labels.** A hunk never holds changes with different
  labels. Adjacent or nearby changes with different labels become separate
  hunks. Unchanged context between them goes to at most one hunk, so no line
  appears in two hunks.
- **Visible whitespace glyphs** inside whitespace edits in human formats, with
  literal glyph characters escaped. Glyphs make an invisible edit readable;
  the label, not the glyph, classifies the change.

Hunks split this way remain valid unified diff. GNU `patch` and `git apply`
read uneven context as anchored to the start or end of the file, so Markout
gives each split hunk equal leading and trailing context except where it
touches either end. It also keeps a "no newline at end of file" marker in the
last hunk, merging the changes from that point on into one unlabeled hunk
when needed. `git apply` needs `--unidiff-zero` for zero-context hunks.
Member diffs use member-relative line numbers, so they were never applicable
to the source file in any case.

M1 changes presentation only, never the facts defined here. Terminal color is
a separate CLI decision. Inspect Web styles directly from the typed facts. No
host uses color as the only cue: a color shade is always paired with a label
or glyphs.

## Bounds and failure

- The whitespace-only predicate and canonical edits are linear. They run
  within the bounds the input diff already satisfied.
- Splitting runs under an owner-defined budget. A region over the budget is
  one `Changed` change. The owner never guesses, never omits the region, and
  never downgrades the document summary.
- Inputs that don't match the diff endpoints fail visibly and never produce an
  empty characterization.

## Pathological demonstration

Each row is a Release test in `tests/Inspector.Text.Tests`: pair rows in
`TextWhitespaceTests` and line-diff rows in `TextDiffCharacterizationTests`. Rows
marked *doc* are observed at the document summary. The others are observed on
their region's changes.

| Case | Before → After | Expected |
| --- | --- | --- |
| Brace reflow | `class Foo {` → `class Foo` / `{` | `WhitespaceOnly`; one edit, `" "` → a boundary, `LineBreaks` |
| Line join | `f(a,` / `␠␠b)` → `f(a, b)` | `WhitespaceOnly`, `LineBreaks` |
| Re-indentation | block indented one level deeper | `WhitespaceOnly`, `Indentation` on each line |
| Tabs versus spaces | `⇥x` → `␠␠␠␠x` | `WhitespaceOnly`, `Indentation` |
| Blank-line spaces (Scrutor) | a blank line with spaces becomes empty | `WhitespaceOnly`, `BlankLineContent` |
| Blank line removed (JToken.Remove) | blank line between statements removed | own region, one `WhitespaceOnly` change, `LineBreaks` |
| Blank line after a rewritten line | `x = Foo(); // old` / `` / `y();` → `x = Foo(); // new` / `y();` | `x` lines one `Changed` change; the removed blank line its own `WhitespaceOnly` change, `LineBreaks` |
| Line re-cut | `ab⏎b⏎c` → `a⏎␠b⏎bc` | one `WhitespaceOnly` change with two `LineBreaks` edits; no split into `Changed` parts is admissible |
| Leading zero-line side | `p⏎k` → `p⏎⏎K` | two changes: Before `⏎`, After `⏎⏎`, `WhitespaceOnly`, one `LineBreaks`; `k` → `K` `Changed` |
| Mixed reflow and edit | `class Foo {` / `int x;` → `class Foo` / `{` / `int y;` | two changes: `class Foo {` → `class Foo` / `{` `WhitespaceOnly`, `LineBreaks`; `int x;` → `int y;` `Changed` |
| Insertion inside a line (JToken.Annotation) | `return (_annotations as T);` → `return (T)(_annotations as T);` | one `Changed` change |
| Pure deletion | `foo(a, b)` → `foo(a)` | one `Changed` change |
| Append at line end | `x = 1;` → `x = 1; z` | one `Changed` change |
| Scatter | `ab` → `a` / `XYZ` / `b` | one `Changed` change |
| Separation | `foo( x )` → `foo(x)` | `WhitespaceOnly`, `Separation` |
| Token fusion | `x - -y` → `x--y` | `WhitespaceOnly`, `Separation`; the text layer makes no layout claim |
| Literal whitespace | `"a b"` → `"a  b"` | `WhitespaceOnly`, `Spacing`; significance left to a certifier |
| Markdown hard break | `line␠␠` → `line` | `WhitespaceOnly`, `Trailing` |
| YAML nesting | a key indented under a sibling | `WhitespaceOnly`, `Indentation`; structural meaning is not claimed |
| No-break space | `a b` → `a`U+00A0`b` | `Changed` |
| Word merge | `foo bar` → `foobar` | `WhitespaceOnly`, `Separation` |
| Swapped lines | `a` / `b` → `b` / `a` | region `Changed`; with a producer that issues a `Moved` correspondence, the moved line's ends are `Moved` changes |
| Trailing line after an anchor | `a⏎␠` → `a` (via `TextAnalysisDiffPresentation.CreateAnalysisDiff`) | `WhitespaceOnly`, `LineBreaks` covering the anchor's boundary |
| Final newline | `x` → `x⏎` | `WhitespaceOnly`, `LineBreaks` and `FinalLineTerminator` |
| Terminator spelling, line diff | `a⏎b` with CRLF → LF | *doc* `WhitespaceOnly`, no region |
| Final terminator spelling, line diff | `a␍␊` → `a␊` | *doc* `WhitespaceOnly`, no region |
| Insertion after a respelled boundary | `a␍␊b` → `a␊␊b` | one `WhitespaceOnly` change; one `LineBreaks` edit whose Before range covers the `␍␊` after `a` |
| Terminator spelling, pair | `a⏎b` with CRLF → LF | `WhitespaceOnly`, `TerminatorSpelling` |
| Surrogate adjacency | `😀 x` → `😀x` | edit spans are valid UTF-16 boundaries |
| Split budget | a region above the budget with one real edit | one `Changed` change; summary `Changed` |

Soundness gates (Release, in PR CI):

- `TextDiffCharacterizationValidator` in `tests/Inspector.Text.Tests` is an
  independent validator. It checks the input preconditions, recomputes each
  region's outcome, checks that a `WhitespaceOnly` region is one change, and
  checks that every region's changes form an ordered, non-overlapping,
  complete, maximal partition of consecutive cuts. It recomputes each change's
  outcome from its own texts, as [Changes](#changes) requires, and replays
  every whitespace-only change's edits to reproduce the After text.
- `TextDiffCharacterizationTests` runs the validator over every line-diff
  fixture row and over pinned real-source excerpts (Polly.Extensions
  `MeterEvent` and Newtonsoft.Json `JsonConvert.ToString`, under
  `tests/Inspector.Text.Tests/Assets`).
- `SmallTextSweep_EveryCharacterizationValidates` runs it over every pair of
  texts of up to three lines drawn from blank, content, indented-content, and
  whitespace-only lines, with and without a final terminator.

The Leading zero-line side, Mixed reflow, and Blank line after a rewritten
line rows state quality
expectations for the S1 splitter. The validator gate is what enforces
soundness.

## Precedent

| Implementation | Lesson |
| --- | --- |
| git `-w`/`-b`/`--ignore-blank-lines` | Whitespace handled as an equivalence switch: turning it on erases the difference. This design keeps the difference and reports it. |
| git `--color-moved-ws=allow-indentation-change` | Moved-and-re-indented blocks are a recognized need; [Text move characterization](text-move-characterization.md) detects them. |
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
  producer's statistics ([Text move characterization](text-move-characterization.md)
  changes `TextFindings.CreateAnalysisDiff` alignment);
- moves, which [Text move characterization](text-move-characterization.md)
  defines;
- intraline ranges for non-whitespace changes, though a follow-on may reuse the
  splitting alignment;
- whitespace beyond U+0020, U+0009, and logical line boundaries;
- locating differences in boundaries that belong to no region (in the
  line-diff characterization; boundaries inside a region are located, and the
  pair characterization locates every boundary difference);
- host rendering, gestures, or interaction; or
- adoption by any owner beyond the operator-directed decisions that
  [Production adoption](#production-adoption) records.

## Production adoption

[#8393](https://github.com/richlander/dotnet-inspect/issues/8393) tracks the
counted plan:

| Step | Owner | Slice |
| --- | --- | --- |
| S0 | `Inspector.Text` | This design |
| S1 | `Inspector.Text` | Pair and line-diff characterization, fixtures, soundness gates |
| S2 | `DotnetInspector.Presentation` | First adopter: lowering and statistics for member source diffs; CLI member Source Diff |
| M1 | Markout | Change labels in hunk headers, no merging across labels, visible whitespace glyphs |
| M2 | Markout | Caller-issued hidden changes, disclosed as omissions |
| S3 | `ILInspector.Research` and CLI | Authored-source text diff for `diff --pdb-source`, sensitive by default; its opt-in exclusion mode needs M2 |
| S4 | Inspect Web source-diff transport | Typed Worker transport of the characterization |
| S5 | Inspect Web diff viewer | The three presentation modes for authored → authored comparisons; Suppress as an explicit choice |
| S6 | `DotnetInspector.Presentation` and CLI | Insensitive policy for the member Source Diff, the default, with an explicit sensitive option; needs M2 |
| S7 | Decompiler harness (RTS) | Insensitive decompiled ↔ authored Correct judgment in place of `NormalizeBody`; whitespace-only classification of A/B changes |

S0–S2 and M1 have landed. S3, S6, and S7 are independent of each other,
except that S3's exclusion mode and S6 both follow M2. S6
changes only the lowering and summary of the existing CLI member Source Diff,
and follows M2 under the [Markout co-development loop](../markout-co-development.md).
M2 starts only after the operator approves it, as M1 was.
Inspect Web is reached at S5, which follows S4. S3 is independent of S4 and
S5.

The operator directed S3's adoption decisions for `diff` with authored source
on both sides. That owner's slice records them:

- the diff body shows every change by default, including whitespace-only
  changes, which are labeled;
- an opt-in mode excludes whitespace-only changes and discloses that it did,
  applying the insensitive rules;
- a summary-only mode reports counts without the body; and
- the summary reports changed lines, whitespace-only lines as a subset of the
  changed lines, and moved lines.

The operator also directed the [whitespace policy](#whitespace-policy)
defaults and their hosts:

- The insensitive default for decompiled ↔ authored comparisons is a CLI and
  RTS capability only; Inspect Web does not need it. This narrows the usual
  CLI-and-browser adoption of shared substrate by the operator's explicit
  approval. The facts stay in host-neutral `Inspector.Text`, which RTS
  consumes directly. The browser adoption of the PDB and decompiled member
  Source Diff that
  [Member source diff presentation](member-source-diff-presentation.md) and
  [Inspect Web source-diff transport](inspect-web-source-diff-transport.md)
  plan is unchanged and stays with those owners; it presents that comparison
  under the sensitive policy, as a recorded exception to the provenance
  default.
- For authored → authored comparisons, S5 offers all three modes. Mark and
  Highlight are the sensitive default, and Suppress is an explicit viewer
  choice that applies the insensitive rules above. S5's viewer renders from
  the transported facts, so it owns how it leaves out a change that cannot
  pair as context.
- The member Source Diff compares PDB (authored) with decompiled text, so S6
  makes it insensitive by default. The S2 labels remain its sensitive
  presentation.
- RTS consumes the facts, not a rendering. Its source probe already judges
  decompiled ↔ authored text with the nested source judgments
  `Printer exact ⊆ Correct ⊆ Valid` that
  [the correctness pipeline](../decompiler-correctness-pipeline.md) owns.
  Correct compares after `NormalizeBody`, which is the insensitive judgment.
  Printer exact is an opt-in ordinal comparison that normalizes only line
  terminators and one trailing newline; it is the existing explicit sensitive
  selection for this endpoint pair, and S7 leaves it unchanged.
- S7 replaces every `NormalizeBody` comparison in the probe with the pair
  outcome: Correct holds when the outcome is `Identical` or `WhitespaceOnly`.
  The probe records Printer exact only for a body that is already Correct,
  as it does today, so the nesting holds by construction; Printer exact
  compares its own printer-body extraction and is otherwise unchanged. S7 reports a body
  that is not Correct with the line-diff facts: `Changed` lines, moved blocks,
  and `WhitespaceOnly` lines counted separately. `NormalizeBody` removes every
  .NET `\s` character, which includes U+00A0, U+000B, U+000C, U+0085, other
  `Zs` characters, U+2028, and U+2029. This design's whitespace set
  deliberately excludes them, so a body that differs only by those characters
  stops being Correct. That change is intended, because a lookalike space must
  not pass as whitespace; S7 reports any registered source-oracle member it
  moves out of Correct as a manifest change in its own slice. Correct remains
  RTS's judgment, not an equivalence verdict from this design: whitespace
  inside a string literal stays a difference until a language certifier says
  otherwise.
- Decompiled → decompiled comparisons, such as RTS A/B runs across decompiler
  builds, keep the sensitive default. RTS A/B compares trimmed text ordinally
  today, which is sensitive to everything except leading and trailing
  whitespace of the whole text. S7 replaces that equality with the pair
  outcome: a method is unchanged only when the outcome is `Identical`, and
  each changed method is classified as `WhitespaceOnly`, a printer layout
  change, or `Changed`. Leading and trailing whitespace of the whole text
  therefore becomes a whitespace-only change instead of no change. The
  structural diff that explains changed methods is unchanged.

Follow-on owners track separately in #8393:

- `CSharpText` lexical certification;
- syntax-level equivalence beyond lexical certification, such as an
  expression-bodied member that corresponds to a one-statement block body,
  which no slice plans yet;
- decompiler structural-diff adoption of the pair primitive;
- retirement of the undeclared `CSharpBodyDiff` line-identity trim; and
- whether Printer exact gains the characterization as an explanation of a
  `Different` result, or moves its verdict onto the pair outcome, which the
  operator deferred to a discussion with the RTS owner.
