# Library family and host namespace boundaries

This document owns the meaning of the repository's project and namespace
families. It separates representation ownership, product composition,
dependency depth, and component role so that a name communicates what a
library is about without pretending that every library occupies the same
architectural altitude.

The end-to-end adoption tracker is [#6315](https://github.com/richlander/dotnet-inspect/issues/6315).
Its two steps are this contract and migration of the CLI host out of the
reusable `DotnetInspector` namespace. The browser host already uses its own
`InspectWeb` namespace.

## Claim

A reusable project or namespace is named for the architectural plane whose
identities and semantics it primarily owns. A host is named for the product
surface it implements. Neither dependency depth nor the physical carrier of an
input decides the family.

Placement considers three distinct questions:

| Axis | Question | Examples |
| --- | --- | --- |
| Architectural plane | Does the component own representation evidence, product composition, a neutral domain, or a host? | `ILInspector`, `DotnetInspector`, `NuGetFetch`, `DotnetInspect.Cli` |
| Composition altitude | Does it provide a primitive, producer, query, presentation, or host boundary? | Metadata primitive, Analysis producer, L1 query, CLI host |
| Component role | What kind of operation or composition does it perform? | Fetch, Service, House, query, renderer |

The answers interact, but they must not substitute mechanically for one
another. Dependency depth alone does not select a family, and a role such as
Service says nothing about the subject. Product query and presentation
contracts do, however, belong to the product-composition plane even when one
operation happens to inspect a single assembly.
`ILInspector.SourceLink` is a relatively high composer inside the
compiled-program family. `DotnetInspector.Artifacts` is a low contract floor
inside the broader product composition. `House` describes reach and role; it
does not select a subject family.

## `ILInspector`: compiled-program evidence

`ILInspector.*` is the representation and producer plane for a compiled .NET
program and its directly associated debug information. Its admissible inputs
include:

- PE and CLI metadata
- portable PDB records and their assembly association
- IL method bodies and exception regions
- typed or textual projections whose semantics are derived from those
  representations
- correlations and Findings whose identity remains anchored to those
  representations

Interpreting this family as only opcode processing would already exclude
`ILInspector.Metadata`, `ILInspector.CSharp`, and other established owners. The
explicit architectural decision is therefore to read the historical name as
the compiled-program representation and producer family. Not every project
must decode IL opcodes. Metadata, PDB correlations, control flow, C#
reconstruction, and implementation comparison describe the inspected program
from different representations.

An `ILInspector.*` component does not own:

- NuGet feed or package policy
- SDK, platform, or restored-project acquisition
- host authorization, command syntax, or navigation
- ambient filesystem discovery
- network fetching, credential policy, or persistent cache policy

Those concerns may supply content to the compiled-program family, but they do
not become compiled-program semantics.

## `DotnetInspector`: ecosystem and reusable product composition

`DotnetInspector.*` is the reusable product-composition plane. It owns
inspection concepts that compose representation producers with ecosystem
assets, workspace state, product query semantics, or shared presentation. Its
subjects include:

- source-neutral artifacts and workspaces
- NuGet packages and package relationships
- platforms, SDK assets, and restored-project evidence
- source-selection and acquisition services
- cross-host queries, sections, row selection, and presentation
- product-owned catalogs shared by more than one host

The family is reusable product substrate. A query may inspect only one assembly
and still belong here when it owns product-level capability, cost,
prerequisite, or execution semantics rather than new Metadata or IL facts. The
family is not the namespace of the `dotnet-inspect` command-line application.

This distinction allows the CLI and browser to consume the same
`DotnetInspector.*` libraries without making either host the architectural
center. A reusable component may be product-specific and still belong here; it
need not be a general-purpose .NET library.

## Independent domain roots

A project may use an independent root when its contract is coherent outside
both inspection families and the shorter name is established by the subject:

- `NuGetFetch` owns NuGet protocol access and transport behavior.
- `CSharpText` owns model-free C# and XML-documentation text grammars.
- `InertText` owns construction-time containment of untrusted text.

An independent root is not an escape from ownership. It must name a focused
domain, expose a contract that does not depend on either umbrella, and have
credible reuse at more than one composition point. A one-caller implementation
detail normally belongs with its caller unless a separate boundary has another
concrete reason to exist.

## Host namespaces

Hosts name themselves rather than claiming a reusable library family.

| Host | Namespace boundary |
| --- | --- |
| `dotnet-inspect` CLI | Target `DotnetInspect.Cli` |
| Inspect Web managed host | Existing `InspectWeb.*` |
| Focused tools and harnesses | Tool- or harness-specific root |

The CLI currently uses `DotnetInspector` and nested namespaces such as
`DotnetInspector.Commands`. That makes application-owned commands, options,
views, and mutable compatibility models appear to be peers of reusable
`DotnetInspector.Queries`, `DotnetInspector.Packages`, and
`DotnetInspector.Presentation`. The CLI migration in #6315 removes that
collision. It does not rename the executable, tool package, or reusable
libraries.

## Carrier does not decide ownership

The format that carries a fact is evidence for placement, not an automatic
answer. Ownership follows the stable identity and semantics exposed to
consumers.

SourceLink demonstrates the rule. SourceLink is a source-provenance protocol,
not IL semantics:

1. A portable PDB carries the SourceLink document map.
2. `ILInspector.Metadata` owns extraction of the custom-debug-information
   record and PDB document, checksum, sequence-point, and row identities.
3. `ILInspector.SourceLink` interprets the map and correlates those identities
   with type, member, and IL-offset source evidence.
4. The planned `PdbSourceHouse` composes authorized local, repository, and network
   acquisition, verification, caching, and visible failure policy.
5. `AssemblyContextSourceQuery` composes verified PDB source with the distinct
   decompiler fallback.

`ILInspector.SourceLink` therefore remains in the compiled-program family
because its primary contract is PDB document, type, member, and IL-offset
correlation. Not every result carries assembly identity: standalone map audit
and repository provenance are narrower subordinate contracts. They remain
colocated because the project has no independent generic SourceLink consumer,
not because all SourceLink semantics are inherently IL-related.

Conversely, moving network acquisition or host policy into that project would
violate the family boundary even though the acquired bytes are C# source named
by SourceLink. A future general-purpose SourceLink implementation with multiple
non-PDB consumers would deserve a neutral owner rather than automatic placement
under `ILInspector`.

The standalone SourceLink map utility had no independent second composition
point and is retired by #6312. Its map grammar and provenance logic remain
implementation details of `ILInspector.SourceLink`.

## Placement test

Classify a project or namespace in this order:

1. **Name the owned plane.** Does the component produce new facts from a
   compiled-program representation, compose product operations, implement a
   neutral domain, or host the product?
2. **Name the stable identities.** Are they metadata entities, PDB rows, and IL
   locations; ecosystem artifacts and workspaces; or protocol/text concepts?
3. **Identify policy dependencies.** Network authorization, package-source
   choice, ambient host state, and command behavior keep a component above the
   compiled-program family.
4. **Distinguish production from composition.** A product query or
   presentation contract belongs to `DotnetInspector` even when its immediate
   input is one assembly. A representation producer belongs to `ILInspector`
   even when it composes several views of that assembly.
5. **Identify the host boundary.** Command routing, options, interactive state,
   and host-native rendering belong to a host-specific namespace.
6. **Then name the role.** Apply `Fetch`, `Service`, `House`, query, rendering,
   or another role only after the family is settled.

If the answers disagree, split the responsibilities before choosing a name.
A project that combines compiled-image interpretation with network policy is
not evidence that either family should absorb both.

## Current classification

The following examples anchor the contract; they are not an exhaustive project
inventory.

| Classification | Representative projects |
| --- | --- |
| Compiled-program inspection | `ILInspector.Metadata`, `ILInspector.SourceLink`, `ILInspector.Instructions`, `ILInspector.Analysis`, `ILInspector.Decompiler`, `ILInspector.ILDiff`, `ILInspector.Research` |
| Ecosystem and reusable product composition | `DotnetInspector.Packages`, `DotnetInspector.Queries`, `DotnetInspector.PackageQueries`, `DotnetInspector.SourceSelection`, `DotnetInspector.Sections`, `DotnetInspector.Presentation` |
| Independent domain roots | `NuGetFetch`, `CSharpText`, `InertText` |
| Product hosts | `DotnetInspect.Cli`, `InspectWeb.*` |

Several existing names deserve focused classification work rather than a
conclusion in this document:

- `DotnetInspector.MetadataRendering` directly consumes Metadata but produces
  a rendering contract.
- `DotnetInspector.CSharpBodySlicer` consumes only `CSharpText` and may be a
  C#-text component rather than ecosystem composition.
- `DotnetInspector.Artifacts` and `DotnetInspector.Core` are cross-family
  floors; the current Metadata-to-Artifact reference is an explicit exception
  that exposes the naming pressure.
- `ILInspector.Findings` and `ILInspector.Text` describe domain-neutral
  contracts in the current architecture. Their established producer-stack
  placement is not evidence that every neutral substrate belongs under
  `ILInspector`.
- `DotnetInspector.Services` contains multiple focused services and Houses;
  continuing to split or rehome owners is preferable to renaming the mixed
  assembly as one unit.

Each accepted move requires its own owner-scoped issue and PR. This document
does not authorize a repository-wide rename or alter any component's runtime
contract.

## Adoption and evidence

The first adoption is the CLI namespace migration tracked by #6315. It proves
that reusable `DotnetInspector.*` libraries and the product host can be named
distinctly without changing command behavior. The browser already demonstrates
the intended host separation through `InspectWeb.*`.

Future moves must preserve dependency direction and public behavior. Their
focused gates are compilation, existing owner tests, dependency-policy
validation when project edges change, and host tests when a consumed namespace
changes. This naming contract adds no runtime safety or correctness claim.

## Non-claims

- Project prefixes do not define the L1/L2/L3 consumer layers.
- A lower dependency does not automatically belong to `ILInspector`.
- PDB carriage does not make acquisition policy an `ILInspector` concern.
- `DotnetInspector.*` does not mean CLI-only or necessarily high-level.
- Independent roots are not preferred over an existing coherent family.
- This document does not settle the candidate projects listed above.
