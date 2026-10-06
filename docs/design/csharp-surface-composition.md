# Single-homed C# surface: composition map

## Status and scope

Proposed composition map. It owns no component contract. It names the
participating owners, the typed handoffs between them, and the order in which
focused efforts adopt the direction. Each slice below is its own focused effort
under [Design scope](../design-scope.md#one-owner-per-focused-design) with its
own owning document, issue, and PR.

The user approved this cross-owner direction on 2026-10-06, with
ReturnToSender (RTS) as the stress-test consumer. That approval covers this map
and its sequencing. It does not approve any slice's design in advance.

Tracking: #9538. RTS cutover sequencing remains with #6199, and harness
origination removal remains with #2782.

## Claim

C# spelling of metadata declarations, type skeletons, and decompiled bodies
has one home per capability. Every consumer reads owner-issued structure and
coordinates instead of re-deriving facts from rendered text, or spelling C#
itself.

Two product requirements anchor the claim:

- **Metadata-only C#.** A caller can obtain the C# spelling of one member or a
  whole type skeleton from Metadata facts without the Decompiler.
- **Single-homed capability.** Each spelling or composition capability has one
  owner, so consumers cannot drift from it.

RTS is the stress test because it compiles what it receives. A declaration
fragment that the harness spells, repairs, or drops becomes compiled evidence
behind an `Exact` verdict. That violates
[harness evidence integrity](../evidence-and-validation.md) and
[the RTS stance](fact-planned-compile-back-harness.md#strategic-stance).

## Participants

| Owner | Role in this map | Owning contract |
| --- | --- | --- |
| `ILInspector.Metadata` | Issues structured declaration and type facts. Spells no C# on new paths. | [Method declaration evidence](metadata-method-declaration-evidence.md) and its sibling Metadata evidence designs |
| `ILInspector.CSharp` | Owns the one C# type speller, declaration and shell composition, and the representability outcome for each profile | [Declaration representability](csharp-declaration-representability.md); [Product libraries](csharp-member-recompilation.md#product-libraries) |
| `CSharpText` | Model-free lowering of composed C# to text and coordinates | [Overview](../overview.md) |
| `ILInspector.Decompiler` | Composes bodies from decided IR and hands body-shaped declaration facts to `ILInspector.CSharp` as neutral data | [Decompiler architecture](../decompiler-architecture.md#position-in-the-product); [The output half](value-typed-emission.md#the-output-half--structure-not-strings) |
| ReturnToSender (tools) | Selects targets, concatenates product artifacts, compiles, and compares. Originates no C#. | [RTS harness](fact-planned-compile-back-harness.md#returntosender-harness); #2782 |
| Display consumers | Read roles and coordinates: member parts, member source diff, body diff, and the annotated source viewer | [Member text parts](member-text-parts.md#signature); [Member source diff](member-source-diff-presentation.md#shared-declaration-boundary); [Implementation diff](implementation-diff.md#ownership); [Annotated source](annotated-source-viewer-interaction.md#source-presentation) |

## Typed handoffs

The participants exchange these four kinds of data. Their shapes belong to the
owning slices.

1. **Metadata to CSharp: structured facts.** Types reach the speller as
   structured shapes with identity, such as `ApiTypeShape` and `ApiSignature`,
   not as pre-spelled strings. A type that cannot be resolved stays visibly
   unresolved.
2. **Decompiler to CSharp: body content and body facts.** Bodies fill body
   slots in a composed declaration. Facts a declaration needs from a body,
   such as the constructor initializer or primary-constructor parameters,
   cross as neutral Metadata-level data. `ILInspector.CSharp` never reads
   Decompiler IR ([#2777](https://github.com/richlander/dotnet-inspect/issues/2777)).
3. **CSharp to consumers: a composed artifact.** The artifact pairs text with
   owner-issued coordinates. The replaceable body range exists today. Later
   slices add signature and body spans, then type-reference spans with
   identity.
4. **Profile and outcome.** The compilable profile, used by RTS, accepts a
   declaration or returns a typed refusal. The display profile may also return
   labelled fallback text. Consumers apply their own policy to the outcome.
   RTS never fills a refusal with harness C#.

## Sequencing

Each slice deletes duplicated spelling or text re-derivation in the consumer it
serves. RTS adopts first wherever it is affected.

1. **Explicit-interface method declarations** (#9531). Owner: CSharp; RTS is
   the first adopter.
2. **Structured constructor initializer and base shells** (#9532). Order: the
   Decompiler issues the initializer as data, then CSharp composes base
   shells, then RTS stops dropping `base(...)` and synthesizing constructors.
3. **Exact accessibility** (#9533), **constant literals** (#9534), and
   **parameter declarations and interface stubs** (#9535). Owner: CSharp; RTS
   is the first adopter of each.
4. **One C# type speller** (#9537). A focused pattern design in
   `ILInspector.CSharp` over structured Metadata type facts, with the
   declaration writer as the first adopter. Later adopters are RTS and the
   Decompiler printer, which retires its own type spelling.
5. **Composed-artifact coordinates.** A focused pattern design for signature
   and body spans, with the first adopter named in that design. Later
   adopters:
   - RTS span attribution (#9536);
   - member parts in the CLI and Browser/Wasm hosts;
   - member source diff;
   - the annotated source declaration;
   - body diff statement kinds.
6. **Expression-level surface.** Unchanged from
   [the output half](value-typed-emission.md#the-output-half--structure-not-strings):
   decided semantics come first. This map moves member-level composition
   earlier; it does not move expression-level structure ahead of decided
   semantics.

## Gates

Each slice names its own gates. Every slice that RTS consumes keeps the RTS
parity gate (#2776) green, and none may turn a legacy `Exact` row into a silent
loss (#6199). A slice counts toward this map only when it deletes the harness
origination or consumer re-derivation it replaces.

## Non-claims

- No new type system. Type identity convergence follows
  [Type identity and the TypeRef duplication](fact-planned-compile-back-harness.md#type-identity-and-the-typeref-duplication).
- No change to Metadata evidence, representability, or RTS admission
  contracts. Slices adopt those contracts; they do not restate them.
- No change to stack-slot storage decisions, which are tracked in #9371.
- No Windows Metadata input.
