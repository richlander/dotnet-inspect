# Output shapes

dotnet-inspect output narrows through a small ladder of **shapes**. Product
producers define the rows and capabilities; Markout renders those shapes after
dotnet-inspect flags choose which rung you land on. Naming the ladder gives a
shared vocabulary for the output flags
(`-S`, `--fields`/`--columns`, `--tsv`/`--jsonl`, `--count`, `-n`/`--rows`,
`--print`, `--bare`, …) and for deciding what a new flag should
do.

**Document** in this file means a rendered multi-section output shape. A typed
semantic
[inspection Document](host-observable-content-kinds.md#document)
may render through that shape, but the two terms are not equivalent.

Default-renderer policy is owned by
[Native type and source defaults](rendering-model.md#native-type-and-source-defaults).
In particular, `type`/`member` source payloads do not require or accept
`--bare`; other content owners retain their existing gestures. Shape selection
and payload acquisition are unchanged by that presentation choice.

The item-limit, projection-role, typed-L2 result, and multi-item print passages
describe historical
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677) target
behavior, not released or implementation-ready contracts. [Item and line
limits](item-and-line-limits.md) records the replacement composition and
focused-owner gaps; it defines no product syntax, behavior, or gates.

Related docs:

- [Output style guide](style-guide.md#machine-names-and-identifiers) — machine
  property and semantic identifier naming
- [Output composition model](output-composition.md) — section selection, filtering, and writer capabilities
- [Projected JSON output](projected-json.md) — typed versus lowered JSON, representability, and atomic failure
- [Rendering model](rendering-model.md) — verbosity vs mode-switch flags
- [Schema query](schema-query.md) — `-D` discovery of sections and columns
- [CLI change classification and obsolete
  inputs](cli-change-classification.md) — published surfaces, change
  disclosure, invalid-input guards, and routing reservations
- [Item and line limits](item-and-line-limits.md) — composition history for the
  retired umbrella target and an index of its focused owners
- [Section-row shaping](section-row-shaping.md) — typed declared-row-set
  binding, projection roles, and terminal Count semantics
- [The package query CLI](package-query-cli.md) — a facet-matched package
  corpus row applying this ladder's "declared row unit" discipline, and the
  source of the item-limit design

## Content shapes and service envelopes

The output-shape ladder is oriented on the **content layer**.
`--envelope` operates at the **service layer**: it exposes the completed
operation's [inspection envelope](inspection-envelope.md), not another rung
above Document. PortableProjection and envelope diagnostics are not content
sections,
columns, or rows.

This section locks the target CLI boundary for
[#6719](https://github.com/richlander/dotnet-inspect/issues/6719), including
the [Diff envelope adoption](command-transition-model.md#envelope-complete-adoption).

The envelope owner's
[service-evidence enrichment](inspection-envelope.md#service-evidence-enrichment)
adds a typed companion without changing that content boundary. Its first
`--evidence-envelope <path>` consumer is asset-mode dependency inspection,
implemented under [#7293](https://github.com/richlander/dotnet-inspect/issues/7293)
with the dependency value adopted under #7117. It is a Debug-only diagnostic
attachment, not a new rung in this ladder.

### Implementation status

Baseline transport is adopted by positional `depends <type>`, ordinary
Library API Diff with exactly one Library per endpoint, Package Activity,
ordinary Package Query, Package Query assembly-semantic evaluation, and exact
package-backed Type and Library API inspection.
The dependency operation registers `result_kind` `type-dependencies` at
`schema_version` `1` and uses one host-neutral
`TypeDependencySectionJsonContext` for both Content-only `--json` and the
Content subtree of `--envelope`.

Debug asset-mode `depends` adopts `--evidence-envelope <path>` for
`DependencyInspectionContent` and `DependencyInspectionEvidenceDocument`.
It preserves the ordinary primary output, supports a distinct ordinary
`--out` destination and paired baseline `--envelope`, and publishes the
complete enriched frame atomically.

`--depth` remains traversal, while `--rows` and
`-n`/`--head`/`--tail` remain semantic relationship selection. Content retains
both
`queryResult.dependency.relationships` and the selected
`rowSelection.relationships`. The service constructs PortableProjection for
both JSON
boundaries regardless of stderr projection; `--json` emits Content only, while
`--envelope` exposes PortableProjection. Mixed-source plans and explicit
`--depth` issue typed `PortableProjection.NonProjectable`. Explicit `--share`
retains the existing final
stderr line policy after host diagnostics.

Admission rejects competing or unadopted output operations before acquisition.
The common writer buffers both JSON forms before stdout commit. Service-issued
empty or non-success Content retains its exit policy; acquisition failure
without a result emits no manufactured envelope.

Library API Diff registers `library-api-diff` at schema version `1` and uses
the host-neutral `LibraryApiDiffJsonContext` for both unprojected `--json` and
`--envelope.content`. Its root `outcome` is `available`, `unavailable`, or
`rejected`; Available retains its `document`, and non-success cases retain
numeric `kind` and both endpoint summaries. Presentation-owned properties
remain camelCase, and the nested `ComparisonDocument` retains its owner-issued
snake_case properties. Enums remain numeric and native nulls, arrays, numbers,
and booleans are preserved.

This replaces the former unprojected CLI `{changes: ...}` JSON view.
Explicit Type/classification filters, section selection, and other admitted
presentation controls still request projected JSON. They are incompatible
with `--envelope`, as are non-API modes and multi-Library endpoints. `--all`
remains a service API-scope input; `--compact` controls whitespace for either
JSON boundary, and rejects projected or unadopted Diff operations rather than
silently ignoring the option. Rendered-line clipping is rejected for complete Content JSON.
PortableProjection remains the service-issued `NonProjectable` at
`comparison/endpoints`.

Exact-pair Implementation Diff registers `implementation-diff` at schema
version `1`. For one local Library on each endpoint, exact
`-S "Implementation Diff"` selects the operation without projecting Content.
`--json` and `--envelope.content` use the same
`ImplementationDiffDocument` serializer and retain request selectors, endpoint
assembly identity/MVID/provenance, member evidence, complexity, and coverage.
Type and member selectors remain semantic request inputs. Package/platform
sources, PDB Source, categories or additional sections, row/field/column
projection, and alternate formats remain outside this complete transport.
Share is `NonProjectable` at `comparison/endpoints`.

Package Activity registers `ecosystem-change-report` at schema version `1`.
Unprojected `--json` and `--envelope.content` share the owner-issued
`EcosystemChangeReportDocument` serializer. Report scope, interval, security
selection, and semantic result limit remain service inputs. Projection, Count,
row selection, discovery, section selection, and competing output formats are
rejected with `--envelope`. Typed incomplete or failed Documents remain
visible before the command returns a nonzero exit.

Ordinary Package Query registers `package-query`, while `--library-literal`
registers `package-assembly-semantic-query`, both at schema version `1`.
Unprojected `--json` and `--envelope.content` share each owner's complete
Document serializer. Query planning inputs remain admitted, while row
selection, projection, section selection, Count, discovery, and competing
output formats are rejected with `--envelope`. Typed incomplete or failed
Documents remain visible before the command returns a nonzero exit.

Exact package-backed Type inspection registers `exact-type`, while exact
package-backed Library API inspection registers `exact-library-api`, both at
schema version `1`. Unprojected `--json` and `--envelope.content` share the
owner-issued `ExactTypeInspectionResult` or
`ExactLibraryApiInspectionResult` serializer. Admission is limited to the
complete quiet/minimal operation. Richer verbosity, tree or bare output,
projection, discovery, Count, and competing formats remain on compatibility
paths or are rejected with `--envelope`. Typed incomplete or unavailable
Content remains visible before the command returns a nonzero exit.

Asset-mode `depends`, other Type routes, other commands, Discover, Count,
`--evidence-envelope`, optional evidence capture from
[#7117](https://github.com/richlander/dotnet-inspect/issues/7117) remain
unadopted. Library API Diff's complete Browser baseline transport is governed
by its [Browser owner](inspect-web-library-api-diff.md#managed-composition).
[#7703](https://github.com/richlander/dotnet-inspect/issues/7703) owns the
remaining Diff command-family adoption.

The adoption also closes two shared Content-serialization prerequisites.
`AssemblyResolutionProvenance` serializes its six existing cases with owner
`kind` discriminators `package`, `platform`, `project`, `local`, `embedded`,
and `designated`. `AssemblyContextSubject` excludes its process-local
`Registration` while retaining Identity and full typed resolution Provenance,
following the
[host-observable object-identity rule](host-observable-content-kinds.md#serialization-ready-schema).
These corrections flow through the existing Browser source-generated
serializer. They do not add Browser framing or runtime evidence capture.

The adopting Release gate assignments are:

- [`ConfiguredPayloadAcquisitionTests.TypeEnvelope.cs`](../../tests/DotnetInspect.Cli.Tests/ConfiguredPayloadAcquisitionTests.TypeEnvelope.cs)
  owns the real Npgsql paired JSON scenario, both PortableProjection cases,
  semantic
  windows/depth, failed and empty results, and pre-acquisition admission.
- [`InspectionEnvelopeOutputTests.cs`](../../tests/DotnetInspect.Cli.Tests/InspectionEnvelopeOutputTests.cs)
  owns framing, ordered diagnostics, serialization-failure buffering, and
  deferred `--share` output after host metrics.
- [`LibraryApiDiffEnvelopeCommandTests.cs`](../../tests/DotnetInspect.Cli.Tests/LibraryApiDiffEnvelopeCommandTests.cs)
  owns the real System.Text.Json paired JSON scenario, native Content,
  empty success, rejection, admission, formatting, and acquisition failure.
  Its Microsoft.NETCore.App.Ref pair owns resolved multi-Library rejection;
  both real-package cases are also run by the daily slow CLI suite.
- [`LibraryApiDiffJsonTests.cs`](../../tests/DotnetInspector.Presentation.Tests/LibraryApiDiffJsonTests.cs)
  owns complete Outcome and endpoint-issue serialization and round trips.
- [`AssemblyResolutionProvenanceJsonTests.cs`](../../tests/ILInspector.Metadata.Tests/AssemblyResolutionProvenanceJsonTests.cs)
  owns round-trip coverage for all six provenance cases.
- [`BrowserEngineBoundaryTypeDependencyTests.cs`](../../inspect-web/DotnetInspect.Web.Tests/BrowserEngineBoundaryTypeDependencyTests.cs)
  test `QueryTypeProjection_RetainsDependencySubjectWireFacts` owns the real
  Browser managed-export boundary: subject identity and provenance survive
  without test-deserializer compensation.

### Service and content serialization boundaries

For the same completed operation, with no additional content-output selection
or projection:

| Option | Layer | Logical operation |
| --- | --- | --- |
| `--envelope` | Service | `envelope.ToJson()` |
| `--evidence-envelope <path>` | Service attachment | `enrichedEnvelope.ToJson(path)` |
| `--json` | Content | `envelope.Content.ToJson()` |

`ToJson()` is contract notation, not a required CLR instance method.
`--envelope` selects the complete service value and fixes JSON as its encoding.
It is JSON-only, not a format-independent wrapper that can be rendered as a
Markdown document, table, TSV, or JSONL stream. `--json` selects JSON encoding
for content; with an admitted output projection it serializes that projected
content under the [projected-JSON contract](projected-json.md).

`--evidence-envelope <path>` selects the evidence-enabled service form and one
complete enriched JSON attachment while leaving the ordinary primary-output
choice in force. It may accompany `--envelope`, in which case stdout receives
the baseline envelope and the file receives the enriched envelope. Both values
come from the same evidence-enabled invocation; the baseline stdout value is
the enriched value's `Inspection` projection.

The unprojected content value decoded from `--json` must equal the Content
subtree decoded from `--envelope`. They use the same owner-issued content
serialization contract, including native value kinds, nullability, sequence
order, and owner-specific Outcome discrimination. JSON whitespace and object
property order are not part of this equality. Serialized property spelling
and transport framing follow [Envelope transport](#envelope-transport).

Content can be a Result, Document, or owner-specific Outcome as defined by
[host-observable content kinds](host-observable-content-kinds.md). Content-only
JSON does not silently unwrap an Available case to its Document or replace a
typed non-success with an empty object. It omits the surrounding envelope,
not evidence within Content. Existing stderr and exit-status policies remain
with their owners; omitting envelope diagnostics from content stdout does not
authorize suppressing their required disclosure.

### Shaping content does not shape the envelope

Output shapes, fields, rows, and format lowering act on content, not on the
envelope's members. Service passthrough serializes the already constructed
envelope without content-output shaping, host enrichment, or a second
inspection. A CLI view model is not a substitute for the Content subtree.
An incompatible output-shaping request beside `--envelope` must be rejected
rather than ignored or used to manufacture a filtered envelope.

An evidence attachment is orthogonal to ordinary content presentation.
Ordinary section, row, field, and format controls retain their existing
admission and primary-output meaning; they do not filter the attachment's
baseline Content or Evidence after service completion. A semantic selection
already bound into the service request still affects both values under its
owner's contract.

This does not bypass semantic selection. Subject, endpoints, operation mode,
and selections bound by the content owner into the resolved operation plan
still determine which envelope the service constructs.
For example, Count is a terminal semantic projection for package version
populations rather than post-service output shaping. `package P@A..B
--count --envelope` therefore serializes an `InspectionEnvelope<int>` whose
Content is the owner-issued Count result. Ordinary `--count`, including
`--count --json`, projects that same integer, so content-only JSON equals the
envelope's Content subtree. Without Count, the population envelope retains the
complete Document and no redundant Count property. A row window already bound
into the semantic Count plan selects the counted population cohort; it is not
an instruction to slice serialized JSON.
The transport's option rules must distinguish those semantic inputs from
post-service output shaping; this section does not invent another selector
grammar or a complete flag-conflict matrix.

Markout remains the default for content rendering and its admitted lowered
projections. Full service-envelope JSON and unprojected Content JSON use the
typed serialization boundary, not a JSON re-encoding of rendered tables.
This is CLI transport of shared values, not a new shared content model.
Browser consumes the same baseline under the
[envelope owner's host contract](inspection-envelope.md#same-baseline-broader-clients);
the CLI flag adds no Browser interaction or private baseline extension.

### Adoption and evidence

Public envelope adoption includes aligning that route's unprojected
`--json` with its owner-issued Content. Some current commands serialize
host-specific presentation models. Merely consuming an envelope internally
does not establish the equality above. Each adopter must deliberately migrate
any differing machine schema, classify and disclose that change under
[CLI change classification](cli-change-classification.md), and exercise the
same Content contract in both JSON modes. Unadopted routes retain their current
contracts.

Debug evidence-attachment adoption alone is not public baseline-envelope
adoption. An evidence adopter may bind baseline framing for the paired
`--envelope --evidence-envelope` Debug path while leaving standalone
`--envelope` unadopted and preserving the route's existing ordinary `--json`
contract. That scoped compatibility path must name and gate the retained
ordinary JSON serializer; attachment Content still uses the owner-issued
Content serializer. A later public standalone-envelope adoption must resolve
the ordinary JSON alignment under the rule above rather than inherit this
Debug-only exception.

The #6719 path has locked the CLI contract and adopted the common transport
with type dependencies. Exercising Library API Diff as the second content kind
remains. The wider CLI and Browser adoption remains in
[the operation/section production path](operation-command-and-subject-section-composition.md#production-adoption).

The first production scenario is
`Npgsql.EntityFrameworkCore.PostgreSQL@8.0.4`, target
`Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal.NpgsqlOptionsExtension`,
and `net8.0`. The named adopting gates above own this scenario and its
transport, selection, failure, provenance, and Browser-boundary coverage. A
content-only success does not imply that a portable projection is available or envelope
diagnostics are empty. The later Library scenario remains
`System.Text.Json@9.0.0..10.0.0`.

### Envelope transport

This section owns the public CLI wire contract for `--envelope` and the
supported Debug-only CLI wire contract for
`--evidence-envelope <path>`. The input is one completed, owner-issued service
value. `--envelope` writes one JSON object to stdout.
`--evidence-envelope <path>` writes one enriched JSON object to its file while
ordinary primary output continues. Neither is a document rendering or a stream
of independently inspected items. An operation over several participants must
have one owner-issued aggregate content value; transport does not concatenate
envelopes or invent an aggregation model.

The baseline object's required members are:

| Member | JSON kind | Meaning |
| --- | --- | --- |
| `schema_version` | Integer | Version of the wire contract selected by `result_kind`, initially `1`. |
| `result_kind` | String | Stable registered content-contract identity, independent of command spelling and CLR type names. |
| `content_kind` | String | Semantic extent of `content`: `result`, `document`, or `outcome`. |
| `content` | Owner-defined, non-null | Complete owner-issued Content, including any Outcome discriminator. |
| `portable_projection` | Object, non-null | Complete owner-issued portable projection. |
| `diagnostics` | Array | Ordered diagnostics; `[]` when empty, never omitted. |

The evidence attachment adds one required non-null `evidence` member to this
same object. Ordinary `--envelope` omits that member, rather than writing
`null`. CLR composition's `Inspection` property does not introduce wire
nesting. There is no second baseline copy inside the attachment or a
transport-created success flag.

The two framing members belong to the CLI transport, not to the shared CLR
envelope. The pair `(result_kind, schema_version)` identifies the complete
registered wire contract, including its admitted ordinary and enriched forms.
An incompatible change to an existing form, including its Content or Evidence
schema, requires a version increment and change disclosure. A version is
specific to its result kind; an unrelated result kind need not advance.
This is schema identification, not a version-negotiation option or a promise
to retain obsolete serializers.

The registered adopter identities are:

| `result_kind` | Content contract |
| --- | --- |
| `type-dependencies` | `TypeDependencySectionResult` |
| `library-api-diff` | `LibraryApiDiffOutcome` |
| `asset-dependencies` | `DependencyInspectionContent` |
| `ecosystem-change-report` | `EcosystemChangeReportDocument` |
| `package-query` | `PackageQueryDocument` |
| `package-assembly-semantic-query` | `PackageAssemblySemanticQueryDocument` |
| `exact-type` | `ExactTypeInspectionResult` |
| `exact-library-api` | `ExactLibraryApiInspectionResult` |

The enriched `asset-dependencies` form binds
`DependencyInspectionEvidenceDocument` under the dependency owner's
[adoption contract](dependency-inspection-command.md#thin-debug-views-and-browser-adoption).
Its baseline Content exposes `hierarchy`, whose roots and non-root
relationship occurrences retain root-relative parent identity; canonical
nodes and relationships remain backing evidence rather than the result row
currency.
An Outcome's Available, Rejected, or other case does not change `result_kind`;
its own discriminator remains inside `content`. Another operation with a
different content contract, such as Discover or semantic Count, needs its own
registration. Neither the command token nor the generic CLR name is a wire
discriminator.

Envelope and diagnostic member names use lower snake case. PortableProjection
keeps its
owner-issued `kind` discriminator and values, including `available` and
`nonProjectable`; the CLI does not rename cases. Other PortableProjection
members use lower snake case, such as `full_url`. Diagnostic severity uses the
strings
`Information`, `Warning`, and `Error`. Nullable diagnostic correspondence is
present as `null` when absent. NonProjectable's `full_url` and `packet` are
likewise `null`, not manufactured strings.

Content and Evidence retain their owners' named properties, discriminators,
native value kinds, optionality, ordering, and contained-text serialization.
Each registration binds their concrete typed serializers; transport does not
apply a second casing, enum, null-omission, or display-text conversion to their
output. Public baseline adoption uses the same Content serializer for
unprojected `--json` and `--envelope`; the scoped Debug attachment exception
above may retain a named ordinary JSON presentation serializer.
A value without an established named serialization contract is an adoption
gap, not permission to emit tuple positions, `Item1`/`Item2`, an empty object,
or a host-authored substitute. The adopting owner must settle that gap before
the route advertises support.

This deliberately follows the existing typed-JSON/source-generated serializer
path rather than Markout's lowered-JSON dialect. The
[projected-JSON design](projected-json.md#ownership-and-pipeline) provides
the analogous validate-before-commit boundary; its string-valued display
lowering is not suitable for intact service values. Existing CLI
`LibraryApiDiffOutput` illustrates the migration boundary: its presentation
document is useful human output but is not `LibraryApiDiffOutcome`.

### Admission and option interactions

`--envelope` remains a public presence-only option.
`--evidence-envelope <path>` is a Debug-only option with exactly one required,
non-empty file value. Neither has a short alias or requires `--json`.
Missing, blank, or option-shaped evidence paths are rejected by
[CLI option-value validation](cli-option-value-validation.md); `-` is not a
stdout shorthand. URL preferences remain separate from evidence output.

The evidence path is resolved against the invocation's current directory
during admission. Its parent directory must already exist, and a directory is
not a file destination. A successful write may replace an existing regular
file, but only through the [atomic publication](#stdout-diagnostics-and-failure)
boundary below. The host does not create a directory tree or infer a filename.

Support is admitted per resolved command operation, not merely per command
name. Its registration binds the result kind/version, complete closed content
and envelope serialization contracts, and the supported request boundary.
Evidence support additionally binds the closed evidence type and the
evidence-enabled service entry point. Missing serialization support or
unavailable evidence capability rejects the request; it never selects a
legacy formatter or silently falls back to the baseline form.

Admission precedes inspection execution. Syntax-known conflicts reject before
acquisition; support depending on resolved input may require the route's
ordinary authorized resolution first. Resolution does not authorize an
inspection solely to discover whether transport can represent its result.
Evidence capture intent reaches the service before execution, under the
[enrichment contract](inspection-envelope.md#request-and-capture-boundary).
Serialization never recaptures evidence or constructs PortableProjection.

| Input or modifier | With `--envelope` | With `--evidence-envelope <path>` |
| --- | --- | --- |
| The other envelope option | Admit: baseline envelope to stdout and enrichment to the file. | Admit the paired destinations. |
| `--json` | Reject competing primary JSON boundaries. | Retain the route's ordinary JSON contract on stdout. |
| Markdown, plaintext, table, TSV, JSONL, tree, Mermaid, or name-only output | Reject competing primary presentations. | Retain the ordinary route's behavior. |
| `--compact` | Change envelope JSON whitespace only. | Change attachment JSON whitespace; when paired, change both envelopes. |
| `--share[=url\|packet]` | Preserve the adopting command's current output-channel and exit contract using this envelope's PortableProjection. | Preserve the same contract from the enriched value's PortableProjection. Package Dependencies retains its known scalar-stdout fast path until the coherent [existing-adopter migration](cli-workspace-sharing.md#status-and-gates); evidence transport does not partially migrate only its output channel. |
| `--verbose`, `--trace`, `--info`, `--tips` | Retain their stderr-only role. | Retain their ordinary role; only the evidence option requests service evidence. |
| Source, endpoints, subject, API scope, traversal, or other semantic inputs | Retain the operation owner's admission, authorization, and semantic meaning. | Retain the same meaning. |
| `-S`, `-v`, row/query controls, or `--count` | Admit only when the operation binds their complete effect into its owner-issued service result; reject post-service shaping. | Retain ordinary shaping; semantic inputs still bind the service result. |
| `--fields`, `--columns`, `--bare`, `--no-headers`, `--print`, `--value`, URL/path projections, or rendered-line clipping | Reject post-service presentation or projection requests. | Retain ordinary primary-output behavior without shaping the attachment. |
| `--out <path>` | Require explicit adoption of complete baseline-envelope file output. | Admit an ordinary output destination when it is distinct from the evidence destination. |
| Discover, schema/query help, or another content operation | Require that operation's own envelope registration. | Require that operation's own evidence registration; never fall through an early ordinary-output return. |

During Package Dependencies' documented scalar-only migration window, an
explicit Share cannot also select paired baseline `--envelope` or Debug
`--out`; admission rejects either combination before acquisition. The scalar
fast path cannot honor a second stdout envelope or ordinary destination, and
evidence transport does not silently drop either modifier or partially perform
the broader #6725 migration.

When both explicit destinations are present, their normalized absolute paths
must be distinct using ordinal-ignore-case comparison on every host. This
portable, conservative rule deliberately rejects case-only path pairs even on
a case-sensitive filesystem so the same invocation cannot admit them on a
case-insensitive Windows, macOS, or mounted filesystem. An equal destination is
rejected before inspection or destination mutation. Shell redirection, hard
links, and excluded symlink/reparse behavior are not explicit CLI destinations
and add no alias-detection claim.

The semantic-versus-presentation rule follows the actual operation contract,
not the option's name. For example, a service-issued Count result can be
enveloped; counting rows after extracting a service result cannot masquerade
as that operation. A Library Diff classification filter applied only by
`LibraryApiDiffOutput` is likewise not an input to the shared comparison.
Transport adoption does not move either algorithm into a service by fiat.

Implicit rendering defaults do not decorate, window, or suppress an envelope
JSON payload. Ordinary semantic defaults and capability limits still apply.
Every explicitly requested modifier must be honored in its admitted role or
rejected. Shell redirection requires no additional CLI capability.

### Stdout, diagnostics, and failure

Successful serialization writes one complete UTF-8 JSON value without a BOM,
followed by a newline. Whitespace and object-property order are not semantic
contracts. No headings, ANSI styling, progress, tips, Share scalar, or
rendered-line truncation enters that payload.

The complete baseline envelope must be representable and serialized before its
first stdout byte is committed. Its serialization failure reports a bounded
error on stderr, exits nonzero, and leaves stdout empty. It does not retry
through a content renderer, omit an unsupported part, or create an error-shaped
replacement envelope. This applies equally to paired unprojected Content JSON.
Buffering strategy is an implementation choice, not a new content size limit.
A stdout sink failure may leave partial bytes; it remains an I/O failure, never
a successfully delivered envelope.

The evidence attachment is serialized completely before its destination is
mutated. Publication writes a same-directory temporary file, closes it, and
atomically moves it over the destination. Serialization or publication failure
removes the temporary artifact and leaves an existing destination byte-for-byte
unchanged or an absent destination absent. The ordinary primary output is still
written, then the command reports the attachment failure and exits nonzero.
The operation's existing nonzero result remains nonzero when attachment
delivery succeeds.

Successful attachment publication writes one contained locator line to stderr:
`Evidence envelope: <effective-path>`. It appears after ordinary diagnostics
but before an explicitly requested stderr Share scalar, which remains the final
non-empty stderr line. During Package Dependencies' documented scalar-only
migration window, an available Share retains that adopter's stdout channel and
the locator is its final stderr line; a non-projectable refusal remains the
final stderr line. Failure writes a bounded contained error naming the effective
path. Raw envelope JSON never enters stderr.

An owner-issued partial or non-success Content is still serializable content:
write its complete envelope and retain the operation's exit-status policy.
Do not replace it with a diagnostic alone or successful empty content.
Parse, admission, acquisition, cancellation, and unexpected execution failures
that produce no service value retain their stderr/nonzero behavior and produce
no envelope payload. The transport does not fabricate a service result.

Ordinary diagnostic disclosure remains on stderr even though the same typed
diagnostics appear in the envelope. Their severity alone does not decide exit
status. Progress and host-only notices remain outside the value.
`PortableProjection.NonProjectable` alone does not fail an otherwise
successful inspection;
an explicit `--share` request still follows
[CLI Workspace sharing](cli-workspace-sharing.md#output-selection), including
its nonzero refusal and the documented existing-adopter exception for an
available Package Dependencies scalar.

### Transport adoption gates

The baseline transport is a supported public machine contract.
`--evidence-envelope` uses the same typed, versioned framing as a supported
Debug-only machine contract; its availability does not make it an ad-hoc dump.
A Release-configuration PR gate explicitly defines `DEBUG` and exercises the
adopter's public command contracts; the ordinary Release binary, where that
host surface is absent, cannot enforce them.
A retail registration requires the separately approved promotion defined by
the envelope owner. Each adopter exposes only the operations it can complete.
Baseline adoption does not wait for optional Evidence support in #7117,
Browser UI, History, or Diff command/section cutover. Those consumers reuse
this transport rather than publish another framing convention.

The first runtime adoption is positional type dependencies; Library API Diff
will supply the second content kind through the same CLI transport. Changing
legacy unprojected `--json` from a graph/presentation document to shared
Content is **intentionally breaking** where the schemas differ. Positional
type dependencies have disclosed that break in current help, product
guidance, and Breaking release notes; there is no compatibility-only JSON
switch. Other formats and unadopted operations keep their owned behavior.
Browser's baseline delivery remains independently governed by the envelope
owner; this CLI-specific framing does not change its wire or interaction model.

The positional type adopter assigns these requirements to the named Release
gates in [Implementation status](#implementation-status). Future adopters must
likewise exercise their public command entry point:

- parse each complete payload and compare Content between the paired JSON
  modes, including empty success and owner-issued non-success;
- round-trip named fields, native values, all ContentKind values, both
  PortableProjection cases, and ordered
  diagnostics through the registered closed serialization contracts;
- preserve stderr and exit behavior, reject competing modes and post-service
  modifiers, and retain admitted semantic selection without clipping JSON;
- demonstrate visible pre-commit serialization failure with empty stdout,
  using the product writer rather than a harness-produced replacement;
- when evidence is adopted, preserve the baseline subtree, deliver the
  concrete Evidence, and reject unavailable capture without fallback;
- preserve ordinary primary output with and without the attachment, including
  ordinary `--out`, `--json`, rendered shapes, and paired `--envelope`;
- prove atomic replace-on-success and unchanged or absent destinations after
  serialization and publication failures, including exact and case-only
  same-path preflight; and
- prove the contained stderr locator, Share ordering, explicit nonzero failure,
  and absence of raw envelope JSON from stderr.

Use the authentic Npgsql type-dependency scenario recorded above and, for the
second adopter, the existing `System.Text.Json@9.0.0..10.0.0` comparison, with
smaller boundary fixtures for PR-fast cases. Reuse the existing production
runtime/serialization gates for CoreCLR and NativeAOT; no new platform
exception is introduced here. Those gates, not this implementation-status
note or Markdown validation, establish the runtime properties.

## The shape ladder

Each shape is a narrowing of the one above it. You start at a Document and
descend to a Scalar by selecting a section, then columns, then collapsing.

| Shape | What it is | Example |
| --- | --- | --- |
| **Document** | many sections | a full `library` / `type` report |
| **Table** | one section: columns × rows | the `Top Leverage` section |
| **Vector** | one column: many rows of a single field | just the `Member` column |
| **Scalar** | a single value, or a text/doc blob | `1234`, a README, a `///` summary |

That descent describes one declared row-set outcome. Count reduces each
declared row set independently: exactly one outcome reaches Scalar, while
multiple exact outcomes reassemble as one ordered count Table. Count never
collapses independent row sets into one request-wide scalar.

- **Document → Table.** A Document is a sequence of sections. Selecting one
  section leaves a single Table (or other single-section payload).
- **Table → Vector.** A Table is columns × rows. Cell-projecting it to one
  column leaves a Vector — many rows of a single field. A field-set membership
  projection instead changes which field-entry rows reach this ladder.
- **Vector → Scalar.** Within one declared row set, collapsing a Vector (count
  it, or take one row) yields a Scalar. A Scalar is also the natural shape of a
  non-tabular payload: one count, a single field value, or a
  text/documentation blob (a README, a decompiled `.cs` body, an XML-doc `///`
  comment).

Most sections are Tables, but a section can also be a key-value field set, a
list, a code/text blob, a tree, or a graph. Those are still "one section" — the
Table rung — and each declared row set can collapse to a Scalar the same way.
For a call graph, the declared row unit is a directed edge: `--count` counts
relationships, `-n` limits them, and `--rows` selects an absolute range of the
same ordered relationships whether the graph is rendered as a Markdown edge
table, standalone tree, standalone Mermaid diagram, or tabular stream. Tree
nodes are presentation context, not additional rows.
`graph integrations` uses the same row contract: one row is one directed
logical relationship. Its package groups and finer member/type nodes are
presentation context, while `--count`, `-n`, and `--rows` count, limit, or
select logical edges consistently across Markdown, tree, Mermaid, tabular, and
structured output. Isolated explicit packages remain node/group context in
graph and JSON views, but never become empty data rows in the default Markdown
edge table.
`OutputModes_UseTheSameWindowedLogicalEdges` gates the same selected logical
edges across the non-count output modes. Count observes the same preceding
semantic stages under
[Section-row shaping](section-row-shaping.md#count-semantics).

The `graph integrations --json` failure array preserves both presentation and
typed addressing: each failure carries its rendered target plus
`target_kind`/`target_id`, and Integration failures retain structured producer,
kind, assembly-reference, acquisition-failure, and exception fields. Opaque
workspace registration handles are deliberately not stringified; the graph
target and typed reference evidence remain the identities a consumer can
interpret outside the owning workspace.
Failure-targeted nodes and groups remain in the JSON document as diagnostic
context even when they are not endpoints of the selected edge window; they are
not additional relationship rows. Structured diagnostic text crosses the same
lossless inert containment boundary as human-readable failures.
`VisibleGraphFailure_PreservesOutputAndNonzeroExit` gates target
resolvability, and `StructuredFailureText_IsInertAfterJsonParsing` gates
containment after a JSON consumer decodes the value.

Integration graph edge rows carry `source`, `source_assembly`, `source_group`,
`relationship`, `target`, `target_assembly`, `target_group`, `occurrences`,
and `evidence`. Assembly and group fields preserve endpoint identity within a
multi-assembly package and package ownership across package contexts;
plain-text and graph node labels carry the same context. JSON nodes also carry
assembly identity, and failure target labels retain it. JSON and JSONL keep
occurrence counts numeric and absent values null; JSON edges carry projected
evidence rather than exposing
document-local occurrence ids without the occurrence collection that owns
them. `ProductionShapedEndpoints_RetainPackageOwnership`,
`AcquiredEndpoints_RetainAssemblyWithinOnePackage`,
`AcquiredFailureTargets_RetainAssemblyWithinOnePackage`, and
`OutputModes_UseTheSameWindowedLogicalEdges` gate these contracts.

## Flag families

Four families walk the shape ladder, and a fifth sits before it. A flag in one
of the ladder families contributes in one of four ways:

- **Shape selectors** narrow the requested data or shape (`-S`,
  `--fields`/`--columns`, `--count`). Under the target
  [section-row-shaping contract](section-row-shaping.md#projection-kinds), L2
  resolves field/column intent as membership or cell projection before a
  renderer sees it.
- **Item/range selectors** narrow the rows without changing the shape rung
  (`--where`, `--order-by`, `-n`, `--top`, `--rows`).
- **Presentation modifiers** change how a selected payload is rendered without
  changing the shape (`--bare`, `--markdown`, `--json`, `--table`, `--tsv`,
  `--jsonl`, `--plaintext`, `--no-headers`, and graph-supported `--tree` or
  `--mermaid`).
- **URL-shape modifiers** prefer rendered browser views for emitted URLs
  (`--prefer-rendered-urls`). They are orthogonal to the output-shape ladder.

The proposed `--envelope` is a separate
[service-output selector](#content-shapes-and-service-envelopes), not another
content-shape or presentation modifier.

`library --package ... --tfm all` selects multiple independent inspections. Its
full output therefore requires a document format: Markdown or JSON.
Single-table, stream, plain-text, tree, unary projection, and single-row-set
`--print` output fail closed rather than selecting one inspection or combining
independent row sets. Count remains valid because it preserves the producer's
declared aggregate or independent row-set scopes.

For unreduced output, shape cardinality is evaluated after both section and
subject selection. `--table`, `--tsv`, and `--jsonl` require exactly one table
shape; `--tree` requires exactly one tree shape; standalone `--mermaid`
requires exactly one graph shape. Selecting one section with `--tfm all` still
produces one shape per inspection, so it does not satisfy any unreduced
single-shape contract.

Count does not apply that eligibility test to its contributing inputs. It first
consumes the already-bound typed reduction result, then evaluates format
eligibility against the resulting Scalar or one count Table as defined below.

### Structural format capabilities

A command may publish owner-issued output-capability metadata for its selectable
sections. Each section declares the presentation modes supported by its product
shape, plus any mode that requires the section to be the complete selection.
The command also declares any section family that forms one homogeneous Table
when multiple members are selected.

The shared `DiscoveryOutputMode` enum carries semantic mode identity in the
host-neutral Discovery Document. Exact spellings such as `--tree` and
`--mermaid` are CLI lowering owned by the host.

Detailed structural discovery evaluates each listed section, or the complete
expansion of a listed category, against that metadata. A category supports a
mode only when every expanded member supports it and the complete selection
satisfies the mode's cardinality contract. In particular, a multi-section
category does not support tree or graph output, an exclusive mode, or
table/TSV/JSONL unless its members form one declared homogeneous Table.

The resulting capability list describes the complete requested selection. It
must not choose the first compatible section, remove incompatible members, or
otherwise let a presentation modifier change semantic section selection.
[`schema-query.md`](schema-query.md) owns the structural discovery surface that
retains these capabilities in `DiscoveryDocument`. Library currently reports
them through the temporary `-D --details` bridge; #7814 replaces that bridge
with structural `explain`.

### Coordinate carriers sit before the ladder

A fourth kind of flag does not walk the ladder at all: it *supplies an input the
command has no other way to express*, and in doing so changes which sections
exist to be selected. The family has two currencies: the IL coordinate, and the
heap coordinate accepted by `library coordinate` (see
[metadata-table-projection.md](metadata-table-projection.md)).

The family is counted in currencies, not syntax elements, because one currency
can have more than one spelling. `library coordinate` accepts either one exact
IL coordinate or `--file` for batch reporting. Exact and file modes are
mutually exclusive, so they are one member of this family rather than two.

A coordinate carrier is the right shape for a flag only when the input is a
genuinely new currency — a value that is not a section name, a column name, or a
row. An IL coordinate (`0x06000002+0x1`) and a heap address (`#Strings:0x1a4`)
qualify; a table name does not, because a table is already a section and `-S`
already addresses sections.

Carriers behave consistently:

- The sections they enable are **discoverable only when the carrier is present**,
  so `-D` reflects the carrier (see the IL-offset case study below).
- Absent the carrier, requesting a coordinate-scoped section is an error that
  names the missing carrier, for example
  `IL coordinate sections require library coordinate <token>+<offset>`.
- Once the carrier resolves, its sections are ordinary sections: they obey `-S`,
  `--columns`, `--count`, and the rest of the ladder like any other.

Prefer a section, a category, or `--where` before reaching for a new carrier.
The bar is a new currency, not merely a new thing to look at.

## How Markout produces the shapes

Markout serializes a view object into a **Document** of **Sections** and renders
it with a chosen **formatter**. The shapes map onto Markout concepts directly:

| Shape | Markout construct |
| --- | --- |
| Document | the serialized view: an ordered set of `[MarkoutSection]` members |
| Table | one section rendered as a table (`WriteTable`: headers + rows), a field set, a `WriteList`, a `CodeSection`, or a tree |
| Vector | a table projected to one column, or a single-column `WriteList` |
| Scalar | a single cell, a `CodeSection` payload, or a row count |

The current product supplies raw field/column names to
`MarkoutWriterOptions.Projection`, which applies both table-column projection
and field-set inclusion during serialization. The target
[section-row-shaping contract](section-row-shaping.md#projection-kinds) moves
the membership-versus-cell decision into L2; after that adoption, two Markout
knobs handle remaining cell narrowing and formatting:

- **Projection** (`MarkoutWriterOptions.Projection`) applies an already-resolved
  cell projection — the Table → Vector step. It does not implement field-set
  membership projection.
- **Table mode** (`MarkoutWriterOptions.TableMode`) picks how tables render:
  Markdown (default), `MarkoutTableMode.Tsv`, or `MarkoutTableMode.Jsonl`.

Formatters decide presentation, not content:

- **`MarkdownFormatter`** — the rich, multi-section, verbosity-aware Document
  format. The canonical shape; everything else is a projection or reduction of
  it.
- **`TableFormatter`** — a single-section tabular renderer (pretty table, `--tsv`,
  `--jsonl`). Because it renders one section at a time, its output is always a
  single Table (or Vector).
- Tree, Mermaid, and table writers render their own narrow shapes (a call graph
  tree or diagram, a table row) and have no verbosity dial — they either show a
  thing or they do not (see [rendering-model.md](rendering-model.md)).

### Member Finding callee evidence

The explicit member `Facts` section keeps one row per Research Finding. Its
`Member`, `IL`, `Cs Line`, and `Anchor` fields describe where the Finding is
presented in the selected member. For `semantics.callee`, `safety.callee`, and
`cost.callee`, three additional fields describe the callee evidence without
moving that caller-side relationship anchor:

- `Evidence Subject` names the producer-owned callee subject.
- `Evidence State` is `instruction`, `method`, or
  `instruction-unavailable`.
- `Evidence Locations` renders each physical method identity with its optional
  IL offset. Method-level evidence has no invented offset, and unavailable
  instruction evidence says so instead of borrowing the caller coordinate.

These fields are the lowered table vocabulary used by Markdown, table, TSV,
JSONL, and projected JSON. They are display text, not the typed interchange
contract.

Exact singleton `member ... -S Facts --json` selects the complete typed Facts
document. Each Finding retains its caller anchor and optional `callee_evidence`.
Callee evidence contains the producer-owned subject plus ordered physical
locations. A physical method is identified by assembly, module version id,
MethodDef token, declaring type, name, parameter types, return type, generic
arity, and static shape; each location adds a nullable numeric IL offset.
`instruction-unavailable` has an empty location array, while `method` has one
location with a null offset. Structured output never substitutes the caller
offset for either state.

The typed document is complete rather than a rendered row window. Combining
its exact unprojected JSON selection with another section, `--rows`, `-n`,
`--head`, or `--tail` is rejected. A caller that wants lowered or windowed rows
uses table, TSV, JSONL, or an explicit field/column projection. In projected
JSON, `-n` with `--head` or `--tail` is a semantic Facts-row window applied
before serialization; it never clips the rendered JSON text.

Release CLI gates cover:

- caller relationship IL remaining distinct from callee instruction evidence;
- method-level `cost.callee` evidence retaining a null callee offset;
- `safety.callee` retaining an explicit unavailable state when the producer has
  no supported instruction coordinate;
- exact Facts JSON retaining the typed subject and physical method identities;
  and
- explicit Facts field and column projections using the lowered row
  vocabulary, including valid first- and last-item JSON windows.

### Reverse type-declaration locator projection

The shared reverse-locator projection implemented under
[#6846](https://github.com/richlander/dotnet-inspect/issues/6846) is the L2
owner for this claim:

> Project each evaluated locator answer as one independently selected row set
> whose row is one exact Library coordinate plus one attached origin and
> observation context, without changing upstream candidate or coverage facts.

`TypeDeclarationLocatorSection.Project` consumes the owner-issued Queries
result. Rejected query admission remains a typed `Rejected` section result.
An evaluated query produces one `TypeDeclarationLocatorSectionAnswer` per
original request in request order. Every answer retains:

- an owner-issued row-set identity, separate from request text;
- the typed exact or pattern request;
- the number of known candidates before output row selection;
- an always-present selected candidate array;
- realization and evaluation completeness independently; and
- the combined query-completeness fact.

The row unit is one `TypeDeclarationLocatorSectionCandidate`: a typed
four-arm Package/Platform/Project/Local coordinate, structured Metadata name,
declaration kind, and one detached observation. The observation retains
population-issued context/member order, assembly identity, source realization,
and image-selection provenance as separate typed values. Equal logical
coordinates observed through different feeds, targets, views, or occurrences
therefore remain different rows. Selection never unwraps a singleton, groups
away an observation, prefers an origin, or rewrites upstream coverage.

Rows retain Metadata's `IsPublicSurface` and
[definition discovery attributes](type-forwarding-resolution.md#definition-discovery-attributes)
as facts. Raw projection does not apply visibility policy. Typed JSON emits
`is_public_surface` and `discovery_attributes`
with `is_editor_browsable_never` and `is_obsolete` for definitions; the field is
omitted for exports whose target attributes are unavailable. Omission is not a
pair of false facts. These facts do not add default Markout columns.
The PR-fast `TypeLocator_DiscoveryAttributesSurviveResidentAppendAndProjection`
gate checks cold/resident equivalence, append reuse, occurrence preservation,
detached lifetime, and both source-generated JSON forms. Its neighboring
`TypeLocator_MalformedDiscoveryAttributesKeepAttributedIncompleteEvidence`
gate preserves Metadata rejection as attributed incomplete discovery.

An optional [type-declaration visibility plan](type-declaration-visibility.md)
selects known matches before output row windows. That owner defines facet
overrides, all-declaration input admission, and three-valued evaluation.
The result echoes its effective plan; each answer retains its original input
count, known exclusion count, and full undecidable candidate vectors with
their unknown facets. These vectors are independent of selected result rows.
`AvailableCandidateCount` counts known visibility matches before row windows.
Combined answer completeness additionally requires visibility completeness;
the original realization/evaluation facts remain unchanged. A visibility
admission failure marks selection unsuccessful and unevaluated, preserves
source coverage and input counts, and skips row windows. Both failures and
undecidable candidates appear in the existing Markout Gaps section, including
when no rows survive or a strict row window fails. A plan omitted by an
existing consumer preserves the previous projection behavior.

`Head`, `Tail`, and `Window` apply independently to every answer through the
shared rows-cohort semantics. The locator declares stable sequence order but
no ranking order, so `Top` is refused rather than treating source order as
preference. A strict Window failure is atomic across answers: no selected
candidate array is published. The result still retains every answer's known
candidate count, request and completeness, plus all context/member coverage,
so the failure cannot become a scoped miss or a uniqueness claim.

Typed JSON is source-generated from the same section result. It preserves the
request and coordinate unions, structured Metadata name, declaration kind,
realization, selection context, per-context and per-member coverage, selected
candidate arrays, pre-selection candidate counts, and any row-selection
failure. Zero, one, and many candidates use the same array shape. It does not
serialize live Workspace handles or configured package-source authorities.

Reference observations use the `platform-reference` realization alternative,
not the legacy implementation-pack `platform` realization. It retains the
exact family target, reference path, population demand, safe authority label,
producer, source generation, candidate/discovery evidence, and package failures.
Package-source association and content-generation tokens lower to separate
result-local integer ordinals: equal owner tokens receive equal ordinals
within that one result, and distinct tokens remain distinct. Those ordinals
are neither portable versions nor keys for reopening a source. A null
or omitted `requested_assembly` denotes a complete source-population demand.

Context gaps use a closed `context-load` / `reference-source` /
`reference-image` union. Source outcomes and diagnostic codes stay separate,
and package failures remain attached. This evolves the prerequisite JSON
context-failure shape: context-loader codes now appear in `code`, while `kind`
identifies the failure alternative. Existing successful context-loader row
shapes are unchanged.

`TypeDeclarationLocatorView` is the common Markout lowering. Its result rows
contain request, Type, declaration kind, source arm, Library, origin and
context display columns; separate Coverage and Gaps sections keep incomplete
or failed evidence visible when Results has zero rows. Dynamic display text
crosses `InertString` field containment. Package and Platform origins use the
credential-free producer identity already carried by realization. Reference
origins use `PackageSourceDisplay`'s safe authority label and explicitly show
the reference view in the context column; structured producer identity remains
separate. They never display raw configured source URLs. Projected JSON, JSONL,
TSV and Markdown are therefore
one-way display projections, not identity codecs or reopening authority.

The Release gates
`TypeLocatorSection_VectorsRetainCoverageAndTypedIdentity`,
`TypeLocatorSection_StrictWindowFailureIsAtomicButKeepsCoverage`, and
`TypeLocatorSection_TopRequiresASeparateRankingContract`, plus
`TypeLocatorSection_RejectedAdmissionRemainsTyped`, enforce the typed vector,
identity/context, coverage, source-generated JSON, Markout correspondence,
atomic failure, admission-failure, and ranking-refusal boundaries.
`ProjectionRetainsEveryCoordinateArmAndOwnerEquality` additionally gates all
four coordinate arms and preserves Source Selection's assembly-equivalence
semantics. Reference admission additionally uses
`ReferenceSection_PreservesOriginTokensVectorsAndSafeDisplay` and
`ReferenceSection_RetainsSourceAndImageFailuresWithoutRows` to gate reference
view evidence, result-local token correspondence, source-generated JSON,
authority display, and failure disclosure when no candidate row matches.
The CLI and Browser/Wasm production consumers remain
[#6844](https://github.com/richlander/dotnet-inspect/issues/6844) and
[#6851](https://github.com/richlander/dotnet-inspect/issues/6851);
`InspectionEnvelope<TypeDeclarationLocatorSectionResult>` is formed at those
completed-operation host boundaries rather than around this prerequisite
projection.

### Approved `extensions --json` compatibility boundary

The CLI host's `extensions --json` path is an approved bounded exception to
the ordinary Markout lowering rule. Its typed input is the final
`List<ExtensionMethodResult>` produced by the extension query, and its lowering
boundary is the generated `ExtensionMethodJsonResult` contract in
`ExtensionsJsonContext` / `ExtensionsCompactJsonContext`. The visible result
is a bare JSON array with the established `method`, `class`, `extended_type`,
`library`, `signature`, `signatures`, numeric `overloads`, `kind`, source, and
reachable-path fields; null values remain omitted and `--compact` remains a
whitespace-only modifier.

This boundary exists to preserve an established machine contract that the
current lowered Markout formatter cannot represent without changing the
top-level array shape and converting typed numeric/list values to string table
cells. It is limited to this CLI host and this plain `--json` output; Markdown,
table, TSV, JSONL, count, and semantic row selection remain on the normal typed
view/Markout path. The Release gates are
`SearchJsonResultTests.ExtensionResult_PreservesPublicJsonFieldNames`,
`ExtensionsCommandTests.ExecuteAsync_CompactJsonPreservesTypedArrayContract`,
and the extension JSON cases in `CommandExecutionTests`. The exception is
owned by the `extensions` adoption tracked in
[#6697](https://github.com/richlander/dotnet-inspect/issues/6697) and should be
retired only when a compatible Markout typed-JSON lowering is available.

The current `CountProjectionFormatter` establishes cardinality by intercepting
structured Markout rows without writing them. The product contract is that
section selection and row windows determine cardinality before count
formatting. Its Release gates are
`OutputFormatterTests.CountProjection_CapturesTableRowsBySection`,
`OutputFormatterTests.CountProjection_AppliesRowWindowBeforeReduction`,
`OutputFormatterTests.CountProjection_DoesNotCountNonTableContent`, and
`OutputFormatterTests.CountProjection_SectionRowsRenderThroughEveryCompatibleFormat`.
No separate source-shape gate constrains which formatter implementation may
satisfy that contract. Under the target
[section-row-shaping contract](section-row-shaping.md#result-binding-and-failure),
formatters instead consume typed L2 Row-outcomes, Count, or failure results and
do not establish cardinality. Producers outside Markout, such as metadata
tables, expose the same declared logical rows to L2 that their renderers
consume.

### Approved `vocabulary --json` compatibility boundary

The CLI host's plain, unprojected `vocabulary --json` path is an approved
bounded exception to ordinary Markout lowering. Its typed input is the selected
owner-issued `VocabularySection` sequence plus the catalog schema version, and
its lowering boundary is `VocabularyWireDocument` through the generated
`VocabularyWireJsonContext` or `VocabularyWireCompactJsonContext`. The visible
result is the established schema-versioned document containing section
metadata, accepted-command identities, field schemas, operators, and typed
value cells.

This boundary exists because the lowered Markout table shape intentionally
contains display rows, not the catalog's schema and typed values. Moving this
path through Markout would discard that information or change the public wire
contract. The exception is limited to this CLI host and plain unprojected
`--json`; Markdown, plain text, table, TSV, JSONL, and projected JSON serialize
one typed `VocabularyView` through `VocabularyViewContext`. The Release gates
are
`VocabularyCommandTests.JsonSerialization_PreservesWireShapeAcrossIndentationModes`,
`Command_JsonCarriesTypedSchemaAndValues`,
`Command_DefaultRendersTheSelfDescribingSectionIndex`,
`Command_PlainTextUsesThePlainTextFormatter`,
`Command_JsonlUsesProjectedRuntimeColumns`, and
`Command_PartialMachineKeyProjectionKeepsSectionIdentityAcrossFormats`.
The focused adoption is tracked by
[#6811](https://github.com/richlander/dotnet-inspect/issues/6811).

### Approved cache JSON compatibility boundary

The CLI host's `cache --json` and `cache --jsonl` paths are an approved bounded
exception to ordinary Markout lowering. Their typed input is the owner-issued
`PackageCacheService.CacheInfo` snapshot, and their lowering boundary is
`CacheInfoJson` through the generated `CacheInfoJsonContext`. Both formats
expose one object containing the active cache `location`, formatted `total`,
and a `categories` array whose rows contain `name`, `size`, and `items`.
JSONL emits that complete object as exactly one line. An empty cache retains the
same object shape with `categories: []`.

This boundary exists because generated Markout list sections do not emit an
empty section, so lowered JSON cannot preserve the required empty array.
Ordinary Markout JSONL would instead emit one object per category row and
discard the snapshot's location and total. The exception is limited to these
two machine formats for cache inspection. Markdown, plain text, table, and TSV
serialize `CacheInfoView` through `CacheInfoContext`; the empty human state
serializes `EmptyCacheInfoView` through the same generated context. The scalar
acknowledgements from `cache clear` expose no format selection and are not a
cache inspection document.

The Release gates are
`CacheCommandTests.EmptyCacheInfoView_DocumentFormatsRenderExactMessage`,
`ExecuteAsync_EmptyCache_JsonFormat_EmitsValidJson`,
`ExecuteAsync_EmptyCache_JsonlFormat_EmitsSingleValidLine`, and
`ExecuteAsync_PopulatedCache_JsonAndJsonlPreserveOneRecordContract`. The
focused adoption is tracked by
[#6833](https://github.com/richlander/dotnet-inspect/issues/6833).

An incomplete comparison is not narrowed into a clean result. Diff document
formats include typed inspection-failure rows. Single-shape diff formats
(`--table`, `--tsv`, `--jsonl`, and `--name-only`) cannot append a second
failure table, so they emit an explicit incomplete-comparison diagnostic and
exit nonzero.

## How dotnet-inspect flags select a shape

Flags are how the user (or an agent) walks the ladder. The important distinction
is that a shape selector changes what data is requested, while a presentation
modifier changes how a selected payload is rendered.

### Shape selectors (narrow the shape)

| Target shape | Flags |
| --- | --- |
| Document | default view; `-v:q`/`-v:m`/`-v:n`/`-v:d` (breadth presets); `-S a,b` (multiple sections) |
| Table | `-S OneSection` (a single section) |
| Vector | `--fields X` / `--columns X` when resolved as a one-column cell projection |
| Scalar or count Table | Count reduction: one declared row-set outcome becomes a Scalar; multiple outcomes become an ordered count Table |

### Count results

[Section-row shaping](section-row-shaping.md#count-semantics) owns which row
sets participate, what Count observes, when its evidence is exact, and whether
L2 binds a successful Count or failure result. This document begins with that
already-bound typed result and owns only its place on the shape ladder and its
presentation.

- A successful Count result containing one exact declared-row-set entry
  produces a culture-invariant decimal scalar. Markdown, plain text, pretty
  table, and TSV emit the same bare value; JSON emits one number; and JSONL
  emits one numeric record.
- A successful Count result containing multiple exact declared-row-set entries
  produces ordered row-set/count rows. Markdown, table, and plain text render
  those rows as their native table form; TSV emits two columns; JSONL emits one
  object per row; JSON emits an array of objects. JSON and JSONL counts are
  numbers rather than numeric strings.
- Standalone Mermaid rejects every Count result because neither a scalar nor a
  count map is a graph.
- An already-bound failure result produces no Scalar or count Table. Failure
  presentation belongs to the consuming output owner and is not encoded as a
  numeric value.

The multi-row-set reduction is itself one table, so table, TSV, and JSONL
formats accept a request that resolves to multiple row sets under `--count`.
Their ordinary one-input-table restriction evaluates the already-bound
post-reduction shape and therefore accepts this one count-result table without
inspecting how many declared row sets contributed entries.

Target adoption must add the non-vacuous Release gate
`TypedCountResultsRenderByShape`. It feeds already-bound typed results directly
to the output layer and requires:

- one exact entry to exercise Markdown, plain-text, pretty-table, TSV, JSON,
  JSONL, and Mermaid paths, rendering the specified bare numeric value in the
  first four, one JSON number, one numeric JSONL record, and the Mermaid
  rejection;
- multiple entries to preserve identity, order, and numeric counts as a native
  Markdown, plain-text, and pretty-table result, two-column TSV, JSON array,
  and object-per-row JSONL result, while the separately exercised Mermaid path
  rejects and the ordinary single-input-table restriction does not reject the
  count result;
- a bound failure to exercise every Markdown, plain-text, pretty-table, TSV,
  JSON, JSONL, and Mermaid route, each using its owner-defined failure
  presentation with no Scalar or count Table payload; and
- fixtures to prove that no tested output path reconstructs cardinality from
  rendered or intercepted rows.

For multiple package subjects, `Package Info` and package-file sections retain
their producer-declared cross-package survey row sets. Other sections preserve
the aggregate or per-package scope declared before shaping. L2 does not infer a
merge from labels or presentation.

Trees and graphs do not acquire row semantics from whichever presentation a
formatter happens to choose. A producer that supports counting such a shape
must declare and count its product-owned lowering. Current dependency commands
count graph nodes. The target
[Dependency Inspection Command](dependency-inspection-command.md) instead
declares one directed logical dependency edge as the shared graph row across
tree, Mermaid, table, JSON, row selection, and count; that target becomes
current only when its migration lands.

`-D`/`--discover` is orthogonal: it does not render the subject, it lists the
*available* shapes — the sections of the Document and the columns of a Table (see
[schema-query.md](schema-query.md)).

### Printable payload projections

The historical #4677 target made normal `--print` a batch projection over the
selected rows. Every selected row was projected to its declared printable
payload:

| Selected rows | `--print` | `--print --row N\|first\|last` |
| ---: | --- | --- |
| 0 | Error: the selected section has no rows. | Error. |
| 1 | Print one framed or structured result. | Print one framed or structured result for the addressed row; any other number is an error. |
| More than 1 | Print one framed or structured result per selected row. | Print one framed or structured result for the addressed row. |

`--where` filters rows; item-mode `-n`, `--rows`, and `--top` then narrow them
before projection. `--row` is the mutually exclusive exactly-one alternative to
the item/range windows; line-mode `-n` remains available under `--lines`.
`--paths` and `--urls` project the same selected rows without acquiring their
content.

This batch behavior remains pending focused L3 payload-projection ownership and
must not guide implementation until that owner adopts it with its gates.

Numeric `--row N` addresses a row by its position after filtering and effective
ordering, but before item/range windows or payload projection. Sections do not
print a row-number column, so N is the number the reader arrives at by counting
the unwindowed ordered rows top to bottom. Later windows and printability do not
renumber anything. A row that declares no payload still occupies its number,
and selecting it reports that it has no document rather than silently sliding
to a neighbour. For projections that omit inapplicable rows, such as `--value`,
`--urls`, and `--paths`, `first` and `last` remain the endpoints actually
emitted by that projection, retaining their original numeric addresses.
`--print` has no such gaps because every selected row emits a success or
failure. Structured output makes the number explicit — `--jsonl` and `--json`
emit it as `row` — and error messages name the available addresses, so a
projection with gaps stays navigable.

This is the one rule that makes the ordinal trustworthy. Renumbering after a
payload projection or printability check is wrong in the worst way available:
it returns a real row, so nothing looks broken, and the reader has no way to
recover the sequence being indexed. Addressing the pre-projection ordered row
can only ever hit the intended row or report a miss.

A row set that declares no printable capability rejects `--print` once during
preflight rather than emitting one failure per row. Per-row failures apply to a
print-capable row set after that preflight, including heterogeneous rows that do
not individually carry a payload.

After successful preflight, every selected print row in normal framed or
structured output emits a visible success or failure result. A heterogeneous
row that does not declare a printable payload, or whose payload cannot be
acquired, is not omitted. Other rows continue, and any failure makes the command
exit non-zero. Normal text frames every result with typed row identity; JSONL
and JSON-array output retain that identity in one complete object per row.
Plain `--json` retains its unary one-object contract and rejects multiple
selected rows. Unary `--bare` and unstructured `--out` report acquisition or
transformation failures as diagnostics with no payload envelope.

A printed document is the document the package shipped. Markdown conventions --
YAML frontmatter scoping through `--frontmatter`/`--body`, and rewriting GitHub
`blob` links to `raw` so the target is fetchable -- apply only to Markdown. A
document's kind comes from its extension, except for the package README, whose
kind comes from its role: the manifest declared it as the readme and NuGet
renders it as Markdown, so an extensionless or unconventionally named README is
still Markdown. That role follows the manifest declaration, not the file the
README section displays. A package that ships `README.md` and also declares a
different file has declared both readmes, and the declared one keeps its kind
even though the section shows the conventional name. The role answers only where
the extension is silent: a manifest can declare anything, and
`<readme>logo.png</readme>` is malformed but shippable, so a declaration never
overrides a name that says what the document is. Any dot in the file name counts
as saying something: `logo.png` names a suffix, `logo.png.` names one with a
stray dot after it, and `.png` spells one as a hidden basename. Telling a hidden
suffix from a hidden word like `.README` would take a list of known suffixes that
goes stale and still guesses wrong at the edges, so the tie goes to the
conservative reading -- refusing a scope on `.README` is loud and leaves the
document readable, while handing a declared PNG to the link rewriter returns a
corrupted file and exit 0.
Applied to anything else they are corruption rather than presentation: the link
rewriter matches bare URLs anywhere in the text, so a URL inside an XML element
or an MSBuild comment is rewritten and the printed manifest silently stops
matching the one the feed serves. Asking for a Markdown scope on a document that
is not Markdown is refused, because both other answers -- the whole document, or
an empty one -- report success for a question that was never answered. The
refusal belongs to the request, not to one flag, so `--content` refuses it on
the same terms as `--print`.

The refusal covers the whole request rather than skipping the documents it does
not apply to. A selection that matches Markdown and non-Markdown alike --
`--path "*" --frontmatter` -- is one request, and answering part of it while
dropping the rest reports success for files that were never scoped. The refusal
names the first such document so the selection can be narrowed, for example with
`--path "*.md"`.

This request-level scope preflight runs after filters and item, range, or
single-row selection establish the selected documents, but before payload
acquisition or output. It inspects only selected rows, so an unselected
non-Markdown row does not reject the request. If any selected row is not
Markdown, one preflight rejection preempts the per-row batch failure model; the
requested transformation itself is invalid rather than one row's payload being
missing or unavailable.

Normal `--print` stdout is a framed, visually encoded projection, even for one
row. Unary `--bare` removes the frame but remains terminal-safe rather than an
exact byte-transfer contract. A caller printing a manifest in order to hash or
diff it uses unary `--out`, which preserves the package bytes exactly,
including any byte order mark.

`-n N` and bare `-N` are semantic item windows applied independently to each
declared row set after filtering and ordering. `--head` names the first-N
direction explicitly, and `--tail` selects the last N items. Non-row sections
remain unchanged:

```text
--print -n 1
  select the first declared row -> emit its framed print success or failure

--print --rows 2..5 -n 20 --lines
  select rows 2 through 5 -> fetch each payload -> render its first 20 lines
```

`--rows` carries only absolute row ranges:

- `--rows 2..10` keeps the rows numbered 2 through 10 inclusive — nine rows.
- `--rows 2+10` keeps ten rows starting at row 2.
- `--rows 10..` keeps row 10 through the last row.

Count-form `--rows 6` and `--rows 6 --tail` retire in favor of `-n 6` and
`-n 6 --tail`. A range may intersect an `-n` or `--top` result without
renumbering stable row addresses.

In `package --all-libraries`, singular sections retain one table per library
for windowing even when a row format flattens them with provenance; aggregate
sections window the rolled-up table once. The paired
`PackageCommand_AllLibraries_RowFormats_WindowPerLibraryLikeMarkdownCount` and
`PackageCommand_AllLibraries_AggregateRowFormats_WindowAcrossRolledUpSection`
tests gate both scopes and their count/row-format parity.
`PackageCommand_AllLibraries_RowFormats_TailWindowMatchesMarkdownRows`,
`PackageCommand_AllLibraries_AggregateRowFormats_WindowSameRowsAsMarkdown`,
and `PackageCommand_AllLibraries_OpportunityRowFormat_WindowSameRowAsMarkdown`
gate selected-row identity at the window boundary.

A count and a range are different kinds, not two spellings of one: a count
anchors to an end and a range does not. Bare `--rows 2..10 --tail` is rejected.
`-n 20 --tail --rows 90..95` is valid because `--tail` belongs to the item
count; `--rows 2..10 --print -n 20 --lines --tail` is valid because it belongs
to the independent line window.

`--lines` changes the unit carried by `-n` from items to rendered lines. For an
ordinary report it windows the report; for multi-item `--print` it windows each
payload independently, excluding separators. `--tail-lines` is sugar for
`--lines --tail`. A single `-n` cannot carry both an item count and a line
count; use `--rows 1..M --print -n N --lines` when both dimensions are needed.

Printability is a row capability, not a property implied by Table or Vector
shape. Multi-item `--print` may not:

- reinterpret an address row as the artifact at that address;
- evaluate an unevaluated address;
- acquire content that the selected row did not declare.

A version-address Vector is therefore not printable merely because each row
could name a package. The explicit transition to that package artifact remains
`package Package@version`. Likewise, printing a timeline may use only declared
payloads on already evaluated rows; it cannot probe missing cells.

### A payload projection is never silently dropped

`--print`, `--value`, `--urls`, `--paths`, and `--count` reshape the payload, so
a render path that ignores one answers a question the caller did not ask while
still exiting 0. That failure is invisible to exit-code checks and to tests that
only cover the unprojected path.

Every accepted payload projection must therefore end in one of two outcomes: the
payload is projected, or the command reports why it cannot be and exits non-zero.
Rendering the full unprojected shape is not a third option. The requirement is
enforced structurally rather than per command — the request is recorded from the
parse result and the projection writers report which projection they honored, so
a route that drops one fails loudly instead of shipping the wrong payload.

The whole-surface type listing (`type` with no type name — the Classes, Structs,
Interfaces, Enums, and Delegates sections) is a name table that exposes no
printable payload, so `--print`/`--value`/`--urls`/`--paths` there is rejected up
front rather than dumping the full surface and then tripping this audit. Inspect
a single type (for example `type <Name>`) to project a member payload. `--count`
is the one payload projection the surface does honor.

Writers report *which* flag they honored rather than merely acknowledging one.
A writer can be reached for more than one reason — the print writer also serves
`--bare` — so an untyped signal would let it satisfy an unrelated request and let
that drop escape.

The projections are mutually exclusive. Two of them cannot both shape one
payload, so a combination is rejected before the command runs rather than
resolved by discarding one.

### Lens modes project their own payload

A few requests select a *lens* rather than a section of the normal document:
`package --versions`, `--layout`, `--tfms`, and `--content`, along with
`library coordinate --file` and the `-D`/`--discover` listing. Each renders a
payload it computes itself and returns before the section pipeline, so the
section-selection vocabulary does not describe what the caller is looking at.

The lens payload is still a payload, so the two-outcome rule above applies
unchanged. Because the lens owns the shape, its answers are fixed:

- `--count` counts the lens payload — versions, target frameworks, package
  files, IL offsets, discovered artifacts — not the lines used to render it. A
  layout count is a count of files, even though the rendered tree also shows the
  directories that contain them.
- `--content` yields one structured row per matched file despite rendering
  text, so its count is the number of files matched.
- A `--content` path that matches nothing in a package still renders a
  per-package placeholder — `(absent)` in the block render, a `found:false` row
  in `--jsonl` — and that placeholder is **not** counted. The count answers *how
  many files did I get content for*, so counting placeholders would report
  matches that did not happen. The placeholder is presentation, which is why
  `--skip-empty` removes it and `--bare` never emits it: under `--skip-empty` the
  rendered rows and the count agree exactly. This is the one place the count is
  deliberately smaller than the default render's row total.
- An opaque lens payload refuses `--print`, `--value`, `--urls`, and `--paths`
  with the reason rather than inferring structure from rendered text. A lens
  that declares rows and their capabilities composes with ordinary projections:
  for example, version rows may expose URLs. A version row set that declares no
  printable capability rejects `--print` once during preflight.
- `-S`/`--select` is refused when the caller typed it, rather than ignored. A
  lens and a section selection are competing answers to *what am I looking at*,
  and silently honoring the lens hides that the selection did nothing.

There is deliberately no lens for printing documents. A flag that names a
particular document is a second answer to *which documents*, competing with the
section and row selectors, and the two can disagree — which is exactly how a
lens that printed the package README came to print the XML manifest through the
README's Markdown pipeline. Printable documents are therefore reached only by
selecting the section that lists them, narrowing its rows, and applying
`--print`.

#### Payload stdout is visually encoded; exact export is explicit

Everything a rendered surface shows is *contained*: untrusted metadata names,
attribute text, doc text, and nuspec fragments have their line terminators
folded and their rendering hazards (VT, ANSI escapes, bidi overrides, LS/PS)
rewritten as visible `\uXXXX`, so they cannot escape a table cell, a code
fence, a tree gutter, or a diagnostic line (issue #3319).

Printing documents (`-S "Package README file" --print`) and `--content`
visually encode rendering hazards on stdout. Exact payload transfer is an
explicit unary file operation: add `--out <path>` to a selection that resolves
one payload. An unscoped file export preserves the package bytes exactly,
including encoding, byte order mark, and line endings, except for package skill
documents: skills are agent instructions, so every route, including
`project -S Skills --print`, `package -S "Package skill files" --print`,
`--content`, and a package README declaration, classifies through a
`TextPolicy.Prose` `InertString` and carries one containment-selected value
through stdout, structured output, and `--out`. The raw scoped skill is
classified before link normalization; concerning text becomes the standard
placeholder, safe text retains its full presented spelling, and exact package
bytes are not retained. The placeholder remains the selected stdout,
structured-output, or `--out` value. A successful containment replacement also
writes one warning to stderr: it names the skill document and reports at most
eight contiguous same-scalar source ranges by one-based line and column,
Unicode code point, and category without reproducing the source text. A final
detail reports any additional range count. Skill destinations therefore accept
rendered line windows; `PackageSkillDestinations_ApplyLineWindowsToSelectedText`
gates safe text and the containment placeholder across stdout and file output,
and `SkillDocuments_ReportBoundedContainmentRanges` gates the split-channel,
bounded diagnostic. A Markdown scope exports projected text.
Terminal-facing output never emits a live control or bidi scalar from package
content. Multi-item
`--print --out` and multi-file or multi-package `--content --out` are refused
unless a structured JSON shape owns the destination; global selection
cardinality is resolved before any selected payload is read, and a unique exact
payload is read from the same retained package acquisition that supplied its
selection metadata. Narrow it with row
or path selectors for exact transfer.
Unstructured exact `--out` rejects line windows because clipping would no
longer be exact. Every refused export is decided before opening its destination:
an absent path stays absent, and an existing file remains byte-for-byte
unchanged.

The historical target gives `--print` unary `--bare` and `--out` companions;
this is not a universal statement of implemented command options. In particular,
the adopted `type`/`member` source paths use native payload output by default
and explicit `--markdown` for document presentation, as defined by the rendering
owner above. Structured
multi-item `--out` is a different mode: after atomic preflight it may publish
complete result records incrementally, including typed row failures, as
described by the historical #4677 target. It remains pending focused L3
payload-projection ownership and gates.

Tool-authored companion sections still use the stream split: for example,
`package X -S "Package README file" --print --info` writes the framed, encoded
document to stdout and the `# Info` table to stderr.

Two consequences define the boundary:

- `--jsonl` preserves ordinary payloads as JSON string values. The wire format
  escapes control characters as required by JSON; parsing reconstructs the
  original ordinary payload. A package skill document that requires containment
  is omitted before serialization, so parsing returns
  `[Text omitted: required containment]`.
- `--content` and target `--print` write framing to stdout. `--content`
  delimits each matched file with a
  `------------ <package> :: <path> ------------` banner; `--print` uses its
  row-identity and line-metadata frame. Every frame field is contained.
  `--print` additionally prefixes each terminal-safe payload line with a
  tool-owned `|` followed by one space, so payload text cannot forge a sibling
  frame.

These are gated by `PayloadLensContainmentTests`, which runs the built CLI over
a package whose README carries bidi, ESC, and LS hazards and asserts encoded
stdout, contained stderr, parsed JSON payload fidelity, and exact `--out`
export. `PackageContentOutput_ContainsNoLiveControlsOnStdoutAndPreservesExplicitFileExport`
gates both framed and `--bare` single-file content export with a UTF-16 payload
that has no trailing newline. The target
`MultiPrintFrameFieldsAreContained` gate applies the same adversarial coverage
to every `--print` frame field, and `MultiPrintPayloadCannotForgeFrames` covers
frame-shaped payload lines and line-ending edge cases. Package skill output is
gated separately by `SkillDocuments_OmitPayloadsThatRequireContainment` and
`SkillDocuments_OutputAliasesWritePackageAndProjectPayloads`.

Discovery (`-D`/`--discover`) is a lens for the projections above but not for
`-S`, which legitimately narrows what discovery reports. Its own `--count` must
come from the discovered rows; the surrounding command's document count is a
different payload that happens to be a plausible-looking number.

`-S` here means an explicit selection. Some options are sugar that synthesize a
selection internally, and a synthesized one must not be mistaken for a request
the caller made.

### Presentation modifiers (render the chosen shape)

These modifiers describe content output. For an adopted envelope-producing
route, unprojected `--json` means the owner-issued Content value under
[the content/service boundary](#service-and-content-serialization-boundaries),
not a rendering of the service envelope.

| Flag | Effect |
| --- | --- |
| `--markdown` | force the full Markdown Document format |
| `--json` | render the selected shape as JSON: the whole Document when no narrower shape is selected, otherwise the projected payload (`--print`, `--value`, `--urls`, `--paths`). Accepted lenses and payload projections claim their own output first. Plain document `--json` keeps the pre-lowered typed document; an otherwise-unclaimed, non-empty `--fields`/`--columns` request names lowered vocabulary and opts into the lowered display view (#3494), with the same machine table keys as `--jsonl` and with semantic item/range windows and `--compact` preserved. `find` and `vocabulary` currently wire lowered document paths, while discovery owns projected JSON under its lens contract; unadopted projection-capable routes reject unsupported combinations before typed JSON serialization. Complete structured values for the historical item/line target remain unverified and await focused ownership; `ProjectedJsonWindowingTests` covers only its named current projected-JSON paths. See [Projected JSON output](projected-json.md) for routing, representability, diagnostics, and compatibility. |
| `--tsv` / `--jsonl` | render the single selected section as TSV / JSON Lines (a Table or Vector) |
| `--table` | render the single selected section as a space-padded pretty table |
| `--no-header` (`--no-headers`) | drop the Table header row |
| `-n N` / numeric shorthand such as `-20` | keep the first N declared items per row set |
| `-n N --head` | keep the first N declared items with the default direction explicit |
| `-n N --tail` | keep the last N declared items per row set |
| `--rows N..M` / `--rows N+K` / `--rows N..` | keep the **rows those stable numbers name**, inclusive; absolute, so no item direction applies |
| `-n N --lines` | keep the first N lines of the rendered report, or of each multi-print payload |
| `-n N --lines --head` | keep the first N lines with the default direction explicit |
| `-n N --tail-lines` | keep the last N lines; sugar for `--lines --tail` |
| `--bare` | render the selected payload without document decoration; multi-item print rejects it because framing carries row identity |
| `--plaintext` | render a whole-document plain-text view; distinct from `--bare` |

`--tsv`/`--jsonl`/`--table` render **one section at a time**, so they require a
Table-or-narrower selection; multi-section (Document) output stays in Markdown or
JSON. `--print` likewise requires exactly one declared row set, though it may
project every selected row in that set.

### URL-shape modifiers (orthogonal to the ladder)

| Flag | Effect |
| --- | --- |
| No flag | emit direct, fetchable content URLs |
| `--prefer-rendered-urls` | prefer a rendered browser view when a supported provider mapping exists; otherwise retain the original URL |

The CLI owns this preference. It changes emitted links, not the selected shape,
payload framing, or source acquisition. Structured source-print output keeps
the selected presentation URL in its `url` field, not the acquisition URL.
`--bare` still removes document decoration; `--print` still requests content.
The former `--raw` and `--blob` flags are removed, not retained as aliases.

Conversion is provider-aware. GitHub raw-content URLs use the existing
SourceLink browse mapping, and GitHub's `/owner/repo/raw/ref/path` route can
select its `/blob/` view. An unknown provider or unsupported URL stays unchanged:
the presence of `/raw/` elsewhere in a URL does not authorize rewriting it.
General source URL presentation preserves an authored fragment across either
supported GitHub form. This preference does not add network probes or new
provider support and does not broaden provenance attribution.
Coordinate source-line locators replace any existing fragment on the selected
URL; composing the locator preserves that URL's path and query.

Package README and skill presentation preserves the existing distinction:
the default normalizes authored GitHub file links to fetchable form, while
`--prefer-rendered-urls` preserves authored links verbatim. It does not rewrite
every link inside an authored document into a browser view. Exact-content
transfer retains its existing byte-preservation rules.

This one-step CLI adoption, tracked by #7619, replaces option registration,
parsers, and consumers together; Browser/Wasm does not parse these flags and
gains no new UI or policy.
Existing typed sections, Markout rendering, and payload lowering are unchanged.
Real evidence uses `Newtonsoft.Json@13.0.3` source links and this repository's
[SourceLinkService.cs at 0cdbe500d](https://github.com/richlander/dotnet-inspect/blob/0cdbe500d11cb77ae7fb3c8612a5ba7bcc83ff86/src/ILInspector.SourceLink/SourceLinkService.cs)
source URL. PR-fast CLI parsing and service URL-conversion tests cover unknown
providers and literal `/raw/` path segments; focused package/member/library
source-output tests cover the production preference and unchanged default.
`SourceUrls_PreserveAuthoredFragment` covers type/member/library output across
both supported GitHub forms and an unknown provider under both preferences;
`SupportedGitHubSource_PreservesAuthoredFragment` covers general, line-range,
and escaped fragments in the shared mapping.
`SourcePrint_EmitsPreferredUrlAndUnchangedContent` covers type/member JSON,
JSONL, and JSON-array source printing with both preferences, authored fragments,
both GitHub forms, and unknown-provider fallback, preserving the acquired text.
`CoordinateUrls_ApplyPreferenceAndPreserveLine` covers production coordinate
output for both GitHub forms, the default, replacement of existing fragments
with line locators, preserved queries, and unknown providers.
`RenderedUrlPreference_SourceLocationRetainsUnmappedUrl` also covers
the Research fallback. `TypeSourceFilesPrint_SelectsExactRepositoryDocument`
proves that preferring rendered URLs does not change selected authored text.

### Walking the ladder — one example

```bash
# Document: the whole assembly report
library MyLib.dll

# Table: one section
library MyLib.dll -S "Top Leverage"

# Vector: one column of that table
library MyLib.dll -S "Top Leverage" --fields Member --tsv

# Scalar: collapse the table to a count …
library MyLib.dll -S "Top Leverage" --count
# … or render a blob payload without decoration
member MyType Method:1 --library MyLib.dll -S "Decompiled Source" > Method.cs
```

### Case study: IL offset as a shape catalogue

`library coordinate` is a compact example of the shape ladder because one
resolved coordinate can expose multiple sibling sections. The source-location
section is useful as a human fact sheet, a row, a scalar, a URL, a path, or a
source-line payload; the member-context section projects the same coordinate to
the owning type and method; the instruction-context section projects it to the
exact IL instruction; the exception-context section appears when the coordinate
falls inside protected exception-handling regions; callsite and return-address
sections explain call-like operations and stack-frame return addresses.

The default stays evidence-oriented and renders all applicable coordinate-scoped
sections:

```bash
dotnet-inspect library coordinate 0x06000002+0x1 --library My.dll
```

```md
## Context: Source Location

| Field | Value |
| ----- | ----- |
| Method | My.Type.Method |
| Token | 0x6000002 |
| IL Offset | 0x1 |
| File | /_/src/Foo.cs |
| Line | 42 |
| Url | https://raw.githubusercontent.com/org/repo/sha/src/Foo.cs#L42 |

## Context: Member

| Field | Value |
| ----- | ----- |
| Assembly | My.Assembly |
| Type | My.Type |
| Type Kind | class |
| Member | My.Type.DoWork |
| Signature | int DoWork(int value) |
| Member Kind | method |
| Visibility | public |
| Static | No |
| Async | State machine |
| Metadata Token | 0x6000002 |
| IL Offset | 0x1 |

## Context: Instruction

| Field | Value |
| ----- | ----- |
| IL Offset | 0x1 |
| Boundary | Exact |
| Opcode | callvirt |
| Operand Kind | Method |
| Operand | MyApp.IWorker::DoWork(int) |
| Operand Token | 0x06000020 |
| Next Offset | 0x6 |
| Length | 5 |
| Block | 0 |
| Terminates Block | No |
| Falls Through | Yes |

## Context: Exception

| Region | Context | Clause | Try Range | Handler Range | Caught Type |
| ------ | ------- | ------ | --------- | ------------- | ----------- |
| 1 | try | catch | IL_0010..IL_0045 | IL_0045..IL_0070 | System.TimeoutException |

## Context: Callsite

| Field | Value |
| ----- | ----- |
| Call Offset | IL_0001 |
| Opcode | callvirt |
| Call Kind | virtual |
| Callee | MyApp.IWorker::DoWork(int) |
| Operand Token | 0x06000020 |
| Return Address | IL_0006 |
```

If the coordinate is the return address after the call, the applicable section
changes:

```md
## Context: Return Address

| Field | Value |
| ----- | ----- |
| IL Offset | IL_0006 |
| Call Offset | IL_0001 |
| Opcode | callvirt |
| Call Kind | virtual |
| Callee | MyApp.IWorker::DoWork(int) |
| Operand Token | 0x06000020 |
```

Coordinate-scoped sections are discoverable only when the coordinate carrier is
present:

```bash
dotnet-inspect library My.dll -D
# Context: Source Location, Context: Member, Context: Instruction, Context: Exception,
# Context: Callsite, and Context: Return Address are omitted.

dotnet-inspect library coordinate 0x06000002+0x1 --library My.dll -D
# Context: Source Location
# Context: Member
# Context: Instruction
# Context: Exception (only when applicable)
# Context: Callsite (only when applicable)
# Context: Return Address (only when applicable)
```

The source-location section then projects cleanly:

```bash
# Scalar
dotnet-inspect library coordinate 0x06000002+0x1 --library My.dll \
  -S "Context: Source Location" --fields Line --value
# 42

# URL vector (one row)
dotnet-inspect library coordinate 0x06000002+0x1 --library My.dll \
  -S "Context: Source Location" --urls
# https://raw.githubusercontent.com/org/repo/sha/src/Foo.cs#L42

# Path vector (one row)
dotnet-inspect library coordinate 0x06000002+0x1 --library My.dll \
  -S "Context: Source Location" --paths
# /_/src/Foo.cs

# Printable payload: the visually encoded resolved source line
dotnet-inspect library coordinate 0x06000002+0x1 --library My.dll \
  -S "Context: Source Location" --print --bare
#         return JsonSerializer.Serialize(value, options);

# Singleton count
dotnet-inspect library coordinate 0x06000002+0x1 --library My.dll \
  -S "Context: Source Location" --count
# 1
```

This keeps the concerns separate: the default fact section shows all
symbolication evidence, `Context: Member` shows the owning metadata context,
`Context: Instruction` shows the exact IL operation, `Context: Exception` shows
active exception-handling regions, `Context: Callsite` shows the call-like
operation at the coordinate, `Context: Return Address` points back to the prior
call, `--urls` returns the anchored source location, `--paths` returns the PDB
document path, and `--print --bare` returns the visually encoded payload at the
location without the normal frame or gutter. Use `--print --out <path>` instead
for exact payload export.

## Design discipline for future flags

The stable vocabulary is:

- `--count` is a terminal shape reduction over the logical rows surviving every
  preceding semantic selection stage. One declared row set collapses to a
  Scalar; multiple sets produce an ordered count Table.
- `-n N` / bare `-N` select the first N declared items per row set after
  filtering and ordering. `--head` names that direction explicitly and
  `--tail` reverses it when the producer can establish a truthful suffix.
- `--rows` selects absolute stable row ranges and carries no count-only form.
- Normal `--print` projects every selected row to one framed or structured
  success/failure result. Unary `--bare` and unstructured `--out` carry no
  result envelope. None of these modes invents printability or evaluates new
  addresses.
- `--lines` changes the `-n` unit to rendered lines. For multi-item print the
  line window applies independently to each payload.
- `--head` / `--tail` name a direction, not a count. They require and modify an
  active item or line `-n` window; they never modify an absolute row range or
  ranking.
- `--row` addresses a rendered row by its position in the section, counting from
  1. Any future selector that takes an ordinal joins this rule: the number a
  reader arrives at by counting rows is the number that can be addressed, and no
  later item/range window or projection may renumber it.
- `--bare` is a presentation modifier: for one selected payload, it strips the
  surrounding frame and payload gutter.
- `--prefer-rendered-urls` is a URL-shape preference, not a payload-shape or
  decoration modifier.
- `--plaintext` remains distinct from `--bare`; if it stays in the product, it is
  a whole-document plain-text rendering mode rather than a bare-payload mode.
- `library coordinate` supplies coordinate input that has no other expression
  and gates the sections it makes meaningful. Coordinate input does not narrow
  a shape, and syntax qualifies for this family only if its input is a new
  currency. Exact and file IL coordinates spell the same currency, so they are
  one member; metadata heap coordinates are the second.

New flags should fit one of those buckets rather than blending concepts.
