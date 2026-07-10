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
DecompilerFindings.InspectDiagnostics(...)
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
- position;
- typed payload;
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
Finding<ApiType>
Finding<ApiMember>
```

Metadata owns the structured shape, canonical identities, and compatibility
classification. Research may project those observations and transitions into a
product view, but it does not become their source of truth.

## 4. Choose the result shape

- Return a Finding collection when the producer is total and an empty collection
  is a complete census.
- Use `FindingInspection<T>` when `Complete([])`, `Absent`, and
  `Failed(InspectionError)` have materially different meanings.
- Keep caller-owned acquisition failures outside a pure in-memory producer when
  the producer cannot own that failure.

An inspection failure is an operation outcome, not an observation. Do not wrap
it in `Finding<InspectionError>` or make it implement `IFinding`.

## 5. Choose identity and ordering semantics

The producer owns stable observation identity:

- `FindingDescriptor` identifies the observation vocabulary entry.
- `FindingKey.IdentityKey` identifies correspondence candidates.
- `FindingKey.ScopeKey` constrains correspondence when the domain requires it.
- `Position` records the producer ordinal. It is correspondence-significant only
  for an ordered stream; identity-set producers may assign a deterministic
  ordinal without making it part of identity.

Use `FindingMatchMode.Ordered` when position is semantic, such as IL, C#, or
text. Use `FindingMatchMode.IdentitySet` when order is not semantic, such as API
members.

Do not overload ordinal position as a future Research join anchor. Stable
structural anchors are a separate design axis.

## 6. Keep generic comparison generic

Producers project observations and stable keys. They reuse
`FindingComparison.Compare` for inspection projection, matching, folding, and
outcome construction.

Producer-owned classification may use `TransformPairs`, which preserves the
matcher's pair count, order, and exact old/new atom references. Producers do not
implement private matchers, folds, summaries, or equivalence policy.

Consumers select equivalence from the emitted transitions. The producer does
not collapse a rich transition stream into one universal verdict.

## 7. Keep higher rungs separate

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

## Review checklist

Before approving a producer, answer:

- What is the raw occurrence?
- Which product layer is its sole authority?
- Which existing payload type represents it?
- What stable descriptor and identity does it use?
- Is position semantic?
- Is empty a complete result, or must absence and failure remain distinct?
- Which two or more consumers need the same census?
- Is any proposed field actually a downstream fact, verdict, or presentation?
- Does comparison reuse the shared matcher and fold?
- Which bespoke producer, adapter, or comparison path will this replace?

The first end-to-end Analysis proof should use one allocation census for both
single-version audit and old/new comparison, replacing the count-only Research
allocation path rather than adding another adapter.
