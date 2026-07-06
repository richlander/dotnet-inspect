# The assertion lane as a staged effect system

The inverse-architecture assertion lane (see
[inverse-architecture.md](inverse-architecture.md)) is, structurally, an **effect
system over pipeline stages**. Naming it that way explains the `OBLIGATION` /
`UNSOUND` vocabulary introduced in [#2269](https://github.com/richlander/dotnet-inspect/issues/2269),
gives the markers a theory instead of a convention, and points at a concrete next
step. This note is the framing; the mechanics live in the harness assertion lane.

## The algebra: accrue, discharge, escape

Every `[InverseOf]` node carries an `assumes:` predicate — a type claim the raw
type system does not encode. As a value is rewritten across the pipeline, three
things can happen to that claim:

- **Accrue.** A rewrite mints a node whose `assumes:` predicate does not yet hold
  — for example, `SlotMaterializationPass` runs before `CoercionInsertionPass`, so
  for a few stages a minted `LoadLocal` sits at a sink whose target type it does
  not match. The rewrite has *accrued a typing obligation*.
- **Discharge.** A downstream pass is contracted to make the claim hold —
  coercion insertion wraps the sink in a `Coerce`, and the obligation is gone.
- **Escape.** An obligation that no pass discharged reaches the final stage. The
  rendered output now leans on a claim nothing justified.

Only *escape* is an error. Accrual and mid-pipeline persistence are the pipeline
working as designed. That is exactly the `OBLIGATION` (informational, at any
non-final stage) vs `UNSOUND` (error, a final-stage survivor) split: the marker
is bookkeeping until it crosses the boundary. **"Final stage is zero `UNSOUND`"**
is the soundness statement.

## Two lenses

The same algebra reads two ways, and both are worth keeping.

**Memory safety (the evocative lens).** This is the mapping
[#2269](https://github.com/richlander/dotnet-inspect/issues/2269) draws:

| Memory safety | Assertion lane |
| --- | --- |
| `unsafe` propagates the obligation to callers (`RequiresUnsafe`) | an `OBLIGATION` propagates the typing claim down the pipeline |
| an `unsafe { }` block *encapsulates* — the author discharges it locally | a downstream pass *discharges* the obligation (the claim is now proven) |
| an unencapsulated unsafe operation escaping a safe boundary | an `UNSOUND` obligation escaping to the final stage |

In both, the mid-flight marker is bookkeeping, not a diagnosis; only crossing the
boundary undischarged is an error. "Final stage is zero" ≙ "the public surface is
safe." It also inherits the static half already documented in inverse-architecture.md:
Rust's [Safety Tags RFC (rust-lang/rfcs#3842)](https://github.com/rust-lang/rfcs/pull/3842)
and the [Contracts experiment (rust-lang/rust#128044)](https://github.com/rust-lang/rust/issues/128044)
are the same move from a `//` safety comment to a named, tool-checkable
obligation that a matching site must discharge.

**Compiler legalization (the mechanically exact lens).** Memory safety's effect
propagates over the *call graph*; the assertion lane's obligation propagates over
a *linear sequence of passes*. The "caller" is just "the next pass," and discharge
is by *contract* — a specific pass is designed to discharge a specific obligation
class — not by author choice. That makes the closest technical cousin not Rust but
**dialect legalization / typestate across lowering phases**: MLIR's partial→full
legalization ("illegal ops must be legalized before this boundary") and the LLVM
verifier invariants that hold only after certain passes. An intermediate
`OBLIGATION` is an "illegal-but-scheduled-for-legalization" op; `UNSOUND` is an
illegal op that reached the legal boundary.

Where the analogy breaks: this is an effect system over a *fixed, ordered* pipeline,
so it is simpler than a whole-program one — there is exactly one boundary (the final
stage) and the discharge schedule is static.

## The concrete next step: per-pass effect signatures

Today discharge is **observed**, not **declared**: the lane notices the marker
disappear between stages. Memory safety's power comes from the obligation being
*named and matched* — an unsafe operation names the invariant, and a matching
`unsafe`/tag discharges *that* invariant.

The analogous step here is a **per-pass effect signature**: each pass declares
which obligation classes it is contracted to discharge (and, optionally, which it
may accrue). The lane then checks a property strictly stronger than "final stage
is zero":

> every accrued obligation has a *named* discharger that provably runs before the
> boundary.

That turns [#2269](https://github.com/richlander/dotnet-inspect/issues/2269)'s
"name the discharging pass" hint (`OBLIGATION (discharged by coercion-insertion)`)
from a printed convenience into a checkable contract, and it catches a class the
observational check cannot: an obligation that happens to be zero on today's
corpus but has no pass contracted to discharge it — a latent escape.

## Follow-ups

Tracked on [#2269](https://github.com/richlander/dotnet-inspect/issues/2269),
in rough dependency order:

1. **Landed ([#2281](https://github.com/richlander/dotnet-inspect/pull/2281)):**
   `--assertion-scan` counts **final-stage survivors** corpus-wide — the survivor
   number, distinct from discharged obligations, is reported and diffable
   (snapshot schema v2 + survivor delta) instead of eyeballed from
   `--dump --assertions`. Still open: promote it from a report/diff signal to a
   hard zero-gate, which needs an allowlist of the known `PrinterOwned` residuals
   so only *new* survivors fail.
2. **Landed ([#2284](https://github.com/richlander/dotnet-inspect/pull/2284)):**
   track **obligation lifetime** — stages between accrual and discharge — as a
   construction-quality metric. Lifetime trending down means passes decide early
   rather than retrofitting a claim late.
3. Attribute discharge to a named pass in local dumps, then graduate to the
   per-pass effect signatures above.
