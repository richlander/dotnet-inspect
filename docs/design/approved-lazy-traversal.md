# Approved lazy traversal

## Status and approved scope

This is the target experience specification for #6012 under the one-Workspace
tracker #5697 and ecosystem delivery tracker #5728. It is **not implemented**;
the target behavior and acceptance scenarios below remain **unverified**.

The operator explicitly approved this bounded cross-owner experience contract:
Workspace policy and defaults, ecosystem knowledge, source/type-resolution
handoffs, operation populations, reference navigation, and preserved saved
intent. The operator subsequently separated Platform opening into #6013.
The approved refinements include independent traversal and package-loading
choices, one-time bounded imports, and a first-class Workspace viewer inventory.
Component algorithms, schemas, identity, publication, and lifetime
mechanics remain separate, focused owner work. This document is the normative
owner of the joined experience, not a replacement owner for those components.

Inspect Web is the primary experience consumer. Shared discovery, ecosystem,
Scope, and query capabilities must also reach the stateless CLI; this does not
change CLI defaults or prescribe new command-line syntax.

## Claim

An inspection subject, permission to traverse, and the work demanded by an
operation are distinct. Registering a traversal domain performs no acquisition
or analysis. A requested operation may acquire eligible inputs within its
declared bounds, without silently enlarging its subject or search population.
Explicit loading selects content independently of traversal permission. A
bounded import is one requested addition, not an ongoing population to maintain.

A Workspace with no acquired packages is useful. It can retain traversal
intent and launch an operation against an explicitly selected prefix,
ecosystem, or Platform. Its first operation does not require an invented
package subject or an already loaded graph hub.

## Three decisions, not one loaded flag

| Decision | Meaning |
| --- | --- |
| Subject | What the user is inspecting or asking the operation to examine |
| Allowed traversal | Which additional domains may supply inputs when that operation needs them |
| Demand | What the selected operation must resolve, acquire, or examine now |

A subject may also belong to an allowed traversal domain. Permission does not
make a domain a subject, graph root, query population, or admitted Root.
Acquiring a dependency does not, by itself, change the selected subject.

For collection operations, a prefix or ecosystem is a query scope under the
Workspace, not a new structural Navigation subject kind. A concrete Library,
Type, or Member destination still requires the Navigation owner's identity.

Registration does not grant source credentials or override offline policy,
source authorization, or resource limits. Already admitted content remains
usable after a permission is removed; removal prevents future permission-based
expansion rather than evicting earlier content. Explicitly opening a chosen
subject is a separate acquisition request, not implicit permission for every
later traversal in that domain.

## Prefix, ecosystem, and Platform

These remain distinct user choices:

| Choice | Knowledge available to an operation |
| --- | --- |
| Raw package prefix | A package-domain discovery boundary, without requiring a product catalog entry |
| Ecosystem | Declared source domains, optional curated packages, compact namespace hints, core-package starting points, and Integration-owned contract knowledge |
| Platform | A named ecosystem with the applicable product-issued platform target and library catalog |

An ecosystem is not merely a display alias for a prefix. Selecting a raw prefix
does not silently select an ecosystem's other contributions. Ecosystem
discovery metadata does not authorize packages outside the effective declared
traversal domains.

An ecosystem need not be NuGet-based. Platform is an ecosystem at the catalog
and Workspace-registration level, while retaining its source-native identity,
target selection, and acquisition semantics. Its UI can still say "Platform".
This does not turn platform libraries into packages or replace Scope's typed
platform eligibility rules.

Ecosystems may select source-owned discovery/acquisition bindings. Several
ecosystems can share an adapter, and an ecosystem can use more than one.
The binding contracts and catalog extensions require focused owner adoption;
this document defines no loader interface or dynamic plugin mechanism.
Demand remains operation-specific, not a general instruction to load an entire
ecosystem. Existing source adapters supply the physical acquisition behavior.

Namespace contributions are a small, curated set of distinctive namespace
roots, potentially covering subtrees such as `Microsoft.Aspire.Hosting.*`.
They are retrieval hints, not an exhaustive namespace inventory or exclusive
ownership assertions. A hint match prioritizes a candidate route; a hint miss
does not prove absence or make an otherwise eligible package ineligible.
Metadata establishes actual type identity and relationships.

Core packages are useful seeds and retrieval priorities, not the complete
ecosystem and not an instruction to load packages during registration. An
optional curated package set is a separate explicit selection whose membership
comes from the [Package Set Registry](package-set-registry.md), not from
prefix expansion. Its members may overlap those starting points without
turning either list into exhaustive ecosystem membership.
Integration types and roles are supplied through Integration-owned knowledge,
not a second Browser classifier. Selecting an ecosystem makes its contributions
available to requested operations; it does not execute its scanner.

The [Ecosystem Pack owner](ecosystem-packs.md) must issue the additional
discovery contribution before hosts can consume it. This specification does
not add fields to its current registration or invent lower-level currencies.

## Workspace construction choices

For an ecosystem contributing a curated package set and a package prefix,
these choices compose independently:

| Choice | Content requested now | Traversal permission |
| --- | --- | --- |
| Allow ecosystem | None | Register its declared traversal paths |
| Add curated packages | Resolve and acquire the curated set's explicit members | Leave existing permissions unchanged |
| Allow package prefix | None | Register that prefix without selecting the whole ecosystem |
| Allow prefix and add top N | Discover a bounded population and acquire its selected package results | Keep the prefix registered for later traversal |

The ordinary fresh-Workspace defaults still apply; "unchanged" means that
adding packages grants no additional ecosystem or prefix permission. Users
can also add one curated package without adding its siblings. For a
twelve-package curated set, selecting the set means those twelve members, not
the first twelve matches from its prefix.

Top N is a one-time bounded import. Package Query owns discovery, ordering,
selection, and population disclosure; Source and Scope owners resolve and
admit the selected inputs. The ordering, requested bound, and selected
population are visible. N counts selected package results, not necessarily
net-new Workspace members when some are already present.

Adding to an existing Workspace does not replace or evict its prior members.
Removing an imported package later does not refill its place or revoke the
prefix permission. A later explicit demand can still use that permission.
Changing a ranking, revisiting the viewer, or restoring the Workspace does
not rerun the import; any additional import is a new explicit request.
Admission uses Scope's existing outcomes. The UI distinguishes permission
state from import success or failure rather than displaying an allowed domain
or a selected candidate as loaded content.

The separate Package Query "Load as workspace" suggestion remains an
undeveloped UX note under #6012; this specification does not define that action.

## Default construction

The target defaults for a newly constructed Inspect Web Workspace are:

| Domain | Default |
| --- | --- |
| Platform | Registered for on-demand traversal |
| Microsoft.Extensions ecosystem | Registered for on-demand traversal |
| Other ecosystems and raw prefixes | Explicit opt-in |

The Browser supplies these defaults when it constructs a new runtime Workspace
for fresh intent, whether through Home, a Spotlight package opening, or the
empty editor. It does not reapply them whenever the editor opens, a package
becomes active, or the UI rerenders.

Package Open, including Spotlight Open, is not new construction when a runtime
Workspace already exists. That Open explicitly supplies the current
Workspace's registrations, including an empty set after both defaults were
removed. It neither reapplies defaults nor relies on implicit Scope
inheritance.

Users can unregister either default through the same trailing removal control
used for other registrations. Navigation within the current Workspace
preserves opt-outs. Restoration supplies recorded intent instead of
construction defaults, even when it requires a new runtime Workspace.

### Why Microsoft.Extensions is a default

Platform supplies ordinary framework references. Microsoft.Extensions adds
common dependency-injection, configuration, and logging contracts with
distinctive namespace roots and useful core packages. These are useful
retrieval routes even when the initial package is unrelated to an application
framework.

The default has costs: more future acquisition is permitted, candidate routes
can overlap, and the editor has another registration to explain. It must not
cause startup downloads, automatic scanner execution, or additional initial
graph roots. Other ecosystems remain opt-in because their domain assumptions
are less general.

This decision follows a contract and interaction walkthrough, not measured
performance or user research. Shipping it requires the default-is-inert,
bounded-resolution, population-isolation, and opt-out scenarios below. A
catalog entry without the required working retrieval path must not be
presented as supported traversal.

## Platform opening is separate

Issue #6013 owns Spotlight -> Platform library list -> Library selection,
building on the Library experience in #5836 and PR #5911. That browsing and
opening experience is outside this specification. The only shared boundary is
intent: opening a chosen Library creates subject demand; registering Platform
permits later traversal and does not itself open or load Platform.

## Following a reference

Use already available identity and reference evidence before catalog hints.
An unresolved reference may be actionable when a supported, approved
resolution route is available. Its action means "resolve this target", not
"this definition is already known to exist".

Rendering or hovering that action performs no speculative package acquisition.
Activation supplies demand. For example, following an `ICollection` reference
may resolve and acquire its applicable Platform input. A fully qualified
Aspire reference may use ecosystem hints to prioritize eligible package
candidates. A short type name alone is not package correspondence.

The resolver returns metadata-confirmed identity or an honest unresolved,
ambiguous, bounded, denied, or failed outcome. The Browser does not manufacture
an exact package coordinate from a namespace, assembly display name, or hint.
A miss does not authorize searching outside the approved domains.

The prior subject remains usable if resolution cannot produce a destination.
Successful navigation and supersession consume the existing Navigation
outcomes, history classification, and focus authority; there is no additional
traversal-specific publication or focus protocol.

## Operations choose their population

| Operation | Required demand |
| --- | --- |
| Follow a type reference or known call edge | Resolve the selected destination |
| Expand an outgoing call graph | Examine the requested frontier under explicit depth and work bounds |
| Non-hub graph, callers, or implementors | Establish and examine a declared candidate population |
| Integrations | Analyze the requested subject/population with the selected operation's stated scanner scope |

An operation's subject or population is explicit even when no package is
loaded. It may be a selected prefix, ecosystem, Platform, or an explicitly
chosen union. Registered defaults do not silently join that population.
An explicit request for all approved domains is different from selecting one
prefix and must disclose the resulting scope.

For a raw prefix, discovery produces a bounded population under a declared
ordering. Download ranking is a reasonable discovery option, not evidence of
relationship relevance or global completeness. Ecosystem core packages can
provide useful initial candidates; they do not establish complete coverage.
Namespace hints may prioritize retrieval but cannot silently filter a broad
query's population to the hinted namespaces.

The operation distinguishes population coverage from reference resolution.
Resolving every encountered reference does not establish that all possible
callers, implementors, or disconnected graph components were examined.
Results identify the selected population, applied bounds, and failures;
"nothing found in these packages" is not absence across the whole prefix.

Candidate versions, framework assets, binding, and admission come from their
existing owners. A population that cannot fit the supported operation or
Workspace limits is refused or reported as an explicitly bounded operation,
not silently shortened or accommodated by evicting prior members.
Increasing a bound is an explicit request whose result carries its own
coverage; this specification promises neither cache reuse nor a stable live
source ordering between runs.

## Workspace and persistence experience

### Viewer, editor, and Package Query

Workspace is a browsing subject, not only a management page. For a
package-based Workspace, its viewer presents the admitted package inventory
in the main content area. Selecting a package enters the existing Package,
Library, Type, and Member hierarchy. Non-package inputs retain their
source-native subjects rather than being labelled as packages.

A fresh multi-package construction with no explicit navigation target lands
on this Workspace inventory, not an arbitrarily selected first package.
Additions to an existing Workspace preserve the current inspection/navigation
unless the user separately requests a destination.

The viewer also makes an ecosystem's curated packages easy to discover and
add, individually or as a set. Available curated packages remain visibly
distinct from admitted packages, including in a zero-package Workspace.
Displaying that catalog knowledge requires no package-content acquisition;
discovering a live prefix population remains an explicit query.

The [Workspace editing contract](inspect-web-workspace-editing.md) owns the
explicit Save/Cancel and dirty-navigation boundary. Drafts are not inspection
inputs, and selecting Inspect never implicitly saves or discards edits.

The editor configures traversal permissions and membership. Package Query
selects a bounded population to add. The viewer browses the resulting content
and can offer contextual Add actions without requiring a trip through the
editor. These surfaces reuse the same owner-issued selections and operations.

The subject strip selects the Workspace browsing level; it need not contain
one tab per package. Exact strip placement, viewer/editor presentation, and
responsive layout remain focused Navigation/Presentation work coordinated
with the in-flight Library hierarchy in #5911.

### Intent and persistence

These are informative layout sketches; the Browser presentation owners retain
placement, responsive behavior, and control mechanics.

The viewer makes a constructed package set browsable without entering the
editor. This example ecosystem and its twelve-member set are illustrative:

```text
Workspace                                                   [Edit]
  Packages (2)
    Example.Core                                            [Inspect]
    Example.Extensions                                      [Inspect]

  Example ecosystem
    Curated packages (12)                 [Browse] [Add curated packages]
    Example.*                            Allowed on demand  [Query]
```

The editor exposes the independent traversal policy:

```text
Edit Workspace
  Subject: Example.Package
  Packages: Example.Package

  Allowed traversal
    Platform                     On demand                  [x]
    Microsoft.Extensions         Ecosystem, on demand       [x]
    [Add allowed traversal]
                                               [Cancel]     [Save]
```

After leaving editing, the prefix-only stress case remains usable in the
viewer with both defaults removed:

```text
Workspace                                                   [Edit]
  Packages: 0
  Allowed traversal
    Aspire.*                     Package prefix, on demand

  Call graph
    Subject: Aspire.*            Population: bounded
    [Run]
```

Keeping the defaults in the neighboring case must not add Platform or
Microsoft.Extensions as graph roots for the selected Aspire prefix.
Permission-based resolution beyond those roots still follows the operation's
declared semantics and bounds.

The editor distinguishes removing a registration from removing an admitted
package. It does not require package content merely to display or edit intent.
Avoid persistent application chrome for these choices; the Workspace is their
management surface.

Saving a valid intent-only Workspace must preserve its raw-prefix/ecosystem
choices and opt-outs without first acquiring packages. Saved intent and exact
resolved content remain different facts. Packet shape, versioning, legacy
interpretation, and projection belong to Workspace Definitions. Unsupported
projection fails visibly rather than dropping registrations or restoring a
fresh default Workspace. Existing saved-definition storage remains an opaque
consumer of that owner's packet.
An imported population is saved as selected content, not a rule to rerun a
prefix query. Restoration may reacquire recorded inputs without repeating the
discovery that originally selected them.

## Ownership and adoption

| Participating owner | Responsibility retained |
| --- | --- |
| [Workspace Scope](workspace-scope-and-expansion.md) | Registration/Root distinction, effective expansion eligibility, revisions, admission requests, and closure |
| [Ecosystem Packs](ecosystem-packs.md) | Static contribution discovery and selection; not source or scanner semantics |
| [Package Set Registry](package-set-registry.md) | Curated package-set identity and membership; not traversal permission or acquisition |
| [Package Sources](package-source-model.md), [Metadata resolution](type-forwarding-resolution.md) | Authorized candidate discovery and acquisition coordinates; actual type identity and forwarding evidence |
| [Queries](../architecture.md) | Operation-specific population, evaluation, bounds, and result coverage |
| [Navigation](inspection-subject-navigation.md) and its [Browser consumer](inspect-web-navigation-consumer.md) | Subject outcomes, installation, history, and focus |
| [Shell](inspect-web-shell-interaction.md), [Presentation](inspect-web-navigation-presentation.md) | Viewer/editor controls, package inventory, subject-strip composition, and layout |
| [Workspace Definitions](workspace-definitions.md) | Portable intent/content representation and restoration |
| [Artifact Acquisition](artifact-acquisition-and-workspaces.md) | Physical realization, binding, budgets, lifetime, and publication |

The generic Scope contract still treats an empty registration set as closed.
The Browser's explicit construction defaults do not change that invariant or
silently change CLI policy. Ecosystem selection, namespace-guided candidate
discovery, and query population are not retroactively claimed as implemented
by the current Scope or catalog primitives.

There are eight counted production-adoption stages, tracked by #6012. These
are delivery stages, not permission to combine component-internal designs:

1. Source and resolution owners supply supported traversal candidate handoffs,
   including the missing prefix intent under #5602 and the applicable exact
   dependency-candidate work under #5765. Namespace-guided discovery needs its
   own focused claim; it is not already supplied by either issue. Any additional
   discovery/acquisition binding, including Platform's, needs a focused
   source-owned contract before catalog adoption.
2. The catalog owner supplies the additional ecosystem knowledge under #5728,
   including Platform's ecosystem contribution, consuming source, package-set,
   and Integration-owned values rather than defining them.
3. Scope adopts the required registration/admission behavior under #5697,
   consuming those owner-issued inputs. This includes a focused revision of its
   current ordinary-Open empty-policy mapping before the Browser's explicit
   current-policy handoff can ship.
4. Query owners establish bounded forward and population-search operations,
   including one-time bounded import selection, the prefix-only non-hub graph,
   and population coverage.
5. Workspace Definitions supplies portable intent-only and opt-out restoration
   before Browser Save can advertise support for those Workspaces.
6. The CLI exposes the shared registered-domain and bounded-query capabilities
   and explicit curated/bounded input selection through its focused consumer
   work under #5513, without inheriting Browser construction defaults.
7. Browser owners adopt default construction, editor controls, deferred
   references, the four construction choices, explicit operation populations,
   and Workspace viewer inventory through focused changes. This includes
   revising Presentation's current management/catalog emphasis in coordination
   with its subject-strip work. Platform opening remains the independent #6013.
8. A separately authorized release and website deployment expose the complete
   supported Browser experience.

Existing acquisition, navigation, and query machinery should be reused.
There is no replacement framework to retire.
Current Browser membership migration remains owned by its existing Scope
adoption track rather than being declared complete by this specification.

## Acceptance and evidence

The following are required future outcome-level scenarios, not claims about
the current implementation:

| Scenario | Required observation |
| --- | --- |
| Construct a fresh Workspace from Home, Spotlight, or the empty editor | The two defaults are registered, with no registration-triggered acquisition or analysis |
| Open another package through Spotlight after unregistering either or both defaults | The existing Workspace explicitly supplies its current registrations; defaults do not reappear |
| Unregister Platform, navigate, save, and restore | The opt-out survives; existing content remains usable; a later reference cannot silently reenlist it |
| Allow an ecosystem, add its twelve curated packages, or allow only its prefix | Permission-only actions acquire nothing; adding the set selects its explicit members without granting wider permission |
| Allow a prefix and import top N, including already-present results | The visible bounded selection is imported without replacing prior members; N does not promise N new members |
| Remove an imported package, revisit the viewer, then save and restore | No automatic refill or query rerun occurs; the independent prefix permission remains available to later explicit demand |
| Construct a multi-package Workspace without a navigation target | The viewer shows its package inventory; choosing one package enters the existing hierarchy |
| Browse curated packages in a zero-package Workspace | Available entries are distinct from loaded content; listing catalog knowledge performs no package-content acquisition |
| Follow an approved framework reference | The applicable target resolves or fails visibly without changing to an unrelated platform |
| Resolve a type with a matching, missing, or overlapping ecosystem hint | Metadata remains authoritative; bounded misses and ambiguity remain honest |
| Run an Aspire-prefix graph with defaults on and off | Initial graph roots come from the selected prefix, not the default permissions |
| Start with no packages and only a prefix | A bounded non-hub graph can establish its own population |
| Put a connected component below the initial ranking cutoff | The first result reports limited population; a larger request can discover the component |
| Cancel, supersede, deny, or fail acquisition | Existing owner outcomes preserve the prior usable subject and visible failures |
| Save an intent-only Workspace or encounter an unsupported packet | Preserve complete supported intent, or fail visibly without substituting defaults |

The Browser Node/original-host and Firefox suites gate interaction outcomes.
Shared owner suites run in Release and gate candidate evidence, admission, and
query coverage; the CLI consumes the same results. Existing owner models retain
their concurrency responsibilities. Any new stateful interaction discovered
during owner adoption requires that owner's focused evidence before coding;
this document adds no competing lifecycle model.

Interactive Browser controls deliberately use the existing typed DOM
rendering/binding path rather than Markout. Shared graph and Integration
results retain their owners' structured models and format-lowering strategy.
No new broad renderer, dependency, or platform exception is introduced.
