# Portable Package-coordinate replacement

## Owner, claim, and status

**Workspace Definitions** owns this focused transformation contract under
[#7466](https://github.com/richlander/dotnet-inspect/issues/7466). It specializes
[realization-backed portable transformation](workspace-definitions.md#realization-backed-portable-transformation):

> Derive one complete portable scenario from the exact input scenario and its
> correlated Package-coordinate replacement outcome, preserving unrelated
> intent; otherwise return a visible typed refusal rather than a misleading
> success packet.

The shared transformation, completed envelope and definition-first CLI route
are implemented. `WorkspacePortableCoordinateReplacementTests` and
`WorkspaceCommandTests` own their Release gates below. Browser coordinate
controls and capture/install adoption remain **unverified**, tracked by
issues #5510 and #5511. Fresh Navigation preparation across explicitly supplied
source and destination Workspaces landed in
[#8094](https://github.com/richlander/dotnet-inspect/pull/8094); production
adoption of that boundary is tracked by
[#8128](https://github.com/richlander/dotnet-inspect/issues/8128).

The conventional basis is an immutable document transformation: evaluated facts
may justify a derived declaration, but evaluation state is not the declaration.
The existing dependency-enrichment design is analogous prior art for exact
context placement and complete output/refusal. Unlike dependency enrichment,
replacement also changes the selected coordinate's committed view. Neither
analogy authorizes a new matching policy or a packet schema extension.

## Demo

The real witness is `Avalonia@11.3.14 -> 12.1.2/net8.0`.
`Avalonia.Data.MultiBinding` moves from `Avalonia.Markup` through a Type
forwarder to its defining `Avalonia.Base` Library. The target portable result
retains an active Type with `type.metadata`, or its exactly corresponding
constructor with `member.overview`, using the destination's complete Library
identity and structural selectors.

For a format-4 input `$w` retaining that Type, the CLI operation is:

```bash
dotnet-inspect workspace --packet "$w" \
  --replace-package 1 --to-version 12.1.2 --json --envelope
```

The following summarizes the semantic before/after rather than the JSON shape:

```text
Input scenario
  selected Package: Avalonia 11.3.14, net8.0
  active subject:   Avalonia.Data.MultiBinding
  defining Library: Avalonia.Markup
  exact inspector:  type.metadata
  other context:   System.Text.Json 10.0.0, net10.0

Request: replace the selected Package Version with 12.1.2

Derived scenario
  selected Package: Avalonia 12.1.2, net8.0
  active subject:   Avalonia.Data.MultiBinding
  defining Library: Avalonia.Base
  exact inspector:  type.metadata
  other context:   System.Text.Json 10.0.0, net10.0 (unchanged)

Outcome evidence, outside the packet
  exact Type correspondence through a forwarder
```

The neighboring missing-Member result preserves Navigation's supplied fallback
and explanation, not a same-named overload. An explicitly active Library
retains the Library-level policy even when a retained descendant could follow
a forwarder. Definitions projects these results; it does not choose them.

Schema/packet versions 2 and 3 encode only
Workspace or Package as the active subject. A retained Type is not an active
Type. The full demo uses
[#7475](https://github.com/richlander/dotnet-inspect/issues/7475), the focused
[format-4 active-descendant committed-view adoption](portable-active-descendant-views.md),
whose shared implementation landed in #7509. Replacement preserves the input
schema version; it never silently upgrades formats 2/3 or rewrites an active
descendant as Package.

## Immediate boundary

Inputs are one validated committed scenario, one selected direct Package
navigation row, and explicit destination intent. The operation restores the
source scenario, then realizes one separate destination Workspace and obtains
the complete Navigation successor outcome associated with those exact
endpoints. The selection must resolve to one unique direct Package member
position in one
`WorkspaceContextAddress` of that input composition. Equal Package names,
coordinates, context labels from another composition, or the scenario's
selected query context do not establish that association. Multiple matching
member positions are a typed source-selection refusal in this first operation.

The first operation changes an exact Version, a TFM, or both within the same
canonical Package ID. The selected source and destination Versions are exact
pins. Floating selected sources, group-expanded members, Package-ID changes,
query-bearing scenarios, and batch replacement are outside this operation and
receive typed refusals rather than implicit expansion or normalization.

The process-local association must connect the exact input composition and
selected row/member position to the source Navigation basis, the successor
Workspace's Add settlement, and the successor query's fresh destination
Navigation state plus detached retention evidence. Source and destination
Workspace identities must differ even for a same-coordinate request. Equal
coordinate strings or an unrelated successful correspondence result do not
substitute for that chain. Definitions consumes each owner-issued association;
it does not relabel either Workspace's occurrence, content generation,
selection, retained intent, or publication epoch.

Supporting owners keep their contracts:

| Owner | Consumed responsibility |
| --- | --- |
| [Workspace Scope](workspace-scope-and-expansion.md) | Append-only destination admission, exact operation association and actual terminal membership |
| [Navigation successor preparation](navigation-coordinate-successor-preparation.md) | Exact source/destination association, fresh destination lineage, retention, fallback and inspector outcome |
| [Forwarded correspondence](forwarded-api-coordinate-correspondence.md) | Exact Library/Type/Member evidence, supplied through Navigation |
| [Definitions](workspace-definitions.md#complete-committed-views) | Existing committed-view, target, schema, validation and projection contracts |
| [Acquisition](artifact-acquisition-and-workspaces.md) | Source authorization, realization, admission and drainage |
| [Inspection envelope](inspection-envelope.md) | Completed Content, Share and diagnostics boundary |

This is a finite definition transformation, not another asynchronous lifecycle.
Existing Scope, Navigation and realization models retain authority. No new
concurrency model or lower-owner contract is introduced by this slice.

## Derived scenario obligations

The selected member and its navigation coordinate denote the requested exact
destination under the existing coordinate and acquisition-target rules. The
selected row's committed state comes from the completed destination result:
active subject, retained context, actual defining Library, structural Type and
Member selectors, and exact-versus-recommended inspector intent remain
distinct. The original inspector request is not replaced by an effective
fallback merely because the two currently render alike.

Do not replay the source Library identity or Member selector against the new
coordinate and call that correspondence. The destination selector must be
issued from the exact destination facts used by the completed outcome. Missing
Member/Type results retain the supplied truncated context, subject and
inspector outcome. An unrepresentable exact request is non-projectable, not
silently dropped.

Preserve the order and meaning of all unselected members, subscriptions,
registrations, navigation rows and committed states, the selected query
context, and navigation focus. Record-local references remain coherent.
Canonical transposition may apply its existing name/encoding normalization;
this is semantic preservation, not a promise of byte-identical authored JSON.
A destination that would duplicate another normalized navigation source is a
typed refusal under the existing uniqueness rule, not permission to merge rows
or discard either retained view.

### TFM changes do not retarget neighbors

The [member-coordinate contract](workspace-definitions.md#member-coordinates)
requires one coherent acquisition target per context. A member declaration
does not override a conflicting context target.

This first operation permits a changed TFM only when the selected Package is
the context's sole member and the context has no subscription. Change that
context's existing TFM declaration and any explicit member/navigation TFM
declarations consistently with the requested target; if the target was declared
only on the member, retain that placement. Preserve RID intent. The ordinary
target validator remains authoritative.

A changed TFM in a shared or subscribed context is a visible unsupported
transformation. Do not retarget its neighbors, split the context, move the
Package, or invent an override. A Version-only replacement preserves target
declarations and uses ordinary compatibility validation.

The requested acquisition target belongs in the definition. The actual
compatible compile asset chosen by the Package owner is observation evidence,
not permission to rewrite that target.

### Complete outcome or refusal

A derived result contains a complete validated scenario. Its canonical packet
or URL is the Share outcome for that same scenario. A valid complete definition
outside the packet projection is still distinguished from an invalid or
incomplete derivation by the existing typed non-projectable result.

Acquisition failure, historical-only membership, incomplete destination
preparation, an unassociated outcome, or a state the portable grammar cannot
represent produces no derived-success packet. Do not attach the input packet
as the replacement result's successful Share. A same-coordinate request still
constructs a fresh destination Workspace and Navigation lineage; it may yield
the same canonical packet only after that complete successor preparation.

The finite operation never publishes either Workspace. Destination admission,
Navigation preparation and projection complete while both exact Workspaces are
alive, then both Workspaces close before detached Content, Share and diagnostics
return. A projection refusal does not claim rollback; it reports the actual
destination Add and Navigation evidence retained before close. It does not
authorize a Browser consumer to reinstall a retired source snapshot.
Installation remains with its existing owner.

Correspondence evidence, explanations and runtime outcomes belong in detached
Content/diagnostics, not in the packet. The final host-neutral boundary is
`InspectionEnvelope<TContent>`; neither the CLI nor Browser reconstructs the
association, retention policy, or portable selectors from rendered text.
Low-level Scope/Navigation protocol tickets remain intermediates.

## Production adoption

This is the portable branch of the six-capability
[#7061](https://github.com/richlander/dotnet-inspect/issues/7061) delivery map,
tracked by #7466. It does not reopen completed base CLI work in #5513. The
portable branch has **six steps**, including its prerequisite and both hosts:

1. Lock this focused Definitions transformation contract.
2. Land #7475's active-descendant committed-view/packet prerequisite under its
   own versioned contract.
3. Implement the shared definition transformation and completed envelope,
   consuming the existing coordinated replacement producer. Any missing
   lower-owner boundary is a separately recorded prerequisite, not a CLI
   implementation of matching or a hidden lifecycle rewrite.
4. Adopt it in the definition-first `workspace` command under #7466.
5. Adopt the shared result for Browser coordinate controls under #5510.
6. Complete Browser installation and portable capture under #5511.

Definition-first authoring #7427 and noun-command packet context under #7379
are additional explicit prerequisites, not completed by this contract. The
full CLI path lets a noun command supply the selected subject and inspector in
its scenario, then transforms that scenario. It adds no duplicate noun grammar
to `workspace --active-package`. Retirement of that transitional path remains
under [the existing adoption plan](workspace-definitions.md#adoption-and-evidence).

Steps 1-4 are implemented. The C# completed operation is:

```csharp
InspectionEnvelope<WorkspacePortableCoordinateReplacementOutcome> result =
    await WorkspacePortableCoordinateReplacementOperation.ExecuteAsync(
        inputScenario,
        new WorkspacePackageCoordinateReplacementRequest(
            "package-0", version: "12.1.2"),
        restorationOptions,
        cancellationToken);
```

The implemented CLI path now uses the fresh-successor boundary above.
The remaining #6751 step removes the protected same-Workspace Navigation Scope
producer and `ReplaceScope` API after their last production consumer is gone.

Browser adoption remains planned; this TypeScript call site is a **mockup**,
not a shipped export or parser:

```typescript
const result: InspectionEnvelope<WorkspacePortableCoordinateReplacementOutcome> =
    await workspaceCommands.replacePackageCoordinate(inputScenario, replacement);
renderReplacement(result.content);
renderShare(result.share);
renderDiagnostics(result.diagnostics);
```

The CLI selects the source by one-based packet navigation-row order using
`--replace-package`, and destination intent with `--to-version`, `--to-tfm`,
or both. It requires either `--share packet|url` or `--json --envelope` so the
complete durable result, rather than only a summary, is always emitted.
The JSON registration is `workspace-coordinate-replacement`, schema version 1.
Sections preserves typed native settlement, correspondence statuses and
inspector outcome, with display-only subject labels; the canonical Share owns
portable selectors. It omits live effect/action authority. NuGet source options
are admitted because replacement explicitly authorizes acquisition.

There is no human summary in this slice. Scalar packet/URL output uses the
canonical codec, and JSON uses source-generated metadata and the shared
envelope writer, intentionally bypassing Markout for these machine protocols.
Any later CLI human-readable summary lowers typed Content through Markout.
Browser interactive rendering uses the typed
projection owned by [Navigation Presentation](inspect-web-navigation-presentation.md)
because focus, accessibility and responsive controls are host concerns.
Neither host serializes a runtime snapshot as portable state.

## Gates and non-claims

Run the focused Release gates:

```bash
dotnet run --project tests/DotnetInspector.Queries.Tests -c Release -- \
  --filter-class '*WorkspacePortableCoordinateReplacementTests'
dotnet run --project tests/DotnetInspect.Cli.Tests -c Release -- \
  --filter-class '*WorkspaceCommandTests'
```

The shared gate covers derived Type/constructor restoration, missing-Member
fallback, active-Library truncation, unavailable exact inspector, unchanged
coordinates, formats 2/3, unrelated intent and inactive-row focus, target/RID
preservation, Workspace-active rows with omitted Package-only context or
preserved deeper context, early refusals and acquisition failure. Its envelope
cases cover
separate source/destination Workspace identity, destination Add settlement,
typed retention, canonical Share, fallback diagnostics and source-generated
serialization. The CLI gate adds option admission, packet/URL output, complete
JSON envelope and restoration of the returned packet. Real-Avalonia cases
measured above the two-second threshold are `Speed=Slow`; this focused
pre-merge gate and daily Deep Inspect own them. Early refusal and option cases
remain PR-fast.

`NavigationCoordinateSuccessorQueryTests` gates the consumed source/destination
association and non-preparation policy. The protected same-Workspace
`NavigationCoordinateReplacementTests` remains a neighboring regression gate
until #6751 step 5 removes that path. Dedicated portable
after-admission projection/cleanup-failure injection and equivalent Browser
requests remain **unverified**; no alternate lifecycle or trusted-caller
hardening is introduced to manufacture those cases.

| Boundary | Required outcome |
| --- | --- |
| Real Avalonia Type and constructor forwarding | Destination selectors and exact inspector survive derived-scenario restoration |
| Missing Member and explicitly active Library | Native fallback/ancestor policy and explanation, without fabricated lower selection |
| Unavailable exact inspector | Exact request and typed outcome retained, or explicit projection refusal |
| Same coordinate | Complete associated result; unchanged canonical packet allowed |
| Wrong input/result association | Refusal; no derived-success packet |
| Repeated matching source positions or duplicate destination navigation source | Explicit refusal, no guessed row or merged view |
| Unrelated Package, dormant state and registration | Unchanged portable meaning and order |
| Singleton TFM change / shared-context TFM change | Coherent selected target / visible refusal without retargeting neighbors |
| Committed Scope followed by preparation or projection failure | Actual outcome plus refusal, not rollback or successful input Share |
| Unsupported active descendant or query-bearing state | Visible refusal, no reduced-state packet |
| Equivalent CLI and Browser requests | Same Content, Share and diagnostics through the completed boundary |

No new correspondence algorithm, migration match, dependency population,
acquisition policy, query rebinding, packet version, Browser history protocol,
or filesystem persistence is specified here. In particular,
[#7454](https://github.com/richlander/dotnet-inspect/issues/7454)'s
three-population History remains separate.
