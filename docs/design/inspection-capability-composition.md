# Inspection capability composition

## Status

This document is the normative design for **Inspection Capability
Composition**, tracked by
[#8417](https://github.com/richlander/dotnet-inspect/issues/8417).

The pattern is implemented for its first bounded production adoption. Package
Query now composes its existing document, executable Query Space, typed route,
and real CLI and Browser bindings without changing Package Query behavior.
Remaining modern owners adopt the same substrate one focused owner at a time.

[Registration, execution, and binding](#registration-execution-and-binding)
is the design for
[#10700](https://github.com/richlander/dotnet-inspect/issues/10700). It is
partly implemented:

- A route declares its [execution](#execution) kind. Diff's Compare operation
  is an inspection route, and `diff --analysis` executes every analysis set
  through its CLI binding.
- Diff's analyses are resources of the Compare route, composed and explained
  in the same graph. The CLI binding exposes them, and search admits them.
- Structural sections are still outside the graph.

The [adoption sequence](#adoption-sequence) counts the steps that close each
gap.

## Owner and exact claim

**Inspection Capability Composition** owns this exact claim:

> Given explicit owner-issued registrations for host-neutral inspection
> documents and routes and the query facets and analyses they expose, and for
> structural catalogs and their sections and categories, together with
> explicit host-owned bindings that select those resources, construct one
> deterministic resource-free capability graph.
> The graph preserves the direct typed dependency from each consumer to its
> producer, records how each registered resource executes, and derives the
> reverse relationships required for discovery, explanation, search
> admission, and production-adoption assessment.

This owner defines:

- the distinction among an inspection document, its section and query
  surfaces, a host-neutral route, and a consumer binding;
- the direction and authority of registration;
- the separation of registration, execution, and host binding, and the
  execution kinds a resource declares;
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

The top-level CLI gesture is:

```console
dotnet-inspect explain literal
```

The `explain` facade dispatches text that is not an exact registered path or
alias, reusable-reference-shaped, or a canonical multi-segment `ResourcePath`
to Capability Catalog Search. Selecting the returned canonical path dispatches
the next invocation to exact Resource Explanation. Resource Explanation itself
remains exact-path-only and never applies similarity after a resolution
failure.

Inspection Capability Composition supplies the searchable owner-issued
identity, canonical query key, name, summary, relationships, path, route, and
available consumer bindings. It does not define search-term projection,
similarity ranking, ordering, result Content, CLI spelling, or Browser
interaction. The search owner must reuse
`ILInspector.MetadataPrimitives.StringDistance.Similarity`, remain
deterministic, resource-free, and network-free, and must not recover
capability from parser help, rendered output, reflection, or source-code
names.

Resource Explanation remains exact-path-only. The `explain` facade composes
search and exact resolution as syntax-selected operations; it does not add
fuzzy, prefix, wildcard, or natural-language resolution to Resource
Explanation.

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

A route's surface profile may include a Query Space, but a route doesn't need
one. A route that executes through Query Space references its binding. A route
that executes its typed entry point directly, such as Diff's Compare
operation over an analysis set, references none. See
[execution](#execution).

### Consumer binding

A **consumer binding** is the route kind of [host binding](#host-binding). It
records one real dependency from a consumer to the exact host-neutral route it
uses. Operation analysis bindings and structural view bindings are the other
kinds; they select analyses and structural resources rather than a route.
Consumers include:

- a CLI command or command mode;
- a Browser/Wasm gesture or managed export;
- an operation-backed section; and
- another owner-approved product composition that invokes the route.

The binding retains consumer-owned lowering and exposure information. Ordered
input rules may describe positional arguments, option lowering, and paired
input requirements. They are declared by the consumer rather than inferred
from help or parser objects; an empty list means no rules are registered. The
binding cannot add section, query, effect, or result semantics outside the selected
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

## Registration, execution, and binding

A capability has three independent properties. Composition keeps them apart,
so that one never stands in for another.

| Property | Question it answers | Owner |
| --- | --- | --- |
| Registration | Does this capability exist, and what is it? | The resource's owner, through a host-neutral module |
| Execution | How does it run, and which effectiveness gates apply? | The route or operation owner |
| Host binding | Can this host run it, and through which gesture? | The host, through a host module |

Discovery depends on registration and binding only. Execution decides how a
resource runs and how it is gated. It never decides whether the resource is
discoverable.

### Registration

A registered resource is one of:

- an inspection document or a host-neutral route;
- a query facet of a route's Query Space;
- an analysis that takes part in an operation; or
- a structural section or category of a structural catalog.

Each registration carries the resource's stable owner-issued identity, its
name, and its purpose text where the owner issues one. It also names the route
or catalog the resource belongs to. Resource Explanation assigns each
registered resource exactly one canonical path. A host explains and searches
the same registered resources from one composed graph. It doesn't combine
per-kind catalogs or assemble Discovery projections, paths, or routes for
discovery.

Registration modules live in host-neutral assemblies. A host composes them; it
doesn't define them. The root's [routing declarations](#routing-declarations)
are registrations too; they move out of CLI code in adoption step 4.

### Execution

Each registered resource has one execution kind. A route declares the kind
for itself and its resources. A structural catalog's resources that no route
produces are host-executed.

| Execution kind | Meaning | Gates |
| --- | --- | --- |
| Query Space route | The route executes through its Query Space binding. | The Query Space gates for [terminal correctness and least-work execution](../evidence-and-validation.md#terminal-correctness-and-least-work-execution), plus the route gates |
| Inspection route | The route's typed entry point returns `InspectionEnvelope<TContent>` without a Query Space. | The route owner's gates |
| Host-executed | No host-neutral route produces the resource yet; only a host path does. | The host path's existing gates |

A structural section whose producing command has no host-neutral route
registers under its structural catalog with the host-executed kind. The graph
records that fact; it never presents a host-executed resource as a route.
When a host-neutral route later produces that section's document, the section
moves under the route, and its execution kind becomes the route's.

Joining Query Space, as Diff may through
[#10227](https://github.com/richlander/dotnet-inspect/issues/10227), changes
a route's execution kind and gates. It doesn't change which resources are
registered or how hosts bind them.

### Host binding

A host binding selects registered resources and names the host's own gesture:

| Binding | Selects | Example |
| --- | --- | --- |
| Route binding | A route, and the query facets it exposes | `package query` |
| Operation analysis binding | The analyses it exposes on its route, under the selection rule below | `diff --analysis` |
| Structural view binding | The sections and categories one structural view selects | `library -S`, `library address -S` |

A resource is **available** in a host when it is registered and at least one
of that host's bindings selects it. An available resource carries exactly the
bindings that select it. A section selected by two views is one registered
resource with two bindings.

An operation analysis binding selects an analysis only when, at one or more
of the report surfaces the analysis takes part in for that operation, the
analysis's report-surface support admits the question mode the operation
requests. Diff's Compare requests Targeted. The analysis owner,
[Inspection Analysis Requests](analysis-surfaces-and-universes.md#report-surface),
issues report-surface support and
[question mode](analysis-surfaces-and-universes.md#question-mode). The
operation owner declares the mode in its `AnalysisOperationDefinition`, and
set validation reads the same mode. Participation alone isn't enough.

An operation analysis binding is a route binding of a route that runs an
analysis operation. It lists the analyses it exposes, as a route binding lists
the query facets it exposes. Exposing an analysis that its route's operation
can't select, or exposing analyses on a route that runs no analysis
operation, prevents construction.

The owning route of an available resource is the route it belongs to. For a
host-executed structural resource, it is its structural catalog's root. For
an analysis, it is the routes of the operations whose bindings select it.

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
| Related operations issued from effective query facets | Route owner |
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

A route may associate one of its effective query facets with an owner-issued
related-operation affordance. The association is valid only for a facet in that
route's effective Query Space and is projected as a Resource Explanation
relationship. A host may bind the exact affordance identity to one of its own
gestures; facet keys, labels, examples, and result evidence are not binding
currency. The route does not issue a Browser lens, URL, command spelling, or
universal next-action ranking.

A route may also register host-neutral presentation facts for one effective
query facet: order, control and presentation kinds, execution class, selection
and replacement groups, combining behavior, inline options, and an external
option-source identity. These declarations are optional descriptive structure,
not execution authority. Every presentation source must belong to the route's
effective Query Space; source identities are unique; and inline option values
must exactly match the effective facet values. Resource Explanation projects
the declaration without creating a host-specific catalog. Consumers still own
rendering, interaction, and layout.

The non-generic registration projection contains only resource-free
descriptive structure. The typed route retains execution. Catalog enumeration
cannot acquire packages, open a Workspace, decode metadata, invoke a producer,
or inspect Content.

## Consumer registration

Every [host binding](#host-binding) has one stable binding identity, one
consumer kind and owner, a host-owned gesture, and the registered resources it
selects. It selects them only within one owning route or structural catalog.
An operation analysis binding names its operation's route. A structural view
binding names its structural catalog and view.

A consumer binding, the route kind, contains:

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

## Analysis participation registration

An analysis that takes part in an operation registers once, through a static
capability module. The registration contains:

- the owner-issued `AnalysisDescriptor`, with its stable analysis identity and
  per-operation participation declarations owned by
  [Inspection Analysis Requests](analysis-surfaces-and-universes.md#operation-participation);
- the typed producer binding that the operation dispatches for each declared
  report surface; and
- the route of the operation it takes part in, which declares that
  operation's `AnalysisOperationDefinition`.

The analysis is a registered resource of its operation's route. An operation
analysis binding is a [host binding](#host-binding) of that route that
exposes analyses under the [selection rule](#host-binding). Analysis
registrations and the
operation route are composed into the same graph as every other route. Hosts
don't compose them as a separate catalog.

The same registration serves discovery and dispatch. An operation must not
list one analysis set for explanation while dispatching another. Catalog
enumeration lists each analysis and the operations and surfaces it takes part
in, without executing a producer.

This registration does not require the operation to be a Query Space route.
It does not define analysis identity, participation, set validation, or result
semantics. Those remain with their owners.

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
5. Every query-facet related operation references one effective facet and is
   unique within that route.
6. Every query-facet presentation references one effective facet, is unique
   within that route, and preserves the effective facet's listed values.
7. Every host binding references one registered route or structural catalog,
   and every resource it selects is registered under that route or catalog.
8. A consumer cannot add a section, facet, analysis, effect, result grain, or
   completion meaning outside that route or catalog.
9. Equivalent top-level and operation-backed-section bindings reference the
   same route and compatible subject roles.
10. Every declared production-adoption requirement has the required binding
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
host-executed resource with its host bindings
```

The graph reports facts rather than assuming that every route requires every
host. A focused owner or overall tracker declares the intended production
binding kinds. Missing intended bindings are adoption gaps.

Legacy paths that do not return `InspectionEnvelope<TContent>` are not
wrapped as routes. The composition doesn't describe a legacy execution path as
a host-neutral route, or preserve one to make adoption look complete. An
owner-issued resource that such a path produces, such as a structural section
with a Section Model description, still registers as a resource with the
host-executed [execution kind](#execution). The adoption projection reports it
as host-executed, which is migration work and not success. Discovery describes
it and the gesture that runs it, and nothing more.

## Discovery and explanation projections

The composed graph is the common basis for the installed self-description
surfaces:

| Surface | Projection |
| --- | --- |
| `-D` | Effective structural sections and items for the selected route |
| `-Q` | Effective executable Query Space bindings for the selected route |
| Resource Explanation | Installed document, route, surface, related-operation, and binding resources plus their declared relationships |
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

### Routing declarations

Inspection Capability Composition issues the explanation root's operation
declarations for the capability families a host composes, under the
[Resource Explanation routing contract](resource-explanation.md#routing-declarations):

| Operation | Destination | Accepts |
| --- | --- | --- |
| `package-query` | The Package Query inspection document | `package` |
| `package-files` | The Package Files inspection document | `package` |
| `diff-analysis` | The Diff analysis inspection document and the `analyses` collection | The subject kinds the registered analyses' Compare participation names |

The Diff operation derives its accepted subject kinds from the registered
participation surfaces. In product order these are Library, Type, and
Member; Root and Workspace surfaces have no subject-kind declaration. Each
declaration also carries an owner-written purpose, representative questions,
grain, and scope, in `CapabilityRoutingDeclarations`.

Every host that composes a declaration's owner modules projects the same
declaration. A host that doesn't compose them admits no declaration for them,
under the [Resource Explanation root composition](resource-explanation.md#root-composition)
rule. Moving the declarations from `CapabilityRoutingDeclarations` in CLI code
into a host-neutral module is adoption step 4 below.

Every operation accepts Schema Query subject kinds. A host therefore admits
these declarations only together with
[Schema Query's](schema-query.md#routing-declarations); admitting them alone
names undeclared subject kinds and is an invalid registry.

## Failure and diagnostics

Capability composition is resource-free. Registration failure is a product
construction defect, not an inspection diagnostic. A production composition
root fails visibly when its selected modules are internally inconsistent.

An adoption census may report missing intended bindings as structured
non-success without preventing unrelated valid bindings from being explained.
It must distinguish:

- absent modern registration;
- invalid registration;
- valid route or registered resource with no current-host binding; and
- valid binding omitted from an intended production host.

It does not infer a missing capability from parser commands, rendered help,
source-code names, or neighboring hosts.

## Platform and safety boundary

The composed descriptor graph is required to be resource-free,
serialization-ready where exported, NativeAOT-compatible, and suitable for
single-threaded Browser/Wasm. It must contain no metadata readers, streams,
package payloads, Workspace leases, credentials, executable inspected code, or
inspected-assembly types.

The Package Query adoption keeps the descriptor graph resource-free and uses
the existing Browser production route. Its Release construction and Browser
binding gates verify that bounded adoption. NativeAOT and single-threaded Wasm
remain **unverified** until their existing product gates exercise this
substrate. Each adopting owner continues to inherit the platform contract of
its existing operation; this pattern does not certify an otherwise unsupported
operation for Browser/Wasm or NativeAOT.

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
  capability catalog;
- a registered resource that no binding of the current host selects is not
  available in that host, and the adoption projection reports it;
- one structural section selected by two views is one resource with two
  bindings;
- an analysis that takes part in an operation, but whose report-surface
  support admits the operation's question mode at none of its surfaces, is
  registered but not selected by that operation's binding;
- a host-executed resource is never presented as a host-neutral route; and
- a host binding that selects an unregistered resource, or a resource outside
  its owning route or structural catalog, prevents construction.

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

The first implementation uses
`dotnet run --project tests/DotnetInspect.Web.Tests -c Release` for the
Browser-managed binding and
`dotnet run --project tests/DotnetInspector.Sections.Tests -c Release` for
resource-free catalog construction. NativeAOT and single-threaded Wasm remain
**unverified** until their existing product gates exercise the new substrate.

The routing declarations' gate is the CLI root test
`ExplanationRoot_ReachesEveryResourceOfTheCompleteCatalog`, with the
bare-explain routing-table tests for the Diff analysis document destination.
Together they assert four things:

- the three operations route to their destinations;
- the Diff operation accepts Library, Type, and Member;
- the Package subject lists both package operations; and
- every resource of the complete catalog, including every Package Query,
  Package Files, and analysis resource, is reachable from the root.

Catalog construction accepts only caller-supplied modules and exposes no
assembly-scan or CLR-type-discovery entrypoint. Construction tests provide
partial gate coverage for that bounded API shape. This design makes no
repository-wide claim that unrelated components contain no reflection.

The [registration, execution, and binding](#registration-execution-and-binding)
adoption must add Release gates for:

- one composed graph containing Query Space routes, an inspection route with
  analyses, and host-executed structural catalogs;
- availability by registration plus binding, including an unbound resource,
  a resource with two bindings, and an analysis whose report-surface support
  doesn't admit the operation's question mode;
- each host's search and explanation root admitting exactly its available
  resources, with no host-assembled discovery input; and
- NativeAOT search cost no worse than the current structural population's
  measured cost class. Today that is 41 ms to read the six projections,
  against 75 ms to create and combine their explanation catalogs.

The inspection-route part of step 2 is gated by:

- the `InspectionRoute_*` composition tests: the execution kind, the rejection
  of a binding that exposes a query term on an inspection route, and the
  route's explanation without Query Space facts;
- the CLI test `DiffAnalysisRoute_ExplainsAsAnInspectionRouteBoundToDiff`,
  which explains the route and document from the complete CLI catalog;
- `AnalysisRepresentation_SharedTablesMatchDocumentsAndKeepFailureEvidence`,
  which shows each table projection equals its document view and keeps its
  diagnostics; and
- the Diff command tests that invoke `diff --analysis`, every accepted run of
  which goes through the CLI binding. The `BuildAnalysisTransitions` tests
  call the route's entry point directly and don't gate the binding.

The analyses-as-route-resources part of step 2 is gated by:

- `AnalysisOperation_RequestsTargetedQuestions` and
  `CompareRoute_OwnsTheAnalysesItsOperationDispatches`: the operation's
  requested mode, and the route's analyses;
- `AnalysisBinding_SelectsOnlyItsRoutesAnalyses`: construction rejects a
  binding that exposes an analysis outside its route, or exposes analyses on a
  route with no analysis operation;
- `AnalysisExplanation_NamesItsRouteBindingAndFindings`: an analysis explains
  its Finding titles, its route, and the bindings that expose it, and an
  unexposed analysis names no binding;
- the `CapabilityCatalogAnalysisSearchTests` and the CLI tests
  `Search_AdmitsEveryAnalysisDiffBinds` and `Phrase_RanksTheAnalysisFirst`:
  search admits exactly the exposed analyses, with their route and binding,
  and resolves each path through exact explanation.

The selection rule's question-mode clause is **unverified** by a dedicated
case. No registered analysis takes part in Compare without Targeted support,
and set validation accepts only a Targeted operation, so the case needs a
synthetic descriptor; it's added with the first operation or analysis that can
reach it.

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

[#10700](https://github.com/richlander/dotnet-inspect/issues/10700) extends
the sequence after step 6 with five steps for
[registration, execution, and binding](#registration-execution-and-binding),
numbered as in that issue:

1. Lock the registration, execution, and binding design. **Complete.**
2. Admit analyses through their operation's route, in two parts.
   - **Inspection route.** A route declares its execution kind, so it needs
     no Query Space. Diff's Compare operation registers as one inspection
     route over the Diff analysis document. The CLI binds it, and
     `diff --analysis` executes every analysis set through that binding.
     The Summary and Transitions tables that the CLI writes for tabular
     output are projections of the document's Summary and Transitions views,
     not separate entry points. **Complete.**
   - **Analyses as route resources.** The analyses become resources of the
     Compare route. `diff --analysis` binds as an operation analysis binding
     in the same composed graph, and search admits the available analyses.
     This part records the question mode Compare requests and completes the
     search part of
     [#10445](https://github.com/richlander/dotnet-inspect/issues/10445).
     **Complete.**
3. Register structural catalogs as host-executed resources, with one
   structural view binding per CLI view. Retire Capability Catalog Search's
   structural population family and its host-assembled entries.
4. Move the root's routing declarations from CLI code into a host-neutral
   module.
5. Let the Browser admit every resource its bindings select. Sections and
   analyses join Browser search when Browser binds them.

Each step lands with its production consumer. The CLI's search results stay
equal to today's except where a step intends a change.

Each adoption after the bounded first witness is a focused owner change.
Neither this design nor its first implementation sweeps every current command,
section, query catalog, or host route.
