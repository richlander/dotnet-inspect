# Command Transition Model

`dotnet-inspect` has two kinds of top-level commands today:

- noun-first inspection commands such as `package`, `library`, `type`, and
  `member`;
- operation-first commands such as `diff`.

That is the current grammar, not a requirement to put every distinct operation
at the root. A user should be able to predict whether a new gesture changes
the subject, observation, operation, lens, or only the rendering.

The governing rule is:

> One transition should change one axis. Subject commands name independently
> navigable domains; their selectors identify subjects. Broad Diff, Graph, and
> Depends operations remain explicit top-level commands. A subject command may
> expose a curated invocation of one through a section, but does not add a
> `diff`, `graph`, or `depends` subject subcommand. A change in arity requires a
> distinct request and outcome contract, not inherently another command.
> Options and sections select context, observations, traversal, and projection
> without silently changing the subject.

[Operation Commands and Subject Sections](operation-command-and-subject-section-composition.md)
owns that cross-command placement. This document retains transition,
cardinality, and comparison-adoption evidence but no longer owns moving Diff
under Library, Type, or Member.

The focused
[Diff operation and subject-section adoption](#diff-operation-and-subject-section-adoption)
is tracked by
[#7703](https://github.com/richlander/dotnet-inspect/issues/7703), under
[Compare delivery #7213](https://github.com/richlander/dotnet-inspect/issues/7213).
It retains relevant comparison and completed-host-adoption requirements from
the earlier
[#7046](https://github.com/richlander/dotnet-inspect/issues/7046) proposal,
while replacing that proposal's subject-subcommand placement with
[the operation/section composition](operation-command-and-subject-section-composition.md).
The [History owner](diff-history.md) retains temporal and population-count
semantics. Full shared envelopes, complete Browser delivery, and public CLI
`--envelope` output remain part of focused Diff adoption.

The [location and result cardinality](#location-and-result-cardinality)
contract is tracked by
[#7215](https://github.com/richlander/dotnet-inspect/issues/7215). This document
owns that cross-command pattern, while each command and Workspace retain their
source, query, registration, result, and syntax internals. Its adoption is
staged one owner at a time.

The host-observable Result, Document, and owner-specific Outcome contract
proposed in
[#7055](https://github.com/richlander/dotnet-inspect/pull/7055)
owns the names and semantic extents carried as `TContent`. This adoption
consumes that contract; it does not define a competing Diff-specific content
taxonomy.

This is a composition specification. Existing top-level `diff` remains current
and is retained by the composition owner; its shared History mode has retired
the standalone `timeline` predecessor. Other root operations such as `match`,
`find`, `depends`, and `graph` are not relocated by this adoption.

Related docs:

- [Coordinate child command](coordinate-child-command.md) defines when a
  required subordinate coordinate earns a child request surface under an
  already selected subject.
- [Inspection graph modes](inspection-graph-modes.md) defines subject-first
  Graph sections and top-level Graph requests over single seeds, peer seeds,
  induced sets, Workspace participants, and paths.
- [Output Shapes](output-shapes.md) defines the
  Document → Table → Vector → Scalar ladder.
- [Host-observable content kinds](host-observable-content-kinds.md) defines
  semantic Result, Document, and owner-specific Outcome extents.
- [Output Composition](output-composition.md) separates data selection,
  filtering, and rendering.
- [Rendering Model](rendering-model.md) defines verbosity and alternate lenses.
- [Method Body Inspection](method-body-inspection.md) defines the shared member
  and IL-coordinate query model.
- [Find assembly-semantic query](find-assembly-semantic-query.md) owns decoded
  string-literal occurrence results.
- [Workspace registration and call-graph focal
  length](workspace-registration-and-call-graph-scope.md) owns inert
  exact-Library and package-prefix registration.

## Independent axes

| Axis | Question | Examples | CLI shape |
| --- | --- | --- | --- |
| Source context | Where is the subject acquired from? | package, platform, local library, restored project, TFM | Named options such as `--package`, `--platform`, `--library`, `--project`, `--tfm` |
| Focus / zoom | What structural subject is being addressed? | package artifact, library, type, member | Subject command and selector, or an existing operation-first command's focus selector |
| Point selector / coordinate | Which exact instance or point is selected within that structural scope? | overload, MethodDef token, IL offset | Positional/named selector whose identity is complete within the current scope |
| Observation / census | Which identities or facts are measured under that focus? | subject presence, child-member census, allocation sites, call sites | Section or producer descriptor such as `--finding` |
| Operation / arity | What is being done, and across how many addresses? | inspect one cell, compare two cells, correlate N cells | Top-level operation or authored subject section, plus admitted modes; separate lifecycle and outcomes do not require another command token |
| Lens / representation | Which view of the same subject and operation is wanted? | API, analysis, implementation, source, IL, versions | `-S` or focused mode options |
| Traversal policy | Which addresses are evaluated, and in what order? | `--at`, endpoints, caller-directed probes, next-probe recommendation | Operation-owned options; never implicit payload acquisition |
| Projection / rendering | How is the same content shaped for output? | fields, columns, count, URLs, printable payload, table, Markdown, JSON | Shape reducers, projectors, and writer options |

Source context is not focus. In:

```bash
dotnet-inspect type JsonSerializer --package System.Text.Json
```

`type` selects the structural focus. `--package` says where that type should be
resolved. Conversely:

```bash
dotnet-inspect package System.Text.Json
```

`package` selects the package artifact itself as the focus. The command noun and
the source option use the same domain word but play different roles.

An IL offset is a point selector into method-body facts, not a standalone
subject. It is reachable from more than one structural scope:

- `library coordinate <MethodDef>+<offset>` supplies a composite coordinate
  that is complete within the library and discovers its containing member;
- member-focused body views already have the member identity and expose the
  peer offset-scoped facts within that narrower scope.

These are two entry points into the same method-body inspection model, not
different meanings for the coordinate. Sections such as `Context: Instruction`
choose the observation/projection, while raw `IL` is a representation lens.
Neither changes coordinate identity.

The target `library coordinate` child gives the Library entry point a closed
request grammar without promoting the IL point to an independently navigable
subject or creating another method-body architecture.

This means focus is not a strict parent-child ladder. A complete coordinate may
refine a broad scope directly and still return the containing type/member
context. Intermediate focus commands remain useful navigation surfaces, but
they are not mandatory waypoints.

Focus is also not the identity family emitted by an observation. A producer may
measure the focused subject itself or a collection structurally owned by that
subject:

| Focus | Observation census | Pairwise question |
| --- | --- | --- |
| Type `T` | Type identity for `T` | Was `T` added or removed? |
| Type `T` | Member identities declared by `T` | Which members of `T` were added or removed? |
| Type `T` | Attribute occurrences applied to `T` | Which applied attributes were added, removed, or changed? |
| Member `M` | Member identity for `M` | Was this particular `M` added or removed? |
| Member `M` | Attribute occurrences applied to `M` | Which applied attributes on `M` were added, removed, or changed? |

These observations have three distinct relationships to the focused subject:

- **self presence:** whether the focused identity exists;
- **owned children:** the census of identities structurally contained by the
  focus, such as members declared by a type;
- **attached facets:** the census of occurrences applied to the focus, such as
  custom attributes.

All three may participate in the same unary, pairwise, or timeline operation
without changing focus. A conceptual "type transition" is therefore incomplete
until its observation census is named. A human default view may compose several
clearly labelled transition sections, while a focused or machine-readable query
selects one producer explicitly.

Dropping to member focus answers the particular-member questions; it cannot
replace the type-scoped member census. Attributes likewise do not require an
`attribute` focus command merely because attribute identities appear as rows. A
child or attached-facet identity row is not evidence that the command should
have zoomed to that identity. Focus defines the producer's scope and input
subject; the producer defines the Finding identity and payload family within
that scope.

## Location and result cardinality

### Claim and boundary

The governing cardinality rule is:

> Every command declares how many location coordinates one invocation admits,
> which semantic result identity families it can return, and whether each
> selected result mode is scalar or vector. These are independent dimensions.
> Unary Package, Library, Type, and Member inspection admits one location
> coordinate; Workspace owns multi-location top-level inventory; search and
> query operations admit multiple coordinates only when their operation
> contract says so.

A **location coordinate** is one hierarchical address from a source root to
the subject scope needed by the request. Depending on the command, its fields
may include:

```text
source root -> Package -> Library -> Type -> Member -> point
```

Not every coordinate uses every field. A direct local Library can be the root,
while a package-relative Library is a child field within a Package coordinate.
`--package P --library L` therefore supplies one compound coordinate: Library
`L` within Package `P`. It does not name two locations and then intersect
them.

**Location cardinality** is the number of independently resolvable coordinates
an operation plan may evaluate:

- a **single-coordinate** operation evaluates at most one exact coordinate;
- a **multi-coordinate** operation may evaluate an operation-owned population
  of exact coordinates.

This is a plan capability, not an observed count. A repeated exact-source
gesture and a bounded population selector can both produce a multi-coordinate
plan even when resolution later yields one or zero candidates. Conversely, a
range plus an exact `--at` selection is single-coordinate because the operation
may evaluate only the selected address. Expansion order, bounds, resolution,
deduplication, and acquisition remain with the operation and source owners.

Workspace is the aggregate owner for portable top-level location **inputs**,
including inert population declarations that are not yet coordinates. The
`workspace` command authors or transforms that aggregate definition, while its
inventory operation is one observation that reports the inputs without
implying that they have been expanded or evaluated. A noun operation consuming
the Workspace context and selecting one realized occurrence establishes its own
location cardinality.

**Result identity family** states what independently meaningful answers the
selected mode emits, such as Package, Library, Type, or Member. **Result
cardinality** is scalar or vector within that selected identity family. The
number of resolved locations does not determine the number of results.

This pattern does not introduce one universal coordinate, result, document, or
outcome CLR type. Source owners retain their typed identities and resolution
rules. Result owners retain their schemas, ordering, completeness, and
non-success cases. The pattern defines how commands declare and compose those
contracts.

### Complexity basis and analogous designs

Three dimensions are the smallest model that explains the current tensions
without making syntax or rendering accidental semantics:

- the same Library spelling can currently change the Type command's positional
  grammar;
- one Package location and one Library location can each produce either a
  focused answer or a child census;
- multi-package Package inspection supports a substantially smaller operation
  set than unary Package inspection; and
- output can reduce a vector to one displayed row without changing what the
  operation returned.

Collapsing any two dimensions loses a required distinction. Location count
cannot determine result count, result count cannot identify the result family,
and rendered shape cannot define either.

`kubectl get` is an analogous noun-oriented surface: namespace and resource
scope are separate from whether an exact resource name or a list is requested.
`ripgrep` and `git grep` are analogous multi-location searches: several paths
contribute search scope while the result remains one match collection. These
tools support separating location scope from result cardinality; their syntax
and resource models are evidence, not authority for this CLI.

### Coordinate fields, result selection, and filtering

Command planning keeps five roles distinct:

| Role | Meaning | Example |
| --- | --- | --- |
| Root field | Establishes the source root of one coordinate | `--package P`, direct Library path |
| Descendant field | Qualifies that coordinate below its root | Library `L` within Package `P`, Type `T` within Library `L` |
| Result-mode selector | Chooses the identity family and scalar/vector contract | exact Type lookup versus Type inventory |
| Result predicate | Narrows the selected result population | visibility, name pattern, classification |
| Projection | Changes presentation of the same semantic content | fields, columns, count, JSON |

A descendant field never adds a location. A result predicate never creates a
source or changes a scalar request into a vector request. A projection never
changes location or result cardinality.

Repeated or sibling root fields add coordinates only for a command whose
declared location cardinality is multiple. For a single-coordinate command,
supplying a second independent root is invalid even if the two roots happen to
resolve to the same bytes. Equality or deduplication after resolution does not
repair an invalid request.

Within one coordinate, each hierarchy level has zero or one selected value
unless that coordinate owner defines a typed aggregate subject. A package-wide
Type inventory may evaluate Types from a package-owned aggregate Library
subject while remaining one Package coordinate; that does not make the Type
command multi-coordinate. The aggregate identity and its completeness remain
owned by the Package/Library query.

The parser, operation plan, portable request identity, and completed content
must preserve these distinctions. Hosts do not recover them from rendered
labels, file names, assembly simple names, or the number of returned rows.

### Positional grammar is stable

Each positional slot has one command-declared role. Lexical shape must not
reassign that slot or change the role of another token.

For the Type command target:

```bash
dotnet-inspect type Cases.Widget --library ./app.dll
dotnet-inspect type Cases.Widget --package P --library L
```

`Cases.Widget` is the Type selector in both requests. In the second request,
`--package P --library L` fills one package-relative Library coordinate. A bare
`L.dll` value must not reinterpret `Cases.Widget` as a positional package merely
because it lacks a path separator. Package selection uses the command's
declared Package field.

Current routes that infer positional meaning from whether a Library value
looks like a path are migration gaps. Their adopting command must classify the
change, reserve obsolete grammar against silent rerouting, and fail visibly
when a former form cannot be interpreted under the stable slots. This pattern
does not require every command to use the same positional slots; it requires
each command's slots to retain one meaning.

### Scalar and vector results

Result cardinality is selected by the semantic gesture, not inferred from the
observed number of matches:

- an exact lookup remains scalar when it succeeds;
- an exact lookup with no valid answer returns an owner-specific typed
  non-available Outcome, not a null Result or empty vector;
- a listing or query remains vector when it returns one item;
- a complete listing or query with no matches retains its vector semantics;
  and
- a bounded or incomplete vector retains its declared cardinality and exposes
  the bound, completion state, and scoped failures required by its owner.

A command that supports both scalar and vector modes chooses the mode before
result population. It does not run a plural query and collapse a one-item
vector into a scalar, nor promote an ambiguous scalar match into a vector after
resolution.

Multi-coordinate operations select one result identity family for the
invocation. `find` returns a Type vector or a Member vector according to its
selected ordinary search mode, never an untyped mixture. Assembly-semantic
`find --literal` instead retains its owner-issued decoded-literal occurrence
family. Package Query returns a Package vector. A typed sum may be one result
identity family when the owning schema defines its closed cases; Workspace
inventory uses that approach rather than returning unrelated objects in one
bare collection.

Semantic result cardinality is not the rendered
[Document → Table → Vector → Scalar](output-shapes.md) ladder. One scalar
Result may render as a multi-section report. One semantic vector may render as
a table, JSON array, count, or one selected row without changing the completed
operation's baseline content. Content projection cannot be used to infer or
rewrite the semantic contract. The content owner separately decides whether
the completed value is a Result, Document, or owner-specific Outcome under
[Host-observable content kinds](host-observable-content-kinds.md).

### Target command classification

The target primary result contracts are:

| Operation | Location coordinates | Primary result identity | Result cardinality |
| --- | ---: | --- | --- |
| `package` inspection | One | Package | Scalar |
| `library` inspection | One | Library | Scalar |
| `type` inspection | One | Type | Scalar or vector, selected by gesture |
| `member` inspection | One | Member | Scalar or vector, selected by gesture |
| `find` | Multiple | Type or Member, selected by mode | Vector |
| `find --literal` | Multiple | Assembly-semantic occurrence | Vector |
| `package query` | Multiple | Package | Vector |
| `workspace` definition | Multiple top-level inputs; no implied evaluation | Workspace definition | Scalar |

The table classifies the command's primary semantic answer. Observations below
that focus may contain other typed populations. Package files and versions,
Library dependencies, Type members, and attached Findings do not change the
primary identity merely because they render rows.

Workspace inventory is one optional observation below the Workspace-definition
result. Its closed entry union and vector cardinality describe that observation,
not the `workspace` command's primary semantic answer.

Package version ranges remain address spaces under
[A version range is an address space](#a-version-range-is-an-address-space).
A unary inspection selects one address; a metadata version listing returns its
owner's version-address Document, not a Package vector. Pairwise and temporal
operations retain their separately declared operation arity.

### Workspace is the aggregate inventory owner

Workspace owns the experience for collecting and inventorying independently
addressable top-level inputs. The current CLI's ordered Package inventory is
the first production subset of that role. The target inventory admits, at
minimum:

- exact Package roots;
- exact-Library registrations; and
- package-prefix registrations.

These are not flattened into one accidental common identity. The Workspace
owner must preserve enough owner-issued type and identity to distinguish exact
Package content, an inert package-prefix declaration, and an exact-Library
registration. [#7219](https://github.com/richlander/dotnet-inspect/issues/7219)
and the focused
[Workspace top-level inventory](workspace-top-level-inventory.md) own the
inventory schema, state, diagnostics, and filter semantics.

Registration remains inert. Merely inventorying a package prefix does not
enumerate matching packages, and merely inventorying an exact Library does not
acquire or analyze it. A Workspace operation that realizes a registration owns
its explicit source authorization, bounds, and failure semantics.

Inventory filters select entries from the completed top-level inventory. They
do not add registrations, mutate Workspace membership, activate an occurrence,
or reinterpret a Package as a Library. Selecting one exact inventory occurrence
for drill-down establishes one coordinate for the subsequent unary
Package/Library/Type/Member operation.

The focused Workspace inventory owner must define the host-neutral completed
content, filters, document-local selection correlation, and CLI/Browser
adoption constraints. Workspace Scope and registration owners continue to
issue semantic identity. Each host owns its controls and syntax; in particular,
new direct-Library registration syntax must not overload the current Workspace
`--library` descendant selector in a way that recreates the root-versus-child
ambiguity this contract removes.

### Current behavior and migration

Several current behaviors are evidence for the target, not permanent
precedent:

| Current behavior | Target disposition |
| --- | --- |
| `package` accepts multiple positional packages and adds an outer Package column. | Move aggregate Package inventory to Workspace through [#7219](https://github.com/richlander/dotnet-inspect/issues/7219), then retire multi-package `package` through [#7221](https://github.com/richlander/dotnet-inspect/issues/7221); do not copy it into `library`. |
| Multi-package `package` rejects Library selection, versions, layout, discovery, printing, dependencies, and other unary operations. | Preserve those operations on one Package coordinate instead of growing a second constrained command mode. |
| Package-backed `library` can emit several Libraries, including all-TFM populations. | Inventory those Library occurrences through Workspace or a package-owned child census; [#7222](https://github.com/richlander/dotnet-inspect/issues/7222) makes `library` select one exact Library Result. |
| A bare `.dll` Library value can change the Type command's positional interpretation. | [#7220](https://github.com/richlander/dotnet-inspect/issues/7220) gives the Type positional slot one stable role and requires explicit coordinate fields. |
| `workspace` accepts repeatable Packages and renders an ordered Package inventory. | [#7219](https://github.com/richlander/dotnet-inspect/issues/7219) owns the focused typed top-level Package, package-prefix, and exact-Library inventory adoption. |
| Ordinary `find`, assembly-semantic `find --literal`, and Package Query resolve plural source populations. | Preserve their operation-owned multi-coordinate inputs and owner-issued homogeneous result modes. |

This is an intentional CLI transition. Adopting commands must follow
[CLI change classification](cli-change-classification.md), update help and
schema discovery, and remove obsolete forms without forwarding aliases or
silent fallback. Workspace parity for the useful aggregate scenario must land
before the corresponding noun-command route is removed.

### Disclosure, identity, and errors

Help and machine-readable discovery must disclose, in command-owned language:

- whether location cardinality is one or multiple;
- which syntax fills each root and descendant coordinate field;
- which fields may repeat and whether repetition adds coordinates;
- the available result identity families and scalar/vector modes;
- whether zero matches is an empty Document or a typed non-available Outcome;
  and
- which options are result predicates or output projections rather than
  location selectors.

Portable request and result identity retain the complete typed coordinate and
selected result mode. They do not encode meaning only in display text or infer
parentage from a file name.

Invalid combinations fail before acquisition when the contradiction is
syntactic or plan-level. Diagnostics name the violated cardinality and the
valid aggregate owner. Representative failures include:

- a second Package or direct-Library root on a unary noun command;
- two sibling Library children inside one coordinate;
- a descendant selector without an admitted parent or direct-root form;
- an option presented as a filter that would have to create another source;
  and
- an obsolete positional form whose meaning would otherwise depend on path
  spelling.

Acquisition and resolution failures remain typed owner outcomes. They are not
reported as cardinality errors merely because fewer coordinates or results
survived than requested.

### Demo

The target semantic experience separates unary inspection from aggregate
inventory. This is a contract mockup; the Workspace owner has not yet selected
new CLI syntax:

```text
package System.Text.Json@10.0.0 Markout@0.35.2
  error: package accepts one location; use Workspace inventory for multiple
         top-level inputs

Workspace top-level inputs
  Package        System.Text.Json@10.0.0
  Package        Markout@0.35.2
  PackagePrefix  Microsoft.Extensions.
  ExactLibrary   ./System.Text.Json.dll

Workspace inventory, filter Kind = ExactLibrary
  ExactLibrary   ./System.Text.Json.dll
```

Drill-down then returns to one coordinate. The exact package-relative Library
syntax belongs to [#7222](https://github.com/richlander/dotnet-inspect/issues/7222);
the semantic request is:

```text
Library coordinate
  Package  System.Text.Json@10.0.0
  Library  compile:lib/net10.0/System.Text.Json.dll

Result
  Library  System.Text.Json.dll
```

The request returns one Library Result. The existing exact Type spelling:

```bash
dotnet-inspect type System.Text.Json.JsonSerializer \
  --package System.Text.Json@10.0.0 \
  --library compile:lib/net10.0/System.Text.Json.dll
```

returns one Type Result. Omitting the exact Type selector in an admitted
Type-listing gesture returns a Type vector even when exactly one Type matches.

### Adoption and evidence

Adopt the pattern through focused owners:

1. Lock this cardinality contract.
2. [#7219](https://github.com/richlander/dotnet-inspect/issues/7219) adopts
   typed top-level inventory in the Workspace owner and delivers its
   host-neutral result to CLI and Browser consumers.
3. [#7221](https://github.com/richlander/dotnet-inspect/issues/7221) adopts
   single-coordinate Package inspection after the Workspace replacement is
   usable.
4. [#7222](https://github.com/richlander/dotnet-inspect/issues/7222) adopts
   exact scalar Library inspection after aggregate Library inventory has an
   owner.
5. [#7220](https://github.com/richlander/dotnet-inspect/issues/7220) adopts
   stable Type coordinate grammar. Other command owners adopt the pattern only
   through separately scoped issues when a concrete gap is identified.

Total steps: **5**. A later step may split into independently reviewed command
adoptions, but no command removes a current aggregate route before the
Workspace replacement for that scenario is usable.

The motivating real cases are aggregate inspection of
`System.Text.Json@10.0.0` with `Markout@0.35.2`, exact Library and Type
drill-down in `System.Text.Json@10.0.0`, and bounded `Microsoft.Extensions.`
package-prefix registration. The current multi-package Package route's reduced
operation set demonstrates why it is not the model to reproduce for Library.

Each focused adoption owns its exact implementation and gates. Pattern
conformance requires evidence for the applicable subset of:

- declared location cardinality is enforced before acquisition;
- scalar and vector modes retain their semantic cardinality for zero, one, and
  many observed matches;
- result identity remains typed and homogeneous, including closed typed sums;
- coordinate fields, result predicates, and projections do not exchange roles;
- positional roles remain stable across lexical variants of the same field;
  and
- help, discovery, portable identity, and structured output disclose the
  selected contract.

These gates are **unverified** in this specification-only change. Existing
Workspace Package inventory, exact-Library inspection, and multi-source search
tests are implementation baselines, not proof of the target cutover.

### Cardinality non-goals

This contract does not:

- choose the new Workspace registration or filter option spellings;
- redefine Workspace registration, realization, drainage, or source policy;
- require one generic result collection across Package, Library, Type, and
  Member;
- relocate Find or Package Query;
- make rendering shape determine semantic cardinality; or
- retain current plural noun-command behavior solely for compatibility.

## When a command transition is justified

A command transition is justified when one of these changes:

1. **Structural focus domain:** the addressed identity family and primary
   workflow change to another independently navigable surface. `type -> member`
   is a valid transition because a member has a different selector, identity,
   default view, and drill-in surface.
2. **Operation arity:** the acquisition topology and Outcome content change.
   Unary inspection, pairwise comparison, and N-address correlation have
   different failure semantics, backpressure, and content kinds. An explicit
   subject-owned operation or mode can express that transition without moving
   the operation to the root.
3. **Required subordinate-coordinate grammar:** the parent subject remains
   selected, but one required subordinate point establishes a coherent family
   of observations with its own useful default result.
   [Coordinate child command](coordinate-child-command.md) owns this narrower
   rule and its initial `library coordinate` adoption.

Keep the current command when only an observation producer, lens, section,
traversal choice, or output projection changes. A type-presence census and a
type-scoped member census can both participate in `diff --type T`;
`member -S IL` does not become an `il` command because it is the same member
under another representation. `--json` does not become a command; it is another
writer over the same content.

An execution lifecycle is different when at least one of these is true:

- the required inputs have a different cardinality;
- a different acquisition plan is required;
- operation outcomes have a structurally incompatible top-level schema;
- the addressed subject has a different identity model.

A coordinate child need not change the parent subject or top-level acquisition
for its exact mode. It is justified when the subordinate point is mandatory,
resolving it is itself useful or several peer observations depend on it, and
the bare child has a meaningful bounded result. A bounded population of those
points is instead a multi-coordinate operation mode and must declare its own
population, acquisition, result, and partial-failure contract. It may remain
beneath the Coordinate child when that child is the closed grammar for the
same coordinate family. A section-specific predicate, metadata-root selector,
traversal depth, row selector, or payload projection does not meet either rule.

Additional optional work does not by itself justify a command. A selected
section may authorize another scanner or network request while remaining one
lens over the same subject and arity.

## Unary inspection and explicit multi-address operations

Unary inspection remains noun-first:

```bash
dotnet-inspect package System.Text.Json@9.0.0
dotnet-inspect type JsonSerializer --package System.Text.Json@9.0.0
dotnet-inspect member JsonSerializer Serialize:1 \
  --package System.Text.Json@9.0.0
```

These commands are ergonomic spellings of the conceptual unary operation
`inspect(package|type|member)`. There is no need to add a literal `inspect`
command until it enables a concrete composition benefit.

Current multi-address operations are operation-first:

```bash
dotnet-inspect diff --package System.Text.Json@8.0.0..9.0.0 \
  --type System.Text.Json.JsonSerializer
dotnet-inspect diff --history --package System.Text.Json@8.0.0..9.0.0 \
  --type System.Text.Json.JsonSerializer --finding api.member --at all
```

The `--history` mode changes arity, acquisition, failure topology, and the
content from a pairwise Diff Document to an ordered History Document.
Top-level Diff preserves its own operation identity. A subject-first Diff
section reuses that operation without turning comparison into an ordinary
unary observation. Native temporal evidence remains owned by
[Diff History inspection](diff-history.md).

### Subject-owned API coordinate match

`--match` on `type` and `member` is an explicit pairwise operation over the
already selected API subject. It changes operation arity without changing
focus:

```bash
dotnet-inspect type System.Text.Json.Schema.JsonSchemaExporter \
  --package System.Text.Json@9.0.0..8.0.6 --match
dotnet-inspect member System.Text.Json.JsonSerializer Deserialize:1 \
  --package System.Text.Json@9.0.0..10.0.0 --match
```

The package range supplies exactly two literal endpoints in caller order.
`--match` authorizes acquisition of those two payload cells only; it does not
resolve an intermediate version population, run History, or change the default
unary behavior when omitted. `--tfm` selects one API surface, and `--all`
widens source selection from the public API to the existing IncludeAll scope.
An optional `--library` narrows the source Library only; destination Library
selection is owned by coordinate-library pairing.

The source Type query is exact. Member focus adds one source selector using the
existing name, `Name:N`, `Name~digest`, or `--index N` grammar. Generic arity,
such as `RegisterAttached<TOwner,THost,TValue>:1`, remains part of that selector
when CLI admission forms the shared request. A bare name may resolve only when
unique. This operation matches declarations, not accessor
bodies: `Foo:1`, `Foo~digest:1`, or `Foo --index 1` is refused when it selects
an accessor of a singleton Property or Event. Omit that accessor ordinal to
match the Property/Event declaration. An ordinal that selects among overloaded
indexer declarations remains valid.

`ApiCoordinateMatchCommandTests.Avalonia_GenericArityPreservesTheSelectedSource`
gates the two- and three-type-parameter Avalonia overloads through the CLI in
Release; the focused cases are PR-fast, measured below the two-second threshold
in isolation.

The source selector is not independently replayed at the destination: the
destination coordinate comes from the established Metadata correspondence and
forwarding contracts. `--all` does not filter the destination: strict native
declaration matching remains independent of ordinary accessibility changes.
The
[coordinate-library pairing](coordinate-library-pairing.md) and
[forwarded API coordinate correspondence](forwarded-api-coordinate-correspondence.md)
owners define those semantics; this section owns only CLI admission and
placement.

Admission rejects `--at`, Type/member populations, Count and row projections,
projection filters, sections, body/source/Analysis requests,
platform/project/local sources, and other rendering modes before package
acquisition. Markdown and plain text lower the typed result through its
host-neutral presentation. `--json` emits the unprojected Content, while
`--envelope` emits that identical Content with ContentKind,
PortableProjection, and diagnostics. This
operation does not reuse or relocate the root `match` command, whose subject is
implementation-clone comparison rather than cross-version API-coordinate
correspondence.

## Diff operation and subject-section adoption

Top-level `diff` remains the broad operation-first comparison command.
Package, Library, Type, and Member may expose curated Diff invocations through
sections when their resolved subjects provide the identity and context needed
by an admitted comparison. Do not add `package diff`, `library diff`,
`type diff`, or `member diff`.

This section owns Diff's adoption of the operation/section composition. Its
exact claim is:

> An operation-first Diff request and an equivalent subject-section request
> lower to the same host-neutral semantic operation and preserve the same
> Content, portable projection, diagnostics, limits, completeness, and failure.

This owner defines CLI admission, subject binding, and production sequencing.
It consumes rather than redefines comparison, correspondence, History, Count,
source, envelope, and Browser contracts. The Browser's
[Compare experience](inspect-web-compare-experience.md) is an existing
subject-first consumer, not a donor of another comparison algorithm.

### Subject and source remain distinct

| Subject binding | Meaning and initial boundary |
| --- | --- |
| Package | A section may compare genuine package facts only after a Package comparison owner defines them. A package version Count remains a metadata-only population reduction, not Diff. |
| Library | Binds existing Library/API and admitted Analysis or Implementation comparison to an already resolved Library. Package and Platform coordinates remain sources. |
| Type | Binds pairwise or admitted History comparison to one exact Type identity, including owner-issued Member projections. |
| Member | Binds declaration, body, Finding, or admitted History comparison to one exact Member identity. |

Current `diff --package P@A..B` commonly compares Libraries acquired from two
package versions. It remains a top-level Library comparison and does not become
a Package comparison merely because packages supplied the endpoints. Multiple
Libraries retain their owner-issued population and correspondence rather than
being collapsed into one invented Library.

Type and Member filters on a Library comparison remain filters. A subject
section may bind exact Type or Member identity only through that subject
owner's resolution contract; one surviving display row does not establish
identity. Legitimate endpoint absence remains comparison evidence. No section
creates a cross-source or cross-subject capability its Diff owner does not
already admit.

### Equivalent requests

The current operation-first filter route remains supported:

```bash
dotnet-inspect diff \
  --package System.Text.Json@9.0.0..10.0.0 \
  --type System.Text.Json.JsonSerializer \
  -S Changes
```

This remains a Library comparison filtered to one Type and is not the
operation-first peer of a future exact-Type section. It is the neighboring case
that prevents adoption from promoting a surviving row into subject identity.

Exact Type and Member adoption must add an operation-first request that accepts
the same owner-resolved exact subject identity as the corresponding subject
section. The operation-first syntax and section binding land together over one
semantic plan; existing `--type` and `--member` filters are not reinterpreted.
The subject supplies its resolved identity, source context, target framework,
and Workspace context. The authored section preset supplies the Diff mode,
observation, cost, and projection. Section names, exact-subject operation
syntax, and range admission land with their focused executable adoption; this
specification does not advertise them early.

Pairwise and temporal comparison remain modes of top-level `diff`. An admitted
subject History section binds the temporal mode through the shared
[History operation](diff-history.md); it does not introduce another command or
algorithm. The History owner's explicit population, evaluation, checkpoint,
Changed Versions, and Count rules remain unchanged. `--envelope` selects
service output, not comparison arity.

The [population-range rule](population-range-selection.md) is unchanged:
creating a population needs an admitted consumer. A row window cannot supply
one, and a section cannot silently authorize payload acquisition beyond its
authored operation preset.

### Envelope-complete adoption

`InspectionEnvelope<TContent>` already exists, and
`LibraryApiDiffInspection.Execute` already returns
`InspectionEnvelope<LibraryApiDiffOutcome>`. Its available case carries one
`LibraryApiDiffDocument`, retaining endpoint summaries, the existing
`ComparisonDocument<LibraryApiTypeDiff>`, verdicts, and evidence. Unavailable
and rejected execution retain their typed cases.

Reuse that terminal and complete missing operation terminals and host delivery;
do not add another envelope, universal Diff content type, or host-specific
semantic copy. The
[Library content adoption](library-api-diff-presentation.md#content-kind-adoption-in-both-hosts)
owns the Library semantic extents.

The [envelope owner](inspection-envelope.md) requires one owner-issued Content
value, required PortableProjection, and ordered typed diagnostics at the
completed shared
boundary. Operation-first and subject-section requests with equal semantic
plans receive equal baselines, including typed unavailable, rejected, or
partial outcomes. Hosts may render less information, but cannot discard it
from the delivered baseline or turn semantic failure into an empty value.

Every adopted CLI surface consumes the completed envelope internally and
supports the public `--envelope` projection defined by
[#6719](https://github.com/richlander/dotnet-inspect/issues/6719). It serializes
the already constructed baseline without another inspection, portable projection,
or host enrichment. The
[CLI output boundary](output-shapes.md#content-shapes-and-service-envelopes)
distinguishes Content-layer shapes and `--json` from service-layer
`--envelope`.

Browser adoption preserves ContentKind, Content, PortableProjection, and
diagnostic identity and order
in one identifiable received baseline. It may compose UI state and additional
owner-issued content outside that envelope. Markout remains the default CLI
lowering for typed Diff content and Count; a specialized source-text or
body-diff lowering requires its own explicit rendering boundary.

### Migration and production path

Top-level `diff` is a permanent part of the go-forward command architecture,
not a migration bridge or retirement candidate. Migration changes how its
shared operations and subject sections compose around it; it does not plan the
command's removal. Existing routes remain until their shared operation or explicit disposition is
complete. Shared History provides replacement parity for population,
evaluation, sparse and failed evidence, Count, output, discovery, and sharing
behavior; the standalone predecessor is retired without a compatibility alias
or second History algorithm.

Before changing a route, inventory existing API, multi-Library,
Type/Member-filtered, Analysis, Implementation, PDB/source, Finding
Transitions, output, and failure behavior. Preserve supported outcomes and
explicit cost gates. Any deliberate capability retirement requires its own
decision under
[CLI change classification](cli-change-classification.md).
The non-normative
[Diff command adoption census](diff-command-adoption-census.md) records the
current route, acquisition, rendering, and Release-gate baseline.

Production adoption is tracked by
[#7703](https://github.com/richlander/dotnet-inspect/issues/7703):

1. Reconcile the Diff owner with the retained top-level operation and transfer
   downstream ownership from the superseded #7126 plan.
2. Establish the complete route and rendering census with named Release
   characterization gates.
3. Adopt one pairwise Library/API operation end to end, retaining top-level
   `diff` and adding one subject-section consumer over the same envelope.
4. Adopt operation-first exact Type and Member requests together with their
   subject sections, using the same owner-resolved identity and semantic plan
   without converting filters or display text into subject identity.
5. Adopt Type and Member History and changed-version Count through the shared
   result from #7229; retire the standalone predecessor after complete parity.
6. Migrate Analysis, Implementation, PDB/source, and Finding routes in focused
   owner slices.
7. Reconcile CLI help, completion, README, skills, Share, structured output,
   release notes, and Browser/Wasm consumers as each executable adoption lands.

Total steps: **7**. Each step is independently coherent; later steps may split
further by existing comparison owner. Contextual default inference remains
with #7625, so explicit section selection can land first.

The real cases are Library Diff for
`System.Text.Json@9.0.0..10.0.0` and Type History for
`Markout.MarkoutWriterOptions` in `Markout@0.33.0..0.35.2`. Implementation
gates must cover unchanged endpoint content, non-range local pairs, explicit
mode and Count units, sparse and failed History, visible non-success, and
absence of duplicate execution for output. Cross-host adopters compare complete
envelopes and round-trip all ContentKind values, both PortableProjection arms,
and ordered typed diagnostics.

These future Release gates are **unverified** in this specification-only
change. Existing envelope and Library Diff evidence is a baseline, not proof
that subject sections or complete host migration are implemented.

## A version range is an address space

`Package@A..B` defines an immutable, inclusive, caller-directed address space.
It does not by itself authorize payload acquisition, choose a cell, compare
endpoints, or infer monotonic history.

Operations do not have to materialize that address space in the same way.
`package --versions`, addressed unary inspection, and Diff History need the
published interior vector and resolve it through `PackageVersionVector`.
`diff` needs only the two literal endpoints, so it currently acquires those
endpoints without enumerating or validating the interior vector first. Endpoint
selectors such as `#N`, `first`, and `last` are `--at` selectors over a resolved
vector; they are not valid replacements for the literal `A` or `B` in the range
syntax.

The selected lens or operation supplies the payload-acquisition contract:

| Address-space use | Evaluated payload cells | Current or intended behavior |
| --- | ---: | --- |
| Select version Vector | 0 | `package Package@A..B --versions` resolves and renders range metadata without acquiring package payloads. |
| Inspect | 1 | `type` and `member` require one explicit `--at <version\|#N\|first\|last>` and acquire only that exact package. |
| Compare | 2 | `diff --package Package@A..B` acquires and compares the two endpoints. |
| Correlate | N policy-authorized cells | `diff --history` resolves the full address space; full population evaluates every cell, repeated `--at` selects checkpoints, and `--max-probes N` bounds adaptive evaluation. |

The first row is a package lens and output-shape selection. It is not an
operation peer of `diff`.

### Acquisition cardinality versus output shape

Operation arity controls how many source addresses may be evaluated and how
many primary subject payloads may be acquired. Output shape controls how
already-selected data is projected or reduced. These cardinalities are
independent.

The result-limit gestures in this section describe historical
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677) target
behavior, not a released or implementation-ready contract. [Item and line
limits](item-and-line-limits.md) records the replacement composition and
focused-owner gaps; it defines no product syntax, behavior, or gates.

`--versions` selects a version **Vector** while retaining package focus. A bare
package's Vector is newest-first. A `Package@A..B` Vector instead preserves the
caller's endpoint direction, so `A` is row 1, `B` is the last row, and
`--at #N|first|last` and result windows address that same order. Resolving
either Vector uses registry/cache metadata and acquires zero package payloads.
The merged metadata provider is ascending and therefore oldest-first.
Both literal range endpoints must be found before any range result is returned;
an item limit cannot turn a missing far endpoint into a valid prefix.
Thereafter, selection may stop early only when provider order can determine the
requested declared rows; a reversed declared order must be materialized through
the applicable endpoint before selection. Once selected, the normal
output-shape rules apply:

| Gesture | Shape effect | Acquisition effect |
| --- | --- | --- |
| `--versions` | Select the version Vector. | Resolve version metadata; acquire zero package payloads. |
| `--count` | Reduce the selected Vector to a Scalar count. | None. Count the bounded, prerelease-filtered addresses already selected. |
| `--urls` | Project URL-bearing rows to a URL Vector. | None. Valid only if the version-row schema exposes a URL. |
| `-n N` | Select the first N rows in declared Vector order. | May stop only when provider order delivers that declared prefix; bare newest-first input must exhaust before choosing rows. |
| `-n N --tail` | Select the last N rows in declared Vector order. | May stop only when provider order delivers that declared suffix first; bare newest-first input may stop after N matching oldest rows. |
| `--rows N..M` | Select an absolute range of stable declared-order version rows. | May stop only when provider order can assign those declared addresses without unseen rows. |
| `--print` | Reject: the version row set declares no printable capability. | None. Reject during preflight without evaluating or acquiring a package payload. |
| `-n N --lines` | Clip the rendered version report to its first N lines. | None. A line window does not bound version-metadata enumeration. |

Shape reducers do not revise operation arity. In particular:

- `package Package@A..B --versions --count` means "how many addresses are in
  this bounded Vector?", not "inspect these packages and count successful
  payloads";
- `--urls` may expose registry URLs if version rows gain such a field, but it
  must not download package contents to manufacture them;
- a version row set declares no printable capability, so `--print` rejects it
  once during preflight rather than producing one failure per version. It must
  not silently transition from version-address rows to package artifact
  inspection. The explicit transition remains `package Package@version`.

The same rule applies to Diff History. `--count` reduces the already authorized
Changed Versions evidence and cannot probe additional cells; semantic
item/range composition follows
[Section-row shaping](section-row-shaping.md#count-semantics), while final CLI
conflicts remain L3-owned. `--print` can print only payloads already carried or
explicitly referenced by evaluated rows; it cannot turn unevaluated rows into
implicit acquisition.

The current package `--versions` path is implemented as a specialized early-exit
list writer, so some shared reducers and projectors are not yet honored
uniformly. That is an implementation gap against the output-shape model, not a
precedent for treating `--versions` as a separate operation or bespoke rendering
island.

`library --package` does not currently accept a package range, and an IL offset
has no independent package-range contract. If retained range navigation becomes
useful at library focus, it must adopt the same explicit one-cell address rule
as `type` and `member`; it must not pass a range through as though it were one
package version.

The same syntax can therefore participate in several commands without changing
meaning. The range always names addresses; the operation decides how many are
evaluated and what envelope is produced.

### Valid range scenarios

Package enumeration:

```bash
dotnet-inspect package System.Text.Json@8.0.0..8.0.5 --versions
```

This is a metadata view over the bounded address space. It is not aggregate
package inspection.

Unary type/member inspection within a retained range:

```bash
dotnet-inspect type JsonSerializer \
  --package System.Text.Json@8.0.0..8.0.5 --at '#4'

dotnet-inspect member JsonSerializer Serialize:1 \
  --package System.Text.Json@8.0.0..8.0.5 --at 8.0.5
```

This is useful for caller-directed onset work: the range supplies stable
addresses and `--at` selects one cell. The selected exact package reference must
replace the range in all downstream symbol, PDB, SourceLink, and source-content
acquisition.

Pairwise confirmation:

```bash
dotnet-inspect diff \
  --package System.Text.Json@8.0.4..8.0.5 \
  --type System.Text.Json.JsonSerializer \
  -S "Finding Transitions"
```

The endpoints are the two cells. For a non-default producer, the confirmation
must retain its descriptor:

```bash
dotnet-inspect diff --package Foo@1.4.0..1.5.0 \
  --type Foo.Parser --member Parse \
  --finding analysis.allocation
```

### Invalid or misleading range scenarios

An unaddressed unary range is an error:

```bash
dotnet-inspect type JsonSerializer \
  --package System.Text.Json@8.0.0..8.0.5
```

The command must not aggregate cells or silently choose `first`, `last`, or
latest. Its diagnostic should name the accepted `--at` forms.

Likewise, package artifact inspection cannot treat a range as one package:

```bash
dotnet-inspect package System.Text.Json@8.0.0..8.0.5
```

Today the range requires `--versions`. A future need to correlate package
metadata belongs in the multi-address operation, not in an implicit aggregate
package view.

`package --at` is intentionally absent. For one package artifact,
`package Package@version` is already the direct spelling. Retaining a larger
range has user-visible value for type/member navigation and correlation, but not
for a one-shot package artifact view unless that range context is itself part of
the rendered result.

## Focus transitions versus operation transitions

These transitions answer different questions.

### Focus / zoom

```text
package -> library -> type -> member
library coordinate + MethodDef/offset -> IL coordinate
member + body offset       -> IL coordinate
```

The user changes what structural thing is being addressed. Identity and schema
change; the operation remains unary inspection. The diagram shows common entry
paths, not a required sequence: the composite library coordinate can jump
directly to an IL point, while member scope can expose facts at offsets within
the selected body. Zooming to a member means selecting one member as the input
subject. It does not mean "observe the members owned by this type"; that remains
a type-focused collection census.

### Operation / arity

The current command split is:

```text
inspect -> diff -> timeline
```

The approved placement retains top-level Diff while allowing a subject section
to bind an already resolved subject:

```text
library + Diff section -> top-level Diff operation
type + Diff section    -> top-level Diff operation
member + Diff section  -> top-level Diff operation
package range + Count  -> package population reduction
```

The user keeps the source and structural focus but changes the question:

- inspect: what does the selected observation report at this address?
- diff: how does the selected observation transition between these addresses?
- history: what is known for the selected observation across this ordered
  address space?

A workflow may move on any axis, but one command transition should not hide
multiple changes. For example:

```text
type + Diff section
  -> change Diff observation, same Type and operation
  -> select Member, then its Diff section
  -> select History-compatible Diff view, same Member
```

Because the CLI is stateless, source and focus selectors must be repeated when
changing operations. A History-compatible Diff section or option makes the mode
change explicit without conflating endpoint and temporal content contracts.
The diagram describes axes, not positional argument grammar.

### Coordinate child

The exact Coordinate mode keeps the subject and unary inspection basis while
establishing one required subordinate address. File mode is a bounded
multi-coordinate operation over the same address family:

```text
library -> library coordinate <coordinate> --library <source>
        -> library coordinate --file <coordinate-population> --library <source>
```

The initial coordinate families are MethodDef token plus IL offset and metadata
heap plus offset. Instruction, member, callsite, return-address, allocation,
safety, cost, and heap-value views remain sections over that established
exact request. Raw IL remains a representation lens. File mode owns its ordered
coordinate population, acquisition plan, Document result, and coordinate-local
failure topology. The child is a CLI grammar boundary over the shared owner
queries, not another method-body or metadata architecture.

The coordinate owns the child's positional slot. Library acquisition remains
source context and therefore uses named `--library`, `--package`, or
`--platform` options, with their applicable selectors. This matches Type and
Member grammar: positional values identify what is sought, while named source
options identify where it is resolved. The bare `library` command's historical
positional source does not transfer into the child grammar.

### Graph operation placement

Graph uses the operation rule. Top-level `graph` accepts single-seed, peer-seed,
induced-set, Workspace-backed, and path requests whether or not one local
subject could also anchor the request. Package, Library, Type, and Member
commands expose curated operation-backed Graph sections over their already
resolved subject; they do not add `graph` child commands.

The two entrances preserve one semantic operation. A subject section supplies
the resolved subject and authored Graph preset, while top-level Graph receives
the equivalent seed and scope explicitly. Both lower to the same typed Graph
request and retain its relationships, occurrences, bounds, failures, and
completeness.

InspectionGraph-backed root modes construct or reopen the Workspace used for
single-seed, peer-seed, induced-set, or path questions. Another root Graph mode
may retain its producer-owned request, result, and rendering contracts when
command placement is the only shared concern.

### Selection / discovery

`match` carries a third transition on the same axis: whether the second operand
is supplied or discovered.

```text
match A B            pairwise: how do these two methods relate?
match A --similar    discovery: which methods should I match against A?
```

Both keep one source and one structural focus. `--similar` changes only the
arity of the *candidate* side, from one named member to a bounded ranked
population. It is not a different noun, so it stays under `match` rather than
becoming a `clone` command that would split one identity-agnostic workflow
between competing nouns.

The two directions compose, and the transition runs one way:

```text
match A --similar          discover ranked candidates
  -> match A B             pairwise relation for one selected candidate
  -> match A B --body             C#/IL body drill-down for that pair
```

Discovery ranks; it does not decide. A rank is a selection step, so the output
must disclose that it establishes no relation, no semantic equivalence, and no
authorship or copying claim. `--implementation` is rejected in discovery mode:
it is a pairwise drill-down and must not run for every ranked row.

The disclosure names only the transition that is actually available, and names
the image that transition must be given. A `--library` argument names exactly
one image, so the seed and the candidate population coincide in the ordinary
case: the transition is pairwise `match` against that same library, and the
printed token is the promise that it will work. The ordinary same-image
disclosure therefore retains that direct image's exact `--library` address
rather than printing only a generic instruction
(`Similar_SameImage_StillNamesThePairwiseTransition`).

They come apart only through type forwarding. When the named library forwards
the seed's type, the rows that retrieval ranks are defined by the forwarded-to
image, not by the facade the caller typed. A MethodDef token addresses a row
only in the image that owns it, so a disclosure that named the facade — or named
no image at all — would hand back an address the caller cannot resolve. The
disclosure therefore names the defining image and the exact `--library` value
that resolves the printed tokens, which keeps the pairwise transition available
rather than withdrawing it. Comparing candidates drawn from two *different*
images remains outside this command: Analysis ranks by portable structural
categories and establishes no cross-reader correspondence, and pairwise `match`
compares two methods within one retained assembly. That capability is
issue #5269, a separate effort under its own owner, not a disclosure this
command may imply it already has. Discovery enforces this rather than relying on
the shape of the ordinary case: when the seed and the candidate type resolve to
different images, the run is refused before retrieval, naming both images
(`Similar_RefusesACandidateTypeDefinedInAnotherImage`). Names are likewise
projected only from the rows an image defines, so a forwarded type can never
label a local row with a name from another assembly
(`Names_DoNotLabelALocalRowWithAForwardedTypesName`).
When a named seed addresses a forwarded type whose target is unavailable,
selection reports the retained typed resolver failure and exact target assembly
identity rather than misclassifying the valid `Type.Member` selector as
malformed
(`Similar_UnavailableForwardedSeed_ReportsTheTypedFailureAndTarget`).

The disclosed address must also still exist once the command exits. Package
extraction and cache paths are implementation details, so naming the extracted
image can satisfy every rule above while still handing back a path the caller
cannot replay. A candidate image drawn from a package is therefore disclosed as
the resolved exact package coordinate, exact package-relative asset, and TFM.
That includes the ordinary case where the package image is also the image the
caller named: the original package spelling may float to another version, so it
cannot be the replay address for a printed MethodDef token. The exact address
survives package ranges and same-named assets in other TFMs. A caller-supplied
local `.nupkg` is likewise disclosed by its canonical absolute path from the
discovery working directory rather than by a relative spelling that another
directory can reinterpret
(`Similar_RelativeLocalPackageReplayIsIndependentOfTheNextWorkingDirectory`).
User-supplied `--source`, `--add-source`, and `--nugetconfig` selectors are part
of that address because they authorize which cached producer may serve the
package offline. Explicit config paths are made absolute so the next command
does not reinterpret them against another working directory. When package
resolution used the ambient `NuGet.Config` hierarchy, the address also carries
the absolute discovery directory through `--nugetconfig-directory`; replay
discovers the same hierarchy rather than a hierarchy rooted at its later
working directory. This context is captured for every discovery source shape
that can resolve package dependencies, including a local `.nupkg` and a
directly named library whose global-cache location supplies package context
(`Similar_AmbientNuGetConfigReplayRetainsTheDiscoveryDirectory`,
`Similar_DirectCacheAndLocalPackageForwardersRetainAmbientSourcePolicy`).
Relative
local source paths are likewise disclosed as their canonical absolute paths
from the discovery working directory
(`ReplaySources_MakesRelativeLocalSourcesIndependentOfTheNextWorkingDirectory`).
The disclosure names its shell dialect and uses POSIX-shell quoting on Unix or
PowerShell quoting on Windows; it does not present one dialect as shell-neutral
(`ShellCommandQuote_UsesTheDeclaredDialect`). A source value that the URL
diagnostic policy would redact cannot be embedded in an executable disclosure;
package-backed discovery rejects that transition and directs the caller to put
the source in `nuget.config` instead of either omitting its authority or
printing credential-bearing text. When version selection narrows a wider source
set, a config-only replay remains sufficient only when package source mapping
already restricts that package to the selected producers; otherwise the
transition is rejected rather than printing the selected producer's protected
URL
(`Similar_ExactPackageReplayRetainsExplicitSourceAuthorityOffline`,
`ReplaySources_RejectAValueThatDiagnosticsWouldRedact`,
`ReplaySources_AcceptHarmlessUrlNormalization`,
`ReplaySources_MakesTheConfigPathIndependentOfTheNextWorkingDirectory`).
When range or floating resolution selects an exact version, only the sources
that reported that selected version authorize its replay. The disclosure
retains that selected producer set, not the wider source set that participated
in discovery, while preserving the original config path or config-discovery
directory for matching credentials, aliases, and mapping
(`Similar_SelectedVersionProducer_ReplayReopensTheSamePayload`,
`Similar_SelectedVersionReplayRetainsAmbientConfigDirectory`).
That restriction belongs to the package identity whose version was selected.
If a tool wrapper redirects acquisition to another package, the wrapper's
reporting sources do not transfer to the target; replay of the final package
uses its own package-specific ambient source policy
(`Similar_RangeToolWrapperReplayUsesFinalPackageSourcePolicy`).
Every exact replay selector -- package coordinate, library selector, TFM,
source, additional source, config file, and config-discovery directory -- must
also survive the output channel's required rendering containment without
changing spelling. Discovery refuses the transition when containment would
rewrite any selector, or when a selector contains the delimiter used by the
disclosure's Markdown code span, rather than emitting a command that names
another asset or renders as another command
(`Similar_PackageAssetThatCannotBeDisclosedLosslessly_IsRefused`,
`Similar_ReplaySourceContainingMarkdownDelimiter_IsRefused`).
Package-coordinate replay and forwarded dependency discovery use package
acquisition's same source-authorized, admitted cache selection. Product-owned
app-cache payloads precede ordered global-package roots, inadmissible payloads
fall through, and a global payload is eligible only when its retained producer
is authorized. Cache lookup uses NuGet's case-insensitive package-version
identity and the cache's canonical lowercase path spelling, so a mixed-case
prerelease dependency resolves the same retained archive as its exact replay.
Discovery therefore resolves forwarding against the same physical package
image that the disclosed exact replay selects; an active `NUGET_PACKAGES`
override also does not hide a retained target in the default secondary root
(`ResolveAll_SourcePolicyUsesTheSameAdmittedCachePayloadAsPackageReplay`,
`ListCachedPackageContent_UsesASecondaryGlobalPackagesRoot`,
`Similar_PackageForwarderUsesOnlyAnAuthorizedDependencyPayload`,
`Similar_DirectCacheAndLocalPackageForwardersRetainAmbientSourcePolicy`,
`Similar_PackageForwardedPopulation_DisclosesTheExactReplayAddress`,
`Similar_PackageSameImage_DisclosesTheExactReplayAddress`). An image the caller
supplied directly outlives the command and is disclosed by its canonical path
(`ReplayableCandidateAddress_ForADirectlyNamedLibrary_KeepsThePathIntact`,
`ReplayableCandidateAddress_ForAnImageOutsideTheExtraction_KeepsThePathIntact`).
When a package-backed candidate came from a relative `NUGET_PACKAGES` override,
the selected global-packages root depends on the discovery working directory
and cannot be represented by the package replay arguments. Discovery refuses
that transition and directs the caller to make the override absolute before
rerunning; it does not print an address that another directory will reinterpret
(`Similar_DirectCacheAndLocalPackageForwardersRetainAmbientSourcePolicy`).
When configured global-packages roots contain one another, package context is
classified against the most-specific containing root and an outer root whose
relative shape is not a package layout does not end the search. This retains
the package provenance and source authority required by the disclosed address
(`ResolveAll_NestedPackageRootsUseTheMostSpecificPackageContext`).

The candidate population follows the disclosure rules rather than the focus
rules. Type-scoped retrieval is the bounded default and is inferred from the
seed's declaring type; whole-assembly search changes the cost class and so
requires an explicit `--assembly-wide`. Both scopes are evaluated in the image
that defines the seed, so widening the scope can never search strictly less than
narrowing it did.

Candidate-row selection and product limits stay orthogonal. `--top` is semantic
Top over the Analysis-issued structural-similarity ranking; `-n`, `--tail`, and
strict `--rows` windows select from the same completed ranked candidate vector.
`--max-results` and `--max-methods` instead move product retrieval limits.
When discovery required `--all` to resolve a non-public seed, the disclosed
pairwise address retains `--all`; the stateless transition must be able to
resolve the same seed before it can consume the candidate token
(`Similar_NonPublicSeedDisclosureRetainsAll`).
Markdown, table, TSV, JSONL, and structured JSON consume the same selected
candidate identities, and `--count` observes that selected vector. Structured
JSON retains complete per-method outcomes, blockers, and the query-issued
receipt because those are retrieval evidence rather than candidate rows. Its
`row_selection` object records the available and selected candidate counts, so
the receipt's `returned_candidates` may truthfully exceed the length of the
selected `candidates` array. The per-method outcomes are what make the receipt's
aggregate counts attributable: a count of skipped methods that names no method
is not evidence. A retrieval that rejects, fails, or reaches a product limit
remains the visible nonzero result; candidate selection cannot replace that
failure with an unavailable-row diagnostic.

The disclosure follows the rendering rather than the format's convenience.
Markdown carries it as a paragraph and structured output as a field, but table,
TSV, and JSONL carry rows without prose, so it is written to stderr. That keeps
the obligation unconditional without corrupting a parsed stream.

The tabular formats emit exactly one row shape: the ranked candidates. Discovery
also produces a seed, a scope, a retrieval disposition, a receipt, and blockers,
and those are not candidate rows. Emitting them as extra tables would give
`--table`, `--tsv`, and `--jsonl` two or three incompatible schemas in one
stream, which the output-shape contract forbids. They travel to stderr as notes
beside the disclosure, so the parsed stream stays single-shaped while the
context remains visible. Markdown and structured output, which can carry several
shapes, keep all of it inline.

Discovery prints a metadata token on every ranked row, which is a promise that
the row is directly addressable by the pairwise transition. Honoring that
promise means the token grammar belongs to `match`'s shared selector resolution
rather than to discovery alone; a token that only discovery can read would make
the printed transition false for overloads and multi-accessor properties.

A MethodDef token is a dense table row index, not an identity, so the promise
holds only against the image that owns the row. A selector token is therefore
resolved against the one image named by `--library` and is rejected when that
image does not define it. Resolving a token against a merged surface — which
includes forwarded types whose rows live elsewhere — binds it to whichever type
collides first, which returns a confidently wrong member at exit 0 rather than a
failure. That is the one outcome this command must never produce, so the row is
range-checked against the image's MethodDef table before any comparison runs.

A selector's origin is a physical-file identity, so it is canonicalized and then
compared ordinally. The spelling arrives by two routes — a forwarded type's
defining image and a resolved type's extraction path — and `./Foo.dll` and its
absolute path are one file. Canonicalizing reconciles those routes. A token
selector contributes no third route: it is anchored to the named library by
construction, so it cannot introduce an origin the caller did not type.

Because `--library` names one image, two origins can differ only by forwarding,
which the metadata layer resolves to a real defining path. Two case-only
spellings of one file can no longer reach that comparison at all, so the
canonicalization rule needs no tie-breaking policy for them. Discovery-only
options are rejected outright on the pairwise path rather than being silently
accepted and ignored, and that rejection is raised in the parse layer as well,
so a caller who supplies one selector and a discovery flag is pointed at
`--similar` rather than asked for a second selector.

Containment is a property of the structured document, not of its callers. The
Markout row gate covers views, and a JSON document is not one, so the document
records contain their own metadata-derived strings. That includes the failure
document, whose detail is the query layer's own spelling of a missing or
ambiguous target and can carry a metadata exception's message. JSON escaping is
not containment: a parser restores the original control character, so an escaped
bidi override would reach a JSON consumer intact.

## History and bisect consequences

Pairwise Diff and Diff History are modes of the same operation over the same
source and focus selectors:

- pairwise `diff` has exactly two evaluated cells and emits pair transitions;
- `diff --history` has an ordered address space, evaluates full, checkpoint, or
  bounded adaptive cells, and emits correlation states plus transitions
  between evaluated censuses.

The most informative initial composition is not necessarily a timeline of one
type or one member. It is a type-focused timeline over a member census:

| Axis | Selection |
| --- | --- |
| Source context | A package version range |
| Focus | One type |
| Observation | The members structurally owned by that type |
| Operation | Timeline |
| Traversal | Every version in the range, or an explicit sparse subset |
| Projection | Member identity tracks and adjacent added/removed transitions |

This is intentionally between package-wide and member-specific focus. The type
provides the stable scope; each member is an observation identity. Joining the
complete member censuses by identity creates one longitudinal track per member,
so the result shows how the type's API surface evolved without requiring the
caller to name each member in advance.

The same type focus can instead select the type-presence census or the
applied-attribute census. Those timelines answer different questions and must
identify the active observation in their title/schema:

- type presence: when the type itself appeared or disappeared;
- members: when each declared member was added, removed, or re-added;
- applied attributes: when each attribute occurrence was added, removed, or
  changed.

Changing among those timelines changes the observation producer, not the focus
or operation.

Adding `--member` changes the focus from the type-owned census to one exact
member. With `--finding api.member`, the correlation selects that member's
native identity key and currently reports `Present`, `Missing`,
`SubjectAbsent`, and `Failed` cells. The shared
[Finding topology](finding-nomenclature.md#inspection-and-comparison-semantics)
also retains `NoApplicableInput` and narrows `SubjectAbsent` to proven
exact-subject absence. Diff History preserves those typed outcomes in complete
Content while its Evaluations projection gives each a distinct state. With
`analysis.allocation`, `analysis.call-site`, or `analysis.unsafety`, the
selected member is the Analysis subject and the correlated values are its
producer-native occurrence censuses.

```bash
dotnet-inspect diff --history --package Foo@1.0.0..2.0.0 \
  --type Foo.Parser --member Parse \
  --finding analysis.unsafety --at all
```

This is the cross-family composition proof: Metadata resolves the structural
member focus, Analysis supplies the selected observation census, and Findings
owns the N-address correlation. The command does not introduce a Research-owned
History model.

### Dense History

A bounded, explicit full-range traversal may evaluate every version in the
address space. Comparing each pair of adjacent complete censuses then yields
the producer's native member transitions at every boundary. This is a true
transition timeline: it can show additions, removals, and later re-additions
without assuming monotonic history.

Selecting full traversal authorizes N package-payload acquisitions and must be
explicit; merely supplying a range does not. The final syntax for that selector
is deferred, but the architecture permits it.

### Sparse timeline and bisect

A sparse traversal evaluates only caller-selected cells. It preserves the
Finding census-correlation semantics:

- `Complete`: the focused census completed, including when it contains zero
  observations;
- `SubjectAbsent`: the current presentation for either typed absence kind;
- `Failed`: inspection did not complete;
- `Unevaluated`: the address exists in the resolved vector but was not supplied
  to `FindingCensusCorrelation`.

The shared
[Finding topology](finding-nomenclature.md#inspection-and-comparison-semantics)
retains `SubjectAbsent` when the exact subject is proven absent and
`NoApplicableInput` when the subject exists without input for this producer.
This command's presentation still collapses both to `SubjectAbsent`; exposing
the distinction is a focused CLI migration. The current projection is gated by
`AnalysisTimeline_NoApplicableInputRetainsLegacySubjectAbsentPresentation`.

`Unevaluated` is a presentation state formed by joining the version vector with
the sparse correlation. It is not fabricated as a Finding or inspection
outcome.

When a projection selects one exact observation identity,
`FindingCensusCorrelation.Correlate` produces its `FindingCorrelation` track:
`Present` means the completed census contains that identity and `Missing` means
it does not. Whole-census and exact-identity states must not be combined into
one shadow cell-state model.

Probe order does not define timeline order. Positions come from the resolved
version vector in the caller's range direction; evaluated inspections retain
those positions regardless of the order in which probes were requested.

Bisect is a traversal policy over a sparse timeline, not another peer operation.
Its default behavior should recommend, not acquire. A recommendation may name
the next unevaluated midpoint and print a copyable command, but it must not
download another payload without explicit caller intent. This preserves:

- network and package-cache backpressure;
- visible retry/failure boundaries;
- the caller-owned probe budget;
- the distinction between recurrence-safe backward scanning and a binary search
  that assumes a monotonic predicate.

A sparse timeline with an unevaluated gap locates only a candidate boundary. A
dense timeline, or a sparse timeline whose evaluated cells are adjacent in the
resolved vector, may claim the exact boundary only when that adjacent census
comparison produces the producer's native `PairFinding.Added` or
`PairFinding.Removed` transition.

## Decision checklist

Before adding a command or mode, answer in order:

1. What is the structural focus and its stable identity?
2. What is the source context?
3. Is the location-coordinate cardinality one or multiple?
4. What result identity family and scalar/vector mode does the gesture select?
5. Is there a point selector, and is its identity complete within that scope?
6. Is that subordinate coordinate required by a coherent family of peer
   observations, and does its bare request have a useful bounded result?
7. What observation census runs within that focus?
8. What is the operation arity and acquisition plan?
9. Does the change require a new top-level outcome schema?
10. Is this only another lens over the same focus and operation?
11. Is this only traversal policy or output projection?
12. Does every range-consuming path state how many payload cells it may
    acquire?
13. Are unevaluated, absent, missing, and failed states kept distinct?

Question 9 does not discriminate by itself. Pair it with question 1 or 8:

- changed addressed-subject identity plus a changed schema means a focus
  transition;
- changed arity/acquisition plus a changed schema means an operation transition.
- a required subordinate coordinate plus peer observations and a useful bare
  result means a coordinate-child transition;
- a changed row schema alone may be another observation within the same focus;
- another format alone is projection.

Otherwise, prefer an option, section, or writer.

## Non-goals

This model does not:

- add an `inspect` command;
- remove existing positional shorthands by itself; a focused adoption may
  intentionally retire an ambiguous form under the CLI change contract;
- make source options global;
- add session state that implicitly carries source/focus between commands;
- authorize implicit or unbounded range scans;
- turn sections into operation substitutes;
- define operation-specific policy or projection contracts owned by focused
  designs.

The purpose is to make operation syntax derivable rather than ad hoc.
