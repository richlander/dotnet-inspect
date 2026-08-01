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
code line is 374 columns, so it is elided here at the `…`:

```csharp
    return comparer.Equals(m_Item1, objTuple.m_Item1) && comparer.Equals(m_Item2, … (374 cols)
//  ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ (370 carets)
//   alloc.box(T1; alloc=boxed T1; path=branch; path-confidence=behind-branch; …  ×16, unattributed
```

Under the model specified below the same line renders 8 numbered carets on a
single row, with 8 of the 16 facts attributed to the expression each is about
and the other 8 marked `-`.

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
the `Tuple.Equals` line below is elided because it is 374 columns wide, and the
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
   match, wrapped to `Budget`. One row per fact, in input order within an
   extent group; the number is written once, on the group's first fact, and the
   groups appear in the rule 2 order the carets were numbered in. Rows for
   facts with no extent are appended after all numbered rows.
7. **A fact with no extent is listed, not drawn.** `AnnotationAnchor` withholds
   an extent when it cannot identify the characters a fact is about. Such a
   fact keeps its place in the list below the carets, marked `-` instead of a
   number. A line may carry both kinds at once — a **mixed** line — and
   stacking still engages: the placeable facts get numbered carets, the rest
   are listed under `-`.
8. **The gutter wins.** A label is drawn immediately before the characters it
   points at, so a caret near the start of a line can push its label left into
   the comment gutter. Moving the label would make it point somewhere else, so
   when any label on a line would begin left of the gutter, that line does not
   stack at all: it falls back to the widening render this document replaces.
   This is a guard rather than a path with traffic — it fires on **0 of the
   2,842 lines that stack** in the corpus below — but it is reachable, and a
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

Every caret lies within the code line and a row cannot extend past its last
caret, so **a stacked caret row never adds width the code line did not already
have.** Measured on all 2,842 lines this model changes: 0 have a stacked caret
row wider than their code line, against 20 (0.70%) under the widening render it
replaces.

Widths below are **final rendered columns**, measured after the same projection
`ApiOutputFormatter` applies — code lines receive `BodyIndentWidth`, hoisted
caret lines do not — and the caret block is real rendered output, detail rows
included. Whole rendered block on those 2,842 lines:

| terminal | widen (today) | stacked |
| --- | ---: | ---: |
| 80 cols | 23.3% | 23.4% |
| 100 cols | 48.3% | 48.3% |
| 120 cols | 65.9% | 65.9% |
| 160 cols | 84.9% | 84.9% |

Terminal fit is unchanged, because on these dense lines the block width is set
by the wrapped detail rows, which both models share. Nor does the block itself
shrink: it is the same width on 91.8% of these lines, narrower on 1.1%, and
wider on 7.2% — the numbered labels cost a few columns where the details were
already the widest thing in the block.

**Stacking buys attribution, and essentially nothing else.** There is no
material aggregate improvement in either fit or block width: 1.1% of blocks do
get narrower and the 80-column fit does tick from 23.3% to 23.4%, but those are
rounding-scale movements against a 7.2% share that gets wider. The honest
summary is that fit and block width are unchanged, and this document should not
claim more.

The constraint still binds on the *alternatives*, which is why it is stated
here: side-alignment overflows the code line on 96.61% of the 29,933 lines
carrying an extent, by a mean of 77 columns, and fits 28.1% of them at 80
columns against 75.7% for the bare code.

### Mixed lines

A **mixed** line carries facts of both kinds: some with an extent, some
without. Summed over the five focus families there are 355 of them, and 254
have exactly one surviving extent, so this is not a corner. They are not spread
evenly: 298 fall under `--focus alloc`, 47 under `safety`, 10 under `cost`, and
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
  inline-detail shortcut. That is **27,091 lines, 90.5%** of those carrying an
  extent; see [Measurements](#measurements) for how that is counted and why the
  focus family has to be named before the number means anything.
- A fact whose expression has no printed node, or prints on a continuation
  line, still gets no extent. What changes is only how it is *shown* on a line
  where some other fact does have one: it is marked `-` instead of widening the
  caret. Lines where no fact has an extent widen exactly as today.
- Detail text stays left-aligned and wrapped to `Budget`.
- `AnnotationAnchor` is untouched. This is a change to `AnnotationCaret` alone.

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
strict subset of one family. Dropping facts can only remove distinct extents and
remove extent-less facts, and a line qualifies to stack only when it has two of
the former or one of each — so the *qualifying* set shrinks under a subset.

That is not by itself enough to make the **stacks** column an upper bound,
because rule 8 is not monotone. `RenderStacked` returns `null` for the whole
line the moment any one label would start left of the gutter, so a broad focus
can fall back to widening on account of a single early fact while a narrower
focus, having dropped that fact, stacks the two later extents successfully.
Naming the guard's rate against the lines that *stack* would be circular, since
a line rule 8 rejects does not stack by construction. Measured against the set
that **qualifies** to stack before rule 8 runs, the guard fires on **0 of
2,842** — so all 2,842 qualifying lines do stack, no such line exists in this
corpus, and the 2,842 does bound every narrower argument over CoreLib. That is a
measurement, not a consequence of subsetting, and it would have to be
re-measured on another assembly.

The bound is also specific to the **stacks** column. The other columns are not
monotone in the same direction: dropping one of two disagreeing extents turns a
multi-extent line into a single-extent line, so `single extent` can *rise* under
a narrower focus even as `multi-extent` and `stacks` fall. Read the table as
what the five documented arguments produce, and the 2,842 as the corpus-measured
ceiling on the rest.

Extents are measured in printed characters, so a figure that does not name its
render is not a claim about anything.

| focus | caret lines | …with an extent | single extent | multi-extent | mixed | stacks |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `alloc` | 14,789 | 12,744 | 12,367 | 377 | 298 | 594 |
| `safety` | 13,908 | 13,480 | 11,452 | 2,028 | 47 | 2,057 |
| `cost` | 2,516 | 2,444 | 2,277 | 167 | 10 | 175 |
| `lifetime` | 928 | 800 | 800 | 0 | 0 | 0 |
| `unsafe` | 723 | 465 | 449 | 16 | 0 | 16 |
| **total** | **32,864** | **29,933** | **27,345** | **2,588** | **355** | **2,842** |

A line stacks when its focused facts disagree about the extent, or when some
carry one and some do not: 2,588 multi-extent lines plus the 254 mixed lines
with a single surviving extent gives **2,842**. Everything else — **27,091
lines, 90.5%** of the 29,933 carrying an extent, or **82.4%** of all 32,864
caret lines — keeps exactly today's geometry, including the inline-detail
shortcut.

`--focus lifetime` never stacks: no line in CoreLib carries two lifetime facts
that disagree about the extent. The gesture is worth having anyway, but this
model is invisible under it, and a claim measured over all facts at once would
have hidden that.

Applying the specification to the 2,842 lines that stack:

| rows | lines | |
| --- | ---: | ---: |
| 1 | 2,543 | 89.5% |
| 2 | 297 | 10.5% |
| 3 | 1 | 0.0% |
| 4 | 1 | 0.0% |

3,884 of 6,566 trails (59.2%) render at true width. Clipping concentrates
exactly where extents nest. The rule 8 gutter fallback fires on **0** of the
2,842.

### Reproducing these figures

The specification above is implemented in `AnnotationCaret` and shipped in
[#3656](https://github.com/richlander/dotnet-inspect/pull/3656) at `a14d3ddce`.

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

Rejected as *the* style on measurement, not taste: aligning after the widest
caret pushes text a mean **77 columns** past the end of the code line and
overflows it on **96.61%** of the lines carrying an extent, leaving 28.1%
intact at 80 columns against 75.7% for the bare code.
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
