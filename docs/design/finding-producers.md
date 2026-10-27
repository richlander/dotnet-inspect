# Finding Producer Design

Start with a stable raw observation, not a new `*Findings` class name. A Finding
producer should add reusable product capability while keeping matching,
interpretation, and presentation at their proper layers.

See [Finding Nomenclature](finding-nomenclature.md) for the observation/change
model and canonical vocabulary.

## 1. Decide whether the value is an observation

A Finding is a concrete, single-version occurrence that can be independently
identified and consumed by more than one product surface. It is not:

- a boolean answer;
- an old/new correspondence;
- an equivalence verdict;
- oracle agreement;
- a compatibility classification;
- triage priority.

Those values belong to transitions, equivalence folds, Research composition, or
triage layers.

## 2. Choose the owner before the name

Prefer one broad facade per product domain and methods for observation families:

```csharp
AnalysisFindings.InspectAllocations(...)
AnalysisFindings.InspectCallSites(...)
AnalysisFindings.InspectUnsafety(...)
MetadataFindings.InspectApiMembers(...)
MetadataFindings.InspectApiTypes(...)
DecompilerFindings.InspectFidelityCauses(...)
```

Do not create `AllocationFindings`, `CallSiteFindings`, or
`DecompilerDiagnosticFindings` classes merely to name queries. A narrower type
is justified only when it owns independent state or behavior rather than acting
as a namespace.

## 3. Reuse or add the payload

`Finding<T>` already owns:

- subject;
- descriptor;
- identity and scope keys;
- typed payload;
- optional ordered-stream ordinal;
- optional detail.

Add a domain payload only for typed properties not already represented by the
Finding contract.

Allocation is the positive Analysis example:

```csharp
Finding<AllocationOccurrence>
```

`AllocationOccurrence` already carries allocation kind, allocated type,
frequency, loop/path context, multiplicity, size, and escape information. An
`AllocationFinding` wrapper would duplicate the generic contract without adding
capability.

API inventory is the positive Metadata example:

```csharp
Finding<ApiTypeHandle>
Finding<ApiMemberHandle>
```

Metadata owns the structured shape, canonical identities, and compatibility
classification. The handles retain the underlying API value together with its
canonical identity and anchor. Research may project those observations and
transitions into a product view, but it does not become their source of truth.

## 4. Choose the result shape

- Return a Finding collection when the producer is total and an empty collection
  is a complete census.
- Use `FindingInspection<T>` when `Complete([])`, `Absent`, and
  `Failed(InspectionError)` have materially different meanings.
- Keep caller-owned acquisition failures outside a pure in-memory producer when
  the producer cannot own that failure.

An inspection failure is an operation outcome, not an observation. Do not wrap
it in `Finding<InspectionError>` or make it implement `IFinding`.

For decompilation, fidelity causes are the raw per-site observations. Import,
context, and projection failures such as `DEC0001`-`DEC0003` are inspection
failures, while a method's `Full` or `Partial` fidelity is a fold over its cause
census. Human diagnostic messages are not occurrence identity.

### Admit body topology before native comparison

**Status:** the shared Findings topology is implemented and verified by the
gates named in
[Finding Nomenclature](finding-nomenclature.md#typed-inspection-topology).
`ILInspector.ILDiff` and `ILInspector.Decompiler` have adopted the obligations
below through `IlAssemblyDiff.CompareMemberEndpoints` and
`CSharpBodyDiff.CompareMemberEndpoints`.

Body differs use the shared
[typed inspection topology](finding-nomenclature.md#typed-inspection-topology)
rather than encoding a missing endpoint as a pairwise diff failure.

The producer-specific adapter evaluates each exact endpoint:

- a successfully inspected body is `Complete`, including a valid empty
  observation census;
- a proven missing target is `Absent(SubjectAbsent)`;
- an existing bodyless or non-method-like target is
  `Absent(NoApplicableInput)`; and
- unavailable acquisition, resolution, decode, or canonicalization evidence is
  `Failed`.

The adapter owns this classification and retains its typed target, body, and
absence evidence in the producer-native result. `Absent.Detail` may explain the
state but cannot authorize it. APIs must not interpret a null body, reader,
handle, stream, or collection as absence; the caller supplies an explicit
owner-issued endpoint or the adapter returns failure.

For a Finding-backed producer, `FindingComparison.Compare` retains the endpoint
transition and folds native observations. `Complete`/`Absent` can produce
native added or removed observations, but that statement is not total:
`Complete([])`/`Absent` has no observation pair while its inspection topology
still differs. Consumers must inspect the transition rather than pair count
when endpoint topology is user-visible.

A native differ whose canonicalization genuinely requires both inputs may keep
that two-body algorithm. Its outer result retains the corresponding native
Finding comparison and shared inspection transition, and invokes the native
algorithm only for `Complete`/`Complete`. Other non-failed combinations are
terminal topology outcomes; any failed endpoint prevents native comparison.
Pair-dependent canonicalization must not be recast as one-version Findings.

Old-body-missing and new-body-missing are endpoint topology, not decode or
comparison failures. Producer migrations retire bespoke missing-body failure
kinds after their typed inspection path is wired. Consumers retain the native
transition and result instead of translating a failure string or enum into an
added/removed conclusion.

These are producer-boundary obligations, not permission for a producer to
resolve selectors or infer cross-version target correspondence. The caller
supplies exact endpoints or typed absence evidence; the producer decides
whether it can inspect them and owns its native observations, pair algorithm,
and result.

The ILDiff adoption is gated by
`CompareMemberEndpoints_BodyfulPair_RetainsFindingAndNativeResults`,
`CompareMemberEndpoints_BodyfulAndBodyless_UsesNoApplicableInputWithoutPairDiff`,
`CompareMemberEndpoints_BodyfulAndSubjectAbsent_RetainsExplicitAbsenceWithoutPairDiff`,
`CompareMemberEndpoints_BothSubjectAbsent_IsExactWithoutPairDiff`,
`CompareMemberEndpoints_DecodeFailure_RetainsFailedInspectionWithoutPairDiff`,
and `PresentEndpoint_RejectsNullAndNilEvidence`. Together they verify explicit
endpoint evidence, null rejection, retained topology, pair suppression outside
`Complete`/`Complete`, and the absence of bespoke missing-body failures on the
adopted path. Legacy assembly-wide and `CompareMembers` paths retain their
existing compatibility result until their consumers migrate; they are not the
typed endpoint path.

The C# adoption is gated by
`CompareMemberEndpoints_BodyfulPair_RetainsFindingAndNativeResults`,
`CompareMemberEndpoints_BodylessAndBodyful_UsesNoApplicableInputWithoutBodyDiff`,
`CompareMemberEndpoints_BodyfulAndSubjectAbsent_RetainsExplicitAbsenceWithoutBodyDiff`,
`CompareMemberEndpoints_SubjectAbsentAndBodyful_RetainsAddedCSharpFindingsWithoutBodyDiff`,
`CompareMemberEndpoints_BothSubjectAbsent_IsExactWithoutBodyDiff`,
`CompareMemberEndpoints_FailedInspection_RetainsFailureWithoutBodyDiff`, and
`PresentEndpoint_RejectsNullAndNilEvidence` in
`CSharpMemberEndpointComparisonTests`. Together they verify the same explicit
endpoint, topology-retention, pair-suppression, native-result, and null
rejection obligations for the Decompiler owner, including added canonical C#
Findings for a one-sided present method. Legacy assembly-wide and
`CompareMembers` paths retain their existing compatibility behavior until
their consumers migrate; they are not the typed endpoint path.

Each future producer migration must name equivalent owner-specific gates. The
Findings gates prove the shared state and transition contract; they do not
prove adjacent producer wiring.

## 5. Choose identity and ordering semantics

The producer owns stable observation identity:

- `FindingDescriptor` identifies the observation vocabulary entry.
- `FindingKey.IdentityKey` identifies correspondence candidates.
- `FindingKey.ScopeKey` constrains correspondence when the domain requires it.
- `FindingKey.SoftKeys` carries producer-owned identity projections for
  explicitly named non-exact tiers.
- `Ordinal` optionally retains the producer's source-stream index. Ordered
  producers populate it when consumers need the source location after matching;
  identity-set producers leave it null.

Use `FindingMatchMode.Ordered` when enumeration order is semantic, such as IL,
C#, or text. Use `FindingMatchMode.IdentitySet` when order is not semantic, such
as API members. The matcher uses collection order, not `Ordinal`, as its
alignment authority.

Do not overload an ordinal as a Research join anchor. Keep domain coordinates
and provenance typed in producer payloads until multiple producers demonstrate
one shared semantic contract. See
[Finding Coordinates](finding-coordinates.md).

## 6. Keep generic comparison generic

Producers project observations and stable keys. They reuse
`FindingComparison.Compare` for inspection projection, matching, folding, and
outcome construction.

Producer-owned classification may use `TransformPairs`, which preserves the
matcher's pair count, order, and exact old/new atom references. Producers do not
implement private matchers, folds, summaries, or equivalence policy.

Consumers select equivalence from the emitted transitions. The producer does
not collapse a rich transition stream into one universal verdict.

Soft projections are additive to exact identity. The matcher commits exact
matches first, suppresses ambiguous residual projections, and leaves each
unique soft correspondence as a deferred candidate. The consumer's acceptance
threshold selects whether that candidate remains `Added` plus `Removed` or
becomes `Changed`; the default threshold of 100 remains exact-only. Accepted
matched transitions retain `FindingMatchProvenance`, including tier and
confidence.

Structured equality tiers are blocked by tier and projected identity. Within
each bucket the matcher computes only bounded endpoint-degree summaries, not
the Cartesian candidate graph, then emits mutual degree-one pairs. Candidate
construction is therefore linear in projected-key memberships even when a
large collision bucket is wholly ambiguous. Future fuzzy/similarity tiers must
add their own LSH blocking and explicit comparison cap before they land.

Metadata's first soft tier is `api.member.extension-instance` at confidence 85.
It projects structured method receiver, name, generic arity, return type, and
non-receiver parameters while retaining extension/instance as the variant.
Same-role relocations, signature near misses, ambiguous endpoints, and
observations lacking structured signatures do not correlate. Compatibility
classification stays with `ApiFindingClassifier`, which reports an accepted
pair as an unrelated removal and addition unless the caller requests
`NormalizeMemberProjection`.

## 7. Compare typed facts; spell at presentation

A producer pairs and classifies observations by typed facts and typed
identities read from the source: metadata handles and definition names,
access and modifier flags, structural types, and enums for kinds. C# or other
human-facing spelling is a presentation of those facts. The producer renders it
only for the rows a consumer shows, and it never compares, parses, or slices
the spelling to decide whether a row exists or how it is classified. A string
may take part in a comparison only when it is a lossless canonical encoding
that the typed source produces, not a spelling written for readers.

The API producer does not yet follow this rule. `ApiFindingClassifier` and
`MetadataFindings` pair and classify partly on spelled text: member identity
falls back to `Signature`, Types pair by flattened full name, a changed member
is found by comparing `Signature` text, `SignatureBeforeAccessors` slices that
text, and accessibility and kinds are strings that `ApiAccessibility.Classify`
reads with `Contains`. Every correctness gap under
[#10227](https://github.com/richlander/dotnet-inspect/issues/10227) sat on that
boundary: [#10679](https://github.com/richlander/dotnet-inspect/issues/10679)
(an `init` setter spelled `set`, so no row),
[#10677](https://github.com/richlander/dotnet-inspect/issues/10677) (accessor
reach recovered from text),
[#10656](https://github.com/richlander/dotnet-inspect/issues/10656) and
[#10657](https://github.com/richlander/dotnet-inspect/issues/10657) (pairing by
degraded or flattened text), and
[#10676](https://github.com/richlander/dotnet-inspect/issues/10676) and
[#10354](https://github.com/richlander/dotnet-inspect/issues/10354), where the
typed Finding Diff Any census and the spelled comparer disagree. It is also the
dominant cost: a NativeAOT profile of the CoreLib 10.0.12 to 11 rc.1 diff
spends about half its time building signature text
(`ApiSurfaceExtractor.GetMethodSignature`), which the comparer reads for every
unchanged member.

Of the 27 sites where the API producer decides a row's existence or
classification, 21 compare or parse strings. A typed fact that suffices already
exists on the model for 6 of them, 9 are partial, and 6 have none. Every gap can
close by *adding* a typed field beside the spelled one, so none of the slices
below changes the C# renderer or the decompiler, which keep reading the spelled
fields; replacing a spelled field instead would ripple into 12 of them. The API
producer adopts this rule in slices, each byte-identical on the runtime-pack
population and measured with NativeAOT unless it is marked as a behavior
change:

1. Compare signatures by their stored components (`SignatureModel`) instead of
   by `Signature` text, falling back to text only where no model exists, gated
   by component equality agreeing with text equality over the corpus.
2. Render `ApiMember.Signature` on first read and cache it, and move the
   remaining forced readers off it, so the diff renders signatures only for
   changed rows. This slice and the first land together; neither saves time
   alone.
3. Render each type position once where its display and canonical spellings
   agree, and compute member anchors, fingerprints, and facet details only for
   changed rows.
4. Add typed population accessibility, accessor kind, and Type kind beside the
   strings, and switch the comparer to them. Typed accessibility is also what
   [#10693](https://github.com/richlander/dotnet-inspect/issues/10693) needs to
   place `private protected` in the `private` bucket.
5. Add annotated type shapes for base Types, interfaces, constraints, and
   parameters, and pair members by a typed canonical key, keeping canonical
   text only for output rows.
6. Behavior changes: pair Types by definition name
   ([#10657](https://github.com/richlander/dotnet-inspect/issues/10657)) and
   keep a degraded signature out of pairing or fail visibly
   ([#10656](https://github.com/richlander/dotnet-inspect/issues/10656)).
   Identity on the shared census `SymbolKey`
   ([#10476](https://github.com/richlander/dotnet-inspect/issues/10476)) follows
   that work rather than preceding it: the census key includes the return type
   and omits nullability, the opposite of the API comparer's member key, so it
   serves Type identity and an Exists prefilter for unchanged Types, not member
   pairing as it stands.

Demand-shaped Metadata performance
([#9257](https://github.com/richlander/dotnet-inspect/issues/9257)) tracks the
cost side of this rule.

## 8. Keep higher rungs separate

Some useful product concepts are intentionally not one raw Finding stream:

- **ReturnToSender:** compile validity, compiler diagnostics, C# differences,
  and opcode fidelity are heterogeneous outputs. Compose existing producer
  results and add a diagnostic producer only where a stable raw occurrence
  exists.
- **Oracle agreement:** agreement is a comparison or verdict. Productize the
  underlying observations first.
- **Dependencies:** package dependencies, assembly references, type forwarders,
  and network vulnerability advisories have different owners, subjects, and
  availability contracts. Split them into concrete queries.

Evidence is a role those values may play; it is not a reason to flatten them
into a parallel generic row.

While `ResearchChange` remains a migration projection, every populated native
payload must belong to its declared mechanism. A payload may be absent for a
status- or failure-only row, but an API-tagged change cannot carry an IL, C#, or
body-signal payload. This construction invariant retires with the projection
fields as mechanisms adopt honest observations and transitions.

## Review checklist

Before approving a producer, answer:

- What is the raw occurrence?
- Which product layer is its sole authority?
- Which existing payload type represents it?
- What stable descriptor and identity does it use?
- Is enumeration order semantic, and must the observation retain its ordinal?
- Is empty a complete result, or must absence and failure remain distinct?
- Which two or more consumers need the same census?
- Is any proposed field actually a downstream fact, verdict, or presentation?
- Does comparison reuse the shared matcher and fold?
- Does it pair and classify on typed facts, rendering spelling only for the
  rows it shows?
- Which bespoke producer, adapter, or comparison path will this replace?

The first end-to-end Analysis proof uses one allocation census for
single-version audit, old/new comparison, and member-scoped Finding transitions.
Research can retain typed allocation comparisons, including exact comparisons,
when a consumer requests them, and derives its compatibility count/hotness
projection from the same comparison path; there is no separate count-only
allocation diff path. The CLI selects and retains the native comparison with
`diff --analysis allocation`.

Direct calls are the second end-to-end Analysis proof. Research's call-site
fact producers and `diff --analysis call-site` consume the same
IL-ordered `Finding<DirectCall>` census. Research retains the complete native
comparison only when requested, so `Present` remains observable without
increasing the ordinary Body Signals result.

Definite unsafe IL operations are the third end-to-end Analysis proof.
`diff --analysis unsafety` consumes the same IL-ordered
`Finding<UnsafetyOccurrence>` census used by Research's safety projection.
Allocation, call-site, and unsafety comparisons share one descriptor-keyed
retention container; new descriptors do not add parallel flags, lists,
constructors, or merge branches.

Resource lifecycle observations are a single-version Analysis producer under
`analysis.resource-lifecycle`. Each occurrence retains one acquisition and its
exact unprotected boundary set without scenario policy.
`ResourceTriageAnalysis` assesses those observations into typed boundary kinds,
aggregate actionability, reason, impact, remediation, confidence, and
version-local candidate identity. The explicit `Resource Triage` section keeps
the complete `FindingInspection<ResourceLifecycleOccurrence>`, selects
untrusted-actionable assessments, and maps their typed judgments to prose.
`Complete([])` and `Failed` remain distinct, and the corpus harness consumes the
same assessments rather than resolving tokens or classifying boundaries itself.

Resource lifecycle acquisition is an explicit
`LibraryBodyAnalysisRequest.CreateResourceLifecycle` request. Library commands
union the analyses required by their selected sections and materialize one
lazy `MethodBodyInspectionSession`. When body analysis is selected, the command
prefetches its existing `PdbContext` PE image. AppContext scanning and
member-drill projection use context capabilities, while
`LibraryBodyAnalysisService` consumes immutable content from that prefetched
image; Resource Triage therefore adds no target-file open. The generic result
retains explicit scope and completeness receipts instead of returning a
success-shaped partial census.

The same three Analysis censuses now compose with the N-address correlation
tier. `timeline --member M --finding analysis.*` uses Metadata only to resolve
the structural method focus, then correlates the producer-native Analysis
inspections directly through `FindingCensusCorrelation<T>`. Exact
`api.member --member M` tracks use that census correlation's `Correlate(key)`
bridge rather than a command-owned identity model.

Decompiler fidelity uses the same retention rule in the single-version product
path. The member command's opt-in `Fidelity Causes` section keeps the complete
`FindingInspection<DecompilerFidelityCause>` through member acquisition and
projects it only when rendering. A complete empty census, absent method body,
and failed inspection therefore remain distinct product outcomes.

Portable-PDB source observations follow the same product-first path.
`SourceLinkFindings` exposes SourceLink-decorated documents and
member-to-document mappings; `MetadataFindings` retains compilation options and
references over raw portable-PDB data. Each is a separate identity-set census.
Source reachability, checksum verification, authored/decompiled
correspondence, and rebuild fidelity remain consumer operations over those raw
observations. See [Source Finding Producers](source-finding-producers.md).

The remaining Analysis safety observations preserve their native families:

- declaration, signature, local, call, and opcode evidence use an identity
  multiset of `Finding<UnsafeEvidence>` values because some observations have no
  IL coordinate.

Safety presentation may suppress broader evidence already covered by a definite
operation at the same IL offset. That is consumer projection policy, not a reason
for either producer to return an incomplete census. The CLI consumes both
censuses directly for safety sections. Research composes their native
comparisons and retains only its compatibility count and offset-attribution
projection; neither consumer owns another matcher.
