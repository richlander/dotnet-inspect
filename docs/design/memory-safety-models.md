# Memory-safety models and evidence

## Status and ownership

This document owns the product vocabulary for the legacy and updated C#
memory-safety models and the rules for composing project, metadata, signature,
method-body, and provenance evidence.

It does not take implementation ownership from the participating subsystems:

- Metadata owns module, member, and decoded-signature facts, including
  version-aware caller-contract classification.
- Analysis owns IL-body evidence and method-level compositions over Metadata
  facts.
- CSharp owns model-bound declaration spelling from typed Metadata facts.
- Decompiler owns source reconstruction and safety-context rendering.
- Research owns cross-evidence summaries.
- JsExportSurface owns export admission and its unsupported-shape policy.
- The CLI owns user-visible presentation.
- Project inspection owns facts declared by or evaluated from project inputs.
- A provenance service, when available, owns correspondence between a project
  and a binary.

The composition rules here do not authorize one implementation change to sweep
all of those owners. Each gap must be addressed through its owning subsystem.

## Terms

| Term | Meaning |
| --- | --- |
| Rules model | The language rules under which a module was compiled. |
| Propagates unsafe | Using the member requires the caller to establish an `unsafe` context. Roslyn calls this *requires-unsafe* or caller-unsafe. |
| Unsafe user | A member with positive evidence that an unsafe context was established for its declaration or implementation. Publishing a caller contract alone is not use. |
| Safe boundary | Under the updated model, a member that uses an `unsafe` context internally but does not propagate that requirement to its caller. |
| Unsafe permission | Whether the build permits unsafe syntax and related constructs. In an SDK project this is controlled by `AllowUnsafeBlocks`. |
| Project policy | The memory-safety rules and unsafe permission requested by project inputs for a particular build. |
| Binary fact | Evidence present in the inspected module's metadata, signatures, or IL. |

Propagation, use, and permission are independent dimensions. A module can use
the updated model without containing unsafe code. A member can propagate unsafe
without having an IL body. A safe-boundary method can use unsafe operations
without propagating unsafe.

## Version vocabulary

This product uses **v1** and **v2** as model names:

- **v1** is the legacy model represented in current binaries by an absent
  `MemorySafetyRulesAttribute`.
- **v2** is the currently implemented updated model represented by
  `[module: MemorySafetyRulesAttribute(2)]`.

The names do not imply that `[module: MemorySafetyRulesAttribute(1)]` is a
valid legacy marker. Current Roslyn treats every explicit version other than
`2` as unsupported. In particular, an explicit version `1` is unsupported
rather than equivalent to an absent attribute.

The accepted SDK design may use `<MemorySafetyRules>1</MemorySafetyRules>` as
a future project-side request for legacy compilation. That configuration value
must result in an unmarked legacy binary; it does not make attribute version
`1` valid.

Consumers must preserve the distinction between these states:

| State | Meaning |
| --- | --- |
| Legacy, unmarked | The module has no `MemorySafetyRulesAttribute`; apply v1 compatibility rules. |
| Updated v2 | The module has one valid module attribute with version `2`; apply v2 rules. |
| Unsupported version | The module attribute contains another integer. Preserve it as unrecognized; Roslyn also applies legacy compatibility inference and reports the unsupported marker when imported methods or accessors are consumed. |
| Malformed marker | The attribute cannot be decoded according to its expected constructor shape. Preserve the failure; Roslyn likewise uses compatibility inference while treating imported methods and accessors as carrying an unrecognized marker. |
| Conflicting markers | More than one candidate marker prevents a unique module judgment; member contracts are unavailable. |

The raw integer and the recognized model are separate facts. Reporting an
actual version must not silently turn every value greater than or equal to `2`
into a supported updated model. Future compiler versions may define additional
values; support begins when the corresponding contract is adopted, not merely
when a larger integer appears.

`MemorySafetyRulesAttribute` is a module attribute. An assembly-level or
member-level lookalike is not evidence of the module's rules model.

### Shared binary-model handoff

Metadata owns one normalized rules-model fact per inspected module and one
version-aware contract resolver for members in that module. The module fact
must preserve both recognition state and the raw integer when one was decoded.
The contract resolver combines that state with the correct member-kind carrier
and legacy pointer compatibility, producing `None`, `Implicit`, `Explicit`, or
`Unavailable`. Analysis, Decompiler, and API-surface extraction should consume
those Metadata-owned facts rather than independently testing for attribute
names, and the CLI should render them rather than reinterpret the integer.

This shared handoff prevents caller-contract analysis, source reconstruction,
and user-visible Signals from assigning different meanings to the same module.
Project policy remains a separate source-evidence type and must not be folded
into the binary fact.

## The two models

### V1: legacy and compatibility rules

In the legacy model:

- a pointer or function-pointer type in a callable member's parameter or return
  signature makes the member propagate unsafe;
- the compiler's compatibility rule also applies to pointer-bearing fields,
  except fixed-size-buffer fields;
- the `unsafe` modifier on a type or member establishes a lexical unsafe
  context for declarations and bodies within its scope;
- metadata does not preserve that source modifier by itself;
- a pointerless member declared `unsafe` may leave no recoverable binary
  evidence when its body also leaves no relevant signature, local, call, or
  opcode evidence; and
- the module normally has no `MemorySafetyRulesAttribute`.

When a consumer reads an unmarked module, pointer-bearing signatures are the
compatibility rule for deciding which members propagate unsafe. A
`RequiresUnsafeAttribute` lookalike in an unmarked module does not opt that
module into v2 semantics.

Historically, declaring those pointer shapes also required a lexical unsafe
context. The unsafe-evolution pointer relaxations are language-feature-gated
independently of the module's v1/v2 marker, however. An unmarked module compiled
with those relaxations can contain pointer signatures or locals that did not
establish an unsafe context. Pointer shape is therefore sufficient binary
evidence for v1 compatibility propagation, but not universal evidence that a
member is an unsafe user. That latter judgment needs a context-requiring
operation or corresponding source/build evidence.

### V2: updated rules

In the updated model:

- `unsafe` on a member publishes a caller contract represented in metadata by
  `RequiresUnsafeAttribute`, including for fields;
- the member modifier does not itself establish an unsafe context for the
  member body;
- an `unsafe` instance constructor is the narrow exception: its modifier
  establishes an unsafe context for its `base(...)` or `this(...)` initializer,
  but still not for its body;
- a pointer-bearing signature does not propagate unsafe unless the member has
  the v2 caller contract;
- an inner `unsafe` block or expression establishes the body context needed for
  operations that require it; and
- the module publishes its rules version with
  `[module: MemorySafetyRulesAttribute(2)]`.

A v2 member with positive body-unsafety evidence and no
`RequiresUnsafeAttribute` is a safe boundary: it accepts the audit obligation
inside its implementation instead of imposing it on callers. A v2 member with
`RequiresUnsafeAttribute` is a propagator whether or not its body contains
unsafe operations.

The metadata carrier depends on the member kind:

| Contract surface | V2 attribute carrier |
| --- | --- |
| Method or constructor | MethodDef |
| Field | FieldDef |
| Property or accessor | PropertyDef for a property contract; MethodDef for an accessor-specific contract; accessors inherit through MethodSemantics |
| Event | EventDef; add/remove accessors inherit the event contract through MethodSemantics |

A TypeDef-level lookalike is not a valid substitute for any of these contracts.
Same- and cross-assembly resolution must follow the accessor association before
deciding that a MethodDef lacks a contract.

Current Roslyn synthesizes an explicit field contract directly onto FieldDef
even though the cited runtime `RequiresUnsafeAttribute.AttributeUsage` does not
include fields. Metadata consumers must follow the compiler's emitted and
imported contract rather than reject a legitimate FieldDef application by
reapplying runtime reflection-target policy.

### Comparison

| Question | V1: legacy | V2: updated |
| --- | --- | --- |
| Binary model marker | Attribute absent | Module attribute version `2` |
| What propagates unsafe? | Pointer or function-pointer callable signatures and non-fixed-buffer fields under compatibility rules | `RequiresUnsafeAttribute` |
| Meaning of member `unsafe` | Establishes a lexical unsafe context | Publishes a caller contract; an instance constructor also establishes its initializer context |
| Meaning of a pointer-bearing signature | Compatibility propagator | Not a propagator by itself |
| How a body establishes unsafe context | Enclosing type/member context or inner unsafe context | Inner unsafe block or expression |
| Safe-boundary method | Not distinguishable as a separate caller contract | Body uses unsafe context, but member does not propagate |

## Mixed-model consumption

Mixed-model behavior has two inputs:

1. The callee's module and member evidence classify the callee contract as
   implicit, explicit, or none.
2. The caller's model determines whether an explicit v2 contract is enforced.
   An implicit v1 compatibility contract is enforced for both caller models.

| Target-member evidence | Contract | V1 caller | V2 caller |
| --- | --- | --- | --- |
| Unmarked module and pointer-bearing callable signature or non-fixed-buffer field | Implicit | Requires unsafe context | Requires unsafe context |
| Unmarked module without a compatibility pointer contract | None | No caller requirement | No caller requirement |
| V2 module and `RequiresUnsafeAttribute` | Explicit | Contract not enforced | Requires unsafe context |
| V2 module without `RequiresUnsafeAttribute`, including a pointer-bearing signature or field | None | No caller requirement | No caller requirement |
| Conflicting module markers | Unavailable | Enforcement unavailable | Enforcement unavailable |

For an unsupported or malformed target-module marker, preserve and report the
unrecognized state. Roslyn also classifies member contracts with the legacy
pointer compatibility rule in this state, while reporting an unrecognized
attribute-version diagnostic when an imported method or accessor is consumed.
Field use does not consistently produce the same Roslyn use-site diagnostic,
so product output must surface the invalid module marker independently of
member access. It should present both facts: a compatibility-derived contract
is not permission to call the module v1 or to hide its invalid marker.

Conflicting markers do not have a compatibility fallback in this product
contract because there is no unique module fact to interpret. Consumers must
propagate the unavailable contract instead of rendering or reporting either a
safe call or an invented unsafe requirement.

The caller's project policy also determines which source operations it may
express, but it does not rewrite the callee's classified contract. A legacy
caller ignoring an explicit v2 contract is compatibility behavior, not evidence
that the callee lacks that contract.

## Pointer shape and operation semantics

The module model controls caller-contract interpretation. It does not by itself
select every rule for pointer syntax and operations. Unsafe-evolution language
features are independently gated by language version.

In particular, pointer declarations, pointer-bearing lambda parameters,
ordinary `fixed` statement headers, pointer-target stack allocation, pointer
arithmetic, and pointer comparison can be legal in safe contexts under the
updated language feature regardless of whether the module publishes v1 or v2.
Pointer dereference and function-pointer invocation remain examples of
context-requiring operations.

Raw IL does not always preserve the source distinction. A `localloc` operation,
for example, can represent a safe pointer-target `stackalloc` or a conditionally
unsafe uninitialized span allocation. Analysis should retain the raw operation
as structural evidence until product-owned reconstruction and relevant facts
such as `SkipLocalsInit` support a source-level judgment.

Consequently, a binary-only unsafe-user answer must distinguish:

- structural pointer and IL evidence;
- operations known to require an unsafe context for the selected language
  semantics; and
- source or provenanced build evidence that establishes the original lexical
  context.

## Project policy

### Current .NET 11 preview activation

The current .NET 11 preview mechanism is the raw compiler feature:

```xml
<PropertyGroup>
  <LangVersion>preview</LangVersion>
  <Features>$(Features);updated-memory-safety-rules</Features>
</PropertyGroup>
```

`Features` is a generic compiler escape hatch. The Roslyn implementation calls
this a temporary opt-in mechanism.

`EnablePreviewFeatures` is insufficient by itself. It can select preview
language behavior in applicable SDK configurations, but it does not append the
`updated-memory-safety-rules` compiler feature.

The accepted SDK direction proposes a dedicated numeric property:

```xml
<MemorySafetyRules>2</MemorySafetyRules>
```

That property is not yet the supported .NET 11 activation mechanism. A project
reader must not interpret the presence of an otherwise unevaluated
`MemorySafetyRules` property as proof that the compiler received or honored it.

This repository has a fixture-only `Directory.Build.targets` alias that maps
text values such as `updated` to the raw compiler feature. That alias is test
infrastructure, not SDK behavior or user-facing configuration guidance.

### Unsafe permission is independent

`AllowUnsafeBlocks` controls whether the compiler permits unsafe source. It
does not select a memory-safety model and does not cause
`MemorySafetyRulesAttribute` to be emitted.
There is no corresponding current C# SDK property named `EnableUnsafe`;
`AllowUnsafeBlocks` is the established permission switch.

The strongest default policy is updated enforcement with unsafe syntax
disabled:

```xml
<PropertyGroup>
  <LangVersion>preview</LangVersion>
  <Features>$(Features);updated-memory-safety-rules</Features>
  <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
</PropertyGroup>
```

`AllowUnsafeBlocks` may also be absent because `false` is the default. A module
compiled with this policy is still v2 even when it contains no unsafe members
or operations. The module marker records the rules used for compilation, not
the presence of unsafe code.

Conditional properties, imported props and targets, command-line overrides,
target frameworks, and configurations can change the effective policy.
Reading literal XML establishes declared source evidence; claiming the policy
of a particular build may require evaluated project evidence.

## Evidence planes and provenance

Complete policy assessment requires project and binary evidence, but those
observations remain independent:

| Evidence plane | Answers | Does not answer |
| --- | --- | --- |
| Project declaration or evaluation | Which rules and unsafe permission the build requested | Which model an arbitrary binary actually publishes |
| Module metadata | The binary's raw rules marker and recognized model | Whether unsafe source was permitted |
| Member metadata and signatures | Which callable members and fields publish or imply caller contracts for that module model | Which bodies actually use unsafe operations |
| Method-body analysis | Candidate declaration, local, call, and IL-operation evidence for model-aware interpretation | The original lexical source form in every case |
| Provenance | Whether a project/build corresponds to a binary | The safety meaning of either artifact |

A report may place project and binary observations side by side without proving
that one produced the other. It must label their correspondence as
**unverified** unless a provenance owner supplies affirmative evidence.
Matching names, paths, target frameworks, versions, or timestamps is not a
substitute for typed provenance.

## Product answer map

| User question | Required evidence | Current product answer |
| --- | --- | --- |
| Which model did this binary publish? | Module `MemorySafetyRulesAttribute` constructor integer and recognition rules | Metadata decodes an `int?`; Library Signals displays it. Current scope and unsupported-version handling are incomplete. |
| Did this project request updated enforcement? | Declared or evaluated `LangVersion`, `Features`, and future supported model property | Not currently answered. The `project` command reads `project.assets.json`, not these project properties. |
| Was unsafe source permitted? | Declared or evaluated `AllowUnsafeBlocks` | Not currently answered. It cannot be inferred from the binary marker. |
| Which members propagate unsafe? | Version-aware composition of the module model with callable signatures, field types, and the correct v2 attribute carrier | Metadata exposes `ApiMember.IsUnsafe` and Analysis exposes method `CallerUnsafeMode`; both are currently model-incomplete, and neither provides complete field/accessor coverage. |
| Which methods use unsafe facilities? | Context-requiring operations plus source/build evidence where pointer declarations are ambiguous | `MethodSafetyAnalysis` produces structural and operation evidence, but it does not yet separate pointer shape from proof that an unsafe context was established. |
| Which methods are safe boundaries? | Recognized v2 module, no member propagation contract, and positive body-unsafety evidence | Not currently exposed as a composed query. |
| How should reconstructed C# express unsafe context? | Binary model, member contract, and recovered body requirements | Decompiler owns this through [memory-safety rendering modes](memory-safety-modes.md). |
| Is the project configured for the strongest default? | Updated project policy, unsafe permission disabled, v2 binary, and no propagators or unsafe users | No single current query composes this answer. |
| Did this project produce this binary? | Affirmative provenance evidence | Unverified unless supplied separately. |

## Focused successors

Implementation findings are preserved in owner-specific issues rather than
specified here:

| Owner | Successor | Dependency |
| --- | --- | --- |
| Metadata module and member contracts | [#5252](https://github.com/richlander/dotnet-inspect/issues/5252) | First binary-evidence dependency |
| Metadata API representation | [#5253](https://github.com/richlander/dotnet-inspect/issues/5253) | After #5252 |
| Analysis | [#5254](https://github.com/richlander/dotnet-inspect/issues/5254) | After #5252; parallel with Decompiler |
| Decompiler | [#5255](https://github.com/richlander/dotnet-inspect/issues/5255) | After #5252; parallel with Analysis |
| Project build-policy inspection | [#5256](https://github.com/richlander/dotnet-inspect/issues/5256) | Independent of binary work |
| C# declaration spelling | [#5257](https://github.com/richlander/dotnet-inspect/issues/5257) | After #5253 |
| JS-export admission | [#5258](https://github.com/richlander/dotnet-inspect/issues/5258) | After #5253 |
| Research summaries | [#5259](https://github.com/richlander/dotnet-inspect/issues/5259) | After #5253 and #5254 |
| Library Signals | [#5260](https://github.com/richlander/dotnet-inspect/issues/5260) | After #5252 |
| Cross-evidence posture query | [#5262](https://github.com/richlander/dotnet-inspect/issues/5262) | After #5252, #5254, and #5256 |

Each successor re-derives its own contract in the named owning document. This
composition document owns only the vocabulary, evidence-plane boundaries, and
typed handoffs among them.

## Demo

An actual throwaway `net11.0` class library containing only
`public sealed class C { }` was built with the strongest default project
configuration shown above. The command was:

```text
dnx dotnet-inspect -y -- library bin/Release/net11.0/MemorySafetyV2.dll -S Signals
```

The memory-safety rows from its output were:

| Area | Signal | Value | Evidence |
| --- | --- | --- | --- |
| Memory safety | Memory safety model | Updated (v2) | module MemorySafetyRulesAttribute |
| Memory safety | RequiresUnsafe members | 0 | RequiresUnsafeAttribute |
| Memory safety | Disable runtime marshalling | No | DisableRuntimeMarshallingAttribute |
| Memory safety | Unsafe public signatures | 0 | public pointer signatures |

For a project built with the strongest default policy, the intended composed
report would keep every observation and its limitation visible:

| Observation | Value | Evidence |
| --- | --- | --- |
| Requested rules | Updated preview rules | Project `LangVersion` and `Features` |
| Unsafe source permission | Disabled | Project `AllowUnsafeBlocks` absent or `false` |
| Published binary model | Updated v2 | Module `MemorySafetyRulesAttribute(2)` |
| Propagating members | 0 | Version-aware member metadata |
| Unsafe users | 0 | Version-aware declaration and method-body analysis |
| Project-to-binary correspondence | Unverified | No provenance evidence supplied |

The last row is not a failure of the safety evidence. It is the correct
boundary on the claim: the project describes one intended build policy, while
the binary independently demonstrates one compiled artifact.

## External contracts

The model follows the implemented compiler and runtime contracts:

- [C# unsafe evolution proposal](https://github.com/dotnet/csharplang/blob/f445f642755a28631b7e37db01f6373c437159c3/proposals/unsafe-evolution.md)
- [SDK memory-safety enforcement design](https://github.com/dotnet/designs/blob/8f17cc55212fe45f563741aa7137d432d82482d5/accepted/2025/memory-safety/sdk-memory-safety-enforcement.md)
- [Roslyn memory-safety version handling](https://github.com/dotnet/roslyn/blob/e79586494f629704a0fd18b7afb840144fd5e673/src/Compilers/CSharp/Portable/Symbols/Metadata/PE/PEModuleSymbol.cs)
- [Roslyn caller-unsafe interpretation](https://github.com/dotnet/roslyn/blob/e79586494f629704a0fd18b7afb840144fd5e673/src/Compilers/CSharp/Portable/Symbols/Metadata/PE/PEMethodSymbol.cs)
- [Roslyn field-contract emission](https://github.com/dotnet/roslyn/blob/e79586494f629704a0fd18b7afb840144fd5e673/src/Compilers/CSharp/Portable/Symbols/Source/SourceMemberFieldSymbol.cs)
- [Roslyn field-contract import](https://github.com/dotnet/roslyn/blob/e79586494f629704a0fd18b7afb840144fd5e673/src/Compilers/CSharp/Portable/Symbols/Metadata/PE/PEFieldSymbol.cs)
- [Runtime `MemorySafetyRulesAttribute`](https://github.com/dotnet/runtime/blob/aa036afce592ad80e938a35bd376222fb232cba9/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/MemorySafetyRulesAttribute.cs)
- [Runtime `RequiresUnsafeAttribute`](https://github.com/dotnet/runtime/blob/aa036afce592ad80e938a35bd376222fb232cba9/src/libraries/System.Private.CoreLib/src/System/Diagnostics/CodeAnalysis/RequiresUnsafeAttribute.cs)

At the cited snapshot, one paragraph in the language proposal anticipates a
different future version value. Roslyn's implementation and tests emit and
recognize `2`; the product contract follows emitted compiler behavior while
preserving the raw integer for future versions.
