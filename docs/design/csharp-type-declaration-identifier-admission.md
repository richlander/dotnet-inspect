# C# type-declaration identifier admission

## Status

Implemented by `CSharpIdentifier.AdmitTypeDeclaration` for issue #5215.
`CSharpTypeDeclarationIdentifierAdmissionTests` gates the compiler and emitted
TypeDef identity contract in Release.

## Responsibility

`CSharpText` owns the model-free decision from one arbitrary string identity to
one C# type-declaration identifier result under the repository's current
language version.

The input is identity text, not source text, display text, a Metadata name, or
an `ApiType`. The result is one of:

- `CSharpTypeDeclarationIdentifierAdmission.Admitted`, carrying one legal
  source spelling whose compiler-emitted TypeDef name equals the input; or
- `CSharpTypeDeclarationIdentifierAdmission.Refused`, carrying one closed
  `CSharpTypeDeclarationIdentifierRefusalReason` and no source spelling.

An admitted reserved or position-sensitive word may differ from its identity
only by the ordinary `@` prefix. Admission does not sanitize, replace, encode,
or truncate identity. Result-arm construction remains assembly-owned so public
callers cannot manufacture an admitted spelling outside the owner.

## Ordered admission

`CSharpIdentifier.AdmitTypeDeclaration` applies one ordered decision:

1. Any UTF-16 surrogate code unit refuses as `InvalidIdentifier`. This includes
   well-formed supplementary-plane scalars such as U+10400, which the current
   compiler rejects in both raw and `\UXXXXXXXX` source forms.
2. Leading U+FEFF code units are ignored only while evaluating the remaining
   text against the existing BMP identifier grammar, matching the compiler's
   treatment of that character before an identifier. Empty or otherwise
   invalid remaining text refuses as `InvalidIdentifier`. Every other leading
   format character is invalid. The grammar admits the compiler-supported
   letter, letter-number, combining-mark, decimal-digit,
   connector-punctuation, and format categories in their valid start or part
   positions.
3. A Unicode `Format` code unit in an otherwise legal identifier refuses as
   `IdentityNotPreserved`. The current compiler accepts examples such as
   U+200C, U+200D, and U+00AD in identifier-part positions and U+FEFF before
   the identifier, but removes them from the emitted TypeDef name.
4. The remaining identity is emitted bare or with `@` according to the
   type-declaration keyword policy.

The type-declaration keyword policy includes the universally reserved words,
the existing conservative declaration contextual set, C# 14 `extension`, and
the reserved intrinsics `__arglist`, `__makeref`, `__reftype`, and
`__refvalue`. `extension` remains specific to this type-declaration boundary;
the broader presentation-only declaration policy is unchanged.

## Evidence

`CSharpTypeDeclarationIdentifierAdmissionTests` uses Roslyn only as a test
oracle. It:

- compiles every admitted specimen with the repository's preview language
  version and reads the resulting TypeDef name through SRM;
- derives the complete reserved and contextual keyword inventory from the
  compiler, then proves every product spelling compiles with exact identity;
- derives the complete BMP Unicode `Format` inventory, then proves each
  leading and identifier-part specimen receives the refusal reason matching
  the compiler's rejection or changed emitted identity;
- proves U+10400 is rejected by the current compiler; and
- pins closed refusal reasons for empty, invalid-start, punctuation,
  line-terminator, surrogate, and format-character neighbors.

Product code remains dependency-free, Roslyn-free, and compatible with
NativeAOT and Browser/Wasm.

## Non-claims

This boundary does not define:

- Metadata identity, arity, nesting, or generated-name policy;
- namespaces, members, parameters, type references, or body identifiers;
- declaration composition, source publication, diagnostics, or fallback
  spelling;
- presentation containment or sanitization; or
- identifier admission for a caller-selected language version.

The first model-bound consumer is
[C# declared-type self-name admission](csharp-declared-type-self-name.md).
