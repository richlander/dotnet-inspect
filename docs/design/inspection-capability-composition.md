# Inspection capability composition

## Status

This document is the normative design for **Inspection Capability
Composition**, tracked by
[#8417](https://github.com/richlander/dotnet-inspect/issues/8417).

The pattern is proposed. Its first production adoption will compose the
existing Package Query document, Query Space route, and CLI and Browser
bindings without changing Package Query behavior.

## Owner and exact claim

**Inspection Capability Composition** owns this exact claim:

> Given explicit owner-issued definitions for modern host-neutral inspection
> documents and routes, and explicit consumer-owned bindings to those routes,
> construct one deterministic resource-free capability graph that preserves
> the direct typed dependency from each consumer to its producer and derives
> the reverse relationships required for discovery, explanation, and
> production-adoption assessment.

This owner defines:

- the distinction among an inspection document, its section and query
  surfaces, a host-neutral route, and a consumer binding;
- the direction and authority of registration;
- static capability modules and their composition boundary;
- construction-time graph validation;
- available-capability and production-adoption projections;
- the cold-materialization and allocation contract; and
- the handoff from the composed graph to structural discovery, query
  discovery, and Resource Explanation.

It does not define:

- inspection Content, Share, diagnostics, or serialization;
- section identity, projection, disclosure, cost, or shape;
- Query Space facets, operators, stages, effects, or executable binders;
- operation execution, acquisition, Workspace lifetime, or completion
  semantics;
- CLI grammar, Browser interaction, or presentation;
- product-resource paths or explanation traversal; or
- reusable inspection-reference identity or subject affordance semantics.

Those contracts remain with their existing owners. Adoption binds their
owner-issued values; it does not restate them.

## Product goal

The heart of modern dotnet-inspect is the host-neutral inspection document
handed to hosts through `InspectionEnvelope<TContent>`. The next layer exposes
owner-issued section and query surfaces over that document. The outer layer
binds production commands, Browser gestures, and operation-backed sections to
the host-neutral route that produces it:

```text
InspectionEnvelope<TContent> document
  <- section and Query Space surfaces
      <- host-neutral route
          <- CLI, Browser, or operation-backed-section binding
```

Today these relationships are present in several useful but separately
composed systems. Structural discovery, query-operation registration,
host-neutral inspection operations, CLI structural routing, output contracts,
and Resource Explanation each retain part of the graph. Their local contracts
work, but no owner-issued composition records that one real production binding
invokes one typed route, produces one document, and exposes one effective
section and query surface.

The missing composition makes installed capability harder for agents to learn
and makes incomplete host-neutral adoption harder for maintainers to see.
Resource Explanation cannot repair that gap by building another capability
inventory. It needs the same registrations that production consumers use.

## Production witness

The first adoption uses the existing Package Query
`library-literal=https://` scenario over
`Microsoft.Azure.SignalR@1.33.1` at `net8.0`:

```console
dotnet-inspect package query Microsoft.Azure.SignalR \
  --where "library-literal=https://" --tfm net8.0
```

[Package Query library-literal
composition](package-query-library-literal.md#production-adoption-and-evidence)
owns the
package facts, aggregate Library behavior, terminal
`InspectionEnvelope<PackageQueryDocument>`, and CLI and Browser production
demonstration. [Query Operation
Infrastructure](query-operation-infrastructure.md) owns the executable Package
Query route and its effective query capability.

This design consumes those existing facts to demonstrate:

- one owner-issued inspection document;
- one executable Query Space route;
- one CLI binding;
- one Browser binding; and
- one derived explanation and adoption graph.

The route and binding adoption does not alter Package Query planning,
acquisition, literal matching, evidence, result rows, completion, sharing, or
ordinary result presentation. The later self-description adoption
intentionally enriches query-discovery metadata with canonical resource links;
it does not change query execution or result Content.

## Agent discovery end to end

Naming the production witness is insufficient. The completed adoption must
demonstrate how an agent that knows the shallow shipped skill discovers
`library-literal`, verifies its meaning, and executes it without a
skill-maintained facet inventory.

### Command-local discovery path

The first required path uses the existing Package Query discovery gesture:

```console
dotnet-inspect package query -Q Packages
```

The effective Package Query registration supplies the `library-literal`
discovery row. That row and its surrounding summary must disclose:

- the canonical `library-literal` query key;
- the `--where` host gesture and admitted operator;
- the `decoded UTF-16 text` value domain and a copyable example;
- `metadata-expensive` execution and its exact-target-framework requirement.

The installed command already supplies those facts. After query-resource
adoption, the same compact row additionally emits its canonical Resource
Explanation path. Package-content acquisition and the route's other effects
remain typed explanation facts rather than new hand-maintained `-Q` prose.

The agent copies that emitted path into exact explanation:

```console
dotnet-inspect explain <library-literal-resource-path>
```

Explanation identifies the Package Query route, Package candidate
qualification role, decoded string-literal matching semantics, required target
context, acquisition and work consequences, result contract, and available
CLI and Browser bindings. It does not acquire a package or execute the query.

The agent can then construct the production request:

```console
dotnet-inspect package query Microsoft.Azure.SignalR \
  --where "library-literal=https://" --tfm net8.0 \
  -S "Literal Strings"
```

The result contains one semantic row per physical decoded `ldstr` occurrence
and retains the complete string as the Literal value. Two operand matches
inside one decoded string do not duplicate that occurrence. Equal complete
strings at two distinct IL coordinates remain two rows.

The installed production command currently returns rows including:

```text
Method Token  IL Offset  Literal
0x0600007A    IL_0008    please check if you set the AzureAuthorityHosts properly in your TokenCredentialOptions, see "https://learn.microsoft.com/en-us/dotnet/api/azure.identity.azureauthorityhosts".
0x060000EA    IL_0026    Endpoint scheme must be 'http://' or 'https://'
0x060002E4    IL_000C    please check if you set the AzureAuthorityHosts properly in your TokenCredentialOptions, see "https://learn.microsoft.com/en-us/dotnet/api/azure.identity.azureauthorityhosts".
```

The first and third excerpts retain equal complete strings at separate method
and IL coordinates. The middle excerpt retains surrounding text and both
scheme spellings instead of extracting only the matching URL fragment.
`CliLiteralStringQueryItemizesPhysicalUrlOccurrences` separately enforces that
a retained string containing more than one `https://` operand still produces
one physical-occurrence row.

This path proves that the installed descriptor set, rather than remembered
skill prose, teaches the agent the query key, context, cost, explanation
resource, and runnable gesture.

The adoption state is explicit:

| Step | Current state |
| --- | --- |
| `package query -Q Packages` discovers `library-literal` | Implemented |
| `-Q` emits the canonical query-resource path | Planned in #8417 |
| Exact `explain <path>` describes the facet and route | Planned Resource Explanation query adoption |
| The `Microsoft.Azure.SignalR` literal query emits whole-string rows | Implemented |
| Global similarity-ranked search for `literal` finds the facet | Planned in #8424 |

### Global catalog orientation

The command-local path assumes the shallow skill has oriented the agent to
Package Query. An unfamiliar capability should also be discoverable without
enumerating every command and running `-Q` repeatedly.

[#8424](https://github.com/richlander/dotnet-inspect/issues/8424) owns a
focused compact capability-catalog search. A similarity-ranked search for
`literal` consumes this composition's stable installed facts and should
return at least:

```text
Kind: Query facet
Key: library-literal
Route: Package Query
Explain: <canonical resource path>
Discover: package query -Q Packages
```

Inspection Capability Composition supplies the searchable owner-issued
identity, canonical query key, name, summary, relationships, path, route, and
available consumer bindings. It does not define search-term projection,
similarity ranking, ordering, result Content, CLI spelling, or Browser
interaction. The search owner must reuse
`ILInspector.MetadataPrimitives.StringDistance.Similarity`, remain
deterministic, resource-free, and network-free, and must not recover
capability from parser help, rendered output, reflection, or source-code
names.

Resource Explanation remains exact-path-only. Catalog search orients the user
to an exact resource; `explain` then resolves that resource without adding
fuzzy, prefix, wildcard, or natural-language resolution.

## Basis and authority map

| Owner | Contract consumed by this composition |
| --- | --- |
| [Inspection Envelope](inspection-envelope.md) | The completed host-neutral `InspectionEnvelope<TContent>` boundary and its owner-issued Content, Share, and diagnostics |
| [Host-observable Content Kinds](host-observable-content-kinds.md) | Result, Document, and owner-specific Outcome meaning |
| [Section Model](section-model.md) and [Schema Query](schema-query.md) | Section identity and structural `DiscoveryDocument` resources |
| [Query Space Composition](query-space-composition.md) | Complete resource-free query-space descriptors and explicit operation and row bindings |
| [Query Operation Infrastructure](query-operation-infrastructure.md) | Typed operation definitions, route bindings, effective profiles, and executable plans |
| [Inspection Operation Composition](inspection-operation-composition.md) | Shared semantic execution across distinct hosts |
| [Operation Commands and Subject Sections](operation-command-and-subject-section-composition.md) | Top-level operation and operation-backed section equivalence |
| [Resource Explanation](resource-explanation.md) | Exact product-resource paths and bounded explanation Documents |
| [Contextual Resource Explanation](contextual-resource-explanation.md) | Command-local and exact-subject explanation dispatch |
| CLI and Browser focused owners | Host gesture lowering and presentation |

Inspection Capability Composition supplies no universal document, section,
query, operation, or host model. It defines only how those owner-issued
contracts register and join.

## Analogous patterns and deliberate differences

The design follows repository precedent first:

- `QueryOperationRoute<TPredicate, TPlan>` preserves typed execution while
  exposing non-generic route metadata.
- `QueryOperationRegistry` validates explicit route registration without
  discovering CLR types.
- `ResourceExplanationCatalog` constructs a deterministic navigable graph from
  explicit owner resources and relationships.
- source-generated `System.Text.Json` contexts provide AOT-compatible explicit
  metadata instead of runtime contract discovery.

ASP.NET Core endpoint metadata and GraphQL introspection provide secondary
analogies: registered operations carry inspectable metadata, and a later
projection can enumerate relationships. This design is deliberately narrower.
It does not make the capability graph an execution dispatcher, remote query
language, or reflection-discovered endpoint inventory.

## Terminology

### Inspection document

An **inspection document** is one owner-issued `TContent` returned at a
completed host-neutral boundary:

```text
InspectionEnvelope<TContent>
```

`TContent` remains the Result, Document, or owner-specific Outcome defined by
its owner. An inspection document is not a Resource Explanation product
resource and does not become a universal content union.

### Surface

A **surface** is the owner-issued structural and query capability applicable
to one document or route. It is composed from references to:

- structural section descriptors and declared row sets;
- operation and row Query Space bindings;
- result-contract identities; and
- owner-issued profiles or presets.

A surface does not copy section or query inventories. A displayed field does
not become a facet, and a query facet does not become applicable merely because
one section renders similarly named data.

### Host-neutral route

A **host-neutral route** is one typed way to produce an inspection document.
It retains:

- one stable owner-issued identity;
- admitted input or subject roles and their cardinality;
- one owner-issued output document contract;
- the applicable surface profile;
- owner-issued acquisition, work, effect, and completion references; and
- the typed execution entry point.

A route identity is independent of CLI command text, Browser control labels,
section names, and Resource Explanation paths.

### Consumer binding

A **consumer binding** records one real dependency from a consumer to the exact
host-neutral route it uses. Consumers include:

- a CLI command or command mode;
- a Browser/Wasm gesture or managed export;
- an operation-backed section; and
- another owner-approved product composition that invokes the route.

The binding retains consumer-owned lowering and exposure information. It
cannot add section, query, effect, or result semantics outside the selected
owner-issued route profile.

### Capability module

A **capability module** is one statically enumerable owner contribution. A
product module contributes document, route, surface, or product-composition
registrations. A host module contributes its real consumer bindings.

The product composition root knows installed modules because it already has
compile-time dependencies on their assemblies. It does not enumerate every
section, facet, route, or command individually.

### Capability catalog

The **capability catalog** is the immutable validated graph constructed from
the selected product and consumer modules. It is a derived index, not another
semantic authority and not an execution service locator.

## Authority and knowledge direction

Definitions are producer-owned. Bindings are consumer-owned. Registration
follows the real compile-time dependency direction:

```text
consumer binding
  -> exact host-neutral route
      -> exact inspection document
          <- applicable section and query bindings
```

The producer does not enumerate or name its consumers. A Package Query
document does not know about CLI or Browser. The CLI and Browser bindings each
know the exact Package Query route they invoke. Catalog composition derives
the reverse relationship:

```text
Package Query document
  <- Package Query route
      <- CLI Package Query
      <- Browser Package Query
```

This direction preserves ordinary typed execution. Registration does not
replace direct calls with string lookup, `object` requests, or dynamic
dispatch.

Each semantic fact has one owner:

| Fact | Owner |
| --- | --- |
| Content type, result identity, schema, Share, and diagnostics | Inspection document owner |
| Typed execution, roles, result, effects, and completion | Route or operation owner |
| Section projection, shape, cost, and row sets | Section owner |
| Facets, operators, stages, orders, and binders | Query Space owner |
| Effective section and query subset | Route profile owner |
| CLI tokens and lowering | CLI binding owner |
| Browser gesture and lowering | Browser binding owner |
| Reverse callers, available paths, and adoption state | Derived capability catalog |
| Canonical product-resource paths and explanation traversal | Resource Explanation |

A consumer references these values. It does not reproduce their labels,
identities, or inventories.

## Producer registration

One modern document-producing route exposes enough owner-issued structure to
compose without executing it:

- stable semantic identity and concise description;
- typed input roles, admitted subject kinds, cardinality, and required context;
- exact output document descriptor;
- effective structural and query profiles;
- effect, work-bound, and completion references; and
- typed execution returning `InspectionEnvelope<TContent>`.

Input roles describe semantic participation rather than command positions.
Examples include root, before, after, seed, peer, candidate population, and
already resolved subject. A role distinguishes one Package used as a Depends
root from one Package used as a Diff endpoint.

The route references effective Query Space bindings. It does not expose a
second string list of supported facet names. The document descriptor similarly
references owner-issued section and result contracts instead of recreating
their schemas.

The non-generic registration projection contains only resource-free
descriptive structure. The typed route retains execution. Catalog enumeration
cannot acquire packages, open a Workspace, decode metadata, invoke a producer,
or inspect Content.

## Consumer registration

A consumer binding contains:

- one stable binding identity;
- one consumer kind and owner;
- a direct reference to the exact route;
- the route profile or owner-issued preset it exposes;
- host-owned gesture or product-composition identity; and
- lowering and presentation references owned by that consumer.

The same binding used for catalog registration participates in production
execution. A modern command must not register one route for explanation while
calling another operation directly. An operation-backed section similarly
binds the same route used by its top-level equivalent when their subject
bindings are semantically equivalent.

Host bindings may expose fewer controls than the complete route. Omission
means that the host does not expose that capability. It does not authorize a
host-local facet, alternate execution path, or changed result contract.

## Static modules and cold materialization

Registration is explicit and statically enumerable. Reflection, assembly
scanning, parser-help scraping, rendered-output parsing, display-name
normalization, and delegate-target inspection are not registration
mechanisms.

A module exposes an immutable static registration sequence. A registration may
use a cached non-capturing provider to return cached static descriptor content.
Invoking that provider does not construct the producer or execute work.

The allocation contract has three paths:

| Path | Contract |
| --- | --- |
| Ordinary execution | The consumer invokes its typed route directly. No aggregate capability catalog is constructed or enumerated. |
| `-D` or `-Q` | The host reads the cached effective descriptor for the active route. No subject acquisition or producer execution occurs. |
| `--explain` or adoption census | The host may construct and index the required capability graph. These are explicit cold operations. |

Static registration arrays and cached providers may allocate once when their
own discovery module is first initialized. Ordinary execution must not
initialize unrelated discovery modules merely because it invokes a route.
Implementations may use nested static holders, spans over static arrays, or
equivalent AOT-compatible mechanisms to preserve that boundary.

This design does not require unsafe function pointers, runtime code
generation, or a source generator. A later implementation may introduce
generated static tables only if measured scale demonstrates a need and the
generated form preserves the same explicit owner registrations.

## Catalog construction

Catalog construction receives explicit product modules and explicit consumer
modules. It does not search the process for additional registrations.

Construction validates:

1. Document, route, binding, and module identities are unique in their
   owner-defined scopes.
2. Every route references one registered document contract with the same typed
   `TContent`.
3. Every structural or query reference belongs to the route's owner-issued
   effective profile.
4. Every query capability has the executable binding required by Query Space.
5. Every consumer binding references one registered route.
6. A consumer cannot add a section, facet, effect, result grain, or completion
   meaning outside that route.
7. Equivalent top-level and operation-backed-section bindings reference the
   same route and compatible subject roles.
8. Every declared production-adoption requirement has the required binding
   kind or produces one explicit adoption gap.

Duplicate, missing, incompatible, or out-of-profile registration prevents
construction. The catalog does not silently omit a broken binding or produce
an empty capability in its place.

Direct object references establish composition inside the process. Stable
owner-issued identities support serialization, diagnostics, deterministic
ordering, and Resource Explanation projection. Display text is never join
currency.

## Available capability and adoption state

The catalog exposes two distinct projections.

### Available capability

An available capability has a real binding in the current production host.
Only available capabilities are advertised as executable command or subject
affordances.

A host does not advertise a document merely because a host-neutral type or
operation exists in an installed assembly.

### Production adoption

The adoption projection reports the relationship among modern documents,
routes, and production bindings:

```text
document with no route
route with no production binding
route with CLI binding only
route with Browser binding only
route with CLI and Browser bindings
```

The graph reports facts rather than assuming that every route requires every
host. A focused owner or overall tracker declares the intended production
binding kinds. Missing intended bindings are adoption gaps.

Legacy paths that do not return `InspectionEnvelope<TContent>` remain outside
the capability graph. The composition does not describe, wrap, or preserve
them solely to make discovery appear complete. Their absence identifies
migration work; it is not a success-shaped compatibility adapter.

## Discovery and explanation projections

The composed graph is the common basis for the installed self-description
surfaces:

| Surface | Projection |
| --- | --- |
| `-D` | Effective structural sections and items for the selected route |
| `-Q` | Effective executable Query Space bindings for the selected route |
| Resource Explanation | Installed document, route, surface, and binding resources plus their declared relationships |
| Command-local `--explain` | Contextual Resource Explanation selection of the command resource or exact-subject affordance issued by its owners |
| Subject-affordance explanation | Applicable registered routes whose roles admit the resolved subject, when issued by the reusable-reference owner |

`-D` and `-Q` remain compact discovery. Resource Explanation retains canonical
paths and bounded graph traversal. Contextual Resource Explanation retains
command and exact-subject dispatch. Inspection Capability Composition supplies
the coherent registered relationships those owners may adopt and project. It
does not assign another meaning or result contract to `--explain`.

The projections preserve three levels:

1. **Declared document surface:** owner-issued sections, row sets, and query
   spaces.
2. **Effective route surface:** the owner-issued profile applicable to one
   route and role.
3. **Exposed consumer surface:** the subset available through one CLI, Browser,
   or operation-backed-section binding.

An explanation must identify which level it describes. It cannot present a
document capability omitted by the current binding as though the user can
invoke it through that host.

## Failure and diagnostics

Capability composition is resource-free. Registration failure is a product
construction defect, not an inspection diagnostic. A production composition
root fails visibly when its selected modules are internally inconsistent.

An adoption census may report missing intended bindings as structured
non-success without preventing unrelated valid bindings from being explained.
It must distinguish:

- absent modern registration;
- invalid registration;
- valid route with no current-host binding; and
- valid binding omitted from an intended production host.

It does not infer a missing capability from parser commands, rendered help,
source-code names, or neighboring hosts.

## Platform and safety boundary

The composed descriptor graph is required to be resource-free,
serialization-ready where exported, NativeAOT-compatible, and suitable for
single-threaded Browser/Wasm. It must contain no metadata readers, streams,
package payloads, Workspace leases, credentials, executable inspected code, or
inspected-assembly types.

These are target implementation properties, not evidence about the current
design-only slice. They are **unverified** until the substrate and first-adopter
Release gates named below exist and pass. Each adopting owner continues to
inherit the platform contract of its existing operation; this pattern does not
certify an otherwise unsupported operation for Browser/Wasm or NativeAOT.

All artifact-authored descriptive text consumed by an adopted descriptor
retains the containment contract of its owner. Capability composition does not
introduce another text-sanitization or trust boundary.

## Pathological cases

The implementation must preserve these cases:

- one document has a valid route but no production binding;
- one route intentionally has only a CLI binding;
- one route has equivalent CLI and Browser bindings;
- two bindings expose different owner-approved subsets of the same route;
- an operation-backed section and top-level command bind the same operation
  through different subject roles;
- a consumer attempts to advertise a facet outside the route profile;
- a binding references a document with the wrong `TContent`;
- two modules register the same stable identity;
- a descriptor provider is enumerated without executing its producer; and
- an unrelated ordinary command runs without initializing the aggregate
  capability catalog.

These are construction and composition fixtures. They do not require hostile
in-process caller defenses beyond the public registration contract.

## Evidence and gates

The first implementation adoption must provide Release gates for:

- deterministic graph construction from explicitly ordered modules;
- duplicate, missing, incompatible, and out-of-profile rejection;
- graph construction from resource-free descriptors without invoking
  acquisition, Workspace, metadata, or producer execution;
- the real CLI command handler and Browser managed export executing through
  their registered binding objects and the same typed Package Query route;
- Package Query structural and query projection without acquisition;
- Resource Explanation projection from the composed graph;
- an agent acceptance flow from Package Query `-Q`, through exact Resource
  Explanation, to the real `library-literal=https://` CLI execution;
- explicit orphan-route and missing-intended-binding adoption outcomes; and
- ordinary Package Query execution without aggregate catalog construction.

The existing `Microsoft.Azure.SignalR@1.33.1` production scenario remains the
behavioral witness. Synthetic registrations isolate construction failures and
cold-materialization behavior. The harness must use product registration and
composition APIs; it must not manufacture a repaired graph that production
construction cannot create.

Catalog inspection alone cannot prove production use. The first adopter's
Release gates must execute the real CLI and Browser entrypoints through the
binding-owned typed execution path. A descriptor registered beside a handler
or export that directly bypasses that binding does not satisfy adoption.

The first implementation must also name the exact Browser/Wasm and NativeAOT
gates that exercise the new substrate before claiming those target properties.
They remain **unverified** in this design slice.

The design requires catalog construction to accept only explicit modules and
to expose no assembly-scan or CLR-type-discovery entrypoint. That
implementation property is also **unverified** until #8417 records its
absence-claim coverage and the corresponding gate. This design makes no
repository-wide claim that unrelated components contain no reflection.

## Non-claims

This design does not:

- create one universal executable plan or request type;
- make the catalog an inversion-of-control container;
- require every document to support sections or query facets;
- require every route to appear in both hosts;
- make every section an operation or every field a query facet;
- expose legacy command-local behavior as modern capability;
- specify command names, aliases, default sections, or Browser layout;
- define reusable subject-reference syntax;
- replace Resource Explanation paths with route identities; or
- require eager global catalog construction.

## Adoption sequence

[#8417](https://github.com/richlander/dotnet-inspect/issues/8417) owns this
counted path:

1. Lock this focused composition pattern.
2. Implement static document, route, binding, module, and cold catalog
   composition.
3. Adopt Package Query as the first complete witness using its existing CLI
   and Browser production callers and Query Space route.
4. Project the adopted graph through Resource Explanation, derive the
   applicable `-D` and `-Q` views, and supply the structured input consumed by
   the focused catalog-search adoption in #8424.
5. Adopt one operation-backed section route to prove subject-section and
   top-level-operation composition.
6. Migrate remaining modern owners one focused owner at a time and retire
   superseded CLI-local capability inventories.

Each adoption after the bounded first witness is a focused owner change.
Neither this design nor its first implementation sweeps every current command,
section, query catalog, or host route.
