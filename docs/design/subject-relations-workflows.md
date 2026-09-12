# Subject relations: locate once, explore in both directions

## Status and authority

Proposed product workflow and composition contract, requested by the operator
on 2026-09-12. Focused design: [#6760](https://github.com/richlander/dotnet-inspect/issues/6760).
End-to-end adoption: [#6761](https://github.com/richlander/dotnet-inspect/issues/6761).
Nothing in this document is a claim that the proposed commands or defaults ship.

**Subject Relations composition** is the single normative owner established
here. Its exact claim is:

> Given an exact inspected subject, an explicitly described candidate
> population, and owner-issued relation evidence, compose one discoverable,
> bidirectional relation view without losing endpoint identity, evidence
> meaning, correspondence, or coverage.

This owner defines the product questions, subject/population distinction,
relation-view semantics, and composition obligations. It does not define
acquisition, Workspace mutation or lifetime, graph identity, metadata decoding,
IL analysis, C# binding, serialization formats, or CLI parsing. Those contracts
remain with the [participating owners](#infrastructure-and-owner-handoffs).
Required changes to their current defaults and capabilities are named adoption
prerequisites, not silently implemented amendments to their designs.

The requested scope joins `find`, existing subject commands, ecosystem
discovery, relation mechanisms, and sharing. This document proposes the workflow
target and composition boundary; it does not sweep lower-owner implementation
designs into one new umbrella component. Each adoption closes in its owner.

## Decision and motivating questions

**Make relation inspection a capability of an already selected subject.**
Use `find` to locate that subject and `@Relations` on `package`, `library`,
`type`, and `member` to explore it. Do not introduce a `relations` verb with
another coordinate grammar.

The convention is the existing `--share` decision: the command that already
resolved a coordinate supplies the operation. IDE symbol navigation offers
the same useful separation between locating a symbol and asking for its
hierarchy or callers.

| Question | Focus and relation reading |
| --- | --- |
| Is there anything here for `HttpClient`? | Locate the type; look inward for extension providers and other relevant relations in the selected population. |
| What is this component built on? | Keep the component as focus; inspect outgoing base/interface, signature, invocation, and dependency evidence without conflating them. |
| What integrates with Aspire resource management? | Locate its builder/resource contracts; find providers and separately identify actual callers. |
| What could I use with `foreach`? | Discover enumerable interfaces and supported enumeration-pattern candidates, including candidates that implement no enumerable interface. |
| Who uses this registration API? | Locate one exact overload; find incoming static call sites, not merely APIs with a similar signature. |
| What can I do with this package I already opened? | Select `@Relations` without re-entering its source, version, target, or binding context. |
| Can another person explore this result? | Share the same resolved subject, population and relation view, or report why that state is not portable. |

These questions are distinct from teaching an agent how to build a familiar
application. The earlier Aspire Redis, PostgreSQL and RabbitMQ baseline tasks
all succeeded using ordinary documentation. They did not test discovery of
relationships in unfamiliar compiled assets.

### Exploration checkpoints

Evaluate two hypotheses independently before broad adoption or command
retirement:

- **Subject continuity:** with the candidate population held constant, does
  locate-once plus subject relations reduce coordinate re-entry and command
  selection errors while preserving the same answers?
- **Default breadth:** with the query semantics held constant, does the broader
  registered population discover useful additional relations at an acceptable
  latency, acquisition cost and noise level?

Use three bounded discovery scenarios: HttpClient extension providers;
interface versus pattern enumeration candidates; and an unfamiliar Aspire
provider/consumer pair. Record answer correctness and evidence provenance,
reopening accuracy, command/tool work, time to useful output, packages/bytes
acquired, and incomplete-result handling. Compare current commands with the
proposed flow, not an agent's memory of how to build a sample application.
Run enough repetitions to distinguish a directional result from a single
successful attempt; do not infer reliability from one run.

All-known registration can remain useful even if eager broad execution is
not. If breadth performs poorly, retain the population and identity machinery
but revisit ranking, finite work policy or the default before shipping it.
That is an explicit design decision, not permission to silently narrow a
query reported as broad.

## Proposed demo

The following is a **mockup**, not executable documentation for today's CLI.
Flag/value binding belongs to the CLI adoption; the distinction between
population selection and semantic filtering is binding here.

```console
# Locate APIs without knowing their package or declaring type.
dotnet-inspect find AddRedis --members --ecosystem aspire

# Once the coordinate is known, use the ordinary subject command.
dotnet-inspect member Aspire.Hosting.RedisBuilderExtensions \
  --package Aspire.Hosting.Redis@13.5.3 --tfm net8.0 \
  -m AddRedis:1 -S @Relations

# Inspect the integration lens, then narrow the relation mechanism.
dotnet-inspect library ./AppHost.dll \
  -S Integration --where "ecosystem=ecosystem.aspire"
dotnet-inspect library ./AppHost.dll \
  -S Integration --where "mechanism=invocation"

# Reuse an already selected type; do not rebuild its coordinate elsewhere.
dotnet-inspect type HttpClient --platform System.Net.Http -S @Relations
```

Illustrative rows, from separately focused requests:

| Focus | Source | Relation | Target | Evidence |
| --- | --- | --- | --- | --- |
| `HttpClient` | `HttpClientJsonExtensions.GetFromJsonAsync` | extension receiver | `HttpClient` | declaration |
| `AddRedis` | `AppHost.Program.<Main>$` | calls | exact `AddRedis` overload | static IL call site |
| `AddRedis` | exact `AddRedis` overload | extension receiver | `IDistributedApplicationBuilder` | declaration |
| `List<T>` | `List<T>` | implements | `IEnumerable<T>` | declaration |
| `Span<T>` | `Span<T>` | enumeration pattern candidate | synchronous enumeration pattern | bounded pattern evidence |

The display abbreviations above are not identities. Production rows retain
exact source/target coordinates and evidence addresses. In particular, an
incoming view of `AddRedis` does not reverse the stored caller-to-callee edge.
The neighboring `Span<T>` case prevents interface membership from becoming the
definition of enumeration support.

## Command placement

Retain `ecosystem` as the ecosystem vocabulary, analogous to `vocabulary` for
the tool's own query vocabulary. It exposes the identities and configured
knowledge that many queries join against; it is not another relation-specific
artifact-inspection verb.

For this exploration, assume the standalone `extensions`, `implements`, and
`depends` commands are removed after replacement coverage is demonstrated.
Preserve their useful workflows, not their command tokens.

| Surface | Target role |
| --- | --- |
| `vocabulary` | Discover the tool's query terms and their meanings. |
| `ecosystem` | Discover ecosystem identities, concepts/bindings and configured contributions: the ecosystem vocabulary used across queries. |
| `find` | Locate packages, libraries, types and members, with exact reopening context. Ecosystem selection narrows its candidate population. |
| Subject commands plus `@Relations` | Primary single-subject relation experience, with the subject's existing resolution and sharing path. |
| Subject shortcut flags | High-value entry points such as `--depends` select the subject's section preset, rather than starting another resolver or inspection pipeline. |
| `graph` | Retain independently useful peer-seed, induced-set and path questions. A section-backed dependency mode can host the existing heterogeneous root-set workflow. |
| Removed verbs | Extension and implementer discovery move to subject sections; dependencies move to subject sections or the explicit root-set graph mode. Ecosystem catalog discovery stays on `ecosystem`. |

The ecosystem identity is a product-level join key between catalog knowledge,
registered candidate contributions and Integration associations. `aspire`
selects the catalog entry whose canonical identity is `ecosystem.aspire`.
Queries consume that owner-issued identity and its established correspondence
to lower-owner declarations, not a new key reconstructed from display names.
This connects the workflows without equating their meanings: selecting an
ecosystem's candidate population is not evidence that every candidate
integrates with it. Ecosystems retains catalog ownership, while the existing
population and Integration owners retain membership and evidence ownership.

A top-level verb per relation is initially easy to discover but repeats
coordinate binding, defaults, filters, scope and sharing. That is not a ban on
shortcuts: a flag or focused command can earn its place by exposing a useful
section-backed workflow. Its underlying sections remain discoverable and
queryable through the ordinary system. A shortcut must not become the only
way to access its evidence.

Conversely, forcing multi-root dependency or peer-graph questions through an
arbitrary fake subject would make the subject model worse. Removing `depends`
therefore requires both a convenient single-subject path and a complete
root-set replacement, not just renaming its type mode.

## Worked example: replace the verbs, keep the workflows

Imagine inspecting an unfamiliar Aspire application: discover the ecosystem,
find provider APIs, inspect the contracts they extend or implement, then ask
what the package and application depend on. **Before** blocks use current
command shapes; **after** blocks are proposed UX, not executable product
documentation. New section, facet and shortcut spellings are illustrative.
The existing command contracts remain authoritative until their owners adopt
these replacements.

### Discover Aspire, then inspect its APIs

Before, discover ecosystem knowledge, then name a package to search:

```console
dotnet-inspect ecosystem
dotnet-inspect ecosystem aspire
dotnet-inspect ecosystem aspire -S Integrations
dotnet-inspect find AddRedis --members --package Aspire.Hosting.Redis@13.5.3
```

After, keep the same ecosystem vocabulary entry point, then use its identity
to select the locator's candidate population:

```console
dotnet-inspect ecosystem
dotnet-inspect ecosystem aspire
dotnet-inspect ecosystem aspire -S Integrations
dotnet-inspect find --ecosystem aspire
dotnet-inspect find AddRedis --members --ecosystem aspire
```

The first three requests remain catalog inspection without package
acquisition. `ecosystem ... -S Integrations` describes configured concepts and
bindings, not an API inventory. Core/tool packages, namespace hints, demos and
availability stay discoverable on `ecosystem`. The fourth request discovers
package/library roots; the fifth finds members in that population. Those last
two requests may need bounded, source-authorized acquisition. `find` consumes
the ecosystem vocabulary rather than becoming a second catalog command.

Select the exact AddRedis overload from the locator. Its copyable subject
command retains the provider package, TFM, selected library and member anchor.
For example, the previously inspected overload can be reopened as:

```console
dotnet-inspect member Aspire.Hosting.RedisBuilderExtensions \
  --package Aspire.Hosting.Redis@13.5.3 --tfm net8.0 \
  -m AddRedis~7618364a03 -S @Relations
dotnet-inspect library Aspire.Hosting.Redis@13.5.3 --tfm net8.0 \
  -S Integration --where "ecosystem=ecosystem.aspire"
```

The provider inventory includes AddRedis and Redis resource types. On the
member, an outgoing extension-receiver row identifies
`IDistributedApplicationBuilder`; a caller search may separately find an
AppHost invocation when that local consumer is in the candidate population.
Selecting the provider package does not make it the complete caller corpus.
The catalog's `ecosystem.aspire` identity connects this semantic filter to the
earlier population selection, but the queries follow different relationships:
declared candidates for `find`, producer-classified evidence for `Integration`.

### Replace extensions and implements with focused subject views

Before:

```console
dotnet-inspect extensions HttpClient --platform
dotnet-inspect implements IDisposable --platform
dotnet-inspect implements Stream --platform
```

After locating each exact type, select its focused relation section:

```console
dotnet-inspect type HttpClient --platform System.Net.Http -S Extensions
dotnet-inspect type IDisposable --platform System.Private.CoreLib -S Implementers
dotnet-inspect type Stream --platform System.Private.CoreLib -S "Derived Types"
```

Illustrative answers include `HttpClientJsonExtensions` receiver declarations,
`MemoryStream` as an IDisposable implementation, and `MemoryStream` as a Stream
subclass. These are three evidence readings, not three coordinate grammars.
The focused sections are projections over the same owner-issued relations as
`Relations`; `-Q` discloses their applicable relation and evidence filters.
They preserve the current concrete-type and inherited-interface semantics,
not merely a filter over direct one-hop declarations.

The old examples explicitly search platform candidates. The proposed examples
pin the focus's platform library but leave relation candidates broad by default.
For the subject-continuity comparison, explicitly select the same finite
platform population on both sides; measure the extra ecosystem results
separately as the breadth experiment.

Reachable extension discovery must survive too: the replacement of
`extensions HttpClient --platform --reachable --depth 2` is the same focused
type request with `-S Extensions --reachable --depth 2`. The existing producer
still owns reachable-type selection and its depth; a generic one-hop receiver
filter is not a replacement for that workflow. View-specific traversal is
explicit and does not deepen every other Relations section.

### Replace depends without hiding the dependency question

Start with the real package already selected, rather than entering a
relationship command and resupplying that package as a root:

```console
# Before: direct declarations, then a bounded dependency graph.
dotnet-inspect depends --package Aspire.Hosting.Redis@13.5.3 \
  --tfm net8.0 -S Dependencies
dotnet-inspect depends --package Aspire.Hosting.Redis@13.5.3 \
  --tfm net8.0 --depth 2 --tree

# After: the same package, selecting evidence or graph sections.
dotnet-inspect package Aspire.Hosting.Redis@13.5.3 \
  --tfm net8.0 -S Dependencies
dotnet-inspect package Aspire.Hosting.Redis@13.5.3 \
  --tfm net8.0 -S "Dependency Graph" --depth 2 --tree
```

The declaration view retains requested version ranges and target groups. The
graph retains owner-resolved endpoints, paths and partial failures; it must
not relabel a version constraint as a resolved version. The replacement
consumes the existing dependency evidence/traversal result, not a new
package-specific approximation.

For frequent use, propose **`--depends` as shorthand for `-S @Dependencies`**
on all four subjects:

```console
dotnet-inspect package Aspire.Hosting.Redis@13.5.3 --tfm net8.0 --depends
dotnet-inspect library ./AppHost.dll --depends
dotnet-inspect type RedisResource \
  --package Aspire.Hosting.Redis@13.5.3 --tfm net8.0 --depends
dotnet-inspect member Aspire.Hosting.RedisBuilderExtensions \
  --package Aspire.Hosting.Redis@13.5.3 --tfm net8.0 \
  -m AddRedis~7618364a03 --depends
```

The category is a subject-specific outward dependency preset, not a claim that
all four subjects have package dependencies:

| Subject | Backing evidence and reading |
| --- | --- |
| Package | Package dependency graph, declarations and failures from the existing dependency operation. |
| Library | Assembly references and their supported traversal, retaining unresolved references. |
| Type | Base-type/interface dependency hierarchy; for RedisResource, its resource contract relationships. |
| Member | Outgoing static calls; for the selected AddRedis overload, its implementation calls rather than its incoming callers. This is not a complete inventory of every field, type or runtime service the member could use. |

Existing focused names such as `Dependencies`, `References` and `Calls`
remain useful. Category adoption cross-lists the relevant sections rather than
requiring another producer. The shortcut retains their disclosed depth and
evidence semantics: package graph traversal and type hierarchy traversal do
not become direct-only tables, nor do direct member calls silently become a
transitive call graph. Use an explicit graph section and its depth controls
when that is the question. Transitive graph sections are not automatically
added to the one-hop `@Relations` view.

The exact counterpart of each `--depends` request is the same command with
`-S @Dependencies`. Selecting only `Dependencies` on the package still avoids
transitive acquisition. This difference remains visible in discovery rather
than being hidden behind the convenient flag.

### Preserve the multi-root case

There is no honest single package or type for a restored project, a nuspec and
a provider package taken together. A candidate replacement uses the existing
graph host with a dependency preset:

```console
# Before.
dotnet-inspect depends \
  --project ./AppHost/AppHost.csproj --nuspec ./artifacts/App.nuspec \
  --package Aspire.Hosting.Redis@13.5.3 --tfm net8.0 \
  -S "Dependency Graph,Dependencies,Failures" --depth 2

# After: proposed section-backed root-set mode.
dotnet-inspect graph dependencies \
  --project ./AppHost/AppHost.csproj --nuspec ./artifacts/App.nuspec \
  --package Aspire.Hosting.Redis@13.5.3 --tfm net8.0 \
  -S "Dependency Graph,Dependencies,Failures" --depth 2
```

This is a traversal, not the induced-set semantics of `graph integrations`.
The dependency mode consumes the existing typed root-set request and
sectioned result. It preserves repeatable package/library/project/nuspec
roots, exclusive bounded package-prefix discovery, shared-target edges,
root-relative depth, direct-evidence-only selection, per-root failures,
exit status, and tree/Mermaid/tabular/JSON projections. A missing nuspec must
remain a failed root beside usable package evidence; no implicit restore or
build occurs.

The [Dependency inspection command owner](dependency-inspection-command.md)
currently places this operation under `depends`. Adopting the new entry point
belongs there; this worked example does not override its admission, traversal
or evidence contracts. If this mode cannot preserve them, removing `depends`
is blocked. Moving only its easy single-root cases is not retirement parity.

### Shortcuts remain part of the section system

Discovery is useful even when the first command someone learns is a shortcut:

```console
dotnet-inspect package -D @Dependencies
dotnet-inspect package -Q @Dependencies
dotnet-inspect type -D @Relations
dotnet-inspect type -Q Extensions
dotnet-inspect library -Q Integration
dotnet-inspect ecosystem aspire -D
dotnet-inspect graph dependencies -D
dotnet-inspect graph dependencies -Q "Dependency Graph"
```

These are separate discovery requests, not `-Q` combined with execution.
Help advertises a shortcut's canonical section expansion; `-D` exposes the
backing sections and `-Q` their actually supported query capabilities,
including an explicit no-operators result when appropriate. Discovery does
not acquire an ecosystem corpus or run the dependency traversal.

A shortcut lowers through the same subject/root binding, section selection,
typed query, coverage, errors and rendering as its expanded spelling.
Ordinary `--where`, projection, row limits, output formats and `--share`
therefore apply where that backing section supports them; no shortcut grants
additional capabilities or bypasses a non-projectable sharing outcome.
Conflicting explicit section selections follow the section-binding owner's
visible diagnostic rather than silently choosing one route. A future
`--extensions` shortcut could follow the same pattern if usage warrants it.
This is permission for high-value shorthand, not a requirement to recreate
every removed verb as an alias.

## One subject, a separate population

The report subject is not the set being searched for relationships.
An explicit package on `member` selects where the member lives; it must not
quietly restrict its incoming callers to that package. On `find`, an explicit
package corpus deliberately restricts the locator's candidates. The shared
plan retains these roles rather than interpreting every package option as
the same kind of scope.

| Subject | Incidence used by the relation view |
| --- | --- |
| Package | Its selected compatible library assets and their owned subjects; preserve library and member endpoints in package-level summaries. |
| Library | The exact admitted assembly and its owned types/members, not every dependency acquired to resolve it. |
| Type | The exact type and its owned members; direct type relations and member-attributed use remain distinguishable. |
| Member | The exact selected overload/member and producer-attributed body evidence; not every member of its declaring type. |

Ownership closure comes from the graph/metadata owners. It is not inferred
from namespaces, display strings or package labels. Reference surfaces may
support declaration queries; invocation queries need implementation evidence.
An absent implementation asset is unavailable invocation evidence, not zero
calls. Alternative TFMs, versions and binding contexts are separate attempts,
never a fictitious merged assembly.

Both directions are the initial relation view. They mean incoming and outgoing
incidence relative to the subject's admitted closure, not reversing an edge.
The initial view is one-hop; path depth and candidate breadth are independent.
Self-edges incident in both directions appear once. A single-subject view
does not include unrelated edges solely because both endpoints are in the
candidate population.

## Broad discovery by default

New `find` and `@Relations` operations use a Workspace with **all ecosystems
known to that product build registered**. At the design baseline those are
Platform, ASP.NET Core, Microsoft.Extensions and Aspire. It does not mean all
ecosystems or packages that exist on nuget.org.

The operator clarified three construction gestures. Names below are
illustrative, not a commitment to concrete API spelling:

| Gesture | Construction intent |
| --- | --- |
| `Workspace.Create()` | Empty host-neutral Workspace, without product curation. |
| `Ecosystem.CreatePlatformWorkspace()` | Application-curated Workspace with the platform-related ecosystems registered. |
| `Ecosystem.CreateWorkspace()` | Application-curated Workspace with all product-known ecosystems registered. |

The broad ecosystem factory is the natural default on the Ecosystems owner;
the narrower platform variant earns the qualifier. A name such as
`CreateWorkspaceWithAllEcosystems` adds little distinction.
This preserves the platform-curated policy, currently Platform, ASP.NET Core
and Microsoft.Extensions, rather than changing its meaning to include Aspire.
New `find` and Relations operations choose the broader factory. The platform
factory remains independently available to consumers that deliberately want
that smaller registration set.

Both curated gestures author registrations, not eager package acquisition or
query execution. They belong to the application Ecosystems owner and consume
the Workspace-owned handoff; raw Workspace construction remains neutral.
Discovery order is not a construction contract. Restoration is separate from
all three gestures: it preserves recorded registrations and never re-applies
either current curated manifest.

Registration, candidate selection, acquisition, admission and reporting remain
different events:

- Registration makes a pack's knowledge and declared source contributions
  available. It grants no network authority and loads no package.
- The broad sweep considers all registered candidate contributions plus the
  explicit local population under the request's source policy. Namespace hints
  and core-package priorities may rank work, not silently exclude other
  candidates or prove ownership.
- Actual discovery and analysis remain finite, capability-authorized work.
  Normal configured-source access may be used under the host's existing
  permissions; `--offline` still prohibits network. Source-content retrieval,
  decompilation and exhaustive work are not implied.
- A pack lacking an executable contribution is reported as unavailable for
  that operation, not represented by an empty successful scan.
- Explicit corpus selection narrows the sweep without erasing registrations.
  A caller-supplied empty corpus does not reactivate the broad default.

`find` opts into its locator work; selecting `@Relations` opts into the
relation domain. This does not add Relations to every subject command's
ordinary minimal view. Breadth means broad candidates and applicable relation
families, not all graph depths or every expensive producer in the tool.

Every result carries its selected populations and per-family coverage:
considered, examined, excluded, unavailable or limited, with the relevant
owner's reason. Complete means complete for that declared finite population
and supported evidence kinds. It never means every possible caller on NuGet
was found. A display row limit is not an acquisition or analysis limit.
If a work limit truncates a sweep, retained useful rows remain visibly partial;
an empty partial result cannot establish absence.

The call-graph `Everything` focal length is comparative precedent, not a
universal enum adopted by fiat. Each producer must accept an owner-backed
population with the required semantics. The current 64-Package Workspace
membership cap, compared with the 82-package Aspire curated inventory, is a
concrete prerequisite: adoption must expose the capacity boundary or provide
an owner-designed finite realization strategy. It must not silently inspect
only the first 64 and call the ecosystem complete.

### Making ecosystem selection useful in find

The target `find --ecosystem aspire` selects the pack's declared candidates;
with a member/type pattern it locates those subjects, and without a pattern
it discovers the available package/library roots rather than enumerating every
API. A namespace hint is not a replacement for a declared population.
Explicit ecosystem, prefix, package and local-library selections compose under
the Source Selection contract; the default activates only without an explicit
candidate selection.

`--ecosystem aspire` selects **where to look**.
`--where "ecosystem=ecosystem.aspire"` selects **which semantic associations
to report**. An API outside an Aspire-named package can integrate with Aspire,
and an API in such a package need not be Aspire integration currency.
The same distinction applies to prefix membership and package ownership.

## A relation dialect, not a list of ecosystems

`@Relations` is the domain door. Its general `Relations` view exposes applicable
relation evidence; its `Integration` view selects evidence carrying a
producer-issued integration association. Both are projections of the same
composition result, not separately implemented scanners.

`Integration` replaces the user-facing family of `Integration: Aspire`,
`Integration: Logging`, and similar sections after adoption. Ecosystem and
concept become discoverable facets. One fact may carry multiple associations
without becoming several physical calls or several logical relation rows.
Unclassified relations remain available in `Relations`.

For invocation evidence, the composition may attach a callee API's Integration
associations only after owner-issued correspondence joins that exact selected
member to the provider evidence in the same binding attempt. The claim is
"this caller statically invokes an API classified for this concept", not
"this application successfully configured or ran the integration".
A package reference, similar name, other overload or different version cannot
supply that join. An unavailable correspondence stays unavailable rather than
becoming an unclassified negative or an inferred invocation.

The conceptual axes are independent:

| Axis | Meaning |
| --- | --- |
| Mechanism | Type-system relationship, invocation, dependency/reference, or language-pattern candidate. |
| Relation | The precise producer-defined relationship within that mechanism. |
| Direction | Incoming/outgoing incidence at the focused subject; `both` selects their union. |
| Evidence | Declaration, static IL observation, bounded pattern candidate, or inferred opportunity. |
| Ecosystem/concept | Zero or more producer-issued semantic associations; not a population or ownership assertion. |

Initial mechanical readings:

| Mechanism | Relation readings and canonical endpoints |
| --- | --- |
| Type system | Derived type to base type; implementing type to interface; extension member to receiver; supported signature member to referenced type. |
| Invocation | Caller member to statically selected callee; constructing member to constructor. |
| Dependency/reference | Referencing library to referenced library; package to its owner-resolved dependency, retaining declaration/resolution distinctions. |
| Language pattern | Candidate type/member to an owner-issued language-pattern description, with the checked shape and remaining applicability conditions. |

This is a product vocabulary, not a replacement relationship-ID registry.
Existing `api.extension`, `metadata.reference`, `integration.observed`,
`integration.opportunity` and `call` descriptors retain their owners and
semantics. Additional graph descriptors or pattern endpoints require their
owning producer/graph adoption. An unresolved dependency declaration is not
fabricated into an exact resolved endpoint.

In particular, the existing word `observed` in an Integration descriptor must
not be rendered as evidence that an application called an API. An extension
declaration, an interface implementation, an IL call and an opportunity may
all be useful, but none substitutes for another.

The public `--where` dialect lowers to these typed facets. `-Q @Relations`
and `-Q Integration` disclose only adopted keys and values; `-D` remains
structural and does not execute the broad sweep. Filtering can avoid irrelevant
producer work when its owner proves the equivalence, but cannot turn
unexamined candidates into a completed negative result.

### What invocation and language patterns do not prove

A static `callvirt` operand is not proof of the runtime implementation.
Reflection, delegate dispatch and `calli` cannot be inferred as direct calls
merely to fill an inventory. Source language syntax is also not recoverable
from a member name or a few IL calls.

For `foreach`, `IEnumerable<T>` is one useful route, not the definition.
`Span<T>` motivates pattern enumeration, and arrays motivate lowering without
enumerator calls. The language-pattern producer must define its bounded
evidence independently of decompiler heuristics. A suitable-looking
`GetEnumerator` alone is not enough: lookup, return shape, readable `Current`,
`MoveNext`, accessibility and ambiguity affect source applicability.
Report a candidate with its conditions, not an unconditional compilation
guarantee. A user-authored method named `GetEnumerator` returning an unsuitable
shape is a required negative control.

No general C# compiler or inspected-code execution is introduced. Existing
SRM-only and Browser/Wasm contracts continue to apply. Any future stronger
applicability claim needs its own owner and enforcing evidence.

## Locate, inspect and share the same thing

A locator row must retain an owner-issued exact subject and reopening context:
package/source settlement, selected library/TFM, exact type or member identity,
and the population declaration needed to reproduce the query. A member name
with `(...)` is useful display, not an overload address. Ambiguous candidates
stay multiple rows; no automatic first-overload selection is implied.

Use existing exact acquisition coordinates, metadata anchors, resolved
inspection bases and graph identities through their established correspondence.
Do not turn the current display-oriented Find result into a new identity type
by string concatenation. The required locator handoff is an explicit
prerequisite, not a claim about today's Find rows.

The subject operation consumes the selected context directly. `--share` uses
that operation's resolved basis, retaining focus, candidate registrations,
semantic filters, evidence demand and view. It must not reconstruct state
from output or a new `relations`/`share` command.
Unsupported local inputs, private source authority or unprojectable relation
state produce the sharing owner's visible non-projectable outcome. A
nuget.org-only or unfiltered link is not a substitute for the original query.
Opening a link restores its saved registrations, not today's defaults.

## Infrastructure and owner handoffs

The new composition consumes these boundaries; this table is a prerequisite
map, not a specification of the participating components' internals.

| Owner | Existing contract / needed adoption |
| --- | --- |
| Ecosystems | [Packs](ecosystem-packs.md) author contributions; adopt an all-known-pack default for these workflows and discoverable selected populations. |
| Workspace | [Registration handoff](workspace-ecosystem-registration-handoff.md) and [scope](workspace-scope-and-expansion.md) retain inert registrations, finite realization, revision and coverage; solve the capacity boundary before claiming complete broad execution. |
| Source Selection / search binding | [Source intent](search-scope-domain.md) and [search scope](search-scope-resolution.md) preserve explicit selection, authority and bounded prefix expansion; adopt the new default and ecosystem selector in their owners. |
| Locator | [Find service](find-search-service.md) needs an exact host-neutral result/context handoff rather than its current CLI-local display rows. |
| Metadata | Hierarchy, extension, reference and signature producers must issue exact typed endpoints; name matching alone is not endpoint correspondence or general assignability. |
| Analysis | [Pair call-use](pairwise-library-call-use.md) supplies physical invocation evidence and static-target qualifications; keep Metadata-to-call-node correspondence owner-issued. |
| Integration | [Integration](integrations.md) supplies concepts, classified currency and opportunity evidence; adopt annotations on composed declaration/use evidence without redefining call semantics. |
| Dependencies | [Dependency inspection](dependency-inspection-command.md) owns the current root-set operation and section/traversal contract; adopt subject presets and a graph-host entry point before retiring `depends`. Existing package/restored-project evidence and traversal owners remain unchanged. |
| Language patterns | A focused producer must own candidate identity, checked shape and applicability limits before pattern rows can enter the view. |
| Graph / Relations composition | [Graph documents](inspection-graph-document.md) and [modes](inspection-graph-modes.md) retain canonical endpoints/occurrences; this owner selects and composes evidence relative to the focused subject and population. |
| Presentation / hosts | [Output shapes](output-shapes.md) lower one typed row set; CLI and browser consume shared results and coverage rather than inferring relations from text. |
| Workspace Definitions / sharing | [Workspace definitions](workspace-definitions.md), [sharing](cli-workspace-sharing.md) and [plan projections](inspection-plan-projections.md) retain exact portable state or refuse it. |

The host-neutral composition lives at the query layer. CLI parsing and browser
gestures lower to the same semantic request, not to calls between hosts.
Structured endpoints, relation descriptors, original occurrence evidence and
per-family coverage survive into the presentation layer.

Markout is the default common document/table/graph lowering substrate.
`Relations` and `Integration` count logical relationship rows after semantic
selection, not table labels, graph nodes or duplicated concept annotations.
Occurrence counts and inspectable physical sites remain separately accessible.
Selecting both views does not create a new combined count.

The browser may bypass Markout only for its interactive graph/navigation
presentation, consuming the same typed result at that final boundary. JSON
retains typed endpoint/evidence/coverage structure; JSONL/tabular forms project
the declared row unit and must retain or separately expose partial-result
diagnostics under the output owner's contract.

## Current evidence and analogous designs

The 2026-09-11 CLI probes used released `0.25.0+473d56a` and current-main
`8fe09457e`, before this design baseline:

| Observation | Consequence for this design |
| --- | --- |
| `library Aspire.Hosting.Redis@13.5.3 --tfm net8.0 -S "Integration: Aspire"` returns resource types and `AddRedis`; a bare copy gives the same inventory. | Preserve useful provider discovery, but do not call it consumer-use evidence. |
| A compiled Aspire AppHost calling `AddRedis` has no rows in that Integration section. Main's `graph libraries` reports `Program.<Main>$` calling the exact overload at `IL_0016`. | Composition must join distinct provider and invocation evidence. |
| Main's `ecosystem aspire -S Integrations` reports the configured Aspire binding, not concrete APIs. | Retain the ecosystem vocabulary command and its identity handoff to queries, distinct from artifact inventory. |
| Prefix discovery works, while direct prefix/curated-set Integration scope is not wired. The shipped four-package Integration graph example returns 91 relationships. | Reuse working producers and explicit-set composition; make population handoff first-class. |

Real motivating assets for implementation are
`Aspire.Hosting.Redis@13.5.3`, `System.Net.Http.Json@10.0.0`, and .NET 10
enumeration declarations in `Microsoft.NETCore.App.Ref@10.0.0`.
The first has actual prior inspection evidence above; the latter two supply
the HttpClient extension and interface/pattern test populations to pin in
adoption. Their new Relations projections remain **unverified**.

The [LSP call-hierarchy workflow](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#textDocument_prepareCallHierarchy)
first resolves an item, then asks for incoming or outgoing calls. It supports
the locator/capability separation and preserved item identity, but not claims
about our package populations or static-call completeness.
The [C# foreach specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/statements#1395-the-foreach-statement)
is the normative language reference for pattern interpretation; it demonstrates
why interface-only discovery and name-only applicability are insufficient.
These are behavioral comparisons, not imported implementations or licenses
to transfer code.

## Adoption and retirement

The [overall tracker](https://github.com/richlander/dotnet-inspect/issues/6761)
contains **15 steps**. Existing completed work may satisfy a step with evidence;
each owner files its focused implementation issue before starting. An owner
contract that needs further splitting must update the count, not hide several
unreviewable changes inside a nominal slice.

| Step | Independently owned deliverable |
| --- | --- |
| 1 | This Subject Relations workflow/composition design, [#6760](https://github.com/richlander/dotnet-inspect/issues/6760). |
| 2 | Workspace registration retention and finite population realization. |
| 3 | Ecosystems-owned platform and all-known-pack factories/manifests, preserving empty raw Workspace construction. |
| 4 | Search Scope Resolution adopts broad versus explicit candidate intent using existing Source Selection declarations. |
| 5 | Find's exact host-neutral locator/context handoff. |
| 6 | Metadata-owned typed hierarchy, extension and reference relation projections. |
| 7 | Analysis-owned invocation/correspondence joins. |
| 8 | Integration-owned semantic annotations and opportunity distinctions. |
| 9 | A focused language-pattern candidate contract and producer. |
| 10 | Shared Subject Relations query composition over adopted producers. |
| 11 | Shared typed section projection and Markout format lowerings. |
| 12 | Workspace Definitions adoption for portable relation views and locator context. |
| 13 | CLI ecosystem-to-locator handoff, subject categories, Integration view, section-backed shortcuts and dependency root-set mode, queries, sharing and focused ecosystem skill adoption. |
| 14 | Inspect Web/Browser-Wasm adoption of the same locator and relation request/results. |
| 15 | Retire `extensions`, `implements`, `depends` and per-ecosystem Integration sections after single-subject and root-set parity and disclosure; retain `ecosystem` as the vocabulary command. Coordinate existing `dependency-evidence` retirement with its owner. |

CLI adoption is step 13 and website adoption step 14; neither is optional
for this shared substrate. Step 15 is part of completion. Producers may ship
through existing hosts earlier, but neither host advertises an unimplemented
relation family or the broad-default workflow prematurely.
Stages form a dependency map, not a requirement to wait serially where owners
can close independently.

Do not update shipped skills with mock syntax now. When CLI adoption lands,
an ecosystem/relations skill should teach locate, inspect, narrow, follow
evidence, and share; it should not maintain another integration API inventory.

## Outcome gates and non-claims

This design-only slice is checked by Markdown validation and adversarial
contract review. The following production claims are **unverified** until
the named adoption gates run in Release:

| Claim | Required outcome gate |
| --- | --- |
| Exact locator continuity | Find two same-named types or overloads; reopening each preserves its package/source, target, subject and context without substitution. |
| Ecosystem identity continuity | The catalog's canonical ecosystem identity selects its declared Find population and filters its Integration associations without conflating membership with evidence. Catalog inspection remains acquisition-free. |
| Direction and evidence fidelity | One AddRedis declaration and a real caller remain separate rows; incoming/outgoing views retain the same canonical endpoints and physical call receipt. |
| Construction and broad scope | Empty, platform-curated and all-known factories retain distinct registration sets without acquisition; find/Relations use the all-known set. Unavailable/offline/budget-limited populations remain visible; an empty partial scan never reports complete absence. Exercise more than 64 candidate packages. |
| Explicit selection | A local-only or empty explicit corpus does not acquire an implicit ecosystem population; a subject's source coordinate alone does not erase broad caller scope. |
| Pattern qualification | IEnumerable/List and Span-style candidates differ correctly; unsuitable or ambiguous GetEnumerator shapes are rejected or qualified, not certified as compilable. |
| Format and host correspondence | CLI formats and browser consume identical logical edges, occurrence associations and coverage; windowing does not change query completeness or row meaning. |
| Sharing fidelity | A portable narrowed Relations view restores the same registrations, focus and filters; an unprojectable local/private case reports the actual limitation. |
| Shortcut equivalence | Each `--depends` request and its `-S @Dependencies` expansion preserve the same focus, population, selected producers, evidence, bounds, errors and output. `-D` exposes those sections; `-Q` describes only executable query bindings without running producers. |
| Retirement parity | Migrated extension/reachable-extension, implementer/subclass and type-hierarchy workflows retain their results and bounds. Single- and mixed-root dependency replacements preserve declarations, traversal, unresolved targets, partial failures, exit status and formats before their old routes disappear. |

Use the smallest real-asset and boundary fixtures proving these outcomes.
Do not harden trusted internal callers as though they were adversaries.
No new concurrency or lifetime protocol is specified here: consume existing
owner models. Any stateful realization change that needs a model belongs to
its focused owner, preserving the same issued identities in composition.

The proposal does not promise global NuGet exhaustiveness, runtime execution,
general C# overload/assignability solving, every indirect call target, automatic
source retrieval, or production deployment validation. It introduces no
platform exception or new external dependency.
