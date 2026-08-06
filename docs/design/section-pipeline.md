# Section pipeline

The section pipeline is the runtime implementation of the
[section model](section-model.md). It centralizes section registration,
category ownership, automatic scope, producer demand, effectiveness, and
render selection.

## Responsibilities

The pipeline answers five independent questions:

1. Which authored sections and categories exist?
2. Which sections are candidates for this gesture?
3. Which producers are required by those candidates?
4. Which candidates are effective for this target?
5. Which effective sections should the serializer render?

Keeping these questions separate prevents verbosity, applicability, and cost
from becoming aliases for one another.

## Section descriptors

`ISectionDescriptor<TModel>` declares a section's typed metadata. Descriptors
provide the section name, output size class, network cost, scanner key,
execution policy, and model predicate.

The pipeline reads static descriptor members during registration. Descriptors
are never instantiated, preserving NativeAOT compatibility and avoiding
reflection-based product behavior.

Important descriptor concepts are:

| Concept | Purpose |
| --- | --- |
| `Name` | Stable selector and rendered heading |
| `SizeClass` | Fixed, moderated, or unbounded row shape |
| `Cost` | Network-free or network-bound production |
| `ScannerKey` | Producer demand key; `null` means core inspection |
| `ExplicitOnly` | Excluded from automatic verbosity |
| `CanRender` | Post-production effectiveness predicate |
| Applicability predicate | Cheap structural gate supplied at registration |

`ExplicitOnly` is an execution policy. Discovery must not expose it as an
“opt-in” section kind.

## Category registration

Categories are authored through `AddBaseCategory` or `AddCategory`.

- Base categories contribute to automatic verbosity and bare-`-S` scope.
- Domain categories remain explicit doors.
- Category members must name registered selectable sections.
- A section may belong to multiple categories.

The pipeline does not derive membership from `Domain:` prefixes or noun
suffixes.

## Candidate selection

Automatic candidate selection is the intersection of:

- the base-category union;
- the current verbosity preset;
- size and cost policy;
- explicit-only policy.

Exact `-S` selection overrides automatic scope. Bare `-S` uses the fixed,
network-free subset of the base union.

Category selection expands to authored members before producer demand is
computed.

## Producer demand

`GetRequiredScanners` walks the candidate set and returns unique scanner keys.
Several sections may share one scanner, so demand is deduplicated before the
registry runs.

```text
selector / verbosity
        |
        v
candidate sections
        |
        v
required scanner keys
        |
        v
scanner prerequisite closure
        |
        v
model facts and findings
```

Command-level prerequisites that no section expresses are passed into the same
method as attributed command demand. This keeps trace output complete.

The `ScannerRegistry` owns key-to-producer wiring and prerequisite expansion.
Harnesses and commands must exercise that product-owned wiring rather than
reconstruct it.

## Effectiveness

The pipeline exposes separate queries for:

- structurally applicable sections;
- post-production renderable sections;
- explicitly applicable sections whose renderability depends on selection.

Cheap discovery uses structural predicates and a small command probe set. Full
effective discovery runs the selected producers and asks the post-production
predicates.

Full bare discovery remains scoped to base categories. Structural evidence for
domain members may keep an applicable category door visible without placing
those members in the flat base catalog.

## Rendering

`ComputeIncludeSections` produces the section-name set passed to Markout.
Markout remains responsible for document serialization and section filtering.

Headless sections can carry compact context without rendering a heading. They
participate in serializer filtering but are not independently selectable
unless the command explicitly declares them as such.

Row-oriented formats require one concrete schema or a homogeneous family.
Heterogeneous categories are rejected before producers run.

## Registration gates

Registration and derived tests enforce:

1. Unique section and category names.
2. Valid category-member references.
3. Authored ownership for every selectable library section.
4. Explicit base-category roles.
5. Unbounded sections are expensive or explicit-only.
6. Deterministic declaration order.
7. Scanner prerequisites resolve.

Set-equality tests should derive expected ownership from the catalog so stale
and missing entries both fail.

## Tracing

Library `--trace` records:

- selected sections;
- section-to-scanner demand;
- command-level demand;
- prerequisite expansion;
- scanner execution time;
- expensive resources acquired.

Trace output is diagnostic stderr and never changes the document on stdout.
