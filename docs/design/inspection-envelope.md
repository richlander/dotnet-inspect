# Inspection envelope

Status: **proposed**.

This design owns one cross-cutting result pattern:
`InspectionEnvelope<TContent>` is the host-neutral boundary around one
classified owner-issued content value, one required portable projection, and
cross-host diagnostics. The CLI consumes that baseline envelope. Broader hosts
consume the same baseline and may compose additional owner-issued content or
host-owned experience state around it without changing the baseline.

The optional [service-evidence enrichment](#service-evidence-enrichment)
composes that baseline with a second owner-issued type. It is an implemented
extension of this envelope pattern, not another primary-content model.
Host-visible evidence capture and complete evidence-envelope delivery are
available only in Debug builds until a separately approved retail adoption
demonstrates a user need.

The
[host-observable content kinds](host-observable-content-kinds.md)
contract classifies that owner-issued value as a Result, Document, or
owner-specific Outcome and defines its serialization-ready boundary.

The implementation tracker is
[#6710](https://github.com/richlander/dotnet-inspect/issues/6710).
The prerequisite content/share plan is
[#6717](https://github.com/richlander/dotnet-inspect/pull/6717), and the first
implementation adoption is
[#6712](https://github.com/richlander/dotnet-inspect/issues/6712).

## Claim

For one resolved semantic plan, every host receives the same content
classification, content, portable projection, and diagnostics:

```text
InspectionEnvelope<TContent>
  ContentKind: Result | Document | Outcome
  Content: TContent
  PortableProjection:
    Available(FullUrl, Packet)
    | NonProjectable(Path, Reason, Explanation)
  Diagnostics
```

`TContent` remains the content type issued by the inspection owner. The
envelope does not replace that type, reinterpret its facts, or become a
universal inspection-content model.

The envelope owns:

- the non-null owner-issued content value;
- its explicit semantic kind;
- one required portable projection for the same semantic plan; and
- an immutable ordered sequence of cross-host diagnostics.

Other information may join the envelope only when a separately demonstrated
cross-host need establishes one common meaning. Adding an untyped metadata or
action dictionary is not an extension mechanism.

## Motivation

The CLI and Inspect Web already need the same semantic results and portable
scenario projection but currently adapt supplemental evidence independently.

The type-dependency path demonstrates the split:

- shared Queries and Sections produce dependency facts, participant outcomes,
  traversal evidence, and selected relationship rows;
- CLI `TypeDependencyExecutionResult` combines those facts with scan
  diagnostics, availability, and row-selection failure;
- `BrowserTypeMetadata` combines projected type facts and graph content with
  stringified `InspectionFailures`; and
- Inspect Web adds Research-owned derived relationships, navigation, and
  interactive rendering that the CLI does not request.

The duplicated wrappers make it easy for one host to drop a diagnostic,
translate a failure into empty content, or attach supplemental evidence to a
different semantic result. Moving the common minimum into a host-neutral
envelope lets the CLI use the baseline directly and lets the Browser build a
richer experience without privately extending the shared content.

This is not a claim that both hosts request the same amount of information.
Equal semantic plans produce equal baseline envelopes. A broader host requests
additional owner-issued results and composes them explicitly.

## Adoption evidence

This generic pattern does not prescribe a package, subject, or fixture.
Authentic assets and expected facts belong to each adopting inspection owner.
The first adoption uses the already-merged real-package evidence in #6709 and
the focused host integration in #6712.

The envelope-specific evidence is cross-host equality and boundary behavior:
the adopting tests must prove the same content kind, content, portable
projection, and diagnostics for equivalent plans, plus the non-projectable
case defined here.

## Boundary

The envelope sits where a completed host-neutral operation hands one detached
result to a host:

```text
resolved basis
  -> one content plan plus required portable projection
  -> owner-issued result
  -> portable projection
  -> InspectionEnvelope<TContent>
  -> CLI or Browser composition and presentation
```

It does not wrap every producer, prerequisite query, Workspace operation, or
internal intermediate. An operation with no L2 owner may envelope its final L1
result. An operation with an L2 section or inspection owner envelopes the L2
result rather than nesting envelopes around each prerequisite.

Execute and Discover return envelopes around their own content result types.
One envelope never authorizes both Execute and Discover. CLI `--share` does not select another content state; it asks the host to present
the same envelope's portable projection alongside ordinary content.

## Primary content

`Content` contains one non-null owner-issued value. Its owner continues to
define:

- semantic facts and identities;
- success, partial, unavailable, and failure variants;
- completion and traversal evidence;
- provenance and participant correspondence;
- section and row identities, ordering, selection, and Count; and
- domain-specific structured diagnostics that are themselves semantic
  evidence.

The envelope does not impose one universal success/failure union. If an owner
cannot produce valid content, its existing typed non-success remains the
operation outcome; an empty value or diagnostic is not an equivalent
substitute.

Wrapping content does not change its equality, row count, ordering,
serialization meaning, or resource ownership. A host may lower or render the
value, but it may not add facts to it after the envelope crosses the shared
boundary.

The content contract requires a settled, serialization-ready snapshot, not a
particular CLR collection implementation. Arrays and other ordinary collection
types may represent serialized sequences; `ImmutableArray<T>` is not required
merely because content crosses the host boundary.

## Portable projection

Every envelope has one `PortableProjection` value derived from the same
resolved basis:

```text
InspectionPortableProjection
  = Available(FullUrl, Packet)
  | NonProjectable(Path, Reason: NotSupported | Invalid | Incomplete
                    | Unavailable | Failed)
```

`Available` contains both the complete canonical production URL and the
canonical encoded Workspace packet carried by that URL. Consumers may use
either representation or both; they do not split the URL to recover the
packet. Hosts do not rebuild either value from argv, rendered content, display
names, Browser navigation, or the current origin.
The producer supplies both values when constructing `Available`; the portable
contract does not derive one by parsing the other.

`NonProjectable` identifies the semantic path and closed reason that prevented
a faithful representation. `NotSupported` means the admitted operation has no
defined projection, `Invalid` means the state violates the projection
contract, `Incomplete` means required settled evidence is incomplete,
`Unavailable` means required portable state is absent, and `Failed` means the
defined projection could not complete. The reason is typed owner output, not
free-form presentation text. The trusted owner-issued `Explanation` supplies
display text; hosts may render it but must not invent or reinterpret the
projection-failure meaning.
The outcome contains no partial URL and never drops, defaults, or approximates
semantic state to manufacture one.

Portable projection performs no ordinary content execution or effectiveness
probe. A non-projectable outcome does not invalidate independently valid
content. A host that explicitly requests `--share` presentation may classify the
missing requested side output as unsuccessful without discarding or changing
the content.

Packet schema, canonical encoding, capacity, and restoration remain owned by
Workspace Definitions. Public CLI packet-versus-URL selection and exit
behavior remain owned by CLI Workspace Sharing.

## Diagnostics

Diagnostics are common supplemental currency because both hosts need to
disclose useful evidence that is not itself content or the portable projection:

- one Workspace participant was rejected while neighboring participants
  produced useful content;
- a requested root could not be certified;
- a strict semantic selection failed;
- a source or producer was unavailable without invalidating an independent
  result; or
- a result is valid but carries an owner-issued limitation that users need to
  see.

One `InspectionDiagnostic` has:

- a stable owner-scoped code;
- `Information`, `Warning`, or `Error` severity;
- a contained summary suitable for cross-host display; and
- optional correspondence only through an existing owner-issued identity.

The code is semantic identity; the summary is presentation input. Hosts do not
parse the summary to recover kind, severity, subject, participant, or failure
meaning. Artifact-authored text is contained before entering the diagnostic.

Diagnostics are immutable and deterministic under deterministic inputs.
Envelope assembly preserves owner order when it already exists and otherwise
uses a documented stable composition order. A code may occur more than once
when distinct owner-issued identities distinguish the occurrences.

Severity alone does not determine operation success, CLI exit status, retry,
or Browser navigation. Those decisions remain with the content and PortableProjection
owners plus host policy. In particular:

- an `Error` diagnostic may accompany a valid partial result;
- a `Warning` cannot hide owner-issued incomplete evidence;
- an empty diagnostic sequence does not certify completeness; and
- adding a diagnostic cannot change content rows or selection.

A diagnostic cannot replace a typed content non-success,
`PortableProjection.NonProjectable`, or a missing required portable projection value.

Verbose logs, traces, tips, performance telemetry, exception stack traces, and
host-authored convenience messages are not inspection diagnostics.

## Service-evidence enrichment

Status: **proposed**, tracked by
[#7116](https://github.com/richlander/dotnet-inspect/issues/7116).
This section owns the generic enrichment contract. It does not define each
service's evidence schema or capture algorithm.
The Debug-only availability policy is tracked by
[#7271](https://github.com/richlander/dotnet-inspect/issues/7271).
Diagnostic-attachment delivery is tracked by
[#7293](https://github.com/richlander/dotnet-inspect/issues/7293).

**Service evidence** records additional inputs, selections, intermediate
facts, and decisions involved in producing an inspection. It need not form an
explanation of why the answer follows. Neither debugging nor build
configuration defines its meaning.

Content retains all evidence required to interpret the answer, including its
failure and completeness meaning. Ordinary Diagnostics retains required
operational notices. Neither may depend on requesting optional service
evidence. Facts can be used in both views, but enrichment is not a reason to
remove required information from the baseline.

### One baseline, two typed forms

```text
InspectionEnvelope<TContent>
  Content: TContent
  PortableProjection
  Diagnostics: InspectionDiagnostic[]

EvidenceInspectionEnvelope<TContent, TEvidence>
  Inspection: InspectionEnvelope<TContent>
  Evidence: TEvidence
```

The enriched form uses composition, not CLR inheritance. It contains one
non-null baseline and one non-null evidence value; it does not redeclare or
independently construct another Content, PortableProjection, or diagnostic collection.
An ordinary consumer can use `Inspection` without knowing `TEvidence`.
Services without evidence support keep the one-generic baseline and need no
dummy evidence type.

`TEvidence` is a named owner-issued Document or, where admitted requests
cannot always produce that Document, an owner-specific Outcome. It consumes
the [content-kind vocabulary](host-observable-content-kinds.md), including
settled snapshots and visible partial or non-available states. The generic
envelope introduces no universal evidence Outcome, untyped object dictionary,
or diagnostic-message encoding of structured evidence.

The issuing service associates both values within the same invocation.
Evidence joins use existing owner-issued subject, population, and plan
identities, not rendered labels or a reconstruction from output. This pattern
adds no new global operation identity or receipt.

### Request and capture boundary

The ordinary and evidence-enabled entry points expose distinct, statically
known return forms for the same inspection operation:

```text
ordinary request         -> InspectionEnvelope<TContent>
evidence-enabled request -> EvidenceInspectionEnvelope<TContent, TEvidence>
```

Each service owns its concrete request and entry-point spelling. A runtime
option may select a typed entry point, but must not make one return type
sometimes conceal an enriched value as `object` or as a bare baseline.
Producing an enriched result performs one inspection, not an ordinary
inspection followed by a second run to obtain evidence.

`EvidenceInspectionBuilder<TContent, TEvidence>` is the shared host-selection
helper. Its
`[Conditional("DEBUG")]` request method makes Release callers omit the capture
request and its argument evaluation. The builder accepts operation state and
ordinary and evidence-enabled static delegates, invokes exactly one, and cannot
be reused for a second execution. Synchronous and asynchronous forms return the
ordinary inspection plus an optional evidence envelope. When enriched, both
tuple members reference the same `InspectionEnvelope<TContent>`; the tuple is
host orchestration rather than a third service return form.

The host resolves both capture intent and delivery intent before execution.
Delivery may replace a host's primary output or accompany its ordinary Content
presentation as a diagnostic attachment. That channel choice does not create a
third service return form, change `TContent` or `TEvidence`, or permit capture
after the ordinary result has already been produced.

Capture intent is resolved before execution. Additional observation work must
have owner-declared costs, bounds, and capability requirements; requesting
serialization is not permission to acquire data or recapture evidence while
writing the result. The ordinary semantic plan remains the basis of Content
and PortableProjection rather than being broadened into another inspection by capture.

For equivalent ordinary inputs and plans, enabling evidence preserves the
baseline's Content, PortableProjection, and normal Diagnostics. An evidence-only
observation failure belongs to the evidence owner's completion or non-success
case; a failure affecting the inspection still requires the baseline's normal
failure disclosure. Requested evidence that cannot be produced must be
represented honestly or rejected at admission, not replaced by a bare
baseline, `null`, or an unexplained empty success. Cancellation and unexpected
exceptions retain their existing operation-failure meaning.

Availability is a separate host capability policy. Hosts expose
evidence-enabled capture and complete evidence-envelope delivery only in Debug
builds. The service-owned types, capture contract, and serializers remain
configuration-neutral so their correctness gates run in Release. Debug
availability does not rename the type, change its meaning, or make required
diagnostics optional. It also does not turn complete delivery into an ad-hoc
dump: an exposed Debug transport remains a typed, versioned, supported
contract.

Release builds retain the ordinary envelope and no host gesture or export that
reaches evidence capture or complete evidence-envelope delivery. That
composition absence is **unverified** by explicit user choice; the policy does
not add a source scan or host-registration absence gate.

Retail promotion is a separate focused adoption decision. It must name one
user scenario and production host, explain why baseline Content and Diagnostics
are insufficient, and define the public gesture, acquisition and cost policy,
and compatibility surface. The existence of the shared type, a Debug consumer,
or host parity does not by itself establish that need.

### Equality, lifetime, and delivery

Extracting `Inspection` preserves the existing baseline equality contract.
Equality of two enriched values additionally compares their owner-issued
Evidence values. Equal baselines alone do not make two enrichments equal.
Cross-host agreement on baseline values remains independent of capture;
evidence equality applies under the evidence owner's declared inputs and
capture contract, not merely because two inspections produced the same answer.

Evidence inherits the envelope's [resource-free lifetime](#safety-and-lifetime)
and contained-data boundary. It is a settled value, not a live service,
Workspace borrow, callback, credential container, or unbounded logging stream.
An evidence section may render that value; rendering does not own its
collection or association.

The logical serialized enrichment keeps Content, PortableProjection, and Diagnostics at
their baseline paths and adds Evidence alongside them. CLR composition does
not require a nested serialized `Inspection` object or a duplicate baseline:

```text
baseline wire value:  Content, PortableProjection, Diagnostics
enriched wire value:  Content, PortableProjection, Diagnostics, Evidence
```

These are logical member names, not a new casing or framing standard.
Transport and generated facade contracts must preserve the complete concrete
closed type. Serializing only `Inspection` is a baseline projection, not
delivery of the enrichment. Serializing only `Evidence` also loses the
same-invocation baseline association and is not complete enriched delivery.
Both hosts must preserve the same service-issued values; they may choose
different views over them.

A diagnostic attachment is complete when it contains the same full closed
enrichment that a primary envelope transport would carry. The host may
simultaneously render or serialize ordinary Content through its established
primary-output contract. Both products derive from one settled enriched value;
the attachment is not a second inspection, a logging stream, or a reconstruction
from rendered output.

An attachment publication failure occurs after service result construction. It
cannot mutate or reinterpret the settled baseline or become an evidence-owner
non-success. The host must disclose that complete enriched delivery failed; its
output and exit policy belongs to the host transport owner.

The approved Debug CLI adoption is tracked by #7293. Its output owner defines
the `--evidence-envelope` destination, primary-output interaction, publication,
diagnostic, and failure rules. This generic pattern does not define a path,
stream, file descriptor, atomic-write mechanism, parser rule, or host exit
status. Release CLI builds still do not register an evidence-delivery gesture.
Unprojected content JSON represents the baseline Content value, not Evidence.

### Motivation and production adoption

The existing
[`depends` diagnostic sections](dependency-inspection-command.md#sections-and-disclosure)
provide the concrete motivation: Roots, Dependency Groups, Restored Packages,
and Restored Edges expose structured service facts through CLI-only sections.
Some selections request extra evidence phases, so moving their output alone
would not establish service-owned capture. This repository's restored
`src/DotnetInspect.Cli/DotnetInspect.Cli.csproj` is a real input exhibiting those
facts; the dependency adopter owns its reproducible fixture and expected data.

The approved host path has five steps:

1. Lock this envelope-pattern extension in #7116.
2. Permit complete diagnostic-attachment delivery in this generic owner under
   #7293 without defining a host transport.
3. In [dependency adoption #7117](https://github.com/richlander/dotnet-inspect/issues/7117),
   classify required versus supplemental facts and define the service-owned
   evidence type and capture request. Scope any prerequisite shared-service
   extraction there, not inside this generic pattern.
4. Have the CLI output owner define and implement the Debug sidecar consumer,
   including complete closed serialization while ordinary Content output
   continues.
5. Deliver the same enrichment through Debug Browser/Wasm and an owner-selected
   inspection-evidence view or attachment.

Steps 4 and 5 may land together; Debug adoption is not complete until both
hosts consume the service-issued enrichment. This Debug-only scope is an
explicitly approved exception to retail adoption; later retail work follows
the promotion boundary above. Each adopter owns its focused contract and
gates. Existing diagnostic sections remain supported under their current owner
until replacement coverage exists. Retire duplicated production after
adoption; useful sections may remain thin views of shared evidence.
The broader Compare path remains tracked by #7213.

Planned Release correctness gates must cover baseline preservation,
same-invocation correspondence, complete/empty and bounded/non-available
evidence, retained failure disclosure, detached lifetime, and serialization
without recapture. They must verify the logical flat wire projection,
owner-specific Outcome cases, complete primary and attachment delivery from the
same settled enrichment, and agreement between runtime JSON and generated
Browser types. Adopters demonstrate their Debug host gestures and views
against that product-owned construction; those demonstrations do not replace
the Release correctness gates or verify the Release host-surface absence.
These implementation properties are **unverified** until the adoption gates
exist. The generic composition type and Debug host-selection helper are
implemented; no command-specific capture request, destination, or section
migration exists yet. This is not a general logging or tracing design.

## Same baseline, broader clients

The CLI consumes `InspectionEnvelope<TContent>` as its complete shared
baseline. An evidence-enabled invocation receives that same baseline inside
the [typed enrichment](#service-evidence-enrichment).
It may:

- lower content through Markout or another approved typed presentation;
- write `PortableProjection` to stderr for `--share` without changing ordinary stdout;
- render diagnostics to stderr or a structured diagnostic projection;
- map the owner-issued outcome to exit status; and
- omit envelope structure from a published output format whose existing
  contract exposes only the primary content.

Inspect Web consumes the same envelope. It may use it directly or compose a
Browser-owned experience around it:

```text
Browser inspection experience
  InspectionEnvelope<TContent>
  additional owner-issued inspection envelopes
  navigation and interaction state
  Browser presentation
```

This is a semantic subtype relationship: the Browser experience contains the
unchanged shared baseline and may be broader. The design does not require CLR
inheritance. Composition is valid when it preserves the baseline envelope as
one identifiable value; inheritance is valid only when serialization,
NativeAOT, and facade ownership preserve the same contract.

A Browser-specific DTO may project an envelope for transport, but it must
preserve content, PortableProjection, and diagnostic identity without converting the DTO
into an alternate domain model.

The first Browser pilot uses the closed CLR contract
`InspectionEnvelope<TypeDependencySectionResult>` directly. Its
`System.Text.Json` discriminator and inert-string converters are part of the
authenticated runtime wire shape. `ts-jsexport` currently emits the
polymorphic base records structurally rather than inventing a TypeScript
discriminated union; the runtime boundary tests still verify the discriminator,
derived fields, and diagnostic text. A future union-lowering slice may expose
those alternatives more narrowly without changing this envelope contract.

## Content extent and equality

Host agreement applies per envelope, not to the complete visible experience.

For the same:

- content contract;
- admitted content generation and participant population;
- exact subject;
- semantic query, section, traversal, and row plan; and
- owner-issued capabilities that affect result semantics,

the content, portable projection, and diagnostic sequence are semantically equal.
`Available` portable projections use the same complete canonical URL. Host
request IDs, operation epochs, cache keys, rendering formats, verbosity,
navigation, and interaction do not participate.

When Inspect Web requests more information than the CLI, it receives additional
owner-issued envelopes or a focused aggregate whose components retain their
identities. It does not add rows or facts to the common envelope.

For type relationships:

```text
CLI depends experience
  InspectionEnvelope<TypeDependencySectionResult>

Browser Type Relationships experience
  InspectionEnvelope<TypeDependencySectionResult>
  Research-owned derived-relationship result
  navigation and interaction state
```

The first component agrees exactly when the plans agree. The Browser experience
as a whole is intentionally broader.

## Evolution

The generic envelope is the stable seam; it is not an excuse to predict every
future field.

A new envelope field requires:

1. the same meaning for at least the CLI and Browser;
2. evidence that changing each primary content type would duplicate or
   misplace the concern;
3. one typed owner and construction rule;
4. defined interaction with content equality and diagnostics;
5. a resource-free, contained transport shape; and
6. adoption gates in both hosts.

Completion, coverage, provenance, and semantic-plan receipts are plausible
future candidates. They remain in their current owner-issued content until a
focused proposal demonstrates one cross-domain meaning. Browser navigation,
interaction, loading, and presentation are host companions rather than
envelope candidates.

Envelope evolution is additive and typed. A versioned serializer may omit a
new optional field for an older transport contract, but a host cannot silently
discard required evidence and still claim the same envelope.

## Safety and lifetime

The envelope crosses the existing resource-free result boundary. Content,
PortableProjection, diagnostics, and any future supplement must therefore be
detached from:

- Workspace participants and leases;
- metadata readers, streams, pooled buffers, or callbacks;
- executable closures and service instances;
- credentials or authorization grants; and
- host UI objects.

The envelope does not extend the lifetime of inspected content. Hosts may
retain it, cache it under their existing policies, or serialize an approved
projection without retaining a live Workspace borrow.

## First adoption

The first implementation slice follows #6709 and adopts the pattern for type
dependencies:

1. define the baseline envelope and diagnostic contracts in the lowest
   host-neutral product layer that both Queries/Sections and hosts can consume;
2. return the selected non-null `TypeDependencySectionResult` as content;
3. include the required full PortableProjection URL or typed non-projectable outcome without
   executing the dependency query a second time;
4. move participant rejection and other cross-host notices into typed
   diagnostics without removing richer owner-issued evidence from the content;
5. make CLI `depends <type>` consume the envelope, preserve ordinary content
   output, and migrate `--share` to the additive stderr contract;
6. make Inspect Web Type Relationships consume the same baseline and compose
   its Research-owned derived relationships and Browser experience separately;
   and
7. compare content, PortableProjection, and diagnostics across hosts for equivalent plans.

The adoption does not add Discover behavior or retire the remaining direct CLI
scanner path. Those remain in
[#6704](https://github.com/richlander/dotnet-inspect/issues/6704).

Planned Release gates:

- generic construction and deterministic diagnostic-order tests;
- the #6709 authentic composed and neighboring package cases;
- content equality with and without CLI `--share`, plus non-projectable PortableProjection;
- CLI participant-rejection and strict-row-failure behavior; and
- Browser managed-boundary tests that compare the same baseline content and
  the same portable projection and diagnostics before Browser-only composition.

## CLI envelope passthrough

Supported public CLI passthrough is tracked by
[#6719](https://github.com/richlander/dotnet-inspect/issues/6719). The CLI
[content-shape and service-envelope boundary](output-shapes.md#content-shapes-and-service-envelopes)
owns the JSON-only `--envelope` spelling and its distinction from content
`--json`. It also owns the required content-JSON alignment when a route adopts
public passthrough. The generic envelope does not define CLI option
interactions or migrate existing machine schemas implicitly.

Passthrough preserves the already constructed envelope exactly.
`--prefer-rendered-urls` remains a separate URL preference, not a service-output
selector.

## Non-claims

This design does not:

- define one universal content, subject, provenance, completion, or failure
  type;
- require every internal query or producer result to be enveloped;
- require all hosts to request the same content extent;
- make Browser experience state part of the CLI baseline;
- require CLR inheritance for Browser extensions;
- change existing CLI output schemas merely because the host consumes an
  envelope;
- move domain-specific evidence out of `TContent`;
- make diagnostic severity determine success or exit status;
- permit diagnostics to replace PortableProjection, typed failure, or completion;
- add an untyped metadata, extension, or action bag; or
- authorize adopting every command or website inspector in one PR.
