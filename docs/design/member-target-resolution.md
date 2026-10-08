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

## Assembly-context exact selection

`ApiSurfaceMemberSelection` owns exact type, declaration, and physical body
selection over one complete API surface. Its member request carries the escaped
structured type identity, member name, owner-issued opaque selector key, and
optional image-local metadata token. A matching token may identify the
declaration or body in the selected image; when reference and implementation
row numbers differ, the owner-issued selector is the structural fallback.

`AssemblyContextMemberSelectionQuery` composes that selection over one
caller-authorized participant. The caller supplies explicit API-surface bounds
and retains ownership of the participant, workspace lifetime, and acquisition
authority. The query performs one bounded `IncludeAll` projection, refuses to
select from a truncated surface, and returns the participant-scoped type,
declaration, or physical body through the ordinary `AssemblyContextEntry<T>`
outcome. A caller that already projected the complete surface uses
`ApiSurfaceMemberSelection` directly rather than repeating that work.

Inspect Web uses this operation after its package or Platform workspace has
selected the participant. Web continues to own browser bounds and protected
scope leases, but it does not reconstruct exact type, declaration, or body
matching rules.

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
  focused Analysis `MethodIdentity` evidence.

Conversion operators are a special API-identity case: every MethodDef name in
Metadata's owner-issued `ApiMemberIdentity.IsConversionOperator`
classification is return-sensitive. The closed name set is owned by
[Type, member, and API representation](type-member-api-representation.md#conversion-ownership).
Their API canonical signatures therefore include a product-owned return-type
suffix `~ReturnType`, for example
`M:System.Decimal.op_Explicit(System.Decimal)~int`. Without the suffix, all
conversions with the same source parameter collapse to one anchor digest. The
suffix deliberately uses the same delimiter shape as XML documentation member
identity so XML lookup and API anchors do not invent divergent spellings for the
same return-type disambiguator; XML documentation is precedent, not the owning
authority for the API identity grammar.

## Boundaries

- Lexical command helpers may still identify source/type/member argument slots,
  but semantic member resolution should flow through `MemberTargetResolver`.
- Commands that target API or body changes, such as `diff -m/--member`, should
  resolve selectors against the old/new API surfaces and filter by the resulting
  `MemberAnchor` identities rather than by re-parsing display text.
- Body evidence should flow through `ResearchMemberIdentity`, which formats
  `MethodIdentity` subjects and API-derived `ResolvedMemberTarget` body aliases
  with the same canonical spelling.
- Assembly-context consumers should use
  `AssemblyContextMemberSelectionQuery` for an already-issued opaque selector
  instead of projecting an API surface solely to reimplement exact matching.
  Consumers that already require the complete surface use
  `ApiSurfaceMemberSelection`.
- `MemberAnchor` remains the durable user/agent-facing identity; producer-native
  references remain producer evidence and should not be replaced by selectors.
- The resolver lives in `ILInspector.Metadata`, so it stays SRM-only and has no
  decompiler dependency.
- Do not add local selector, canonical-signature, fingerprint, or
  anchor-construction helpers in producers. Add or extend the owning identity
  layer instead, then cover the bridge with a round-trip or alias-vs-subject
  test.
