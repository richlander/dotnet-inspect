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

## What we are protecting

Three channels, kept separate because they have different blast radii and a
mitigation for one is not automatically a mitigation for another.

- **The terminal.** The only channel where foreign text reaches the machine
  rather than the reader. `OSC 52` writes the user's clipboard, `CSI` and `DEC`
  sequences repaint the screen and can forge a shell prompt, and terminals have
  shipped answerback paths that reach command execution (iTerm2,
  CVE-2019-9535). This is what makes containment a safety requirement rather
  than a formatting preference.
- **The reader.** Bidi and homoglyph attacks never touch the machine. They make
  output mean something other than what it displays, so the harm lands on
  whoever acts on it — a reordered assembly name in a dependency listing is a
  supply-chain decision made on false information.
- **An agent's context.** This tool ships a skill, so its output is deliberately
  fed to language models. Tag characters carry an invisible ASCII payload and no
  terminal is involved at any point, so a sink that is safe for the first two
  channels can be wide open on this one.

Deliberately out of scope:

- **Structural escaping.** Markdown pipes, JSON quotes and CSV delimiters
  restructure a document rather than attack a renderer. Different mechanism,
  different inverse; it composes with this one rather than being replaced by it.
- **Text used for identity or control flow.** A name being compared, parsed or
  matched is not being presented, and encoding it changes the answer.
- **Text that must stay byte-exact to function.** File paths, request URLs and
  assembly names are consumed by APIs, not read by people.

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

- The **policy** (`TextPolicy`) answers "is this scalar permitted *here*". It is
  per-sink, and it is a **closed enum** rather than a predicate the caller
  supplies. `Field` is deny-shaped and refuses `Cc`, `Cf`, `Cs`, `Zl` and `Zp`.
  `Prose` is the same minus a `CR`/`LF`/`TAB` exemption, for genuinely
  multi-line text. The rule table behind it is an internal `ScalarPolicy`
  delegate, so no caller code runs during encoding or repair.
- The **speller** (`VisualEncoder`) answers "how is a refused scalar written
  down". It is total over Unicode, and it never learns *why* a scalar was
  refused.

### Why the policy set is closed

An open predicate is the more obvious design and it was the first one here. Two
things ruled it out, both measured rather than argued.

**Drift.** Before this component the tree had grown five independent hand-rolled
hazard predicates, and they disagree by up to 48 BMP characters:
`IsRenderingHazard` refuses 76, `NeedsEscape` 76, `MetadataTableProjector`'s
control check 65, `Field` 110, `Prose` 107. That is not hypothetical drift — it
had already shipped a defect, since the metadata layer escapes `Cc` and `C1`
while emitting `U+202E`, `U+200D`, `U+FEFF` and `U+2028` verbatim. A per-caller
predicate makes each new sink a fresh opportunity to disagree, and the whole
point of a shared component is that the rules are written once.

**Disclosure.** `EnsurePermitted` has to decode before it can re-spell. A
caller-supplied predicate would then be handed the *decoded, hostile* original
one scalar at a time, in a file that need never name the capability namespace —
the audit boundary walked back out through a callback, invisible to any
reflection test over return types because the leak is an argument rather than a
result.

A closed set answers both: the rules live in one table, and a repair runs no
caller code. Adding a kind of text is an enum member and an arm in that table,
with no new public member anywhere, so the set stays extensible without the
type growing.

Allow-shaped rules are deliberately not expressible, and encoding could not
serve one anyway. Repairing `Nеwtonsoft.Json` to `N\u0435wtonsoft.Json` fails
the same letters-only check that rejected the original: what such a sink wants
is *rejection*, which is the threat model's "reject, do not sanitize" and a
different operation from this one.

The speller's obligations:

- **Total.** Defined on every Unicode scalar, including the 127 encoded scalars
  above the BMP. A `\uXXXX`-only speller is neither total nor invertible on
  exactly the inputs an attacker reaches for.
- **Injective, in both directions.** One scalar has one *emitted* spelling, so
  the decoder refuses spellings the encoder never produces: `\U` for a BMP
  scalar, `\uXXXX` for a scalar with a canonical short form such as `\\`, `\^X`
  or `\^?`, lowercase hex in either width, and a raw unpaired surrogate in the
  decoder's input. The one spelling accepted but never emitted is a surrogate
  *pair* written as two `\uXXXX` escapes, because composition produces it: a
  .NET string is UTF-16, so `"\uD83D" + "\uDE00"` *is* `"\U0001F600"`, and
  `Join` encodes its fragments separately.
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

## Where text becomes inert

The tempting answer is "as early as possible": have every API that returns
foreign text return `InertString`, so no caller can hold raw text and every
consumer is forced to participate. That is the wrong answer here, for three
reasons.

**`InertString` marks the wrong end of the pipeline.** It means *this text has
been treated for a sink*, which is an output marker. Asking metadata APIs to
return it is asking for an input marker — *this text came from outside*. Those
are different propositions, and only the second one is a property of a source.

**A policy belongs to the sink, and acquisition does not know the sink.**
`Encode` takes a `TextPolicy` because what may pass depends on where the text
is going: `Field` for a table cell, `Prose` for a paragraph, something else for
a sink with no terminal at the end of it. Where a name is read out of metadata
there is no sink yet, so the API would have to pick a policy arbitrarily and be
wrong everywhere that wanted a different one. Encoding again later does not
recover it, because encoding under two policies is not encoding under a union of
them.

**Encoding at acquisition corrupts identity, and does it invisibly.** A literal
backslash is always rewritten whatever the policy says, as is any scalar the
policy refuses. 318 sites in `ILInspector.Metadata` compare foreign names for
control flow rather than display — `member.Name == ".ctor"`,
`prop.Name.StartsWith("/_")`, the pairing loop in the diff analyzer. Against
encoded text those comparisons answer differently, and they do so *only for the
inputs that needed encoding*. Benign text encodes to itself, so every test still
passes and the behaviour changes exactly on unusual or hostile input. That is
the worst available failure distribution: invisible in CI, live in the field.

So containment goes late — but not "as late as possible", which is just the
per-site approach with better manners. It goes at **the last structural boundary
every rendered value must cross**.

### Why a structural boundary and not a rule

A boundary is worth something only if it cannot be walked around, so the claim
has to be measured rather than asserted. The table path holds up: of 113 direct
`Console.Write` calls in the tree only 24 interpolate anything, and all 24 are
in a cache command, an analysis dev app and a decompiler fixture. None are in
`src/dotnet-inspect/Output/`, which writes content the serializer has already
rendered. Foreign text cannot reach stdout as a table cell without passing
through a row property.

That is the difference from containing at each use site. A structural boundary
is crossed whether or not anyone remembered it; a per-site rule is a memory
obligation, which is why the earlier approach was forgotten across 359 columns.

Late containment also buys three things early containment cannot:

- comparisons upstream operate on true text, so identity stays correct;
- the sink is known, so the policy is known;
- volume is bounded by what is rendered rather than everything read — one
  framework assembly exposes 144,854 metadata name slots, while a user asking
  for one type's members renders a handful.

### The boundaries are not yet fully enumerated

Two are known, and neither is complete on its own:

| Boundary | Measured size | Tracked by |
| -------- | ------------- | ---------- |
| Row and view types | 359 columns across 86 row types | #3463 |
| Diagnostic and log callbacks | 97 `Action<string>` sites | #3606 |

Those cover the table path and the logging path. They are not exhaustive: the
tree also has 46 `Console.Error` calls, 93 `TextWriter` references and 8
`File.WriteAllText` calls, and no one has yet shown which of those carry foreign
text. Establishing that list is a prerequisite for the guarantee, not a detail
of it — a chokepoint you have not finished enumerating is a chokepoint you
cannot claim.

## Carrying a contained value to the sink

Containing text at the boundary answers *whether* a value was treated. It does
not, on its own, get the value to the printer intact. The metadata table
projection is the worked example, and it started out doing this:

```csharp
internal static string ContainCellText(string value, int maxChars, out bool truncated)
{
    var contained = new InertString(TextPolicy.Field, value, maxChars);
    truncated = contained.IsTruncated;
    return contained.ToString();
}
```

That takes an `InertString` apart one line after building it, and it is worth
being precise about the cost, because "it still returns treated text" is true and
is not the point.

**The type stops guarding the moment it is unwrapped.** A `string` field cannot
say whether it was treated, so every later edit that assigns to it is
unconstrained, and the only way to audit the field is to re-trace its producers.
Carrying `InertString` instead makes that a compiler question: there is no
conversion into the type that does not apply a policy, so a field of that type
has no untreated inhabitants.

**Splitting the pair invites the halves to disagree.** Truncation is not
decoration on the text, it is the difference between a prefix and a whole value,
and a sink that loses it renders a clipped name as though it were complete. Two
fields can drift; one value cannot. `HandleRef` carried a `Display` and a
`DisplayTruncated` that could be set independently — and a test constructed
`Display: "", DisplayTruncated: true`, a state the projector cannot produce.
Once `Display` is an `InertString`, that fixture has to be written as bounding
real text to a real budget, because a value that was never cut cannot claim it
was.

So the rule is: **contain once, carry the contained value, and un-pair only at
the sink.** The producer names the policy and nothing else; the sink decides what
a partial value looks like, because notation is the sink's business and no
producer should be guessing at an ellipsis.

```csharp
internal static InertString ContainCellText(string value, int maxChars)
    => new(TextPolicy.Field, value, maxChars);
```

```csharp
InertString text = ContainCellText(raw, options.MaxStringChars);
return new MetadataValue.HeapReference(
    HeapKind.String, HeapOffset(handle), raw.Length, text, text, text.IsTruncated);
```

One consequence is worth stating, because it looks like an over-share until the
alternative is written down. `ContainCellText` is `internal`, and the renderer's
own test assembly reaches it through `InternalsVisibleTo` rather than
constructing `new InertString(TextPolicy.Field, …)` in fixture code. A fixture
spelled the second way pins the policy that was current when it was written, so
changing `TextPolicy.Field` would leave every such fixture asserting the old
policy while the product moved — the tests would stay green and mean less. The
factory is the single place the policy is chosen, so tests should be made to go
through it. Widening `InternalsVisibleTo` to the one assembly that needs it is a
smaller surface than making the factory public.

### A partiality flag survives only where the text cannot know

The tempting next step is to delete every truncation field and read
`IsTruncated` off the text. That is right wherever the character budget is the
only way a value can become partial, which is why `HandleRef.DisplayTruncated`
and `MetadataImageOverview.MetadataVersionTruncated` are gone.

It is wrong for `HeapReference`. A Blob preview is bounded by a **byte** budget,
upstream of any text at all:

```csharp
int take = Math.Min(length, Math.Max(0, options.MaxPreviewBytes));
byte[] bytes = blobReader.ReadBytes(take);
```

The hex that follows is a *complete* spelling of the bytes that were read, so its
`IsTruncated` is `false` for a blob that lost most of its content. Deriving the
flag there would report those blobs as whole. `HeapReference.Truncated` therefore
stays a stored field meaning "this projected value is not the whole value", with
two causes feeding it and only one of them visible in the text.

The general form: a length or a flag is only meaningful in the units that
produced it. `InertString` refuses to remember a source *length* for the same
reason — re-spelling under a stricter policy makes text longer, so a length
carried across a re-encode is compared against a different unit.

### What this buys, and what still carries `string`

Every field of the metadata projection that can hold artifact-derived text is
now `InertString`: `HeapReference.Text` and `.Preview`, `HandleRef.Display`,
`MetadataImageOverview.MetadataVersion`, and `Malformed.Detail`. The last one is
included because it splices a third party's exception message into a diagnostic
about an image that has already proved malformed; whether SRM ever puts artifact
bytes into a message is not a property this repository can pin, and typing the
field means it does not have to.

`Scalar.Display` and `Flags.Decoded` remain `string` on purpose. They hold
formatted numbers and BCL enum names — `0x{rva:X8}`, `TypeCode.Boolean` — and
never heap content, so containing them would add ceremony without removing a
hazard.

The gate for "no untreated metadata text reaches a sink" is therefore the
compiler: there is no conversion that would let a `string` into any of those
fields. That is a stronger enforcement than a test, and it is the reason to
prefer carrying the type over re-checking the text.

## Holding a value is not the same as being able to reverse it

A type answers what a *sink* accepts. It does not answer what a *file* can do,
and with the decoder as a member of the value type it cannot: every file holding
a value would be one member access away from the original, so a search for the
type name finds mentions rather than capabilities.

So the decoder lives in its own namespace:

| Namespace | Contains | A file naming it can |
| --- | --- | --- |
| `InertText` | `InertString`, `TextPolicy`, `VisualForm` | build, compose, compare and print inert text |
| `InertText.Encoding` | `VisualEncoder` | additionally recover the original text |

Text enters through `InertString`'s constructor and leaves through `ToString`
already spelled, so the currency namespace is sufficient for every ordinary use.

**The audit is one search, and the string is `InertText.Encoding`.** Reaching the
decoder means naming its namespace, and that act *is* the opt-in. Decoding is a
legitimate operation with legitimate callers, so the design does not try to
prevent it — only to make it impossible to do quietly.

A namespace can be named in two places, and both are equally legitimate:

```csharp
using InertText.Encoding;            // named in the import block
VisualEncoder.TryDecode(inert.ToString(), out string? original);
```

```csharp
// no using directive of any kind in this file. InertText.Encoding here is the
// namespace; there is no InertText type and no Encoder member on InertString.
InertText.Encoding.VisualEncoder.TryDecode(inert.ToString(), out string? original);
```

Neither is evasion. The second names the namespace at the call site rather than
at the top of the file, in plain sight on the line that uses it. What matters is
that the *audit* covers both, which is why the search is for the bare namespace
and not for `using InertText.Encoding`: a fully-qualified call declares itself on
its own line and needs no directive at all, so a reviewer grepping only the
import block would read that file as clean and conclude it cannot decode.

There is a third way, and unlike those two it does not name the namespace in the
file that decodes at all:

```csharp
// one file, or a <Using Include="InertText.Encoding" /> item in the .csproj
global using InertText.Encoding;
```

Every other file in that project can then call `VisualEncoder.TryDecode` with no
local mention of the namespace. The search still finds the import — it is still
text in the repository — but it stops answering *which files* can decode and
starts answering *which projects* can, and the file it points at is not the file
doing the decoding.

That granularity is therefore an invariant of the build rather than of the
language, so it is gated by a test
(`NoProjectImportsTheCapabilityNamespaceForEveryFileAtOnce`) that fails on a
`global using` or a `<Using>` item naming the capability namespace. Nothing needs
one: production does not name the namespace at all, and the tests that
legitimately decode use ordinary per-file directives.

So, with that gate in place: a file that does not mention `InertText.Encoding` at
all has no path back to the original of any value it handles.

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

### Asking whether a value suits your sink

That repair is exposed as `EnsurePermitted(TextPolicy)`, because a sink that
accepts an `InertString` has no other correct way to make one safe for itself.
The obvious substitute is wrong: `new InertString(policy, value.ToString())`
hands already-encoded text back to the encoder, so the backslashes double on
every pass — `a\u202Eb` becomes `a\\u202Eb`, then `a\\\\u202Eb`. Because
`EnsurePermitted` decodes before re-spelling, repeating it is a no-op.

What a sink must **not** use for this is `WasEncoded` or `Forms`. Those report
what was *done* to a value, never what it *satisfies*, and as a conformance
check they are wrong in both directions:

| Value | `WasEncoded` | Satisfies `Field` |
| ----- | ------------ | ----------------- |
| `Prose`-encoded `"line1\nline2"` | `false` | **no** — it carries a raw line feed |
| `Field`-encoded `"a\u202Eb"` | `true` | yes, and `Prose` too |

Encoding makes a value *more* conformant, so the flag is close to
anti-correlated with the property a sink cares about. The underlying reason is
that conformance is a relation between a value and a policy rather than a
property of the value, which is why it cannot be cached on one and why
`EnsurePermitted` has to recompute it.

Storing the producing policy on the value would not help either. Knowing which
policy produced a value says nothing about whether it is stricter than the one
you are about to apply, short of sweeping every scalar to compare their
permitted sets — so the value would carry an extra field and still pay for the
scan.

## Bounding a value to a budget

A sink that puts treated text in a column or a record field has a length limit,
and applying it to the encoded text is not a substring operation. A spelling is
between two and ten characters wide, so an arbitrary cut can land inside one and
leave `\u2` behind.

That is not merely ugly, it is unsound. `EnsurePermitted` treats text it cannot
decode as raw and re-encodes it, so the surviving backslash doubles and `a\u2`
becomes `a\\u2` — which is exactly what the untreated literal `a\u2` encodes to.
Two unrelated inputs converge on one output, and the injectivity the repair path
rests on is gone. The budget is enforced on the *encoded* length for the same
reason it is in the escaper this replaces: encoding expands, so bounding the
input bounds the wrong number.

Two members divide a value, and both are best-effort:

| Member | Takes |
| ------ | ----- |
| `Truncate(int maxLength)` | the longest prefix within the budget |
| `Truncate(Range)` | the largest whole window inside the range |

Both bounds move **inward** — the start forward to the next whole spelling, the
end back to the previous one — so the result is always a subset of what was
asked for. That asymmetry is the whole of the safety argument: returning less
than a caller asked for is something it can detect from `Length`, while
returning more is not. A window holding no whole spelling is therefore empty
rather than widened to the spelling enclosing it.

Every range is answerable, including a reversed one and one that runs off
either end. Neither member reports truncation separately, because
`result.Length` against the width that was asked for already says whether
anything moved.

There is deliberately **no exact counterpart** that refuses an unusable window.
An indexer would wear the syntax of ordinary slicing while throwing on bounds
that look perfectly reasonable — six of the twelve positions in an
eleven-character value divide a spelling — so the shape invites exactly the call
it refuses. It would also buy nothing over the length comparison above.

A surrogate pair counts as one thing however it is spelled — raw, as `\U`, or as
the two `\uXXXX` escapes that composition produces when the halves are encoded in
separate fragments — so a boundary can never leave a lone surrogate behind.
`Forms` is recomputed from what is kept, so a legend drawn from a bounded value
cannot name a spelling that went with the tail.

The walker that finds these boundaries lives beside the speller rather than on
the currency type, because its widths are the speller's output read backwards; a
new spelling added to one and not the other would make every boundary past it
wrong. It is internal, and it adds nothing to the capability surface: it reports
where text can be divided, never what any of it decodes to.

## Testing

Two shapes, because they fail differently.

**Property tests** sweep every scalar for invertibility and injectivity. They
prove the transform is total, and a regression reports that some scalar broke.

**A corpus of named attacks** records what the properties are *for*. A regression
there reports that Trojan Source works again, which is a sentence someone can
act on. Each fixture records what it attacks, and six claims are asserted over
every one of them, so adding a fixture is a single line that is immediately
subject to all of them.

The corpus also carries a payload nothing catches — the Cyrillic homoglyph —
and asserts the boundary directly: *no* `TextPolicy` refuses it, swept over
`Enum.GetValues<TextPolicy>()` so a policy added later is covered without an
edit. Category rules cannot catch it and none should, since refusing every
non-Latin letter would break most of the world's text. A corpus containing only
what the policies catch would quietly imply they are sufficient.

## Placement

`InertText` sits below every other project and references nothing. Every assembly
that prints artifact-derived text needs it, including the dependency-free leaves,
so it has to sit below all of them. A reference added to it is a reference added
to everything.
