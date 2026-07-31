# InertText

A shared component for making untrusted text inert before it reaches a sink that
can act on it.

## The problem

Text that comes out of an artifact — a package id, a type name, a file path, an
assembly reference — is attacker-controlled. Printing it hands that control to
whatever renders it. The attacks do not need malformed input; every payload in
this component's test corpus is well-formed Unicode that a naive sink prints
without complaint:

- **Reordering.** `Cf` scalars drive the bidi algorithm. Trojan Source
  (CVE-2021-42574) uses them to make source render differently from how it
  compiles, and a right-to-left override in a name reorders the rest of the line.
  `invoice\u202Egpj.exe` renders as `invoice.jpg`.
- **Terminal control.** `Cc` scalars introduce escape sequences that clear the
  screen, rewrite the window title, or make text a hyperlink to somewhere else.
- **Line forgery.** A line terminator inside a value produces a second log line
  that looks like the tool emitted it. `CR` alone rewinds so the truth is
  overwritten. `NEL`, `LS` and `PS` are terminators that a `CR`/`LF` check misses.
- **Invisibility.** Zero-width scalars, soft hyphens and word joiners split a
  name for the eye while leaving it one token to a comparison. Tag characters
  carry an invisible ASCII payload, which is how instructions get hidden in text
  an agent will read.

Percent-encoding, `Uri` normalization and HTML escaping each stop some of this
and none of it. `Uri` percent-encodes `Cc` and passes `Cf` straight through.

## What it does

Refused scalars are rewritten in a visible spelling. The term and the contract
come from BSD `vis(3)`: the output is **inert**, **lossless** and **invertible**.

```text
invoice\u202Egpj.exe   ->   invoice\u202Egpj.exe    (the override, spelled out)
package<LF>Error: x    ->   package\^JError: x
```

This is not neutralization, which has none of the three properties. Dropping the
hostile scalars would also produce safe output, but it destroys the evidence —
and the reader is usually trying to work out what the artifact actually says.

## The predicate and the speller are separate

This is the central design split, and it is what lets one speller serve every
sink:

- The **predicate** (`ScalarPolicy`) answers "is this scalar permitted *here*". It
  is per-sink and it is the caller's to choose. `TextPolicy.Field` is deny-shaped
  and refuses `Cc`, `Cf`, `Cs`, `Zl` and `Zp`. `TextPolicy.Prose` is the same
  minus a `CR`/`LF`/`TAB` exemption, for genuinely multi-line text.
- The **speller** (`VisualEncoder`) answers "how is a refused scalar written
  down". It is total over Unicode, and it never learns *why* a scalar was
  refused.

A speller with a built-in hazard set has absorbed policy and cannot serve a sink
whose grammar is constrained. That case is not hypothetical: the central
typosquatting vector is a homoglyph, and Cyrillic `а` and Latin `a` are the same
glyph, both category `Ll`. No category rule catches that and none should —
refusing every non-Latin letter would break most of the world's text. Only an
allow-shaped policy catches it, and because the speller is total, such a sink
needs no spelling table of its own.

The speller's obligations:

- **Total.** Defined on every Unicode scalar, including the 127 encoded scalars
  above the BMP. A `\uXXXX`-only speller is neither total nor invertible on
  exactly the inputs an attacker reaches for.
- **Injective, in both directions.** One scalar has one spelling, so the decoder
  refuses spellings the encoder never emits: `\U` for a BMP scalar, and `\uXXXX`
  for a scalar with a canonical short form such as `\\`, `\^X` or `\^?`.
- **Scalar-based, not code-unit-based.** An unpaired surrogate is not a scalar.
  `Rune` cannot hold one and `EnumerateRunes` substitutes `U+FFFD`, so a speller
  built on either is lossy on the one input that cannot be written any other way.
- **Able to report what it emitted**, so a caller can print a legend without
  keeping a second copy of the spelling table.
- **Not responsible for structural escaping.** A `|` still breaks a Markdown
  cell and a `"` still ends a JSON string. Neither is in an encoded category, and
  neither should be — escaping for a grammar is the serializer's job. Visual
  encoding and structural escaping compose; neither substitutes for the other.

## Treated text is carried as a type

Encoding on its own is transactional: a `string` goes in and a `string` comes
out, so a treated value and an untreated one have the same type. Nothing stops a
later edit from interpolating the untreated one, and nothing marks the difference
at the sink. Auditing that means tracing every path that reaches a printer, again
after every change.

`InertString` is the currency form. It can only be built by applying a policy —
there is no conversion *from* `string`, and a test asserts that no public entry
point takes text without also taking a policy. A sink that accepts only this type
cannot be handed raw text by accident, which answers **what a sink accepts** with
a type search rather than a call-graph trace.

Conversion *to* `string` is unrestricted, which is safe here in a way it usually
is not. The customary objection to a wrapper is that `ToString` launders it, but
that assumes the payload is dangerous and the wrapper is what holds it back. Here
the payload is already inert. Losing the wrapper loses provenance, not
protection.

## Holding a value is not the same as being able to reverse it

A type answers what a *sink* accepts. It does not answer what a *file* can do,
and with the decoder as a member of the value type it cannot: every file holding
a value would be one member access away from the original, so a search for the
type name finds mentions rather than capabilities.

So the decoder lives in its own namespace:

| Namespace | Contains | A file naming it can |
| --- | --- | --- |
| `InertText` | `InertString`, `ScalarPolicy`, `TextPolicy`, `VisualForm` | build, compose, compare and print inert text |
| `InertText.Encoder` | `VisualEncoder` | additionally recover the original text |

Text enters through `InertString`'s constructor and leaves through `ToString`
already spelled, so the currency namespace is sufficient for every ordinary use.

**The audit is one search, and the string is `InertText.Encoder`.** Not
`using InertText.Encoder` — a using directive is only one of the two ways to
reach a namespace, and the other one leaves the import block untouched:

```csharp
using InertText;                    // the only directive in the file

InertText.Encoder.VisualEncoder.TryDecode(inert.ToString(), out string? original);
```

That compiles and recovers the original. A reviewer who greps for the directive
sees a clean import list and concludes the file cannot decode, which is the
opposite of the truth. Greping the bare namespace catches both forms, because a
fully-qualified call has to spell the namespace too — there is no third way in.

So: a file that does not mention `InertText.Encoder` at all has no path back to
the original of any value it handles.

A reflection test enumerates every public member of the `InertText` namespace
that returns text and accounts for each one: `ToString` (the encoded form),
`DescribeLegend` (fixed strings naming spellings) and `ScalarViolation.ToString`
(an index, a code point and a category — which is why a violation reports an
`int` rather than the character, so a survey can name what it refused without
echoing it). Adding a decode convenience to the currency type fails that test.

**This is an audit boundary, not a capability barrier.** A file can name the
namespace either way, and nothing should stop it — decoding is a legitimate
operation with legitimate callers. What the boundary buys is that the reversing
half cannot arrive unnoticed *by a reviewer who searches correctly*.

## Composition

Composition has to work, or callers fall back to `$"...{treated}..."` and drop
the guarantee at the moment it matters most. `InertString.Format` is an
interpolated string handler that encodes each part as it is appended — holes
because they are untrusted, and literals too, because an invariant with an
exception in it has to be re-argued at every use.

An already-inert hole is not encoded twice, but neither is it trusted. The type
records that *a* policy was applied, not *which* one, and a value built for one
sink is routinely spliced into a message bound for another. `Prose` permits the
line feed that `Field` exists to remove, so splicing a `Prose` value into a
`Field` message unexamined would put a raw newline into a single-line record and
report no encoded forms for it — log injection, with the type appearing to vouch
for it.

Splices are therefore re-checked against the policy in force and re-spelled where
they do not satisfy it, in `Format` and `Join` alike. That repair is the second
thing invertibility buys, and it is worth noticing that the requirement is
unsatisfiable without a decoder: a mismatched part can only be repaired if the
original can be recovered exactly.

The repair only ever **tightens**. A value encoded under a stricter policy keeps
its spellings when spliced into a laxer sink, because composition making a value
*less* inert would let a caller launder one by quoting it somewhere permissive.
The cost is that the splice path is observable — the same source text can render
differently depending on where it was encoded — which is a deliberate trade.

## Testing

Two shapes, because they fail differently.

**Property tests** sweep every scalar for invertibility and injectivity. They
prove the transform is total, and a regression reports that some scalar broke.

**A corpus of named attacks** records what the properties are *for*. A regression
there reports that Trojan Source works again, which is a sentence someone can
act on. Each fixture records what it attacks, and six claims are asserted over
every one of them, so adding a fixture is a single line that is immediately
subject to all of them.

The corpus also carries a payload nothing catches — the Cyrillic homoglyph — with
both halves asserted: the category policy permits it, an allow-shaped policy
refuses it. A corpus containing only what the default catches would quietly imply
the default is sufficient.

## Placement

`InertText` sits below every other project and references nothing. Every assembly
that prints artifact-derived text needs it, including the dependency-free leaves,
so it has to sit below all of them. A reference added to it is a reference added
to everything.
