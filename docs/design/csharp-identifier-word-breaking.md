# C# identifier word breaking

## Status, owner, and claim

Status: **implemented contract** for
[#8897](https://github.com/richlander/dotnet-inspect/issues/8897), the textual
prerequisite for
[Library name-family summaries](https://github.com/richlander/dotnet-inspect/issues/8698).

`CSharpText` owns this claim:

> Given arbitrary UTF-16 text, one exact identifier-word oracle, and optional
> explicit numbered-family context, return an ordinal, culture-independent,
> gap-free sequence of source spans classified as words, ordinals,
> separators, or unresolved runs, with the rule evidence that produced every
> classification.

The contract is model-free. It does not know whether the input came from
Metadata, source, a Type, a member, or a Library. It neither acquires nor
rebuilds an oracle and does not count or interpret name families.

The contract is verified by the `CSharpText.Tests` Release gate under
[Required evidence](#required-evidence).

## User question

> Which exact word-like units can the product issue from this identifier, and
> where must it preserve uncertainty rather than guess?

This is reusable textual substrate, not natural-language analysis. Its first
production consumer is the Library name-family summary, which reduces
thousands of Type names to evidence-backed suffix populations before an agent
assigns architectural meaning.

## Basis and motivating evidence

The #8634 survey examined 27,108 Type names from the repository's pinned
top-100 NuGet corpus. Ordinary case boundaries broke 94.0% of names. A
prototype informed by the .NET 10.0.10 reference pack reached about 97% on a
fresh hand-graded sample. The remaining uncertainty concentrated in
mixed-case proper nouns, unknown acronym runs, digit compounds, and
library-local numbered families.

`Microsoft.NETCore.App.Ref` 10.0.10 is the motivating oracle asset. Its
API-reviewed names contain the difficult positive controls:

- `HMACSHA256`;
- `RSAPKCS1`;
- `IPv6`; and
- `UInt16`.

FluentValidation 12.1.1 is the first consumer control. Its recurring
`Validator` suffix demonstrates the product value of preserving ordinary
case-derived words without requiring every domain word to appear in the
runtime oracle.

Roslyn's `StringBreaker` at commit
`644b6341ab31edd8528721aee7f31e4c6bb57f37` is analogous implementation
evidence. It supplies conventional lower-to-upper, acronym-to-word, digit,
and punctuation boundaries for editor search. Its purpose deliberately
differs from this owner:

- it drops most punctuation rather than returning gap-free evidence;
- it issues opaque uppercase runs without qualifying recognition;
- it has no versioned vocabulary oracle or local numbered-family context; and
- its result is an unqualified list of spans for matching, not a reproducible
  analysis input.

This design retains those useful conventional boundaries but is stricter
where architecture evidence must remain inspectable.

## Input contract

One break binds:

```text
IdentifierWordBreakRequest
  Text                         arbitrary UTF-16
  OracleReceipt
  NumberedFamilyContext?       optional, explicit, same grammar version
```

The text is identity evidence. The breaker never sanitizes, normalizes,
case-folds, trims, or repairs it. Returned offsets are UTF-16 source offsets
into that exact string.

### Oracle receipt

An immutable oracle contains:

- a grammar version;
- an oracle vocabulary version;
- a deterministic digest over the ordered entry set;
- the immutable source coordinate from which the vocabulary was curated;
- exact ordinal atom entries;
- exact ordinal compound entries; and
- the review-set version for deliberately protected mixed-case spellings.

An **atom** is one independently recognized word such as `HMAC` or `RSA`. A
**compound** is one word whose internal case or digit shape must remain
together, such as `SHA256`, `PKCS1`, `IPv6`, or `UInt16`. Both are word
evidence; the distinction explains why a span was protected.

Entries match exact ordinal source spelling. Alternate casing is recognized
only through another explicit entry. The breaker preserves the input spelling
in every result and never performs culture-sensitive comparison.

The first product oracle is a source-controlled snapshot derived from
`Microsoft.NETCore.App.Ref` 10.0.10 public Type, MethodDef, Field, and
namespace-segment names, plus a reviewed set of runtime-spelled mixed-case
atoms. Its generation tool and checked-in receipt are implementation
evidence, not runtime acquisition. CSharpText remains network-free and does
not inspect assemblies.

A caller may supply another immutable oracle through the same contract. Its
receipt, entries, and results remain distinct from the product oracle; a
caller-selected version string cannot substitute for the deterministic entry
digest.

### Numbered-family context

Numbered-family context is optional textual evidence built for one explicit
ordered identifier population. It contains:

- the grammar version;
- a deterministic population digest and identifier count; and
- each exact identifier prefix whose final decimal suffix has at least three
  distinct positive canonical values in that population.

`DelegateInvoker1`, `DelegateInvoker2`, and `DelegateInvoker10` therefore
establish `DelegateInvoker` as a numbered-family prefix.
`LookupType3`, `LookupType4`, `LookupType5`, and `LookupType8` establish
`LookupType`. A single `Adler32` does not establish `Adler`.

Only a decimal run at the end of the complete identifier participates.
Evidence never crosses a separator and never treats an internal run such as
the `256` in `Secp256r1` as an ordinal. Leading zero, zero, sign, decimal
separator, exponent, and non-ASCII-number spellings are not canonical positive
ordinals.

The context builder owns only this textual grouping rule. The caller owns
which identifiers form the population and associates the resulting receipt
with any Library, artifact, or other model identity. The break operation
rejects context from another grammar version rather than silently ignoring
it.

The builder computes the population digest and count while enumerating the
caller-supplied text and keeps result construction owner-controlled. A later
model-bound consumer that claims the context describes its population must
recompute and compare that receipt at its own binding boundary; a break of one
string cannot prove the population association.

## Result contract

A successful result carries the exact oracle receipt, optional numbered-family
receipt, and an ordered span sequence. The spans cover `[0, Text.Length)`
exactly once: they do not overlap, leave gaps, or reorder text.

Every span has one of four classifications:

| Classification | Meaning |
| --- | --- |
| `Word` | The grammar admits this exact source spelling as one word. |
| `Ordinal` | Explicit population evidence identifies this final canonical decimal run as a numbered-family ordinal. |
| `Separator` | Unicode punctuation, symbol, or whitespace separates neighboring runs. |
| `Unresolved` | The grammar preserves this exact run but lacks sufficient evidence to call it a word, ordinal, or separator. |

Each span retains its UTF-16 start and length, exact source slice, and
owner-issued rule evidence. Consumers do not reconstruct a rule from the
spelling.

Empty text succeeds with an empty span sequence. It is not an unavailable
result. Arbitrary text, including malformed UTF-16, also returns a gap-free
sequence; malformed code units are unresolved evidence rather than an
exception or replacement character.

## Word grammar

The grammar is ordered. Earlier evidence protects a span from a later,
weaker rule.

### 1. Preserve separators and malformed text

Unicode punctuation, symbols, and whitespace form separator spans.
Whitespace classification takes precedence when a character is also a
control, so tab, CR, and LF are separators. Remaining controls are unresolved.

Format characters, unpaired surrogates, a leading combining mark, and Unicode
categories not admitted below also form unresolved spans. They are not
discarded or rewritten as separators.

A combining mark following an admitted letter remains in that word's source
span. The breaker does not perform Unicode normalization or grapheme
substitution.

### 2. Protect exact oracle entries

The breaker first finds the conventional case and letter/digit units described
by the remaining rules without committing their classification. An exact
mixed-case or digit-compound oracle entry can protect one or more complete
adjacent units, but it cannot begin or end inside an ordinary case-derived
word. This keeps `IPv6` and `UInt16` intact without allowing an atom `Valid`
to fragment `Validator` into `Valid` + `ator`.

An opaque uppercase or uppercase/digit run commits oracle words only when
exact entries cover the complete run. A suffix match does not partially
resolve it: an oracle containing `URL` but not `CF` leaves all of `CFURL`
unresolved.

The breaker ranks complete oracle segmentations by the fewest entries first.
Ties prefer the longest leftmost entry, then the longest next entry, until
they differ. Exact spelling provides the final ordinal tie break. This one
ranking also governs a single exact entry, so no separate longest-match rule
can conflict with it. The evidence records each selected entry and the oracle
receipt. The same request therefore has one result independent of collection
iteration order.

An oracle entry may not consume punctuation, symbols, whitespace, malformed
UTF-16, or text outside its exact source spelling.

### 3. Apply conventional case boundaries

Unprotected letter runs use Unicode upper-, title-, and lower-case categories
with these conventional boundaries:

- a lower-to-upper transition begins a new word;
- an uppercase run followed by an uppercase-plus-lowercase word ends before
  that final uppercase character; and
- a single uppercase letter followed by lowercase letters forms one word.

Examples include `jsonContext` as `json` + `Context`, `JsonContext` as
`Json` + `Context`, and `XMLDocument` as `XML` + `Document`.

A lower/title-case word is admitted by case evidence and does not need to be
present in the oracle. A single uppercase letter at a conventional boundary
is also admitted. This preserves domain vocabulary such as `Validator`,
`Unmarshaller`, and `Paginator`.

An uppercase run of two or more characters is admitted only when the oracle
can segment the complete run into exact atoms or compounds. Otherwise it is
one unresolved span. `CFURL` therefore remains unresolved under an oracle
that cannot completely segment it; the breaker does not guess `CF` + `URL`.

### 4. Classify decimal runs

An exact protected oracle compound owns its digits. Otherwise:

- a final canonical decimal run whose complete identifier prefix appears in
  the supplied numbered-family context is an `Ordinal`;
- a final decimal run immediately following one admitted word in the same
  separator-delimited run remains part of that word, with
  digit-compound-fallback evidence; and
- any other decimal run is unresolved.

The fallback keeps `Adler32` together when no local family evidence justifies
`Adler` + ordinal `32`. It does not attach across `_`, punctuation, or other
separators. Multiple letter/digit transitions such as `P320t1`, and digit
runs followed by more letters such as `Log4Net`, retain unresolved evidence
unless exact oracle entries cover them.

For a recognized numbered family, `DelegateInvoker1` yields `Delegate`,
`Invoker`, and ordinal `1`. The ordinal is not a word. This prevents a
downstream suffix analysis from inventing a `*1` role family while retaining
the exact numbered-family evidence.

## Rule evidence

Rule evidence is a closed owner-issued result, not a confidence score. The
first contract distinguishes at least:

- exact oracle atom;
- exact oracle compound;
- oracle segmentation of an uppercase run;
- ordinary lower/title-case word;
- single-uppercase word;
- digit-compound fallback;
- numbered-family ordinal;
- separator category; and
- unresolved reason.

Unresolved reasons distinguish unknown uppercase run, unsupported
letter/digit shape, unsupported Unicode category, leading combining mark,
control or format text, and malformed UTF-16.

Rule-kind names are CSharpText vocabulary. Consumers carry them unchanged and
must not copy the closed set into another owner as a synchronized enum.

Span coalescing is deterministic. Adjacent separators with the same separator
rule kind form one maximal span. Adjacent unresolved code units with the same
unresolved reason form one maximal span. A rule-kind or reason boundary starts
a new span; words and ordinals never coalesce across their grammatical
boundaries.

The result makes no statistical accuracy claim and emits no aggregate
confidence. Exact admitted, ordinal, separator, and unresolved counts are
available to a population owner by counting the typed spans.

## Failure semantics

Construction rejects an oracle with an empty version, duplicate exact entry,
empty entry, entry containing a separator or malformed UTF-16, or a digest
that does not match its ordered contents.

Numbered-family context construction rejects an empty version, fewer than
three distinct canonical positive suffixes for a declared prefix, or duplicate
entries. The break request rejects a context grammar version different from
the bound oracle.

Those are typed construction failures. They are never converted to an empty
oracle, ignored context, or a successful all-unresolved result.

Breaking arbitrary input under valid immutable inputs does not fail. Text the
grammar cannot classify remains unresolved.

## Consumer handoff

The Library name-family summary consumes:

- the exact grammar and oracle receipt;
- the exact gap-free spans and rule evidence; and
- explicit ordinal spans.

Research does not tokenize names or rebuild numbered-family evidence.

The current #8698 design candidate names only word, separator, and unresolved
spans. Before implementation it must add the owner-issued ordinal
classification and define suffix-family eligibility over the last admitted
word before any trailing ordinal. That is a consumer-contract adjustment, not
part of this CSharpText owner.

Changing the grammar, oracle entry set, or numbered-family rule changes the
receipt consumed by a population methodology. Results produced under
different receipts are not silently compared as the same methodology.

## Required evidence

`CSharpText.Tests` is the Release contract gate. It must cover:

- ordinary PascalCase and camelCase words;
- acronym-to-word transitions and exact oracle segmentation;
- recognized and unknown uppercase runs;
- exact atom, compound, casing, complete-run, and ambiguous-segmentation
  behavior, including `CFURL` with only `URL` recognized and `Validator` with
  only `Valid` recognized;
- a conflicting-choice oracle where the minimum-entry segmentation does not
  begin with the longest available entry;
- `HMACSHA256`, `RSAPKCS1`, `IPv6`, and `UInt16` under the product oracle;
- `DelegateInvoker1..10` and `LookupType3/4/5/8` with and without matching
  numbered-family context;
- close digit controls including `Adler32`, `Secp256r1`, `P320t1`,
  `Log4Net`, and a digit separated by `_`;
- punctuation, symbols, whitespace controls including tab and CR/LF,
  non-whitespace controls, format characters, combining marks, non-ASCII case
  boundaries, and malformed UTF-16;
- exact source coverage, ordering, deterministic coalescing and rule evidence,
  and receipt preservation; and
- every typed oracle and numbered-context construction rejection.

The checked-in product-oracle generator has a separate deterministic fixture
gate. Given the pinned `Microsoft.NETCore.App.Ref` 10.0.10 input and reviewed
mixed-case set, it must reproduce the checked-in ordered entries and digest
byte for byte. That gate proves snapshot provenance; it does not claim that
runtime naming is natural-language truth.

`eng/generate-csharp-identifier-word-oracle.cs` is the generator;
`eng/csharp-identifier-word-oracle-reviewed.txt` is its reviewed input; and
`eng/csharp-identifier-word-oracle.snapshot` is the exact gated output.

The FluentValidation 12.1.1 production-consumer gate belongs to #8698. It
proves that this textual result is composed into the expected `Validator`
population through product code rather than through a test-only tokenizer.

## Production adoption

This focused substrate participates in the seven-step #8698 production plan:

1. land this CSharpText grammar, product oracle, and Release gate;
2. consume the existing Metadata Type-definition inventory;
3. consume typed source provenance from #8643;
4. issue the Research Library name-family document;
5. expose the document through QuerySpace, CLI Markout/JSON, and
   `InspectionEnvelope<TContent>`;
6. call the same managed query from Browser/Wasm with no TypeScript
   tokenization; and
7. compose role evidence with exact-identity Graph shape and Library Metrics
   amplitude in the project-analysis workflow.

The production consumer retires ad hoc agent, CLI, and browser tokenization.
This design does not add a standalone command or rendering shape.

## Non-claims

This owner does not:

- recognize natural language or promise 100% segmentation accuracy;
- infer Type roles, singularize words, merge synonyms, or strip a leading
  interface `I`;
- own metadata names, generic arity, namespaces, nesting, or Type identity;
- decide which identifiers form a Library or corpus population;
- classify source as authored or generated;
- acquire reference packs or inspect assemblies at runtime;
- infer numbered families from ambient process state;
- issue suffix families, corpus distinctiveness, graph groups, or
  architecture judgments; or
- parse C# syntax.

Unknown domain vocabulary remains useful evidence. Ordinary case-derived
words stay available, while opaque runs that need semantic judgment remain
exact and unresolved for a later consumer.
