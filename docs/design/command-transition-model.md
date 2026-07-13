# Command Transition Model

`dotnet-inspect` has two kinds of top-level commands today:

- noun-first inspection commands such as `package`, `library`, `type`, and
  `member`;
- operation-first commands such as `diff`.

That split is useful, but only if transitions between commands follow an
explicit model. A user should be able to predict whether a new gesture changes
the subject, the observation, the operation, the lens, or only the rendering.

The governing rule is:

> One transition should change one axis. Change commands when the independently
> navigable subject domain or operation arity changes; use selectors for points
> within an established domain, and options or sections for context,
> observations, lenses, and projection.

Related docs:

- [Output Shapes](output-shapes.md) defines the
  Document → Table → Vector → Scalar ladder.
- [Output Composition](output-composition.md) separates data selection,
  filtering, and rendering.
- [Rendering Model](rendering-model.md) defines verbosity and alternate lenses.
- [Method Body Inspection](method-body-inspection.md) defines the shared member
  and IL-coordinate query model.

## Independent axes

| Axis | Question | Examples | CLI shape |
| --- | --- | --- | --- |
| Source context | Where is the subject acquired from? | package, platform, local library, restored project, TFM | Named options such as `--package`, `--platform`, `--library`, `--project`, `--tfm` |
| Focus / zoom | What structural subject is being addressed? | package artifact, library, type, member | Noun-first inspection command or an explicit focus selector on an operation-first command |
| Point selector / coordinate | Which exact instance or point is selected within that structural scope? | overload, MethodDef token, IL offset | Positional/named selector whose identity is complete within the current scope |
| Observation / census | Which identities or facts are measured under that focus? | subject presence, child-member census, allocation sites, call sites | Section or producer descriptor such as `--finding` |
| Operation / arity | What is being done, and across how many addresses? | inspect one cell, compare two cells, correlate N cells | Top-level operation when the acquisition lifecycle and outcome shape change |
| Lens / representation | Which view of the same subject and operation is wanted? | API, analysis, implementation, source, IL, versions | `-S` or focused mode options |
| Traversal policy | Which addresses are evaluated, and in what order? | `--at`, endpoints, caller-directed probes, next-probe recommendation | Operation-owned options; never implicit payload acquisition |
| Projection / rendering | How is the same result shaped for output? | fields, columns, count, URLs, printable payload, table, Markdown, JSON | Shape reducers, projectors, and writer options |

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
command today. It is reachable from more than one structural scope:

- `library --il-offset <MethodDef>+<offset>` supplies a composite coordinate
  that is complete within the library and discovers its containing member;
- member-focused body views already have the member identity and expose the
  peer offset-scoped facts within that narrower scope.

These are two entry points into the same method-body inspection model, not
different meanings for the coordinate. Sections such as `Instruction Context`
choose the observation/projection, while raw `IL` is a representation lens.
Neither changes coordinate identity.

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

## When a command transition is justified

A command transition is justified when either of these changes:

1. **Structural focus domain:** the addressed identity family and primary
   workflow change to another independently navigable surface. `type -> member`
   is a valid transition because a member has a different selector, identity,
   default view, and drill-in surface. A coordinate refinement such as
   `library --il-offset`, however, remains an option when the selector is
   complete within the established library scope and does not need an
   independent command surface.
2. **Operation arity:** the acquisition topology and outcome envelope change.
   Unary inspection, pairwise comparison, and N-address correlation have
   different failure semantics, backpressure, and result shapes.

Keep the current command when only an observation producer, lens, section,
traversal choice, or output projection changes. A type-presence census and a
type-scoped member census can both participate in `diff --type T`;
`member -S IL` does not become an `il` command because it is the same member
under another representation. `--json` does not become a command; it is another
writer over the same result.

An execution lifecycle is different when at least one of these is true:

- the required inputs have a different cardinality;
- a different acquisition plan is required;
- operation outcomes have a structurally incompatible top-level schema;
- the addressed subject has a different identity model.

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

Multi-address operations are operation-first:

```bash
dotnet-inspect diff --package System.Text.Json@8.0.0..9.0.0 \
  --type System.Text.Json.JsonSerializer
dotnet-inspect timeline --package System.Text.Json@8.0.0..9.0.0 \
  --type System.Text.Json.JsonSerializer --finding api.member --at all
```

`timeline` belongs beside `diff`, not behind `type --timeline` or a
`Timeline` section. It changes arity, acquisition, failure topology, and the
top-level result from a subject document to an ordered history.

Operation-first commands carry source and focus as explicit selectors. Existing
positional source shorthands, such as `diff Package@A..B`, may remain compatible,
but documentation and new cross-operation examples should prefer the named form
so the source/focus/operation axes stay visible.

## A version range is an address space

`Package@A..B` defines an immutable, inclusive, caller-directed address space.
It does not by itself authorize payload acquisition, choose a cell, compare
endpoints, or infer monotonic history.

Operations do not have to materialize that address space in the same way.
`package --versions`, addressed unary inspection, and `timeline` need the
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
| Correlate | N explicit cells | `timeline` resolves the full address space but acquires only repeated `--at` probe cells; `--at all` explicitly authorizes every cell. |

The first row is a package lens and output-shape selection. It is not an
operation peer of `diff` and `timeline`.

### Acquisition cardinality versus output shape

Operation arity controls how many source addresses may be evaluated and how
many primary subject payloads may be acquired. Output shape controls how
already-selected data is projected or reduced. These cardinalities are
independent.

`--versions` selects a version **Vector** while retaining package focus. For a
range, resolving that Vector uses registry/cache metadata and acquires zero
package payloads. Once selected, the normal output-shape rules apply:

| Gesture | Shape effect | Acquisition effect |
| --- | --- | --- |
| `--versions` | Select the version Vector. | Resolve version metadata; acquire zero package payloads. |
| `--count` | Reduce the selected Vector to a Scalar count. | None. Count the bounded, prerelease-filtered addresses already selected. |
| `--urls` | Project URL-bearing rows to a URL Vector. | None. Valid only if the version-row schema exposes a URL. |
| `--print` | Resolve a printable payload already referenced by one selected row. | May fetch that declared payload at the same evaluated address; must not add or evaluate another source address. |
| `--print-all` | Resolve every declared printable row payload in stable row order. | Explicitly authorizes payload fan-out only for already selected/evaluated rows; must not evaluate source addresses. |
| `--head N` / `--tail N` | Clip rendered output lines after projection. | None. They do not select printable rows or limit payload fetches. |

Shape reducers do not revise operation arity. In particular:

- `package Package@A..B --versions --count` means "how many addresses are in
  this bounded Vector?", not "inspect these packages and count successful
  payloads";
- `--urls` may expose registry URLs if version rows gain such a field, but it
  must not download package contents to manufacture them;
- `--print` is exactly-one, not implicit-first: one printable row prints
  directly, multiple printable rows require `--row N|first|last` or `--print-all`, and zero
  printable rows reject;
- `--head N` and `--tail N` run after print selection and fetching, so
  `--print --head 1` does not select the first printable row and
  `--print-all --head 1` still authorizes all declared payload fetches;
- `--rows --head N` and the symmetric `--rows --tail N` are first/last
  table-row rendering windows and remain incompatible with `--print` and
  `--print-all`; `--row N|first|last` selects exactly one printable row;
- a plain version string has no printable document. `--print` and `--print-all`
  must report that the selected shape is not printable rather than silently
  transition from version-address rows to package artifact inspection. The
  explicit transition remains `package Package@version`.

The same rule applies to `timeline`. `--count` can reduce an already
assembled Timeline table; it cannot probe additional cells. `--print` and
`--print-all` can print only payloads already carried or explicitly referenced
by evaluated rows; they cannot turn unevaluated rows into implicit acquisition.

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
library + MethodDef/offset -> IL coordinate
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

```text
inspect -> diff -> timeline
```

The user keeps the source and structural focus but changes the question:

- inspect: what does the selected observation report at this address?
- diff: how does the selected observation transition between these addresses?
- timeline: what is known for the selected observation across this ordered
  address space?

A workflow may move on any axis, but one command transition should not hide
multiple changes. For example:

```text
diff type presence
  -> diff type members  observation change, same type focus and operation
  -> diff member        structural zoom to one member
  -> timeline member    operation change, same member focus and observation
```

Because the CLI is stateless, source and focus selectors must be repeated when
changing operations. That repetition is not a reason to conflate the commands;
it makes the transition explicit and reproducible.

## Timeline and bisect consequences

`timeline` and `diff` are peers. Both are multi-address operations over the same
source and focus selectors:

- `diff` has exactly two evaluated cells and emits pair transitions;
- `timeline` has an ordered address space, evaluates a caller-selected dense or
  sparse set of cells, and emits correlation states plus transitions between
  evaluated censuses.

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
native identity key and reports `Present`, `Missing`, `SubjectAbsent`, and
`Failed` cells. With `analysis.allocation`, `analysis.call-site`, or
`analysis.unsafety`, the selected member is the Analysis subject and the
correlated values are its producer-native occurrence censuses:

```bash
dotnet-inspect timeline --package Foo@1.0.0..2.0.0 \
  --type Foo.Parser --member Parse \
  --finding analysis.unsafety --at all
```

This is the cross-family composition proof: Metadata resolves the structural
member focus, Analysis supplies the selected observation census, and Findings
owns the N-address correlation. The command does not introduce a Research-owned
timeline model.

### Dense timeline

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
- `SubjectAbsent`: the producer has no applicable subject input;
- `Failed`: inspection did not complete;
- `Unevaluated`: the address exists in the resolved vector but was not supplied
  to `FindingCensusCorrelation`.

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
3. Is there a point selector, and is its identity complete within that scope?
4. What observation census runs within that focus?
5. What is the operation arity and acquisition plan?
6. Does the change require a new top-level outcome schema?
7. Is this only another lens over the same focus and operation?
8. Is this only traversal policy or output projection?
9. Does every range-consuming path state how many payload cells it may acquire?
10. Are unevaluated, absent, missing, and failed states kept distinct?

Question 6 does not discriminate by itself. Pair it with question 1 or 5:

- changed addressed-subject identity plus a changed schema means a focus
  transition;
- changed arity/acquisition plus a changed schema means an operation transition.

Otherwise, prefer an option, section, or writer.

## Non-goals

This model does not:

- add an `inspect` command;
- remove existing positional shorthands;
- make source options global;
- add session state that implicitly carries source/focus between commands;
- authorize implicit or unbounded range scans;
- turn sections into operation substitutes;
- define the final `timeline` syntax before its bounded producer/view contract
  is designed.

The immediate purpose is to make that future syntax derivable rather than
ad hoc.
