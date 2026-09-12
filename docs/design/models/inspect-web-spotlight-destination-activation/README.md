# Inspect Web Spotlight destination activation model

## Owner and claim

[Inspect Web Spotlight destination
activation](../../inspect-web-spotlight-destination-activation.md) is the
normative owner. This model checks its interaction boundary:

> One exact Spotlight candidate is classified into a rendered descriptor
> against one captured active Workspace, Scope revision, publication base,
> complete registration projection, and Package occurrence inventory.
> Selection transfers that retained basis into a new activation intent rather
> than reclassifying from live state. A current Package may commit before focus
> settles, while only a current Definitions-issued complete activation may
> publish a fresh Workspace.

This is bounded design evidence, not implementation conformance.

## Finite instance

`SpotlightDestinationActivation.tla` uses:

- two activation intents;
- three result generations, with at most one stale descriptor refresh before
  each next activation intent;
- one initial active Workspace, one external replacement, and one
  Definitions-issued fresh Workspace identity per intent;
- three Scope revisions, four publication bases, and three complete
  registration profiles;
- one already admitted Package occurrence;
- same-name Platform and package `System.Text.Json` candidates;
- one Platform `System.Text.Json.JsonSerializer` Type and one `Serialize`
  Member candidate emitted from a resident Platform surface;
- one package-origin `System.Text.Json` Library candidate; and
- one `Microsoft.Extensions.Logging` package covered by the initial curated
  registration profile.

Registration profile 1 covers the Platform `System.Text.Json` Library and
`Microsoft.Extensions.Logging`, but not the package `System.Text.Json`
coordinate or package-origin Library. Profile 2 covers the package-origin
Library through its exact-Library registration only; it does not cover the
enclosing Package generally. Profile 3 covers both source families and gives
the package-origin Library three overlapping witnesses: exact Library, direct
package prefix, and ecosystem-nested package prefix. The finite values preserve
exact source, registration order, and Package occurrence distinctions without
reproducing package, metadata, or Platform coordinate representation.

## Modeled boundary

The model retains the product join currencies used by Spotlight:

- rendered result generation;
- activation intent;
- active Workspace identity;
- Scope revision;
- Scope publication base;
- complete registration profile and ordered coverage witnesses;
- exact Package occurrence;
- exact destination identity and source family; and
- current versus fresh-Workspace publication.

Adjacent operations are abstracted at their observable boundaries. A current
Package admission either commits a new Scope revision or does not. Focus
settles separately and must consume the retained or newly issued exact Package
occurrence for a package-origin Library. Workspace Definitions nondeterministically
returns a complete activation or failure; the model does not reproduce its
construction or cleanup phases.

The model does not copy Workspace Scope, Navigation, Platform, source,
Artifact, or retained-host collection transitions. Their detailed outcomes are
represented only as nondeterministic success, failure, revision movement,
Workspace replacement, or settlement at the consumed boundary.

## Checked properties

| Claim | Positive gate | Detecting mutation |
| --- | --- | --- |
| Same-name Platform and package destinations retain source-specific classification | `Safety` | `BrokenCollapseSameNameCoverage` |
| A realized Platform Library selected again in the same Workspace retains its Browser-local Platform action after covering registration is removed | `SafetyRepeatPlatform`, `ReachabilityRepeatPlatform` | `BrokenRepeatPlatformNavigation` |
| Platform Type and Member selections retain exact Browser-local Platform actions | `SafetyPlatformDescendants`, `ReachabilityPlatformDescendants` | `BrokenPlatformDescendantNavigation` |
| Platform realization remains scoped to its exact Workspace | `SafetyRepeatPlatform`, `ReachabilityReplacementPlatformIsolation` | `BrokenCrossWorkspaceRealization` |
| Every overlapping registration contribution remains in exact order | `Safety`, `ReachabilityOverlappingWitnesses` | `BrokenDropOverlappingWitnesses` |
| Selection retains the rendered descriptor's captured plan and basis rather than reclassifying from live state | `SafetyStaleDescriptor`, `ReachabilityPreselectionRegistrationStale`, `ReachabilityPreselectionWorkspaceStale` | `BrokenRebindRenderedDescriptor` |
| Every plan arm has an exact reachable settlement, including exact-Library-only package admission, Platform Type/Member activation, and never-realized uncovered Platform unavailability | `ReachabilityNavigateCurrent`, `ReachabilityCurrentMembership`, `ReachabilityAddCurrentPackageLibrary`, `ReachabilityMembershipCoveredLibrary`, `ReachabilityPlatform`, `ReachabilityPlatformDescendants`, `ReachabilityFreshWorkspace`, `ReachabilityUnavailableLibrary`, `ReachabilityUnavailablePlatform` | positive census, not a mutation pair |
| Current Package membership classifies its not-yet-realized Library without duplicate Add | `Safety`, `ReachabilityMembershipCoveredLibrary` | occurrence checks below |
| Package-origin Library focus uses the exact retained or Scope-returned occurrence | `Safety` | `BrokenWrongLibraryOccurrence` |
| One attempt cannot produce both current and fresh-Workspace effects | `Safety` | `BrokenDualCurrentAndFreshPublication` |
| Covered failure never creates a fallback Workspace | `Safety` | `BrokenFallbackAfterCoveredFailure` |
| Current Package commit, direct focus, post-commit focus, and fresh publication require current captured authority | `Safety` | `BrokenStaleCurrentCommit`, `BrokenStaleDirectFocus`, `BrokenStalePostMembershipFocus`, `BrokenStaleFreshPublication` |
| Membership committed before focus failure or supersession remains present | `Safety`, `ReachabilityPartialMembershipFailure` | covered by the ordinary failure and supersession state space |
| A failed Definitions result is never published by the host | `Safety` | `BrokenPublishFailedFreshActivation` |
| Every bounded attempt settles under weakly fair adjacent completion | `Liveness` | safety mutations are not treated as liveness evidence |

The eight safety profiles and eight liveness profiles expect TLC exit 0. Their
fixed destination schedules partition the package/Platform source pair,
covered-Package, exact-Library-only, Package-to-Library, and
overlapping-Library, repeated-Platform, and Platform Type/Member scenarios while
retaining every permitted revision, publication-base, failure, replacement,
and supersession placement within each profile. The stale-descriptor profile
also explores render, preselection registration movement, descriptor
replacement, selection, and settlement; the other profiles admit environment
movement before rendering or after selection so this dedicated profile owns
the preselection registration-change interleavings. A separate bounded
Platform scenario owns active-Workspace replacement before selection. Each of
the fourteen `Broken*.cfg` files expects exit 12 at its named invariant. The
seventeen
`Reachability*.cfg` files
intentionally check a false absence invariant and expect exit 12 when TLC
reaches every plan arm, exact-Library-only package admission, Platform
Type/Member activation, repeated Platform activation after in-place coverage
removal, replacement-Workspace isolation, never-realized uncovered Platform
unavailability, complete overlapping coverage, visible failure, post-commit
focus failure, stale/superseded settlement, or stale-before-selection
settlement after registration or active-Workspace change.

The checked profiles explore 126,863 to 842,480 distinct states each. All 47
registered configurations produced their exact expected semantic verdict.

All configurations are registered with their exact expected semantic verdict
in
[`eng/tla-expected-exit-codes.txt`](../../../../eng/tla-expected-exit-codes.txt).

## Running and limits

Run the repository gate from the repository root:

```bash
printf '%s\0' \
  docs/design/models/inspect-web-spotlight-destination-activation/SpotlightDestinationActivation.tla \
  | ./eng/run-tla-checks.sh --changed-files0
```

The model is finite and bounded. It checks every permitted interleaving within
the configured instance; it is not an inductive proof for unbounded
activations. It does not prove implementation identity construction, source
authorization, package-prefix matching, Scope admission, Navigation
reconciliation, Platform activation, Workspace Definitions construction or
cleanup, or Browser result rendering.
