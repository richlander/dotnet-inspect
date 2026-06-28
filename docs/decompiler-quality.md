# Decompiler Quality

How `ILInspector.Decompiler` stays correct, and how it stays correct as the
raising passes evolve. The companion docs split the concern: [decompiler.md](decompiler.md)
is the architecture (*how* output is produced), [decompiler-taste.md](decompiler-taste.md)
is *what* to render. This doc is the goal — *how we know the output is right*.
Tool invocation lives in the harness reference ([tools/DecompilerHarness/README.md](../tools/DecompilerHarness/README.md));
here we describe the strategy those tools serve.
The staged test/harness ladder — which check is the entry gate, which is the
final boss, and what evidence each PR should report — is in
[decompiler-correctness-pipeline.md](decompiler-correctness-pipeline.md).

## Correct by construction

The pipeline's floor is set in its shape, before any test runs:

- **No crash, no silent wrong output.** `IrImporter.Import` and
  `CSharpPrinter.PrintRaised` are exception-safe by construction. A one-method
  bug surfaces as a diagnostic comment and a lowered fidelity level, never a
  process crash or plausible-but-wrong text.
- **Honest degradation.** IL with no C# spelling becomes an explicit
  `UnsupportedNode` rendered as a visible `/* … */` comment, and the result's
  fidelity level drops accordingly (`Full` → `Partial` → `StructuredOnly` →
  `IlOnly` → `Failed`). Output never pretends to be more faithful than it is.
- **Machine-readable diagnostics.** Every degradation carries a stable
  Roslyn-style `DEC####` code with prose alongside, so CI triage and fallback
  routing read identifiers, not strings.

Everything below verifies that the output *above* the floor — the `Full`-fidelity
C# we claim is faithful — actually is.

## Checks, floors, and views

Correctness is anchored by construction plus weight of evidence — "pounds of
IL" — rather than per-expression semantic re-resolution. The verification surface
has a few distinct roles, and keeping them distinct is what makes "what proves
what" legible:

- **check** — renders a per-run verdict on each method: does *this* body compile,
  round-trip, or carry agreeing annotations? Each has a `--<property>-check` flag.
- **floor** — an aggregate threshold over the whole corpus, with no per-method
  verdict and no flag.
- **view** — shows you something with no verdict at all: `--gaps`, `--dump`,
  `--diff`, `--cfg`, `--facts`, `--remarks`, `--pass-impact`.
- **gate** — a check or floor wired into CI to hold a line. **oracle** — the
  reference of truth a check compares against (the original IL opcode stream).

### The three checks

Each is named for what it proves; a method can pass one and fail another, so they
are not redundant:

| Check | Question | Proves | Blind to |
| --- | --- | --- | --- |
| `--fidelity-check` | *Does it still mean the same thing?* | Semantic **fidelity**: decompile → recompile → compare the canonical opcode stream | Methods it cannot recompile (reported separately, not as diffs) |
| `--validity-check` | *Does it even compile?* | **Validity**: the rendered C# parses, is statement-legal, and binds | Whether valid C# is *faithful* (fidelity's job) |
| `--annotation-check` | *Do the IL annotations match the opcodes?* | **Annotation fidelity**: each allocation/unsafety/lifetime annotation agrees with the raw IL opcode at its offset (precision), and every unambiguous opcode produces its annotation (recall) | Whether the C# itself is right — only the annotations |

The deepest is **fidelity**: a body that compiles and reads plausibly but
recompiles to a different opcode stream changed the program — the worst failure
class, invisible to every check that never runs the output back through a
compiler. The supporting evidence:

- **The IL round-trip oracle.** Our disassembly reassembles (vendored managed
  ILAssembler, native `ilasm`) to byte-identical IL — the ground truth fidelity
  grades against.
- **Fixtures in both configurations.** Purpose-built methods whose *compilation*
  produces the IL shape under test, run in Debug *and* Release (the compiler
  emits structurally different IL per configuration; CI runs both).
- **Corpus sweeps.** Emit-all stress over each platform's CoreLib (three OSes in
  CI = three corpora), measured by the floors below.

### Expanding real-world fidelity coverage

The real-world corpus card exposes compile-back fidelity coverage honestly, but
coverage can be thin because many methods are not yet standalone-recompilable.
Increasing the cap is only a measurement step: it characterizes how much useful
opcode evidence exists today and buckets why the rest fails. The first expansion
target is therefore the **checked population inside the fixed corpus**, not a
larger random assembly set.

Use this order for risky decompiler work:

1. Run the fixed corpus with multiple `--corpus-fidelity-cap` values and record
   exact, opcode-diff, recompile-failed, and context-failed counts plus failure
   buckets.
2. For a risky raise/structuring PR, emit a per-method corpus delta and treat the
   changed methods as the fidelity population to cover. A bigger general sample
   is not enough if the changed methods remain unchecked.
3. Improve harness context only where failure buckets show many methods can be
   converted into useful opcode comparisons without changing the product path.
   The product decompiler stays SRM-only, NativeAOT-friendly, Roslyn-free, and
   free of inspected-assembly loading.
4. Add new corpus assemblies only when the current fixed corpus lacks enough
   examples of the target lowering family. Add pinned deterministic assemblies
   with a documented shape reason, refresh the baseline, and prove a no-op
   `--emit-corpus-delta` stays empty.

For #1175-class retained-label / forward-merge work, the interim bar is fidelity
coverage over the methods the PR changes, especially methods in the
forward-merge/structuring-residual population. If that population recompiles at a
lower rate than the corpus average, treat that as the next context-injection
target rather than declaring victory from an easier global sample.

#### Reconstruction closures and the safely-capturable population

Changed-method fidelity is most meaningful on **reconstructable** libraries. The
whole-module skeleton must stub every top-level type because the target assembly
cannot be referenced (the reconstructed type would collide), so the compile is
all-or-nothing: one un-reconstructable sibling type poisons every method in the
module, scoring targets `RecompileFail` for reasons unrelated to their own
fidelity. Two populations are structurally hostile to this and are *not* the
signal to chase: generated/synthesized members (regex source-gen output, lambda
display classes, iterator/async frames — classified out up front), and
Roslyn-class assemblies whose internal cross-assembly type graphs (e.g.
`Microsoft.CodeAnalysis` ↔ `.CSharp`) are too large and entangled to reconstruct
in isolation. On the Roslyn-heavy #1251/#1209 stress delta these dominate the
failures; the genuinely checkable rows are all non-Roslyn.

The harness's opt-in **reconstruction-closure (cluster) capture** (`CB_CLUSTER=1`,
see the [harness README](../tools/DecompilerHarness/README.md)) reconstructs only
the target's transitive closure rather than the whole module: emit the target
type, compile, and let the compiler name the missing same-assembly types it
still needs, recompiling until the unit binds or the closure stops growing. It
runs in **escalation** order — the cheap whole-module compile first, and only the
rows it could not check are escalated to the closure path, which reaches the same
checkable population as attempting the closure everywhere at a fraction of the
cost. A row falls back to its whole-module result when the closure cannot be
closed within budget, so it never regresses below the baseline — it only rescues
targets the all-or-nothing skeleton failed for unrelated sibling reasons. A
persistent bail is a principled *not-safely-capturable* classification rather than
a fidelity verdict, and the changed-method report prints the segmented
**safely-capturable bands** (checkable whole-module, checkable cluster-rescued,
not-safely-capturable) from each row's capture provenance. The current gain is
modest and library-shaped, and improves lever by lever as the closure learns to
resolve more of what the compiler names: namespace-segment inclusion (the
dominant `CS0234` bail, ~81% on Newtonsoft.Json), a synthetic parameterless
constructor stub for reconstructed classes whose base lacks one (the `CS1729`/`CS7036`
implicit-`base()` bail), and reconstructing sibling properties as property syntax
(the `CS1061` `obj.X` bail) together took Newtonsoft.Json exact-match from 7.9% to
43.4%; inherited/extension members (the rest of `CS1061`) are the next measured
lever. The point is the
inverse of cheating: a good cluster system lets honest changed-method fidelity
make real progress on non-pathological libraries instead of being held hostage by
an unrelated gap somewhere else in the module.

### Fixture idiom-shape scorecard

`IdiomShapeScorecardTests` is the fixture-backed C# altitude check. It renders
raised fixture bodies, parses the output with Roslyn, and asserts that the
expected syntax nodes appear (`TupleExpressionSyntax`, `UsingStatementSyntax`,
`LockStatementSyntax`, etc.) while lower-altitude substitutes stay absent. This
is deliberately not a compile/fidelity check: valid C# can still be the wrong
idiom, such as a switch expression recovered as a switch statement.

Run it after changing a raising pass or printer path that can affect C# shape.
The current ratchet records the fixture idioms that are recovered today; when an
owed fixture starts passing, flip its scorecard entry to recovered in the same PR
so the aggregate score moves forward and future regressions fail.

The scorecard is a positive altitude signal, not a soundness proof. A raise that
recovers the target idiom can still be too broad, so each new or expanded raise
should also add at least one adversarial fixture when a nearby non-idiom shape is
plausible. Good negative fixtures include hand-written spellings that resemble
the lowering, older-C# equivalents, unsigned/null/pattern variants, and source
that differs only in an important compiler discriminator such as a receiver spill.
Pin those in pass-level tests or the fidelity gate; the scorecard keeps the
positive ratchet.

As the scorecard saturates, adversarial research becomes more valuable, not
less. A high recovered ratio means the curated positives are mostly climbing; it
does **not** mean the raises are sound around their edges. Do not add easy
scorecard rows just to keep the number moving. Once the obvious positive climbs
are recovered, shift more target selection toward adversarial passes over recent
or broad raises and toward hardening `Partial` ledger rows.

### Choosing high-value targets

Pick targets for value, not just availability. Use this vocabulary in planning
and PRs so the work stays tied to visible progress:

| Target kind | What it means | Good signal |
| --- | --- | --- |
| **Scorecard climb** | Raise a visible source idiom to a higher C# altitude, or shrink an owed ledger row. | A scorecard/ledger row changes, a floor improves, or the output diff is source users recognize. |
| **Bug hunt** | Fix demonstrated wrong, invalid, or fidelity-breaking output. | A negative fixture or corpus method fails before the change and passes after. |
| **Adversarial pass** | Try to falsify a recent or fragile raise with near-miss negative fixtures. | A positive/negative pair differs by one compiler discriminator. |
| **Predicate hardening** | Move exact rewrite-gate evidence into `MemberIdentity`, `GeneratedCodeIdentity`, or `PlaceIdentity`. | A concrete pass customer exists, a duplicated check is drifting, or fuzzy matching already caused a bug. |

Terminology matters. Use **decompiler substrate** for the broad family of shared
pass-evidence layers. Use **identity predicates** for the exact rewrite gates the
passes call (`MemberIdentity`, `GeneratedCodeIdentity`, `PlaceIdentity`). Avoid
**fact substrate** in quality docs: "facts" already names hidden-fact
annotations and sidecar ledger metadata, while these predicates are private
rewrite gates.

A high-value target has at least one of these signals:

- It is a scorecard climb users recognize in source (`await`, `foreach`,
  tuple/deconstruction, interpolation, using/lock, switch/range).
- It fixes a demonstrated false-positive or fidelity/validity failure, ideally
  with a negative fixture that fails before the change.
- It strengthens an adversarial pass for an active or recently changed raise.
- It consolidates identity predicates with a concrete consumer, especially after
  an adversarial review found the same category of bug.
- It improves a CI gate or measured corpus floor, not just formatting of an
  already-correct shape.

Prefer work in this order: correctness bug > validity failure > scorecard climb
> predicate hardening with a concrete consumer > adversarial pass for an active
raise > cosmetic readability. After a burst of obvious predicate wins, bias back
toward scorecard climbs and adversarial passes. Avoid speculative helpers,
one-off cleanup, or broad rewrites that do not move a measured signal. When
choosing among similar targets, pick the one with the clearest discriminator and
the smallest adversarial fixture pair.

For fold-before-structuring control-flow work, apply the
[pre-structuring normalization layer](design/control-flow-structuring.md#pre-structuring-normalization-layer)
contract before adding another pass-local liveness, branch-ownership, or EH
legality model. New normalizers should satisfy that checklist and use shared CFG,
identity, and slot/place helpers rather than growing adjacent mini dataflow
engines.

When the obvious target queue dries up, stop the random breadth hunt. A run that
mostly finds stale ledger wording, tiny pins, or overlapping branch ideas has
crossed from discovery into coordination overhead. At that point, pick one of
three modes deliberately:

| Mode | Use when | Good output |
| --- | --- | --- |
| **Stabilization** | Several PRs are open or recently merged in adjacent pass families. | Merge-conflict fixes, CI follow-through, and no new feature overlap. |
| **Curation** | Recent raises changed the truth of sidecars, ledger notes, or test intent. | Small PRs that update scorecard/ledger/sidecar/test classification only. |
| **Scoped climb** | The next target is a large frontier item rather than a one-PR slice. | A short design plan and ownership of one area, not drive-by broad edits. |

Prefer validity/corpus bug hunts while the PR queue is busy; they are usually
independent and grounded by failing methods. Do not start another small raise in
a pass family that already has active branches unless it directly fixes one of
them. If choosing a large climb — classic async state machines,
positional/list patterns, deeper state-machine/PDB work, or checked-block
synthesis — write down the slice and expected discriminators before coding.

Read progress as **leverage**, not elapsed time. The ledger gives breadth: `None`
rows are owed mechanisms, `Partial` rows are the live frontier, and `Full` rows
are solved enough for the current lowering category. The scorecard gives
altitude on curated fixtures: a climb means a recognizable source idiom is back,
not that soundness has been proven for every nearby lowering. A strong progress
review asks whether a row moved from `None` to `Partial`/`Full`, a `Partial` note
got narrower, a scorecard fixture climbed, an adversarial pass added a useful
near-miss, predicate hardening removed a fuzzy rewrite gate, or a corpus floor
improved without losing fidelity.

Use the ledger and scorecard together, but do not optimize either one in
isolation. The ledger selects terrain; the scorecard pins a visible altitude
target; adversarial passes, fidelity/validity checks, and corpus floors prove
the climb did not over-match. Do not add easy scorecard cases just to inflate the
ratio, and do not mark a ledger row `Full` until fixtures and adversarial shapes
cover the meaningful variants of that lowering. A `Partial` row with a sharper
note, stronger fixtures, and safer predicates is real progress even if the raw
bucket count does not change.

Treat a near-full scorecard as a mode switch. The default question changes from
"can we recover this idiom at all?" to "where could this recovered idiom
over-match?" Good follow-up targets are recent broad raises, pass families with
many `Partial` notes, and places where a single missing discriminator could turn
source-like output into different IL.

When the scorecard is fully recovered, the default mode changes again: do not add
more easy positives just to keep motion visible. Either stabilize/curate the
recent wave, take validity/fidelity bugs from measured signals, or explicitly
scope a larger climb. A new scorecard row should represent a meaningful frontier,
not a small variant of an already-recovered shape.

### Curating uncoordinated raise work

When several agents work independently, add periodic **curator passes**. A
curator pass is documentation/test metadata work, not a new raise by default: it
normalizes the work queue so future agents do not chase stale labels or mistake
guardrails for frontier work.

The curator checks:

- **Scorecard entries are positives only.** A near-miss that must stay lowered
  belongs in pass-level tests or a fidelity gate, not in `IdiomShapeScorecardTests`.
- **Adversarial tests name the intent.** Negative guardrails should say
  `IsNotRaised`, `Stays...`, `FallsBack...`, or equivalent, and assert absence of
  the raised node plus survival of the source/lowered shape when practical.
- **Owed positives are not called adversarial.** If a fixture is a real source
  idiom we want to recover, record it as scorecard/ledger work (or a `Partial`
  note), not as a negative near-miss.
- **Sidecar facts are current.** `PositiveCoverage`, `AdversarialCoverage`, and
  `MissingDiscriminator` should be updated after a raise lands; recovered shapes
  must move out of missing/adversarial text.
- **Ledger notes describe today's frontier.** A `Partial` row with stale owed
  text is worse than no note: it sends the next agent to the wrong target.

Good curator PRs are small and boring: rename/comment tests, update sidecar
coverage strings, sharpen ledger notes, and avoid behavior changes unless the
curation exposes an actual bug. Run the relevant catalog/fixture tests so the
metadata still points at real rows and mechanisms.

Operational burndown queue hygiene — stale rows, merged PR status, merge
conflicts, CI breaks, rebaseline triggers, and subagent delegation — is the
**Burndown Curator** role. Its protocol starts in
[burndown-curator.md](burndown-curator.md), with role personas under
[`../agents/`](../agents/).
Burndown row ownership is hot-start work: a claimed row should proceed to a PR,
explicit blocker, or pivot issue rather than waiting or stopping at an internal
milestone.

### PR-intent-informed adversarial review

Use a separate **Decompiler Adversarial Reviewer** role when the concern is not
"is the queue metadata honest?" but "is this raise actually sound?" The curator
keeps the map current; the adversarial reviewer tries to falsify the map's
claims. Pick two reviewers from the model roster in the AGENTS.md
[Adversarial Review](../AGENTS.md#adversarial-review) section, never your own
model.

This is different from simply "creating adversarial fixtures." A fixture is one
artifact the review may produce; the role is the upstream proof audit that
decides which fixture matters. The reviewer reconstructs the intended proof of
the raise, checks whether the current matcher still implements exactly that
proof, and only then adds a near-miss fixture, narrows a gate, updates sidecar
coverage, or files a larger issue.

The reviewer works from a review packet:

- the original PR/commit and its stated intent;
- the pass and printer/substrate files that implement the raise today;
- the ledger row, sidecar facts, scorecard positives, and pass-level fixtures;
- the intended discriminator: the one compiler/runtime fact that proves the
  lowered IL came from this source idiom and not a nearby manual spelling.

Start each review by writing the claim in this form: "This pass may raise only
when **X** proves source idiom **Y**." Then compare that claim to the current
matcher, not just to the PR diff. Audit whether the gates are still exact:
member/type identity must be assembly+signature based, place identity must
preserve re-evaluation and aliasing rules, hidden locals and source-named locals
must not be confused, PDB/no-PDB dependencies must be explicit, side effects and
evaluation order must round-trip, and cross-assembly/user lookalikes must stay
lowered.

The useful output is one of three things:

- a failing near-miss negative fixture, followed by a narrowed matcher;
- no bug found, plus sharpened `AdversarialCoverage`/`MissingDiscriminator`
  sidecar text that records the reviewed edge;
- a pattern-pivoted issue with minimized examples when the bug or gap is larger
  than one safe PR.

When the review is posted to a PR, include links to the commit(s) that resolved
actionable review guidance. If guidance is dismissed or left for a follow-up,
state why no resolution commit applies and link the follow-up issue when one
exists.

**When to run this role.** It is a targeted review lane, not a universal CI
gate, so spend it where a raise's breadth is least proven:

- A recent or broad raise PR, especially one that changes a **discriminator**
  (the fact that proves a source idiom) or a shared **declaration/naming
  heuristic** — not just one narrow added pattern.
- A **validity-motivated** fix (it kills a `CSxxxx`): the inverse of an
  over-broad validity fix is *silent wrong output*, which no validity check
  catches, so a green validity number is not evidence of soundness.
- A PR touching a shared **printer/substrate path** (the printer, identity
  predicates, definite-assignment) rather than one pass, so its blast radius
  spans many methods.
- A high-risk `Partial` ledger row, or a pass whose tests prove **examples but
  not the discriminator**.
- Any raise where a near-miss source shape is plausible: covariant types,
  slot/temp reuse, unsigned/null comparisons, provider/format overloads, or
  aliasing.

Skip it for curation/metadata-only or formatting-only PRs, and for a raise
already bounded by one-discriminator negative fixtures **and** a
baseline-compared corpus read.

#### Claiming a review-queue row

Adversarial review targets are often tracked as a table in a tracking issue, one
row per pass family — for example, issue #959, "Decompiler adversarial review
target queue." (That issue is one instance of the pattern; the live queue may
move to a different issue over time, so treat the number as an example, not a
fixed address.) Because several agents may work the queue in parallel, claim a
row before starting so passes stay focused and conflict-light.

Add a **Status** column to the table and move a claimed row through these states:

- `Open` — available to take.
- `🔵 In progress — <branch or @owner>` — claimed; one owner per row.
- `👀 In review — #<PR>` — PR open, awaiting sign-off.
- `✅ Done — #<PR>` — merged / signed off.
- `🔀 Pivoted — #<issue>` — finding was larger than one safe PR; tracked elsewhere.

Edit the Status cell three times over a review's life: to claim the row, when the
PR opens, and at merge. Keep each review to one row. The issue body is the single
source of truth, and in-place edits are last-write-wins — for a hot queue, post a
short claim comment before editing, or split the table into per-row sub-issues.
Update the issue with the GitHub CLI so the change is scriptable and reviewable:

```bash
gh issue view 959 --json body -q .body > /tmp/queue.md   # current body
# edit only the claimed row's Status cell in /tmp/queue.md
gh issue edit 959 --body-file /tmp/queue.md
```

Always work a claimed row in a dedicated **git worktree**, never in the main
checkout, and never share one worktree across unrelated rows. Adversarial review
touches the same hotspots as raise work (`LoweringCoverage`, `IrPasses`,
`CfgSampleClass`, the sidecar fact providers, and the scorecard), so two rows
sharing a tree will collide. A per-row worktree keeps each review isolated,
lets reviews proceed in parallel, and makes the upstream sync in the pass loop
below clean. Tear the worktree down once the row's PR merges.

#### Useful tool patterns

These keep a review fast and the proof legible:

- **Build the review packet from git, not memory.** `git log --oneline --
  <pass-file>` lists the PRs that introduced and broadened a raise, and
  `gh pr view <n> --json title,body` recovers each one's stated intent.
  Reconstruct the claim from that history before reading today's matcher.
- **Run the pass's tests in isolation.** The full decompiler suite is slow, so
  filter to the class under review —
  `dotnet run --project src/ILInspector.Decompiler.Tests -- -class
  ILInspector.Decompiler.Tests.<PassTests>`. Run the full suite once for a
  baseline so you can separate pre-existing failures (for example the
  fidelity-gate docket) from regressions you introduce.
- **Run the fixtures in `-c Release`, the configuration CI uses.** csc emits
  structurally different IL per configuration, and several raises only fire on the
  Release shape — the tuple `==` lowering, for example, becomes a non-raising
  ternary in Debug. A default Debug run can therefore show every positive fixture
  failing with an empty collection and the whole suite red; that is a config
  artifact, not a regression. Match CI:
  `dotnet run --project src/ILInspector.Decompiler.Tests -c Release`.
- **Prefer synthetic IR for near-miss negatives.** Many discriminators
  (non-local targets, field/temp receivers, user-assembly lookalikes) are awkward
  or impossible to spell in C# source but trivial to build directly as IR in the
  test, mirroring the existing builder helpers in the pass's test file.
- **Decompile a throwaway assembly to see the real lowering.** When a near-miss
  *is* expressible as C#, write the methods in a scratch library, build it in the
  configuration under test, and decompile it directly in a file-based app
  (`MetadataSource.Open` → `IrImporter.Import` → `IrPasses.Run` →
  `CSharpPrinter.Print`). This shows csc's actual lowering and the real raised C#
  for any source shape, and — unlike adding methods to `CfgSampleClass` — keeps the
  probe out of the fidelity gate and corpus snapshots. Use IR builders for shapes
  C# cannot spell; use a scratch assembly for shapes it can.
- **Dump pre-pass IR by slicing the pipeline.** To see the evidence a
  discriminator rests on, run a prefix of `IrPasses.Default` up to (but excluding)
  the pass under review and print the block children. Reading the actual spill
  prologue and node shape beats reasoning about it — it is how you confirm which
  operands are hidden temps and which comparison consumes each one.
- **Keep near-miss negatives out of `CfgSampleClass`.** The fidelity gate and the
  corpus floors scan `CfgSampleClass` by name, so probe methods added there
  pollute the opcode-diff snapshots and surface as dozens of unrelated floor
  failures. Put pass-level negatives in the pass's own adversarial sample class
  (for example `TupleBinaryAdversarialSamples`), which those gates do not scan.
- **Diff the corpus report against a clean baseline worktree, not the PR's quoted
  numbers.** When a fix changes a broad heuristic, build a detached worktree at
  the PR's *base* commit and run the same `--library-report` command on both, then
  diff every validity/fidelity row. A fix can hold its headline metric while
  trading one defect class for another, or while a conservative fallback silently
  regresses a different bucket — both invisible against a remembered number. The
  PR body's count is a claim to reproduce, not a baseline.
- **Record the reviewed edge even when no bug is found.** Move the edge out of
  the sidecar's `MissingDiscriminator` and into `AdversarialCoverage` so the next
  reviewer reads it as proven rather than still owed.

### Decompiler quality diff PR card

Decompiler PRs should include a compact **Decompiler quality diff** card when
they can affect raising, structuring, validity, fidelity, or corpus behaviour.
The model is the current dotnet/runtime JIT review posture: evidence is
tool-driven (`jit-diff`, SPMI/PMI diff jobs, benchmark artifacts, or linked
EgorBot benchmark runs), with small before/after codegen excerpts when a local
shape matters. Newer JIT PRs do not always paste a full table into the body; they
often link the generated diff job and summarize the verdict. The invariant is
that the numbers come from a reproducible tool artifact, not from a reviewer or
agent re-keying measurements. Roslyn performance PRs are similar in spirit but
less uniform: they use BenchmarkDotNet tables, Speedometer/PR-validation links,
allocation/trace snippets, and reviewer-requested reruns rather than one
standard card.

Generate corpus rows with the harness
(`--diff-corpus-baseline --quality-diff-card`) and paste the harness output into
the PR. Use one of the documented corpus/baseline pairs, never a mixed pair:

- **PR quick card:** `eng/prepare-decompiler-pr-corpus.sh` with
  `tools/DecompilerHarness/corpus/pr-quick-baseline.json`.
- **Daily/manual real-world card:** `eng/prepare-decompiler-corpus.sh` with
  `tools/DecompilerHarness/corpus/real-world-baseline.json`.

Mixing the full corpus script with the PR quick baseline (or the reverse) makes
the card compare different populations and can produce bogus assembly
additions/removals and misleading aggregate deltas. Do not ask an agent to
construct or re-key the table: that is wasteful, open to hallucination, and
harder for a reviewer to validate. Method-level examples are the only
hand-authored addendum, and only when the PR intentionally changes behaviour. The
card is reviewer-sized evidence, not a dump-stage artifact; keep `--dump
--steps` output in linked diagnosis notes only when reviewers need the
drill-down. To reproduce the full row set behind a capped card, follow
[Reproducing decompiler corpus deltas](decompiler-corpus-delta-repro.md).
Use the terse PR body shape in
[docs/templates/decompiler-pr.md](templates/decompiler-pr.md) when writing the
human summary around the generated card.

Rate deltas in card prose use **percentage points** (`pp`): `+0.49 pp` means
the rate increased from, for example, `82.17%` to `82.66%`. Counts still use raw
signed count deltas in `Count delta`.

For behaviour-preserving refactors, the existing #1174/#1166 real-world corpus
sensor values are enough when you need the broader daily/manual signal:

Run the sensor with the same command documented in the harness README. The
`--quality-diff-card` flag is what emits the PR-ready Markdown block:

```bash
dotnet build src/dotnet-inspect -c Release -p:PublishAot=false
bash eng/prepare-decompiler-corpus.sh /tmp/corpus-assemblies.txt
mapfile -t assemblies < /tmp/corpus-assemblies.txt
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --diff-corpus-baseline tools/DecompilerHarness/corpus/real-world-baseline.json \
  --quality-diff-card \
  --compile-cap 25 \
  --corpus-fidelity-cap 3 \
  --max-examples 3
```

For risky raise or structuring PRs, add `--quality-card-risky`. It keeps the
card generated from the same snapshots, but adds a thin-coverage warning when the
semantic validity sample is below 1.00% of methods or the compile-back fidelity
sample is below 0.10%. That warning means the aggregate card is not enough by
itself; add method-level improved examples and still-flat near misses.

The tool emits a block like:

```md
### Decompiler quality diff

Corpus: #1166 real-world decompiler corpus sensor: #1150 pinned NuGet assemblies plus dotnet-inspect managed assemblies. 14 assemblies, 87,907 methods
Correctness coverage: validity sampled 350 / 87,907 (0.40%); fidelity sampled 6 / 87,907 (0.01%)

| Metric (desired direction) | Baseline | PR | Count delta |
| --- | ---: | ---: | ---: |
| Fully raised (+) | 77,376 (88.02%) | 77,376 (88.02%) | 0 |
| Conditional-branch residual (-) | 2,298 (2.61%) | 2,298 (2.61%) | 0 |
| Forward-merge stops (-) | 2,290 (2.61%) | 2,290 (2.61%) | 0 |
| Full malformed (-) | 165 | 165 | 0 |
| Semantic defects (-) | 4/350 — sampled 350 / 87,907 (0.40%) | 4/350 — sampled 350 / 87,907 (0.40%) | 0 |
| Fidelity diffs (-) | opcode-diff 1/6, exact 5, recompile-failed 0, context-failed 0; sampled 6 / 87,907 (0.01%) | opcode-diff 1/6, exact 5, recompile-failed 0, context-failed 0; sampled 6 / 87,907 (0.01%) | 0 |
| Pass bugs (-) | 0 | 0 | 0 |

Verdict: corpus sensor matched baseline tolerances.
```

For raise or deliberate behaviour PRs, keep the generated table and add targeted
examples:

```md
Improved examples:
- Type::Method — `structuring: conditional-branch` -> Full

Still-flat near miss:
- Type::Other — declined because the discriminator is missing / readability
  failed / fidelity would regress.
```

Full malformed rows should be root-cause bucketed in a linked library report,
validity-defect report, or issue when they are relevant to the PR. The generated
card intentionally stays short; the linked report carries the long tail.

Read movement as correctness evidence, not a JIT-style tradeoff budget.
Acceptable movement is: completeness improves while validity/fidelity stay flat,
validity/fidelity defects shrink, or a Full -> Partial change is an explicit
honesty correction that stops overclaiming. Do not normalize new pass bugs, new
Full malformed/bound defects, new fidelity opcode diffs, or broad "correct but
uglier" output without an explicit design approval and readability gate.

For compiler/runtime expert review, optimize the code and tests for legible
proof obligations:

- every non-trivial raise should have an obvious claim, discriminator, and
  failure mode;
- exact identity checks should live in substrate predicates (`MemberIdentity`,
  `GeneratedCodeIdentity`, `PlaceIdentity`) rather than name/string folklore;
- matcher breadth should be justified by positive fixtures and bounded by
  one-discriminator negative fixtures;
- comments should explain why an IL shape proves a source idiom, not narrate what
  the code already says;
- sidecar facts and `Partial` ledger notes should describe today's frontier so a
  reviewer can see what is proven, what is deliberately unraised, and what is
  still owed.

### Stepper semantic audit

Use a separate **Stepper Semantic Auditor** role when the concern is not final
outcome or matcher breadth, but whether each intermediate rewrite is legal under
ECMA-335 IL semantics and the pipeline's own phase contract. This is the review
lane for a compiler/runtime expert who wants to see the exact moment where a pass
overclaims what a prior phase could have proven.

The three review lanes answer different questions:

| Lane | Question | Artifact |
| --- | --- | --- |
| Outcome correctness | Does the final output compile, round-trip, and recover the expected altitude? | gates, corpus reports, defect diffs |
| PR-intent adversarial review | Does this matcher over-raise near-miss shapes? | negative fixtures, narrowed predicates, sidecar coverage |
| Stepper semantic audit | Is this rewrite legal at the exact step where it happens? | step traces, pass-contract notes, illegal-transform fixtures |

Start each audit by writing the phase claim: "At step **N**, pass **P** may
rewrite shape **X** because prior phases guarantee **Y**, and no legal IL shape
can violate **Z**." Then run the specimen through:

```bash
dotnet run --project tools/DecompilerHarness -c Release -- <assembly> \
  --dump 'Type::Method' --steps --diff --cfg --facts --remarks
dotnet run --project tools/DecompilerHarness -c Release -- <assembly> \
  --dump 'Type::Method' --step-limit N --diff --cfg --facts --remarks
```

The auditor reads the import tree, each pass boundary, and the fine-grained step
log to find the first illegal transformation, not just the first bad final
render. Useful specimens are intentionally small but semantic: byref and managed
pointer flows, stack-slot reuse/live ranges, `dup`/spill provenance, `leave`,
filter, and `finally` boundaries, constrained calls, volatile/pinned locals,
switch/branch target regions, bool/int/type joins, and any pass that removes a
store, block, or EH region based on reachability assumptions.

The useful output is one of three things:

- an illegal-transform fixture that fails at the offending pass/step, followed by
  a narrowed pass contract;
- no bug found, plus a short pass-contract note explaining the invariant the
  stepper trace proves;
- a pattern-pivoted issue when the pass needs broader phase-contract redesign
  rather than one safe narrowing.

Run this role for broad control-flow/dataflow changes, validity fixes that touch
shared phase assumptions, and any pass where an expert reviewer would ask "what
exactly did the previous phase prove before this rewrite ran?" Do not use it for
metadata-only curation or simple one-discriminator matcher hardening; those
belong to curator or adversarial-review lanes.

The intended pass-improvement loop is:

1. Pick one high-value target type: a **scorecard climb** (new or improved raise
   for an owed/recoverable idiom), a **bug hunt** (demonstrated wrong output), an
   **adversarial pass** (try to falsify an existing raise), or an
   **predicate hardening** task (shared exact evidence with a concrete pass
   customer). Apply the high-value filter above before starting; do not spend a
   PR on a target that lacks a measurable signal or a concrete consumer.
2. Create a dedicated feature worktree for the raise task. Raise work touches
   common hotspots, so do not work directly in the main checkout or share one
   worktree across unrelated raise/adversarial/doc tasks.
3. Add or identify fixture methods that represent the source idiom.
4. For new/improved raises, add scorecard entries as unrecovered when the
   current output is lower altitude.
5. Add adversarial fixtures for nearby shapes that must not raise.
6. Run the scorecard to capture the baseline.
7. Implement or improve the raise pass. For a pure adversarial-discovery task,
   do not broaden the pass; narrow it only if the negative fixture exposes an
   over-match.
8. Run the scorecard again and flip newly recovered entries in the same PR.
9. Before pushing or opening the PR, synchronize with current upstream
   `origin/main` (or recreate the work from it), resolve conflicts locally, and
   re-check files that frequently collide in raise work: `LoweringCoverage`,
   `LoweringCoverageTests`, `IrPasses`, `CfgSampleClass`, and the scorecard.
10. Run fidelity/validity checks after that upstream sync to prove the final
   branch state, not just the pre-merge local state, stayed
   semantic, valid C#.

After a raise looks good, run the **Decompiler Adversarial Reviewer** before
merge. This is a review pass, not an IR pipeline pass: give the reviewer the
raise packet above and ask it to add or update fixtures that try to falsify the
match without changing the implementation first. The useful output is concrete:
a source-shaped negative fixture, the exact discriminator it toggles, and the
test that proves the pass does not raise it. When the pass is too broad, add the
negative fixture first so it fails for the current implementation, then narrow
the matcher. In the PR-visible review summary, link the commit(s) that resolved
the actionable review guidance.

Adversarial research has three useful modes. Use the cheapest one that can
answer the question, and escalate when the pass is broad, recent, or tied to an
important `Partial` row:

| Mode | Use when | Expected output |
| --- | --- | --- |
| **Memory adversarial** | A new raise has an obvious nearby shape or discriminator. | A small synthetic positive/negative fixture pair. |
| **Corpus adversarial** | A broad/recent raise needs real-world pressure. | A real method shape, minimized fixture, and pass-impact or fidelity signal. |
| **Reference-source adversarial** | The discriminator depends on compiler/runtime/framework idiom knowledge. | A documented target family from runtime/ASP.NET/Roslyn source, then a synthetic fixture. |

Memory adversarial work is fast and catches obvious traps: name lookalikes,
unsigned comparisons, extra statements, aliasing, reassignment, overload or
provider differences, and missing metadata evidence. Corpus and reference-source
research are more expensive, but they find shapes people do not usually invent:
generated code, old idioms, nested control flow, framework-specific patterns,
and optimization artifacts. Prefer real-source mining when a `Partial` row is
important and the memory-derived fixture matrix has stopped finding new
discriminators.

Good adversarial fixtures are usually positive/negative pairs that differ by
one compiler discriminator. Examples: the real `^1` lowering spills the receiver
once while hand-written `a[a.Length - 1]` reloads it; an interpolated string
uses a hidden handler temp while hand-written `DefaultInterpolatedStringHandler`
uses a source local; a signed guard-return comparison can fold to `?:` while an
unsigned bounds-check guard must stay in statement form because the ternary
recompiles differently; a compiler lowering calls one exact overload while
nearby source uses an overload with extra provider/format/alignment arguments.
Common false-positive traps are name-only method matches, ignored constructor or
call operands, accepting any same-shaped local instead of a compiler temp, slot
aliasing/reuse, unsigned/null comparisons that depend on branch codegen, and
formatted/provider overloads that carry semantics the candidate syntax cannot
represent. Pin positives in the idiom scorecard; pin near-miss negatives in
pass-level tests or the fidelity gate.

### The two floor-only properties

Two properties have no per-method check at all — they exist only as corpus
floors, enforced by `CorpusSweepGateTests`:

- **completeness** — the fully-raised %, surfaced by the `--gaps` *view*. A method
  is incomplete iff its raised tree still holds unstructured control flow (a
  surviving `goto`) or an `UnsupportedNode`. `--gaps` measures the residual; it
  renders no per-run verdict, which is why it is a view, not a check.
- **pass-safety** — zero pass-bugs / exceptions over the whole corpus, pinning the
  by-construction safety. No flag; observed only in aggregate.

(Fidelity, being a check, *also* contributes an aggregate floor to the same
sweep — its corpus `Full`-fidelity % — but unlike these two it has a per-method
form. So `CorpusSweepGateTests` enforces three thresholds: pass-safety, the
`Full`-fidelity floor, and the completeness floor.)

## What gates CI

The durable, blocking guard is the **fixture fidelity gate**:
`FidelityGateTests` (and its lowered twin `LoweredFidelityGateTests`)
decompile every method of `CfgSampleClass`, recompile each inside a reconstructed
type skeleton, and fail CI when a method newly recompiles to a different opcode
stream — a regression beyond the documented `KnownDiffs` docket — or when a
`PinnedExact` method (a previously-fixed one) regresses. Shrinking `KnownDiffs`
and growing `PinnedExact` is how fidelity progress ratchets forward and cannot
slip back. The **annotation gate** (`AnnotationGateTests`) holds annotation
fidelity over the whole CoreLib corpus the same way — precision is absolute (a
wrong fact always fails; it is never runtime drift), recall is held above a
floor. The decompiler unit suite (`ILInspector.Decompiler.Tests`) and the IL
round-trip sweep (`DotnetInspector.ILRoundtrip.Tests`) gate the importer, the
passes, and the disassembler.

Breadth is gated separately by `CorpusSweepGateTests` (next section), which
enforces health **floors** — pass-safety, the aggregate `Full`-fidelity %, and
completeness — over the whole CoreLib corpus. The fixture and annotation gates
are the depth signals; the
corpus sweep is the breadth signal. The exploratory corpus checks and views
(`--fidelity-check`/`--validity-check` over a real assembly, the `--gaps` view,
`--pass-impact`) stay **developer-driven** — run them while working and read
them in review, but only the gates and the sweep's floors block CI.

## The corpus breadth gate

The fixture gate is strong but narrow (~80 curated methods), and the corpus
checks and views above are manual. The breadth net is **`CorpusSweepGateTests`** — the
new-pipeline analog of the old stack's no-crash sweep, made objective:

- It runs every method of the running runtime's CoreLib through `IrImporter →
  IrPasses` and the fidelity/gap classification (the SDK pins the version, so the
  corpus is stable). Cheap — no recompile.
- It asserts **floors, not exact baselines**: zero pass-bugs / exceptions
  (pass-safety), the `Full`-fidelity % (the fidelity check's aggregate floor), and
  the fully-raised % (completeness) above ratchets a couple points below the
  measured numbers. Floors tolerate minor
  runtime-version drift and need no per-method baseline file — which is why this
  beats both fuzzy text agreement and a brittle exact-baseline net.

So a crash, or a broad fidelity/raising drop, fails CI, using only the
self-contained signals. When the structuring work raises the true numbers, the
floors ratchet up to lock the gain in. Beyond breadth, deepening *fidelity*
coverage means growing the fidelity fixture corpus, not widening the sweep.

## Multi-mode coverage

Both the corpus gate and the fixture gates measure **one compiler mode** — the
one the running CoreLib (and the repo) shipped in: `runtime-async=on`, updated
memory-safety rules, Release, current LangVersion. But the same C# lowers to
different IL under different compiler flags, and the decompiler must read *every*
assembly, not just same-mode ones. So a single-mode sweep has blind spots that
read as false confidence: classic async state machines report **0** in CoreLib
not because they are handled but because runtime-async CoreLib never contains
them. The standout splits are **async** (runtime-async call vs classic
`AsyncTaskMethodBuilder` state machine — two unrelated lowerings), **memory
safety** (updated vs legacy unsafe contexts), **checked arithmetic**, and
**downlevel framework/LangVersion** (which cascades several old lowerings at
once).

The instrument is the **multi-mode fixture matrix**: mode-sensitive fixture
source is compiled with one flag flipped, so a construct is measured in both
lowering modes. When one source is legal in both modes, reuse it; when a mode
changes source legality or required spelling, use paired representative source
that produces the same IL and differs only in mode metadata. It is small by
construction — only mode-sensitive fixtures get thin per-flag overlay assemblies
— so the cost is one default plus a few shrinking single-flag overlays, never the
corpus times N. The active axes are the `runtime-async=off` overlay
(`Fixtures.ClassicAsync`), the checked-arithmetic overlay
(`Fixtures.CheckedArithmetic`), and the old/new memory-safety pair
(`Fixtures.LegacyUnsafe` / `Fixtures.NewUnsafe`, plus `UnsafeChainA/B/C` for
cross-assembly `RequiresUnsafeAttribute` resolution). The mechanics, axis
switches, and recipe for adding an axis live in
[the harness README](../tools/DecompilerHarness/README.md), "Multi-mode fixture
matrix".

This is a **discovery and bring-down instrument, on-demand — not a CI gate.** It
feeds the quality loop from the other end than the corpus does:

- **Discover.** `--library-report` over an overlay surfaces unsupported-pattern
  buckets the single-mode corpus could never produce. Each bucket is a real,
  named gap — a new target, grounded by failing methods, in a mode that matters
  in the field but is absent from CoreLib.
- **Bring down.** Raise the idiom for a bucket and re-run the report; the count
  shrinks method by method. That count is the tracked signal — the multi-mode
  analogue of a ledger row or scorecard ratchet, but for a lowering mode instead
  of a source idiom.
- **Guard the shipped mode.** The default corpus sweep and the CI gates still
  hold the line on the mode the framework actually ships, so teaching a new mode
  cannot silently regress it. Pair the overlay report with the usual
  `--diff-validity-defects` / `--fidelity-check` on the default corpus.

So the corpus gate answers "did we regress the default mode broadly," and the
matrix answers "do we handle this lowering mode *at all*" — complementary signals,
and the matrix is where a mode the corpus omits becomes measurable work rather
than an invisible gap. When you discover such a mode, add its overlay (the recipe
in the harness README) so the gap turns into a counter someone can drive down.

## From report to ownable issues: pivot on the pattern

A measurement report — `--library-report` over a real library, a mode overlay, or
the corpus defect map — is only useful if its findings become work someone can
own without colliding with others. The rule: **file issues pivoted on the
pattern, not on the library or the assembly.** A report carries both pivots (a
per-pattern section and a per-library section); the per-pattern one is the unit of
work.

- **One issue per pattern.** The title is the owed raise (the source idiom or
  lowering a single pass recovers — "raise classic async state machines," "raise
  the `fixed (T* p = array)` array-pin form"). The body lists that pattern's
  **hits** — the methods that exercise it, copied from the report, with a couple
  of example renders. The hits are the owner's ready-made test set and definition
  of done (the count goes to zero).
- **One pattern, one agent, end to end.** A pattern maps to one pass family, so an
  agent can own building out that raise — discovery, the pass, the fixtures, the
  bring-down — without touching another agent's area. Where a pattern's
  mechanism-level buckets share one root (classic async surfaces as both a
  `structuring` residual on `MoveNext` and a `fidelity` stop on the kickoff),
  group them into a single issue so one agent owns the whole raise.

**Why pattern, not library.** A library is a *bundle* of unrelated patterns; an
issue per library forces one agent to span several pass families, or several
agents to edit the same library's many patterns and conflict. Pivoting on the
pattern gives each agent an exclusive, conflict-light raise and keeps the
"do not start a raise in a pass family that already has active branches" rule
(see [AGENTS.md](../AGENTS.md)) easy to honour — the issue *is* the claim on that
family. Library-pivoted reporting stays useful for portfolio triage ("which
libraries are worst"), but the **issues** that drive raise work are
pattern-pivoted.

## The quality loop: detect, then diagnose

The harness modes pair into a loop, both ends anchored on the **same final C#**
the product emits:

- **Detect at scale.** `--fidelity-check` finds *which* methods regressed (opcode
  diffs across an assembly); `--gaps` finds which lost completeness;
  `--pass-impact` shows a pass's blast radius before and after a change.
- **Diagnose one.** `--dump` (with `--diff`, `--facts`, `--cfg`, `--remarks`)
  drills into the per-pass IR of a single method to find which pass introduced
  the divergence; `--steps` / `--step-limit` narrows to a single rewrite.

The connection is load-bearing: the final stage `--dump` shows is byte-identical
to what `--fidelity-check` grades, so there is no drift between what you inspect
and what is measured.

Two gotchas that save head-scratching when diagnosing:

- **Call-site defects surface at callers, not at the definition.** Dumping a
  method's own definition (e.g. `System.AppContext::TryGetSwitch`) can report
  `fidelity: Full` because its callees are MethodDefs in the same assembly; a
  `DEC0007` ref-kind loss only appears when you dump a cross-assembly *caller*.
  Dump the call site, not the target, to reproduce a call-shape defect.
- **`--skip-pdb` does not affect fidelity.** Local names are cosmetic — they
  never change emitted IL — so `--fidelity-check` is unaffected by whether names
  were recovered. `--skip-pdb` only changes the *spelling* in a dump (`V_n` vs
  `i`/`j`), for deterministic, symbol-independent reading.

## Soundness checklist for IR-mutating passes

Reviews of the raising passes converge on three recurring questions; an author
who answers them before requesting review collapses the serial round-trips. Any
pass that detaches, replaces, or rewrites IR nodes states its answers in the PR
(a short "Soundness" note), and the review verifies them rather than
rediscovering them:

1. **Prove preconditions whole-function, not locally.** A rewrite that removes a
   node's defining store (or any binding) must prove the affected locals, slots,
   and stack positions are referenced *only* by the nodes it consumes — scanned
   across the whole function, not just the matched neighbourhood. Hand-written or
   obfuscated IL can wear the shape without being the pattern.
2. **Preserve semantics exactly.** A conversion, comparison, or identity match
   keeps checked/unsigned/overflow behaviour and produces valid C# (no CS-error
   spellings). Match metadata members on assembly identity and exact signature,
   not just namespace/name and call-site argument shapes.
3. **Isolate per-item failure.** A sweep over many methods (or assemblies) guards
   each item so one malformed input yields a diagnostic, not a lost batch;
   results that escape their source hold no live readers.

A proposed change should arrive with: the IL shape it targets, the argument for
its class under the taste doc's three-class rule, a fixture covering both
configurations, and a `--pass-impact` blast-radius read showing exactly the
intended changes across the corpus. The default first reviewer is the author —
run the high-effort self-review over the branch before opening the PR.
