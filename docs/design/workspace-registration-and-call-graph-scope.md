# Workspace registration and call-graph focal length

## Status and approved scope

This is the target experience specification for
[#6012](https://github.com/richlander/dotnet-inspect/issues/6012), under the
one-Workspace tracker
[#5697](https://github.com/richlander/dotnet-inspect/issues/5697) and the
platform-first tracker
[#6228](https://github.com/richlander/dotnet-inspect/issues/6228). The
construction-ownership replacement is tracked by
[#6570](https://github.com/richlander/dotnet-inspect/issues/6570). It is **not
implemented**; the target behavior and acceptance scenarios below remain
**unverified**.

The operator explicitly approved this bounded cross-owner replacement:

- the Workspace API defaults to an empty Workspace and has no curated option;
- the Ecosystems API owns the product's one curated Workspace composition,
  initially Platform, ASP.NET Core, and Microsoft.Extensions;
- callers explicitly choose raw or curated construction according to their
  operation;
- registration describes what the Workspace is about and supplies typed
  discovery populations;
- registration is not approval or permission to traverse;
- call-graph consumers choose among `Self`,
  `SelfAndRegisteredEcosystems`, and `Everything`; and
- `Everything` is the default.

This document replaces the former Approved Lazy Traversal specification. No
compatibility surface preserves "approved", "allowed", or permission-based
traversal. Component algorithms, schemas, identity, publication, acquisition,
and lifetime mechanics remain separate focused owner work.

The production consumers are Inspect Web and the CLI. Shared registration and
call-graph request contracts must reach both hosts; host-specific controls and
rendering remain with each host.

## Authority and exact claim

This document is the normative owner of one joined experience claim:

> A Workspace registration identifies a relevant exact library, package
> prefix, or ecosystem without granting reachability. A call-graph request
> independently chooses how far beyond its focal subject to traverse. The
> Workspace API defaults to no registrations, while the Ecosystems API may
> construct the product's one curated Workspace. Call graphs default to every
> resolvable participant within the Workspace that the caller actually chose,
> under explicit operation bounds.

This is an experience-composition owner, not a replacement owner for Workspace
Scope, Ecosystem Packs, source resolution, Call Graph, Workspace Definitions,
Navigation, or presentation.

## Three independent decisions

The product keeps these decisions separate:

| Decision | Meaning |
| --- | --- |
| Subject | The member, type, library, package-derived population, prefix, or ecosystem the user is inspecting |
| Workspace registration | Typed declarations describing relevant libraries and populations available to operations |
| Operation scope | The focal length, direction, depth, node limit, acquisition budget, and other work requested now |

Registration does not:

- grant or deny access to an edge;
- authorize a package source, credentials, or network use;
- override offline policy, source authorization, or resource limits;
- acquire, analyze, or admit content merely because it is registered;
- make a registration a graph seed unless the request selects it; or
- make every registered population part of every operation.

Acquisition and admission remain explicit operation effects. Content already
admitted to the Workspace remains usable after its registration is removed.
Removing a registration changes later population selection; it does not evict
content or invalidate an existing subject.

## Registration vocabulary

The version-1 registration vocabulary is a closed typed union:

```text
WorkspaceRegistration
  = ExactLibrary(source-owner-issued library coordinate)
  | PackagePrefix(owner-issued validated package-prefix declaration)
  | Ecosystem(owner-issued Workspace ecosystem declaration)
```

Each arm retains its owning component's exact value. The Workspace does not
infer a registration from display text, an assembly simple name, a namespace,
or an acquired package.

The ecosystem arm is not `DotnetInspector.Ecosystems.EcosystemPackId`.
Ecosystem Packs is an application catalog above Queries and browser Core, so
its type cannot flow into reusable Workspace state. The
[Workspace Ecosystem Registration Handoff](workspace-ecosystem-registration-handoff.md)
defines the lower-layer-consumable declaration and explicit catalog projection
from one selected pack. Scope retains the projected declaration without
referencing or rediscovering the application catalog.

### Exact library

An exact-library registration names one source-owner-issued library
coordinate. It may identify a platform or package-origin library without
converting either into the other's identity model.

The
[Exact Library Source Coordinate](exact-library-source-coordinate.md)
owns the closed package/Platform source distinction, exact Metadata assembly
identity, equality, and resource-free non-action retained by this arm.

A Package may contribute one or more admitted libraries, but Package
membership and exact-library registration remain distinct. Opening or
admitting a package does not silently register all of its libraries.

### Package prefix

A package-prefix registration supplies a typed discovery population. It does
not enumerate, rank, acquire, or continuously maintain the packages matching
that prefix. A call-graph request selecting that prefix supplies its own
finite discovery and acquisition bounds.

### Ecosystem

An ecosystem registration names one product-owned ecosystem contribution.
Ecosystem Packs may provide namespace hints, core-package priorities, package
sets, package prefixes, platform bindings, and Integration-owned knowledge.
Those contributions retain their owners' semantics.

Registering an ecosystem makes its contribution available to a consumer that
selects registered ecosystems. It does not execute a scanner, add curated
packages, or load an entire ecosystem.

The application catalog also owns the single curated product manifest. It
uses Platform, ASP.NET Core, and Microsoft.Extensions to construct the one
curated product Workspace through the shared ecosystem-registration handoff.
Scope owns neither those product choices nor a duplicate identity table.

## Workspace construction

The Workspace API has one neutral default construction meaning: without an
explicit registration set, a new Workspace has no registrations. The API may
accept a complete explicit initial set for restoration, raw caller
configuration, and higher-layer composition, but it exposes no curated
constructor, preset, flag, callback, or catalog hook. Raw construction performs
no acquisition or analysis and never consults
`DotnetInspector.Ecosystems`.

The Ecosystems API owns one current curated Workspace composition. Its initial
registration sequence is:

1. Platform
2. ASP.NET Core
3. Microsoft.Extensions

Curated construction passes that complete sequence to the Workspace API's
atomic explicit-initialization path and returns a new independent Workspace.
It is not a singleton Workspace instance and does not confer special
registration, acquisition, traversal, persistence, or lifetime semantics.

There is one curated composition, not a family of named presets or a
compatibility catalog of earlier compositions. The Ecosystems owner may change
it over time as product policy. A change affects only later curated
construction. It does not mutate an existing Workspace or reinterpret a saved
or shared definition.

Callers choose the construction owner according to their purpose:

- a discovery experience such as `find` may request the curated Workspace from
  Ecosystems;
- a high-fidelity or explicitly scoped operation may construct a raw Workspace
  and add only its declared inputs; and
- restoration constructs a raw Workspace and applies the complete persisted
  registration set.

No operation may infer curated intent from an empty registration sequence.
Curated registrations never appear because the editor opens, a subject becomes
active, Navigation changes focus, a graph runs, or restoration observes an
empty set.

The CLI and Inspect Web do not maintain separate curated manifests. Reusable
Workspace Scope remains independent of the application catalog, and callers
that choose raw construction do not reference the curated composition.

Platform, ASP.NET Core, and Microsoft.Extensions are separate registrations
even when the applicable platform target subsumes some ASP.NET Core or
Microsoft.Extensions packages. Platform/package pruning remains a per-target
fact; it does not merge ecosystem identities or rewrite saved registration
intent.

### Motivating real scenario

The raw-versus-curated choice is observable with
`System.Memory.Data@11.0.0-preview.7.26381.103`, whose `net10.0` package
declarations include `System.Text.Json@11.0.0-preview.7.26381.103`.

- `find System.Text.Json` is a discovery question. Its target adoption chooses
  the Ecosystems-curated Workspace so Platform libraries participate without
  making Platform intrinsic to every Workspace.
- package-mode `depends` is a package-authorship question. Its target adoption
  chooses raw construction so the package-declared route remains the
  high-fidelity starting point.
- adding explicit Platform scope registers `ecosystem.platform` and activates
  pruning eligibility. The pruning owner still compares against the exact
  selected platform target; a .NET 10 target cannot subsume that newer .NET 11
  preview package merely because the assembly name overlaps.

These commands are named consumers, not contracts redefined here. Search Scope
Resolution, Dependency Inspection, and Platform/package Pruning retain their
request, evidence, comparison, and result semantics.

### End-to-end product scenario

The primary product scenario begins with one subject and grows into a
cross-library question without making product curation an ambient Workspace
default:

1. A discovery-oriented CLI or Inspect Web operation selects a package or
   Library. With no reusable active Workspace, the host may explicitly ask
   Ecosystems for a fresh curated Workspace; raw callers do not take this
   path. Inspect Web Spotlight instead preserves an active Workspace when the
   exact destination is already admitted or covered by its current
   registration-bearing Scope revision, as owned by
   [Spotlight destination
   activation](inspect-web-spotlight-destination-activation.md).
2. Ecosystems passes the complete Platform, ASP.NET Core, and
   Microsoft.Extensions registration sequence through Workspace's neutral
   explicit-initialization API. Construction performs no source work.
3. The selected package becomes explicit Workspace membership and its selected
   Library, type, or member becomes the inspection subject. Membership,
   registration, and focus remain independent.
4. A call-graph request defaults to `Everything`. It may therefore use all
   registered and already admitted populations available through that
   Workspace, while the request still supplies finite discovery, acquisition,
   traversal, and result bounds.
5. Resolution retains the exact route and evidence selected for each edge.
   Platform registration makes target-applicable Platform candidates and
   pruning available; it does not convert package-authored evidence into
   Platform evidence or require every consumer to prefer Platform.
6. Saving or sharing the resulting configuration records the exact expanded
   membership and registration intent selected for that Workspace. Restoration
   uses raw construction and never re-evaluates the product's later curated
   manifest.

For example, a user may discover `System.Memory.Data`, focus a member whose
dependency path reaches `System.Text.Json`, and ask for a graph that continues
through relevant Platform or ecosystem Libraries. Curated construction supplies
the population context that makes that broader question useful. Package-mode
`depends` asks a different question and therefore starts raw, preserving the
package-authored `System.Text.Json` route as its high-fidelity evidence.

The shared evidence can therefore support two explicit policies:

```text
System.Memory.Data (package membership)
└─ System.Text.Json
   ├─ dependency definition: retain the package-authored route
   └─ curated traversal: an exact target-applicable Platform route may prune
      the package edge when ecosystem.platform is registered
      └─ continuation remains available to System.Text.Encodings.Web
```

The declaration and resolution layers preserve both possible routes and their
source identities. `depends` and graph traversal select policy from the
question being answered; neither reconstructs source intent from assembly
display names.

This scenario is the reason construction choice belongs to the caller,
registration belongs to Workspace configuration, and focal length belongs to
the operation. Combining any two would either hide product policy inside
Workspace, make registration a traversal permission, or make saved Workspaces
drift when product curation changes.

## Call-graph focal lengths

Call Graph owns one typed request axis with three values. The axis composes
with the request mode owned by
[Inspection Graph Modes](inspection-graph-modes.md):

| Value | Display meaning | Population admitted for traversal |
| --- | --- | --- |
| `Self` | Self | The selected registration population or the selected member's containing exact library |
| `SelfAndRegisteredEcosystems` | Self + registered ecosystems | `Self` plus every ecosystem registration in the current Workspace revision |
| `Everything` | Everything | Every exact-library, package-prefix, ecosystem, package-derived, and already admitted participant available through the current Workspace |

`Everything` is the default for both hosts. Opening one package and requesting
a multi-depth graph should be able to leave that package. Narrowing is a
consumer choice made where the graph is requested, not permission granted
before the request starts.

### Self

For a member or type in an exact library, `Self` means that exact library. It
does not mean only the selected method body, every library in the same package,
or every currently loaded Package.

When the selected subject is a package prefix or ecosystem, `Self` means the
bounded population selected for that exact registration by the request. It
does not add other Workspace registrations.

### Member-seeded and registration-seeded graphs

A member-seeded graph retains the existing single-seed contract. Focal length
chooses which Workspace participants may contribute caller or callee evidence;
it does not replace the selected member or turn a registration into another
seed.

An exact library, package prefix, or ecosystem may also be the selected input
without inventing a focal member. That collection form uses Inspection Graph
Modes' induced-set contract over the finite realized participant set. It has no
focus subject, direction, or depth queue: it retains `call` evidence only when
both endpoint closures are admitted, preserves disconnected selected inputs,
and reports population bounds or failures.

Population selection and graph mode therefore remain separate:

| Selected input | Graph mode |
| --- | --- |
| Member | Single seed with directed caller/callee traversal |
| Exact library, package prefix, or ecosystem | Induced set over a finite realized participant population |

Future peer-member requests may use the existing peer-seed contract, but a
prefix or ecosystem registration does not itself manufacture those member
seeds.

### Self + registered ecosystems

This focal length starts with `Self` and adds all ecosystem registrations from
the exact Workspace revision bound to the request. A curated Workspace
initially contributes its three ecosystem registrations; a raw Workspace
contributes none until its caller or user adds them.

Other exact-library and package-prefix registrations do not join this mode
merely because they are registered. A request may select one of them as
`Self`, or use `Everything`.

### Everything

`Everything` admits all Workspace registrations and all already admitted
libraries. It may also follow package-derived resolution routes supplied by the
resolution owner, admitting newly resolved participants into the same
Workspace under the operation's source authorization and bounds.

"Everything" means everything available through this Workspace, not every
package on nuget.org, every installed SDK, or an unbounded global search.
Unregistered remote populations do not become ambient search sources merely
because this mode is broad.

## Traversal, resolution, and acquisition

Focal length selects relevance scope. It does not define call edges, binding,
resolution, package discovery, or physical acquisition.

The call-graph consumer uses the owner-issued
[Assembly Reference Resolution Ladder](assembly-reference-resolution-ladder.md)
tracked by #6288. Exact in-context binding, applicable platform resolution,
and package-derived resolution retain their own typed outcomes. The graph
never manufactures a package coordinate from an assembly name, namespace,
ecosystem hint, or display label.

Every request has finite depth, node, candidate, byte, and acquisition-work
bounds appropriate to its host. Retiring the permission gate makes the
acquisition budget load-bearing: `Everything` is permissive, not unbounded.
Budget exhaustion, source denial, missing candidates, ambiguity, failed
acquisition, and incomplete binding remain visible typed outcomes.

For a member-seeded graph, the focal subject is evaluated before wider
populations. A broader focal length cannot replace the seed with an ecosystem
or package that happened to rank earlier. For an induced graph, every selected
input remains represented even when it contributes no retained call edge.
Under a bound, omitted wider work is reported as limited coverage rather than
a complete absence result.

Incoming and outgoing traversal use the same focal-length value but may need
different candidate work. Resolving every encountered outgoing edge does not
prove that every possible caller population was searched.

## Call-graph result and presentation

Every result carries:

- the selected input and its optional focus role;
- the requested focal length;
- the single-seed or induced-set graph mode;
- the exact Workspace revision or equivalent owner-issued scope identity;
- the effective participant population;
- direction, depth, node, acquisition, and discovery bounds;
- population and traversal completeness; and
- typed resolution, acquisition, analysis, and correspondence failures.

The existing host-neutral Call Graph projection remains the structured graph
currency. The CLI continues to lower it through Markout. Inspect Web consumes
the same typed result and may render host-native controls and Mermaid without
reconstructing graph identity or focal-length semantics.

Issue [#6248](https://github.com/richlander/dotnet-inspect/issues/6248)
separately owns cause-oriented incomplete-result diagnostics. A caller that
chooses curated construction gains Platform registration and should remove the
common missing-corelib case once the resolution and acquisition path is
adopted. Raw callers retain responsibility for their explicit population, and
the diagnostic remains necessary for other missing populations and failures.

## Workspace and persistence experience

Registration appears as Workspace configuration, not as a security or
permission editor:

```text
Workspace                                                   [Edit]
  Registered scope
    Platform
    ASP.NET Core
    Microsoft.Extensions
    Contoso.*
    Contoso.Application.dll

  Call graph
    Focal length: Everything                [Change]
```

The call-graph control offers:

```text
Self
Self + registered ecosystems
Everything
```

The editor adds and removes exact libraries, package prefixes, and ecosystems.
Adding curated packages remains an independent content action. Registering an
ecosystem does not add its curated set, and adding the set does not register
the ecosystem.

Workspace Definitions owns portable representation of the complete expanded
registration set, including an empty set.
The selected call-graph focal length is view intent, not Workspace membership;
its portable location remains that owner's focused adoption decision. An
unsupported packet fails visibly rather than dropping registrations or
substituting the current curated composition. Definitions persist the expanded
registration set, not a durable `curated` bit whose meaning could drift.

## Analogous designs

Visual Studio Call Hierarchy exposes a visible search-scope selector such as
current document, current project, or solution. JetBrains products likewise
separate a hierarchy's focus from reusable project scopes:

- [Visual Studio Call Hierarchy](https://learn.microsoft.com/en-us/visualstudio/ide/call-hierarchy)
- [JetBrains Rider Call Hierarchy](https://www.jetbrains.com/help/rider/Code_Analysis__Call_Tracking.html)
- [JetBrains Rider Scopes](https://www.jetbrains.com/help/rider/Settings_Scopes.html)

dotnet-inspect follows the conventional decision to place hierarchy breadth on
the hierarchy consumer. It deliberately diverges from source IDEs by using
ecosystem and package-prefix registrations because binary inspection can
discover relevant content beyond a checked-out project. The finite Workspace
and per-operation bounds keep that broader model explicit.

## Ownership and adoption

| Participating owner | Responsibility retained |
| --- | --- |
| [Workspace Scope and Expansion](workspace-scope-and-expansion.md) | Committed Package membership, registration revision, complete snapshots, and scope-operation results |
| [Static Ecosystem Packs](ecosystem-packs.md) | Ecosystem identity, curated Workspace manifest, static contributions, and projection onto a lower registration declaration; not reusable Workspace state or call-graph scope |
| [Workspace Ecosystem Registration Handoff](workspace-ecosystem-registration-handoff.md) | Lower ecosystem declaration, explicit pack correspondence, projection outcomes, and curated construction validation |
| [Platform Library Population Declaration](platform-library-population-declaration.md) and other source owners | Platform relevance values, exact-library and package-prefix declarations, and later source-specific candidate outcomes |
| [Inspection Graph Modes](inspection-graph-modes.md) | Single-seed versus induced-set request meaning, focus roles, endpoint admission, and disconnected-input retention |
| [Call Graph projection](call-graph-projection.md) and Queries | Focal-length request, participant population, call traversal or induction, bounds, completeness, and typed graph result |
| [Workspace Definitions](workspace-definitions.md) | Portable complete registrations, including an empty set, and view-intent projection |
| [Inspect Web Spotlight Destination Activation](inspect-web-spotlight-destination-activation.md) | Exact current-Workspace coverage classification and Browser activation settlement; not registration construction or traversal permission |
| CLI host | Per-command raw-versus-curated choice, request binding, Markout lowering, and CLI disclosure |
| Inspect Web | Raw-versus-curated experience choice, editor and focal-length controls, and host-native interaction |

There are ten counted production-adoption stages, tracked by #6012:

1. Lock this replacement contract and retire Approved Lazy Traversal
   terminology and design authority.
2. Land the owner-issued assembly-reference resolution ladder and finite
   per-operation acquisition-budget contract tracked by #6228.
3. Define the lower-layer ecosystem-registration declaration and Ecosystem
   Packs projection under #6307, then extend catalog and source owners with the
   exact typed platform, exact-library, and package-prefix contributions and
   one curated manifest for Platform, ASP.NET Core, and
   Microsoft.Extensions.
4. Revise Workspace Scope from expansion permission to inert registration,
   make default Workspace API construction empty, accept complete explicit
   initial registrations, and expose no curated option.
5. Have Ecosystems construct one fresh independent Workspace from its current
   complete curated manifest through the public Workspace API.
6. Add the three typed focal lengths to the host-neutral call-graph request and
   result, with `Everything` as the default. Adopt both member-seeded
   neighborhoods and registration-seeded induced call graphs through Inspection
   Graph Modes.
7. Adopt complete expanded registration persistence in Workspace Definitions,
   including an empty set, plus any focused portable call-graph view intent.
8. Have each CLI command explicitly choose raw or Ecosystems-curated
   construction, then adopt registration disclosure and focal-length selection.
9. Adopt the same explicit construction choice, Workspace editing, Spotlight
   destination activation, and focal-length selection in Inspect Web, removing
   permission-oriented controls and copy.
10. Complete separately authorized product release and website deployment.

Each implementation PR adopts this pattern in one owning component. This
document does not authorize one PR spanning Workspace Scope, catalog, source
resolution, Call Graph, persistence, CLI, and Browser implementation.

## Acceptance and evidence

The following are required future outcome-level scenarios:

| Scenario | Required observation |
| --- | --- |
| Construct directly through the Workspace API | The registration set is empty; no catalog lookup, acquisition, or analysis occurs |
| Construct through the Ecosystems curated API | A new independent Workspace contains Platform, ASP.NET Core, and Microsoft.Extensions in order; no registration-triggered acquisition or analysis occurs |
| Remove one or all curated registrations, then navigate, open another subject, save, and restore | The exact registration set survives; the current curated composition does not reappear |
| Change the curated manifest in a later product build | Later curated construction uses the new complete manifest; existing and restored Workspaces retain their exact registrations |
| Run `find` for the real `System.Text.Json` overlap | The command explicitly chooses curated construction and can discover the Platform library without making curation intrinsic to Workspace |
| Run a raw package-focused operation | The operation receives no ambient ecosystem registration and adds only its explicit scope |
| Register an exact library, package prefix, or ecosystem | Registration is visible and inert until selected operation demand |
| Adopt Inspect Web Spotlight destination activation | Current-Workspace coverage classification and external-package restoration conform to their focused owner without making registration eager |
| Run one member-seeded graph at all three focal lengths | The seed stays fixed; `Self` remains local, the middle mode adds registered ecosystems, and `Everything` admits all Workspace populations |
| Run the same graph without an explicit focal length in either host | The request uses `Everything` |
| Start with no admitted libraries and use a package prefix or ecosystem as `Self` | Discovery and realization are bounded; the result is an induced graph with no fabricated focal member |
| Induce a graph over a registration population with disconnected libraries | Every selected input remains represented, or its bounded omission or acquisition failure remains visible |
| Use `Everything` with a dependency outside the initial package | The graph may leave the package through an owner-issued resolution route without a permission grant |
| Exhaust acquisition or discovery work | Existing graph evidence remains usable and the omitted population is reported as limited, not complete |
| Deny source use or fail candidate acquisition | The failure remains visible and does not mutate registration intent |
| Encounter missing Platform or another unresolved population | Cause-oriented diagnostics identify the missing input rather than leading with raw incomplete-node counts |

Shared owner suites run in Release and gate empty construction, curated
construction, request defaults, exact registration association, bounded
population selection, and result coverage. CLI tests gate command-owned
construction choice, Markout, and structured output. Browser original-host and
Firefox suites gate construction choice, editing, focal-length controls, and
exact restoration. No separate TLA+ model is required for the static
raw-versus-curated choice: Workspace revision and lifetime models retain their
existing state-machine obligations, while the new contract adds no runtime
manifest mutation or concurrent join. The design remains unverified until
those focused adoptions land.

The absence-claim coverage is deliberate. Existing project-and-assembly gates
fully cover the dependency direction that keeps Ecosystems out of Queries and
browser Core. No dedicated source-prohibition gate scans for a curated
Workspace option or copied host manifest; ownership review enforces those
boundaries, while positive product gates exercise the empty default, complete
explicit initialization, the Ecosystems-owned curated constructor, and each
host's selected call path.

## Non-claims

This design does not define:

- simultaneous active-Workspace composition, host collection presentation, or
  cross-Workspace queries;
- a security boundary based on registrations;
- an eager import or continuously maintained package-prefix population;
- a global NuGet, SDK, or filesystem crawl;
- package ownership inferred from assembly names or namespaces;
- call-edge, correspondence, pruning, or package-candidate algorithms;
- one universal operation scope for Integrations, search, dependencies, or
  other consumers;
- a durable named curated profile, version selector, or compatibility archive;
- runtime mutation of the curated manifest or an existing Workspace when
  product policy changes;
- a new graph renderer or duplicate CLI/Browser graph model;
- compatibility flags, aliases, or saved semantics for approved or allowed
  traversal; or
- implementation, merge, release, or deployment authorization.
