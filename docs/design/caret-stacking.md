# Caret stacking

This document specifies **caret stacking**: rendering one caret per *fact
extent* on a line, each keeping the width of the expression it is about,
instead of widening to a single statement-length caret whenever the facts on a
line disagree.

It is the display half of the `--focus` gesture. The analysis half — deciding
which characters a fact is about — is described in
[hidden-fact-annotations.md](hidden-fact-annotations.md) and implemented by
`AnnotationAnchor.ComputeCaretExtents`. **Stacking changes no analysis.** The
per-fact extents already exist and are already correct; today's renderer
discards them.

## The problem

`AnnotationCaret.Agreed` returns an extent only when every fact on the line
points at the *same* characters. When they disagree it returns `null` and the
caret widens to the whole statement. On a line carrying several facts about
several different sub-expressions, that produces a caret that points at
everything and therefore at nothing, above a stack of details with no way to
tell which detail belongs to which expression.

`System.Tuple<…>.Equals` is the worst real case in CoreLib — 16 facts, one
330-column caret:

```csharp
return comparer.Equals(m_Item1, V_0.m_Item1) && comparer.Equals(m_Item2, V_0.m_Item2) && … (330 cols)
//  ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ (330 carets)
//   alloc.box(T1; …)   ×16, unattributed
```

The facts are right. The anchoring is right. Only the render throws the
information away.

## One style, specified

Caret layout will eventually be a **style** dimension — a terminal, a wide
editor, and rendered Markdown do not want the same picture. This document does
not try to design that dimension. It locks **one** style, the one that has to
work everywhere, and makes it the only behaviour: **`stacked`**.

Anything else is a later style added beside it. Nothing here is contingent on
those styles existing, and nothing here should be generalised in anticipation
of them.

```csharp
    if (!V_0.IsMemberRef && !V_0.IsMethodDef && !V_0.IsTypeSpec && !V_0.IsSignature && !V_0.IsFieldDef)
//     1.^^^^^^^^^^^^^^^   2.^^^^^^^^^^^^^^^   3.^^^^^^^^^^^^^^   4.^^^^^^^^^^^^^^^   5.^^^^^^^^^^^^^^
//  1. cost.callee(callee get_IsMemberRef: reflection)
//  2. cost.callee(callee get_IsMethodDef: reflection)
//  3. cost.callee(callee get_IsTypeSpec: reflection)
//  4. cost.callee(callee get_IsSignature: reflection)
//  5. cost.callee(callee get_IsFieldDef: reflection)
```

### Specification

1. **Group facts by extent.** The unit is the extent, not the fact. Facts about
   the same characters share one caret and one number, their texts listed
   together under it. `Tuple.Equals` renders 8 carets for its 16 facts.
2. **Order by start column, widest first at a tie.** Extents sharing a start
   column are always a nesting, so they cannot share a row; widest-first makes
   each row narrower than the one above, and matches the order the printer
   records nesting in.
3. **Number in that order,** `1.` upward, and label each caret with its number
   immediately before the trail.
4. **Pack greedily onto rows.** An extent joins the first row where the
   previous caret on that row can still render `^~` — or its full width, if
   that is shorter than two — followed by one blank column, before this
   caret's label begins. Otherwise it opens a new row.
5. **Render each trail at true width,** clipped only where it would collide
   with the next label on its row, in which case the last column becomes `~`.
   Width is information the anchoring layer worked to compute, so the renderer
   states it truthfully or marks that it could not.
6. **List the facts below,** left-aligned in the existing gutter, numbered to
   match, wrapped to `Budget`.
7. **A fact with no extent is listed, not drawn.** `AnnotationAnchor` withholds
   an extent when it cannot identify the characters a fact is about. Such a
   fact keeps its place in the list below the carets, marked `-` instead of a
   number. A line may carry both kinds at once — a **mixed** line — and
   stacking still engages: the placeable facts get numbered carets, the rest
   are listed under `-`.

Rule 4's two-column threshold is a *spill* threshold, not a render floor: a
genuinely one-character expression renders one caret, never a padded two.
`HebrewCalendar.CheckHebrewYearValue` is the case that proves it — caret `2.`
is the single argument `y`, and the mix of clipped and full trails on one row
is what the rules produce:

```csharp
    throw new ArgumentOutOfRangeException(varName, y, SR.Format(SR.ArgumentOutOfRange_Range, 5343, 5999));
//          1.^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^~ 2.^                                       3.^^~ 4.^^^^
```

And rule 2, on a line with two nested pairs:

```csharp
        return LessThan<uint>(Abs<T>(vector).As<T, uint>() - Vector<uint>.One, Create<uint>(8388607)).As<uint, T>();
//             1.^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
//             2.^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
//                            3.^^^^^^^^^^^^^^^^^^^^^^^^^^^^                   5.^^^^^^^^^^^^^^^^^^^^^
//                            4.^^^^^^^^^^^^^^
```

### Why this is the style that has to work

The primary consumer is a terminal. A layout wider than the terminal wraps, and
a wrapped caret row lands underneath characters it has nothing to do with —
worse than no caret, because it is actively misleading.

Every caret lies within the code line and a row cannot extend past its last
caret, so **a stacked caret row never adds width the code line did not already
have.** Measured on all 3,385 lines this model changes: 0 have a stacked caret
row wider than their code line, against 137 (4.05%) under the widening render
it replaces.

Widths below are **final rendered columns**, measured after the same projection
`ApiOutputFormatter` applies — code lines receive `BodyIndentWidth`, hoisted
caret lines do not — and the caret block is real rendered output, detail rows
included. Whole rendered block on those 3,385 lines:

| terminal | widen (today) | stacked |
| --- | ---: | ---: |
| 80 cols | 27.8% | **28.0%** |
| 100 cols | 53.3% | 53.3% |
| 120 cols | 69.5% | 69.5% |
| 160 cols | 86.5% | 86.5% |

Terminal fit is unchanged, because on these dense lines the block width is set
by the wrapped detail rows, which both models share. What changes is the block:
91.0% narrower, 0.4% the same, 8.6% wider. So stacking buys attribution at no
cost in fit — it does not improve fit, and this document should not claim it
does.

The constraint still binds on the *alternatives*, which is why it is stated
here: side-alignment overflows the code line on 97.28% of caret-bearing lines
by a mean of 81 columns, and fits 25.4% of them at 80 columns against 76.5% for
the bare code.

### Mixed lines

A **mixed** line carries facts of both kinds: some with an extent, some
without. There are 615 of them in CoreLib, and 499 have exactly one surviving
extent, so this is not a corner.

Today they widen. The reasoning was sound while a caret was the only signal
available: narrowing to the one surviving extent would underline an expression
true of only *some* of the facts sharing the caret. Rule 7 removes the
ambiguity that argument rests on — an extent-less fact is now visibly marked
`-` rather than silently sharing an underline — so the surviving extent is
drawn.

The dominant shape, and the reason this matters, is `stackalloc`:

```csharp
        Span<char> V_0 = stackalloc char[256];
    //  ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
    //   lifetime.stack-bound(Span<char>)
    //   unsafe.stackalloc(byte*)
```

Both facts are about `stackalloc char[256]`. Widening underlines
`Span<char> V_0 =` as well, which neither fact claims. Under rule 7:

```csharp
        Span<char> V_0 = stackalloc char[256];
    //                 1.^^^^^^^^^^^^^^^^^^^^
    //  1. lifetime.stack-bound(Span<char>)
    //  -  unsafe.stackalloc(byte*)
```

The `-` is doing real work here: it says the second fact has no characters to
point at, which is a different claim from "it is about the whole statement".

Stacking needs at least one placeable fact. A line where *no* fact has an
extent still has nothing to point at, so it widens exactly as before.

- Lines whose facts all agree on one extent, and lines carrying a single fact —
  **90.1%** of caret-bearing lines — keep exactly today's geometry, including
  the inline-detail shortcut.
- A fact whose expression has no printed node, or prints on a continuation
  line, still gets no extent. What changes is only how it is *shown* on a line
  where some other fact does have one: it is marked `-` instead of widening the
  caret. Lines where no fact has an extent widen exactly as today.
- Detail text stays left-aligned and wrapped to `Budget`.
- `AnnotationAnchor` is untouched. This is a change to `AnnotationCaret` alone.

## Measurements

CoreLib `11.0.0-preview.7.26366.102`, measured on the **annotated-source**
render (`importMethodBody: ImportMethodBody`) — the one `--focus` produces.
Extents are measured in printed characters, so a figure that does not name its
render is not a claim about anything.

| | |
| --- | ---: |
| caret lines carrying at least one extent | 29,013 |
| …with a single distinct extent | 26,127 (90.1%) |
| …with two or more distinct extents | 2,886 (9.9%) |
| worst line | 8 distinct extents |

Of the single-extent lines, all but 499 carry no extent-less fact and so keep
today's geometry exactly. The 499 are the mixed lines of rule 7, which gain a
drawn caret and a `-` row where they previously widened:

| | |
| --- | ---: |
| mixed lines (facts both with and without an extent) | 615 |
| …with exactly one surviving extent | 499 |
| …with two or more | 116 |

Applying the specification above to those 2,886 multi-extent lines:

| rows | lines | |
| --- | ---: | ---: |
| 1 | 2,557 | 88.6% |
| 2 | 326 | 11.3% |
| 3 | 2 | 0.1% |
| 4 | 1 | 0.0% |

3,949 of 6,986 trails (56.5%) render at true width, and 527 lines (18.3%) have
no clipped trail at all. Clipping concentrates exactly where extents nest.

## Rejected alternatives

Each was mocked up against real CoreLib lines before being rejected. Several
are plausible *styles* for later; none can be the one style.

### Widen on disagreement (today)

The caret points at the whole statement, so it carries no information, and the
details below it are unattributable. This is the defect.

### Side-aligned annotations

One caret per row with its text beside it. Genuinely attractive — association
becomes spatial, so numbering disappears, and with one caret per row there is
no neighbour to collide with, so clipping disappears too. A single-extent line
collapses to one row.

```csharp
    Span<char> V_0 = stackalloc char[256];
//                   ^^^^^^^^^^^^^^^^^^^^  lifetime.stack-bound(Span<char>)
```

Rejected as *the* style on measurement, not taste: aligning after the widest
caret pushes text a mean **81 columns** past the end of the code line and
overflows it on **97.28%** of caret lines, leaving 25.4% intact at 80 columns.
Its annotation column exceeds 100 on 10.5% of lines, with a maximum of 57,942
on `IcuLocaleData.get_NameIndexToNumericData` — the pathological line already
filed as #3610. It wraps three lines in four, and the wrap destroys the very
adjacency that motivates it. The obvious first candidate for a wide style.

### Constant-width caret trails

Render every caret at a fixed width so packing depends only on start columns.
Compact — a 4-wide trail fits 87.5% of multi-extent lines on one row.

Rejected because it **misstates width**. A 4-wide trail under the single
character `y` in `ArgumentOutOfRangeException(varName, y, …)` claims a
four-character expression that does not exist.

### Annotations column-aligned under their own caret

Indent each fact's text to start under the caret it explains.

Rejected for side-alignment's reason: on a line whose facts sit at columns 12
through 87, the texts stagger across the width of the line and each then wraps.
Left-aligning the numbered list keeps every fact starting in the same column,
where wrapping is predictable and cheap.

### Interleaved ladder with left-aligned details

One caret row per extent, each followed immediately by its detail lines.

Rejected: `alloc.box` details run past 200 characters, so consecutive carets end
up separated by paragraphs, destroying the side-by-side comparison that makes a
multi-fact line legible in the first place.
