# Metadata accessor aggregate validity

## Owner and claim

`ILInspector.Metadata` owns construction of one direct PropertyDef or EventDef
accessor aggregate. Given an admitted ECMA-335 assembly and one exact root
address, the post returns complete detached root and MethodSemantics evidence,
explicit validity and correspondence statuses, or one typed construction
failure.

The post is an evidence boundary, not a C# declaration validator and not a
whole-assembly verifier. It distinguishes:

1. malformed metadata that violates an owned ECMA-335 `[ERROR]` rule or cannot
   be decoded completely;
2. valid metadata that does not satisfy a CLS correspondence convention;
3. complete evidence whose external type category is unavailable locally; and
4. C# representability, which remains owned by `ILInspector.CSharp`.

This distinction is the focused claim. It replaces the earlier assumption that
all root/accessor disagreement is malformed metadata.

## Basis

The normative format baseline is [ECMA-335][ecma-335]:

- Partition II `Property`, `Event`, and `MethodSemantics` table rules identify
  metadata errors separately from CLS rules;
- `PropertySig`, `MethodDefSig`, `RetType`, `Param`, and `Type` define the
  signature grammar; and
- the [.NET ECMA-335 addendum][runtime-augment] records current runtime grammar,
  including `PropertySig` as `PROPERTY HASTHIS? ParamCount RetType Param*`,
  by-reference property values, recursive custom modifiers, and the positions
  that admit `TYPEDBYREF`.

The distinction between `[ERROR]` and `[CLS]` is load-bearing. For example,
matching property accessor accessibility and virtual flags is a CLS rule, not
a MethodSemantics metadata error. Current C# compilers emit a virtual public
getter with a nonvirtual private setter for:

```csharp
public virtual int Value { get; private set; }
```

They emit the symmetric shape for a private getter. Both are real,
compiler-produced evidence that aggregate-wide virtual-flag equality is not a
Metadata validity rule.

A .NET 11 Release compiler probe provides neighboring grammar evidence:

| Declaration | PropertySig | Relevant accessor evidence |
| --- | --- | --- |
| `virtual int Value { get; private set; }` | `28 00 08` | Getter is public/virtual/new-slot; setter is private/nonvirtual. |
| `static ref int RefValue` | `08 00 10 08` | Property and getter retain top-level `BYREF I4`. |
| `static delegate*<int, void> FunctionPointer` | `08 00 1B 00 01 01 08` | Property, getter, and setter retain the function-pointer type. |

The existing
[lossless MethodSemantics row boundary](../metadata-primitives.md#lossless-methodsemantics-row-boundary)
remains the mechanical source. `PropertyDefinition.GetAccessors()` and
`EventDefinition.GetAccessors()` remain unsuitable because they allocate the
complete `Other` collection before returning, collapse duplicate conventional
roles, and discard unrecognized combined semantics flags.

## Inputs and output

The construction input is:

- one exact PropertyDef or EventDef and its declaring TypeDef;
- the reader-scoped, bounded root and type-identity services;
- the complete physical MethodSemantics range for that root;
- exact MethodDef declaration evidence for every occurrence; and
- one operation context that owns all signature, relationship, node, and text
  budgets.

A posted aggregate contains:

- root address, declaring TypeDef, inert name, raw attributes, and exact root
  signature or event type;
- every physical MethodSemantics occurrence in physical order, including raw
  semantics bits and exact MethodDef evidence;
- explicit per-occurrence ordinary-callable, role-correspondence, and
  prerequisite statuses;
- exact per-accessor accessibility, static, virtual, abstract, new-slot,
  final, special-name, implementation, custom-modifier, and safety facts; and
- the existing root and occurrence-bound memory-safety evidence.

`Posted` means the complete bounded evidence graph was constructed. It does
not silently mean CLS-compliant, C#-representable, or fully verified against an
external type hierarchy. Negative or unavailable statuses remain first-class
facts.

No failure publishes a partial aggregate.

## Three validation layers

| Layer | Metadata responsibility | Negative result |
| --- | --- | --- |
| Mechanical validity | Enforce locally decidable ECMA-335 `[ERROR]` rules, complete signature grammar, row ownership, and bounded decoding. | Typed rejection. |
| Aggregate correspondence | Compare exact root and accessor facts and retain the result for every occurrence. | Posted negative or unavailable status. |
| Language representability | Decide whether the complete evidence has one faithful C# form under a language profile. | CSharp-owned `Unavailable` or `Unrepresentable`. |

A CLS convention does not become a Metadata error merely because CSharp needs
it. Conversely, a malformed signature blob is not posted as a negative C#
fact.

## Mechanical validity

### Root rows

A property root requires an exact owner, valid Property row, bounded non-empty
name, specified flags, non-null signature blob, full-blob consumption, and the
current .NET `PropertySig` grammar. Its header is `PROPERTY` with optional
`HASTHIS`; no reserved or unrelated calling-convention bits are accepted.

An event root requires an exact owner, valid Event row, bounded non-empty name,
specified flags, and a valid EventType coded index when non-null. A local
EventType whose authoritative category contradicts the ECMA class requirement
is malformed. An external EventType whose category is not available without
acquisition retains typed prerequisite unavailability; this post does not
perform network or workspace acquisition.

### MethodSemantics rows

Every physical row is read before aggregate construction. Each row must:

- contain exactly one role legal for its association kind;
- reference a valid MethodDef on the root's declaring type; and
- satisfy the existing range, ordering, coded-index, and operation-budget
  rules.

Event metadata requires exactly one add and one remove occurrence and permits
at most one raise occurrence. Violations are malformed metadata. Property
getter/setter multiplicity is retained and classified because ECMA assigns the
one-getter/one-setter convention to CLS rather than a table `[ERROR]` rule.
Every legal `Other` occurrence remains lossless.

### Signatures

All root and MethodDef signature blobs must consume the complete blob and obey
the current .NET grammar:

- `PropertySig` uses the runtime-augmented `RetType` and `Param` productions;
- `MethodDefSig` retains valid default, vararg, instance, explicit-this, and
  generic forms rather than redefining "ordinary C# method" as metadata
  validity;
- `RetType` admits its defined `void`, by-reference, typed-reference, custom
  modifier, and `Type` forms;
- `Param` admits `Type`, custom-modified by-reference `Type`, and
  custom-modified `TYPEDBYREF`;
- nested positions use `Type`, so position-illegal `void`, by-reference,
  typed-reference, and pinned encodings reject;
- pointer-to-void, arrays, generic instances, and function pointers follow
  their recursive grammar; and
- custom-modifier tokens, generic arity, array shape, and function-pointer
  signatures are validated at every recursive position.

Grammar validity and ordinary accessor shape are separate. A complete generic
or vararg MethodDef signature can be valid metadata while remaining negative
correspondence evidence for CSharp.

## Aggregate correspondence

Correspondence is computed from exact structural identities. It never infers a
role from a method name and never normalizes away custom modifiers.

| Role | Retained correspondence facts |
| --- | --- |
| Getter | Return identity versus property value identity; ordered parameters versus property index parameters; callable instance bit versus PropertySig `HASTHIS`. |
| Setter | Void return category; ordered index-parameter prefix; final value identity; callable instance bit versus PropertySig `HASTHIS`. |
| Add/remove | Void return category; exact parameter count and identity versus EventType; add/remove pair staticness and other exact MethodDef facts. |
| Raise | Complete MethodDef signature and ordinary-callable classification; no comparison with EventType because the signature describes invocation. |
| Other | Exact MethodDef and occurrence facts; no conventional correspondence classification. |

Outer custom modifiers remain in exact identities. Only a role's category test,
such as whether a setter return is `void`, may inspect through its permitted
outer modifiers; the exact modified type remains posted.

The post does not require conventional accessors to share accessibility,
virtual, abstract, new-slot, final, or C# declaration modifiers. Those are
per-MethodDef facts. It also does not reject solely because `SpecialName`,
`HideBySig`, CLS naming, ordinary calling convention, or C# implementation
flags are absent. CSharp owns the supported property-level modifier shape and
requires affirmative correspondence where its form needs it.

For event `[ERROR]` rules that require a delegate category, authoritative local
type evidence can confirm or contradict the rule. Missing external hierarchy
evidence is retained as prerequisite unavailability rather than guessed from a
name or converted into malformed metadata.

## Failures

Construction returns one typed failure:

| Failure | Meaning |
| --- | --- |
| `MalformedMetadata` | A locally owned `[ERROR]` rule, row relationship, coded index, complete-blob grammar rule, or authenticated local type-category rule is contradicted. |
| `RelationshipTraversal` | A required bounded owner, scope, or relationship walk is cyclic, ambiguous, or otherwise invalid. |
| `BudgetExceeded` | Signature bytes, structural nodes, relationship edges, retained text, or retained occurrences exceed the operation policy. |
| `SessionUnavailable` | Required reader/session authority is retired or the request does not belong to it. |

CLS mismatch, negative C# modifier shape, duplicate property roles, and absent
external type-category evidence do not use `MalformedMetadata`.

The cache key includes reader/session identity and exact root address. A cache
entry stores only the complete posted aggregate or complete typed rejection.
Posted values retain no reader, session, lease, callback, or mutable budget.

## Consumers and sequence

The implementation sequence is:

1. #8836 implements the shared signature grammar owned here.
2. #8837 implements direct root construction and correspondence.
3. #8838 adds the separate local declaration-MethodDef reverse join.
4. #8677 consumes the posted aggregate and reverse join in CSharp.
5. #7889 and #6199 adopt the accepted CSharp request and retire legacy
   accessor selection.

The reverse join is not part of this direct-root contract. CSharp does not
reopen SRM, scan MethodSemantics, or infer aggregate identity from accessor
names. The
[ordinary-property policy](csharp-memory-safety-spelling.md#properties-and-events)
owns C# accessibility, property-modifier, caller-contract, pointer, and
memory-safety admission.

This design adds no rendering, QuerySpace vocabulary, CLI section, browser
surface, or host-specific path.

## Evidence and gates

The implementation issues own Release gates for:

- `System.Collections.Generic.List<T>.Count`;
- compiler-produced indexers, events, init-only properties, ref-return
  properties, function-pointer properties, and pointer types;
- virtual properties with private getters and private setters;
- a raise method whose invocation signature differs from EventType;
- multiple physical `Other` occurrences;
- well-formed non-CLS correspondence mismatches that post negative facts;
- malformed headers, trailing bytes, illegal recursive type positions,
  invalid array/generic/function-pointer shapes, and cyclic scopes;
- exact-limit and over-limit signature, node, relationship, and text budgets;
  and
- detached use after session retirement.

Until #8836 and #8837 land, these product properties are **unverified**. The
design PR gate is Markdown validation only.

[runtime-augment]: https://github.com/dotnet/runtime/blob/main/docs/design/specs/Ecma-335-Augments.md
[ecma-335]: https://ecma-international.org/publications-and-standards/standards/ecma-335/
