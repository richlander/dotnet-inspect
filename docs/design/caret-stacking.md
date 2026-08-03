# Caret stacking

This document specifies **caret stacking**: rendering one caret per *fact
extent* on a line, each keeping the width of the expression it is about,
instead of widening to a single statement-length caret whenever the facts on a
line disagree.

It is the display half of the `--focus` gesture. The analysis half — deciding
which characters a fact is about — is described in
[hidden-fact-annotations.md](hidden-fact-annotations.md) and implemented by
`AnnotationAnchor.ComputeCaretExtents`. **Stacking changes no analysis.** The
per-fact extents already exist; today's renderer discards them.

That separation held for the stacking change itself and no longer describes the
current state of the anchor. [#3674](https://github.com/richlander/dotnet-inspect/pull/3674)
went on to fix two anchoring defects — a fact whose own node prints nothing now
adopts an extent from its nearest printed descendant, which gave 1,841 more
facts an extent — and every figure in this document was re-measured against it.
The design below is unchanged by that; only its numbers moved.

Every figure in this document comes from the corpus named under
[Measurements](#measurements): `System.Private.CoreLib`
`11.0.0-preview.7.26366.102`, rendered by the annotated-source view. Extents
are measured in printed characters, so the render is part of the figure.

## The problem

`AnnotationCaret.Agreed` returns an extent only when every fact on the line
points at the *same* characters. When they disagree it returns `null` and the
caret widens to the whole statement. On a line carrying several facts about
several different sub-expressions, that produces a caret that points at
everything and therefore at nothing, above a stack of details with no way to
tell which detail belongs to which expression.

`System.Tuple<…>.Equals` is the worst real case in the corpus. Under
`--focus alloc` its comparison line carries 16 facts under a single caret 370
columns wide, above 32 wrapped detail rows, and attributes none of them. The
code line is 370 columns, reaching final column 374 after the body indent, so it
is elided here at the `…`:

```csharp
    return comparer.Equals(m_Item1, objTuple.m_Item1) && comparer.Equals(m_Item2, … (370 cols)
//  ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ (370 carets)
//   alloc.box(T1; alloc=boxed T1; path=branch; path-confidence=behind-branch; …  ×16, unattributed
```

Under the model specified below the same line renders 8 numbered carets on a
single row, with 8 of the 16 facts attributed to the expression each is about
and the other 8 marked `-`. The block does not grow: both renders are one caret
row above the same 32 detail rows, and the stacked caret row ends at column 372
rather than 374.

> This width has now been mis-stated in two directions across review rounds —
> recorded as 370, "corrected" to 330, and measured back to 370. It is 370. The
> value was re-derived from the render itself rather than from agreement with
> the rest of this document; internal consistency was what let the wrong value
> survive. Re-measure before changing it again.

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

Every code block showing behaviour that exists is verbatim `Annotated Source`
output rather than a sketch. The two exceptions are marked where they appear:
the `Tuple.Equals` line below is elided because it is 370 columns wide, and the
side-aligned block under [Rejected alternatives](#side-aligned-annotations)
depicts a layout that was never built. This one is
`System.Reflection.RuntimeModule.ResolveSignature` under `--focus cost`; see
[Reproducing these figures](#reproducing-these-figures).

```csharp
    if (!tk.IsMemberRef && !tk.IsMethodDef && !tk.IsTypeSpec && !tk.IsSignature && !tk.IsFieldDef)
//     1.^^^^^^^^^^^^^^   2.^^^^^^^^^^^^^^   3.^^^^^^^^^^^^^   4.^^^^^^^^^^^^^^   5.^^^^^^^^^^^^^
//  1. cost.callee(callee get_IsMemberRef: reflection)
//  2. cost.callee(callee get_IsMethodDef: reflection)
//  3. cost.callee(callee get_IsTypeSpec: reflection)
//  4. cost.callee(callee get_IsSignature: reflection)
//  5. cost.callee(callee get_IsFieldDef: reflection)
```

### Specification

1. **Group facts by extent.** The unit is the extent, not the fact. Facts about
   the same characters share one caret and one number, their texts listed
   together under it. Under `--focus alloc`, `Tuple.Equals` renders 8 carets
   for its 16 facts.
2. **Order by start column, widest first at a tie.** Extents sharing a start
   column are always a nesting, so they cannot share a row; widest-first puts
   the outer one on the upper row, so reading down the rows moves inward. That
   is a presentation choice, and it is the *reverse* of the order the printer
   records nesting in — `RecordExpressionRanges` reverses its parent-first walk
   precisely so a node is recorded after every one of its descendants, the
   descendants-before-ancestors contract the anchor depends on. Nothing here
   inherits that order.
   This orders *same-start* extents only, and it is not what causes
   clipping — a trail is only ever cut short by the next label **on its own
   row** (rule 5), and a nested extent that lands on a different row is not on
   its parent's row to cut anything. Nor does it order row widths overall: a
   later disjoint extent can be packed onto a lower row and reach further right
   than anything above it, which happens on **50 of the 3,169** lines.
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
   match, wrapped to `Budget`. One row per fact, in input order within an
   extent group; the number is written once, on the group's first fact, and the
   groups appear in the rule 2 order the carets were numbered in. Rows for
   facts with no extent are appended after all numbered rows.
7. **A fact with no extent is listed, not drawn.** `AnnotationAnchor` withholds
   an extent when it cannot identify the characters a fact is about. Such a
   fact keeps its place in the list below the carets, marked `-` instead of a
   number. A line may carry both kinds at once — a **mixed** line — and
   stacking still engages: the placeable facts get numbered carets, the rest
   are listed under `-`. This holds only when the line actually stacks; if
   rule 8 rejects the layout the line widens and *no* fact on it is marked.
8. **The gutter wins.** A label is drawn immediately before the characters it
   points at, so a caret near the start of a line can push its label left into
   the comment gutter. Moving the label would make it point somewhere else, so
   when any label on a line would begin left of the gutter, that line does not
   stack at all: it falls back to the widening render this document replaces.
   This is a guard rather than a path with traffic — it fires on **0 of the
   3,169 lines that qualify to stack** in the corpus below, which is the
   non-circular denominator, since a line it rejects does not stack by
   construction — but it is reachable, and a
   line is never rendered with a label that lies about its column.

Rule 4's two-column threshold is a *spill* threshold, not a render floor: a
genuinely one-character expression renders one caret, never a padded two.
`System.Globalization.HebrewCalendar.CheckHebrewYearValue` under `--focus
alloc` is the case that proves it — caret `2.` is the single argument `y`, and
the mix of clipped and full trails on one row is what the rules produce (detail
rows omitted here; the carets are verbatim):

```csharp
        throw new ArgumentOutOfRangeException(varName, y, SR.Format(SR.ArgumentOutOfRange_Range, 5343, 5999));
//          1.^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^~ 2.^                                       3.^^~ 4.^^^^
```

And rule 2, on a line with two nested pairs —
`System.Numerics.Vector.IsSubnormal` under `--focus safety`:

```csharp
        return LessThan<uint>(Abs<T>(vector).As<T, uint>() - Vector<uint>.One, Create<uint>(8388607)).As<uint, T>();
//           1.^^^^^^^^^^^~ 3.^^^^^^^^^^^^^^^^^^^^^^^^^^^^                   5.^^^^^^^^^^^^^^^^^^^^^
//           2.^^^^^^^^^^^~ 4.^^^^^^^^^^^^^^
```

Carets `1.` and `2.` share a start column, as do `3.` and `4.`, so neither pair
can share a row. The outer member of each pair is drawn first and the row below
carries the inner one, which is why the second row is the shorter of the two.
Both members of the first pair clip to `~`: `3.`'s label is the next thing on
row one, and rule 4 packs `4.` onto row two directly beneath it.

### Why this is the style that has to work

The primary consumer is a terminal. A layout wider than the terminal wraps, and
a wrapped caret row lands underneath characters it has nothing to do with —
worse than no caret, because it is actively misleading.

**A stacked caret row never adds width the code line did not already have.**
That is a structural guarantee rather than a lucky corpus. `Stack` sends any
extent with `Column + Length > lineLength` to the unplaced list, so every caret
it does place ends within the line; labels are grown leftward from the column
they point at, and rule 8 refuses to shift a trail rightward to make room,
bailing out instead. The rightmost column of a stacked row is therefore bounded
by the code line by construction.

The corpus agrees — 0 of the 3,169 lines carry a stacked caret row wider than
their code line — but that count is a consistency check on the derivation above,
not evidence for it, because no corpus could produce a counter-example.

The comparison against the widening render is narrower than two earlier
revisions of this section claimed, and the correction is worth stating plainly.
**No caret glyph overhangs the code line in either render.** Measured over the
same 3,169 lines, both are **0**, because the widening underline covers the
trimmed statement, which ends inside the line. The widening render has no
explicit bound, but it does not need one to stay within the line.

What differs is the rendered **row**. Widening appends the first detail string
to the caret row when it fits the inline budget, and that appended text carries
the row past the end of the code line. Quoting this as **20 of 3,169 lines
(0.63%)** pads the denominator with the 3,149 whose detail never goes inline and
which therefore cannot overhang at all. Inline detail appears on exactly **20**
of these lines, and **all 20** overhang — the rate is **20/20**. It comes out
whole because on the hoisted render these figures are taken from, widening
underlines the whole trimmed statement, so the caret ends where the code line
ends and any appended text lands past it. That is not a universal guarantee and
is not claimed as one: a fact whose formatted text is empty appends nothing, and
an un-hoisted render can clamp `pad` to its floor of 1 and push the caret past
the code with no detail appended at all. Stacking never appends detail to a caret row, so it is 0. The contrast is about where detail is
placed, not about bounding the underline — and the earlier claim that the
widening underline "shifts rightward while preserving the full extent length"
described a mechanism that does not occur.

Widths below are **final rendered columns**, measured after the same projection
`ApiOutputFormatter` applies — code lines receive `BodyIndentWidth`, hoisted
caret lines do not — and the caret block is real rendered output, detail rows
included. Whole rendered block on those 3,169 lines:

| terminal | widen (today) | stacked |
| --- | ---: | ---: |
| 80 cols | 20.9% | 21.0% |
| 100 cols | 52.6% | 52.6% |
| 120 cols | 68.7% | 68.7% |
| 160 cols | 86.5% | 86.5% |

That denominator is padded, and the padding is most of it: the block can never
be narrower than the code line, so a line whose code alone overflows the
terminal cannot fit under *any* caret model. At 80 columns only 954 of the 3,169
have code that fits at all — the other 2,215 are structurally incapable of the
outcome being counted. Restricted to the 954 that could fit, the rates are 69.3%
widening and 69.7% stacked. At the wider terminals the padded rate and the
code-fits share converge almost exactly (100 cols: 52.6% fit, 52.7% code-fits;
120: 68.7% / 68.8%; 160: 86.5% / 86.5%), which says the block width simply *is*
the code width there.

Terminal fit is unchanged, because on these dense lines the block width is set
by the code line and the wrapped detail rows, which both models share. Nor does
the block itself shrink much: it is the same width on 83.4% of these lines,
narrower on 1.3%, and wider on 15.3% — the numbered labels cost a few columns
where the details were already the widest thing in the block.

Those three shares are all padded, and quoting any of them as a rate invites the
next correction. A block is never narrower than its code line, so a block already
*at* the code width can only stay or grow. Splitting on that floor says far more
than the percentages did:

| population | unchanged | narrower | wider |
| --- | ---: | ---: | ---: |
| **2,648** pinned at the code width | 2,643 | — | 5 |
| **521** with headroom above it | 0 | 40 | 481 |

Nothing happens on the pinned 2,648: five blocks grow and the rest are untouched.
Every one of the 521 with headroom changes, and 481 of them get wider. So the
aggregate "83.4% unchanged" is almost entirely the pinned population, and the
real behaviour is confined to the 521. Growth turns out to be floor-limited too
— 5 of 2,648 — so the earlier claim that "any block can grow", used to argue the
shares were comparable, was wrong as well.

**Stacking buys attribution, and costs a little width.** Fit does not move:
unpadded, the 80-column rate goes from 69.3% to 69.7% on the 954 lines that could
fit. Block width moves only on the 521 lines that have room to move, and it moves
the wrong way far more often than the right one — 481 wider against 40 narrower.
Stated as counts within one population that is a clear result, and it is not the
one the percentages suggested: the earlier text called the narrowing rounding
noise and set 1.1% against 7.2% on an earlier corpus, which compared two shares of a denominator
dominated by lines that could do neither.

The constraint still binds on the *alternatives*, which is why it is stated
here: side-alignment overflows the code line on 97.63% of the 31,320 lines
carrying an extent, by a mean of 85 columns over the 30,577 that overflow, and
fits 23.6% of them at 80 columns against 76.4% for the bare code. That 23.6% is
padded — a line whose bare code already overflows 80 columns cannot fit
side-aligned either. Conditioned on the 23,937 whose bare code does fit,
side-alignment still fits only **30.4%**. Unpadding it makes the rejected
alternative look better than the headline did, which is why it is stated: the
rejection rests on 30.4% against 76.4%, not on the padded figure. See
[Side-aligned annotations](#side-aligned-annotations) for the measurement
convention these depend on.

### Mixed lines

A **mixed** line carries facts of both kinds: some with an extent, some
without. Summed over the five focus families there are 356 of them, and 255
have exactly one surviving extent, so this is not a corner. They are not spread
evenly: 299 fall under `--focus alloc`, 47 under `safety`, 10 under `cost`, and
none at all under `unsafe` or `lifetime`.

Today they widen. The reasoning was sound while a caret was the only signal
available: narrowing to the one surviving extent would underline an expression
true of only *some* of the facts sharing the caret. Rule 7 removes the
ambiguity that argument rests on — an extent-less fact is now visibly marked
`-` rather than silently sharing an underline — so the surviving extent is
drawn.

A throw helper in `System.MemoryExtensions` is the shape this matters for.
Under `--focus alloc` it renders today as:

```csharp
    throw new ArgumentNullException((lowInclusive) is null ? "lowInclusive" : "highInclusive");
//  ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
//   alloc.box(T; alloc=boxed T; path=straight-line)
//   alloc.new(ArgumentNullException; alloc=System.ArgumentNullException; path=error-path;
//   escape=throw-path; multiplicity=conditional)
```

The `alloc.new` is about `new ArgumentNullException(…)` and carries an extent.
The `alloc.box` is about the boxing of `lowInclusive` for the `is null` test,
and `AnnotationAnchor` does not place it. Widening underlines the `throw`
keyword as well, which neither fact claims, and gives the reader no way to tell
that only one of the two facts was ever located. The `-` is a statement about
*this pipeline's* knowledge, not about the fact: it says no characters were
identified, which is weaker than and different from a claim that the fact
covers the whole statement. Under rule 7:

```csharp
    throw new ArgumentNullException((lowInclusive) is null ? "lowInclusive" : "highInclusive");
//      1.^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
//  1. alloc.new(ArgumentNullException; alloc=System.ArgumentNullException; path=error-path;
//     escape=throw-path; multiplicity=conditional)
//  -  alloc.box(T; alloc=boxed T; path=straight-line)
```

The `-` is doing real work here: it says the second fact has no characters to
point at, which is a different claim from "it is about the whole statement".

Stacking needs at least one placeable fact. A line where *no* fact has an
extent still has nothing to point at, so it widens exactly as before.

- Lines whose focused facts all agree on one extent, and lines carrying a
  single focused fact, keep exactly today's geometry, including the
  inline-detail shortcut. That is **28,151 lines, 89.9%** of those carrying an
  extent. Adding the **1,544** lines where no fact has an extent, which also
  render exactly as they do today, **29,695 of 32,864 caret lines (90.4%) are
  untouched** and 3,169 (9.6%) change; see [Measurements](#measurements) for how
  that is counted and why the focus family has to be named before the number
  means anything. That denominator is the whole shipped population on purpose —
  the claim is how much existing output this changes, not a rate of some
  outcome among the cases capable of it. Read the other way it is close to a
  mechanism: 28,151 + 1,544 is exactly 29,695, so the 3,169 is precisely the
  complement, and **every line that can stack does**. The selector is
  `Stack(...) is { Count: > 0 }`, not `Agreed`: `Agreed` returns null on
  **4,713** lines, but 1,544 of those have no extent at all and so render
  unchanged, leaving exactly the 3,169. The remaining gap is `Agreed`'s final
  bounds check, which could in principle reject a line that agrees; it rejects
  **0** on this corpus, so the identity is exact here by measurement rather
  than guaranteed by construction.
- A fact whose expression has no printed node, or prints on a continuation
  line, still gets no extent. What changes is only how it is *shown* on a line
  where some other fact does have one, and only when that line stacks: it is
  marked `-` instead of widening the
  caret. Lines where no fact has an extent widen exactly as today, as do lines
  rule 8 rejects.
- Detail text stays left-aligned and wrapped to `Budget`.
- The stacking change itself is confined to `AnnotationCaret`; it reads extents
  and does not compute them. The follow-up in
  [#3674](https://github.com/richlander/dotnet-inspect/pull/3674) does change
  `AnnotationAnchor`, which is why the figures here were re-measured, but it
  changes no rule in this specification.

## Measurements

CoreLib `11.0.0-preview.7.26366.102`, measured on the **annotated-source**
render — the one `--focus` produces.

Two things decide what these figures mean, and both were got wrong in an
earlier draft of this document:

- **The focus filter comes first.** `--focus` promotes only the facts of the
  requested family to carets; everything else stays a side comment and never
  reaches `AnnotationCaret`. Counting every collected fact describes a render
  no invocation produces, and inflates every figure here. Each line below is
  counted **after** the filter, once per family.
- **A user asks for one family at a time,** so there is no single corpus-wide
  population. The totals are sums over the five families, and a line carrying
  both an `alloc` and a `safety` fact is counted once under each — because that
  is two different renders, and it is the render that is being measured.

Those five families cover every argument that can widen this table. They
**partition** the corpus: `alloc`, `unsafe`, `lifetime`, `cost` and `safety`
select 16,328 + 749 + 928 + 2,820 + 17,041 facts, which is exactly the 37,866
facts CoreLib yields, with no fact in two families and none outside them.
`AnnotationGestureSelector.Focus` also matches a category name, and those are
aliases of the same sets — `allocation` selects the same 16,328 as `alloc`,
`unsafety` the same 749 as `unsafe`, and `semantics` the same 17,041 as
`safety`. That last one is a coincidence of this corpus rather than a
definition: the `Semantics` category declares both `safety.callee` and
`semantics.callee`, and `--focus semantics` would select the union of the two —
but CoreLib yields **0** `semantics.callee` facts, so the two selections are
equal here. On an assembly that produced them, `semantics` would be a strict
superset of `safety` and would need measuring separately.

Any other argument is a narrower id prefix, such as `alloc.box`, and selects a
subset of one family — not necessarily a *strict* one, since `safety.callee` is
the only `safety.*` descriptor CoreLib produces and so selects exactly what
`safety` does. Dropping facts can only remove distinct extents and
remove extent-less facts, and a line qualifies to stack only when it has two of
the former or one of each — so the *qualifying* set shrinks under a subset.

That is not by itself enough to make the **stacks** column an upper bound,
because rule 8 is not monotone. `RenderStacked` returns `null` for the whole
line the moment any one label would start left of the gutter, so a broad focus
can fall back to widening on account of a single early fact while a narrower
focus, having dropped that fact, stacks the two later extents successfully.
Naming the guard's rate against the lines that *stack* would be circular, since
a line rule 8 rejects does not stack by construction. The measurement is against
the set that **qualifies** to stack before rule 8 runs, and a fallback is
detected by the shape of the row: a stacked row always carries an `N.` label
immediately before its caret run, and the widening row never does. Detecting it
as "the block has no caret row" would be vacuous, because a fallback still
renders the widening caret.

So counted, the guard fires on **0 of 3,169** qualifying lines: all 3,169 do
stack. That the gate can report a non-zero was checked by mutation — raising the
guard's margin from `commentColumn + 2` to `+ 6` makes it fire on 306 of the
3,169, and to `+ 200` on all 3,169. The zero is a property of the corpus, not of
the probe. It is still a measurement rather than a consequence of subsetting,
and it would have to be re-measured on another assembly.

The bound is also specific to the **stacks** column. The other columns are not
monotone in the same direction: dropping one of two disagreeing extents turns a
multi-extent line into a single-extent line, so `single extent` can *rise* under
a narrower focus even as `multi-extent` and `stacks` fall. Read the table as
what the five documented arguments produce, and the 3,169 as the corpus-measured
ceiling on the rest.

Extents are measured in printed characters, so a figure that does not name its
render is not a claim about anything.

| focus | caret lines | …with an extent | single extent | multi-extent | mixed | stacks |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `alloc` | 14,789 | 14,123 | 13,420 | 703 | 299 | 921 |
| `safety` | 13,908 | 13,487 | 11,459 | 2,028 | 47 | 2,057 |
| `cost` | 2,516 | 2,445 | 2,278 | 167 | 10 | 175 |
| `lifetime` | 928 | 800 | 800 | 0 | 0 | 0 |
| `unsafe` | 723 | 465 | 449 | 16 | 0 | 16 |
| **total** | **32,864** | **31,320** | **28,406** | **2,914** | **356** | **3,169** |

A line stacks when its focused facts disagree about the extent, or when some
carry one and some do not: 2,914 multi-extent lines plus the 255 mixed lines
with a single surviving extent gives **3,169**. Everything else — **29,695
lines, 90.4%** of all 32,864 caret lines — keeps exactly today's geometry,
including the inline-detail shortcut. Two populations make up that remainder and
they keep it for different reasons: **28,151** lines narrow to one agreed extent,
and **1,544** lines have no fact with an extent at all and widen to output
identical to today's. An earlier revision quoted only the first of those as
"everything else", which understated the unchanged share by the whole no-extent
population.

"Unchanged" here is a claim about rendered output, not about the code path. A
no-extent line now enters `Stack`, which walks the facts, finds no extent for
any of them, collects them all as unplaced and returns an empty group list; the
`{ Count: > 0 }` guard then fails and the widening branch runs exactly as
before. The bytes are the same; the work done to reach them is not.

`--focus lifetime` never stacks: no line in CoreLib carries two lifetime facts
that disagree about the extent. The gesture is worth having anyway, but this
model is invisible under it, and a claim measured over all facts at once would
have hidden that.

Applying the specification to the 3,169 lines that stack:

| rows | lines | |
| --- | ---: | ---: |
| 1 | 2,870 | 90.6% |
| 2 | 297 | 9.4% |
| 3 | 1 | 0.0% |
| 4 | 1 | 0.0% |

That 90.6% is padded too: 255 of these lines carry a single extent group and
cannot occupy more than one row. Among the **2,914** lines with two or more
groups — the ones with something to pack — **2,615 (89.7%)** take a single row.

And 2,914 is padded in turn, by a structural fact stated earlier in this
document: extents sharing a start column can never share a row. **136** of those
lines carry two distinct extents at one column and so cannot take a single row
whatever the packer does. Among the remaining **2,778** the rate is **2,615
(94.1%)**.

"Remaining" rather than "able to": 163 of those 2,778 also fail, refused by row
admission because their extents sit too close together. They are left in
deliberately. Crowding is what this rate exists to measure, so excluding lines
for being crowded would drive it to 100% and measure nothing. A shared start
column is excluded because it is a different phenomenon — two extents anchored
at one column are a nesting, not a packing failure.

4,457 of 7,347 trails (60.7%) render at true width, but that headline rate is
padded by a structural immunity and should not be read as a packing success
rate. The last trail on a row has no successor, so its clip limit is
`int.MaxValue` and it renders at true width by construction. Those 3,169 lines
occupy **3,471 rows**, so 3,471 of the 7,347 trails could not have been clipped —
they are 3,471 of the 4,457 counted as rendering at true width, and measurement
confirms all 3,471 do. Of the **3,876** trails
actually exposed to a successor, 986 (25.4%) survived at true width. Eight of
those are immune as well, their extents being no longer than `MinTrail`, which
row admission already reserves. Among the **3,868** trails that could genuinely
be cut, **978 (25.3%)** survived and **2,890 (74.7%)** were clipped. That is the
informative rate; it took two passes to strip both layers of padding off it.

A trail is cut
short by the next label on its own row, which is the only thing that clips a
trail. That successor is nested inside the trail it clips on **2,075 (71.8%)**
of them and wholly disjoint from it on **815 (28.2%)**, so clipping is
concentrated at nestings but is not confined to them. The rule 8 gutter fallback
fires on **0** of the 3,169 lines qualifying to stack before it runs.

### Reproducing these figures

The specification above is implemented in `AnnotationCaret` and shipped in
[#3656](https://github.com/richlander/dotnet-inspect/pull/3656) at `721adb61a`.
Every figure below it was re-measured after
[#3674](https://github.com/richlander/dotnet-inspect/pull/3674) taught the
anchor to adopt a printed descendant for a fact whose own node prints nothing,
which gave 1,841 more facts an extent and moved almost every number here. Each
probe was first replayed against `74b4f546c` and reproduced the superseded
figure exactly before its new result was trusted.

Every code block in this document is a verbatim excerpt of `Annotated Source`
output for the member and focus family named beside it:

```bash
dotnet-inspect member RuntimeModule ResolveSignature \
    --all --platform System.Private.CoreLib --focus cost -S "Annotated Source"
dotnet-inspect member HebrewCalendar CheckHebrewYearValue \
    --all --platform System.Private.CoreLib --focus alloc -S "Annotated Source"
dotnet-inspect member Vector IsSubnormal \
    --all --platform System.Private.CoreLib --focus safety -S "Annotated Source"
dotnet-inspect member MemoryExtensions ThrowNullLowHighInclusive \
    --all --platform System.Private.CoreLib --focus alloc -S "Annotated Source"
dotnet-inspect member "System.Tuple<T1,T2,T3,T4,T5,T6,T7,TRest>" Equals:1 \
    --all --platform System.Private.CoreLib --focus alloc -S "Annotated Source"
```

Both flags matter. `--all` is required because all but one of these members are
non-public. `--platform System.Private.CoreLib` is required because without it
`RuntimeModule` does not resolve at all and `Interop`-style names resolve to a
different assembly — and because the platform scope loads the PDB, so locals
print as `tk` and `buffer` rather than `V_0`. A render taken without it will
disagree with every snippet here.

The two blocks labelled as today's render are the **widening** renderer, which
is the behaviour this document replaces; they were taken at `f26fa8e0a`, the
commit before the implementation.

Columns in a snippet are **final rendered columns**, which is not the geometry
the research render produces. Three coordinate systems exist and mixing them is
the easiest way to read a correct caret as a misaligned one:

1. The **research render** emits the code line at its own nesting indent and
   prefixes each hoisted caret row with a `HoistMarker` control character.
2. `ApiOutputFormatter` adds `BodyIndentWidth` (4) to **code lines only**, drops
   the marker, and leaves hoisted caret rows at column 0.
3. What a snippet shows is therefore a code line indented four columns further
   than the raw render, above caret rows that were not moved at all.

A caret verified against the raw code line will appear four columns off. Verify
against the rendered pair, both taken from the same output.

The corpus figures come from the probes described in #3656; they walk
`System.Private.CoreLib` with `IrImporter.ImportAssembly`, call the production
`AnnotationAnchor` and `AnnotationCaret` entry points rather than reimplementing
them, and read every width back off rendered output.

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

The layout this rejects was never implemented, so unlike every other code block
in this document the following is a mockup, not a render:

```csharp
    Span<char> V_0 = stackalloc char[256];
//                   ^^^^^^^^^^^^^^^^^^^^  lifetime.stack-bound(Span<char>)
```

Rejected as *the* style on measurement, not taste. Every figure below shares
one convention, and it has to be stated because an earlier revision of this
section did not state it and quietly mixed two: **final rendered columns**,
one row per fact, text set two columns past the widest caret, which is what
the sketch above shows. Rendered column of code character *i* is
`BodyIndentWidth + i`, so the body indent counts on both sides of every
comparison. The population is the **31,320** lines carrying at least one
extent under the five focus families.

- Text lands past the end of the code line on **30,577 lines (97.63%)**, and
  over those 30,577 the mean overhang is **85 columns** — the mean is over the
  lines that overflow, not over all 31,320.
- **23.6%** of lines still fit 80 columns, against **76.4%** for the bare code.
  Restricted to the 23,937 whose bare code fits at all, **30.4%** survive; the
  other **69.6%** wrap, and the wrap destroys the very adjacency that motivates
  the style. Over all 31,320 the wrap rate reads 76.4%, but 7,383 of those
  lines overflow 80 columns before any annotation is added and wrap under any
  style, so that figure measures the corpus rather than this one.
- The annotation column passes 100 on **3,691 lines (11.8%)** — or **87.7%** of
  the **4,211** lines long enough to push it that far at all, which is the rate
  that means something: when the style can hurt, it almost always does.
- The maximum annotation column is **57,946**, on
  `IcuLocaleData.get_NameIndexToNumericData`, the pathological line already
  filed as #3610. That is derived rather than measured directly: the line is
  **57,940** characters, one extent covers all of it, and 4 + 57,940 + 2 is
  57,946. The line length is the figure that survives a change of convention.

At a one-column gap every percentage above moves by under one percentage point
(97.32%, 24.2%, 31.2%, 11.4%, 87.3%) and the maximum column moves by one, to
57,945; the counts behind them shift, but the rejection is unaffected. The
obvious first candidate for a wide style.

### Constant-width caret trails

Render every caret at a fixed width so packing depends only on start columns.
Compact, supposedly. An earlier revision claimed a 4-wide trail fits 87.5% of
multi-extent lines on one row. That figure has no probe behind it anywhere and
does not reproduce, so it is withdrawn rather than replaced: a fixed-width
packer is not implemented here, and two independent reconstructions of what it
would do disagreed with each other (88.4% and 87.2% on the same corpus).

One thing about it can be settled without a figure, because it is a fact about
the *shipped* packer rather than the unbuilt one. Row admission reserves
`Math.Min(Extent.Length, MinTrail)` columns — capped at `MinTrail`, which is 2 —
and nothing else in the admission test reads a length. **The shipped packer is
already width-independent above two columns**: a trail's real width never affects
whether the next one joins its row. So there is no packing headroom for a
fixed-width variant to recover by narrowing trails; a variant that reserved four
columns would admit strictly less. Whether such a model could win by *grouping*
differently — merging distinct extents that share a start column, which this
model keeps separate — is a real question and is not answered here.

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
