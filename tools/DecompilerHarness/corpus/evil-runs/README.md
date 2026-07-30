# EVIL authored-corpus run history

This directory stores the compact trend history for the EVIL authored-source
correspondence benchmark tracked by #3079.

`history.jsonl` is newline-delimited JSON, newest-last. Each line is one full
`--benchmark-authored-corpus --json` run summarized to stable header metrics.
The multi-megabyte per-row JSON stays out-of-tree as a session artifact, issue
attachment, or CI artifact. Do not commit full per-row run payloads here.

## Schema

Each row contains these fields:

- `date`: UTC run date, formatted as `yyyy-mm-dd`.
- `commit`: short source commit SHA for the harness under test, or `null` when
  the original run did not record it.
- `poolMatched` and `poolTotal`: corpus assembly coverage for the supplied
  assembly pool.
- `evaluated`: target methods evaluated by the benchmark.
- `validPct`: one-decimal valid percentage reported for the run.
- `correct`: valid rows that match authored source.
- `validDifferent`: the valid-different partition. The sub-buckets must sum to
  `total`; the benchmark fails the run if they do not.
  - `total`: all valid rows that differ from authored source.
  - `lowering`: authored sugar the compiler erases (inherent, unrecoverable).
  - `knownTaste`: a documented product decision, already accounted for.
  - `frontierIlExact`: cosmetic frontier rows with IL-exact output.
  - `frontierIlDiff`: semantic frontier rows with IL-different output.
  - `frontierIlNoVerdict`: rows the compile-back oracle returned **no verdict**
    for. This is instrument failure, not a classification — those rows are
    *unmeasured*, not "neither exact nor diff". It is recorded even when zero so
    that a shortfall can never be mistaken for data.

  `lowering`, `knownTaste`, and `frontierIlNoVerdict` are absent only on rows
  recorded before the store carried them, where the values are not recoverable
  from any retained artifact. Absent means **not recorded**, never zero.
- `invalid`: rows that did not round-trip.
- `invalidBreakdown`: `null` for runs before #3096; otherwise the
  FaultIsolation-backed split:
  - `productBodyDefect`: invalid rows isolated to the target body.
  - `harnessShellReconstruction`: invalid rows isolated to the reconstructed
    shell or closure. These rows are **unmeasured** for product status, not
    product-clean — a broken shell masks whatever the body did.
  - `unclassified`: invalid rows without a product-vs-harness classification.
- `notFull`: rows uncheckable at `Full` fidelity (a surfaced decompiler
  limitation, not a corpus problem).
- `unsupported`: unsupported ReturnToSender targets.
- `drift`: rows where corpus source could not be resolved.
- `unknownOutcome`: rows whose probe outcome the classifier does not recognize.
  Unreachable while every outcome is classified; it exists so that an
  unclassified outcome surfaces instead of inflating a real bucket.
- `inputsComplete`: `true` only when the run had no unmatched rows and evaluated
  at least one target. This reports **only that the inputs were all present**.
  It makes no claim that every evaluated row was measured — see
  `frontierIlNoVerdict` and `harnessShellReconstruction`, both of which are
  unmeasured rather than clean. (Rows before #3244 spell this field `honest`,
  which overclaimed exactly that distinction.)
- `corpusSha256`: identity of the corpus measured, copied from the run JSON.
  Absent on rows recorded before the ratchet.
- `poolSha256`: identity of the assembly pool measured, copied from the run
  JSON. Runs derive it from the assemblies themselves (each named and
  content-hashed), so it always describes exactly what was decompiled.
- `sweepManifestSha256`: the superseded pool identity on rows from 2026-07, a
  hand-recorded SHA-256 of the sweep manifest *file*. It could not identify the
  pool — the pool is the **union** of the sweep and a fixed set of real-world
  assemblies, and the manifest described only the sweep half — so it does not
  interoperate with `poolSha256` and rows carrying only it record no pool
  identity under the current scheme.
- `methodologyVersion`: how `invalidBreakdown.productBodyDefect` was computed.
  **Every version is a lower bound on decompiler-caused body defects**; a later
  version tightens the bound rather than measuring the true count. Copy this
  field verbatim from the run JSON's top-level `methodologyVersion`.

  Absent (or `null`) means **v1**: substitution control only — a body defect is
  credited only when the checksum-verified authored body compiles in the failing
  row's shell, so a broken shell masks the defect entirely.

  **v2** keeps the v1 substitution control and adds span attribution, which
  recovers some of the rows a broken shell used to mask. Under a broken shell it
  credits a body defect only when *both* hold:

  1. the authored body is error-free within its own body span (the control), and
  2. the decompiled body carries an in-body error that a broken shell
     **provably cannot manufacture**.

  Condition 2 is the entire soundness argument and is enforced by default-deny.
  Only two error classes qualify:

  - any **parser diagnostic** inside the body span (from
    `SyntaxTree.GetDiagnostics()`, which no shell state can influence), and
  - a **body-intrinsic semantic error** drawn from an explicit allowlist,
    currently exactly `CS0128` (duplicate local declaration).

  Context-dependent errors — unresolved names, types, or members (`CS0103`,
  `CS0246`, `CS0234`, `CS1061`, `CS1069`), conversions, and overload resolution
  — are **never** credited, because a broken shell reconstructor produces them
  identically to a real body defect. Every other case declines, which is a
  deliberate false negative that preserves the lower bound.

  Because the allowlist defines what `v2` means, expanding it requires a new
  `methodologyVersion`; do not add IDs under the existing stamp. This is
  enforced mechanically, not by convention:
  `SpanAttributionTests.BodyIntrinsicAllowlist_IsPinnedToCurrentMethodologyVersion`
  asserts set equality between the live allowlist and the set pinned for the
  current version, so any addition fails the gate until the version is bumped
  and a new pin recorded. A companion test pins the excluded *categories*
  (resolution, conversion, overload, scope collision, definite assignment) that
  the prose above forbids. v1 and v2 counts are not directly comparable, and the
  progress card never diffs `productBodyDefect` across the boundary.

## Append procedure

1. Build the harness at the commit under test. **This must be a commit on
   `main`.** The store is a trend series, so every row has to be reproducible
   from the recorded commit; a feature-branch commit is not, and it carries
   whatever product state the branch happened to be based on. Measuring a
   methodology or product change against its own base is a valid experiment —
   it just belongs in the PR that makes the change, not in this series.
2. Prepare or reuse the EVIL pool:

   ```bash
   bash eng/prepare-evil-corpus.sh /tmp/evil-pool
   ```

3. Run the full corpus with JSON output:

   ```bash
   dotnet run --project tools/DecompilerHarness -c Release --no-build -- \
     --benchmark-authored-corpus external/authored-source-corpus/evil/corpus.jsonl \
     --json $(cat /tmp/evil-pool/assemblies.txt) > evil-run-YYYYMMDD-SHA.json
   ```

   The run reports the `poolSha256` the recorded row needs without being asked:
   the identity is derived from the assemblies passed in, so an appended row can
   never fail to identify its pool.

   Exit code 1 is expected while `invalid`, `drift`, or `unsupported` is
   non-zero; the JSON is still authoritative. That contract is unchanged, and
   deliberately so: it applies whenever `--ratchet-baseline` is absent, which is
   the case for this append run. To judge a run by movement instead of by
   perfection, see [The regression ratchet](#the-regression-ratchet).

4. Archive the full JSON and `/tmp/evil-pool/sweep-manifest.json` out-of-tree.
5. Record the UTC date and short SHA, and copy `poolSha256` and
   `corpusSha256` from the run JSON. Do not compute these by hand: both are
   defined by the tool (see [Comparability](#comparability-and-the-difference-between-a-skip-and-a-pass)),
   and a hand-derived value that disagrees makes the row uncomparable.
6. Copy the run JSON's top-level `methodologyVersion` into the row, **and its
   `invalidBreakdown`**. A row that states a methodology and omits the breakdown
   is refused: the version it stamps is the definition of a number it declined
   to record.
7. Append one compact JSON object to `history.jsonl`, copying **every** bucket —
   the full `validDifferent` partition (including zeros), `notFull`, `drift`,
   `unsupported`, and `unknownOutcome`. A row that omits a bucket shrinks the
   partition silently, which is the defect #3244 fixed. The row's
   `validDifferent` member is the run JSON's `validBreakdown` object copied
   whole; it carries `total` for exactly this reason, because that total is what
   the ratchet's `valid` metric is built from.
8. Validate every line parses before committing.

### The partition is enforced, not assumed

`AuthoredCorpusHistoryCardTests` reads this tracked store and fails when:

- a row's `validDifferent` sub-buckets do not sum to its `total`, or its
  top-level buckets do not sum to `evaluated`
  (`TrackedHistory_CompleteRows_PartitionExactly`); or
- any row other than the grandfathered ones omits a bucket
  (`TrackedHistory_OnlyGrandfatheredRowsOmitThePartition`, asserted as set
  equality so the grandfather list cannot go stale).

The benchmark applies the same check to its own output before exiting, and
fails with a `BLOCKER:` line rather than emitting a payload that looks complete.

Exactly one row is grandfathered: **2026-07-20**, recorded before the store
carried the full partition and with no retained artifact to recover it from. Its
sub-buckets are absent rather than zero. Every other historical row was
backfilled from its own archived run JSON, verified by matching all nine shared
fields against the recorded row.

## The regression ratchet

`--ratchet-baseline <history.jsonl>` judges a run by **movement against this
store** instead of by perfection:

```bash
dotnet run --project tools/DecompilerHarness -c Release --no-build -- \
  --benchmark-authored-corpus external/authored-source-corpus/evil/corpus.jsonl \
  --ratchet-baseline tools/DecompilerHarness/corpus/evil-runs/history.jsonl \
  --json $(cat /tmp/evil-pool/assemblies.txt)
```

It exists because the benchmark's exit code was a constant. Success required
`invalid == 0`, which on a 12,000-row adversarial corpus sitting near 5,200
invalid is unreachable, so the exit code read identically at 56.7% valid and at
40% valid and detected no regression at all (#3245).

The fix separates two questions the old contract conflated:

- **Measurement integrity** — unmatched rows, an empty run, a non-closing
  partition, drift, unsupported targets, or an unrecognized outcome — still
  fails hard, with or without a baseline. These do not say the decompiler got
  worse; they say the run is not trustworthy, and an untrustworthy number must
  not be compared to anything. A ratchet skip never rescues an integrity
  failure.
- **Quality level** is the thing being measured. Without a baseline it keeps the
  historical `invalid == 0` contract. With one, the run fails on a *regression*
  in `valid`, `correct`, `invalid`, or `invalidBreakdown.productBodyDefect`.

### The band is zero

Every metric ratchets strictly. The four-run spread in this store
(56.6/56.2/56.2/56.5) is not instrument noise to be absorbed: each of those runs
is a *different commit* against an *identical pool manifest*, so the movement is
code-attributable — exactly what the ratchet exists to catch. Re-running one
commit against one pinned pool came back bit-identical on every counted metric.
A tolerance band would therefore be the harness declining to report real
movement, not compensating for a noisy instrument.

The ratcheted metric is the **exact valid count** (`correct` + `validDifferent.total`),
not `validPct`. The store records the percentage to one decimal, so 6,802/12,000
and 6,801/12,000 both read as 56.7 — a genuinely lost valid row would clear a
"zero tolerance" ratchet on the rounded figure. Because `evaluated` is equal by
the comparability key, the exact count says the same thing with none of the
rounding. Rows predating the `validDifferent` partition cannot reconstruct it, so
for them the metric is omitted rather than inferred from the percentage.

### Comparability, and the difference between a skip and a pass

A baseline row is comparable only when it shares the run's `evaluated`,
`poolMatched`, `poolTotal`, `poolSha256`, and `corpusSha256`, **and** can state
every metric the run states. The newest comparable row wins — not the newest row outright, so a resized corpus or a repooled sweep
cannot silently retarget the ratchet at an incomparable baseline.

`methodologyVersion` is deliberately **not** part of that key. It defines how
`productBodyDefect` is computed and nothing else, so folding it in discarded
three perfectly sound metrics at every version bump — and, because every row
after a bump was then incomparable, it is what made the tracked-store gate a
permanent skip. It is applied per metric instead: across a bump, `valid`,
`correct`, and `invalid` keep ratcheting and only `productBodyDefect` drops out.

Both identity hashes compare **symmetrically, absence included** — unknown never
equals known, in either direction. Two weaker rules were tried and both were
unsound:

- *Check the hash only when both sides recorded one.* This assumed a drifted pool
  always surfaces as unmatched rows or unresolved identities. It does not: a
  package resolving to a newer version that still carries the same method
  identities resolves cleanly, drifts nothing, and would have been ratcheted
  against numbers measured on different code.
- *Let the baseline govern.* Better, but it had a fallthrough. A run whose pool
  **mismatched** the newest row did not stop there — it continued down the store
  and settled on an older row that recorded no hash at all, turning a drifted
  pool back into a green comparison against an unidentified one. This store
  contains exactly such a row (2026-07-20), so it was reachable in production.

Symmetry is the only rule with no fallthrough.

`poolSha256` is derived from the run's own inputs: every assembly it actually
decompiled, named and content-hashed, sorted and digested. *Decompiled*, not
*supplied*: evaluation takes the first path offered for each assembly identity,
so digesting the supplied set let two byte-distinct assemblies sharing an
identity be reordered — changing which one was measured while the identity
stayed put. Digesting the selected set makes that impossible, and stops an
assembly the corpus never mentions from perturbing an identity it contributed
nothing to. Two earlier schemes failed here.
Hashing the manifest *file* was unreproducible — it carries `generatedAtUtc` and
per-package `fromCache`, so two sweeps of an identical pool hashed differently,
and a digest that never repeats makes the gate permanently red, which is as
uninformative as permanently green. Hashing the *resolved package identities*
was reproducible but still not the pool: `eng/prepare-evil-corpus.sh` measures
the union of the sweep and a fixed set of real-world assemblies, so changing the
real-world half left the identity unchanged, and packages that resolved without
producing an assembly counted anyway.

Taking the identity from the inputs themselves removes all of that. It is the
bytes that were decompiled, so it cannot describe a pool other than the one
measured, and it needs no flag — an identity that depends on the caller
remembering an argument has the same shape as the gate nobody invoked (#3245).
Identity is file name plus content, never path, because the pool is staged to a
different directory on every run.

Both digests are full SHA-256, and the per-assembly records compose two
fixed-width digests rather than interpolating the file name raw. Neither is
incidental: a truncated 64-bit identity falls to a birthday attack in about
2^32 operations, which would let a pool be swapped underneath a recorded
baseline while its identity still matched; and a Linux file name may contain
both separators, so a raw name could spell a second record and let one file
forge a two-file pool's identity.

`corpusSha256` identifies the corpus itself, because the counts do not. Swapping
in a different 12,000 rows — or editing a single row's authored body — preserves
`evaluated` and the pool, so without it a wholly different measurement compared
clean.

> Rows recorded before this change carry a hand-recorded `sweepManifestSha256`
> and no `corpusSha256` at all. They are not comparable to a run that identifies
> itself under the current scheme. That is the intended direction: a loud skip,
> never a false pass.

### A row is a baseline only if its own measurement was sound

The ratchet judges quality, and quality metrics mean nothing on a run that
measured less than it claimed. A row that shed rows into `drift`, `unsupported`,
or `unknownOutcome` reports a *lower* `invalid` for having measured less, which a
pure quality ratchet reads as an improvement.

So an untrustworthy row is neither judged nor used as a baseline, on both sides
of the comparison. `unknownOutcome` must be recorded and zero rather than merely
absent — absent means the run did not report it, and an unconfirmable row is not
a baseline. The 2026-07-20 row is the only tracked row that fails this, pinned as
set equality by `TrackedHistory_OnlyTheUnconfirmableRowIsNotTrustworthy`.

Soundness is checked by **summing the buckets**, not by confirming they were
recorded. A row claiming 12,000 evaluated whose buckets total 11,999 has lost a
target, and a lost target reads as a lower `invalid` — the same "looks like
progress, is actually absence" shape. All three levels are checked: the
top-level buckets must account for `evaluated`, the `validDifferent` sub-buckets
must account for their own total, and — when the row states it — the
`invalidBreakdown` reasons must account for `invalid`.

The third level is not decorative. Review forged a row pairing `invalid: 0` with
`productBodyDefect: 100`. It closed the other two partitions, became comparable,
and set a `productBodyDefect` ceiling of 100 that a real regression could then
pass under with `RATCHET OK` and exit 0 — a threshold laundered from a run that
cannot exist. A row that omits the breakdown *entirely* is a different case and
is refused separately, by the rule below.

Every recorded count must also be **non-negative**, at every level. Closure and
non-negativity are each load-bearing and neither implies the other: given
closure alone, pushing one bucket above the run's own size stays expressible by
driving another negative, so 100 `productBodyDefect` on a 60-target run is
reachable by recording `-40` somewhere else.

When you append a row by hand, this is the rule most likely to reject it: take
`invalidBreakdown` from the run JSON verbatim rather than filling in the reason
you care about and zeroing the rest.

A baseline must also be able to **state every metric the run states**. A metric
is only emitted when both sides have a number for it, so a row missing one
yields a comparison that ratchets fewer metrics than the run has while still
reporting `RATCHET OK`. A row recording the current `methodologyVersion` but no
`invalidBreakdown` is malformed rather than historical, and is refused. The one
legitimate omission is a methodology bump, which redefines what
`productBodyDefect` counts; there the other three metrics keep ratcheting.

Three outcomes, deliberately distinct:

| Situation | Exit | Output |
| --- | --- | --- |
| Baseline missing or unparseable | non-zero | hard error |
| Baseline parses, no comparable row | non-zero | loud `RATCHET SKIPPED` |
| Baseline compared | 0 or non-zero | per-metric `held`/`REGRESSED` |

**A skip fails.** It carries no quality opinion — nothing was compared — but
exiting 0 on it would rebuild the defect this replaces one level up: a gate
reporting success having measured nothing. The weekly caller makes that concrete.
Its pool is resolved from current top-N package versions, so it *will* drift off
the recorded manifest; on a green skip the job would pass forever in silence, and
the silence would look exactly like health. Passing `--ratchet-baseline` is a
demand for a verdict, and "none available" fails that demand. The remedy is a
corpus refresh or a corrected baseline, never a product change. A run with no
baseline to compare against simply does not pass the flag.

For the same reason a typo'd path is a hard error rather than "nothing to
compare", and `--ratchet-baseline` without `--benchmark-authored-corpus` is
refused rather than ignored.

`--integrity-only` is the third contract: it reports measurement integrity and
makes **no quality claim at all**, for a lane that cannot yet ratchet. It is
refused alongside `--ratchet-baseline`, since declining to judge quality and
demanding a verdict on it are contradictory. Selecting a contract by *omission*
is what this flag exists to prevent: the weekly lane was first wired by simply
dropping `--ratchet-baseline`, which silently selected the historical
`invalid == 0` contract that this corpus cannot satisfy, so the job would have
failed every week forever. Both the run output and the JSON's `qualityContract`
record which contract applied, so a green run cannot be misread as a quality
pass.

A **malformed corpus row** is an integrity failure, not a logged curiosity, for
the same reason. Dropping one silently shrinks `evaluated`, which makes the run
incomparable, which produces a skip — so before this was enforced, a corpus
quietly losing rows would have *disarmed* the gate instead of tripping it.

"Malformed" means unparseable *or* the wrong shape. A row can be valid JSON and
still not be a corpus row, and such a row used to abort the process on a null
grouping key with an unhandled exception and exit 134 — no report, no JSON, and
no way for a caller to tell an unmeasurable corpus from a broken tool. Rows are
checked against the fields `CorpusRecord` declares non-nullable, and the set is
derived from that type by reflection rather than restated, so a new required
field that goes unchecked fails a test instead of going unenforced.

`productBodyDefect` is reported with its lower-bound caveat attached: it counts
decompiler-caused body defects the oracle could adjudicate (~5.9% coverage), so
its movement is a floor, not a census.

### The identity bootstrap, and how it was crossed

Rows recorded before 2026-07-30 carry no `poolSha256` or `corpusSha256` — they
predate run identity. A live run always records both, and comparability compares
both symmetrically, so the first identified row was comparable to *nothing*: the
comparison skipped, and a skip fails the gate.

Crossing that took two identified runs landed together (#3353), both over the
same pinned pool and the same corpus. They are the first ratchetable pair, and
every append after them ratchets normally — including `productBodyDefect`, which
no earlier pair in this store could compare.

Those two rows carry the **same date, the same commit, and identical counts**,
and that is deliberate rather than a double-append: they are two separate full
runs of the same product over the same pinned pool, so the pair also measures
what the pin was built for. Identical `poolSha256` across two independent sweeps
is the reproducibility claim of #3353 discharged end to end, and identical
counts show the harness reads the same pool the same way twice. A trend needs a
second point before it can have a direction; this pair's direction is flat by
construction, which is the only honest thing a bootstrap pair can say.

The historical rows could not be back-filled: their pools and corpora were
archived out-of-tree and the artifacts are gone. The cheaper alternative — let a
baseline that records no identity compare against anything — is unsound, because
`--ratchet-baseline` reads a caller-supplied file and that rule would let any
baseline opt out of identity and then compare clean against a run over a wholly
different corpus.

Nothing re-opens the bootstrap. A future row that dropped its identity would be
comparable to nothing in exactly the same way, and
`TrackedHistory_NewestRowDoesNotRegressAgainstItsBaseline` fails on a skip.

### Who runs it

- **Every PR**: `AuthoredCorpusRatchetTests` ratchets the newest row of this
  tracked store against the newest earlier row it is comparable with, so a
  hand-appended row that halves the quality fails review. Rows recorded before
  the ratchet existed are data, not a contract, so only the new row is judged.
  The gate asserts that the comparison actually *happened*, not merely that it
  found no regressions — an empty-regressions assertion alone passes for a skip,
  which is how the first version of this gate was vacuous.
- **Every PR**: `AuthoredCorpusHarnessProcessTests` runs the harness *binary*
  and asserts what it says for each gate-flag combination — a gate combined with
  an earlier mode, a gate combined with `--help`, each modifier without its
  gate, the contradictory pair, a mode that preempts nothing, and the scheduled
  lane's own flags. It also runs the binary far enough to observe that
  `--integrity-only` and `--ratchet-baseline` were *forwarded*, and that each of
  the seventeen preceding modes refuses rather than replacing a requested gate.

  These exist because eight review rounds found eight instances of one defect: a
  term of the gate rule stranded in `Program.cs`, which owns an entry point and
  so cannot be linked into a test project. Each round moved one more term into
  `AuthoredCorpusExitContract`, and the next round found the next one — a
  forwarded argument, an array literal, a tuple's boolean, an argument at the
  dispatch call. Every time, the contract function was thoroughly tested, the
  suite was green, and the binary exited 0 having measured nothing.

  Pinning the call site's source text was tried and was worse than useless: a
  behavior-preserving comment failed it, while a commented-out decoy above the
  real call satisfied it. A second source-parsing test survived one round longer
  on the reasoning that a name mirror can false-red but cannot false-green;
  review then added an untested mode to the live dispatch list behind a
  commented-out copy of the old one, and the suite stayed green while the binary
  discarded a requested gate. Reading source text is guessing at what the
  program does, and a decoy is always available. The modes are declared in
  product code instead, and the refusal rejects a dispatch order that does not
  match the declaration.

  That declaration is not the guarantee, though, and round twelve showed why:
  both operands of that check are lists, and the dispatch itself is an
  `if`-cascade neither list is derived from. A mode with a parse case and a
  handler in the cascade, absent from both lists, discarded a requested gate at
  exit 0 with the suite green — the twelfth instance of the same defect, and the
  fourth mechanism to be defeated by something simply not being in a list.

  So the last check names nothing. The harness records whether a gate was asked
  for and whether that gate ran, and refuses to exit 0 when the first is true and
  the second is not. Where it runs from, what selected it, and whether anyone
  declared it are all irrelevant; the property is the outcome, not the route to
  it. Since that state is only reachable when the harness is broken, the
  `DOTNET_INSPECT_HARNESS_SIMULATE_PREEMPTION` variable makes review's exploit a
  supported input, so a test keeps running it rather than the wiring going
  unnoticed if it were deleted.

  Running the binary leaves no seam to strand a rule behind — but only as far as
  the binary actually gets. The first version of these tests pointed every run at
  a missing corpus, so the process returned at "corpus file not found" before
  reading the arguments the tests were named for, and dropping either one left
  the suite green. Reaching past that guard is the point.

  The test project takes a `ReferenceOutputAssembly="false"` reference on the
  harness so the binary is rebuilt with the tests, and a missing binary fails
  rather than skips.
- **Weekly**: the `authored-corpus-ratchet` lane in `deep-inspect.yml` restores
  the vendored corpus, prepares the EVIL pool, and runs the benchmark. It is a
  *periodic* job — the corpus and the 100-package sweep are far too expensive for
  the PR lane. It deliberately passes `--integrity-only` rather than
  `--ratchet-baseline`: this lane is the measurement-integrity gate and the
  source of the run JSON that an append starts from.

  The pool itself is now pinned: `docs/data/nuget-top-packages.lock.json`
  records the exact version, TFM, and SHA-256 of every swept package, and the
  sweep refuses to run against a package it cannot acquire as pinned (#3353). The
  hash is what makes the pin describe the bytes measured rather than the request
  made: a local NuGet cache entry whose contents were replaced still answers at
  the pinned version and TFM. Nine of the top
  hundred ship no primary library and are pinned as `no-library`; those are
  acquired too, and the absence is confirmed rather than believed, so the status
  cannot be used to drop a package out of the pool. A fresh sweep therefore
  reproduces the same assemblies, and its pool identity is stable.

  Two rows measured over that pinned pool are now recorded, so the lane passes
  `--ratchet-baseline` and judges the run by movement against the trend store.
  Note what it must *not* do instead: merely omitting `--ratchet-baseline`
  selects the historical `invalid == 0` contract, which this ~5,200-invalid
  corpus cannot satisfy, so the job would fail every week and file a
  scheduled-failure issue each time. `--integrity-only`, which this lane carried
  until the bootstrap was crossed, says only that the measurement was sound.
  Note the limitation it replaced was not new:
  the first comparability key was loose enough to compare across a drifted pool,
  which is a false green, and identifying the pool is what turned that silent
  wrong answer into a visible refusal.

## Progress card

Render a Markout progress card over this trend store — every recorded run as a
trend table plus a pivoted movement table over the most recent runs — with:

```bash
dotnet run --project tools/DecompilerHarness -c Release -- --history-card
```

Options:

- `--history-window <n>`: bound the movement pivot to the last `n` runs
  (default 3; `<= 0` uses every run). The Runs trend table always lists every
  run.
- `--history-path <file>`: read a specific `history.jsonl` instead of the
  committed default.

The card reads no assemblies and runs no decompiler. Its headline metric is
`invalidBreakdown.productBodyDefect` (genuine target-body decompiler defects),
not raw `invalid`: per #3079 the raw invalid population is dominated by harness
shell-reconstruction noise that does not move on decompiler fixes.

The **Movement** table is the transpose (pivot) of **Runs**: each metric is a
row and each recent run a column. Every metric row declares its optimization
goal, so Markout renders the goal glyph (`↑` higher-is-better / `↓`
lower-is-better) on the label and a per-step polarity glyph (`✓`/`✗`) on each
column compared to the previous populated one — no hand-computed delta or trend
word. Runs that predate PR #3096 have no `invalidBreakdown`, so their
product/harness columns render as `—` and the product-defect pivot cell is
absent (`-`) with no fabricated step, keeping the missing signal honest.

The Runs table's `Method` column reports each run's `methodologyVersion`
(`v1`/`v2`). Both versions are lower bounds, but v2's tighter rule counts
strictly more rows, so the counts are not comparable. When the movement window
straddles a version boundary the product-defect metric is split into
`Product defects (v1 substitution lower bound)` and
`Product defects (v2 span-measured lower bound)` rows, each populated only for
its own version's columns. Markout therefore never charts a step across the
boundary; a window of uniform version keeps the single `Product defects` row.
