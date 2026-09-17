# Workspace default target framework

## Status and authority

This document is the focused owner of the default target-framework policy
carried by every Workspace. Design and production adoption are tracked by
[#7352](https://github.com/richlander/dotnet-inspect/issues/7352).

The owner defines one typed default, its construction and validation, and the
governing-target choice made when a consumer must select a participant and has
or lacks one authoritative target framework. It does not select package
assets, select package dependency groups, determine framework compatibility,
infer project intent, or define Workspace wire formats.

Adjacent owners retain their authority:

- [Workspace scope and expansion](workspace-scope-and-expansion.md) owns
  `WorkspacePlan`, live Workspace identity, and plan retention.
- [Workspace definitions](workspace-definitions.md) owns portable definition
  and share-packet versions.
- Package asset selectors own compatibility, nearest applicable asset,
  ambiguity, and no-match outcomes.
- [Package Dependency Evidence](package-dependency-evidence.md) owns normalized
  declarations and selected dependency groups;
  `PackageDependencyGroupsQuery` owns group selection.
- [Package dependency traversal](package-dependency-traversal.md) owns graph
  traversal, preservation of owner-issued dependency evidence, and completion.
- [Realized package participant and dependency-evidence
  association](https://github.com/richlander/dotnet-inspect/issues/7401) owns
  the composition between one physical source participant and its selected
  dependency evidence.
- [Target-framework selection across dependency
  realization](https://github.com/richlander/dotnet-inspect/issues/6424) owns
  correspondence from an exact edge target to realized package assets.
- Restored-project and explicit command owners issue authoritative target
  requests.

## Claim

> Every Workspace has one validated canonical default target framework.
> Product-created Workspaces use `net11.0` unless configured otherwise.
> Consumers use that value from the first participant selection when no
> authoritative target governs the choice, and retain which target governed
> selection.

The default is stable Workspace construction intent. It is not:

- a participant's declared or selected target framework;
- evidence that two participants are compatible;
- permission to replace a no-match for an explicit command, project, restored
  graph, dependency edge, or context target;
- a request to register or acquire a Platform; or
- a dynamic alias for the newest installed SDK, Platform catalog entry, or
  package asset folder.

## Policy value

The host-neutral value is:

```text
WorkspaceTargetFrameworkPolicy
  DefaultFramework: canonical NuGet target-framework identity
  Source: ProductDefault | Configured
```

`DefaultFramework` is never absent. Omitted configuration constructs
`ProductDefault(net11.0)`. Explicit configuration constructs `Configured` only
after the existing canonical NuGet framework parser accepts the value.
Malformed, unsupported-shape, or padded input is a typed construction failure;
it does not become `net11.0`.

`net11.0` is a product constant. It does not track the executing SDK or the
highest framework currently known to a host. Changing that constant is a
user-visible default change and requires ordinary compatibility disclosure.

The policy is immutable and resource-free. Reusing one Workspace construction
plan retains an equal value in each independently constructed live Workspace.
Replacing registration state does not replace the policy.

## Target intent and governing target

A consumer that must select a target-specific participant supplies one typed
local intent:

```text
WorkspaceTargetFrameworkIntent
  Required(Target, Origin)
  Unspecified
```

The supplying owner classifies the intent. This owner never infers authority
from framework text, package paths, assembly names, or display labels.

- `Required` is used for explicit operation input, an exact restored-project
  target, an explicit Workspace-context target, or an owner-issued dependency
  realization target.
- `Unspecified` means the operation has no local target evidence.

The policy lowers the intent to exactly one governing target:

| Intent | Governing target |
| --- | --- |
| `Required(T)` | `T` |
| `Unspecified` | the Workspace default |

An inspected source participant's selected asset-folder framework is
provenance, not implicit consumer intent for a newly reached destination. It
never inserts a source-TFM request before the Workspace default. This remains
true when the destination carries an asset for that source framework: the
Workspace default governs compatibility and nearest participant selection
through the operation's only selection request.

The adjacent selection owner evaluates the governing target under its existing
compatibility and nearest-selection contract. `Selected`, `NoMatch`,
`Ambiguous`, invalid input, unavailable evidence, cancellation, and every
other owner-issued outcome are terminal. The Workspace policy never supplies
a second target, an unconstrained "highest TFM" request, or an alternate
manifest-local default.

An explicitly selected hub asset remains that exact physical subject. The
Workspace governing target selects newly reached destination participants; it
does not replace the hub merely because the Workspace default would select a
different hub asset.

This target choice does not select the dependency group of an already realized
package participant. A consumer of this policy accepts dependency declarations
only through the owner-issued association tracked by #7401 between that
physical source participant and Package Dependency Evidence's selected group.
Package Traversal preserves and consumes that association. Those declarations
form candidate edges; the Workspace governing target then selects the
destination participant reached by each edge. A selected source asset-folder
framework may participate in the association evidence without becoming the
target used to select the destination.

## Retained decision evidence

A framework-selecting operation retains one decision:

```text
WorkspaceTargetFrameworkDecision
  Policy
  Intent
  GoverningTarget: canonical target supplied to selection
  ParticipantSelectionOutcome: owner-issued outcome
```

The selected package asset-folder framework remains separate owner-issued
evidence. A result may therefore say that Workspace default `net11.0` governed
selection of a `net8.0` asset. It must not rewrite either value or imply that
the inspected source itself targets `net11.0`.

When a source dependency declaration authorizes destination realization, the
consumer passes the exact governing target into the correspondence owned by
issue #6424. Later destination selections do not substitute their source
participant's selected asset-folder framework or rerun Workspace default
choice. Source dependency-group selection remains separate owner-issued
evidence associated through #7401 at every node.

Human output may explain that the Workspace default governed selection.
Machine output retains the typed decision. Hosts do not reconstruct it from
selected paths or descriptive text. This owner introduces no rendering domain;
adopting operations use their existing Markout and structured-output
boundaries.

## Motivating real assets

`Microsoft.Extensions.DependencyInjection.Abstractions@8.0.0` carries
`IServiceCollection` in its `netstandard2.0` assets.
`Microsoft.Extensions.Telemetry@8.0.0` has `net6.0`, `net8.0`, and `net462`
assets and contributes extension methods for `IServiceCollection`, including
`AddHttpRouteProcessor`.

That real relationship demonstrates the missing choice. A `netstandard2.0`
source target is not the consumer target for either modern .NET asset. With
Workspace default `net11.0`, existing nearest-compatible selection chooses
`net8.0`; with configured default `net7.0`, it chooses `net6.0`. The retained
decision discloses that the Workspace default, not the source framework,
governed target-package selection.

Cross-TFM inspection also demonstrates why source-first selection cannot be a
harmless preference:

- `NodaTime@3.2.2` adds 61 interfaces, eight members, and three types from
  `netstandard2.0` to `net8.0`, including `DateOnly`, `TimeOnly`, and
  `TimeProvider` integration.
- `Newtonsoft.Json@13.0.3` adds five API elements from `netstandard2.0` to
  `net6.0`.
- `Polly.Core@8.8.0` removes two legacy serialization members from
  `netstandard2.0` to `net8.0`, proving that the modern asset is not
  necessarily a strict API superset.
- `Dapper@2.1.66` and `Microsoft.Extensions.Telemetry@8.0.0` retain equal API
  surfaces across the inspected pairs.

Dependency groups differ as well. Polly.Core's `netstandard2.0` group carries
four compatibility dependencies while its `net8.0` group carries none;
Telemetry's `net6.0` group includes `System.Collections.Immutable` while its
`net8.0` group does not. That evidence requires the selected dependency group
to remain associated with its physical source participant. It does not make
the source participant's TFM the destination-selection target.

Observed with production `dotnet-inspect` 0.25.0:

```console
dotnet-inspect package Microsoft.Extensions.Telemetry@8.0.0 --json
dotnet-inspect extensions \
  Microsoft.Extensions.DependencyInjection.IServiceCollection \
  --package Microsoft.Extensions.Telemetry@8.0.0 --tfm net8.0 --json
```

The package reports `net6.0`, `net8.0`, and `net462`; the relationship query
reports the extension methods. The cross-TFM comparisons currently require
manual extraction and `diff --library`; issue #7388 tracks a package-native
comparison. Deterministic fixtures will preserve the selection boundaries
without making live NuGet availability a CI prerequisite.

## Conventional basis and divergence

NuGet restore selects package assets relative to a project's target framework.
A free-standing inspection Workspace has no project target from which to
derive that consumer intent. The Workspace default provides the analogous
explicit ambient target and then delegates compatibility and nearest selection
to the existing package owner.

Using the ambient target from the first destination-participant selection
follows the conventional restore model: a project's target governs package
asset selection, not whichever physical TFM happened to expose the referring
body.

The deliberate inspection-only divergence is preserving an explicitly
selected hub asset even when the Workspace default would select another asset
from that package. The hub is the subject the person chose to inspect; the
package owner keeps its dependency declarations associated with that subject,
and the Workspace default governs newly realized destination participants.
Results keep physical source, dependency-group, and governing-target evidence
separate and do not claim that this mixed inspection graph is one restored
project.

## Required gates

| Property | Release gate |
| --- | --- |
| Omitted configuration produces exactly `ProductDefault(net11.0)`; configured values canonicalize; invalid values fail before a Workspace exists. | Workspace-policy construction tests using the product constant and existing NuGet target-framework parser. |
| Reusing a construction plan preserves equal policy in independent live Workspaces; registration replacement preserves it. | `WorkspacePlan` and registration-replacement tests. |
| Required intent supplies its exact target once and never substitutes the Workspace default after any selection outcome. | Governing-target unit tests covering explicit, restored, context, and edge origins. |
| Unspecified intent supplies the Workspace default once and never inserts the source asset TFM or an unconstrained highest-folder request into destination participant selection. | Governing-target unit test plus a participant-selector spy asserting the exact target. |
| A `netstandard2.0` source reaching NodaTime selects its `net8.0` asset under default `net11.0` even though NodaTime also carries `netstandard2.0`; source and selected folder remain separate evidence. | Pinned package integration test using `NodaTime@3.2.2`, with an equivalent deterministic fixture. |
| The real relationship selects Telemetry `net8.0` under default `net11.0` and `net6.0` under configured `net7.0`, retaining source target, policy source, governing target, participant-selection outcome, and selected folder separately. | Pinned package integration test using `Microsoft.Extensions.DependencyInjection.Abstractions@8.0.0` and `Microsoft.Extensions.Telemetry@8.0.0`, with an equivalent deterministic fixture. |
| An explicitly selected Polly.Core `netstandard2.0` hub remains unchanged and retains its four associated dependency declarations, while every destination participant uses the Workspace governing target. | Pinned `Polly.Core@8.8.0` composition test under #7401 plus an equivalent two-participant fixture. |
| A default with no applicable asset returns typed `NoMatch`. | Boundary fixture containing only incompatible framework families. |
| Later destination selections retain the first governing target rather than source selected-folder TFMs, while every source node retains its owner-issued selected dependency group. | Three-node traversal fixture composing #7401 and #6424. |

The design is specification-only. Every property is unverified until its named
Release gate lands.

## Adoption map

Issue #7352 is the single end-to-end tracker. This focused pattern lands first;
existing owners adopt it independently:

1. Workspace Scope adds the non-null policy to construction plans and live
   Workspace snapshots.
2. Artifact Acquisition consumes it for a context that requires a framework
   and declares none.
3. The package composition tracked by #7401 associates each realized source
   participant with Package Dependency Evidence's selected group.
4. Package Traversal consumes the #7401 source association, uses the Workspace
   target for destination participant selection through #6424, and retires
   per-manifest no-request group selection for realized participants.
5. Workspace Definitions and share packets add versioned portable
   representation; legacy versions lower to the product default.
6. CLI and Browser/Wasm expose configuration through the same host-neutral
   plan and display retained governing-target evidence where their operation
   requires it.

These are sequenced follow-up efforts, not normative changes to those owners in
this document.

## Non-goals

- Redefining NuGet framework compatibility or nearest selection.
- Guessing a project target from an inspected assembly.
- Treating the default as a Platform family or version.
- Making incompatible Workspace participants compatible.
- Replacing an explicit or owner-issued required target.
- Selecting an already realized source participant's dependency group.
- Selecting the highest package folder without a concrete target.
- Adding a host-specific default or a second Workspace policy.
