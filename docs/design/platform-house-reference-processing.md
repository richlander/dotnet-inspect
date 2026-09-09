# PlatformHouse realization and reference processing

## Status and approved scope

This document is the normative owner for the host-neutral `PlatformHouse`
composition boundary. It is tracked by
[#6301](https://github.com/richlander/dotnet-inspect/issues/6301) and is a
focused prerequisite of the platform-first tracker
[#6228](https://github.com/richlander/dotnet-inspect/issues/6228).
The documentation-source extension is tracked by
[#6375](https://github.com/richlander/dotnet-inspect/issues/6375).

The design depends on:

- [Platform Target Currency](platform-target-currency.md) for exact runtime
  and ASP.NET Core family targets;
- [Platform Library Population Declaration](platform-library-population-declaration.md)
  for target-independent Workspace relevance;
- [Package dependency evidence](package-dependency-evidence.md) for typed
  package references and their source provenance;
- [Platform/package pruning](platform-package-pruning.md) for exact-target
  package subsumption facts retained by the package owner;
- [Platform package supply policy](platform-package-supply-policy.md) for the
  typed upstream delegation that a package-processing owner may issue after
  consuming exact-target pruning facts;
- [Structured type-forwarding resolution](type-forwarding-resolution.md) for
  Metadata-owned forwarding and binding outcomes;
- [Platform composition and overlays](platform-composition-and-overlays.md)
  for source-specific coherent implementation realization;
- the
  [Assembly Reference Resolution Ladder](assembly-reference-resolution-ladder.md)
  for the ordering and lifetime of an applicable-platform rung;
- [SourceLink exposure](../sourcelink-exposure.md) for product SourceLink
  surfaces and explicit network policy; and
- [PDB acquisition](../pdb-acquisition.md) for matching-PDB acquisition and
  checksum-verified source-document settlement.

The approved scope is:

- `PlatformHouse` is the sole product-facing platform realization and
  reference-processing facade;
- typed package references enter `PackageHouse`, which alone applies the
  pruning service when deciding whether to continue package processing or
  issue a typed platform delegation;
- package, platform, and direct-library flows converge only after their owning
  source has produced a provenance-retaining bare-library value;
- platform XML documentation and SourceLink-backed documentation evidence are
  settled against the House's reference and implementation views;
- .NET Standard remains transparent compatibility processing rather than a
  `PlatformFamily`, registration population, or implementation target; and
- Metadata type forwarding connects reference contracts to definitions under
  a platform-authorized binding context.

The first production consumer is the applicable-platform rung of the assembly
reference resolution ladder. PackageHouse delegation, Queries, Workspace
composition, CLI platform operations, and Inspect Web adopt the same House
contract in separately reviewed slices.

This is one owner claim. Documentation evidence is another facet of the same
exact-target, reference-to-implementation settlement rather than a second
platform architecture. The design specifies the House request, settlement,
result, evidence-retention, and encapsulation contracts. It consumes the
owner-issued inputs listed above without redefining their identities,
algorithms, lifetimes, or failure semantics.

The PackageHouse and direct-library paths below form a thin composition map,
not additional normative owners inside this document. Their focused designs
own their complete requests, outcomes, and receipts. This document owns only
the typed boundary into PlatformHouse and the evidence its library-focused
result must preserve at the shared Library inspection handoff.

## Authority and exact claim

**PlatformHouse Realization and Reference Processing** owns:

> Given one typed platform target demand, one host-authorized platform source
> plan, one closed platform operation, and finite operation work, settle the
> demand to one exact `PlatformFamilyTarget`, then compose
> source candidates and owner-issued platform facts into one typed settlement
> that preserves the request, any selected exact target, selection evidence,
> source, reference-contract, implementation-supplier, forwarding,
> documentation provenance, completion, and failure evidence.

It owns:

- the product-facing `PlatformHouse` facade;
- common request context shared by platform operations;
- composition of explicit target demands with owner-issued version-selection
  results into one exact `PlatformFamilyTarget`;
- closed realization, assembly-reference, type-resolution, and documentation
  operation shapes;
- host-authorized source-capability composition;
- candidate validation, comparison, selection, and settlement;
- the distinction between reference and implementation demand;
- reference-to-implementation correspondence between settled platform views;
- platform-specific invocation of Metadata forwarding;
- provenance-retaining bare-library results for one-library demand;
- acceptance of ordinary platform requests issued from orchestration-owned
  typed delegations without interpreting the package reference that caused
  them;
- target-bound settlement of compiled XML and SourceLink-backed documentation
  evidence without collapsing their provenance;
- the House result envelope and settlement receipt;
- visible unavailable, ambiguous, rejected, and incomplete outcomes; and
- the rule that product reference processing does not compose lower platform
  mechanisms outside the House.

It does not own:

- platform family, target-framework, or version identity;
- target-independent ecosystem registration;
- package identity, package source authority, package dependency evidence,
  package candidate selection, package version resolution, package payload
  acquisition, or package asset selection;
- package-reference processing, pruning-service invocation, prune inventory
  construction, supplied-version comparison, or staleness;
- package-to-library or package-to-platform-library correspondence;
- framework compatibility, runtime roll-forward, or source-specific version
  inventory and ranking algorithms;
- assembly identity, declaration probing, binding selection, forwarding hops,
  or Metadata resolution outcomes;
- installed-hive discovery, manifest-defined implementation closure,
  reference-pack layout, package-pack acquisition, Browser catalog generation,
  or source-specific cache policy;
- assembly-reference rung ordering or package-route reachability;
- Workspace admission, revisions, replacement, leases, or participant
  lifetime;
- call-graph traversal, search ranking, section selection, rendering, or host
  presentation;
- XML-documentation identity, grammar, parsing, or textual normalization;
- Portable PDB identity, SourceLink mapping, source acquisition, checksum
  verification, or source-comment parsing; or
- package-specific documentation-source selection.

## Why a House is needed

Current platform behavior is divided across mechanisms with different
coordinates and implicit policy:

- `PlatformResolver` combines ambient installed discovery, reference and
  implementation lookup, framework aliases, version selection, assembly
  probing, type lookup, and .NET Standard handling;
- `PlatformPackService` combines pack-name mapping, package acquisition,
  cache layout, source policy, and assembly-name bias;
- `PlatformTypeCatalog` derives a reference-pack index and applies
  type-selection heuristics;
- Queries currently map platform coordinates directly to representative
  implementation-pack packages; and
- CLI and Browser paths compose these mechanisms differently.

These are useful implementations or owner facts, but none is the product
boundary. Direct composition makes the caller responsible for distinctions it
cannot safely reconstruct:

- which exact platform target the evidence describes;
- whether the request needs reference contracts, implementation bodies, or
  both;
- whether a .NET Standard facade resolved to an implementation definition;
- whether a source result is complete, projected, stale, unavailable, or
  unauthorized; and
- whether several source results correspond to one target and may be combined;
- whether documentation came from the reference contract or authored source;
  and
- whether a missing XML file may authorize PDB or SourceLink acquisition.

`PlatformHouse` centralizes those decisions without centralizing every
algorithm. It is a clearing house over focused owners, not a renamed
`DotnetInspector.Services` bucket.

## Vocabulary

| Term | Meaning |
| --- | --- |
| **House target** | One exact owner-issued `PlatformFamilyTarget` against which the operation is settled. |
| **Target demand** | Either one exact `PlatformFamilyTarget` or one typed family/TFM/version-selection request with explicit host policy and authorized target-discovery capabilities. |
| **Source plan** | An immutable host-authorized set of platform source capabilities and their explicit selection policy. |
| **Source capability** | A bounded adapter entry point for one source-specific operation. It is not source authority by display name. |
| **Source contribution** | One source's typed candidate, non-match, failure, or incomplete evidence, retaining exact target correspondence. |
| **Platform delegation** | An orchestration handoff produced from an upstream package-processing result. It authorizes one ordinary platform request for an exact family target while retaining the original package and pruning evidence outside this House. It does not identify a platform assembly. |
| **Bare library** | One logical library inspection subject, backed by one or more acquired assembly views, detached from package or platform container semantics while retaining content lifetime, physical identity, origin provenance, exact platform target when applicable, and each view's reference or implementation role. |
| **View demand** | Whether the consumer requires reference contracts, implementation bodies, or both. |
| **Population demand** | Whether the consumer requires one exact library or one complete family population. |
| **Reference contract** | A reference assembly or facade used to describe an API contract. It does not prove an implementation supplier. |
| **Implementation supplier** | The exact physical assembly candidate containing the implementation definition or body. |
| **View correspondence** | House-owned evidence connecting one resolved reference definition to one authorized implementation-resolution start without pretending that the transition is a Metadata forwarding hop. |
| **Documentation demand** | Whether an operation requests compiled XML documentation, source-derived documentation, or both. It is independent of reference/implementation body demand. |
| **Compiled XML documentation** | Structured documentation selected by XML-documentation identity from a companion artifact associated with the settled reference contribution. |
| **Source-derived documentation** | Documentation comments extracted from checksum-verified PDB-mapped source associated with the settled implementation supplier. It retains its weaker declaration-correspondence evidence. |
| **Settlement** | The House decision that selects, composes, or declines source contributions under the request's policy. |
| **Settlement receipt** | Resource-free evidence binding the request, target, source-plan generation, selected contributions, owner results, completion, and work. |

The vocabulary deliberately does not call `.NET Standard` a platform family.
Its reference contract may participate in one operation while the House target
remains an exact .NET runtime or ASP.NET Core family target.

## Contract shape

```text
typed target demand
  + one closed operation
  + host-authorized source plan
  + finite work and cancellation
        |
        v
PlatformHouse
  1. validate request and selection policy
  2. settle one exact PlatformFamilyTarget
  3. ask only authorized source capabilities
  4. retain every owner-issued contribution
  5. compose Metadata only when the operation requires it
  6. compose documentation owners only when documentation is requested
  7. settle the requested view, population, and documentation demand
  8. unwrap one-library success to a provenance-retaining bare library
        |
        v
one closed House outcome
  - completed operation result + settlement receipt
  - unavailable
  - ambiguous
  - rejected
  - incomplete
```

The House performs no ambient source discovery. A desktop host may authorize
installed, cached, and network-backed capabilities; a Browser/Wasm host may
authorize generated, embedded, or network-backed capabilities. The House sees
only the supplied plan and never widens it because another source is locally
reachable.

### Common request context

Every operation carries:

- one typed target demand;
- one immutable source-plan identity and policy generation;
- the exact Workspace revision or standalone operation identity;
- the typed platform-request origin, including an orchestration-owned
  delegation identity when one caused the operation;
- the requested reference and implementation views;
- one-library or whole-population demand;
- requested documentation channels and source-access authorization;
- finite source, candidate, assembly, XML, PDB, source-document, byte,
  forwarding-hop, and deadline budgets;
- owner-issued prerequisite correspondence; and
- caller cancellation.

The exact numeric defaults remain host policy. The House owns validation,
checked charging, and the rule that a broad source plan or population demand
is never unbounded work.

### Target and version settlement

An exact target demand carries an existing `PlatformFamilyTarget` unchanged.
A selecting target demand carries:

- one `PlatformFamily`;
- one target framework;
- one explicit version requirement or named host-default selection policy;
- the authorized target-discovery capabilities; and
- finite discovery and comparison work.

The House asks only those capabilities for owner-issued exact target
candidates and composes the version-selection owner's result. It does not
parse version syntax, define compatibility or roll-forward, enumerate ambient
installations, or rank candidates by an unstated "latest" rule.

Successful target settlement freezes one exact `PlatformFamilyTarget` before
library acquisition, view selection, or Metadata work begins. Every outcome
retains the original target demand and any candidate, policy, or failure
evidence used to settle it. An omitted CLI version is therefore not an
implicit target inside the House: the command supplies a named default policy
that remains visible in the request and receipt.

### Closed operations

The version-1 facade has four conceptual operation kinds. Exact public type and
member names may change during implementation, but the distinctions may not be
collapsed into strings, optional parameters, or nullable tuples.

| Operation | Input unique to the operation | Completed value |
| --- | --- | --- |
| **Realize** | One library identity or complete population demand | One bare library or one platform population whose library members retain source and view correspondence |
| **Resolve assembly reference** | One exact Metadata `AssemblyBindingRequest` and platform-route prerequisites | Metadata-owned binding decision plus the platform contribution used by the ladder |
| **Resolve type definition** | One exact Metadata `TypeResolutionRequest`, starting reference candidate, and required view | Metadata-owned `TypeResolutionOutcome` plus reference/implementation correspondence |
| **Resolve documentation evidence** | One exact type or member subject, settled reference evidence, requested documentation channels, and implementation correspondence when source-derived documentation is requested | Independent compiled-XML and source-derived documentation attempts with retained provenance |

`Realize` supports direct platform browsing and supplies source candidates for
the other operations. The two reference-resolution operations are the only
product-facing paths that may turn platform catalogs, .NET Standard reference
contracts, or platform-specific Metadata policy into a binding or
implementation result.
`Resolve documentation evidence` is the only product-facing platform path that
may combine reference-pack XML with implementation PDB/SourceLink evidence.
It consumes the existing XML-documentation, PDB, SourceLink, and source-text
owners rather than reimplementing them.

### View and population demand

View demand is closed:

- **Reference** requires contract assemblies suitable for API and Metadata
  inspection;
- **Implementation** requires assemblies that can supply implementation bodies;
  and
- **ReferenceAndImplementation** requires both views and explicit
  correspondence between them.

Population demand is also closed:

- **Library** names one exact requested assembly identity or one owner-issued
  platform-library identity; and
- **Population** requests the complete applicable family population and any
  source-owned support closure.

One-library demand does not authorize whole-pack indexing, whole-platform
realization, or population completeness work unless the selected source's
atomic realization unit requires it. When a source must realize a larger
coherent unit, the result says which unit was realized and why; the House does
not relabel that cost as a one-file operation.

Reference success does not satisfy implementation demand. A reference-only
source can complete API inspection while implementation-body or call-graph
requests remain visibly unavailable.

## Source plans and contributions

### Host authorization is explicit

A source plan identifies each capability the host permits for the exact
operation and the policy for considering it. It may authorize:

- installed or package-backed target discovery;
- installed reference packs;
- installed implementation-platform realization;
- package-backed reference packs;
- package-backed implementation packs;
- a generated Browser platform catalog;
- embedded platform content;
- reference-view XML-documentation companions;
- implementation-view PDB and source-document capabilities; or
- another separately designed platform source.

The list is illustrative rather than an authority grant. Each source owner
defines its own request, evidence, failure, cache, and lifetime contract before
it can contribute.

Network-backed work remains explicit or capability-gated. The House cannot
turn an installed miss into a NuGet request, a Browser catalog miss into a
desktop probe, or a package-cache hit into current source authorization unless
the source plan explicitly permits that route.

### Source contribution contract

Every contribution retains:

- source-capability identity;
- the exact requested House target;
- the source-specific coordinate and owner-issued target correspondence;
- supported view and population demand;
- source generation or freshness evidence;
- candidate identities or realized owner result;
- associated documentation artifacts or source capabilities when requested;
- authoritative, partial, or unavailable completion; and
- typed rejection or failure when the operation did not succeed.

Target-discovery results are separate from realization contributions. After
the House freezes one exact target, a realization source that discovers
another exact target may return it as discovery evidence only when its source
contract permits that result. It does not answer the settled exact request by
relabeling that evidence.

Paths, pack names, package IDs, framework aliases, file names, and Browser
labels are source coordinates or presentation. They never independently prove
House target correspondence.

### Settlement

The source plan states whether a source order is precedence, fallback,
aggregation, or facet-specific selection. The House does not infer that policy
from enumeration order.

Settlement obeys these rules:

- a candidate must retain exact House-target correspondence;
- reference and implementation contributions settle independently;
- two facets may be paired only when owner-issued evidence establishes their
  correspondence;
- a reference definition reaches an implementation-resolution start only
  through House-owned view correspondence;
- one source's absence does not authorize another source;
- an earlier success may stop later work only when the explicit policy proves
  that later contributions cannot change validity, ambiguity, or required
  completeness;
- a selected source failure is not hidden by a success-shaped empty result;
- a partial population cannot satisfy complete-population demand;
- a reference assembly does not prove an implementation body;
- a reference XML companion does not prove SourceLink availability, while a
  matching implementation PDB does not prove compiled XML availability; and
- equal names or versions do not merge different families, targets, sources,
  generations, or physical suppliers.

ASP.NET Core source realizations may retain a .NET runtime support closure.
The source owner establishes that closure and its version behavior. The House
preserves the focus target, support targets, members, and evidence rather than
inventing one merged family target.

## Typed ingress and bare-library convergence

Package, platform, and library are distinct entry domains. Textual equality
between their coordinates does not transfer identity or authority across those
domains.

Package and platform are peer container/source domains with analogous
opportunity: each keeps its own typed identity, version settlement,
acquisition, provenance, container inspection, and library unwrapping. Neither
is modeled as a wrapper around the other or as a specialized form of a bare
library.

The owning command or asset-reference parser classifies ingress before any
House runs:

- a CLI package selector, `.nuspec` dependency, project
  `PackageReference`, or restored package edge enters `PackageHouse` as a typed
  package coordinate;
- a CLI platform selector or project `FrameworkReference` enters
  `PlatformHouse` as a typed target demand; the request may already contain an
  exact target or may authorize explicit version selection;
- a file path, project `Reference` or `HintPath`, built project output, or
  already-acquired assembly enters shared Library inspection as a direct
  library;
- a Metadata `AssemblyRef` enters the Assembly Reference Resolution Ladder and
  reaches `PlatformHouse` only if the ladder authorizes its platform rung; and
- an upstream `PlatformDelegation` authorizes orchestration to issue an
  ordinary `PlatformHouse` request without changing the package coordinate
  that produced it into an assembly or library identity.

A bare CLI token such as `System.Text.Json` is therefore not intrinsically an
`AssemblyRef` or `PackageRef`. The command grammar and discovery result issue
the typed request before source processing begins.

The three asset flows are:

```text
PackageRef
  -> PackageHouse
  -> pruning service when comparable
     -> Subsumed: emit platform delegation and exit package acquisition
     -> otherwise:
          resolve package version when required
          re-evaluate pruning for the exact coordinate when required
          acquire -> unwrap -> bare library

Platform request or orchestration-issued delegation
  -> PlatformHouse
  -> target and version resolution
  -> acquire authorized realization
  -> select reference and/or implementation view
  -> unwrap
  -> bare library

Direct library
  -> bare library
```

The analogous CLI subject scenarios are:

| Subject | Entry domain | Native inspection | Library-focused handoff |
| --- | --- | --- | --- |
| `library` | Direct library coordinate | Library-shaped from entry | The entry already is a bare library. |
| `package` | Typed package coordinate through PackageHouse | Package-shaped metadata, dependencies, and assets | Each explicitly selected compatible library unwraps with package provenance. |
| `platform` | Typed target demand through PlatformHouse | Platform-shaped target, population, and view evidence | Each explicitly selected platform library unwraps with platform provenance. |

These paths converge on shared Library inspection, not on another House. A
bare library is container-independent but not provenance-free. It retains:

- physical artifact and assembly identity for every retained view;
- direct, package, or platform origin;
- source capability and generation evidence;
- exact target context when one exists;
- reference or implementation role;
- reference-to-implementation correspondence when both views exist;
- the content lease or lifetime required to keep bytes readable; and
- visible acquisition failure, rejection, ambiguity, or incompleteness.

The bare-library contract does not imply one library per package or one library
per platform. A package or platform may produce zero, one, or many compatible
libraries, and retains its own non-library assets and container-shaped
inspection. Unwrapping occurs only when a consumer explicitly requests a
library-focused result.

`PlatformHouse` owns the platform half of this convergence: it turns one
settled platform library demand into a bare library without discarding
platform target, source, or view correspondence. Shared Library inspection owns
assembly-level metadata, API, dependency, source, analysis, and decompilation
behavior after that handoff.

The exact shared type name and project placement belong to the artifact and
Library inspection owner, not this document. Its focused design must preserve
the evidence above before any PackageHouse, PlatformHouse, or direct-library
adoption ships.

## Assembly-reference processing

The
[Assembly Reference Resolution Ladder](assembly-reference-resolution-ladder.md)
owns rung order, route-set completeness, Workspace replacement, and the final
ladder outcome. `PlatformHouse` owns only the applicable-platform contribution.

The ladder calls the House after the referencing-context rung returns
`NoNameOwner` and after its route owner supplies:

- the exact `AssemblyBindingRequest`;
- exact origin and resolver lineage;
- the applicable `PlatformFamilyTarget`;
- typed authorization to evaluate the platform rung;
- the source-plan and operation identities; and
- the remaining work ledger.

Because the ladder operates inside an already-settled Workspace context, this
request carries an exact target demand. The House:

1. validates the exact platform target and route authorization;
2. realizes the required platform library or source-owned atomic unit;
3. forms the platform-authorized Metadata binding policy over the selected
   candidates; and
4. returns the unchanged Metadata binding decision with House correspondence.

The House does not call the package rung, search NuGet by assembly name, or
advance the ladder. A completed platform `NoNameOwner` lets the ladder proceed
under its own contract. `NameOwnedNoMatch`, ambiguity, unavailable evidence,
rejection, or incomplete work retain their existing terminal meaning.

## Typed platform delegation from PackageHouse

Package-reference processing remains package-shaped. The package owner retains
the original typed `PackageRef`, target context, resolved or unresolved package
version evidence, and the source from which that evidence was obtained.

The
[Platform package supply policy](platform-package-supply-policy.md)
is a focused pure service used by that package owner. When it returns
`Subsumed`, the package owner may stop before package payload acquisition and
issue a typed `PlatformDelegation`. `NotSubsumed`, `NotComparable`, unavailable,
or incomplete pruning evidence does not authorize that delegation. A ranged or
unresolved package version may require package metadata resolution before the
policy can return a final comparable result, but that does not require package
payload acquisition.

The package owner retains:

- the original package coordinate and version evidence;
- the exact target used by the pruning service;
- the complete pruning result and supplying family/version evidence; and
- the fact that package payload acquisition was skipped.

The orchestration owner consumes the delegation and issues an ordinary
platform demand with one exact `PlatformFamilyTarget`. `PlatformHouse` neither
receives nor re-evaluates the raw `PackageRef`, and its result receipt does not
absorb the package decision receipt. The orchestration owner preserves the
association between those two receipts when the end-to-end operation needs to
explain the delegation.

`PlatformSupply` deliberately contains no platform-library identity. A package
ID such as `System.Text.Json` cannot establish an assembly identity with the
same spelling. A delegated population request may realize a complete platform
population. A delegated one-library request additionally requires an
independently typed platform-library or assembly demand from the owning
scenario.

## Transparent .NET Standard processing

.NET Standard is a reference contract, not a `PlatformFamily`,
`PlatformFamilyTarget`, registration population, or implementation population.
It therefore never appears as the House target.

When a platform operation begins from a .NET Standard facade:

1. the source contribution retains the exact .NET Standard reference-contract
   coordinate separately from the House target;
2. the House realizes or selects the exact runtime reference or implementation
   candidates authorized by the operation;
3. the House supplies those candidates through an explicit Metadata binding
   policy;
4. Metadata follows any exact `ExportedType` and `AssemblyRef` evidence and
   returns its unchanged reference-view outcome;
5. when implementation demand remains, the House requires an explicit
   reference-to-implementation view correspondence for the resolved contract
   definition and starts a second Metadata resolution in the authorized
   implementation context; and
6. the House settlement retains the starting facade, both Metadata outcomes,
   every real forwarding hop, the separate view-correspondence transition, the
   terminal physical supplier when resolved, and the runtime target and source
   evidence.

The House never parses a forwarder itself, guesses a sibling from a file name,
or treats the facade's contract version as the runtime target version.
Metadata remains unaware of platform-family policy; it receives candidates and
binding decisions through its generic contract.

Metadata may terminate the reference-view request at a `TypeDef`. That
terminal definition remains correct and unchanged. It does not itself prove
the implementation supplier. The House-owned view correspondence bridges that
reference result to one implementation-resolution start and is recorded
outside the forwarding-hop sequence.

View correspondence is valid only when:

- both sides retain exact source and target evidence;
- a source owner explicitly pairs the reference and implementation assembly,
  or a separately owned compatibility projection explicitly maps the external
  contract definition to one runtime resolution start;
- the same structured `MetadataTypeDefinitionName` is used for the
  implementation request;
- the implementation candidate is authorized by the captured source plan; and
- no file-name, path, namespace, assembly-prefix, or first-match inference
  supplies the relation.

A correspondence may connect a normal platform reference assembly to its
implementation assembly or connect a transparent .NET Standard contract
definition to a runtime compatibility facade or implementation candidate. If
the required correspondence is unavailable or ambiguous, implementation
demand remains unavailable or ambiguous even when a same-named type exists
somewhere in the runtime population.

Reference-only success is sufficient only for reference demand. An operation
requiring implementation bodies succeeds only when Metadata reaches a terminal
definition in an implementation contribution. A forwarding chain ending in an
unavailable, ambiguous, rejected, or unbound implementation candidate keeps
that outcome. It is not reported as type absence or an empty body population.

The same Metadata engine remains available to package, project, and local
domains under their own binding policies. The encapsulation rule is narrower:
only `PlatformHouse` composes that generic engine into product platform
reference processing.

## Type catalogs are derived facets

A platform type catalog is a target-bound index over selected reference or
implementation candidates. It may accelerate one-library discovery or seed a
structured Metadata request, but it does not own type resolution.

The House may retain a catalog facet when:

- every indexed declaration retains its exact assembly candidate;
- definition and forwarding declarations remain distinct;
- the catalog's target, source generation, view, and completeness are visible;
- selection still passes through Metadata identity and binding contracts; and
- a catalog miss is not strengthened into type absence when indexing was
  partial or unavailable.

The current `PlatformTypeCatalog` is therefore migration evidence, not the
future public API. Its useful indexing behavior moves behind the House; its
source-selection heuristics do not become identity or Metadata policy.

## Platform documentation evidence

Platform documentation is available through two independent evidence channels.
They may describe the same public API, but they do not have interchangeable
identity, provenance, cost, or failure semantics.

### Documentation demand is explicit

Documentation demand is closed:

- **CompiledXml** requests only compiled XML documentation associated with the
  selected reference contribution;
- **SourceDerived** requests only documentation comments extracted from
  PDB-mapped, checksum-verified source associated with the selected
  implementation supplier; and
- **CompiledXmlAndSourceDerived** requests both independent attempts.

Compiled XML is local or already-acquired artifact work once its reference
contribution is realized. Source-derived documentation may require PDB,
SourceLink, and source-body acquisition. A missing compiled XML artifact never
widens the request to SourceLink, and an unsuccessful SourceLink attempt never
suppresses available compiled XML. The source plan and documentation demand
must authorize every attempted channel.

Documentation demand may require a corresponding House view even when the
caller does not request API or body output from that view. `CompiledXml`
requires a reference contribution; `SourceDerived` requires an implementation
supplier. This requirement authorizes only the evidence channel, not unrelated
reference indexing, implementation-body analysis, or whole-population work.

The operation subject is one owner-issued type or member identity retained from
the selected API or Metadata result. Display names, source text, file names,
and overload ordinals cannot reconstruct that subject.

### Compiled XML follows the reference view

The XML channel consumes a companion artifact associated by its source owner
with the exact settled reference contribution and House target. A conventional
reference-pack sibling such as `System.Text.Json.xml` is a source coordinate,
not independent proof of association. The contribution retains the reference
assembly identity, source generation, exact target, companion evidence, and
XML-documentation subject identity used for lookup.

XML lookup follows the unchanged terminal reference-view definition returned by
Metadata. When the operation starts from a reference facade and Metadata
follows real forwarding declarations, the XML companion belongs to the
reference contribution that supplies the terminal definition, not necessarily
the facade named by the caller. The settlement retains the starting contract,
forwarding hops, terminal reference supplier, and selected companion. A direct
reference `TypeDef` remains bound to its own reference contribution.

Compiled XML may satisfy platform documentation without an implementation
assembly, PDB, SourceLink map, or network source fetch. Missing member
documentation is a typed absence for that exact subject. A missing companion,
malformed document, rejected identity, parse failure, or exhausted work remains
its distinct outcome rather than becoming an empty successful document.

XML-documentation identity and parsing remain owned by their existing Metadata,
CSharpText, and hardened-reader contracts. The House selects and retains their
results; it does not define the XML grammar or parse the file itself.

### Source-derived documentation follows the implementation view

The source channel begins only from the exact implementation supplier selected
through the House's existing reference-to-implementation correspondence. The
reference assembly's lack of debug information is not a SourceLink failure,
and a same-named runtime assembly found by path or file-name convention cannot
replace the required correspondence.

The implementation supplier enters the existing PDB and SourceLink contracts:

1. a portable PDB must match the implementation assembly identity;
2. SourceLink maps the selected PDB document under its existing rules;
3. `PdbSourceHouse` settles an authorized source candidate and verifies its
   checksum; and
4. the source-comment owner extracts documentation evidence for the requested
   subject.

This path is shared with package-backed source inspection. Platform status does
not create a second PDB matcher, SourceLink resolver, source fetcher, checksum
policy, or source-comment parser.

Browser/Wasm may authorize an acquired or generated XML companion and a
host-neutral PDB store and source fetcher. It does not gain desktop filesystem
or ambient symbol access. When the host omits the SourceLink capabilities, the
source-derived attempt is visibly unauthorized or unavailable while compiled
XML may still settle.

Portable PDB document identity and checksum correspondence do not prove which
physical syntax tree produced a metadata definition. Source-derived
documentation therefore retains its declaration-correspondence evidence. An
exact owner-issued member/source correspondence may support an exact result; a
name- or text-selected comment is explicitly best-effort and cannot silently
replace exact compiled XML documentation.

### Settlement preserves both channels

The completed value contains one typed attempt per requested channel. Each
attempt reports available, absent, unavailable, rejected, failed, or incomplete
evidence with its own provenance and work. One successful channel does not
rewrite the other channel's outcome.

When both channels produce documentation:

- the House preserves both original documents and their provenance;
- compiled XML fields selected by exact XML-documentation identity are not
  overwritten by best-effort source-comment extraction;
- source-derived content may fill a missing field only in an explicitly
  requested projection that retains field-level origin;
- differing non-empty values remain a visible conflict in detailed evidence;
  and
- rendering or section selection may choose a concise default without deleting
  the unselected evidence from the House result or receipt.

The House does not infer that one channel is newer from a path, package version,
framework alias, repository revision, or display label. It composes the
channels only when the reference and implementation contributions retain the
same exact House target and explicit view correspondence.

## Result and receipt contract

Each operation returns an operation-specific value inside one common House
outcome envelope:

- **Completed** — the requested operation settled, with its typed value and
  settlement receipt;
- **Unavailable** — an authorized target-selection capability, source, exact
  target, required view, candidate, inventory, or implementation supplier
  could not be obtained;
- **Ambiguous** — owner evidence permits several active candidates without an
  authorized precedence;
- **Rejected** — the request, source plan, target correspondence, candidate,
  owner result, budget, or retained evidence is invalid; or
- **Incomplete** — bounded work, partial source evidence, route association,
  catalog coverage, or generation replacement prevents a conclusive result.

Cancellation remains `OperationCanceledException`.

`Completed` does not mean every possible facet succeeded. It means every facet
required by the exact operation settled. For example:

- a reference-only `Realize` operation may complete without implementation
  evidence;
- a one-library `Realize` operation may complete with one bare-library value
  retaining its platform provenance;
- an assembly-reference operation may complete with Metadata
  `NoNameOwner`; and
- a type-resolution operation may complete with Metadata `NotFound` only when
  the selected readable image authoritatively returned that result.

A documentation operation completes when every requested channel reaches a
typed terminal attempt, including absence, unavailability, or failure.
Top-level `Unavailable`, `Rejected`, or `Incomplete` is reserved for a missing
or invalid House target, subject, source plan, correspondence, or work boundary
that prevents the requested attempts themselves from settling.

The settlement receipt binds:

- the exact request, target demand, and settled House target when available;
- target-selection policy, candidates, and owner result when selection was
  required;
- source-plan identity and policy generation;
- typed request origin and any orchestration-owned delegation association;
- every selected or outcome-relevant source contribution;
- reference and implementation view correspondence;
- the physical identity, role, origin, and owner-issued content-lifetime
  association of every returned bare library;
- Metadata binding or forwarding outcomes when invoked;
- requested documentation channels and every compiled-XML or source-derived
  attempt, including per-field provenance or conflicts in a composed
  projection;
- source, Workspace, and catalog generations;
- completeness; and
- consumed work.

The receipt does not expose credentials, mutable buffers, live streams,
filesystem paths as identity, or a capability that can repeat source work.
Bound source handles and leases remain owned by their source or Workspace
contract.

## Lifetime and Workspace boundary

The House may discover that an authorized platform source must acquire or
realize content. It does not mutate a sealed `AssemblyContextGroup` or publish a
Workspace generation.

For Workspace-bound resolution:

1. the House returns an owner-issued acquisition or realization demand
   associated with the exact request and target;
2. the assembly ladder suspends under its existing result contract;
3. Workspace performs retirement, preparation, admission, and replacement;
4. the House receives the replacement's fresh source and binding
   correspondence; and
5. the logical operation continues only after validating every generation and
   policy identity.

For standalone platform inspection, the caller owns the operation lifetime and
disposes every returned source lease. The same House request and settlement
semantics apply; standalone use does not create Workspace authority.

House caches key successes and failures by target demand, settled exact target,
target-selection policy generation, operation shape, source-plan generation,
source generation, view, and population demand. A replacement or policy
change cannot reuse a prior binding decision merely because the paths or
display labels are equal.
Documentation cache keys additionally retain the exact subject, requested
channels, reference/XML generation, implementation/PDB identity, and
source-document identity used by the settled attempt.

## Encapsulation boundary

Product code that decides platform binding, transparent .NET Standard
implementation resolution, or platform documentation-source settlement calls
`PlatformHouse`. Product code processing a `PackageRef` calls `PackageHouse`;
only an orchestration-owned typed platform delegation or ordinary typed
platform request may cross into `PlatformHouse`.

Lower-level owner APIs remain valid for:

- their own implementations and tests;
- source-adapter construction;
- generic Metadata resolution in non-platform domains;
- pruning data generation, PackageHouse decision-making, and audit projection;
  and
- independently designed diagnostics that expose an owner fact without using
  it to change reference processing.

They are not parallel product service surfaces. In particular:

- CLI and Browser do not call `PlatformResolver`, `PlatformPackService`,
  or `PlatformTypeCatalog` to process a platform reference;
- CLI, Browser, Queries, and Workspace do not send a raw `PackageRef` to
  `PlatformHouse` or ask it to invoke pruning;
- Queries and Workspace do not recreate platform source precedence or
  .NET Standard forwarding policy;
- CLI and Browser do not independently choose between platform reference-pack
  XML and implementation SourceLink evidence;
- the assembly ladder receives one House platform contribution rather than
  composing platform internals; and
- source adapters do not call back into the House or the assembly ladder.

The user selected no automated absence gate for this composition claim. It
remains binding architecture enforced by design and code review. Future
adoption gates prove representative product paths through the House but do not
scan the repository or compiled graph for every possible bypass.

## Project and dependency boundaries

The logical House owner may span projects; a project name alone does not
define the owner.

The required dependency shape is:

```text
DotnetInspector.Platforms
  package-neutral target currency
             |
             v
PlatformHouse contract and composition
  consumes platform source, Metadata, XML/PDB/SourceLink capabilities,
  and owner results
             |
             | one-library Realize
             v
provenance-retaining bare library
             |
             v
shared Library inspection

PackageHouse --typed platform delegation--> application orchestration
platform CLI or FrameworkReference --------> application orchestration
application orchestration --ordinary platform request--> PlatformHouse

direct library -----------------------------------------> shared Library inspection
```

`DotnetInspector.Platforms` remains the lower resource-free contract floor. It
does not acquire House request, source, package, pruning, Metadata, or result
dependencies.

The operational House belongs in a separate host-neutral project boundary
above Metadata and any package-backed platform-source adapter. `PackageHouse`
does not call into that implementation project; it returns its typed
delegation to an orchestration layer that can issue an ordinary House request.
If source-adapter dependency inversion requires a smaller contract seam, that
seam remains part of the same House owner rather than expanding the identity
floor. Exact project names and moves are tracked by
[#6335](https://github.com/richlander/dotnet-inspect/issues/6335).

Source-specific implementations remain focused:

- installed implementation realization preserves its package-free dependency
  boundary;
- package-backed pack acquisition may depend on package source and payload
  owners;
- XML-documentation parsing, PDB acquisition, SourceLink interpretation, and
  PDB-mapped source acquisition remain reusable package/platform mechanisms;
- Browser-generated catalogs remain portable and contain no desktop adapter;
- pruning remains with the package/pruning owner; and
- forwarding remains with Metadata.

The House composition project may depend on these owner contracts. None of
them depends on the operational House merely to preserve its own facts. Source
capabilities and the cross-House delegation handoff are injected or adapted in
the direction that avoids `PackageHouse -> PlatformHouse -> Packages` and
equivalent cycles.

## Pathological cases

### A delegated package ID matches an assembly name

PackageHouse delegates a subsumed `System.Text.Json` package coordinate to a
runtime platform target. The delegation does not prove that
`System.Text.Json.dll` is the requested or supplying assembly. PlatformHouse
realizes a whole population unless the owning scenario also supplies an exact
platform-library or assembly demand. Equal spelling cannot create that demand.

### A ranged package reference needs version metadata

PackageHouse receives a package version range that the pruning service cannot
compare. It may resolve package version metadata before making its final
decision, but it does not acquire and unwrap the package payload first.
PlatformHouse receives no request until the package owner emits a typed
delegation with one exact platform target.

### A direct library bypasses both container Houses

A CLI path or project `HintPath` already identifies a physical library. The
orchestration owner creates the provenance-retaining bare-library value and
enters shared Library inspection directly. It does not wrap the file as a
package or platform request merely to obtain command symmetry.

### .NET Standard facade resolves to runtime implementation

A type request starts from a selected `.NET Standard` facade. The House target
is `DotNetRuntime/net11.0/11.0.0`. Metadata follows the facade's exact
forwarding declarations under the House-supplied reference policy. Whether the
reference path ends at a forwarded or directly declared `TypeDef`, the House
then consumes explicit view correspondence and starts a separate Metadata
resolution in the runtime implementation context. The settlement retains the
reference-contract start, real forwarding hops, the non-forwarding view
transition, and the physical runtime supplier. No `NetStandard` family target
exists.

### A reference TypeDef is not an implementation TypeDef

The reference view defines the requested type directly. Metadata correctly
returns that reference `TypeDef` and performs no forwarding. The House does
not reinterpret the definition as an implementation body. Implementation
demand proceeds only through explicit view correspondence and a second
Metadata request against the implementation context.

### .NET Standard implementation is unavailable

The same facade is available, but the authorized source plan contains no
implementation contribution. A reference-only API request may complete. An
implementation-body request returns unavailable or Metadata's exact unbound
outcome. It does not report that the type has no implementation.

### ASP.NET Core has a runtime support closure

An ASP.NET Core implementation source returns an exact ASP.NET Core focus
target and a separately selected runtime support target. The House preserves
both and the source owner's closure evidence. It neither merges their family
identities nor replaces one patch version with the other.

### Browser catalog is stale

The Browser source plan supplies a generated catalog that lacks the requested
target. The House may use another explicitly authorized Browser capability.
Otherwise it returns unavailable or incomplete. It does not probe desktop
installation paths or relabel the stale catalog.

### A source succeeds after another source fails

Whether the later success is usable depends on the explicit source policy. A
fallback plan may select it while retaining the first failure. An aggregation
plan may remain incomplete if the failed source could contribute a peer
candidate or required facet. Enumeration order alone decides nothing.

### Budget expires after one candidate

One source returns an identity-eligible assembly before work expires, but
another required source has not settled. The House returns incomplete unless
the source plan proves the first source has precedence independent of the
unexamined result. It does not publish a provisional first match as settled.

### Reference XML exists without SourceLink

The selected reference pack supplies `System.Text.Json.xml`, but no matching
implementation PDB is available. A `CompiledXml` request completes with exact
XML documentation. A combined request retains successful XML and unavailable
source-derived documentation; it does not erase the description or claim that
platform documentation is absent.

### SourceLink exists without reference XML

The exact reference contribution has no XML companion, while the
corresponding implementation supplier has a matching portable PDB and usable
SourceLink map. A `CompiledXml` request reports the missing companion without
network work. A separately authorized `SourceDerived` or combined request may
return source-derived documentation and retains the XML absence.

### Reference and source comments differ

Compiled XML and checksum-verified authored source provide different summaries
for the same member. The House returns both values and their provenance. The
exact XML-documentation result is not overwritten by a best-effort textual
match, and a detailed projection exposes the conflict.

### SourceLink belongs to the forwarding facade

The selected reference assembly defines the public type, while the runtime
facade forwards it to another implementation assembly. The House does not open
the facade PDB and report source absence as final. It uses explicit view
correspondence and Metadata resolution to select the physical supplier before
PDB acquisition, then retains the facade, forwarding, view transition, PDB,
and source-document evidence.

### Reference XML belongs to a forwarded declaration supplier

The request starts from a reference facade whose Metadata declarations forward
the selected type. The House preserves the starting facade and forwarding
evidence, then looks up compiled XML only through the companion associated with
the terminal reference declaration supplier. A same-named XML file beside the
starting facade cannot manufacture documentation for the forwarded subject.

## Production adoption and retirement

There are eleven counted production steps:

1. Lock the lower Platform Target Currency under #6361.
2. Lock this focused House contract under #6301.
3. Introduce the host-neutral target-demand, House request, outcome, receipt,
   source-plan, and contribution types without moving source implementations.
4. Adapt installed target discovery plus reference and implementation
   realization to contribute owner-issued selection and exact-target source
   evidence while preserving the package-free installed boundary.
5. Adapt package-backed target discovery plus reference and implementation
   packs to contribute source-authorized selection and exact-target evidence.
6. Define the shared provenance-retaining bare-library handoff, adapt
   PlatformHouse one-library realization to produce it, and route direct
   libraries to the same Library inspection entry.
7. Move target-bound type indexing behind the House and integrate transparent
   .NET Standard resolution through Metadata's structured forwarding contract.
8. Add target-bound platform documentation evidence settlement, adapting
   reference XML companions and implementation PDB/SourceLink capabilities
   without moving their parsing or acquisition owners.
9. Adopt the House platform contribution in the assembly-reference ladder,
   add the PackageHouse-to-platform delegation handoff without a direct
   House-to-House dependency, and integrate Queries, Workspace replacement,
   and dependency traversal.
10. Adopt the same House requests and outcomes, including documentation
    evidence, in CLI and Inspect Web.
11. Retire direct product reference-processing and platform-documentation
    composition in `PlatformResolver`, `PlatformPackService`,
    `PlatformTypeCatalog`, `SourceEnricher`, and host composition, then
    complete the `DotnetInspector.Services` decomposition tracked by #6335.

Each step after the House contract is a separately reviewed owner adoption.
The stack preserves a usable product after every step; a bypass is retired only
after its House replacement is live in every supported host that uses it.

No CLI flag is retained solely for compatibility. User-facing platform
coordinates project to the shared target and House request, and unsupported
legacy combinations fail visibly under the adopting command owner.

## Demo

### PackageRef exits to platform realization

```text
PackageRef ingress
  coordinate: System.Text.Json 10.0.0
  exact target: DotNetRuntime / net11.0 / 11.0.0

PackageHouse
  pruning service: Subsumed
  package payload acquisition: skipped
  output:
    typed platform delegation
    package decision receipt retains PackageRef and pruning evidence

orchestration -> PlatformHouse
  operation: realize
  target: DotNetRuntime / net11.0 / 11.0.0
  demand: platform population
  sources: host-authorized installed + package-backed platform plan

PlatformHouse
  target/version settlement
  authorized realization acquisition
  reference/implementation selection

completed
  platform receipt retains exact target, source, views, and work
  each selected library unwraps with platform provenance
  package receipt remains associated but separate
```

### Direct platform library becomes a bare library

```text
request
  target demand:
    family: DotNetRuntime
    framework: net11.0
    version policy: highest authorized installed stable in the net11.0 band
  operation: realize
  library: owner-issued System.Text.Json assembly identity
  demand: reference + implementation
  sources: host-authorized platform plan

House composition
  version selection:
    exact target: DotNetRuntime / net11.0 / 11.0.3
    owner-issued candidate and policy evidence retained
  realizes reference and implementation contributions
  verifies view correspondence
  unwraps the selected contributions

completed
  bare library:
    physical assembly identities
    Platform origin
    exact target and source generations
    reference/implementation roles and correspondence
    readable-content leases
```

### Direct library bypasses container resolution

```text
CLI or asset-reference ingress
  path, HintPath, or built project output

orchestration
  validates and acquires the direct artifact
  constructs the same bare-library contract with Direct origin

Library inspection
  consumes the bare library directly
  no PackageHouse or PlatformHouse request is manufactured
```

### Transparent .NET Standard forwarding

```text
request
  target: DotNetRuntime / net11.0 / 11.0.0
  operation: resolve type definition
  start: selected netstandard reference facade
  demand: implementation

House composition
  reference contract retained separately
  reference Metadata resolution follows:
    ExportedType -> exact AssemblyRef -> selected runtime candidate
  reference TypeDef retained
  House view correspondence selects an implementation start
  implementation Metadata resolution reaches the physical supplier

completed
  no NetStandard family target
  reference facade, real forwarding hops, view correspondence,
  runtime target, implementation supplier, and source evidence all retained
```

### Reference XML and SourceLink documentation

```text
request
  target: DotNetRuntime / net11.0 / 11.0.0
  operation: resolve documentation evidence
  subject: JsonSerializer.Serialize(...)
  demand: compiled XML + source-derived
  source policy: installed reference pack + authorized PDB/SourceLink

House composition
  reference contribution:
    System.Text.Json.dll + associated System.Text.Json.xml
    XML member identity -> exact compiled documentation
  view correspondence:
    reference definition -> runtime implementation supplier
  implementation contribution:
    matching portable PDB -> SourceLink document
    checksum-verified source -> source-derived comment

completed
  compiled XML: available
  source-derived documentation: available, best-effort declaration match
  effective projection:
    XML fields retained
    source-only fields carry source provenance
  receipt:
    exact target, subject, reference/XML contribution,
    implementation/PDB/source contribution, correspondence, and work
```

## Evidence and required gates

This design introduces no new independent state machine. Source operations
remain under their source owners, Metadata forwarding remains under its
bounded frozen-context contract, and Workspace replacement remains under the
artifact-session lifecycle. A new TLA+ model would duplicate those owners
rather than establish this request/settlement boundary.

The implementation and adoption slices own these Release gates:

| Property | Required gate |
| --- | --- |
| Target settlement | An exact demand is retained unchanged; a selecting demand freezes one owner-issued exact `PlatformFamilyTarget` before acquisition, and every outcome retains the demand and selection evidence. |
| Explicit source authorization | No source capability, cache, installed root, or network route is used unless present in the captured source plan. |
| Demand-bounded work | One-library demand does not become whole-population work unless the selected source reports an atomic larger realization unit. |
| Reference and implementation separation | Reference-only success cannot satisfy implementation-body demand; a combined result retains explicit correspondence. |
| Reference-definition bridge | A reference `TypeDef` reaches implementation only through House-owned view correspondence and a separate Metadata request; no forwarding hop is invented. |
| Typed ingress separation | A raw `PackageRef` cannot enter PlatformHouse, and a bare CLI selector cannot become an `AssemblyRef` or `PackageRef` without command-owned classification. |
| Delegation preserves identity boundaries | An upstream platform delegation retains its package decision receipt outside PlatformHouse and cannot establish a platform-library or assembly identity from package spelling. |
| Bare-library provenance | Every one-library realization retains physical identity, platform origin, exact target, source generation, view role, correspondence, and content lifetime after unwrapping. |
| Direct-library convergence | A direct path or project library enters the same Library inspection contract without PackageHouse or PlatformHouse mediation. |
| Metadata ownership | Platform type resolution invokes the structured Metadata API and preserves its exact outcome and forwarding hops. |
| Transparent .NET Standard | A `.NET Standard` facade can resolve through an exact runtime target without constructing a `NetStandard` family or implementation population. |
| Physical supplier retention | A resolved implementation type or assembly retains its physical supplier rather than being relabeled as the reference facade. |
| XML/reference correspondence | Compiled XML documentation can settle only through companion evidence associated with the exact selected reference contribution and target. |
| Source/implementation correspondence | Platform SourceLink documentation starts from the exact implementation supplier selected through House view correspondence and a matching portable PDB. |
| Independent documentation channels | XML success survives SourceLink absence or failure, SourceLink success survives XML absence, and an XML miss performs no source acquisition unless explicitly requested. |
| Documentation provenance | Compiled XML and source-derived values retain channel, artifact, subject-correspondence, and generation evidence; best-effort source extraction cannot overwrite exact XML identity. |
| Documentation conflict visibility | Differing non-empty XML and source-derived values remain observable in the typed result even when a concise projection selects one value. |
| Visible incomplete evidence | Source, catalog, forwarding, acquisition, work, and generation incompleteness never become absence or a success-shaped empty result. |
| Ladder composition | The assembly-reference ladder receives one House platform contribution and preserves its own rung order and result algebra. |
| Host parity | Representative CLI and Browser operations issue equivalent House requests and interpret the same typed outcomes. |
| Installed boundary | Browser composition does not reference desktop installed adapters, and installed realization remains package-free. |
| Encapsulation | Representative ladder, Query, CLI, and Browser paths use the House. Per user choice, no automated repository-wide bypass-absence gate is required. |

The design-only PR is Markdown-only and requires `markdownlint`. Each
implementation slice adds the smallest gate covering its adopted property.

The House carries typed operation data but defines no presentation. Markout
and host-specific rendering are not part of this owner.

## Non-claims

This design does not:

- add `.NET Standard`, WindowsDesktop, or workloads as a `PlatformFamily`;
- define framework compatibility, runtime roll-forward, source version
  discovery, or version-ranking algorithms;
- define platform catalog membership or source-specific family composition;
- create a package-to-assembly naming convention;
- process a `PackageRef`, choose a package version, acquire a package payload,
  or apply package pruning;
- define the complete PackageHouse request, outcome, or receipt contract;
- make pruning an `AssemblyRef` classifier;
- alter package dependency traversal or the assembly ladder's route order;
- alter Metadata forwarding, binding, declaration, or terminal outcomes;
- make a reference assembly an implementation supplier;
- redefine XML-documentation identity or parsing, SourceLink mapping, PDB
  matching, source acquisition, checksum verification, source-comment parsing,
  or symbol-server policy;
- claim that SourceLink provenance proves the physical syntax tree that
  produced a metadata definition;
- define Workspace admission, replacement, or lease lifetime;
- require network access or desktop filesystem capabilities;
- permit inspected-assembly loading or Roslyn;
- add WinMD support; or
- create an aggregate platform assembly containing every platform helper.
