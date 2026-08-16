# Member target resolution

> **Map:** [Type, member, and API representation](type-member-api-representation.md) is the entry
> point for choosing a type, member, or API identity shape. This document owns
> the details below.

Member target resolution is the typed seam between user selectors, API surface
members, durable member anchors, and physical body evidence.

`MemberTargetResolver` owns semantic selection for a member within an `ApiType`.
It consumes a `MemberTargetSelector` rather than a loose tuple of strings, so
selector details survive past command-line parsing:

- normalized member name
- `Name:N` overload index
- `Name~digest` stable selector prefix
- generic method arity from `M<T>` / `M<TKey,TValue>`
- kind qualifiers: `operator:`, `explicit:`, and `extension:`

The resolver returns `ResolvedMemberTarget`, which carries the API member handle,
its `MemberAnchor`, selector/declaring overload indexes, and a `BodyTarget` when
the selected API member maps to a physical declaring member. Projected extension
methods use this body target to preserve the difference between the API target
and the member that owns IL/native metadata evidence.

Diagnostics are typed (`MemberTargetDiagnosticKind`) and include candidate
anchors for ambiguous or out-of-range selections. CLI commands should render the
diagnostic instead of falling back to partial string matching.

## Identity ownership

Member identity has two related vocabularies:

- **API identity** is owned by `ILInspector.Metadata.ApiMemberIdentity`. It
  creates `MemberAnchor` values, selector prefixes (`operator:`, `explicit:`,
  `extension:`), canonical signatures, and stable selector fingerprints. Product
  producers such as C# body diff should call this layer instead of building
  anchors locally.
- **Body identity** is owned by `ILInspector.Research.ResearchMemberIdentity`.
  It formats `MethodIdentity` subjects and API-derived `ResolvedMemberTarget`
  body aliases through one body canonicalization path. Body identity deliberately
  has a different type-name vocabulary from API identity because it mirrors
  `LibraryBodyIndex`/`MethodIdentity` evidence.

Conversion operators are a special API-identity case: C# overloads
`op_Implicit`, `op_Explicit`, and `op_CheckedExplicit` by return type. Their
API canonical signatures therefore include a product-owned return-type suffix
`~ReturnType`, for example
`M:System.Decimal.op_Explicit(System.Decimal)~int`. Without the suffix, all
conversions with the same source parameter collapse to one anchor digest. The
suffix deliberately uses the same delimiter shape as XML documentation member
identity so XML lookup and API anchors do not invent divergent spellings for the
same return-type disambiguator; XML documentation is precedent, not the owning
authority for the API identity grammar.

## Operator vocabularies

"Is this an operator?" has two different answers, and conflating them produces
either invalid C# or a lost identity join. `CSharpText.OperatorNames` owns both,
as separate model-free helpers:

- **Metadata (CLI) classification** — `IsMetadataOperatorMethod` /
  `IsMetadataOperatorMethodName`. The complete ECMA-335 I.10.3 vocabulary
  (Tables I.4 and I.5 plus the I.10.3.3 conversions), the checked and C# 14
  compound-assignment names, and the Visual Basic arithmetic names, gated on the
  `SpecialName` flag and zero generic arity. This is what API `Kind` and stable
  operator selectors use. `op_LogicalAnd`, `op_AddressOf`, `op_Assign`, and
  `op_CheckedImplicit` are operators here even though C# cannot declare them; an
  ordinary method named `op_Multiply` is not an operator at all, and a bare
  `op_` prefix never classifies.
- **C# source representability** — `IsCSharpOperatorMethodName` for the name and
  `IsCSharpOperatorDeclaration` for the full shape: accessibility, static-ness
  (except the C# 14 instance compound-assignment form), parameter arity and
  `ref`/`out` modifiers, return shape, and declaring-type participation
  (`DeclaringTypeParticipates`). `ILInspector.Metadata.OperatorMetadata` answers
  both questions from metadata handles for producers holding SRM evidence.

Declaration rendering, decompiler raising (`MethodRef.IsOperator`), and
Return-to-Sender closure use the C# proof, because each of them turns the answer
into C# operator *syntax*. Metadata that satisfies the first question but not
the second — a private operator, a binary operator whose declaring type is
neither operand, a `void`-returning one — stays an ordinary method there rather
than becoming invalid or semantics-changing C#.

The operator answer is also an identity fact. `ApiMemberIdentity` reads it from
the `SpecialName` flag, and body identity must agree or an implementation diff
splits one member into two subjects. `ILInspector.Analysis.MetadataOperatorFact`
carries it through `MethodIdentity` (exact — the identity names a MethodDef whose
own metadata supplies it) and `MemberRef` (`Unknown` for an unresolved reference,
which knows less than the definition). `ResearchMemberIdentity` consumes those,
falling back to the metadata name vocabulary only for `Unknown`. Because the
`MemberRef` value is knowledge rather than identity, it is deliberately excluded
from that record's equality and hashing; `MethodIdentity`'s participates.

## Boundaries

- Lexical command helpers may still identify source/type/member argument slots,
  but semantic member resolution should flow through `MemberTargetResolver`.
- Commands that target API or body changes, such as `diff -m/--member`, should
  resolve selectors against the old/new API surfaces and filter by the resulting
  `MemberAnchor` identities rather than by re-parsing display text.
- Body evidence should flow through `ResearchMemberIdentity`, which formats
  `MethodIdentity` subjects and API-derived `ResolvedMemberTarget` body aliases
  with the same canonical spelling.
- `MemberAnchor` remains the durable user/agent-facing identity; producer-native
  references remain producer evidence and should not be replaced by selectors.
- The resolver lives in `ILInspector.Metadata`, so it stays SRM-only and has no
  decompiler dependency.
- Do not add local selector, canonical-signature, fingerprint, or
  anchor-construction helpers in producers. Add or extend the owning identity
  layer instead, then cover the bridge with a round-trip or alias-vs-subject
  test.
