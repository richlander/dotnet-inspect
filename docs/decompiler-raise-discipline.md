# Raise-work discipline

The working rules for changing the decompiler's raising, typing, or emission
behavior. Each rule was paid for by an adversarial-review finding or a
measured regression on a real PR; the citation is the receipt. The design
itself lives in [value-typed-emission.md](design/value-typed-emission.md) and
[inverse-architecture.md](design/inverse-architecture.md); this note is how to
work inside it without re-buying the lessons.

## Evidence

- **Render-text A/B is standing evidence for any raise/printer-affecting PR.**
  Emit a base dump at the explicit merge-base ref and diff the head against it
  (`DecompilerHarness --emit-render-ab` / `--render-ab`, with `--workers`).
  Every changed method gets classified into an intended class; an unclassified
  diff is a finding, not noise. The base is the one dangerous degree of
  freedom — a stale base once turned 97 changed methods into 359 phantom ones
  (#2170), twice. Never diff against a hand-managed dump.
- **No claimed win before the A/B lands.** Census counters and structural
  metrics move before rendering truth is known; slice 4's blocking corruption
  (#2170 review round 1) was found by reviewers running the A/B the author had
  reported around.
- **Audit at method granularity, and audit bytes.** Line-diff pairing lies
  when a method restructures (a switch statement flipping to an expression
  aligns unrelated lines). Terminals lie about content (lone surrogates render
  as U+FFFD on both sides of a real difference — #2204). `hexdump` settles it.
- **Trace every card verdict.** "Regressions; review before merging" from the
  corpus sensor is frequently a sampling-cap artifact (PR-recipe caps vs the
  daily baseline's); state the artifact in the PR rather than papering over
  the verdict — and never re-key card numbers by hand (AGENTS.md).
- **A false positive in a sensor is still a lead.** The #2157 render-A/B
  surrogate false positive, byte-audited instead of waved off, exposed one
  real product bug (raw lone-surrogate literals) and one real sensor bug
  (lossy dump encoding) (#2204, #2155).

## Typing

- **Join typing must be exact; rendering must widen; narrowing is never
  silent.** Asserting a merged type demands nominal equality with the
  representation (width and sign) — a family-level match let a byte-backed
  enum absorb a full-int path and change a boxed value (#2170, blocking).
  Spelling a value at a sink accepts any value-preserving widening — refusing
  it produced bare CS0266 output graded Full (#2170, cross-check). A genuine
  narrowing arrives as a `Convert` node; a cast must never be the place that
  discovers one.
- **Ambiguity is not an invitation to guess; every witness testifies.** A
  reconciliation over multiple observations (slot loads, join arms) must
  collect evidence from *every* observer — typed observers contribute their
  type, untyped observers contribute their consuming sink's target, and an
  observer with neither vetoes (#2204 round 2: "untyped loads are silence"
  was the hole both reviewers found independently).
- **The oracle rule, scoped.** Any type or shape asserted from an IL method
  body must be assertable by RyuJIT's importer from the same IL; stronger is
  unsound. The scope matters: metadata-derived facts (enum member names,
  signatures, sugar shapes) are not oracle-bounded
  ([inverse-architecture.md](design/inverse-architecture.md)).

## Structure

- **The recurring defect species is the partial sibling rule.** Ten
  consecutive adversarial findings across #2114–#2204 were one species: a
  width-, direction-, or scope-partial rule present in one render context or
  pass and absent in a sibling. Two corollaries: the fix is never a local
  patch — it is one named rule serving every context (`TryCoerceJoinArm`,
  `RequiresCoercion`, `StoreElementTarget`); and **when you fix a rule
  anywhere, grep for its siblings** — the printer knew
  `DescendantsOutsideNestedFunctions` while two passes walked `Descendants`
  into nested bodies (#2143 round 1, #2204 round 2).
- **Nested bodies are separate scopes.** `Lambda` and
  `LocalFunctionStatement` bodies carry their own return types and their own
  slot numbering. `function.Descendants` crossing them has produced three
  independent bugs (lambda returns coerced to the outer signature, twice; slot
  maps unified across bodies). Walk body scopes.
- **Decisions have owners; do not preempt a sibling lane.** Bool joins belong
  to `BooleanFoldingPass`; slot C# types belong to the unifier until instance
  2 materializes locals; merge-node rendering belongs to `CoerceText`'s
  targeted branches. A pass reaching into another lane's decision is how the
  phantom-split and formatting regressions happened (#2143 rounds 1–3).
  Exclusions are fine when they are *counted* — the invariant's residual
  ledger exists so a scope limit is measured, never silent (#2145).
- **Mutating a finished tree is the fragile shape.** The coercion invariant is
  established by a pipeline-last pass today; every neutrality break traced to
  that retrofit (clone-vs-live-tree, structural pattern matches through
  wrappers). The end state is Roslyn's: establish invariants at construction.
  Until then, wrap only where the consuming path is provably the one renderer
  with the same target.

## The assertion dump: development aid vs signoff

The assertion dump (`DecompilerHarness --dump --assertions`) is a
**single-method, qualitative** tool — a strong development aid and a
legitimate *localized* signoff, never a population gate.

- **Reach for it while developing:** authoring an annotation batch (does a
  node's `Forward`/`Oracle`/precondition read coherently over real decompiled
  IR, not just in the attribute string?); debugging one bad raise (the
  first-unsound-rewrite marker localizes the illegal intermediate step — its
  highest-value everyday use); deciding checkable-inverse vs `[NotInverted]`.
- **Attach it as signoff only when** the change touches
  structuring/typing/printer semantics on nodes that carry a **runnable
  `assumes:` predicate**, shown as a before/after on the targeted method(s)
  (the improved example plus a still-flat near miss) — and always as a
  complement to the corpus evidence, never a replacement. A burndown /
  invalid-`Full` fix showing the violation gone at that node is the natural
  "defect is dead" artifact.
- **Never as signoff when** the claim is population-scale (that is the card
  and render A/B's job); when the changed nodes carry no `assumes:` predicate
  (the dump is then purely descriptive — do not dress it up as a gate); or on
  a metadata-only annotation batch (the drift/coverage tests are the gate; a
  dump is optional color).

Rule of thumb: **its signoff weight equals how many of the changed nodes
carry runnable predicates.** No predicate → dev aid only; predicate present →
localized proof, on top of the card.

### The aggregate view: `--assertion-scan`

The single-method dump can't answer population questions; `DecompilerHarness
--assertion-scan [--sample N] [--package …]` runs the same `assumes:`
predicates across an assembly and reports a violation histogram (by sink type /
pass / node / predicate), the methods-with-violation rate, and **annotation
coverage** — which `[InverseOf]` nodes the scanned population actually
exercised. `--emit-assertion-violations` / `--diff-assertion-violations` give a
REGRESSED / IMPROVED differential (the "N→M violations, 0 regressions" proof for
a coercion PR, mirroring the validity-defects loop).

Reach for it to:

- **Audit a new annotation batch** across a corpus — confirm the nodes you
  annotated actually appear over real IR (e.g. a full scan of the decompiler
  assembly, 6756 methods, exercised 22/25 annotated nodes with 0 pass bugs).
- **Measure the leak surface** — the methods-with-violation rate is the
  automatable form of the value-typed-emission leak number.
- **Localize a coercion regression** — the emit/diff pair names the methods
  that gained a violation.

It stays **measurement and triage, not a correctness gate.** Its counts and
histograms do not gate fidelity or validity — that remains the quality-diff
card and render A/B. Its exit code flags *pass bugs* (importer / pipeline
crashes), never a violation count. Same rule as the single-method dump: an
assertion-scan number is population *measurement*, not a pass/fail bar.

## Annotations (the inverse ledger)

- **`assumes` names an executable, or it moves to prose.** An attribute
  precondition without a release-capable `Check()` is a comment with extra
  steps and rots against behavior. By-construction facts (a `Box`'s type comes
  from the instruction) deliberately omit `assumes` — absence is a statement,
  distinct from "real but unspellable," which gets the prose marker (#2148
  thread).
- **`Oracle` is a licensing authority, not a family label.** Cite
  `RyuJitStackNormalization` only where the node's *recovery claim* leans on
  it (`Coerce`); a node that records history (`Convert`) asserts nothing and
  carries `None`.
- **Annotate today's truth, not the plan.** A row whose witness or
  precondition describes unlanded work undermines the ledger's "as real as
  its runnable check" rule; the owner of the changing lane re-annotates in the
  changing PR (#2213 hand-off).

## Sequencing

- **Measure before designing; let the census choose the slice.** Slice 4's
  join rules and slice 5a's reconciliation were scoped by counting the actual
  corpus population (join-diagnostic census, split-name census) rather than by
  the design doc's a-priori ordering. Build the ten-line census tool first.
- **Each slice's residual is the next slice's opening move.** Declared
  exclusions (slot values "until instance 2", merge arms, `Box` operands) are
  IOUs; keep them enumerated (doc + residual counters) so the next slice
  starts from a list, not a hunt.
