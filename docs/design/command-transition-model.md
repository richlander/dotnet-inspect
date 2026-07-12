# Command Transition Model

`dotnet-inspect` has two kinds of top-level commands today:

- noun-first inspection commands such as `package`, `library`, `type`, and
  `member`;
- operation-first commands such as `diff`.

That split is useful, but only if transitions between commands follow an
explicit model. A user should be able to predict whether a new gesture changes
the subject, the operation, the lens, or only the rendering.

The governing rule is:

> One transition should change one axis. Change commands when subject identity
> or operation arity changes; use options and sections for context, lenses, and
> projection.

## Independent axes

| Axis | Question | Examples | CLI shape |
| --- | --- | --- | --- |
| Source context | Where is the subject acquired from? | package, platform, local library, restored project, TFM | Named options such as `--package`, `--platform`, `--library`, `--project`, `--tfm` |
| Focus / zoom | What structural subject is being addressed? | package artifact, library, type, member, member coordinate | Noun-first inspection command or an explicit focus selector on an operation-first command |
| Operation / arity | What is being done, and across how many addresses? | inspect one cell, compare two cells, correlate N cells | Top-level operation when the acquisition lifecycle and outcome shape change |
| Lens / representation | Which view of the same subject and operation is wanted? | API, analysis, implementation, source, IL, Findings | `-S`, descriptor selectors, or focused mode options |
| Traversal policy | Which addresses are evaluated, and in what order? | `--at`, endpoints, caller-directed probes, next-probe recommendation | Operation-owned options; never implicit payload acquisition |
| Projection / rendering | How is the same result shaped for output? | fields, columns, rows, table, Markdown, JSON | Query and writer options |

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

An IL offset is a coordinate within a member, not a standalone command today.
`library --il-offset` resolves that coordinate and sections such as
`Instruction Context` render it. Raw `IL` is a representation lens over a
member. The coordinate and the representation therefore occupy different axes.

## When a command transition is justified

A command transition is justified when either of these changes:

1. **Structural focus:** the addressed identity and primary schema change.
   `type -> member` is a valid transition because a member has a different
   selector, identity, default view, and drill-in surface.
2. **Operation arity:** the acquisition topology and outcome envelope change.
   Unary inspection, pairwise comparison, and N-address correlation have
   different failure semantics, backpressure, and result shapes.

Keep the current command when only a lens, section, traversal choice, or output
projection changes. `member -S IL` does not become an `il` command; it is the
same member under another representation. `--json` does not become a command;
it is another writer over the same result.

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
```

A future `timeline` belongs beside `diff`, not behind `type --timeline` or a
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
`package --versions`, addressed unary inspection, and a future timeline need the
published interior vector and resolve it through `PackageVersionVector`.
`diff` needs only the two literal endpoints, so it currently acquires those
endpoints without enumerating or validating the interior vector first. Endpoint
selectors such as `#N`, `first`, and `last` are `--at` selectors over a resolved
vector; they are not valid replacements for the literal `A` or `B` in the range
syntax.

The operation supplies the cardinality contract:

| Operation | Evaluated payload cells | Current or intended behavior |
| --- | ---: | --- |
| Enumerate | 0 | `package Package@A..B --versions` resolves and renders range metadata without acquiring package payloads. |
| Inspect | 1 | `type` and `member` require one explicit `--at <version\|#N\|first\|last>` and acquire only that exact package. |
| Compare | 2 | `diff --package Package@A..B` acquires and compares the two endpoints. |
| Correlate | N explicit cells | A future `timeline` resolves the full address space but acquires only caller-selected probe cells. |

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
package -> library -> type -> member -> member coordinate
```

The user changes what structural thing is being addressed. Identity and schema
change; the operation remains unary inspection. The final step is expressed
today with a coordinate option such as `--il-offset`, not an `offset` command.

### Operation / arity

```text
inspect -> diff -> timeline
```

The user keeps the source and structural focus but changes the question:

- inspect: what is true at this address?
- diff: what transition exists between these two addresses?
- timeline: what is known across this ordered address space?

A workflow may move on either axis, but one command transition should not hide
both. For example:

```text
type at #6
  -> member at #6       structural zoom
  -> timeline member    operation change, same member focus
  -> diff member        pairwise confirmation, same member focus
```

Because the CLI is stateless, source and focus selectors must be repeated when
changing operations. That repetition is not a reason to conflate the commands;
it makes the transition explicit and reproducible.

## Timeline and bisect consequences

`timeline` and `diff` are peers. Both are multi-address operations over the same
source and focus selectors:

- `diff` has exactly two evaluated cells and emits pair transitions;
- `timeline` has an ordered address space, zero or more explicitly evaluated
  cells, and emits correlation states.

The first timeline UX should preserve the Finding correlation semantics:

- `Present`: a completed census contains the exact identity;
- `Missing`: a completed census does not contain it;
- `SubjectAbsent`: the producer has no applicable subject input;
- `Failed`: inspection did not complete;
- `Unevaluated`: the address exists in the resolved vector but was not supplied
  to `FindingCorrelation`.

`Unevaluated` is a presentation state formed by joining the version vector with
the sparse correlation. It is not fabricated as a Finding or inspection
outcome.

Probe order does not define timeline order. Positions come from the resolved
version vector in the caller's range direction; evaluated inspections retain
those positions regardless of the order in which probes were requested.

The default bisect behavior should recommend, not acquire. A recommendation may
name the next unevaluated midpoint and print a copyable command, but it must not
download another payload without explicit caller intent. This preserves:

- network and package-cache backpressure;
- visible retry/failure boundaries;
- the caller-owned probe budget;
- the distinction between recurrence-safe backward scanning and a binary search
  that assumes a monotonic predicate.

A timeline locates a candidate boundary. It does not claim introduction.
The adjacent endpoint comparison must still produce the producer's native
`PairFinding.Added` transition.

## Decision checklist

Before adding a command or mode, answer in order:

1. What is the structural focus and its stable identity?
2. What is the source context?
3. What is the operation arity and acquisition plan?
4. Does the change require a new top-level outcome schema?
5. Is this only another lens over the same focus and operation?
6. Is this only traversal policy or output projection?
7. Does every range-consuming path state how many payload cells it may acquire?
8. Are unevaluated, absent, missing, and failed states kept distinct?

Question 4 does not discriminate by itself. Pair it with question 1 or 3:

- changed identity plus a changed schema means a focus transition;
- changed arity/acquisition plus a changed schema means an operation transition.

Otherwise, prefer an option, section, or writer.

## Non-goals

This model does not:

- add an `inspect` command;
- remove existing positional shorthands;
- make source options global;
- add session state that implicitly carries source/focus between commands;
- authorize automatic range scans;
- turn sections into operation substitutes;
- define the final `timeline` syntax before its bounded producer/view contract
  is designed.

The immediate purpose is to make that future syntax derivable rather than
ad hoc.
