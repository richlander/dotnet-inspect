# Demand-driven Member comparison

## Status and scope

**Proposed approach; implementation is not claimed.** This document describes
the execution and resource-reuse approach for Finding Diff Any's
[Member level](finding-diff-any.md#member), under
[#9716](https://github.com/richlander/dotnet-inspect/issues/9716).

The two goals are to avoid expensive work that the reader is unlikely to use,
and to keep an acquired Package Library warm for as long as the active
inspection needs it. Keeping resources available and computing results ahead
of demand are separate decisions.

Finding Diff Any remains the normative owner of the Member-level claim. This
is a composition plan: it references existing acquisition, correspondence,
retention and presentation contracts without redefining their internals.
Focused implementation slices must establish any missing owner capability
before claiming this approach is delivered.

## Intended experience

A reader opens `System.Text.Json@10.0.0`, selects `JsonSerializerOptions`,
sees a change cue against `9.0.0`, and opens one Member comparison. The
comparison uses the already acquired endpoint content and Library resources
where those exact resources are retained. It resolves the selected Member's
endpoints and compares that Member. It does not first decompile all changed
Members to manufacture an inventory from which to select one row.

Moving to another Member of the same Library pair reuses the endpoint
resources and requests only the new Member result. Switching C# and IL,
expanding Explore, or returning to a held result reuses applicable evidence;
it does not reacquire the Package or reopen the Library for presentation.

The existing Member Body design supplies a second motivating pair:
`System.Text.Json@11.0.0-preview.6.26359.118` to
`11.0.0-preview.7.26381.103`. The copy constructor changes while its API
declaration remains stable. Opening it should not require complete
implementation comparison of the Library.

## Work follows the visible question

| Demand | Work justified by that demand | Work deferred |
| --- | --- | --- |
| Package or Library navigation | Existing inventory and exact selected-Library acquisition | Sibling Library bodies and complete Member comparisons |
| Type cues | Existing Finding Diff Any request for that Type and baseline | Complete API/body diff documents and decompilation |
| Open one Member comparison | Exact endpoint selection, correspondence and the requested Member evidence | Whole-Library implementation inventory and other Members' complete diffs |
| Change a Member medium or expand Explore | Missing evidence for the selected medium, or rendering of held evidence | Repeating acquisition or previously completed evidence production |
| Explicit Library Compare | The Library inventory requested by that action | Complete detail documents for every inventory row |

This preserves the existing
[background queue](inspect-web-background-analysis.md#contract) and its
approved depth-1 baseline acquisition. It does not authorize additional
baseline downloads, deeper version windows, neighbor-Member prefetch, or
speculative decompilation. A selected comparison medium is demand; an
unopened medium is not automatically demand.

Joint demand means requirements already known to be needed together, such as
the selected view's surface and implementation requirements. It uses the
existing [exact Library demand planner](package-house-semantic-content-demand.md#joint-exact-library-inspection-demand).
It does not widen a cheap request to every capability a later gesture might
request, or wait for a timer to guess future work.

## Keep the needed Library warm

Warm means more than a cached download. There are three independently
observable opportunities for reuse:

1. **Package content:** reuse the source owner's admitted exact content and
   content generation. Do not repeat an equivalent in-flight transfer or
   download bytes already available under the owner's validity rules.
2. **Library resources:** reuse the active realization's selected Library
   owners, immutable images and applicable sessions/indexes. Retaining an
   archive while repeatedly constructing and disposing a reader for each
   Member does not meet this goal.
3. **Completed evidence:** reuse a valid detached result for the exact Member,
   endpoint pair, comparison policy and requested medium. Retaining Library
   resources does not require producing that result in advance.

The consumer declares the continuing need for the selected Library and, while
comparison is active, its selected baseline. Child operations borrow the
owner-managed resources for that need instead of independently acquiring and
closing them at each UI transition. Type-to-Member and Member-to-Member
navigation within that same admitted Library pair keep the continuing need.
An operation ending is not, by itself, the end of the view's need.

The physical owner remains the
[active Workspace realization](stateless-core-services.md#workspace), not a
new service cache or a UI-held reader. Before and After are endpoint roles
within that ownership; this plan does not introduce several active Workspace
realizations. Queries return detached evidence and hosts retain no borrowed
metadata reader, stream or session in a result or closure.

Reuse must preserve the existing exact source authority, content generation,
Library selection, framework/runtime context, surface/implementation roles,
binding policy and realization association. The owners' typed identities,
rather than Package names or file paths alone, establish whether resources
can be shared. Distinct reference and implementation images remain distinct;
one physical image serving both roles need not be opened twice for that reason.

Warmth is bounded by the existing owners' capacity and lifecycle contracts.
The active need protects resources only through owner-issued authority.
Failure to admit the required working set is visible; it must not silently
cause a download/reopen loop or return an incomplete result as complete.
This proposal promises reuse during one admitted need, not permanent residency
after leaving the Library or across page reloads.

## Supersession and release

Changing the selected Member supersedes that Member's unfinished expensive
work. It does not discard endpoint resources still needed by the successor.
Equivalent concurrent demand joins existing owner-supported work; canceling
one waiter does not cancel another waiter's required acquisition.

Changing a baseline releases the old comparison need after admitted work
settles, while preserving any independently continuing current-Library need.
A version, source, target or realization change must be evaluated under its
own owner's identity and cutover rules; it is not authority to reuse an old
reader just because the assembly name matches.

Leaving the inspection, closing the Workspace, or ending the last applicable
need releases resources through the existing owners' drainage and cleanup
contracts. Detached results can outlive live resources under their result or
cache contract; they do not keep acquisition authority alive. Operation
Authority still prevents an old operation from publishing into a new view.

## Select endpoints before producing detail

The Member request carries an exact selected declaration and the ordered
Library pair. Shared managed queries establish its counterpart and any
supported added/removed topology before the complete Member comparison runs.
The Browser does not pair names, reuse an image-local token across images, or
construct an absent endpoint from the lack of an equal anchor.

Existing [strict declaration correspondence](api-declaration-correspondence.md#strict-declaration-profile),
[qualified-anchor evidence](api-qualified-anchor.md#version-pair-correspondence)
and diff-owned changed-declaration pairing have different claims. In
particular, anchor equality excludes generic constraints and inequality is
not proof of removal. The targeted path consumes the applicable owner's
typed result, preserving ambiguity, refusal and failure.

Selection can require evaluating a complete relevant candidate set. The goal
is to avoid unrelated Library-wide projection and body work, not to promise
constant-time correspondence. Required preparation must be distinguished from
Member comparison cost in measurements.

The existing exact Member reader and designated/added-Member comparison
mechanisms remain the rendering and evidence path. An explicitly opened
Library inventory may supply existing endpoint evidence, but direct entry
from a Type cue must not require constructing that inventory first. If exact
selection cannot be established, show the typed non-success instead of
silently starting the whole-Library fallback.

## Owner handoffs

| Existing owner | Responsibility this approach consumes |
| --- | --- |
| [PackageHouse semantic demand](package-house-semantic-content-demand.md) | Exact Library/role requirements before acquisition; no sibling payload authorization |
| [Package Source](package-source-model.md#shared-operation-context-and-payload-lifetime) and [cache policy](package-cache-policy.md) | Authority, transfer sharing, content validity and payload lifetime |
| [Artifact and Workspace](artifact-acquisition-and-workspaces.md), [inspection space](../inspection-space.md#workspace-close-and-group-release-authority) and [Library inspection](assembly-inspection-query.md) | Admitted resources, image/session ownership, budgets and release |
| [Correspondence](api-declaration-correspondence.md) and [API diff presentation](library-api-diff-presentation.md) | Exact endpoint evidence, changed declarations and typed non-success |
| [Member Body](inspect-web-member-body-diff.md) and [Annotated Source diff](annotated-source-diff-document.md) | One complete Member document and its media |
| [Background analysis](inspect-web-background-analysis.md) and [Operation Authority](inspect-web-operation-authority.md) | Relevance, foreground ordering, supersession and publication |

## Delivery and evidence

Start by measuring the existing production sequence: Library open, Type cues,
one Member comparison, another Member, a medium change, and return. Record
transfers, transferred bytes, content materializations, physical Library/reader
opens, selected-MethodDef work, retained bytes and release. A scope-wrapper
invocation is not a physical reopen; a cache hit is not proof that no reader
was reconstructed. Existing diagnostics should establish these distinctions
before adding infrastructure or choosing the first implementation slice.

Then deliver focused owner adoptions: close the demonstrated acquisition or
resource-lifetime gap, establish the required targeted endpoint selection,
and connect it to the existing Member reader. Record each owner change and
its enforcing gate separately. [#11233](https://github.com/richlander/dotnet-inspect/issues/11233)
is a correspondence residual, not a requirement to finish every anchor
consumer before demonstrating a useful Member path. The design precedes
implementation; neither a broad retention framework nor a complete anchor
migration is assumed to be the first code change.

Acceptance evidence must cover:

- Cold then warm navigation over the pinned real pairs: no duplicate transfer
  of still-admitted equivalent content and no repeated physical opens of the
  same required Library image during the continuing need. Ranged acquisition
  can use several HTTP requests; request count alone is not duplicate download.
- Opening a Member without a prior Library inventory: no unrelated Member
  decompilation or whole-Library implementation comparison. Changing media
  produces only missing selected-Member evidence.
- Two consumers, cancellation of one, rapid Member/baseline changes, and
  source/target changes: correct sharing, independent outcomes, no stale
  publication and no cross-generation reuse.
- Distinct ref/runtime assets, an image serving both roles, added Members,
  changed signatures, overload ambiguity and acquisition/capacity failures:
  correct endpoints and visible non-success.
- End of need and Workspace close: admitted work settles and live resources
  release. Warm reuse must not be purchased with unbounded retained memory.

Publish exact-head Release NativeAOT cold/warm end-to-end time and allocation
results, resource counts and complete outcome parity. Production Browser/Wasm
gates separately demonstrate the actual interaction and lifetime sequence.
Avoided work and successful reuse are independent acceptance criteria; neither
can be inferred from a faster aggregate time alone.
