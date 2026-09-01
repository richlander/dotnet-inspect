# Repository CI Change Plan

## Status and owner

This document owns the repository CI change planner: the boundary from one
checked candidate and its event provenance to one immutable plan describing
which CI jobs are relevant and which exact scoped path evidence they may
consume. Issue [#5415](https://github.com/richlander/dotnet-inspect/issues/5415)
tracks this design.

The planner owns candidate identity, changed-path acquisition and
interpretation, routing policy, cross-field implications, plan construction,
and scoped-evidence identity. Workflow YAML transports inputs and outputs.
Individual jobs retain ownership of their execution, validation semantics,
tools, and results.

## Purpose

The repository should answer "which validation applies to this candidate?"
once. A contributor should not have to reconcile path rules distributed among
workflow expressions, shell scripts, APIs, and domain jobs, and an unrelated
pull request should not run expensive content checks because one routing
surface disagrees with another.

The planner makes CI routing a typed, reviewable contract. This is
foundational repository infrastructure rather than product behavior.

## Problem

The current change detector has grown into a policy engine without a single
typed boundary. `eng/ci-detect-changes.sh` acquires paths, validates API
responses and Git streams, evaluates job rules, reads policy manifests, and
enforces implications among eleven outputs. Separate workflow and domain-job
logic can still establish another candidate relation or reinterpret paths.

PR #5347 demonstrated the concrete consequence. GitHub's pull-request Files
API described merge-base-to-PR-head paths, while the checked-out workflow
validated the current synthetic merge candidate. A base-branch rename made
those path sets disagree, so TLA+ scheduling had to recompute its decision from
the candidate that the job actually checked.

Structural tests make the current behavior safer, but they do not remove the
distributed ownership that caused the mismatch.

## Boundary

The planner consumes:

- one checked repository candidate;
- one typed event-provenance case identifying the candidate and required
  comparison endpoints; and
- planner-owned routing policy and policy data.

It produces either:

- **planned** — one immutable, versioned CI plan plus any bounded scoped path
  evidence named by that plan; or
- **refused** — a nonzero result with a diagnostic identifying why no valid
  plan exists.

A valid empty change set is a planned result. Missing provenance, an
unavailable comparison endpoint, malformed changed-path framing, incomplete
evidence, unsupported path representation, or an oversized result is a
refusal, not an empty plan.

## Candidate provenance

Provenance is a closed union. Each case names the two endpoint trees whose
difference defines the checked candidate:

- **pull-request synthetic candidate** — the current pull-request base tree and
  the checked synthetic merge-candidate tree;
- **push** — the event's before tree and pushed head tree;
- **merge group** — the merge-group base tree and checked synthetic group tree;
  or
- a separately designed replay case that supplies both exact endpoints.

The planner validates that both endpoints exist and that the checked candidate
matches the provenance case. It must not silently replace a required relation
with a merge-base comparison, parent comparison, PR-head comparison, API path
set, tracked-file inventory, or another approximation.

The pull-request relation is deliberately current-base-to-synthetic-candidate,
not GitHub's documented three-dot workflow path-filter relation. Default
pull-request checkout validates `refs/pull/<number>/merge`; routing must describe
that same candidate.

## Changed-path evidence

The planner acquires changed paths once from the required endpoint trees.
Evidence preserves:

- path identity without newline, tab, quoting, locale, or shell
  reinterpretation;
- addition, modification, deletion, and type-change status;
- both tree locations affected by a rename, without similarity heuristics; and
- a distinction between a valid zero-record stream and unavailable or
  malformed evidence.

The planner's canonical path identity is bytes, not display text. Routing rules
operate on that identity. If an adoption cannot represent a repository path
without loss, planning refuses visibly rather than decoding with replacement
or omitting the path.

Rename detection is not part of routing. The endpoint trees expose an old-path
deletion and new-path addition, so rename-into and rename-out cases are
deterministic.

Exact path corpora do not travel through GitHub's textual job-output channel.
When a domain job needs scoped paths, the planner produces a bounded evidence
file. The file is a NUL-terminated sequence of exact path-byte records. A path
need not exist in the candidate tree: deletion evidence remains meaningful.
Change status is not part of a scoped record unless the consuming contract
separately requires it.

The plan descriptor binds the file to:

- the plan schema version;
- candidate provenance;
- record framing;
- record count;
- byte digest; and
- the consuming scope.

Selection and scoped-record count are distinct. A policy change may select a
job while assigning it zero content paths, so a true selection with a valid
zero-record evidence file is allowed.

The workflow may transport that file as an artifact. A consumer must obtain,
digest-verify, and framing-validate its assigned evidence or fail visibly. It
must not reconstruct provenance or broaden, narrow, or reacquire the planner's
path set.

## Routing policy

The planner owns every repository path rule that selects a CI job and every
implication among plan fields. A workflow condition may project a plan field;
it must not contain an independently meaningful path expression or fallback.

Policy data such as project-root inventories may remain in focused files. The
planner owns their input contract, validation, and routing effect. An invalid
policy input cannot silently remove a job. A deliberately conservative
selection must be represented in a valid plan and tested as policy, not reached
through an accidental parsing fallback.

Jobs consume selections, not paths. Domain-specific interpretation begins only
after routing. For example, the TLA+ job may validate model-directory layout
within its assigned evidence, but it does not decide which repository changes
made the TLA+ job relevant.

## Immutable plan

One planning operation constructs one plan. Every scalar workflow output is a
mechanical projection of that object, and every scoped-evidence descriptor
belongs to it.

The plan contains:

- a schema version;
- planned status, the only status valid in a serialized plan;
- candidate provenance;
- changed-input record count and digest;
- one typed field for each repository CI job selection;
- bounded scoped-evidence descriptors; and
- bounded diagnostics that do not replace a refusal.

Conceptually:

```json
{
  "schemaVersion": 1,
  "status": "planned",
  "provenance": {
    "kind": "pullRequestSyntheticCandidate",
    "baseSha": "...",
    "candidateSha": "..."
  },
  "input": {
    "recordCount": 17,
    "sha256": "..."
  },
  "jobs": {
    "code": true,
    "docs": false,
    "tla": true
  },
  "scopes": {
    "tla": {
      "artifact": "ci-plan-tla-paths0",
      "recordCount": 2,
      "sha256": "..."
    }
  }
}
```

The example is a shape mockup, not a complete field inventory. The serialized
plan contains only ASCII and is bounded to at most 16 KiB, leaving margin under
the repository-observed 21,000-character workflow-expression scalar boundary.
Path corpora and refusal diagnostics remain outside the plan.

Consumers reject an unsupported schema version or malformed plan. Because all
workflow and planner changes land together in this repository, this design
does not require compatibility shims for obsolete plan versions.

## Failure contract

A planner refusal remains a failed required check. Missing or malformed plan
output must not become an all-false success, and successful execution of other
jobs must not erase the planner failure.

The planner emits no plan after refusal. It may emit a small diagnostic through
a distinct channel before failing. Diagnostics identify categories, record
positions, and digests; they do not embed arbitrary path bytes.

This contract depends on an aggregate-gate precondition: a planner job result
other than success, including skipped, cancelled, or never started, blocks the
candidate. Recovery jobs, if an adjacent workflow design authorizes them, do
not consume a refused plan and cannot erase that result.

The planner never:

- changes comparison relations to recover;
- treats an API prefix as complete;
- lists every tracked file as substitute changed-path evidence;
- converts an exception into an empty plan; or
- emits independently calculated scalar outputs after plan construction fails.

## Workflow and consumer contract

A published plan represents exactly one planner invocation for one candidate.
Workflow YAML supplies event fields and the checked repository, publishes the
compact JSON plan from a non-matrix producer, and transports scoped evidence.
Downstream jobs use only typed field projection from that plan.

A matrix, if later planned, is another bounded plan field produced by the same
operation. Matrix jobs do not contribute competing planner outputs because
GitHub does not guarantee matrix execution order and duplicate output names
are last-writer-wins.

The aggregate CI gate, individual test suites, tool acquisition, and job
outcomes remain outside planner ownership. They consume a plan or a visible
planner refusal; they do not delegate their correctness semantics to it.

## Convention and deliberate divergence

[`dorny/paths-filter` v4.0.3](https://github.com/dorny/paths-filter/tree/ceb8a2b8f2d89434be7ff52d3de7ec3738c5cc9d)
provides the closest workflow convention: acquire changes once, evaluate named
filters once, and publish selections for jobs to consume. Its local Git path
uses NUL-delimited `--name-status` evidence and represents API renames as
deletion plus addition. The project is MIT licensed; this design transfers the
boundary and observed behavior, not code.

[`tj-actions/changed-files` v47.0.6](https://github.com/tj-actions/changed-files/tree/9426d40962ed5378910ee2e21d5f8c6fcbf2dd96)
demonstrates explicit change-status categories and JSON outputs. Its default
warning-plus-empty behavior after an initial diff failure and its
newline/tab-delimited local parsing do not satisfy this repository's visible
failure and exact-path requirements. The project is MIT licensed; no code is
transferred.

[`Nx` 23.1.3](https://github.com/nrwl/nx/tree/a5a1e685d03fd00cd1cf43136cf31b5cf3334079)
provides the typed-planning analogue: typed file changes enter one affected
calculation and consumers receive selected entities rather than reinterpreting
paths. The project is MIT licensed. Project-graph discovery, dependency
propagation, and build orchestration do not transfer.

GitHub conventionally supports a producer job publishing JSON and consumers
using
[`fromJSON`](https://github.com/github/docs/blob/d653c7b3c164566a710a05f27d645e926be9b0d9/content/actions/reference/workflows-and-actions/expressions.md#L191-L223).
The repository follows that transport convention.

The repository deliberately diverges from:

- native path filters, whose pull-request relation differs from the checked
  synthetic candidate and whose bounded diff can omit a later match;
- PR Files API planning, which cannot authoritatively describe the synthetic
  candidate and has a 3,000-file ceiling;
- actions that change comparison relation when a merge base is unavailable;
- actions that publish success-shaped empty results after diff failure; and
- workflow-owned filter expressions, which would preserve distributed policy.

## Evidence

The contract-defining pathological fixture is a base rename that moves a
PR-authored edit into a gated path in the synthetic candidate while the PR
Files API-like path remains outside it. Planning must select the gate. The
inverse rename must not select unchanged gated content.

An implementation gate must also cover:

- additions, modifications, deletions, type changes, and both sides of renames;
- spaces, leading dashes, quotes, shell metacharacters, embedded newlines, and
  tabs in path identities;
- invalidly encoded path bytes;
- a valid empty diff;
- missing and inconsistent provenance;
- unavailable endpoint trees;
- malformed or truncated changed-path evidence;
- policy-data absence and invalidity;
- every job rule and cross-field implication;
- an oversized plan or scoped-evidence descriptor;
- malformed and unsupported plan versions at the consumer boundary;
- missing, malformed, or digest-mismatched scoped evidence at the consumer
  boundary;
- planner process failure and a planner job that is skipped, cancelled, or
  never starts;
- routing-rule parity with every existing classifier scenario when both
  classifiers receive the same changed-path corpus;
- the deliberate provenance change from API or event approximations to exact
  candidate endpoints;
- the deliberate failure-contract change from all-true recovery to a blocking
  refusal; and
- a neighboring docs-only candidate that selects documentation validation
  without unrelated content gates.

The demo is one planner invocation for the #5347 rename-into-model fixture. Its
plan selects TLA+ and its scoped evidence contains the planner-assigned changed
paths; the TLA+ consumer still owns model-directory validation. The neighboring
inverse fixture produces `tla: false`. A provenance failure produces a visible
refusal rather than an empty or all-false plan.

This design is **unverified** until an implementation gate constructs the plan
through the production planner, validates its serialized boundary, and proves
workflow consumption from that exact object.

## Adoption sequence

Adoption proceeds in focused slices:

1. Implement the planner types, path-evidence reader, routing policy, canonical
   serialization, and routing-rule parity harness without making it
   authoritative in CI.
2. Make the workflow's change-planning job consume the planner and project all
   existing scalar selections mechanically from one plan. This slice owns the
   intentional exact-candidate provenance and blocking-refusal changes, and it
   updates the aggregate gate's structural assertions as a named sequencing
   dependency.
3. Move each scoped-path consumer, beginning with TLA+, to planner-produced
   evidence and remove its independent provenance and path acquisition.
4. Remove the legacy shell classifier and obsolete structural seams only after
   parity and all planned consumers have transferred.

Each slice names its own adopting owner and gate. The design does not authorize
a single PR to rewrite every CI consumer.

## Non-claims

The planner does not:

- define which tests, builds, packages, or deployments a selected job performs;
- own the aggregate gate's success policy;
- create a project or dependency graph;
- schedule work within a selected job;
- optimize runner allocation or execution order;
- preserve compatibility with obsolete planner schemas;
- make GitHub APIs authoritative for synthetic-candidate paths;
- treat repository contributors or local agents as hostile actors; or
- provide a general build orchestration framework.
