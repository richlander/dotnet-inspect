# Resolver-lineage continuation model

Owner:
[Type-forwarding resolution: resolver-lineage continuations](../../type-forwarding-resolution.md#resolver-lineage-continuations).
Focused issue: #5666. End-to-end adoption: #5274.

## Question and boundary

Can two independent requests reach the same selected assembly and retain
different resolver contexts without mutating a registration-to-resolver map?
Can an already configured canonical participant instead supply its own context?

`AssemblyBindingLineage.tla` represents the product join currency directly.
An occurrence pairs a candidate with a lineage containing the issuing group,
captured policy version, and resolver discriminator. Both the request and its
cache key retain that lineage. The issuer is one fixed group; this model does
not claim cross-issuer rejection coverage.

The finite model has two flows, two resolver contexts, one shared intermediate
candidate, two terminal dependencies, and one possible real policy replacement.
Both the transitive and canonical-right-root scenarios are explored.
`VersionOne` is the captured generation version. Selecting an occurrence
under the stable routing function leaves it unchanged.

## Imported boundary

`BindingVersion` is a named instance of the existing
`AssemblyBindingPolicyVersionLifecycle` module, with its two versions bound to
`VersionOne` and `VersionTwo` and its state bound to `version` and `advanced`.
`ChangePolicy` invokes the owner's `Advance` action. The positive configuration
rechecks freshness and projected refinement of the imported safety
specification. `BrokenRouteChurn` uses that same valid advance at the wrong
semantic event; it demonstrates that lifecycle freshness alone does not justify
superseding a generation for an ordinary selection.

The generation either publishes its two prepared results or is superseded.
Weak fairness covers each flow and settlement. External policy change is
optional, not fair or inevitable. No retry action exists. This is an
association model, not a replacement for the existing atomic-snapshot or
workspace-realization models.

## Exact configurations

| Configuration | Required TLC result | Property |
| --- | --- | --- |
| `Safety.cfg` | 0 | Occurrence, cache, and publication association; imported version lifecycle; eventual settlement |
| `StableProgress.cfg` | 0 | Both flows publish under the unchanged token |
| `BrokenRegistrationMap.cfg` | 12 | The second context cannot use the first writer's registration route |
| `BrokenCandidateCache.cfg` | 12 | Candidate-only cache identity mixes different resolver answers |
| `BrokenAlwaysInherit.cfg` | 12 | Selecting a canonical root must use its configured context |
| `BrokenRouteChurn.cfg` | 12 | Selecting a continuation is not external policy change |
| `ReachabilitySupersession.cfg` | 12 | Witness that a genuine policy change can supersede the attempt |

The negative controls describe plausible association regressions, not hostile
internal callers. The reachability configuration intentionally asserts the
negation of its witness. Exact outcomes are registered in
`eng/tla-expected-exit-codes.txt`.

## Running and interpretation

Use the repository-pinned TLC build `2026.08.11.125311`, SHA-256
`ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f`.
From the repository root:

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar \
  ./eng/run-tla-checks.sh docs/design/models/resolver-lineage
```

The 2026-09-04 run completed all seven exact outcomes with the pinned build.
`Safety.cfg` explored 64 generated / 56 distinct states; `StableProgress.cfg`
explored 28 generated / 20 distinct states. Both exhausted their state spaces.

Single-worker trace inspection confirmed the intended negative controls:
the registration map sent Right to `LeftBase`; the candidate-only cache reused
Left's answer for Right; always-inherit gave a canonical Right root Left's
context; and route churn advanced the version with no external policy change.
The supersession witness was `ChangePolicy -> Settle`, with no published
result. Counterexample state counts are scheduling-dependent and are not gates.

TLC results establish only this bounded model's behavior. The model abstracts
PE contents, identity matching, candidate-domain finalization, ambiguity,
intrinsic metadata reads, acquisition and budget enforcement, multiple nested
composites, historical recipe carry-forward, and real host execution.
Those are not implicitly proven by a successful model run.

The #5801 compiled fixture is the existing product oracle for the minimal
Services forwarding case. The new representation's Metadata, Services/CLI, and
Queries/Browser adoption gates remain unverified until the four-step plan in
the owning design lands. No inspected assembly is executed by this model.
