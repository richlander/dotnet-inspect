# Workspace registration and call-graph focal length

## Status and approved scope

This is the target experience specification for
[#6012](https://github.com/richlander/dotnet-inspect/issues/6012), under the
one-Workspace tracker
[#5697](https://github.com/richlander/dotnet-inspect/issues/5697) and the
platform-first tracker
[#6228](https://github.com/richlander/dotnet-inspect/issues/6228). It is **not
implemented**; the target behavior and acceptance scenarios below remain
**unverified**.

The operator explicitly approved this bounded cross-owner replacement:

- fresh Workspaces register Platform, ASP.NET Core, and Microsoft.Extensions;
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
> independently chooses how far beyond its focal subject to traverse. Fresh
> Workspaces start with Platform, ASP.NET Core, and Microsoft.Extensions
> registered, and call graphs default to every resolvable participant within
> the Workspace under explicit operation bounds.

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
  = ExactLibrary(owner-issued source-native library coordinate)
  | PackagePrefix(owner-issued validated package-prefix declaration)
  | Ecosystem(owner-issued Workspace ecosystem declaration)
```

Each arm retains its owning component's exact value. The Workspace does not
infer a registration from display text, an assembly simple name, a namespace,
or an acquired package.

The ecosystem arm is not `DotnetInspector.Ecosystems.EcosystemPackId`.
Ecosystem Packs is an application catalog above Queries and browser Core, so
its type cannot flow into reusable Workspace state. A focused prerequisite
must define a lower-layer-consumable ecosystem-registration declaration and a
catalog projection from one selected pack onto that declaration. Scope retains
the projected declaration without referencing or rediscovering the application
catalog.

### Exact library

An exact-library registration names one source-native library coordinate. It
may identify a platform or package-origin library without converting either
into the other's identity model.

A package Root may contribute one or more admitted libraries, but package
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

The application catalog also owns the single product default manifest. It
projects Platform, ASP.NET Core, and Microsoft.Extensions through the shared
ecosystem-registration handoff. Scope owns neither those product choices nor a
duplicate identity table.

## Fresh Workspace defaults

Every ordinary fresh Workspace starts with these ecosystem registrations, in
this order:

1. Platform
2. ASP.NET Core
3. Microsoft.Extensions

The application catalog supplies this list through the shared registration
handoff to both product hosts. The CLI and Inspect Web do not maintain separate
default manifests, and reusable Workspace Scope does not depend on the
application catalog.

Default registration performs no acquisition or analysis. A fresh empty
Workspace is therefore useful before any package or library has been loaded.
Its first operation may select a registered ecosystem, an exact library, or a
package prefix and then perform only the demand declared by that operation.

Defaults apply only to fresh construction. They do not reappear when:

- the Workspace editor opens;
- a package or library becomes active;
- a call graph runs;
- Navigation changes focus; or
- a saved or shared Workspace is restored.

Users may remove any default. Explicit construction and restoration supply the
complete registration set, including an empty set, so opt-outs survive future
opens, navigation, save, share, and restore.

Platform, ASP.NET Core, and Microsoft.Extensions are separate registrations
even when the applicable platform target subsumes some ASP.NET Core or
Microsoft.Extensions packages. Platform/package pruning remains a per-target
fact; it does not merge ecosystem identities or rewrite saved registration
intent.

## Call-graph focal lengths

Call Graph owns one typed request axis with three values. The axis composes
with the request mode owned by
[Inspection Graph Modes](inspection-graph-modes.md):

| Value | Display meaning | Population admitted for traversal |
| --- | --- | --- |
| `Self` | Self | The selected registration population or the selected member's containing exact library |
| `SelfAndRegisteredEcosystems` | Self + registered ecosystems | `Self` plus every ecosystem registration in the current Workspace revision |
| `Everything` | Everything | Every exact-library, package-prefix, ecosystem, Root-derived, and already admitted participant available through the current Workspace |

`Everything` is the default for both hosts. Opening one package and requesting
a multi-depth graph should be able to leave that package. Narrowing is a
consumer choice made where the graph is requested, not permission granted
before the request starts.

### Self

For a member or type in an exact library, `Self` means that exact library. It
does not mean only the selected method body, every library in the same package,
or every currently loaded Root.

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
the exact Workspace revision bound to the request. The three fresh defaults
therefore participate unless the user removed them.

Other exact-library and package-prefix registrations do not join this mode
merely because they are registered. A request may select one of them as
`Self`, or use `Everything`.

### Everything

`Everything` admits all Workspace registrations and all already admitted
libraries. It may also follow Root-derived resolution routes supplied by the
resolution owner, admitting newly resolved participants into the same
Workspace under the operation's source authorization and bounds.

"Everything" means everything available through this Workspace, not every
package on nuget.org, every installed SDK, or an unbounded global search.
Unregistered remote populations do not become ambient search sources merely
because this mode is broad.

## Traversal, resolution, and acquisition

Focal length selects relevance scope. It does not define call edges, binding,
resolution, package discovery, or physical acquisition.

The call-graph consumer uses the owner-issued assembly-reference resolution
ladder tracked by #6228. Exact in-context binding, applicable platform
resolution, and package-derived resolution retain their own typed outcomes.
The graph never manufactures a package coordinate from an assembly name,
namespace, ecosystem hint, or display label.

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
separately owns cause-oriented incomplete-result diagnostics. Default Platform
registration should remove the common missing-corelib case once the resolution
and acquisition path is adopted, but the diagnostic remains necessary for
other missing populations and failures.

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

Workspace Definitions owns portable registration and opt-out representation.
The selected call-graph focal length is view intent, not Workspace membership;
its portable location remains that owner's focused adoption decision. An
unsupported packet fails visibly rather than dropping registrations or
substituting fresh defaults.

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
| [Workspace Scope and Expansion](workspace-scope-and-expansion.md) | Committed Root membership, registration revision, complete snapshots, and scope-operation results |
| [Static Ecosystem Packs](ecosystem-packs.md) | Ecosystem identity, product default manifest, static contributions, and projection onto a lower registration declaration; not reusable Workspace state or call-graph scope |
| Source and resolution owners | Exact library, package-prefix, platform, and package-derived candidate outcomes |
| [Inspection Graph Modes](inspection-graph-modes.md) | Single-seed versus induced-set request meaning, focus roles, endpoint admission, and disconnected-input retention |
| [Call Graph projection](call-graph-projection.md) and Queries | Focal-length request, participant population, call traversal or induction, bounds, completeness, and typed graph result |
| [Workspace Definitions](workspace-definitions.md) | Portable registrations, opt-outs, and view-intent projection |
| CLI host | Shared default construction, request binding, Markout lowering, and CLI disclosure |
| Inspect Web | Shared default construction, editor and focal-length controls, and host-native interaction |

There are nine counted production-adoption stages, tracked by #6012:

1. Lock this replacement contract and retire Approved Lazy Traversal
   terminology and design authority.
2. Land the owner-issued assembly-reference resolution ladder and finite
   per-operation acquisition-budget contract tracked by #6228.
3. Define the lower-layer ecosystem-registration declaration and Ecosystem
   Packs projection, then extend catalog and source owners with the exact typed
   contributions and one product default manifest for Platform, ASP.NET Core,
   Microsoft.Extensions, exact libraries, and package prefixes.
4. Revise Workspace Scope from expansion permission to inert registration,
   including the shared three-ecosystem fresh-construction default.
5. Add the three typed focal lengths to the host-neutral call-graph request and
   result, with `Everything` as the default. Adopt both member-seeded
   neighborhoods and registration-seeded induced call graphs through Inspection
   Graph Modes.
6. Adopt complete registration and opt-out persistence in Workspace
   Definitions, plus any focused portable call-graph view intent.
7. Adopt registration construction, disclosure, and focal-length selection in
   the CLI.
8. Adopt registration construction, Workspace editing, and focal-length
   selection in Inspect Web, removing permission-oriented controls and copy.
9. Complete separately authorized product release and website deployment.

Each implementation PR adopts this pattern in one owning component. This
document does not authorize one PR spanning Workspace Scope, catalog, source
resolution, Call Graph, persistence, CLI, and Browser implementation.

## Acceptance and evidence

The following are required future outcome-level scenarios:

| Scenario | Required observation |
| --- | --- |
| Construct a fresh Workspace in either host | Platform, ASP.NET Core, and Microsoft.Extensions are registered in order; no registration-triggered acquisition or analysis occurs |
| Remove one or all defaults, then navigate, open another subject, save, and restore | The exact registration set and opt-outs survive; defaults do not reappear |
| Register an exact library, package prefix, or ecosystem | Registration is visible and inert until selected operation demand |
| Run one member-seeded graph at all three focal lengths | The seed stays fixed; `Self` remains local, the middle mode adds registered ecosystems, and `Everything` admits all Workspace populations |
| Run the same graph without an explicit focal length in either host | The request uses `Everything` |
| Start with no admitted libraries and use a package prefix or ecosystem as `Self` | Discovery and realization are bounded; the result is an induced graph with no fabricated focal member |
| Induce a graph over a registration population with disconnected libraries | Every selected input remains represented, or its bounded omission or acquisition failure remains visible |
| Use `Everything` with a dependency outside the initial package | The graph may leave the package through an owner-issued resolution route without a permission grant |
| Exhaust acquisition or discovery work | Existing graph evidence remains usable and the omitted population is reported as limited, not complete |
| Deny source use or fail candidate acquisition | The failure remains visible and does not mutate registration intent |
| Encounter missing Platform or another unresolved population | Cause-oriented diagnostics identify the missing input rather than leading with raw incomplete-node counts |

Shared owner suites run in Release and gate request defaults, exact
registration association, bounded population selection, and result coverage.
CLI tests gate Markout and structured output. Browser original-host and Firefox
suites gate default construction, editing, focal-length controls, and opt-out
restoration. The design remains unverified until those focused adoptions land.

## Non-claims

This design does not define:

- simultaneous live Workspaces, Workspace switching, tabs, or cross-Workspace
  operations;
- a security boundary based on registrations;
- an eager import or continuously maintained package-prefix population;
- a global NuGet, SDK, or filesystem crawl;
- package ownership inferred from assembly names or namespaces;
- call-edge, correspondence, pruning, or package-candidate algorithms;
- one universal operation scope for Integrations, search, dependencies, or
  other consumers;
- a new graph renderer or duplicate CLI/Browser graph model;
- compatibility flags, aliases, or saved semantics for approved or allowed
  traversal; or
- implementation, merge, release, or deployment authorization.
