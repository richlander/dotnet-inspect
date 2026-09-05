# Integration scanner binding

## Status and exact claim

Design-only contract for [#5719](https://github.com/richlander/dotnet-inspect/issues/5719),
owned by [Integrations](integrations.md). The binding and decoded context are
not implemented. Current library inspection and Census behavior is unchanged.
The enforcement described below is planned, not evidence of implemented
behavior.

**Claim:** Integration orchestration supplies an immutable, decoded observation
context to one selected static semantic scanner and projects its classifications
with the original evidence. The application contributes interpretation; it does
not become a second traversal, evidence, or execution owner.

The first application consumer is the Aspire contribution in
`DotnetInspector.Ecosystems`. Both the CLI and inspect-web consume it through
the [six-step adoption path](#adoption-and-retirement), tracked under
[#5728](https://github.com/richlander/dotnet-inspect/issues/5728).

## Ownership

This is a focused contract of the existing Integration owner, not a new
cross-component authority.

| Responsibility | Owner |
| --- | --- |
| Decoding, observation construction, binding construction and invocation, classification admission, evidence attachment, row projection, and scan failures | Integrations |
| Which packs and scanner contributions ship; literal pack grouping and selection | [Static Ecosystem Packs](ecosystem-packs.md#integration-scanner-binding) |
| Acquired images, participant identity, provenance, and image lifetime | Existing [workspace and artifact owners](artifact-acquisition-and-workspaces.md) |
| Command/interaction policy and final presentation | CLI and browser hosts |

Integrations retains its configured concept and producer-policy identities,
actionable type/member currency, ordering, deduplication, participant outcomes,
and completeness meanings. This contract does not change Census requirements,
opportunity production, graph induction, package acquisition, or workspace
construction. Selecting a scanner is not a way to satisfy an unrelated Census
producer-policy obligation.

## Basis in the current product

The present scanner already separates three concerns:

- `EcosystemIntegrationProjection` traverses public type definitions, admits
  public static extension methods, performs guarded decoding, and retains
  structured evidence behind compact rows.
- `EcosystemIntegrationClassifier` interprets metadata names and decoded
  signatures without owning traversal or accumulation.
- `EcosystemIntegrationPresenceBuilder` combines projected signals with broader
  public-type evidence and the separate OpenTelemetry predicate.

The missing seam is between decoded observations and interpretation. Moving the
existing `PEReader` facade into the application catalog would move the wrong
responsibility.

The current Aspire predicates are the initial behavior oracle: public
`Aspire.Hosting.*Resource` types, and `Add*` extension methods on
`Aspire.Hosting.IDistributedApplicationBuilder` returning
`Aspire.Hosting.ApplicationModel.IResourceBuilder<T>`. Other concepts can
legitimately classify the same assembly; package or ecosystem grouping does not
establish an Integration concept.

Comparable repository patterns are the static method bindings in the
[capability section registry spike](capability-section-registry-spike.md) and
the [product-demo source binding](workspace-definitions.md#product-demos-are-closed-section-presets).
They support an inert static binding instead of a plugin lifecycle. The demo
binding's public resolution operation does **not** transfer here: scanner
invocation belongs inside Integration orchestration.

## Static binding

The proposed owner-issued currency is `EcosystemIntegrationScannerBinding`.
Its authoring form is a single target-free static method group:

```text
EcosystemIntegrationScannerBinding.Create(AspireIntegrationScanner.Scan)
```

Construction retains that method without invoking it. It rejects a captured
target or combined invocation list rather than introducing scanner-instance
lifetime or several callbacks behind one binding. Discovery and selection
carry the same opaque value. It exposes no public invocation method, delegate,
or callback accessor; its execution seam is internal to the Integration owner.

A binding is immutable application-lifetime metadata, not an operation,
scanner instance, or cache. One scanner may compose ordinary classification
helpers internally. No registration graph, dynamic loading, or scanner
collection protocol is introduced.

## Decoded observation context

One context describes one already acquired participant. Integration
orchestration constructs it from that participant's retained image before
invoking the selected scanner. It contains eager immutable observations, not
deferred reader callbacks or an enumerable that decodes during iteration.

The initial observation domain is deliberately limited to the current
ecosystem classifier:

| Observation | Information available to interpretation |
| --- | --- |
| Public type | Decoder-produced metadata type name and the owner-held structured definition, when available |
| Admitted starter method | Declaring type observation, method name, ordered decoded parameter-type text, and decoded return-type text |
| Evidence association | The original type/method observation to which a classification applies, including whether structured member evidence is unavailable |

The owner admits public types and public static extension methods using the
current rules. A starter-method observation has at least one parameter.
Parameter order matters: the receiver is the first parameter, while existing
Configuration classifiers also examine later parameters. An input containing
only receiver and return types would not preserve the existing classifier.

Classification text is the current decoder-produced matching vocabulary,
not a CLI label or a browser-rendered signature. Structured
`MetadataTypeDefinitionName`, `MemberAnchor`, and
`MetadataNamedTypeReference` evidence remain separate. Neither scanner nor
host recovers those identities by parsing classification or display text.
Changing the matching vocabulary requires its own parity evidence; this
extraction does not introduce another type-name parser.

Each overload remains a distinct observation even when several overloads will
render as one `Type.Method(...)` row. The association between an observation
and its original structured evidence survives interpretation and projection.
The scanner returns that observation with its classification, not a replacement
anchor, participant identity, provenance object, or reconstructed API label.

The context is data-only and independent of reader lifetime once constructed.
The owner bounds retention to the participant being scanned; this does not add
a workspace-wide inventory cache or change acquisition budgets. Snapshot
ownership and disposal remain with the existing operation.

## Interpretation and projection

The callback returns a finite immutable sequence of classifications. Each
classification pairs an observation from this context with an existing
Integration-owned concept descriptor and a kind under that concept's policy.
The type/API shape follows the observation, not an application-supplied label.

Integrations admits classifications under its existing producer policy and
projects the original evidence into ordinary `EcosystemIntegrationSignalInfo`
rows. It retains structured overload evidence when presentation-equivalent
rows coalesce. Missing structured evidence stays marked as missing; a
classification cannot repair it with a display name.

The scanner neither supplies final row order nor changes concept identity,
producer-policy membership, row equality, or provenance. Projection retains
the current concept/kind priority and deterministic ordinal tie-breaking.
Legacy broad presence flags and the full `IntegrationCount` remain separate:
the chosen scanner's rows alone are not sufficient to reconstruct them.

This is a supported-call contract for cooperating code, not a demand for a
runtime framework that polices deliberately forged observations or descriptor
graphs. Use the existing owner-issued types and the simplest construction
surface that preserves the association.

## Selected execution and failures

The selected binding is an explicit input to an Integration operation over
already realized participants. For each participant whose observation context
is successfully constructed, the operation invokes that binding exactly once,
even if the context or classification result is empty. A participant rejected
before context construction invokes it zero times. Other bindings are not
invoked. Repeating an explicit operation performs another invocation; this
initial contract adds neither result caching nor retries.

Participant traversal stays in owner-defined order. The supported call is
synchronous interpretation over decoded data; existing host acquisition and
cancellation behavior remain outside it. No parallel scheduler or new
cross-operation lifetime protocol is needed.

A selected result covers **that scanner selection**, not all configured
concepts. A successful empty result means that scanner found no currency in its
admitted observations. It is not a complete Census, an all-Integration absence
claim, or a substitute for existing full-library presence. Host adoption must
retain the selection scope when presenting the result. The existing
`AssemblyContextIntegrationsQuery.Definition` must not cache different
selections as if they were its one unchanged full-scan request.

The operation carries ordinary participant identity/provenance and outcome
information beside the projected rows. It preserves these distinctions:

- Acquisition rejection or participant-level decode failure remains a
  rejected/failed participant, not an available participant with no matches.
- A method that the existing guarded signature path cannot classify remains
  outside the classification-ready observations. Extraction does not silently
  promise complete metadata inventory or change that established skip policy.
- Failure to obtain a structured anchor does not erase an otherwise classified
  API. The existing incomplete-evidence marker remains visible to composition.
- Unexpected callback/configuration faults surface as such; they are not
  converted into no matches, retried against a neighboring scanner, or silently
  recategorized as malformed inspected metadata.

This reuses the Integration owner's result semantics rather than giving the
application a second success/failure or completion algebra.

## Trust and evidence posture

The scanner is trusted, source-authored application code. The supported
handoff consists of decoded observations rather than a reader, workspace,
artifact bytes, or acquisition service. This is an ownership boundary, **not a
sandbox**: it does not prevent a contributor from writing unrelated I/O or
metadata access elsewhere in a scanner.

The operator chose outcome-level gates and design review, not source policing.
Do not add source scans, recursive reachable-API audits, or hostile-internal-
state fixtures to establish a stronger capability-absence claim. Such a
whole-library absence claim is **unverified and not claimed**. Existing
repository dependency and friendship requirements still apply unchanged.

Internet-origin metadata retains the existing guarded-decoding and rendering
boundaries. No new decoder, artifact format, platform exception, or inspected
code execution is introduced by this design.

## Gates

All new gates below are **planned Release gates** for implementation. Their
behavior is currently unverified. Existing tests identify the oracle to
preserve; this design-only PR does not claim to have implemented the handoff.

| Gate | Observable obligation |
| --- | --- |
| Focused binding tests | Construction/discovery invoke no callback; one selected binding runs once per admitted participant, including empty input; repetition runs again and neighboring bindings stay untouched. |
| Non-friend consumer test | Application-authored interpretation constructs and carries the binding through the ordinary public Integration operation and consumes the projected result. |
| Observation/projection parity tests | Product-owned observation construction preserves classifications, parameter order, structured type/member associations, overload evidence, row equality/order, and existing full-presence behavior on the same compiled inputs. |
| Failure parity tests | Rejected participants remain beside later results; unclassifiable signatures and unavailable anchors retain their distinct existing treatment; callback faults are not success-shaped output. |
| Aspire and neighboring host cases | CLI and CatalogExports use the selected binding with the same realized inputs and consume equivalent Integration-owned rows/outcomes without changing existing full-scan behavior. |

The focused oracle includes
`EcosystemIntegrationScannerTests.Scan_ProjectsExactOrderedPublicCurrencyAndPresence`,
`Scan_SkipsExtensionMethodWithoutReceiver`, and
`Scan_PreservesClassifiedApiWhenStructuredAnchorIsOverBudget`, plus
`AssemblyContextIntegrationsQueryTests` for participant ordering, snapshot
reuse, acquisition rejection, and budget exhaustion. Add an overload case that
compares the retained evidence set, not just the compact display row.

Tests exercise product-owned decoding and projection. They do not manufacture
the observation/evidence pair they later claim the product preserved.
Synthetic counted callbacks are sufficient for invocation isolation; compiled
fixtures and pinned real packages supply classification and host parity.

Existing NativeAOT and Browser/Wasm coverage remains applicable. First real
binding adoption records the Browser/Wasm publish-size delta required by the
pack owner; successful publication is not proof of API absence.

## Adoption and retirement

[#5728](https://github.com/richlander/dotnet-inspect/issues/5728) is the
non-normative end-to-end tracker. This scanner track has **six steps**:

1. **Integration contract (#5719):** lock this design. No product code changes.
2. **Integration implementation:** extract observations behind the existing
   scanner facade, add the opaque binding and selected operation, and prove
   parity with the current classifier/projection/presence paths. Existing
   library inspection is the production consumer of the extraction.
3. **Application catalog adoption:** expose the Aspire scanner binding under
   the pack owner's existing static selection contract. During migration,
   reuse the owner-side Aspire interpretation through a compatibility adapter
   rather than maintain two independently edited semantic policies.
4. **CLI adoption:** wire explicit selection into the ordinary Integration
   operation and existing section/Markout lowering. Preserve the old complete
   library/presence path until its callers have an equivalent replacement.
5. **Browser adoption:** wire the same selection through
   `InspectWeb.Engine.CatalogExports`; browser infrastructure carries the
   Integration-owned value and results, not the application catalog. Its
   existing typed DTO/presentation path owns final UI lowering.
6. **Retirement:** move Aspire interpretation fully into application source
   and remove its owner-side compatibility policy only after both hosts and
   existing full-scan/presence callers retain their behavior. Keep common
   decoding, projection, and other concepts with Integrations. Fold retirement
   into the final adoption if it cannot land independently without a gap.

Each later step requires its own focused implementation/adoption issue before
work starts. This map names handoffs and retirement, not host command grammar,
browser UI, workspace internals, or a scanner-aware Census redesign. No current
feature is presented as depending on unfinished later steps.

## Demo

Design mockup, not a new CLI command:

```text
Select: ecosystem.aspire / Integration
Input:  Aspire.Hosting.PostgreSQL@13.5.3, net8.0
Owner:  decode public type and starter-method observations
Scan:   invoke the selected Aspire binding once for this participant
Rows:   Resource Builder  Aspire.Hosting.PostgresBuilderExtensions.AddPostgres(...)
        Resource         Aspire.Hosting.ApplicationModel.PostgresServerResource
```

The neighboring `Aspire.Hosting.Redis@13.5.3` case exercises the same
observation contract with `AddRedis`. A non-Aspire DI extension remains
ordinary DI currency for the full scanner and produces no Aspire
classification for the selected Aspire scan. An unavailable anchor preserves
the applicable API row but not an invented member endpoint.

Both hosts render the owner's structured results through their existing
lowerings. Discovery alone performs none of these operations.
