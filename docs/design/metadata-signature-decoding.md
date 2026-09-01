# Bounded Metadata signature decoding

> **Map:** [Structured type-forwarding resolution](type-forwarding-resolution.md)
> owns the cross-assembly operation that may consume requests produced by this
> single-signature decode. This document owns the decode's independent
> work-bounding contract.

## Status

Design-only and unverified until the named gates in this document land. The
operation and work-budget types named below are planned owner-issued surfaces,
not current product APIs.

## Contract

A signature decode walks artifact-authored metadata on behalf of a caller who
has not inspected it. The decode must therefore complete within a stated bound
or refuse, and it must never report success after doing unbounded work.

This section specifies that bound: the quantities a decode consumes, how each
is bounded, and what a gate must do to enforce it.

### Owner

The planned signature decode inside `ILInspector.Metadata` owns this contract:
`SignatureOccurrenceProvider` and the work budget it charges. This document is
the owning document for that future surface.

This contract does not govern acquisition, binding policy, forwarding
semantics, the evidence model, or anything outside a single signature decode.

### What goes wrong without a bound

A decode reads metadata the caller did not write. The artifact arrives from a
package feed, and every length, count, and name in it is a number its author
chose. The decode's job is to turn that into a typed plan; the caller's
expectation is that reading one member signature costs about what one member
signature is worth.

Four things break that, and they fail in different directions.

**No bound at all.** Work becomes proportional to author-chosen numbers, so the
ratio of effort to artifact size has no limit. The probe below builds a method
with no parameters whose one type reference is scoped to an assembly reference
carrying a 16 MiB public key: decoding that one signature copies 16 MiB. Work
is driven by a number in the artifact rather than by the size of the question
asked, and a caller decoding many members repeats it per member.
The decode then *succeeds*, which is the worst part -- nothing is reported,
and the cost surfaces only as a tool that has stopped responding.

**A bound that is too low.** Legitimate signatures are refused, and refusal is
attributed to the artifact: the tool reports a rejected signature for code that
is entirely well-formed. A slow tool is a complaint; a tool that calls valid
input malformed is wrong, and wrong about someone else's work.

**A bound that is too high.** The bound exists, passes review, and never binds,
so the first failure returns unchanged. Nothing in the code distinguishes this
from a good ceiling. Only a census does, by showing the distance between the
ceiling and what real artifacts consume.

**A bound on the wrong quantity.** This is the one that survives review, because
the budget is visibly present and is charged on every path. A budget that counts
callbacks is fully satisfied while cost grows without limit, since it cannot
observe that one callback read a megabyte. The code reads as bounded and is not.

The ceilings therefore need two justifications, and each answers a different
failure. They must sit far enough above real artifacts that no legitimate
signature is refused, which the census establishes: the largest real decode
consumed 1,182 ledger units against a ceiling of 262,144. And they must bind on
the quantity that actually grows, which is what the classification below is
for.

### The two classes of cost

Throughout this section, **materializing** means copying artifact bytes into a
managed object -- a string, a byte array, or a typed identity built from them.
The cost is proportional to the size of what is copied. Reading how large
something is, without copying it, is not materializing and costs nothing
comparable.

Every quantity a decode consumes falls into exactly one class. The classes
divide on a single question -- **who fixes the number** -- because that is the
question the threat model turns on. The obligation follows from the answer.

**Class A -- tool-capped.** A constant chosen by this code caps what a *single*
materialization can cost, and a gate enforces that cap before the
materialization happens. The artifact cannot raise it, so the worst case is
known without asking the artifact anything.

**Class B -- author-sized.** A number in the artifact fixes the size, and
nothing caps it. Nothing about one occurrence is known until the artifact is
asked.

A cap is tool-capped only if its value does not come from the artifact. A bound
derived from artifact content -- scaling a limit by a declared length, a member
count, or a table size -- is author-sized wearing a cap, and belongs in Class B
however it is spelled.

Where the constant is written does not matter: a `const` field, a parameter
default, and a literal at a call site are the same class. A bound supplied by a
caller of this library is likewise Class A, because the caller is on the trusted
side of the threat model and spends only its own budget. Caller configuration
therefore does not form a third class; it changes who picks the ceiling, not
whether the ceiling is known before the read.

### The bounding invariant

> Every metadata materialization inside the decode is **either** capped by a
> constant this code chose, **or** charged against the work ledger before it
> occurs.

The disjunction is the whole contract, and both arms are load-bearing. Each
arm is what its class makes possible: a tool-capped quantity has a known worst
case, so the charge may come after; an author-sized one does not, so the charge
must come first.

For **Class A**, charging may follow materialization. The ledger's role there is
bounding *repetition*, not magnitude: a name capped at
`MaxTypeNameCharacters` cannot exceed the ceiling by itself, so reading it and
then charging it is sound. Requiring charge-before-read for Class A would be a
correctness claim the code does not need and does not make.

For **Class B**, the charge **must** precede the materialization. A single
author-sized blob can exceed any aggregate ceiling on its own, so charging
afterwards charges a bill already paid: by the time the copy has happened, the
work the ledger exists to refuse has been done, and the ledger can only report
it.

That requires knowing what a read will cost before performing it, which sounds
circular but is not: metadata records how large a thing is separately from the
thing, so the size can be read without producing the value.

`MetadataReader.GetBlobReader(handle)` positions a reader over one heap entry
and exposes its byte count as `Length`, allocating nothing and decoding
nothing. What that costs depends on the heap, because ECMA-335 stores the two
differently. A `#Blob` entry is length-prefixed, so its size is a compressed
integer read at a known offset. A `#Strings` entry is null-terminated, so its
size is found by scanning for the terminator.

Measured against entries from 16 bytes to 16 MiB:

| Entry | `.Length` at 16 B | `.Length` at 16 MiB | Allocated |
| --- | ---: | ---: | ---: |
| `#Blob` -- public key | 13.5 ns | 6.5 ns | 0 bytes |
| `#Strings` -- name, culture | 25.0 ns | 278,354.0 ns | 0 bytes |

Pricing a blob is constant. Pricing a string is not: the scan grows with the
string. Both are still sound prices, for the reason that matters -- neither
allocates, neither decodes UTF-8, and neither produces a value the decode
retains. The scan reads bytes the artifact already contains, so it cannot
amplify: at worst it examines each byte once. Materializing amplifies, because
a managed copy is allocated and retained, UTF-8 becomes wider UTF-16, and the
same entry is reached once per occurrence.

Those costs describe *physical* heap entries. SRM can also return **projected**
virtual strings, whose bytes it synthesizes and allocates inside
`GetBlobReader` itself; for those the price is paid in the act of reading it,
and pricing before materializing is not available. Projected strings arise only
from Windows Metadata, which `AGENTS.md` excludes as an unsupported input
format.

That exclusion is **not currently enforced on the decode path**.
`MetadataImageFormatClassifier` can refuse Windows Metadata, but this decode
path does not call it; adoption is tracked by #4877, and
`docs/metadata-primitives.md` states that the classifier's existence alone does
not close the entry-point inventory. Until a caller admits images through a
`SupportedEcma335` result, the allocation-free pricing claim above holds for
physical entries and is **unverified** for the repository's decode entry points
as a whole. A decode that admitted Windows Metadata would need this quantity
reclassified, because its price could not be read before it was paid.

The concrete contrast at a Class B site is therefore:

| Call | Produces | Allocates | Can amplify |
| --- | --- | --- | --- |
| `reader.GetBlobReader(handle).Length` | a byte count | nothing | no |
| `reader.GetString(handle)`, or a typed identity built from the blob | the value | a managed copy | yes |

So the decode reads the price, charges the ledger that amount, and performs the
copy only if the ledger accepted. `Length` decides nothing -- it is a
measurement, and the ledger is what refuses. Ordering is the entire point: the
same two calls in the opposite order compute the same numbers and bound
nothing.

One residual follows from the string scan and is accepted rather than hidden.
Pricing an author-sized name does work proportional to that name before any
charge is made. It is bounded by the image, allocation-free, and orders of
magnitude below materializing, so it cannot be amplified into the failure this
contract prevents -- but it is not zero, and a future quantity whose price
cannot be read this cheaply would need a different treatment.

Misclassifying a Class B quantity as Class A permits unbounded work on an
accepted decode, and is the failure this classification exists to prevent.

### The cost model

These are the quantities a decode consumes. The set is closed: a change that
introduces a new quantity must extend this table in the same change.

| Quantity | Arises from | Class | Bounded by |
| --- | --- | --- | --- |
| Expanded signature nodes | one provider callback per decoded node | A | `MaxSignatureTypeNodes` node budget |
| Occurrence copies | copying occurrence arrays through aggregate layers | A | materialization budget, `MaxSignatureTypeNodes * 8` |
| Type name characters | `TypeDef`/`TypeRef` name projection | A | `MaxTypeNameCharacters`, applied by the aggregate as the budget it hands the name reader, which refuses an over-budget entry before materializing; repetition charged to the ledger |
| Resolution-scope chain length | walking a `TypeRef` resolution-scope chain | A | `MaxRelationshipNodes` per walk; length charged to the ledger |
| Declaring-type chain length | walking a `TypeDef` declaring-type chain to project a nested type's full name | A | `MaxRelationshipNodes` per walk; length charged to the ledger |
| `TypeSpec` blob bytes scanned | completeness scan, re-entered once per occurrence | A | `TypeSpecGuard.MaxCumulativeBytes` across the active re-entry closure, not per `TypeSpec`; repetition charged to the ledger |
| Array shape bounds | array shape materialization | A | the guard's shape allowance, enforced by `SignatureBlobGuard` before decoding begins: it charges the declared size and lower-bound counts against its own `remainingTypeNodes`, because a byte-length check alone does not bound this work |
| `AssemblyRef` public-key **token** | terminal scope projection | A | exactly 8 bytes, enforced before the token is projected |
| `AssemblyRef` **full public key** | terminal scope projection, when `AssemblyFlags.PublicKey` is set | **B** | charged from storage length before materializing |
| `AssemblyRef` name and culture storage | terminal scope projection | **B** | charged from storage length before materializing |
| `ModuleRef` name storage | terminal scope projection | **B** | charged from storage length before materializing |

The `AssemblyRef` public key appears twice because one flag decides its class.
When `AssemblyFlags.PublicKey` is clear the blob is a token and an exact
8-byte check rejects anything else, so it is Class A. When the flag is set the
blob is a real key the author sizes, nothing caps it, and it is Class B. A
classification that named the field without naming the flag would be wrong for
one of the two paths.

### Budgets

Three budgets, each bounding a distinct thing. They are not interchangeable and
one cannot substitute for another.

- **Node budget** -- how many callbacks run. Bounds decode *breadth*.
- **Materialization budget** -- how many occurrence copies are made. Bounds
  aggregation *fan-out*.
- **Work ledger** -- how much metadata is examined, in bytes or characters.
  Bounds decode *cost*.

The first two count events; only the ledger observes magnitude. A budget that
counts callbacks cannot observe that one callback read a megabyte, so no count
budget substitutes for the ledger.

The ledger ceiling is `MaxTypeNameCharacters * 64`. The rationale is that one
decode may legitimately examine the equivalent of 64 maximum-length type names.
The census below reports the observed maxima this ceiling must clear; a change
that raises it must state why a legitimate signature needed more, not merely
that an input was rejected.

### Measured bounds

A ceiling is a claim about real artifacts, so it is set from a census rather
than from judgement. This one decoded every method, field, and property
signature in two corpora with all three budgets removed, recording what each
decode consumed.

| Corpus | Assemblies | Decodes | Ordered SHA-256 of inputs |
| --- | ---: | ---: | --- |
| .NET 11 preview 6 runtime and reference packs (`11.0.0-preview.6.26359.118`) | 490 | 363,322 | `4c0c167ce14db91ca046c44aa038a21d411da7d8b95fe5a18cba6248eaee38cc` |
| Third-party packages pinned by `docs/data/nuget-top-packages.lock.json` (90 of the 100 carry a `lib/` assembly), deduplicated by content | 431 | 2,387,301 | `776fd357c28d39124bba1c1d19e858692e2ecbddc23baff8caf02059a1dde97e` |
| Combined | 921 | 2,750,623 | |

No decode was rejected by a pre-existing guard, so every observation is of a
complete decode.

Per-decode consumption against each budget:

| Budget | Ceiling | p50 bucket | p99.99 bucket | Observed max | Headroom |
| --- | ---: | ---: | ---: | ---: | ---: |
| Node budget | 65,536 | ≤1 | ≤63 | 72 | 910x |
| Materialization budget | 524,288 | ≤1 | ≤63 | 158 | 3,318x |
| Work ledger | 262,144 | ≤63 | ≤511 | 1,182 | 222x |

Percentiles are recorded as base-2 histogram buckets, so each is reported as
its bucket's upper bound rather than an exact value. Observed maxima are exact.

Per-quantity consumption, as the largest single charge and the largest total
within one decode:

| Quantity | Largest single | Largest per decode | Charges | Per-item cap |
| --- | ---: | ---: | ---: | --- |
| Type name characters | 175 | 1,078 | 2,574,175 | 4,096 |
| Resolution-scope chain length | 3 | 19 | 1,465,380 | 256 |
| Declaring-type chain length | *unmeasured* | *unmeasured* | *unmeasured* | 256 |
| Array shape bounds | *unmeasured* | *unmeasured* | *unmeasured* | guard allowance |
| `AssemblyRef` name storage | 58 | 292 | 1,232,837 | none |
| `AssemblyRef` `PublicKeyOrToken` storage | 8 | 64 | 1,232,641 | 8 when a token |
| `AssemblyRef` culture storage | 0 | 0 | 0 | none |
| `ModuleRef` name storage | 0 | 0 | 0 | **none** |
| `TypeSpec` blob bytes | 0 | 0 | 0 | 4,096 |

Three quantities are unmeasured, for three different reasons, and none is
measured at zero.

The declaring-type chain is unmeasured because the census measures what the
instrumented build charged, and that build charges the chain length only on the
`TypeRef` resolution-scope path. A conforming implementation charges it on the
`TypeDef` path too, so the ledger figures above are a **lower bound** for a
conforming decode, understated by at most one charge per declaring-chain node
per projected nested name. The per-walk cap still holds unconditionally: the
walk reads into caller-owned storage of exactly `MaxRelationshipNodes` entries
and is refused beyond it.

Array shape bounds are unmeasured because of *where* they are enforced.
`SignatureBlobGuard` charges the declared size and lower-bound counts against
its own `remainingTypeNodes` allowance before decoding begins, and the census
accumulators start after the guard returns. That allowance is a separate
enforcement point from the aggregate's node budget, not the same counter
reached by another route, so the node figures above are accurate for what they
measure and simply say nothing about shape bounds. The quantity is bounded --
the guard refuses a blob whose shape counts exceed its allowance -- but the
corpus never priced it, so no observed magnitude supports the ceiling.

The `PublicKeyOrToken` class split is unmeasured because the census charges it
at one site and does not record `AssemblyFlags.PublicKey`, so the split cannot
be recovered from the recorded maximum. The flag decides the class, not the
blob's size or cryptographic validity: an artifact may set
`AssemblyFlags.PublicKey` on an 8-byte blob, and the adversarial probe below
does exactly that. The measured 8-byte maximum therefore bounds the quantity
but does not establish that no full public key occurred. Instrumenting the flag
and re-running would settle it.

Two results set the ceilings, for the measured quantities. Every measured
Class A quantity stays far below its cap -- the longest single type name
observed is 175 characters against a 4,096 ceiling, and the longest
resolution-scope chain is 3 against 256 -- so the caps constrain nothing real.
And no decode approached any of the three instrumented budgets, which is what
makes those budgets available to bound repetition rather than typical cost.
That statement does not extend to the guard's separate shape allowance.

The headroom column divides each ceiling by a measured maximum, so where the
maximum is understated the ratio is an **upper bound on headroom**, not
guaranteed headroom. This affects the work ledger, the one budget the
declaring-type chain would charge. Its guaranteed floor is obtained by assuming
the worst unmeasured case: every occurrence copy in the largest observed decode
projects a nested name whose declaring chain runs to the full
`MaxRelationshipNodes` cap. That is `1,182 + 158 * 256 = 41,630` against a
262,144 ceiling, or roughly **6.3x** guaranteed, against 222x measured. The
conclusion that the ledger is not the binding constraint survives, but only the
6.3x figure is load-bearing until the charge is added and the census re-run.

The last three rows were never exercised, and no probe below drives the culture
or `ModuleRef` name paths. That is a statement about the corpus, not about
reachability: each is reachable by construction. The `TypeSpec` probe below
drives the last row, and the public-key probe drives the full-key path that the
merged `PublicKeyOrToken` row cannot separate.
`GetTypeFromSpecification` in particular is unreachable
through `ELEMENT_TYPE_CLASS`, which admits only `TypeDef` and `TypeRef`; it is
reached through a custom modifier, where `TypeDefOrRefOrSpecEncoded` admits a
`TypeSpec`.

#### What the census cannot show

The census bounds the Class A quantities it measured. It does not bound the two
Class A quantities disclosed above as unmeasured: each is structurally bounded
by its guard, but neither has an observed margin. And it cannot bound Class B
at all, because the largest Class B value in any corpus is a fact about the
authors who happened to produce it.

A single method taking no parameters, whose one `TypeRef` is scoped to an
`AssemblyRef` carrying a full public key, consumes ledger units equal to that
key's size:

| Public key bytes | Ledger units charged | Against the 262,144 ceiling |
| ---: | ---: | ---: |
| 8 | 17 | 0.0x |
| 1,024 | 1,033 | 0.0x |
| 65,536 | 65,545 | 0.3x |
| 1,048,576 | 1,048,585 | **4.0x** |
| 16,777,216 | 16,777,225 | **64.0x** |

Every real decode measured stayed under 1,182 units. One author-chosen field
reaches four orders of magnitude beyond that, from an artifact small enough to
mail, and it scales linearly with no upper limit. This is the entire reason the
ledger exists and the reason charging must precede a Class B read: no census,
however large, would have predicted the fourth row, and no count of callbacks
would observe it.

The `TypeSpec` probe shows the contrasting Class A shape. Charged units track
the blob exactly, and the pre-existing guard, not the ledger, rejects the
oversized case:

| `TypeSpec` bytes | Ledger units charged | Outcome |
| ---: | ---: | --- |
| 5 | 16 | decoded |
| 1,029 | 1,040 | decoded |
| 8,197 | -- | rejected by `TypeSpecGuard` |

Because that guard caps the active re-entry closure at `MaxCumulativeBytes`, no
single `TypeSpec` charge in an accepted decode can approach the ledger ceiling,
and the ledger's role for this quantity is bounding how many times a shared
`TypeSpec` is re-entered.

#### Reproducing

Both corpora are pinned. The platform tier is the installed runtime and
reference packs at the stated version. The third-party tier is every `lib/`
assembly of the package versions in `docs/data/nuget-top-packages.lock.json`,
fetched from nuget.org and deduplicated by content; ten of those packages ship
no `lib/` assembly and contribute nothing. The digests above are over the
ordered per-file SHA-256 of the inputs, so a corpus that drifts is detectable
rather than silently different.

The census is a measurement build, not product code: it replaces the three
budget checks with accumulators, tags each charge site by caller line, and
decodes every member signature in each input. Rebuild it by instrumenting
`SignatureOccurrenceWorkBudget`. A change that alters what a decode charges
must re-run it, because the observed maxima are the only evidence that the
ceilings clear real artifacts.

A census run also checks that every charge the instrumented build *made* was
accounted for: charges that no classified site accounts for are recorded
against an unmapped bucket, which was zero across all 2,750,623 decodes. A
non-zero unmapped count means the table above is missing a quantity.

That check has a blind spot, and two of the three unmeasured quantities above
fell into it. The unmapped bucket sees only charges that execute. The
declaring-type chain is never charged on one path, and array shape bounds are
charged by the guard before the accumulators start; neither produces an
unmapped entry, because neither produces an entry at all. A zero unmapped count
therefore does not establish that the closed set is complete; it establishes
only that the charges the build made were classified. The `PublicKeyOrToken`
split is unmeasured for an unrelated reason -- that charge executed and was
mapped, but the census did not record the flag that separates the classes.

The pricing costs in *The two classes of cost* are measured separately and need
no product code. Emit an assembly whose single `AssemblyRef` carries a name and
a public key of a chosen size, then time and measure allocations for
`GetBlobReader(reference.Name).Length` and
`GetBlobReader(reference.PublicKeyOrToken).Length` in Release across sizes from
16 bytes to 16 MiB. The blob figure must stay flat and both allocation figures
must stay zero; a regression in either invalidates the charge-before-read rule
for that quantity.

### Charging bounds; caching does not

Caching a projection is an optimization and must never be load-bearing for the
bound. Removing any cache must leave the decode *bounded* -- it may cause a
legitimate input to be rejected, but it must not permit unbounded work.

Cache removal is therefore a valid probe of the bound: the required failure is
the ledger refusing, not an exception, a duplicate key, or an unrelated budget.
A cache-removal mutation that fails a gate for any other reason establishes
nothing about the bound.

### Enforcement obligation

This contract is enforced structurally, not by review. A conforming gate must
satisfy all of the following.

1. **Deny by default.** Any call that can materialize metadata fails the gate
   unless its site is classified by this contract. A gate that enumerates
   forbidden member names is not conforming, because every unnamed member --
   and every member added later -- is permitted by omission.
2. **No exempt regions.** A method that charges is not thereby trusted for its
   other reads. Sanctioned methods are checked like any other.
3. **Ordering is verified, not assumed.** For Class B sites the gate must
   establish that the charge dominates the materialization on every
   control-flow path. Asserting that a charge appears somewhere in the method
   does not discharge this obligation.
4. **Classification is explicit.** Each materializing site names its class. An
   unclassified site fails.

A gate that does not meet these obligations is named and documented for the
property it actually checks.

No gate meeting these obligations exists yet, so this property is currently
**unverified**; building one is implementation work, not part of this contract.

The obligations describe a division of labor that gate would complete. A gate
establishes that every site is classified. The census records any charge no
classified site accounts for. Neither closes the set: a gate cannot see a
quantity the contract never named, and a census cannot see work the
implementation never charges -- including work the corpus reaches constantly,
and work a separate enforcement point charges before the accumulators start.
Completeness of the closed set is therefore **unverified**, and establishing it
requires deriving the inventory from the source rather than from what a run
happened to charge.

### Failure is visible and attributed

A decode that exceeds a budget fails closed through the typed rejection outcome.
Exceeding a bound is a statement about the *artifact*, so it must not be
reported as anything else, and an internal programming error must not be
reported as a rejected signature. See #5062.

Refusal is not the only useful signal. Charging a Class B read requires its
magnitude before the read, so every Class B site holds that number by
construction; today it is compared against the ledger and discarded. Nothing in
this contract requires discarding it. A threshold *below* the refusal ceiling
may report an unusual magnitude as an observation, and the census shows such a
threshold would be quiet: the largest Class B charge anywhere in the corpus was
58 bytes, and two of the three measured Class B quantities never occurred at
all.

Two constraints hold if that is built. A reporting threshold never affects
acceptance -- it is an observation, and removing it changes no outcome. And it
never replaces the ceiling, because a threshold that reports and continues does
not bound. The ledger refuses; a threshold only notices.

### Non-claims

- Does not change `MaxSignatureTypeNodes`, `MaxTypeNameCharacters`, or
  `MaxRelationshipNodes`.
- Does not specify the aggregate's typed API, forwarding semantics, evidence
  model, or caching strategy beyond the load-bearing rule above.
- Does not specify exception mapping, which is #5062.
- Does not specify how a bound magnitude becomes a Finding, or any audit
  surface, which is #5074. The rule above constrains such a threshold; it does
  not design one.
- Does not claim any existing gate is conforming. The obligations above are the
  standard against which gates are to be judged, including gates already
  written.
