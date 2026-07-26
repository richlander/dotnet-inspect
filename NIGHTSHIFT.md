# NIGHTSHIFT.md — this repository's charter

This document is the Nightshift engineering charter for **dotnet-inspect**. It is written for a
**planner** to import at the start of a shift and use to turn this repo's issues into **orders** that
workers execute. Read the planner skill (`nightshift skill planner`) for *how* to plan; this charter
carries only what is specific to this repo. Your authority is this charter **plus** whatever the
operator told you this session — where both are silent, do not assume: post on the issue and wait.

Repository-wide engineering requirements are in [`AGENTS.md`](AGENTS.md); it is the source of truth
for the SDK, build/test commands, branching, the output-verbosity contract, evidence expectations, and
adversarial review. This charter does not restate those — it only shapes *which issues become orders
and how they are sliced*.

## Starting the tools

The `nightshift`, `turnstile`, and `octoshift` commands are **already installed as binaries on your
PATH**. You do **not** build, rebuild, or redeploy them, and there is nothing to compile before a
shift — ignore any instruction that tells you to.

Bring up the coordination daemon **once per machine** — it holds the board, so leave it running:

```bash
turnstile serve --socket ~/.turnstile/turnstile.sock --db ~/.turnstile/turnstile.db
```

Point every `nightshift` and `turnstile` call at that socket (the default is
`~/.turnstile/turnstile.sock`, so this only matters if you chose another path):

```bash
export TURNSTILE_SOCKET=~/.turnstile/turnstile.sock
```

With the daemon up, follow your role skill — `nightshift skill coordinator` to register the plan and
open the ready set, `nightshift skill worker` to claim and build an order.

**If any tool errors, hangs, crashes, or behaves in a way you do not expect, stop and ask the operator
for help. Do not try to diagnose, patch, rebuild, restart, or work around the tools yourself.**

## Scope — which issues become work

Candidates are **open issues in this repository** that describe a concrete change to one of the
product subsystems, the harnesses, the skills, or the design docs:

- **Metadata** (`src/ILInspector.Metadata/**`, `src/ILInspector.MetadataPrimitives/**`) — SRM-level
  metadata facts.
- **Analysis** (`src/ILInspector.Analysis/**`, `src/ILInspector.CallGraph/**`,
  `src/ILInspector.ControlFlow/**`, `src/ILInspector.Instructions/**`) — IL-body evidence.
- **CSharp / decompiler** (`src/ILInspector.CSharp/**`, `src/ILInspector.Decompiler/**`) — C# spelling,
  type views, and raising/structuring.
- **Research / Findings** (`src/ILInspector.Research/**`, `src/ILInspector.Findings/**`) — evidence
  composition.
- **CLI and output** (`src/dotnet-inspect/**`) — command and presentation concerns.
- **Shared services** (`src/DotnetInspector.*/**`), the **harnesses** (`tools/**`, `tests/**`), the
  **skills** (`skills/**`, `.github/skills/**`), and the **docs** (`docs/**`, `README.md`).

The **Product Manager tells you the theme at the start of each session** — plan the open issues that
match that theme. Pure discussion, open design questions, and product-shape decisions with unresolved
tradeoffs are **not** planned; they belong to the Product Manager (you may post to move them forward).

## Turning issues into orders

The order — its id, `paths`, `after` edges, and `standard` — is something **you produce** to drive the
shift; it is not authored into the issue. Issues stay ordinary issues. To turn the ones that match the
theme into orders:

- **Size each order to about an hour**, and to a size that can be **adversarially reviewed at high
  quality**. Many issues already fit and need no breakdown; split the ones that don't.
- **Keep `paths` disjoint and layer-aligned.** Slice orders along the subsystem boundaries above so
  concurrent workers never collide on the same files, and so each order stays inside one owner's layer.
  Metadata owns metadata facts, Analysis owns IL-body evidence, CSharp owns C# spelling and type views,
  Research composes evidence, and the CLI owns command and presentation concerns — do not let one order
  reach across these to infer another layer's facts.
- **Design-first for the hard ones.** If an issue is ambiguous, carries tradeoff decisions, has
  significant cross-subsystem interactions, or introduces a new foundational capability, start with a
  **docs-only design PR** under `docs/design/` before any implementation. Design PRs clear the same
  adversarial gate as code — often it matters more, because a bad design produces bad code.
- **Use the decompiler PR templates for decompiler work.** Raising, typing, structuring, fidelity,
  validity, or corpus changes carry extra evidence requirements; set each such order's `standard` to
  the relevant template and design doc:
  - `docs/templates/decompiler-pr.md` — raising, structuring, validity, fidelity, or corpus behavior.
  - `docs/templates/decompiler-burndown-fix-pr.md` — a focused invalid-`Full` or burndown row fix.
  - `docs/templates/decompiler-compile-back-harness-pr.md` — compile-back harness or fidelity skeleton.
  The `Area` / `Speed` decompiler test taxonomy in `docs/decompiler-correctness-pipeline.md` is how a
  worker scopes evidence for these orders.
- **Collapse strong overlap.** If two issues overlap heavily, merge them into a single order (or a
  shared set of overlapping slices) rather than planning orders that will collide on the same files.
- **Readiness is a gate.** An order needs a design solid enough to slice into `paths`-bounded pieces
  against a concrete `standard`. When an issue is still a sketch — unsettled scope, no agreed shape,
  unresolved tradeoffs — **do not plan it.** Post on the issue naming the specific design it still
  needs, and leave it for the Product Manager to shape.

## How this composes with the repo's review gate

`AGENTS.md` already requires adversarial review from **two different models** for any non-trivial
behavior change — this **is** Nightshift's two-clean gate, so the two do not stack: a worker's two
clean reviews from two different models on the final head satisfy both. Where the two bars differ,
**Nightshift is the stricter one and it governs the shift.** `AGENTS.md` exempts simple, mechanical,
or documentation-only changes from adversarial review; a Nightshift **order** does **not** inherit
that exemption — **every order clears the two-clean gate on its final head, docs and design orders
included.** Scale the review *effort* to the blast radius — a docs order is cleared by confirming its
claims and links are accurate, a heuristic or shape change by attacking its correctness — but never
waive the two-clean bar itself. Keep the repo's readiness convention: when all merge-blocking
validation and required review are complete, the clearance note is `Ready to merge`.

The mechanics of expressing an order and driving it to merge — the plan format, the two-clean review
gate, landing — are standard Nightshift and belong to your skill.
