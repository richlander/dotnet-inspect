# Package Dependency Inspection

## Owner and claim

Package Dependency Inspection owns the completed host-neutral answer to one
exact Package manifest-dependency question.

> For one exact Package inspection input and optional requested target
> framework, Package Dependency Inspection composes owner-issued manifest facts
> and dependency-group selection into one detached available, no-manifest, or
> failed outcome and returns it in
> `InspectionEnvelope<PackageDependencyInspectionOutcome>`.

Package Manifest Facts owns manifest reading and validation. Package Dependency
Groups owns declared groups and target-framework selection. The inspection
executes and classifies those owner-issued results; it does not reinterpret
manifest declarations or acquire host presentation policy.

This owner is adopted by both production hosts under #10635. The CLI Package
section is the first adopter. #10818 makes Inspect Web the second adopter while
retaining Browser-owned platform presentation, retained Workspace state,
graph interaction, transport, and rendering.

## Content

`PackageDependencyInspectionOutcome` is an owner-specific Outcome:

- `Available` carries the exact manifest facts and dependency-group result;
- `NoManifest` distinguishes a valid package without a root manifest from an
  empty dependency population; and
- `Failed` carries a typed manifest-read, manifest-rejection, or unavailable
  content reason plus contained diagnostics.

Available groups preserve manifest occurrence order. Dependencies preserve
their owner-issued order and identity, including duplicates and case-distinct
declarations. Selection retains its typed status, requested target framework,
selected occurrence index, and selected logical group. A selected empty group,
no groups, and no matching target framework remain distinct.

The envelope's Share outcome is non-projectable until one exact Package
manifest-dependency request has a canonical Workspace projection. Supplemental
diagnostics are empty; non-success that determines the answer remains primary
content.

## Production boundaries

Hosts retain:

- source authorization, acquisition, and Package lifetime;
- target-framework intent and Browser runtime/platform presentation;
- retained Workspace dependency state;
- graph interaction, transport, and rendering.

Hosts consume the completed envelope once. They do not reopen Package content,
reparse the manifest, reselect the active group, normalize declaration
identity, omit conflicting declarations, or define a second baseline ordering.
Inspect Web may compose Browser platform-reference presentation beside the
envelope, but the envelope remains one identifiable transport value and
platform references do not become shared Package dependencies.

## Evidence

`Microsoft.Extensions.Hosting@10.0.0` / `net10.0` is the real populated
scenario. `System.Text.Json@10.0.0` / `net10.0` is the successful-zero
neighbor. Authored selected-empty, no-match, no-manifest, rejected-manifest,
unavailable-content, and conflicting-declaration cases prove that the outcome
does not collapse distinct states or retain host-local normalization.

The shared gate covers populated, successful-zero, selected-empty, no-match,
no-manifest, failed, ordering, duplicate, envelope, and source-generated JSON
behavior. The adopter gates prove CLI Package rendering and Inspect Web Package
Dependencies presentation consume the same owner-issued groups.

## Non-claims

This owner does not define Package acquisition, retained Workspace lifetime,
platform references, compile assets, Library references, transitive dependency
traversal, pruning, graph navigation, licenses, latest versions, or
presentation. It does not merge Package and Library questions or replace
neighboring Package or Library sections.
