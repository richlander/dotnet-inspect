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
different meanings for the coordinate. Sections such as `Context: Instruction`
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

The same rule applies to `timeline`. `--count` can reduce an already assembled
Timeline table and cannot probe additional cells; semantic item/range
composition follows
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
  -> match A B --implementation   decompiled drill-down for that pair
```

Discovery ranks; it does not decide. A rank is a selection step, so the output
must disclose that it establishes no relation, no semantic equivalence, and no
authorship or copying claim. `--implementation` is rejected in discovery mode:
it is a pairwise drill-down and must not run for every ranked row.

The disclosure names only the transition that is actually available, and names
the image that transition must be given. A `--library` argument names exactly
one image, so the seed and the candidate population coincide in the ordinary
case: the transition is pairwise `match` against that same library, and the
printed token is the promise that it will work.

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
survives package ranges and same-named assets in other TFMs. User-supplied
`--source`, `--add-source`, and `--nugetconfig` selectors are part of that
address because they authorize which cached producer may serve the package
offline. Explicit config paths are made absolute so the next command does not
reinterpret them against another working directory. A source value that the URL
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
in discovery, while preserving the original config path for matching
credentials and aliases
(`Similar_SelectedVersionProducer_ReplayReopensTheSamePayload`).
The exact package coordinate, library selector, and TFM must also survive the
output channel's required rendering containment without changing spelling.
Discovery refuses the transition when containment would rewrite any of those
selectors rather than emitting a command that names another asset
(`Similar_PackageAssetThatCannotBeDisclosedLosslessly_IsRefused`).
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
`Similar_PackageForwardedPopulation_DisclosesTheExactReplayAddress`,
`Similar_PackageSameImage_DisclosesTheExactReplayAddress`). An image the caller
supplied directly outlives the command and is disclosed unchanged
(`ReplayableCandidateAddress_ForADirectlyNamedLibrary_KeepsThePathIntact`,
`ReplayableCandidateAddress_ForAnImageOutsideTheExtraction_KeepsThePathIntact`).

The candidate population follows the disclosure rules rather than the focus
rules. Type-scoped retrieval is the bounded default and is inferred from the
seed's declaring type; whole-assembly search changes the cost class and so
requires an explicit `--assembly-wide`. Both scopes are evaluated in the image
that defines the seed, so widening the scope can never search strictly less than
narrowing it did.

Presentation and product limits stay orthogonal: `--top` bounds rendered rows,
while `--max-results` and `--max-methods` move the product retrieval limits.
Structured output retains every candidate, per-method outcome, blocker, and
receipt regardless of `--top`, so a text-shaping flag can never silently discard
evidence. The per-method outcomes are what make the receipt's aggregate counts
attributable: a count of skipped methods that names no method is not evidence.

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
native identity key and currently reports `Present`, `Missing`,
`SubjectAbsent`, and `Failed` cells. The shared
[Finding topology](finding-nomenclature.md#inspection-and-comparison-semantics)
also retains `NoApplicableInput` and narrows `SubjectAbsent` to proven
exact-subject absence. The current timeline projection renders both typed
absence kinds as `SubjectAbsent` pending a focused CLI migration. With
`analysis.allocation`, `analysis.call-site`, or `analysis.unsafety`, the
selected member is the Analysis subject and the correlated values are its
producer-native occurrence censuses. The compatibility projection is gated by
`AnalysisTimeline_NoApplicableInputRetainsLegacySubjectAbsentPresentation`:

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
