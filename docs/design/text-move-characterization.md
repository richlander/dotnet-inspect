# Text move characterization

## Status and ownership

This document defines the `Inspector.Text`-owned characterization of moved
line blocks within one text diff, for
[#8435](https://github.com/richlander/dotnet-inspect/issues/8435) under the
tracker [#8393](https://github.com/richlander/dotnet-inspect/issues/8393). It
extends [Text whitespace characterization](text-whitespace-characterization.md),
which the same owner defines.

The normative claim is:

> For a completed line `AnalysisDiff<string>` that meets the whitespace
> characterization's preconditions, `Inspector.Text` identifies each moved
> block as one *move*. A move links its Before lines to its After lines with a
> deterministic, document-local move id, and states whether the block moved
> unchanged, moved with whitespace-only edits, or moved with other changes.
> `TextFindings.CreateAnalysisDiff` aligns lines whitespace-insensitively, so
> it also detects blocks that moved with whitespace-only edits, and it tells
> them apart from blocks re-indented in place.

`Inspector.Text` owns:

- the definition of a move and its id;
- the move facts issued beside the whitespace characterization;
- how moves take part in regions, changes, line facts, and the summary; and
- whitespace-insensitive alignment in `TextFindings.CreateAnalysisDiff`.

Supporting owners remain authoritative:

| Owner | Contract consumed |
| --- | --- |
| [Analysis diff](analysis-diff.md) | `Moved` placement on correspondences, consumed unchanged |
| `FindingMatcher` (`Inspector.Findings`) | Exact move detection: maximal blocks of at least two lines, contiguous on both sides |
| [Text whitespace characterization](text-whitespace-characterization.md) | Regions, changes, the whitespace-only predicate, edits, and presentation rules |
| Markout `MappedTextDiff` | Ordered, non-overlapping changes, adjacent changes allowed |

Relocation across members, such as reordered methods in a type, belongs to
cross-version member pairing and is out of scope. So are copies, splits, and
moves between files.

## Why

Today, a moved block renders as a removal in one place and an addition in
another, with nothing to connect them. A reviewer has to find the pair by eye.
If a relocated block was also re-indented, for example by being wrapped in a
new `if` at its destination, no move is detected at all, because the line
contents differ.

The matcher already detects exact moved blocks, and `AnalysisDiff<T>` already
carries `Moved` placement. What's missing is a typed identity that links the two
ends, plus whitespace-insensitive detection.

### Motivating assets

**Relocated blocks, PDB versus decompiled.** In Newtonsoft.Json 13.0.3,
`JsonConvert.ToString(object?)` (SourceLink commit
`0a2e291c0d9c0c7675d445703e51750363a549ef`, `Src/Newtonsoft.Json/JsonConvert.cs`)
gets its `switch` cases back in IL case-value order, not source order:

```bash
dnx dotnet-inspect -y -- member Newtonsoft.Json.JsonConvert \
  --package Newtonsoft.Json@13.0.3 "ToString:17" -S "Source Diff" -v:d
```

```diff
-                case PrimitiveTypeCode.String:
-                    return ToString((string)value);
                 case PrimitiveTypeCode.Char:
 ...
+                case PrimitiveTypeCode.Uri:
+                    return ToString((Uri)value);
+                case PrimitiveTypeCode.String:
+                    return ToString((string)value);
```

Three 2-line blocks (`String`, `Decimal`, and `Uri`) move unchanged, but
today they render as unrelated removals and additions.

**Re-indented in place, cross-version.** In Polly.Extensions 8.5.2 → 8.6.0,
`TelemetryListenerImpl.MeterEvent` (App-vNext/Polly
[`3fb0897`](https://github.com/App-vNext/Polly/commit/3fb089717fb63c52b51d66a378cdad08b3f6335f),
`src/Polly.Extensions/Telemetry/TelemetryListenerImpl.cs`) changes two guards
of the form `if (!X.Enabled) { return; }` followed by a block into
`if (X.Enabled) { block }`. The 5-line and 7-line blocks gain four spaces of
indentation and are otherwise byte-identical:

```bash
dnx dotnet-inspect -y -- diff --package Polly.Extensions@8.5.2..8.6.0 \
  -S "Implementation Diff" --pdb-source --all \
  -t Polly.Telemetry.TelemetryListenerImpl -m MeterEvent -v:d
```

git's `--color-moved-ws=allow-indentation-change` marks these blocks as moved.
Relative to the unchanged lines around them, though, they never move: they
are re-indented where they stand. This design therefore separates
*relocation* from *re-indentation*. A block that keeps its order in the
order-preserving alignment is a whitespace-only change under the whitespace
characterization, not a move. Only a block that the move pass relocates is a
move. The `diff --pdb-source` path uses `TextFindings.Compare`, which stays
exact until its follow-on, so the S1 fixture carries this evidence.

S1 retains both members as pinned-package evidence. The pathological cases
below become deterministic `Inspector.Text` fixtures.

## Moves

### Definition

A *moved run* is a maximal set of `Moved` correspondences whose Before lines
form one contiguous run and whose After lines form one contiguous run, with
the correspondences in the same order on both sides. A blank or
whitespace-only line inside a moved run belongs to the run, just as it would
inside any block of code.

A *move* is a moved run that contains at least one non-whitespace character.
A moved run made entirely of whitespace-only lines isn't a move: relocating
blank space is a whitespace difference, the whitespace characterization treats
it as one, and its placement is ignored here.

Each side of a move is its *end*. An end is contiguous and holds at least one
line. A `Moved` correspondence whose Before or After population isn't
contiguous is an argument failure.

### Move ids

Moves are numbered from 1 in the order in which their first end appears in the
change sequence of the whitespace characterization. When a removal-only change
and an addition-only change start at the same position, the removal comes
first, following the unified-diff convention. With that rule the sequence, and
therefore the numbering, is deterministic. A block that moved up has its
addition first, so its id is assigned at the addition.

Ids are local to one diff. They identify the move relation, not line content.
Every move id appears on exactly two changes.

### Move facts

Each move carries:

- its id;
- its Before line range and its After line range;
- a *content* fact from the pair characterization of the two ends' texts,
  where each end's text is its lines joined with single logical boundaries and
  has no leading or trailing boundary:

  | Content | Meaning |
  | --- | --- |
  | `Unchanged` | The ends' lines are ordinal-equal. |
  | `WhitespaceOnly` | The ends differ whitespace-only. The canonical edits and their kinds are issued, with coordinates on both ends' lines. |
  | `Changed` | The ends differ beyond whitespace. Only a producer that issues such correspondences reaches this; no current producer does. |

Terminator spelling inside a moved block isn't characterized, because logical
lines don't retain it.

## Moves within the whitespace characterization

Moves refine the whitespace characterization's regions and changes without
changing its region outcomes or document summary:

- **Regions.** A region that holds a move end is `Changed`, because movement
  is a real change. The whitespace characterization's preconditions and
  region rules apply unchanged, with "`Moved` endpoint" read as "move end".
- **Changes.** Each move end is exactly one change, with outcome `Moved`. It
  holds the end's lines on one side and no lines on the other. A change that
  isn't a `Moved` change holds no move end.
- **Maximality.** `Moved` changes are exempt from the rule that adjacent
  changes have different outcomes: each move end is always its own change,
  including next to another move end. The rule still applies among all other
  changes.
- **Line facts.** Each line in a `Moved` change takes the fact `Moved`, with
  its move id.
- **Change texts.** A `Moved` change's text is its cut of the region text, as
  for any change. The whitespace facts of a move come from the move's content
  fact, not from that cut.

The whitespace characterization's validator gains four checks:

- every move end is exactly one `Moved` change;
- every move id appears on exactly two changes, one on each side;
- the ids follow the stated order; and
- each move's content fact matches its ends' texts.

## Detection in `TextFindings`

`TextFindings.CreateAnalysisDiff` aligns lines by their *alignment key*: the
line's content with every whitespace character removed. Content facts still
use exact text:

- a correspondence whose lines are ordinal-equal and agree in whether they
  are terminated is `Unchanged`; a change in final-terminator presence stays
  `Changed`, as today; and
- one whose lines are equal only by alignment key is `Changed`, and so differs
  whitespace-only.

The matcher's existing passes then decide placement, with no extra rule:

- the order-preserving longest common subsequence gives `Stable` placement;
  and
- the move pass gives `Moved` placement: maximal blocks of at least two lines,
  contiguous on both sides, with ties resolved by length and then position.

A block re-indented where it stands, such as a block wrapped in a new `if`, is
therefore part of the longest common subsequence. It is `Stable` with
`Changed` content inside its region, so it is not a move. Isolating it as its
own `WhitespaceOnly` change is splitter quality in the whitespace
characterization. A block that relocates and is re-indented is found
by the move pass and becomes a move whose content is `WhitespaceOnly`.
Anchors, which are stable and `Unchanged`, remain lines with ordinal-equal
content, so the whitespace characterization's preconditions hold.

This changes `TextFindings.CreateAnalysisDiff` output, which its consumers
see: lines that differ only in whitespace now correspond one to one as
`Changed`, where before they fell into a shared replacement population. Line
statistics count these as changed rather than as removals and additions.

Other producers' `Moved` correspondences are consumed as issued.
`TextFindings.Compare`, which feeds the cross-version member source pair
through `TextAnalysisDiffPresentation.CreateAnalysisDiff`, keeps exact
alignment. Its adoption is tracked as a follow-on in #8435, because it changes
a `FindingComparison` contract that another path consumes.

## Presentation

A move lowers to two ordinary Markout changes, a removal and an addition, and
no line appears in both. Under Markout slice M1, each carries a typed label:

- the kind, `moved`;
- the move id;
- the direction, *to* on the removal and *from* on the addition;
- the other end's first line; and
- when the content is `WhitespaceOnly`, the whitespace kinds.

In a unified diff, the label follows the closing `@@`:

```diff
@@ -12,4 +12,0 @@ moved (1) to +40
-    var cache = new Cache();
-    cache.Warm();
-    Log("warmed");
-    Flush();
@@ -44,0 +40,4 @@ moved (1) from -12; whitespace-only: indentation
+        var cache = new Cache();
+        cache.Warm();
+        Log("warmed");
+        Flush();
```

The id makes moves countable and greppable: both ends share one token. The
summary reports moved blocks as well as moved lines, for example
`Moved: 2 blocks (6 → 6 lines)`. A host that shows only one end, such as a
suppressed or filtered view, still reports the move in the summary.

In Inspect Web, the typed link lets the viewer jump between the two ends. No
host uses color as the only cue for a move.

## Pathological demonstration

Each row becomes an S1 Release test in `tests/Inspector.Text.Tests`, using
`TextFindings.CreateAnalysisDiff` unless the row says otherwise.

| Case | Before → After | Expected |
| --- | --- | --- |
| Block moved down | `A B x y z` → `x y z A B` (one line per letter) | one move, id 1, `A B` from Before 0–1 to After 3–4, `Unchanged`; the removal comes first |
| Block moved up | `x y z A B` → `A B x y z` | one move, id 1, assigned at the addition, which comes first |
| Relocated and re-indented | `A B x y z` → `x y z if (c) {` / `␠␠A` / `␠␠B` / `}` | one move, `WhitespaceOnly`, `Indentation`; the `if` and brace lines are ordinary `Changed` changes |
| Re-indented in place (Polly `MeterEvent`) | guard `if (!e) {` / `return;` / `}` / `A` / `B` → `if (e) {` / `␠␠A` / `␠␠B` / `}` | no move: `A B` is the longest common subsequence by alignment key, so it is `Stable` with `Changed` content in a `Changed` region; its own `WhitespaceOnly` change is a quality expectation |
| Relocated case blocks (Newtonsoft `JsonConvert.ToString`) | `case String` pair moved after the other cases | a move, `Unchanged`, with ids in change-sequence order for each relocated pair |
| Moved and edited | `A` / `B` moved, and `B` became `B2` | no move under `TextFindings`; ordinary removal and addition |
| Single-line move | `A x y` → `x y A` | no move (below the two-line minimum) |
| Blank lines moved | two blank lines relocated | no move; whitespace-only regions |
| Two moves | two separate blocks relocated | ids 1 and 2 in change-sequence order, each on exactly two changes |
| Swapped blocks | `A B C D` → `C D A B` | one move (the exact pass keeps one block as matched); id 1 |
| Re-indented swap | `x A B C D y` → `x ␠␠C ␠␠D ␠␠A ␠␠B y` | one block `Stable` (by the LCS tie rule) and a `WhitespaceOnly` change; the other block one move, `WhitespaceOnly` |
| Interior blank line | `A ␣ B x y z` → `x y z A ␣ B` | one move of three lines, id 1 |
| Id tie at one position | `A B x1 x2 x3 C D` → `C D x1 x2 x3 A B` | two moves; `A B` is id 1 because its removal precedes the addition of `C D` at the same position |
| Adjacent ends of different moves | two moved blocks leave adjacent Before positions | two adjacent `Moved` changes with different ids |
| Ambiguous source | two identical `A B` blocks, one moves | deterministic choice of source, by the pass's tie rule |
| Braces only | a block of `}` / `}` lines relocated | a move (it has non-whitespace characters); reviewers see it labeled |

## Non-claims

This design does not define:

- moves across members, types, or files, or member reordering;
- copies, or a block split into several destinations;
- single-line moves;
- moves with content changes beyond whitespace in `TextFindings`;
- whitespace-insensitive move detection in `TextFindings.Compare` (a tracked
  follow-on); or
- host rendering beyond the label content and the summary counts.

## Production adoption

The whitespace plan in #8393 carries moves along its existing steps:

| Step | Owner | Moves adds |
| --- | --- | --- |
| S1 | `Inspector.Text` | Move facts, ids, validator checks, and whitespace-insensitive alignment in `TextFindings.CreateAnalysisDiff` |
| M1 | Markout | The typed `moved` label (id, direction, other end, whitespace kinds) beside the whitespace label |
| S2 | `DotnetInspector.Presentation` + CLI | Lowering moves in member Source Diff; the summary's moved-block count |
| S3 | `ILInspector.Research` + CLI | The same for `diff --pdb-source` |
| S4, S5 | Inspect Web | Transport of move facts; jump between ends |

The CLI is first reached at S2, three steps after this design. Inspect Web is
reached at S5. The `TextFindings.Compare` follow-on is independent of those
steps.
