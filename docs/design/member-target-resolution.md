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
- `MemberAnchor` remains the durable user/agent-facing identity; producer-native
  references remain producer evidence and should not be replaced by selectors.
- The resolver lives in `ILInspector.Metadata`, so it stays SRM-only and has no
  decompiler dependency.
- Do not add local selector, canonical-signature, fingerprint, or
  anchor-construction helpers in producers. Add or extend the owning identity
  layer instead, then cover the bridge with a round-trip or alias-vs-subject
  test.

## Selected-Type population

`MemberTargetResolver` selects within one `ApiType`, so its result depends only
on that Type's members. A member request whose sections read only the selected
member therefore resolves over a surface built from the selected Type's own
declarations. Those sections are Signature, IL, Custom Attributes, Exception
Regions, Source Locations, and Fidelity Causes. The extractor skips every other
Type during the same image walk, and the CLI skips forwarded-Type resolution,
which only the complete surface consumes. Overload ordinals keep the
displayed-signature order and scope of the complete route.

A request keeps the complete surface when it:

- includes any other section, or uses discovery, a caller scope, or a deferred
  Type or member;
- selects more than one member or uses a wildcard member name;
- targets an image that declares a same-named extension method, which the
  complete surface projects onto receiver Types;
- names a Type that the metadata owner cannot find by full name, such as a
  forwarded Type;
- names a Type for which surface Type lookup (`TypeMatcher.Lookup`) also
  treats another TypeDef or exported Type as an exact match: a case variant,
  or a dotted suffix such as `A.Outer.Widget` for `Outer.Widget`. Lookup takes
  the first exact match in surface order. It consults base-name matches, such
  as `JsonValue` and ``JsonValue`1``, only when no exact match exists, so those
  siblings keep the selected-Type surface.

When the selected Type is out of scope, for example a hidden Type without
`--all`, the selected-Type surface is empty. The CLI then rebuilds the complete
surface so the not-found report keeps its suggestions.

These requests report failures of the work they perform. They do not report
diagnostics of an API-surface extraction they no longer run, such as unbound
type forwarders elsewhere in the image. Across 12,144 requests (the six
sections, first and last overload ordinals, both scopes, five shared-framework
assemblies), output is byte-identical to the complete route except in 1,152
System.Text.Json requests. Those drop the rejected-row warning and exit 0
instead of 1. `MemberSingleTypeSurfaceTests` gates section parity, suffix-colliding
and generic-arity sibling Types, the empty-surface rebuild, and the absence
of forwarded-Type resolution.
