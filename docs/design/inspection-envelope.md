# Inspection envelope

Status: **proposed**.

This design owns one cross-cutting result pattern:
`InspectionEnvelope<TContent>` is the host-neutral terminal boundary around one
owner-issued inspection result. The CLI consumes that baseline envelope.
Broader hosts consume the same baseline and may compose additional
owner-issued content or host-owned experience state around it without changing
the baseline content.

The implementation tracker is
[#6710](https://github.com/richlander/dotnet-inspect/issues/6710).
The first adoption follows the authentic type-dependency evidence in
[#6709](https://github.com/richlander/dotnet-inspect/pull/6709).

## Claim

For one resolved content generation and semantic plan, every host receives the
same primary content and diagnostics:

```text
InspectionEnvelope<TContent>
  Content
  Diagnostics
```

`TContent` remains the result type issued by the inspection owner. The envelope
does not replace that type, reinterpret its facts, or become a universal
inspection-content model.

The envelope initially owns only:

- the non-null primary content value; and
- an immutable ordered sequence of cross-host diagnostics.

Other information may join the envelope only when a separately demonstrated
cross-host need establishes one common meaning. Adding an untyped metadata or
action dictionary is not an extension mechanism.

## Motivation

The CLI and Inspect Web already need the same semantic results but currently
adapt supplemental evidence independently.

The type-dependency path demonstrates the split:

- shared Queries and Sections produce dependency facts, participant outcomes,
  traversal evidence, and selected relationship rows;
- CLI `TypeDependencyExecutionResult` combines those facts with scan
  diagnostics, availability, and row-selection failure;
- `BrowserTypeMetadata` combines projected type facts and graph content with
  stringified `InspectionFailures`; and
- Inspect Web adds Research-owned derived relationships, sharing, navigation,
  and interactive rendering that the CLI does not request.

The duplicated wrappers make it easy for one host to drop a diagnostic,
translate a failure into empty content, or attach supplemental evidence to a
different semantic result. Moving the common minimum into a host-neutral
envelope lets the CLI use the baseline directly and lets the Browser build a
richer experience without privately extending the shared content.

This is not a claim that both hosts request the same amount of information.
Equal semantic plans produce equal baseline envelopes. A broader host requests
additional owner-issued results and composes them explicitly.

## Authentic basis

The first adoption uses:

- `Npgsql.EntityFrameworkCore.PostgreSQL@8.0.4`;
- `Microsoft.EntityFrameworkCore.Relational@8.0.4`;
- target
  `Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal.NpgsqlOptionsExtension`;
- target framework `net8.0`; and
- the relationship chain
  `NpgsqlOptionsExtension -> RelationalOptionsExtension ->
  IDbContextOptionsExtension`.

PR #6709 preserves the exact published packages and gates the composed and
neighboring single-package results through normal Workspace, query, section,
row, and CLI paths.

Existing participant-rejection, unavailable-root, fuzzy-root, strict-row, and
Browser transport cases supply the pathological evidence for diagnostics.
Synthetic boundary fixtures complement the authentic content evidence; they do
not replace it.

## Boundary

The envelope sits where a completed host-neutral operation hands one detached
result to a host:

```text
resolved basis
  -> one terminal plan
  -> owner-issued result
  -> InspectionEnvelope<TContent>
  -> CLI or Browser composition and presentation
```

It does not wrap every producer, prerequisite query, Workspace operation, or
internal intermediate. An operation with no L2 owner may envelope its final L1
result. An operation with an L2 section or inspection owner envelopes the L2
result rather than nesting envelopes around each prerequisite.

Execute, Discover, and Share may each return an envelope around their own
primary result type. One envelope does not grant or combine multiple terminal
purposes.

## Primary content

`Content` is required and non-null. Its owner continues to define:

- semantic facts and identities;
- success, partial, unavailable, and failure variants;
- completion and traversal evidence;
- provenance and participant correspondence;
- section and row identities, ordering, selection, and Count; and
- domain-specific structured diagnostics that are themselves semantic
  evidence.

The envelope does not impose one universal success/failure union. If an owner
cannot produce valid content, its existing typed non-success remains the
operation outcome; an empty `TContent` plus a diagnostic is not an equivalent
substitute.

Wrapping content does not change its equality, row count, ordering,
serialization meaning, or resource ownership. A host may lower or render
`Content`, but it may not add facts to it after the envelope crosses the shared
boundary.

## Diagnostics

Diagnostics are the first common supplemental currency because both hosts need
to disclose useful evidence that is not itself a content row:

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

Diagnostics are immutable and deterministic under deterministic inputs. The
terminal operation preserves owner order when it already exists and otherwise
uses a documented stable composition order. A code may occur more than once
when distinct owner-issued identities distinguish the occurrences.

Severity alone does not determine operation success, CLI exit status, retry,
or Browser navigation. Those decisions remain with the content owner and
host policy. In particular:

- an `Error` diagnostic may accompany a valid partial result;
- a `Warning` cannot hide owner-issued incomplete evidence;
- an empty diagnostic sequence does not certify completeness; and
- adding a diagnostic cannot change content rows or selection.

Verbose logs, traces, tips, performance telemetry, exception stack traces, and
host-authored convenience messages are not inspection diagnostics.

## Same baseline, broader clients

The CLI consumes `InspectionEnvelope<TContent>` as its complete shared input.
It may:

- lower `Content` through Markout or another approved typed presentation;
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
  Share projection
  navigation and interaction state
  Browser presentation
```

This is a semantic subtype relationship: the Browser experience contains the
unchanged shared baseline and may be broader. The design does not require CLR
inheritance. Composition is valid when it preserves the baseline envelope as
one identifiable value; inheritance is valid only when serialization,
NativeAOT, and facade ownership preserve the same contract.

A Browser-specific DTO may project an envelope for transport, but it must
preserve the primary content and diagnostic identity without converting the
DTO into an alternate domain model.

## Content extent and equality

Host agreement applies per envelope, not to the complete visible experience.

For the same:

- content contract;
- admitted content generation and participant population;
- exact subject;
- semantic query, section, traversal, and row plan; and
- owner-issued capabilities that affect result semantics,

the primary content and diagnostic sequence are semantically equal. Host
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
  Share and navigation state
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

Completion, coverage, provenance, semantic-plan receipts, and portable
projection are plausible future candidates. They remain in their current
owner-issued content until a focused proposal demonstrates one cross-domain
meaning. Browser Share URLs, navigation, interaction, loading, and presentation
are host companions rather than envelope candidates.

Envelope evolution is additive and typed. A versioned serializer may omit a
new optional field for an older transport contract, but a host cannot silently
discard required evidence and still claim the same envelope.

## Safety and lifetime

The envelope crosses the existing resource-free result boundary. `Content`,
diagnostics, and any future supplement must therefore be detached from:

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
2. return the selected `TypeDependencySectionResult` as primary content;
3. move participant rejection and other cross-host notices into typed
   diagnostics without removing richer owner-issued evidence from the content;
4. make CLI `depends <type>` consume the envelope while preserving current
   output and exit behavior;
5. make Inspect Web Type Relationships consume the same baseline and compose
   its Research-owned derived relationships and Browser experience separately;
   and
6. compare baseline content and diagnostics across hosts for equivalent plans.

The adoption does not retire the remaining direct CLI scanner path or add
Discover and Share behavior. Those remain in
[#6704](https://github.com/richlander/dotnet-inspect/issues/6704).

Planned Release gates:

- generic construction and deterministic diagnostic-order tests;
- the #6709 authentic composed and neighboring package cases;
- CLI participant-rejection and strict-row-failure behavior; and
- Browser managed-boundary tests that compare the same baseline content and
  diagnostics before Browser-only composition.

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
- permit diagnostics to replace typed failure or completion;
- add an untyped metadata, extension, or action bag; or
- authorize adopting every command or website inspector in one PR.
