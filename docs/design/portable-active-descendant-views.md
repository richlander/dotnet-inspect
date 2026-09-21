# Portable active descendant views

## Owner, claim, and status

**Workspace Definitions** owns this focused extension under
[#7475](https://github.com/richlander/dotnet-inspect/issues/7475):

> Schema version 4 and packet format 4 preserve which exact node of a direct
> Package's retained path is active, independently of deeper retained context
> and independently of an exact inspector request versus recommendation.

Format-4 construction, managed projection, exact selector resolution and
complete restoration are implemented, with the Release gates below. The #6971
query-adoption slice adds schema-version-4 query records and format-4
state-bound query and Library-scope projection without changing this
extension's subject contract. CLI and Browser capture/transport adoption
remain **unverified**. The schema/packet 2/3 active-subject vocabulary remains
Workspace and Package. Retained Library, Type and Member selectors already
exist; they do not make those nodes active.

The basis is the existing
[complete committed-view contract](workspace-definitions.md#complete-committed-views).
This adds three subject tags, not another selector vocabulary, Navigation
policy, query model or restoration lifecycle. The existing managed
`WorkspaceSharePacketCodec`, `WorkspaceSharePacketTransposer` and
`CommittedScenarioSelectorResolver` are implementation prior art: they already
separate a versioned portable request from exact resolved Navigation input.
The conventional design is an explicit versioned discriminated union, rather
than inferring selection from whichever optional field happens to be deepest.

## Demo

The real destination from
[portable coordinate replacement](portable-coordinate-replacement.md) is
`Avalonia@12.1.2/net8.0`. `Avalonia.Data.MultiBinding` is defined in
`Avalonia.Base, Version=12.1.2.0, Culture=neutral,
PublicKeyToken=c8d484a7012f9a8b`; its parameterless constructor has canonical
selector `M:Avalonia.Data.MultiBinding.#ctor()`.

This long-form view keeps the constructor as retained context while
selecting **Type**, not Member:

```json
{
  "schemaVersion": 4,
  "kind": "view",
  "id": "multi-binding-view",
  "states": [
    {
      "navigation": null,
      "subject": {"kind": "workspace"}
    },
    {
      "navigation": "avalonia",
      "subject": {"kind": "type"},
      "context": {
        "kind": "member",
        "library": {
          "name": "Avalonia.Base",
          "version": "12.1.2.0",
          "culture": null,
          "publicKeyToken": "c8d484a7012f9a8b"
        },
        "type": {
          "namespace": "Avalonia.Data",
          "segments": ["MultiBinding"]
        },
        "memberSignature": "M:Avalonia.Data.MultiBinding.#ctor()"
      },
      "facet": "type.metadata"
    }
  ]
}
```

It composes with version-4 peers whose `avalonia` navigation entry identifies
that direct Package and is focused. The complete decoded packet is:

```json
{
  "f": 4,
  "t": [["Avalonia", "12.1.2", "net8.0", null]],
  "g": [[0]],
  "r": [],
  "a": 0,
  "x": 0,
  "v": [
    {"t": null, "u": {"k": "workspace"}},
    {
      "t": 0,
      "r": {
        "k": "member",
        "l": ["Avalonia.Base", "12.1.2.0", null, "c8d484a7012f9a8b"],
        "y": "Avalonia.Data.MultiBinding",
        "s": "M:Avalonia.Data.MultiBinding.#ctor()"
      },
      "u": {"k": "type"},
      "f": "type.metadata"
    }
  ]
}
```

Whitespace is illustrative; the existing canonical writer determines bytes.
The restored outcome must have active Type, retained constructor, and exact
`type.metadata`. Changing only the subject to `member` and inspector to
`member.overview` makes the constructor active. Changing them to `workspace`
and `workspace.overview` retains the same path without promoting its Type or
Member. These are different semantic views, not alternate encodings of one
selection.

## Versioned representation

Version 4 uses version 3's Workspace, coordinate, registration, navigation,
context, query and view composition rules except for the subject extension
below. Every peer record in the composition uses schema version 4; the packet
uses exact integer `f: 4`. Schema/packet versions 1, 2 and 3 retain their
respective meanings. An older-format subject object containing a new tag is
invalid for that format, not an implicit upgrade.

The version-3 query-only attachment defined under #6971 remains separately
owned by its existing Definitions contract and is not admitted by version 4.
Version 4 otherwise inherits version 3's state-bound query and Library-scope
composition unchanged; #6971 supplies that query-bearing implementation.

The long-form subject still contains exactly `kind`; the packet counterpart
still contains exactly `k`. Both add the exact lower-case values `library`,
`type`, and `member` beside `workspace` and `package`. Do not copy Library
identities, Type names or Member selectors into the subject object.

The existing `context` / packet `r` carries the one retained path. The chosen
subject tag selects a node of that path:

| Active request | Required retained context on a direct Package row | Selected subject |
| --- | --- | --- |
| Absent | Omitted | Existing Package-only initial recommendation |
| `workspace` | Omitted or existing non-Package-only descendant context | Workspace, independently of the retained path |
| `package` | `package`, `allLibraries`, `library`, `type` or `member` | The row's exact Package |
| `library` | `allLibraries`, `library`, `type` or `member` | The retained Library node |
| `type` | `type` or `member` | The retained exact Type |
| `member` | `member` | The retained exact Member |

The table uses long-form context tags; packet spelling retains the existing
`all-libraries` mapping. An aggregate Library can be active with
`allLibraries` context. It cannot retain a Type/Member underneath that
aggregate: the existing contiguous path requires a Type's exact defining
Library. A Library-active row with Member context therefore selects that
exact defining Library, not the aggregate.

All pre-existing canonical omissions remain. In particular, explicit
Package-only context is not an alias for omission on a Workspace or
subject-less row. A descendant request with missing or shallower context is
invalid; it never asks for an initial-subject recommendation.

The leading null-navigation row still requests Workspace and forbids retained
Package context. Non-Package coordinate rows remain undecorated dormant
inventory. This extension introduces no Ecosystem, Platform, registered-Library
or other non-Package active structural grammar.

Packet property order, retained-selector spelling, canonical scalars, limits,
base64url encoding and all-or-nothing validation are inherited from format 3,
including its 64-coordinate table and per-context limit. The only new wire
values are format/schema version 4 and the three subject tags. In particular,
packet view rows remain ordered as `t`, optional `r`, optional `u`, optional
`f`, optional `q`, then optional `l`.

Packet 4 transposes only to version-4 peer records, and version-4 records
project only to packet 4. Round-tripping a format-4 Workspace-only state does
not downgrade it to format 3. Existing format-2/3 producer paths do not
silently start emitting format 4. Migration, automatic upgrade and legacy
consumer retirement are not part of this extension.

`WorkspaceSharePacket.CreateV4` and schema-version-4 records explicitly select
the new format. `WorkspaceSharePacketCodec.Format4Version` identifies it;
`CurrentFormatVersion` remains the existing format-3 producer default.

## Resolution and inspector intent

Resolve the existing retained selectors against the row's exact realized
Package through the ordinary Definitions boundary. Select the requested node
from that resolved path and supply it as the exact active subject in the
existing `NavigationInitialization`. Resolving a Member for retained context
does not make Member the active subject.

The Library identity is complete, the Type name is structured or its existing
injective escaped form, and the Member selector is its existing anchor or
canonical signature. Missing, ambiguous, incomplete, noncontiguous or
cross-occurrence resolution remains a typed preparation failure. Ordinary
restoration does not follow a new forwarder, search a namesake, repair a stale
Library identity, or apply coordinate-replacement correspondence. That
separate operation must first supply the actual destination path.

`facet` / packet `f` remains an opaque exact Registry ID. Its applicability
uses the requested **active** structural kind, not the deepest retained node.
Absence asks Navigation for recommendation; presence retains exact intent even
when it currently equals the recommendation.

The existing [View Facet Registry](view-facet-registry.md) and
[Navigation initialization](inspection-subject-navigation.md) outcomes remain:
unknown or inapplicable exact inspectors fail preparation; available,
unavailable or failed exact inspectors retain their request and native
evidence. The latter two have no effective inspector but remain complete
Navigation outcomes. Library aggregate capability comes from the actual
resolved Library subject, not from the `library` tag alone.

Diagnostics and availability verdicts do not enter the packet. On a fresh
restoration, they are obtained again under current capabilities; the durable
value is the requested subject/path/inspector, not the prior verdict.

Active and inactive Package rows obey the same selector and inspector
validation. Navigation focus still selects just one row for installation.
Inactive rows remain dormant exact inputs, not additional Navigation sessions
or live effect authority. The scenario's selected query context remains
independent of the row's structural occurrence.

This is a finite schema and selector-resolution extension. Existing
Definitions restoration, Navigation initialization, Scope, acquisition and
host-installation lifecycles and models are consumed unchanged. No new
association or concurrency protocol is introduced.

## Adoption and gates

This is a prerequisite branch of #7466's portable replacement plan within
the #7061 continuity initiative. The #7475 branch has **four steps**:

1. Lock this format-4 Definitions contract.
2. Implement version-4 records and managed codec/transposition, exact node
   resolution, and complete restoration together with the shared outcome
   gates. The initial slice left query state unsupported; #6971 adds the
   inherited state-bound query composition.
3. Adopt format-4 capture and replay in the CLI packet-context noun paths
   under #7379, consumed by portable replacement #7466.
4. Adopt the same managed boundary in Browser/Wasm capture, transport and
   restoration under #5510/#5511.

No sibling codec or client-side subject inference is added. Shared completed
host results retain `InspectionEnvelope<TContent>`; low-level restoration
recipes, activation handles and Navigation protocol values stay intermediates.
The CLI lowers typed content through Markout. Browser uses its existing typed
Navigation presentation for focus and interactive controls, not a second
portable-state parser.

Illustrative completed host calls are **mockups**, not API declarations:

```csharp
InspectionEnvelope<WorkspaceViewContent> result =
    await inspections.RestoreWorkspaceViewAsync(packet, cancellationToken);
Render(result.Content);
RenderShare(result.PortableProjection);
RenderDiagnostics(result.Diagnostics);
```

```typescript
const result: InspectionEnvelope<WorkspaceViewContent> =
    await inspections.restoreWorkspaceView(packet);
render(result.content);
renderShare(result.share);
renderDiagnostics(result.diagnostics);
```

The shared Release gates are `WorkspaceSharePacketV4CodecTransposerTests`,
including the format-4 state-bound query round trip, and the `Version4_*`
cases in `CompleteRestorationExecutionTests`.
The latter exercise the public consumer assembly, product-owned selector
resolution and complete unpublished-Workspace restoration, from both packets
and definitions. The Avalonia activation cases measured above two seconds and
are `Speed=Slow`: the focused pre-merge selection runs them, and the existing
daily Deep Inspect query suite retains them. Codec, shape, early-facet-refusal
and stale-forwarder cases are PR-fast.

`RegistrationOnly_RestoresWithoutAcquisition` covers versions 3 and 4.
`BrowserRetainedWorkspaceActivationTests.CommittedPacket_RemainsActivatable`
covers the existing managed installation seam for versions 2, 3 and 4; it
does not establish Browser capture or transport support. Existing CLI
format-3 authoring and Browser canonicalization admission remain unchanged.

These gates cover the shared cases below. Equivalent CLI and Browser
capture/replay remains **unverified** pending adoption steps 3 and 4:

| Case | Required result |
| --- | --- |
| Real Avalonia vector above | Canonical packet-record-packet identity; restored Type-active constructor context and exact inspector |
| Same path with Workspace, Package, Library, Type or Member active | Five distinct requests; each selects only its named node |
| Library-active `allLibraries` context | Exact Package aggregate and its native inspector-capability outcome |
| Missing/shallow context, subject-less descendant context, foreign Library/Type/Member | Typed refusal, not recommendation or selector repair |
| Type-active Member context with `member.overview` | Inapplicable inspector, not inferred Member activation |
| Exact inspector unavailable or failed | Request survives; native outcome reported outside the packet |
| Exact inspector equals recommendation / inspector omitted | Distinct exact and recommended intent round-trips |
| Inactive descendant row and unrelated registration | Preserved complete state and order; no second active session |
| Format 2/3 with new subject tag, mixed-version peers or unsupported reader | Visible version/shape failure before construction |
| Format 4 without descendants | Remains format 4; no implicit downgrade |
| Equivalent CLI and Browser capture/replay | Same canonical intent and complete envelope, without host inference |

The real identity and constructor selector were obtained from production
`dotnet-inspect` against `Avalonia@12.1.2` and its exact
`lib/net8.0/Avalonia.Base.dll` asset. The outcome gates exercise
product-owned codecs and restoration rather than having a harness repair the
packet or inject an active subject after restoration.
