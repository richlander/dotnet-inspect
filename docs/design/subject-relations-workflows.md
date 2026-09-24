# Subject relations: locate once, explore in both directions

## Status and authority

Proposed product workflow and composition contract, requested by the operator
on 2026-09-12. Focused design: [#6760](https://github.com/richlander/dotnet-inspect/issues/6760).
End-to-end adoption: [#6761](https://github.com/richlander/dotnet-inspect/issues/6761).
The QuerySpace-native composition reframe is tracked by
[#8124](https://github.com/richlander/dotnet-inspect/issues/8124).
The request-driven population reconciliation is tracked by
[#8184](https://github.com/richlander/dotnet-inspect/issues/8184).
Nothing in this document is a claim that the proposed commands or defaults ship.
The local-throw refinement is tracked by
[#6960](https://github.com/richlander/dotnet-inspect/issues/6960).

The Ecosystems construction prerequisite (step 3) is complete: #6786 landed in
[#6787](https://github.com/richlander/dotnet-inspect/pull/6787), followed by
issue #6791's resource-free plan factories in
[#6800](https://github.com/richlander/dotnet-inspect/pull/6800). The
[handoff owner](workspace-ecosystem-registration-handoff.md#boundary-shape)
defines the implemented API and its Release gates. This does not complete
finite population realization, CLI/browser activation, or retirement parity.

**Subject Relations composition** is the single normative owner established
here. Its exact claim is:

> Given an exact inspected subject, an explicitly described candidate
> population, and owner-issued relation evidence, compose one discoverable,
> bidirectional relation view as a QuerySpace operation exposed through subject
> sections, without losing endpoint identity, evidence meaning,
> correspondence, or coverage.

This owner defines the product questions, subject/population distinction,
relation-view semantics, and composition obligations. It does not define
acquisition, Workspace mutation or lifetime, graph identity, metadata decoding,
IL analysis, C# binding, serialization formats, or CLI parsing. Those contracts
remain with the [participating owners](#infrastructure-and-owner-handoffs).
Required changes to their current defaults and capabilities are named adoption
prerequisites, not silently implemented amendments to their designs.

The requested scope joins `find`, existing subject commands, ecosystem
discovery, relation forms, and sharing. This document proposes the workflow
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
| Which APIs accept or return a particular type shape? | Locate members by typed signature evidence, distinguishing a parameter occurrence from the declared return type and from the declaring type's interfaces. |
| Which members throw this exception type? | Locate members by typed throw-site evidence, or focus the exception type and look inward; distinguish a local throw from construction, documentation and propagation to callers. |
| Who uses this registration API? | Locate one exact overload; find incoming static call sites, not merely APIs with a similar signature. |
| What can I do with this package I already opened? | Select `@Relations` without re-entering its source, version, target, or binding context. |
| Can another person explore this result? | Share the same resolved subject, population and relation view, or report why that state is not portable. |

These questions are distinct from teaching an agent how to build a familiar
application. The earlier Aspire Redis, PostgreSQL and RabbitMQ baseline tasks
all succeeded using ordinary documentation. They did not test discovery of
relationships in unfamiliar compiled assets.

### Preserve curation, widen the questions

The current Integration experience pairs curated recognition with a specific
set of queries. That makes its baseline easy: users ask a familiar question
and get a domain-oriented answer without constructing the underlying query.
Its reach is also shaped by the questions those views were designed to ask.

The proposed experience separates curated knowledge from query composition.
Users can combine population, relation, signature position, type shape and
evidence constraints to ask questions beyond the existing curated views.
That is potentially much more powerful, but assembling even a baseline query
may take more work. Expressiveness is not automatically a usability win.

Keep the current `Integrations` section, ecosystem guidance and useful
shortcuts as curated starting points over the shared query capabilities, not
as a competing pipeline. The proposed `Integration` relation view may later
adopt that role. Preserve producer-issued semantic associations: a raw
signature predicate is not a replacement for Integration classification. Users
should be able to start with a useful curated answer, discover its query
dimensions, and narrow or extend the question without abandoning its evidence.

### Exploration checkpoints

Evaluate these hypotheses independently before broad adoption or command
retirement:

- **Subject continuity:** with the candidate population held constant, does
  locate-once plus subject relations reduce coordinate re-entry and command
  selection errors while preserving the same answers?
- **Default breadth:** with the query semantics held constant, does the broader
  registered population discover useful additional relations at an acceptable
  latency, acquisition cost and noise level?
- **Composability:** do the worked signature/middleware combinations answer
  useful questions beyond the curated baseline, without making that baseline
  disproportionately harder to construct or understand?

Use three bounded discovery scenarios: HttpClient extension providers;
interface versus pattern enumeration candidates; and an unfamiliar Aspire
provider/consumer pair. Record answer correctness and evidence provenance,
reopening accuracy, query-construction errors, command/tool work, time to useful
output, packages/bytes acquired, and incomplete-result handling. Compare
production commands with the runnable candidate flow, not an agent's memory
of how to build a sample application.
Run enough repetitions to distinguish a directional result from a single
successful attempt; do not infer reliability from one run.

#### Lightweight head-to-head

Perform a head-to-head (H2H) during runnable CLI adoption, before claiming
workflow parity or retiring the existing commands. Use the **latest production
version of dotnet-inspect available when the comparison starts** as the
baseline, rather than rebuilding an old branch or treating main as production.
Record that exact package/informational version, the candidate commit and
Release build, input package/assembly versions, selected TFMs, and runtime.
Keep those versions fixed for the comparison.

Use the three discovery scenarios above, including applicable worked
signature/return refinements. Compare both the easy curated entry point and
the additional composable questions; do not demonstrate new expressiveness
only by choosing questions that the baseline cannot answer. Let production
use its best supported combination of commands, sections, help and
version-matched skills. Several commands or manual inspection can still be
a correct baseline answer; an unavailable flag alone is not a failed task.

First hold candidate populations constant to compare subject continuity and
query composition. Then compare the default populations separately to assess
breadth, cost and noise. Both sides inspect the same versioned assets, with
the same source/network permissions and a recorded cache state. Run the sides
sequentially; distinguish acquisition and startup work from query work rather
than attributing every timing difference to the new design.

**No formal harness is required.** A command transcript, relevant outputs
and a short side-by-side results table in the adoption PR are sufficient.
For each question, report correct/incorrect/incomplete/unsupported, exact
answer coordinates and evidence, construction/reopening work, time to useful
output and acquisition cost. Compare semantic answers rather than requiring
identical table layouts or treating a larger row count as better coverage.
Preserve counterexamples and failures, not just the successful demonstration.
If agents perform the tasks, use fresh contexts, identical task text and
resource permissions, and the same model/configuration; record their tool
work as part of the result.

End with a decision for each hypothesis: demonstrated benefit, regression,
or inconclusive. A small manual H2H is directional workflow evidence, not a
statistical reliability study or a replacement for the Release correctness
gates. Comparable baseline success is a valid result and a reason to
reconsider added UX complexity. Rework a regression or explicitly resolve the
tradeoff before using the comparison to justify adoption or retirement.

The H2H is **not yet run**: this design-only slice has no executable candidate
for the proposed UX. Existing production probes establish facts about the
baseline; mockups do not count as candidate execution. Attach the actual
comparison to the relevant implementation slice when that path is runnable.

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

# Inspect the integration lens, then narrow the relation form.
dotnet-inspect library ./AppHost.dll \
  -S Integration --where "ecosystem=ecosystem.aspire"
dotnet-inspect library ./AppHost.dll \
  -S Integration --where "form=invocation"

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

Retain the broad `diff`, `graph`, and `depends` operations at the top level
under
[Operation Commands and Subject Sections](operation-command-and-subject-section-composition.md).
After replacement coverage is demonstrated, standalone relation-specific
commands such as `extensions` and `implements` may retire. Preserve their useful
workflows and evidence, not necessarily their command tokens.

| Surface | Target role |
| --- | --- |
| `vocabulary` | Discover the tool's query terms and their meanings. |
| `ecosystem` | Discover ecosystem identities, concepts/bindings and configured contributions: the ecosystem vocabulary used across queries. |
| `find` | Locate packages, libraries, types and members, with exact reopening context. Ecosystem selection narrows its candidate population. |
| Subject commands plus `@Relations` | Primary local direct-relation experience, with the subject's existing resolution and sharing path. |
| Operation-backed subject sections | Curated Diff, Graph, or Depends invocations over the already selected Package, Library, Type, or Member, using the same host-neutral operation as the top-level command. |
| Subject shortcut flags | High-value entry points such as `--depends` select the subject's direct-evidence section preset, rather than starting traversal or another resolver. |
| Top-level `graph` | Identity-preserving topology over one or more seeds, explicit participants, or a reopened Workspace. |
| Top-level `depends` | Dependency-specific direct evidence or rooted hierarchy over selected subjects and heterogeneous roots. |
| Top-level `diff` | Two-endpoint or population comparison whose correspondence and result remain Diff-owned. |
| Removed verbs | Extension and implementer discovery move to subject sections and queries after parity. Ecosystem catalog discovery stays on `ecosystem`; semantic Integration evidence remains available through Integration and Relations views. |

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

Conversely, forcing multi-root dependency, comparison, or peer-graph questions
through an arbitrary fake subject would make the subject model worse. The
top-level operations remain available whether or not an equivalent
subject-first section exists.

The same boundary applies when top-level Graph has only one seed: operation
placement does not depend on cardinality. A subject section is the
subject-first entrance; top-level Graph is the operation-first entrance. Both
consume the same typed Graph request and retain the same relationship
directions and physical evidence.

## Request-driven QuerySpace composition

Subject Relations owns one reusable relation-population contract for exact
Package, Library, Type, and Member subjects. It has no top-level `relations`
command and does not define parallel Package, Library, Type, or Member document
families. Each singular subject document adopts the population under its own
owner when that document exists. A Library relation inventory, for example, is
a requested population inside one `LibraryDocument`, not a
`SubjectRelationsContent` or `RelationsDocument`.

The portable plan describes semantic work independently from process-local
subject authority:

```text
SubjectRelationPopulationRequest
  canonical facet selection
    form
    relationship
    direction
    evidence
    Integration association
    ecosystem
    concept
  optional Count request
  optional Rows request

SubjectRelationPopulationRowsRequest
  ordering
  row projection
  maximum returned rows
  optional continuation

SubjectRelationsInspectionRequest
  exact StructuralSubjectIdentity authority
  exact candidate-population authority
  SubjectRelationPopulationRequest
```

The in-process request pairs one detached plan with the exact live focus and
candidate population. Source-specific paths, package coordinates, CLI options,
Browser DTOs, streams, readers, and rendering settings do not enter the
population plan.

Public population facets select producer work and become part of population
identity. They are not duplicated as a second set of public `request-*` keys.
A compatible row-query binding may apply residual shaping to a returned
segment, but it cannot widen acquisition, authorize another producer, change
the canonical population, or strengthen completion. Sections and convenience
commands lower their gestures to the same typed population request.

The canonical row unit is one logical relation. Each row preserves:

- exact source and target identities;
- the producer-issued relationship and evidence identities;
- direction relative to the focused subject;
- typed occurrence or match-site evidence when the producer supplies it;
- zero or more owner-issued Integration associations; and
- the correspondence needed to relate the row to its exact focus and
  candidate population.

One population result preserves its exact binding, producer availability,
failures, finite coverage, completion, and independently requested terminal
outcomes:

```text
SubjectRelationPopulationResult
  binding
  producer outcomes and coverage
  optional Count outcome
  optional Rows outcome
```

Count and Rows execute the same canonical facet selection but remain
independent terminals. Count succeeds only from exact completion or another
owner-accepted exact witness. Rows may retain a useful bounded segment while
remaining visibly incomplete. Count success does not conceal Rows failure, and
Rows success does not turn a partial observed cardinality, including zero, into
exact Count.

Rows continuation is an opaque producer-issued receipt bound to the exact
focus, candidate-population generation, canonical facets, ordering, row
projection, and next population ordinal. Maximum returned rows is a physical
segment bound, not population identity. After resolving that opaque value, the
producer retains process-local continuation authority carrying those exact
bindings; generic composition can reject invalid, stale, or incompatible
continuation without owning the producer's encoding. Settlement uses the
resolved next ordinal when checking whole-population Count against a final
continued segment. A section-row executor may shape a producer-returned segment
but must not manufacture source continuation from a previously completed
in-memory array.

Rows may contain only relationships covered by producer outcomes that are
Complete or Partial. An Unavailable or Failed producer remains visible in the
population evidence but cannot contribute successful rows merely because a
different producer was usable.

`Integration` is a named classified view over the same relation population.
Every matching row retains its producer-issued Integration associations.
Selecting the Integration view, an ecosystem, or a concept narrows the
population through those intrinsic associations; it does not run a second
scanner, infer classification from package membership, create a second source,
or change physical occurrence identity. Incomplete Integration-association
evidence remains incomplete and cannot establish that an unclassified row is a
negative match.

The completed host boundary remains the singular subject document's
`InspectionEnvelope<TOutcome>`. Step 8 defines the reusable relation population
request, binding, canonical row, terminal outcomes, and exact-subject
composition contract. It does not invent placeholder subject documents or a
temporary standalone envelope that later hosts must retire.

Curated local sections remain deliberate views rather than aliases for the
canonical row set. `Extensions`, `Implementers`, `Derived Types`,
`Dependencies`, `References`, `Calls`, and `Callers` retain their
owner-established row units, evidence details, failure behavior, and query
capabilities. Their producer evidence may also adapt into canonical logical
rows for `Relations`, but the general view does not replace the focused
result. A focused section may bind its own compatible QuerySpace row scope or
consume another retained operation such as Depends or Graph.

`@Relations` is section-category composition, not one universal query space or
row vocabulary. Structural discovery lists its members. Query discovery
expands the category and reports each exact section's accepted binding and row
unit. A predicate accepted by `Relations`, `Integration`, or `Extensions` is
not silently applied to incompatible neighboring sections; the mixed request
fails before producer execution.

The verified QuerySpace path remains usable for residual shaping of one
producer-returned row segment. It preserves typed source disposition and
completion through Rows and refuses exact Count from an insufficient source.
It does not replace the producer-owned Count/Rows request, population binding,
or continuation contract. A future gesture that truly needs one atomic request
over heterogeneous row-query scopes must first land that separately owned
QuerySpace and section-row composition support.

Depends hierarchies and Graph topology remain their owners' results. Relations
may preserve the same direct evidence and endpoint identities, but it does not
flatten rooted occurrences, graph connectedness, paths, or traversal
characteristics into one-hop logical rows.

The [Inspection Graph focus projection](inspection-graph-focus-projection.md)
is likewise separate. It projects an already-produced finite graph into
internal, exit-frontier, or target-corridor topology and may retain multi-hop
connectors to an Integration target. Subject Relations exposes direct logical
incidence at one exact subject. An Integration-classified relation row is not a
target corridor, and a corridor must not be flattened into a fabricated direct
relation.

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
The focused sections consume the same owner-issued evidence as `Relations`,
with their own candidate-selection semantics; `-Q` discloses their applicable
relation and evidence filters.
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

# After: the same package, selecting direct evidence or a rooted Depends view.
dotnet-inspect package Aspire.Hosting.Redis@13.5.3 \
  --tfm net8.0 -S Dependencies
dotnet-inspect package Aspire.Hosting.Redis@13.5.3 \
  --tfm net8.0 -S "Dependency Hierarchy" --depth 2 --tree
```

The declaration view retains requested version ranges and target groups. The
hierarchy retains owner-resolved endpoints, paths and partial failures; it must
not relabel a version constraint as a resolved version. The replacement
consumes the existing dependency evidence/traversal result, not a new
package-specific approximation.

For frequent direct evidence, propose **`--depends` as shorthand for
`-S @Dependencies`** on all four subjects:

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
| Package | Direct package declarations and their available resolution evidence. |
| Library | Direct assembly references, retaining unresolved references. |
| Type | Direct base-class and implemented-interface evidence; for RedisResource, its directly declared resource contract relationships. |
| Member | Outgoing static calls; for the selected AddRedis overload, its implementation calls rather than its incoming callers. This is not a complete inventory of every field, type or runtime service the member could use. |

Existing focused names such as `Dependencies`, `References` and `Calls`
remain useful. Category adoption cross-lists the relevant sections rather than
requiring another producer. The shortcut retains their direct evidence
semantics; it never starts package, assembly-reference, type-hierarchy, or call
traversal. Select an operation-backed section such as `Dependency Hierarchy`,
`Dependency Graph`, `Type Hierarchy`, or `Call Graph` when traversal or
topology is the question. Those sections are not automatically added to either
one-hop subject category.

The exact counterpart of each `--depends` request is the same command with
`-S @Dependencies`. Selecting that preset on a package still avoids transitive
acquisition. Operation-backed hierarchy and Graph sections remain visible in
discovery rather than being hidden behind the convenient flag.

### Preserve the multi-root case

There is no honest single package or type for a restored project, a nuspec and
a provider package taken together. The top-level Depends operation retains
that heterogeneous root set:

```console
# Before.
dotnet-inspect depends \
  --project ./AppHost/AppHost.csproj --nuspec ./artifacts/App.nuspec \
  --package Aspire.Hosting.Redis@13.5.3 --tfm net8.0 \
  -S "Dependency Hierarchy,Dependencies,Failures" --depth 2

# Target: retained operation with explicit section selection.
dotnet-inspect depends \
  --project ./AppHost/AppHost.csproj --nuspec ./artifacts/App.nuspec \
  --package Aspire.Hosting.Redis@13.5.3 --tfm net8.0 \
  -S "Dependency Hierarchy,Dependencies,Failures" --depth 2
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
retains its admission, traversal, evidence, and failure contracts. The
forward-looking section name follows
[Relationship Section Naming](relationship-section-naming.md); this worked
example does not rename the current product section.

### Shortcuts remain part of the section system

Discovery is useful even when the first command someone learns is a shortcut:

```console
dotnet-inspect package -D @Dependencies
dotnet-inspect package -Q @Dependencies
dotnet-inspect type -D @Relations
dotnet-inspect type -Q Extensions
dotnet-inspect library -Q Integration
dotnet-inspect ecosystem aspire -D
dotnet-inspect depends -D
dotnet-inspect depends -Q "Dependency Hierarchy"
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

## Find by contract and signature shape

Name search is only one way to locate an API. `--members` already changes
`find` from type-name to member-name search; contract and signature predicates
can answer questions where the useful clue is a type's role, not a method's
name. These are proposed typed queries and shortcut spellings, not additional
shipping flags:

| Proposed gesture | Result kind and question |
| --- | --- |
| `find Pattern --members` | Members whose names match the pattern; existing gesture. |
| `find --implements Interface` | Types implementing the selected interface, including owner-established inherited implementation. |
| `find --signature TypeShape` | Members whose parameter or return type structure contains that shape. |
| `find --returns TypeShape` | Members whose declared return type matches that shape, rather than merely mentioning it elsewhere. |
| `find --throws ExceptionType` | Members with owner-established local throw evidence for that exception type, not merely a construction or call to a throwing helper. |
| `find --span` | Members with `System.Span<T>` or `System.ReadOnlySpan<T>` occurrences in their signatures, for any element type. |

The signature and throws predicates imply member results; an optional positional pattern
still filters member names. An implementation predicate selects types, not
all methods on those types. Cross-kind combinations must not silently change
that result unit; any supported declaring-type/member join needs explicit
query semantics. Every result retains the exact reopening context used by
ordinary `find`.

### Span is a signature-family shortcut

```console
# Both Span<T> and ReadOnlySpan<T>, including parameter and return occurrences.
dotnet-inspect find --span

# A specific constructed shape, anywhere in the signature.
dotnet-inspect find --signature 'System.ReadOnlySpan<byte>'

# The returned shape, not an input or the result of awaiting a wrapper.
dotnet-inspect find --returns 'System.ReadOnlySpan<char>'
```

Two inspected .NET 10.0.10 APIs show the difference:

| API | Proposed match |
| --- | --- |
| `string Convert.ToHexString(ReadOnlySpan<byte> bytes)` | `--span` and the byte-span signature query; not a span-return query. |
| `ReadOnlySpan<char> MemoryExtensions.AsSpan(string? text)` | `--span` and the char-span return query, despite having no span parameter. |

`--span` should be a vocabulary-backed union over the two exact framework
type definitions, not a text search for `Span`, an alias for all ref structs,
or an allocation-free certification. `SpanLike` in a name or span use only
inside a method body does not satisfy this signature query. Concrete element
arguments remain available when narrowing the family.

### Middleware separates implementation, signature use and return shape

ASP.NET Core 10.0.10 provides these real declaration shapes:

| Declaration | What it establishes |
| --- | --- |
| `ApplicationBuilder : IApplicationBuilder` | Type implementation, not a member return. |
| `IMiddleware? IMiddlewareFactory.Create(Type middlewareType)` | A factory API returning the middleware contract. |
| `void IMiddlewareFactory.Release(IMiddleware middleware)` | A parameter occurrence of that same contract, not a factory result. |
| `IApplicationBuilder IApplicationBuilder.Use(Func<RequestDelegate, RequestDelegate> middleware)` | The pipeline contract occurs inside a parameter's generic arguments. |
| `IApplicationBuilder UseExtensions.Use(IApplicationBuilder app, Func<HttpContext, RequestDelegate, Task> middleware)` | An extension receiver, nested delegate argument types, and a builder return are separate signature facts. |
| `Task ExceptionHandlerMiddleware.Invoke(HttpContext context)` | A convention-shaped middleware entry method; its declaring type does not implement `IMiddleware`. |

Use those differences to discover APIs, then inspect the selected subjects:

```console
# Implementations of a contract versus APIs that mention it.
dotnet-inspect find --implements IApplicationBuilder --ecosystem aspnetcore
dotnet-inspect find --signature IApplicationBuilder --ecosystem aspnetcore

# Both Create and Release mention IMiddleware; only Create returns it.
dotnet-inspect find --signature IMiddleware --ecosystem aspnetcore
dotnet-inspect find --returns IMiddleware --ecosystem aspnetcore

# Builder-returning Use APIs, then their nested pipeline signature shape.
dotnet-inspect find 'Use*' --members --returns IApplicationBuilder \
  --ecosystem aspnetcore
dotnet-inspect find 'Use*' --members --signature RequestDelegate \
  --ecosystem aspnetcore

# Distinct middleware discovery routes, not equivalent inventories.
dotnet-inspect find --implements IMiddleware --ecosystem aspnetcore
dotnet-inspect find 'Invoke*' --members --signature HttpContext \
  --returns Task --ecosystem aspnetcore
```

The return filter earns its place: a signature match on `IMiddleware` includes
both factory methods, while a return match excludes `Release`. Likewise,
`Task` inside a `Use` delegate is a signature occurrence, but the `Use` method
returns `IApplicationBuilder`, not `Task`. Reference-nullability annotations
such as `IMiddleware?` remain visible without becoming another interface
identity.

Precision is not completeness. Builder-returning `Use*` APIs omit terminal
`Run` APIs that return `void`. Interface-only middleware discovery misses
`ExceptionHandlerMiddleware`. The broader `Invoke*` query produces candidates:
it does not establish the complete ASP.NET Core convention, including public
method shape, first-parameter position, constructor requirements, ambiguity
and activation dependencies.
[Convention-based middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/write?view=aspnetcore-10.0)
and [factory-based middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/extensibility?view=aspnetcore-10.0)
are the framework's distinct contracts. Any stronger middleware classification
belongs to its Integration producer, not to a generic signature match.

### Preserve the position and structure that explain a match

For these workflows, signature means the API's parameter and return type
structure, including explicit extension receivers and nested constructed type
arguments. It does not include the declaring type's interfaces, generic
constraints, attributes, locals or body calls. Matching does not recursively
expand a named delegate's `Invoke` declaration: returning `RequestDelegate`
is not itself a signature occurrence of `HttpContext`.

`--signature` can find a nested occurrence; `--returns` matches the declared
returned shape as a whole. It does not implicitly unwrap `Task<T>`, infer
assignability or substitute an interface implemented by the returned class.
Combined name, signature and return predicates constrain the same exact
member, rather than matching different overloads or sibling methods.

Results retain match sites such as return, parameter index, receiver and nested
type-argument path, alongside the exact member and type identities. One member
with several matching sites is one locator row with several reasons, not
several apparent overloads. Generic arguments, array structure and
by-reference qualifications must survive the evidence projection; shortened
rendered signatures are not the matching substrate. Ambiguous type selectors
or unavailable signature/binding evidence stay visible under the existing
owner contracts, rather than becoming fuzzy positive matches or no-match
answers.

The existing [MemberSignatureShape](member-signature-shape.md) is not this
query model: it deliberately omits ordinary returns and assembly identity for
its non-authoritative source lookup. The typed Metadata producers must supply
the needed declaration evidence and bound type correspondence. Their focused
adoption owns matching details, supported type-shape grammar and bounds; this
workflow does not introduce a general C# applicability solver or a new
signature-identity codec.

The flags follow the same shortcut rule as `--depends`. For example, a
proposed `--span` lowers to member results with a `signature-family=span`
predicate; `--signature Shape` and `--returns Shape` lower to their respective
`signature=Shape` and `returns=Shape` predicates. They do not get separate
scanners. The same predicates can filter member results within an already
selected subject without locating it again. CLI and browser consume the same
match sites, and sharing retains the predicate rather than expanding it into
a name-only search.

### Throws is body evidence, not a signature declaration

`throws` complements `returns`, but consumes a different kind of evidence.
The initial relation is **member throws exception type**, backed by
Analysis-issued local throw-site evidence. Focusing the member gives the
outgoing reading; focusing the exception type gives the incoming **thrown by**
reading without reversing the stored endpoints. An ordinary CLR method
signature does not declare a throws list.

Proposed gestures, still **mockups**:

```console
dotnet-inspect find --throws System.ArgumentNullException

dotnet-inspect find --members \
  --where "throws=System.ArgumentNullException"

dotnet-inspect type System.ArgumentNullException \
  --platform System.Private.CoreLib -S Relations \
  --throws System.ArgumentNullException --where "direction=incoming"

dotnet-inspect member System.ArgumentNullException Throw:1 --all \
  --platform System.Private.CoreLib -S Relations \
  --where "form=exception" --where "direction=outgoing"
```

The Find spellings select members with matching throw evidence; they retain
the ordinary visibility and population rules. A non-public throwing helper
requires the same explicit inclusion as any other non-public member.
The subject spellings select logical throw relations. All retain the exact
member, bound exception type, original throw occurrence and evidence limits.
Several sites supporting the same logical relation remain inspectable
occurrences, not apparent overloads or duplicated logical edges.

The first producer adoption establishes typed local throws, not an exhaustive
exception surface. A throw may be caught in the same member; its presence
does not establish escape or execution on every path. Conversely, a member
with no local throw can propagate an exception from a callee, fault a returned
task or trigger a runtime exception. Those are not inferred local throw
relations. XML `<exception>` documentation is another evidence kind, not
an IL observation. Propagated, escaping, deferred and documented exception
relations need explicit producer adoption and distinct evidence disclosure
before joining this view; they are not implied by this initial predicate.

Construction alone is not throwing. Neither a catch type, a call to an
exception constructor, a name ending in `Exception`, nor a throw count joined
to all constructed types supplies the required association. Analysis owns
the supported inference and bounds; composition consumes its typed evidence
rather than performing its own IL analysis. Unknown thrown values, unresolved
types and rethrows without an established type preserve the producer's
incomplete/unsupported outcome, not a guessed `System.Exception` target or
a complete negative match. Reference assemblies or unavailable bodies likewise
do not establish absence. Complete coverage remains scoped to the advertised
local-throw evidence and selected population, never a claim of exception safety.

`throws=E` matches an owner-established exception type using bound identity,
not display text or implicit base-type assignability. A throw of
`ArgumentNullException` does not thereby match `throws=ArgumentException`.
Type bounds or ambiguous type evidence must not be upgraded to exact targets.
`--throws E` lowers to that same predicate; it does not launch another scanner
or change `--signature` to include body facts.

### How shortcut flags and --where combine

A flag can be a **compound shortcut: which results + where constraint**.
For example, on `find`, `--returns Foo` selects member results and requires
their declared return type to match `Foo`. The proposed equivalence is:

```console
dotnet-inspect find --returns Foo

dotnet-inspect find --members --where "returns=Foo"
```

The same Find-result decomposition applies to signature and throws shortcuts:

| Shortcut | Which results | Where constraint |
| --- | --- | --- |
| `--returns Foo` | Members | `returns=Foo` |
| `--throws E` | Members | `throws=E` |
| `--signature Foo` | Members | `signature=Foo` |
| `--span` | Members | `signature-family=span`, the Span/ReadOnlySpan union |

This is a query-meaning expansion, not a requirement to rewrite command-line
text. One flag can supply several parts of the shared declarative query
without introducing a separate execution path. Population selection remains
distinct: `--ecosystem` chooses where to look, not which semantic associations
to report.

For these proposed Find member queries, the short flags and `--where` contribute
to **one typed request**. Expand the shortcuts, then combine the constraints
with **AND** on the same exact result. Neither spelling takes precedence;
argument order cannot change the question. A shortcut's default result-kind
selection is part of its advertised expansion, not a second execution mode.

These two proposed requests mean the same thing:

```console
dotnet-inspect find 'Use*' --returns IApplicationBuilder \
  --where "signature=RequestDelegate" --ecosystem aspnetcore

dotnet-inspect find 'Use*' --members \
  --where "returns=IApplicationBuilder" \
  --where "signature=RequestDelegate" --ecosystem aspnetcore
```

Both require a member named `Use*` that returns `IApplicationBuilder` and has
a `RequestDelegate` occurrence in its signature. The ecosystem selector
still chooses the candidate population. The return and signature predicates
do not have to match the same type occurrence, but they must match the same
member; one overload cannot supply the return while another supplies the
parameter.

Similarly, `--where` can refine a useful family shortcut:

```console
# Span or ReadOnlySpan somewhere in the signature, AND a string return.
dotnet-inspect find --span --where "returns=string"

# Fully expanded spelling.
dotnet-inspect find --members \
  --where "signature-family=span" --where "returns=string"
```

This includes `Convert.ToHexString(ReadOnlySpan<byte>)` but excludes
`MemoryExtensions.AsSpan(string?)`. The family itself means Span **OR**
ReadOnlySpan; the extra return predicate narrows that union rather than
replacing it.

| Combination | Proposed meaning |
| --- | --- |
| `--signature HttpContext --where "signature=RequestDelegate"` | Both shapes must occur in one member's signature; they may occupy different sites. Repeating a facet is not implicitly OR. |
| `--returns Task --where "returns=Task"` | Redundant equivalent constraints; no extra rows or duplicated match evidence. |
| `--returns Task --where "returns=IApplicationBuilder"` | Both constraints apply. These distinct exact returned types cannot both match, so the result is empty, not last-option-wins. Coverage still determines whether that empty result is complete. |
| `--returns string --where "throws=FormatException"` | One exact member must both return string and have matching local throw evidence. Its return and throw may be different evidence sites; another overload or a throwing callee cannot supply the match. |
| `--throws ArgumentNullException --where "throws=FormatException"` | One member needs both exception-type matches, possibly at different throw sites. Unlike incompatible exact return types, two thrown types are not a contradiction. |
| A member-only predicate combined with a type implementation result | Unsupported without an explicit declaring-type/member join; report the incompatible result kind rather than quietly reinterpreting the request. |
| A malformed shape or unsupported predicate/operator | A visible binding diagnostic, not a successful empty scan or an ignored option. |

An empty answer to a valid conjunction is different from invalid syntax.
No new constraint solver is required to prove every contradiction before
execution. OR is available only through an explicitly described family or
an adopted query operator, not through argument order or choosing the short
versus long spelling. These rules specify the proposed signature and throws predicates;
other query families keep their owner-defined combination rules.

Section shortcuts such as `--depends` select a section preset instead of
adding a predicate. `--where` then constrains the supported query within that
selection. A preset does not grant new filter bindings: incompatible section
and predicate combinations are diagnosed, not silently dropped. Neither
kind of shortcut changes source authority, work bounds or the meaning of
incomplete coverage.

`find -D Results` exposes the result shape. `find -Q Results` should disclose
the adopted predicate keys, required result kind, combination rules and
shortcut equivalents; command help advertises the same expansions, and
`vocabulary` explains the signature family and its alternatives. Discovery is
a separate request, not `-Q` combined with execution flags. This keeps the
easy gesture teachable while exposing how to build a more specific query.

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

The operator clarified three construction gestures. The
[registration handoff](workspace-ecosystem-registration-handoff.md) now returns
resource-free plans; a caller explicitly constructs `InspectionWorkspace(plan)`
when it needs a live owner:

| Gesture | Construction intent |
| --- | --- |
| `new WorkspacePlan()` | Empty host-neutral plan, without product curation. |
| `EcosystemPackCatalog.CreatePlatformWorkspacePlan()` | Application-curated plan with the platform-related ecosystem registrations. |
| `EcosystemPackCatalog.CreateWorkspacePlan()` | Application-curated plan with all product-known ecosystem registrations. |

The broad ecosystem factory is the natural default on the Ecosystems owner;
the narrower platform variant earns the qualifier. A name such as
`CreateWorkspacePlanWithAllEcosystems` adds little distinction.
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
with a member/type pattern or an explicit contract/signature/throws predicate it
locates those subjects. Without either, it discovers the available
package/library roots rather than enumerating every API. A namespace hint is
not a replacement for a declared population.
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

### Per-subject section catalog

This is the complete proposed initial membership of `@Relations`. `Yes` means
authored category membership for that subject kind, not that every inspected
subject has rows. `No` means the named section is not in `@Relations` for
that subject; it does not remove an independently available section. Relevant
evidence can still appear in the general `Relations` view over that subject's
owned closure. These are adoption targets, not claims about today's section
registration.

| Section | Package | Library | Type | Member | Relation forms | Focus-relative reading |
| --- | --- | --- | --- | --- | --- | --- |
| `Relations` | Yes | Yes | Yes | Yes | All applicable forms | Both incoming and outgoing incidence; the general one-hop relation view. |
| `Integration` | Yes | Yes | Yes | Yes | All applicable forms with Integration associations | Both directions, retaining classification and evidence rather than inferring successful integration. |
| `Dependencies` | Yes | No | No | No | `package-dependency` | Outgoing direct package declarations and their available resolution evidence. |
| `References` | No | Yes | No | No | `assembly-reference` | Outgoing direct assembly references; unresolved references remain visible. |
| `Baseclass` | No | No | Yes | No | `base-type` | The selected Type's directly declared base class. |
| `Interfaces` | No | No | Yes | No | `interface` | The selected Type's directly declared interfaces. |
| `Extensions` | No | No | Yes | No | `extension` | Incoming extension-provider matches for the focused receiver type. |
| `Implementers` | No | No | Yes | No | `interface` | Incoming implementation candidates for the focused interface, under the existing concrete/inherited-match policy. |
| `Derived Types` | No | No | Yes | No | `base-type` | Incoming subclass candidates for the focused base type, including owner-established indirect matches. |
| `Calls` | No | No | No | Yes | `invocation`, `object-creation` | Outgoing direct static call evidence from the selected member. |
| `Callers` | No | No | No | Yes | `invocation`, `object-creation` | Incoming direct static call-site evidence reaching the selected member. |

For example, `package X -D @Relations` describes `Relations`, `Integration`
and `Dependencies`; `type T -D @Relations` describes `Relations`,
`Integration`, `Baseclass`, `Interfaces`, `Extensions`, `Implementers` and
`Derived Types`.
Structural discovery describes membership even where a focused question is
inapplicable to the particular target. Effective discovery and execution
retain the owning section's applicability, no-match and unavailable outcomes.
`-Q @Relations` describes adopted query bindings, not invented common operators
for every member of the category.

Candidate sections do not change the general view's one-hop contract.
An inherited interface match or indirect subclass match may need hierarchy
evidence, but it must not be mislabeled as a direct declaration edge.
Preserve the producer's actual endpoints and match explanation rather than
retargeting an edge to the focus. Likewise, reachable extension discovery
requires its explicit `--reachable` gesture; category selection does not
enable it.

`form` and `relation` are facets, not rules for creating a section per value.
Signature uses, throws and qualified pattern candidates remain available through
`Relations` and its filters; a `--span` predicate does not introduce a `Span`
section, and `--throws` does not introduce an `Exceptions` section.
The current member `Signature` view, Find `Results`, ecosystem
catalog sections and vocabulary sections do not become category members
merely because these workflows use them.

Selecting `@Relations` selects its distinct sections, so a curated section
may summarize evidence also present in `Relations`. It is not a union whose
rows should be summed across sections. Select `-S Relations` for one general
logical-edge inventory or a focused section for that question. Focused
sections retain their owner-defined row units and call-site detail; the
general view does not force every evidence table into one graph-row schema.

### Overlap with Dependencies and explicit graph views

`@Dependencies` is the outward dependency preset selected by `--depends`.
Its overlap with `@Relations` is section cross-listing, not a second query
implementation. The complete proposed preset for each subject is the union
of the two middle columns:

| Subject | In both `@Relations` and `@Dependencies` | In `@Dependencies` only | Operation-backed section |
| --- | --- | --- | --- |
| Package | `Dependencies` | None | A Dependency hierarchy section invokes Depends; a Dependency graph section invokes Graph. |
| Library | `References` | None | A Reference hierarchy section invokes Depends; a Reference graph section invokes Graph. |
| Type | `Baseclass`, `Interfaces` | None | `Type Hierarchy` invokes Graph over base-type and interface topology. |
| Member | `Calls` | None | `Call Graph` invokes Graph over transitive call topology; `Callers` belongs only to `@Relations`. |

Both categories therefore remain direct evidence views. Selecting
`@Dependencies` never requests a Graph document or transitive acquisition.
Every selected direct view still retains mandatory failure, coverage, and
exit-status behavior without adding a traversal-specific section. Combining
categories selects a shared section once; it does not duplicate rows or change
query or evidence meaning.

### Integration classification

The proposed `Integration` relation view supersedes the current
observed-currency `Integrations` section after adoption. Ecosystem and concept
remain discoverable facets. One fact may carry multiple associations without
becoming several physical calls or several logical relation rows. Unclassified
relations remain available in `Relations`.

For invocation evidence, the composition may attach a callee API's Integration
associations only after owner-issued correspondence joins that exact selected
member to the provider evidence in the same binding attempt. The claim is
"this caller statically invokes an API classified for this concept", not
"this application successfully configured or ran the integration".
A package reference, similar name, other overload or different version cannot
supply that join. An unavailable correspondence stays unavailable rather than
becoming an unclassified negative or an inferred invocation.

### Relation forms and query facets

The conceptual axes are independent:

| Axis | Meaning |
| --- | --- |
| Form | How the relation is expressed: interface, base type, extension declaration, signature, exception, invocation, object creation, reference/dependency, or pattern. |
| Relation | The precise producer-defined connection expressed in that form, such as implements, accepts, returns, throws or calls. |
| Direction | Incoming/outgoing incidence at the focused subject; `both` selects their union. |
| Evidence | Declaration, static IL observation, bounded pattern candidate, or inferred opportunity. |
| Signature site/shape | Where a referenced type occurs in a member declaration, preserving parameter/return role and constructed shape; not proof of interface implementation or invocation. |
| Ecosystem/concept | Zero or more producer-issued semantic associations; not a population or ownership assertion. |

Initial relation forms and readings (query spellings remain proposed):

| Form | Relation readings and canonical endpoints |
| --- | --- |
| `interface` | Implements: implementing type to interface. |
| `base-type` | Inherits: derived type to base type; indirect candidate matches retain their supporting hierarchy evidence. |
| `extension` | Extends: extension member to receiver type. |
| `signature` | Accepts or returns: member to the referenced type, retaining position and constructed shape. |
| `exception` | Throws: throwing member to the exception type, retaining Analysis-issued local throw occurrences and qualifications, not implying escape to callers. |
| `invocation` | Calls: caller member to statically selected callee. |
| `object-creation` | Constructs: constructing member to constructor. |
| `assembly-reference` | References: referencing library to referenced assembly, preserving unresolved declaration evidence. |
| `package-dependency` | Depends on: package to its owner-resolved dependency, retaining declaration/resolution distinctions. |
| `pattern` | Candidate type/member to an owner-issued language-pattern description, with the checked shape and remaining applicability conditions. |

Discovery describes the facet as **relation form**; the query key is `form`.
For example, `form=signature` can be narrowed by an accepts/returns relation,
while `form=invocation` selects call evidence. Form is separate from evidence
kind: a pattern candidate does not become a declaration or a runtime fact
because it is included in the same domain.

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

### Query discovery for the sections

**The proposed `-Q Relations` advertises `--returns` and `--throws`**, alongside
their canonical predicates and their meanings for relation rows. Query
discovery must answer both "what can I filter?" and "which convenient spelling
can I use?" A shortcut is advertised for a particular command/section
binding, not globally just because its option name exists.

The [query-discovery owner](progressive-disclosure.md#query-discovery) retains
the acquisition-free `-Q` contract and the `Query: <Section>` companion.
This adoption needs its descriptors to disclose the result unit, predicate
meaning, operators, value domain, combination rules, and shortcut expansion.
Those facts must come from the accepted binding, not a separate help-only
registry. The following output is a **mockup of the target**, not a claim
that today's CLI accepts these predicates.

```console
dotnet-inspect type -Q Relations
```

**Query: Relations** (result unit: logical relation rows).

Direction default: both.
Composition: AND on each relation; explicit family alternatives are OR.

| Predicate | Operators | Meaning / values | Shortcut |
| --- | --- | --- | --- |
| `form` | `=` | Relation forms such as `interface`, `signature`, `exception`, `invocation`; enumerate adopted values. | None |
| `relation` | `=` | Producer-issued relation IDs and their readings, such as implements, accepts, returns, throws or calls. | None |
| `direction` | `=` | `incoming`, `outgoing`, `both`, relative to the subject closure. | None |
| `evidence` | `=` | Adopted declaration, static IL, pattern-candidate or opportunity evidence kinds. | None |
| `ecosystem` | `=` | Canonical ecosystem association IDs, such as `ecosystem.aspire`. | None; `--ecosystem` selects a population instead. |
| `concept` | `=` | Producer-issued Integration concept IDs. | None |
| `signature` | `=` | Keep signature relations whose referenced type occurrence matches the supplied shape. | `--signature Shape` |
| `returns` | `=` | Keep return relations whose whole declared returned shape matches the supplied shape. | `--returns Shape` |
| `throws` | `=` | Keep local throw relations whose owner-established exception type matches the supplied bound type; not an escaping-exception summary. | `--throws ExceptionType` |
| `signature-family` | `=` | Keep signature relations matching an adopted type family; `span` means Span/ReadOnlySpan. | `--span` for `span` |

The real descriptor must enumerate supported IDs or identify their owned
vocabulary; the abbreviated value descriptions above are not new relation-ID
registries. Named shape predicates retain bound type identity and match-site
evidence. `returns=Foo` does not match merely because `Foo` occurs inside a
returned `Task<Foo>` or appears in a parameter.

For the selected relation section, the short and long spellings are:

```console
dotnet-inspect type IApplicationBuilder \
  --platform Microsoft.AspNetCore.Http.Abstractions \
  -S Relations --returns IApplicationBuilder

dotnet-inspect type IApplicationBuilder \
  --platform Microsoft.AspNetCore.Http.Abstractions \
  -S Relations --where "returns=IApplicationBuilder"
```

Both retain return relations such as
`UseExtensions.Use -> IApplicationBuilder`; they do not retain that method's
extension-receiver relation just because the method also returns the
requested type. They do not turn the result into a member inventory.

The explicit section supplies **which**, and its shortcut supplies **where**.
The Find member-result expansion earlier is the default for that Find
workflow, not a mandatory `--members` rewrite on every command. Explicit
compatible section selection is resolved before defaults, irrespective of
argument order; an incompatible selection is diagnosed rather than replaced.

| Query context | What `--returns Foo` keeps |
| --- | --- |
| Find member `Results` | Members whose declared return type matches `Foo`, with their match evidence. |
| `Relations` | Return-relation rows whose declared returned shape matches `Foo`. |
| `Integration` | The same return-relation rows, still restricted to producer-classified Integration evidence and any selected ecosystem/concept associations. |
| `Extensions` | Extension-member candidates whose declared return shape matches `Foo`; the receiver-match requirement remains in force. |

This row-unit distinction also governs conjunctions. Find can require two
signature shapes at different sites on one member. Relation predicates
constrain one logical relation; they do not combine different edges from
the same member to manufacture a match. A query for a callee that returns
`Foo` is not automatically a return relation, either: signature filtering
must not silently become a join over invocation targets.

The corresponding `--throws E` expansion keeps matching members in Find,
throw-relation rows in `Relations`, independently classified throw-relation
rows in `Integration`, and extension-member candidates with matching throw
evidence in `Extensions`. Extension receiver matching remains required.
Integration classification must be producer-issued for that evidence; merely
residing in an ecosystem package does not classify a throw.

Find and extension-member candidates can combine `returns=Foo` with
`throws=E` on the same member. In the logical-relation views that conjunction
has no matching row: a return edge and a throw edge are different relations,
even when their source member is the same. Likewise two distinct exact
`throws` types can match one member through separate sites but cannot match
one logical member-to-type edge. Coverage still qualifies an empty result.
No implicit cross-edge or caller-propagation join is introduced.

#### Initial discovery coverage by section

The following table defines the initial predicate adoption for every section
in the proposed category. Existing owner-adopted capabilities are retained;
"no new predicates" does not remove formatting, row selection or explicit
traversal options.

| Section | Query predicates introduced here | Shortcuts |
| --- | --- | --- |
| `Relations` | The ten predicates in the mockup, with logical-relation semantics. | `--signature`, `--returns`, `--throws`, `--span` |
| `Integration` | The same predicates, with the classified-evidence condition always retained. | `--signature`, `--returns`, `--throws`, `--span` |
| `Extensions` | `signature`, `returns`, `throws`, `signature-family` over extension-member candidates. | `--signature`, `--returns`, `--throws`, `--span` |
| `Implementers`, `Derived Types` | No new predicates; describe any adopted type-candidate bindings, or explicitly report no query operators. | None; member signature/throws predicates are not type-candidate predicates. |
| `Dependencies`, `References` | No new predicates; describe the owning evidence view's adopted bindings, or explicitly report no query operators. | None |
| `Calls`, `Callers` | No new predicates; describe the owning call view's adopted bindings, or explicitly report no query operators. | None; do not imply a callee-signature join. |

Operation-backed Graph sections retain the Graph owner's discovery contracts;
this design does not invent `--where` bindings for Graph-document columns.
Where a known section has no adopted operators, a named request such as
`type -Q Implementers` still identifies the section and explicitly says
**no query operators**. An unknown section remains an error. Bare `-Q` lists
only query-capable sections, under the existing discovery contract.

`-Q @Relations` expands the subject's category and reports these capabilities
**per section**, not as an unlabeled union of keys or shortcuts. Fixed form,
direction and candidate restrictions remain part of each section's
description. For example, `--returns` is not advertised for `Implementers`
merely because it is available for the neighboring `Relations` section.
Consequently, a type request combining all of `@Relations` with `--returns`
is incompatible with its type-candidate sections: ask for `Relations`,
`Integration` or `Extensions` explicitly. Do not silently drop sections or
leave some of their rows unfiltered.
The same incompatibility applies to `--throws`; it is not a filter on
`Implementers`, `Calls` or `Callers` merely because those sections share a
category with `Relations`.

The same descriptor supplies the shortcut spelling, canonical predicate and
contextual expansion to help and `-Q`; a displayed shortcut must actually bind
in that section. `-D` describes membership and fields rather than promising
that every displayed field is filterable. A section predicate is advertised
only after its producer, binding and output path are adopted. Merely landing
this design must not make future flags appear in production discovery.

`-Q` does not acquire a supplied target, expand candidate populations, decode
signatures or run any relation producer. It cannot combine with `-S`, `-D`
or execution options such as `--where`, `--returns` and `--throws`. Request discovery,
then run a separate inspection. Markout remains the common metadata lowering
path; this is richer section capability disclosure, not another query engine.

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
| Locator | [Reverse Type-Declaration Locator](reverse-type-declaration-locator.md) proposes the exact finite-population type-declaration query; its [adoption map](reverse-type-locator-adoption.md) tracks the source/context and host prerequisites. The current [Find service](find-search-service.md) remains CLI-local; member/signature locator adoption is separate. |
| QuerySpace | [Query Operation Infrastructure](query-operation-infrastructure.md) registers exact-subject Relations routes; [Query Space Composition](query-space-composition.md) supplies population and continuation semantics; [section-row shaping](section-row-shaping.md) may apply compatible residual shaping to producer-returned segments. The relation owner defines one typed population request with independent Count and Rows, exact binding, source disposition/completion, and producer-issued continuation. |
| Metadata | Hierarchy, extension, reference and signature producers must issue exact typed endpoints. Signature discovery additionally needs parameter/return roles, constructed shapes and match sites; name matching alone is not endpoint correspondence or general assignability. |
| Analysis | [Pair call-use](pairwise-library-call-use.md) supplies physical invocation evidence and static-target qualifications; keep Metadata-to-call-node correspondence owner-issued. [Local-throw evidence](analysis-local-throw-evidence.md) owns member/type/site associations and visible evidence limits. Existing [throw counts and constructed-exception signals](graph-signal-annotations.md#exception-risk) are not that projection. |
| Integration | [Integration](integrations.md) supplies concepts, classified currency and opportunity evidence; adopt annotations on composed declaration/use evidence without redefining call semantics. |
| Dependencies | [Dependency inspection](dependency-inspection-command.md) owns the retained top-level Depends operation, direct evidence, rooted hierarchy, and heterogeneous asset roots. Subject sections adopt curated Depends or Graph invocations without retiring `depends`; existing package/restored-project evidence and traversal owners remain unchanged. |
| Language patterns | A focused producer must own candidate identity, checked shape and applicability limits before pattern rows can enter the view. |
| Graph / Relations composition | [Graph documents](inspection-graph-document.md) and [modes](inspection-graph-modes.md) retain canonical endpoints/occurrences; this owner selects and composes direct evidence relative to the focused subject and population. [Graph focus projection](inspection-graph-focus-projection.md) separately owns internal, exit-frontier, and target-corridor topology without flattening connectors into direct relation rows. |
| Presentation / hosts | [Output shapes](output-shapes.md) lower one requested population result; CLI and browser consume the same subject document, binding, terminals, continuation, and coverage rather than inferring relations from text. The [query-discovery owner](progressive-disclosure.md#query-discovery) must adopt section-specific result-unit and shortcut disclosure from the same accepted bindings. |
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

The 2026-09-12 signature probes used the same released `0.25.0+473d56a`
against installed .NET/ASP.NET Core 10.0.10 implementation assemblies:
`System.Private.CoreLib`, `Microsoft.AspNetCore.Http.Abstractions`,
`Microsoft.AspNetCore.Http` and `Microsoft.AspNetCore.Diagnostics`.
They confirmed the declarations in the signature-shape worked example, not
execution of the proposed Find predicates. For example, existing commands
reproduce the factory and nested-delegate evidence:

```console
dotnet-inspect type IMiddlewareFactory \
  --library "$ASP_NET_10_0_10/Microsoft.AspNetCore.Http.Abstractions.dll"
dotnet-inspect member UseExtensions \
  --library "$ASP_NET_10_0_10/Microsoft.AspNetCore.Http.Abstractions.dll" \
  -m Use -S "Member Index"
```

Here `ASP_NET_10_0_10` is the installed runtime directory reported by
`dotnet --list-runtimes`, including its `10.0.10` version subdirectory.
Retain those real API shapes as adoption fixtures; new signature discovery
and its shortcut equivalence remain **unverified**.

The 2026-09-14 throw probes used released `0.25.0+473d56a` against .NET
10.0.10 `System.Private.CoreLib`. These executable baseline commands expose
the real local-throw versus helper-call boundary:

```console
dotnet-inspect member System.ArgumentNullException Throw:1 --all \
  --platform System.Private.CoreLib --framework runtime@10.0.10 -S IL
dotnet-inspect member System.ArgumentNullException ThrowIfNull:1 --all \
  --platform System.Private.CoreLib --framework runtime@10.0.10 -S IL
```

`Throw(string)` constructs `ArgumentNullException` at `IL_0001` and throws
at `IL_0006`. The selected `ThrowIfNull` overload calls `Throw(string)` at
`IL_0009` and has no local throw. The latter can propagate the exception but
is not a match for the initial local-throw predicate. Preserve this real
runtime boundary in the Analysis adoption, together with construction-only,
locally caught and unknown-type controls. These IL probes are evidence about
the input, not execution of the proposed `throws` query.

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
contains **19 steps** after the QuerySpace reframe. Existing completed work may
satisfy a step with evidence; each owner files its focused implementation issue
before starting. An owner contract that needs further splitting must update the
count, not hide several unreviewable changes inside a nominal slice.

| Step | Independently owned deliverable |
| --- | --- |
| 1 | **Complete:** original Subject Relations workflow/composition design, [#6760](https://github.com/richlander/dotnet-inspect/issues/6760) and #6763. |
| 2 | QuerySpace-native Subject Relations composition reframe, [#8124](https://github.com/richlander/dotnet-inspect/issues/8124). |
| 3 | Workspace registration retention and finite population realization. Registration retention is complete; realization and honest coverage remain. |
| 4 | **Complete:** Ecosystems-owned platform and all-known-pack factories/manifests (#6786, #6787; plan-factory adoption #6791, #6800), preserving empty raw Workspace construction. |
| 5 | **Complete:** Search Scope Resolution broad-versus-explicit candidate intent (#6931, #6932). |
| 6 | Find's exact host-neutral locator/context handoff, including CLI and Browser reopening. |
| 7 | **Complete substrate:** Query Operation registration, typed row binding, source disposition/completion through Rows, and Count exactness enforcement (#7712, #8007, #8042, #8073, #8139). The request-driven Library population in #8333, #8385, and #8397 additionally establishes independent Count/Rows requests, exact population binding, producer-issued continuation, and intrinsic classification facets. |
| 8 | Subject Relations population request, Package/Library/Type/Member exact-subject composition, canonical logical-row shape, population binding, independent Count/Rows outcomes, producer coverage, and Integration-association selection. This step does not define placeholder subject documents or `SubjectRelationsContent`. |
| 9 | Metadata-owned hierarchy, extension, reference, and signature adapters, including constructed shapes and return/parameter match sites. |
| 10 | Analysis-owned invocation and exact correspondence adapters. |
| 11 | Local-throw relation adapter consuming the **complete** typed local-throw producer (#6961, #6992) without expanding its evidence claim. |
| 12 | Integration-owned annotations and opportunity distinctions over exact composed evidence. |
| 13 | A focused language-pattern candidate contract, producer, and relation adapter. |
| 14 | Subject Relations section projection, per-subject `@Relations` membership and cross-listing, exact-section QuerySpace discovery, and Markout lowerings. |
| 15 | Workspace Definitions adoption for portable relation views and locator context, retaining typed predicates and evidence meaning. |
| 16 | CLI ecosystem-to-locator handoff, local sections, shortcuts, query discovery, sharing, and focused ecosystem/relations skill adoption. This consumes the operation/section placement in #7623 and retains top-level Find, Depends, and Graph. |
| 17 | Inspect Web/Browser-Wasm adoption of the same locator, QuerySpace request, content, Share outcome, diagnostics, typed evidence, and coverage. |
| 18 | Lightweight production-versus-candidate H2H over the three real discovery scenarios. |
| 19 | Retire `extensions`, `implements`, and superseded Integration-specific surfaces after population, relation, evidence, failure, format, discovery, sharing, and both-host parity. Retain top-level `diff`, `graph`, `depends`, and ecosystem vocabulary. Preserve the dependency owner's completed `dependency-evidence` retirement. |

CLI adoption is step 16 and website adoption step 17; neither is optional
for this shared substrate. Step 19 is part of completion. Producers may ship
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
| Signature discovery fidelity | ToHexString's byte-span input and AsSpan's char-span return differ correctly. Factory Create/Release differ by return versus parameter; Use retains its nested delegate sites without claiming to return Task. Combined predicates apply to one member, repeated sites do not duplicate it, and unavailable evidence stays visible. The flags and section predicates yield the same results in CLI and browser. |
| Throw discovery fidelity | The real ArgumentNullException.Throw helper matches its exact exception type; ThrowIfNull's call alone does not. Construction-only and catch-only controls do not match; a locally caught throw does not claim escape. Unknown/rethrow type evidence and absent bodies stay visibly incomplete/unsupported. Member-return/throw conjunctions use one exact member; edge conjunctions never stitch its separate relations together. Incoming and outgoing views retain identical endpoints/sites; CLI and browser agree on matches, shortcut discovery, coverage and portable restoration. |
| Ecosystem identity continuity | The catalog's canonical ecosystem identity selects its declared Find population and filters its Integration associations without conflating membership with evidence. Catalog inspection remains acquisition-free. |
| Direction and evidence fidelity | One AddRedis declaration and a real caller remain separate rows; incoming/outgoing views retain the same canonical endpoints and physical call receipt. |
| Construction and broad scope | Empty, platform-curated and all-known factories retain distinct registration sets without acquisition; find/Relations use the all-known set. Unavailable/offline/budget-limited populations remain visible; an empty partial scan never reports complete absence. Exercise more than 64 candidate packages. |
| Partial Rows and Count exactness | A bounded producer returning some rows retains those rows with typed incomplete source evidence; a bounded producer returning zero rows cannot establish absence. Rows preserves each disposition and completion outcome. Count returns no cardinality unless the source is exact or supplies an owner-accepted exact witness; observed partial counts, including zero, produce the typed non-count outcome. |
| Explicit selection | A local-only or empty explicit corpus does not acquire an implicit ecosystem population; a subject's source coordinate alone does not erase broad caller scope. |
| Pattern qualification | IEnumerable/List and Span-style candidates differ correctly; unsuitable or ambiguous GetEnumerator shapes are rejected or qualified, not certified as compilable. |
| Format and host correspondence | CLI formats and browser consume identical logical edges, occurrence associations and coverage; windowing does not change query completeness or row meaning. |
| Sharing fidelity | A portable narrowed Relations view restores the same registrations, focus and filters; an unprojectable local/private case reports the actual limitation. |
| Shortcut equivalence | Each `--depends` request and its `-S @Dependencies` expansion preserve the same focus, population, selected direct producers, evidence, errors, and output without requesting Graph traversal. `-D` exposes those sections; `-Q` describes only executable query bindings without running producers. |
| Section catalog and traversal disclosure | The four subject catalogs match the membership and overlap tables. Shared category sections select once. Neither category requests Graph traversal; Package, Library, Type, and Member topology begins only through an operation-backed Graph section or an explicit top-level Graph request. Equivalent subject-first and operation-first requests preserve the same typed Graph request and result. Focused inherited matches retain their evidence rather than masquerading as direct edges. |
| Section query discovery | Named and category `-Q` report actual per-section bindings, result units and accepted shortcut expansions without target acquisition or producer work. Return filtering keeps member rows in Find, return edges in Relations and classified return edges in Integration; it does not retain unrelated edges or switch an explicit view. Unsupported categories/bindings fail visibly, and known sections without operators say so. |
| Predicate composition | Mixed flags/`--where` and their expanded forms agree regardless of order. Span-family OR remains inside the AND with a string return; repeated signature shapes match one member, duplicate constraints do not duplicate evidence, contradictory return predicates give an honestly scoped empty result, and invalid bindings fail visibly. |
| Retirement parity | Migrated extension/reachable-extension, implementer/subclass and type-hierarchy workflows retain their results and bounds before those relation-specific commands disappear. Retained top-level Depends and operation-backed subject sections preserve declarations, traversal, unresolved targets, partial failures, exit status, and formats; equivalent requests produce equivalent semantic results, and adopting a subject section does not retire `depends`. |

Use the smallest real-asset and boundary fixtures proving these outcomes.
Do not harden trusted internal callers as though they were adversaries.
No new concurrency or lifetime protocol is specified here: consume existing
owner models. Any stateful realization change that needs a model belongs to
its focused owner, preserving the same issued identities in composition.

The proposal does not promise global NuGet exhaustiveness, runtime execution,
general C# overload/assignability solving, every indirect call target, automatic
source retrieval, or production deployment validation. It introduces no
platform exception or new external dependency.
