# C# declaration representability

## Status and ownership

Issue [#4852][issue-4852] defines this
`ILInspector.CSharp` contract. This document is its sole normative owner.

The exact claim is:

> Given one complete detached post of Metadata-owned declaration facts and one
> selected C# language profile, CSharp returns one immutable accepted
> declaration request, a typed C# refusal, or typed prerequisite
> unavailability. It does not reopen metadata, reconstruct relationships from
> display text or flags, or publish a partial declaration.

The first product slice implements the canonical static explicit-interface
operator path. It consumes the ordinary MethodDef evidence from
[#7886][issue-7886], containing TypeDef evidence from [#8348][issue-8348], the
exact local declaration-owner address from [#8399][issue-8399], MethodImpl
relationship evidence from [#7887][issue-7887], and InterfaceImpl association
evidence from [#7897][issue-7897].

## Consumer and adoption

The first production consumer is DecompilerHarness/ReturnToSender through the
nineteen-step migration in [#6199][issue-6199]. CSharp produces the accepted
declaration request. ReturnToSender separately owns target population, artifact
scope, compilation, comparison, fidelity, and reporting.

The adoption path is:

1. Metadata posts complete declaration and relationship evidence.
2. This contract decides C# representability and produces an accepted request.
3. [#7888][issue-7888] adopts exact method declaration requests in RTS target
   selection.
4. [#7889][issue-7889] separately adopts accessor declarations after complete
   MethodSemantics evidence exists.
5. [#7890][issue-7890] cuts raised standalone fidelity over to product
   artifacts.
6. [#6199][issue-6199] retires legacy compile-back only after the migration's
   measured closure gates pass.

This is an approved tools-first path. CLI presentation and browser product
adoption are non-claims, while the reusable CSharp path remains
Browser/Wasm-compatible.

## Demo and motivating boundary

The canonical positive witness is the static explicit interface operator on
`System.Int32`:

```csharp
static int IAdditionOperators<int, int, int>.operator +(
    int left, int right) => left + right;
```

Metadata observes a MethodDef body, a MethodImpl declaration whose owner is
the constructed `IAdditionOperators<int, int, int>` identity, a locally
authenticated `SpecialName` declaration, and an InterfaceImpl association for
that exact structured identity. CSharp must combine those facts with the body
declaration and selected language profile before accepting the operator form.

No individual shortcut is sufficient:

- the `op_Addition` name does not prove an operator;
- `SpecialName` does not prove the required operator signature;
- a qualified display name does not prove the MethodImpl relationship;
- a matching simple interface name does not prove the InterfaceImpl
  association; and
- a printable signature does not prove that every required declaration fact
  was posted.

The neighboring negative witness is an ordinary method named `op_Addition`
without authenticated operator facts. It may be valid metadata, but CSharp
must not silently turn it into an operator declaration.

## Design basis

The selected [C# language specification][csharp-spec] is the authority for
whether a declaration form exists under that language profile.
[ECMA-335][ecma-335] defines the metadata structures that the supporting
Metadata owners authenticate. Neither specification assigns source-language
meaning to a display string.

The conventional compiler analogue is Roslyn's separation between imported PE
symbols and C# language decisions. PE declarations remain typed symbols, and
unsupported metadata types become error symbols carrying use-site evidence
rather than repaired source-looking text. Relevant examples are
[`PEMethodSymbol`][roslyn-pe-method],
[`PENamedTypeSymbol`][roslyn-pe-type],
[`UnsupportedMetadataTypeSymbol` through `SymbolFactory`][roslyn-symbol-factory],
and [`ErrorTypeSymbol`][roslyn-error-type]. This design transfers the
separation, not Roslyn's architecture or code.

The dotnet-inspect-specific addition is an explicit post boundary. Product
Metadata results are detached, bounded, and typed before CSharp decides a
language form. CSharp therefore needs neither a compiler-sized symbol graph nor
a second metadata importer.

## Pipeline boundary

The declaration path has four distinct stages:

```text
Metadata observation
  -> detached declaration post
  -> CSharp representability decision
  -> accepted-request rendering
```

**Observation** reads and authenticates metadata under its owner-backed
session, operation policy, and lifetime.

**Post** is the final detached fact handoff for one decision. The term means
posting the complete receipts for CSharp acceptance; it does not mean session
construction, cache publication, or source rendering.

**Decision** classifies the complete post under one explicit C# language
profile.

**Rendering** consumes only an accepted request. It may choose qualified or
contextual spelling according to existing CSharp policy, but it cannot
rediscover declaration category, relationship, or representability.

## Declaration post

The post is CSharp-owned composition over owner-issued Metadata values. It
does not redefine how Metadata constructs, validates, budgets, or retains
those values.

A post identifies one selected declaration and carries every Metadata result
required by the declaration category:

- the complete ordinary MethodDef declaration result;
- the complete MethodImpl result for the selected body;
- one explicitly paired InterfaceImpl request and result for each MethodImpl
  declaration owner whose interface association participates in the decision;
- the containing type shape needed by the requested C# form;
- complete signature, modifier-marker, declaration-category, accessibility,
  modifier, and special-name evidence required by that form; and
- for property, indexer, or event forms, one complete MethodSemantics
  aggregate rather than independently selected accessors.

An unavailable or rejected Metadata result remains part of a terminal post.
It leads to typed unavailability; it is not omitted so the remaining facts can
look complete.

The post carries no `MetadataReader`, `PEReader`, stream, session, operation
context, lease, callback that can reopen evidence, or mutable budget. Metadata
coordinates may remain as detached evidence and body bindings, but they do not
become semantic cross-session identity.

The public post is capture-only: callers provide one live declaration session
to the CSharp producer, and only the resulting detached graph is publicly
consumable. This prevents unrelated observations from being assembled into a
success-shaped post with value-equal coordinates.

## Composition and join invariants

The MethodImpl result is consumed as a whole. A body with multiple physical
relationships cannot be represented by selecting the easiest relationship and
discarding the others. Physical order and multiplicity remain observable
evidence until the CSharp decision explicitly accounts for every occurrence.

For each MethodImpl relationship that requires interface association:

1. the relationship's exact `DeclarationOwner` structured identity is the
   InterfaceImpl request identity;
2. the relationship's exact declaring `Type` is the InterfaceImpl request
   type;
3. a locally resolved declaration's exact owner TypeDef address is the request
   for its paired TypeDef declaration post;
4. that TypeDef post's structured definition identity equals the definition
   carried by `DeclarationOwner`, and its category is `Interface`;
5. the request and its detached result remain paired with that exact
   relationship occurrence; and
6. the post contains neither an unpaired relationship nor an extra association
   result.

Structured type identity is the semantic join currency. MVIDs and row handles
remain coordinates within the observation that issued them. Qualified names,
simple names, rendered C#, row ordering, and value-equal Boolean flag sets are
not join currencies.

Metadata also owns the safe inert-text rendering needed to cross the enforced
assembly-dependency boundary. CSharp applies identifier admission and spelling
policy to those rendered strings; the rendering does not classify declarations
or become semantic identity.

An exact InterfaceImpl `Absent` result proves only the absence defined by its
owner. It does not prove that the declaration owner is a class, that an
interface is unreachable through another interface, or that no C# explicit
implementation exists. If the requested C# decision needs those stronger
facts, the result is prerequisite unavailability until their owner posts them.

## Representability outcome

One decision returns exactly one closed outcome:

| Outcome | Meaning |
| --- | --- |
| `Representable` | Every required fact is complete and one faithful C# declaration form exists under the selected language profile. The result carries one immutable accepted declaration request. |
| `Unrepresentable` | Metadata facts are complete and valid for this decision, but no faithful declaration form exists under the selected language profile. The result carries a stable CSharp-owned reason. |
| `Unavailable` | The decision lacks authoritative prerequisites because evidence is rejected, degraded, unresolved, incomplete, inconsistent, or outside the posted contract. |

`Unrepresentable` is a language conclusion. `Unavailable` is an evidence
conclusion. They must not collapse into one Boolean.

No outcome carries partial source. Failure diagnostics identify the rule and
trusted caller coordinate without quoting artifact-authored names or signature
text.

This contract does not add a metadata-looking fallback declaration.
Contained type/member fallback from the broader historical migration design is
separate work because it serves inspection and presentation rather than the
compilable RTS artifact. If that capability is resumed, it requires its own
focused claim, complete fact parity, and inert rendering gates.

## Accepted declaration request

The accepted request is immutable and sufficient for existing CSharp-owned
rendering to produce the declaration without another semantic decision. It
preserves:

- the selected declaration category;
- the exact accepted identifier and type spellings or structured spelling
  plans;
- the structured containing-type, explicit-interface-owner, and method
  signature identities behind those spellings;
- accessibility and declaration modifiers;
- generic arity, parameters, return/value type, and required constraints;
- explicit-interface owner identity and member category when applicable;
- operator or conversion identity when applicable;
- containing-type obligations required by the declaration;
- the exact selected body or accessor binding; and
- the language profile under which acceptance was decided.

An implementation may lower the accepted request into existing printer models.
That lowering must be one-way and mechanical. Mutable `ApiMember` fields,
legacy `Kind` strings, rendered signatures, or formatter success cannot become
new evidence for acceptance.

## Language profile

Representability is always evaluated against an explicit language profile.
The profile identifies the C# language version and any separately modeled
language semantics that affect declaration legality. The default used by a
consumer is consumer policy; it is not inferred from the inspected binary.

The profile participates in the result identity. A request accepted for one
profile cannot be reused as proof for another profile without a new decision.
Examples include static interface members, checked operators, ref-safety
spelling, and later declaration forms.

## Initial implementation boundary

The first implementation is a method-like proof slice for one directly
associated static explicit-interface `op_Addition` operator:

- the MethodImpl declaration owner is locally resolved and its separately
  posted TypeDef is an interface with the same structured definition identity;
- exactly one physical MethodImpl and one exact InterfaceImpl association
  participate;
- the declaration is an authenticated two-operand `op_Addition` with a
  non-void return, a containing-type operand, plain complete parameter
  evidence, and a C# 11-or-later profile; and
- the containing declaration is not a static class, and no recursively spelled
  type position contains `void`; and
- the accepted immutable request carries qualified type spellings, parameter
  spellings, exact body binding, and enough information to render a complete
  stub declaration without reopening metadata.

Multiple complete physical MethodImpl or InterfaceImpl occurrences produce a
stable language refusal because one C# declaration cannot preserve that
multiplicity. Rejected, absent, unresolved, mismatched, or unposted evidence
produces `Unavailable` atomically. Method-like categories and type shapes
outside this first proof boundary also remain `Unavailable`; the implementation
does not misstate an unimplemented but potentially valid C# form as a language
impossibility. In particular, an otherwise valid explicit-interface operator
whose operands do not include the implementing containing type is outside this
slice rather than a language refusal.

Ordinary methods, constructors, conversions, checked operators, generic
method-like declarations, and broader explicit-interface methods remain later
expansions of this same contract.

Properties, indexers, events, and accessor-level requests remain outside the
first slice. They require the complete MethodSemantics work in
[#4849][issue-4849] and the remaining declaration evidence tracked by
[#5164][issue-5164]. Finalizer and other specialized categories enter only
when their complete ordinary-declaration facts and CSharp rules fit the same
focused method-like contract.

## Pathological and neighboring evidence

The Release gate for the first implementation must include:

- the real `System.Int32` generic-math operator witness;
- the same `op_*` name with and without authenticated operator evidence;
- same-spelled declaration owners from different assembly scopes;
- multiple MethodImpl rows for one body, including duplicate physical rows;
- repeated InterfaceImpl associations whose multiplicity cannot be collapsed;
- a relevant InterfaceImpl rejection after an earlier positive relationship,
  proving atomic non-publication;
- exact InterfaceImpl absence where interface reachability is not posted,
  proving `Unavailable` rather than a false class-slot or language conclusion;
- a Metadata degraded or rejected declaration result with no partial accepted
  request;
- a form accepted under one language profile and refused under another; and
- hostile control characters in artifact names proving failure text contains
  no artifact payload.

Compiler-produced fixtures establish ordinary legal forms. Authored IL or
metadata-builder fixtures establish duplicate, malformed, and contradictory
boundaries that source compilers do not produce.

## Gates

The implementation claim is enforced by focused Release tests:

| Gate | Property |
| --- | --- |
| `CDR001` | A post accounts for every required result and every MethodImpl relationship occurrence before decision. |
| `CDR002` | Structured identities, not display text or coordinates, preserve MethodImpl-to-InterfaceImpl association. |
| `CDR003` | Complete valid facts produce either one accepted request or a stable language refusal; incomplete facts produce typed unavailability. |
| `CDR004` | Rendering an accepted request performs no relationship or declaration-category decision and publishes no partial source. |
| `CDR005` | Language-profile changes cannot reuse acceptance from another profile. |
| `CDR006` | Failure text contains no artifact-authored payload. |
| `CDR007` | Posted inputs and all outcomes remain detached and usable after Metadata session retirement. |

The public input and result-shape test rejects live authority types in the post
and outcome object graphs. Focused behavior tests prove the forbidden shortcut
cases through outcomes rather than policing unrelated trusted CSharp code.

## Non-claims

This contract does not:

- validate metadata or replace a Metadata rejection;
- define MethodDef, MethodImpl, InterfaceImpl, or MethodSemantics evidence;
- infer interface inheritance or runtime dispatch;
- guarantee that every valid metadata declaration has C# syntax;
- define contained metadata fallback or general inspection presentation;
- select RTS targets, choose compilation scope, compile source, compare IL, or
  assign fidelity;
- reconstruct a project, source tree, attributes not included in the post, or
  reference closure;
- make MVID, row tokens, names, or source spelling durable correspondence;
- load an inspected assembly or add Roslyn to a product path; or
- update root product, overview, architecture, or skill documentation.

## Completion boundary

The design slice is complete when this focused document is reviewed and merged.
It locks the CSharp-owned input, outcome, and invariants without pretending the
open Metadata prerequisites are implemented.

The first implementation slice is complete only when [#7886][issue-7886] has
posted the required ordinary MethodDef facts, [#8348][issue-8348] has posted
the required containing TypeDef facts, [#8399][issue-8399] has posted the exact
local declaration-owner TypeDef address, the method-like producer and accepted
request land together, `CDR001` through `CDR007` pass in Release, and the RTS
adoption remains deferred to [#7888][issue-7888] and [#7889][issue-7889].

[csharp-spec]: https://learn.microsoft.com/dotnet/csharp/language-reference/language-specification/
[ecma-335]: https://ecma-international.org/publications-and-standards/standards/ecma-335/
[issue-4849]: https://github.com/richlander/dotnet-inspect/issues/4849
[issue-4852]: https://github.com/richlander/dotnet-inspect/issues/4852
[issue-5164]: https://github.com/richlander/dotnet-inspect/issues/5164
[issue-6199]: https://github.com/richlander/dotnet-inspect/issues/6199
[issue-7886]: https://github.com/richlander/dotnet-inspect/issues/7886
[issue-7887]: https://github.com/richlander/dotnet-inspect/issues/7887
[issue-7888]: https://github.com/richlander/dotnet-inspect/issues/7888
[issue-7889]: https://github.com/richlander/dotnet-inspect/issues/7889
[issue-7890]: https://github.com/richlander/dotnet-inspect/issues/7890
[issue-7897]: https://github.com/richlander/dotnet-inspect/issues/7897
[issue-8348]: https://github.com/richlander/dotnet-inspect/issues/8348
[issue-8399]: https://github.com/richlander/dotnet-inspect/issues/8399
[roslyn-error-type]: https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Symbols/ErrorTypeSymbol.cs
[roslyn-pe-method]: https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Symbols/Metadata/PE/PEMethodSymbol.cs
[roslyn-pe-type]: https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Symbols/Metadata/PE/PENamedTypeSymbol.cs
[roslyn-symbol-factory]: https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Symbols/Metadata/PE/SymbolFactory.cs
