# Type spelling: identity vs. display

> Design write-up for the API-shape change that lets presentation refinements
> (tuple element names, `dynamic`, NRT annotations) reach the display without
> contaminating member **identity**. Motivated by folding
> `[TupleElementNamesAttribute]` into Metadata's type view (issue
> [#2996](https://github.com/richlander/dotnet-inspect/issues/2996), Step 3),
> which surfaced a conflation that predates tuples.

## The question that started this

Rendering named tuples as C# `(int count, string name)` instead of
`System.ValueTuple<int, string>` is a **settled, fixture-backed outcome** (22
passing display tests; the encoding is total, local, and taste-free). Wiring it
in broke one test — `MetadataFindingsTests.RealAssemblySelfCompare_IsExact` —
with an `ArgumentOutOfRangeException` inside the member-**identity** layer.

That failure is not a tuple bug. It is the symptom of a model deficiency: the
API shape stores every type as **one opaque display string**, and that same
string is what member identity re-parses to build its correspondence digest.
Every presentation refinement therefore leaks into identity.

## Current shape (the conflation)

A type is spelled exactly once, as a rendered string, in these slots:

- `ApiParameter.Type` (and `TypeWithModifier`)
- `ApiSignature.ReturnType`, `ApiMember.ReturnType`, `ApiMember.Signature`
- field / property / event type strings

All of them come from `TypeNode.Render()`
(`src/ILInspector.Metadata/TypeNode.cs`), which applies **presentation
refinements**:

- NRT annotation: `string` → `string?` (lines 217/239/312/410…)
- `dynamic`: `object` → `dynamic` (line 196)
- proposed: tuple syntax + element names, `System.ValueTuple<int,string>` →
  `(int count, string name)`

That one string then feeds **member identity**
(`src/ILInspector.Metadata/ApiMemberIdentity.cs`), the producer of the Member
Index digest / correspondence key. Identity does **not** have a structural
model to read; it re-derives structure by string-parsing the display spelling
and laundering presentation back out:

- Primary path: `signature.ParameterTypesSummary` →
  `ApiParameter.TypeWithModifier` (a `Render()` string), then
  `NormalizeCanonicalParameters` → `NormalizeDynamicToObject`.
- Fallback path (JSON-round-tripped surface; `SignatureModel` is `[JsonIgnore]`):
  re-parse `member.Signature` via `LegacyCanonicalMemberName`,
  `ExtractCanonicalParameterList`, `ExtractCanonicalIndexerParameterList`,
  `AbbreviateSignature`, each also passed through `NormalizeDynamicToObject`.

### Why this is a root-cause conflation, not a tuple edge case

**One slot, two consumers with opposite requirements.** Display wants the
refined spelling; identity wants a presentation-independent structural spelling.
Because they share a slot, each new refinement forces a per-feature identity-side
scrub — or breaks identity outright:

1. **`dynamic`** was patched with `NormalizeDynamicToObject` — a string scrub
   that works only because `dynamic`/`object` is a paren-free token substitution.
2. **NRT `?`** renders into `Type` but has **no** scrub. Identity is therefore
   already *latently annotation-sensitive*: two builds of the same assembly under
   different nullable context produce different fingerprints, even though
   nullability is erased at runtime and cannot distinguish overloads. This is a
   pre-existing leak that simply hasn't bitten a self-compare (annotations are
   stable within one build).
3. **Tuples** cannot use the scrub pattern at all:
   - `(int count, string name)` adds parentheses, element **names**, and reverses
     the `TRest` flattening — far past what a token substitution can undo.
   - Element names in the digest would make identity **name-sensitive**: renaming
     a tuple element would change a member's identity, which is wrong (names are
     erased at runtime; you cannot overload on them).
   - A tuple **return type** puts `(` at string position 0.
     `LegacyCanonicalMemberName` computes `signature.IndexOf('(')` (0) then
     `signature.LastIndexOf(memberName, parenStart - 1)` =
     `LastIndexOf(name, -1)` → `ArgumentOutOfRangeException`. Every string parser
     here assumes the first `(` is the parameter list; before tuples, no rendered
     type ever contained `(` (generics use `<>`, arrays `[]`, fnptr
     `delegate*<>`).

The pattern is unmistakable: presentation is baked into the identity-bearing
string, and identity has accreted feature-by-feature string surgery to remove it.
Tuples are the case where surgery is no longer viable, forcing the architectural
fix.

## Design principle

From `AGENTS.md`: *"Treat identifiers … and presentation as separate concerns.
Do not infer one from display text when a typed identity exists."* Metadata owns
the type facts and should produce **both** spellings from the structure it
already holds; identity should consume the structural spelling **directly** and
never re-parse display.

## The two spellings

| Spelling | Example | Properties | Producer |
| --- | --- | --- | --- |
| **Display** | `(int count, string name)`, `dynamic`, `string?` | presentation-refined; what humans/agents read | `TypeNode.Render()` (unchanged) |
| **Canonical** | `System.ValueTuple<int, string>`, `object`, `string` | presentation-independent, name-insensitive, annotation-insensitive | `TypeNode.RenderCanonical()` (new) |

`TypeNode` is the fact owner and already holds every discriminator
(`IsDynamic`, `IsNullableAnnotated`, tuple elements + `TupleElementName`).
`RenderCanonical()` ignores those three refinements and emits the structural
spelling. This **centralizes the split at the fact owner** and lets identity
**retire** the accreted string scrubs.

## Where the canonical spelling lives (the real decision)

Identity has two entry points — the live `SignatureModel` (transient,
`[JsonIgnore]`) and the persisted `member.Signature`/`ReturnType` strings (used
after JSON round-trip). The canonical spelling must be available to **both**,
which means it must survive JSON. Three options:

### Option A — Canonical fields on the typed model

Add persisted canonical spellings alongside the display ones:

- `ApiParameter.CanonicalType` next to `Type`
- canonical return next to `ApiSignature.ReturnType` / `ApiMember.ReturnType`
- canonical field / property / event type next to the display string

Produced at extraction by rendering the `TypeNode` twice (`Render()` +
`RenderCanonical()`). Identity's `ParameterTypesSummary`-equivalent composes from
`CanonicalType`; display keeps `Type`. The identity **fallback** path reads the
persisted canonical fields — **no string re-parsing, no
`NormalizeDynamicToObject`** on the live path.

- **Pros:** identity becomes a *stored structural fact*. Kills the string parsers
  and the dynamic scrub at the root. Name/annotation-insensitive by construction.
  Reusable for every future shape fact. Survives JSON.
- **Cons:** schema addition on `ApiParameter`/`ApiSignature`/`ApiMember`; the
  second render must be threaded through every type-bearing extraction site
  (return/param/field/property/event); older serialized surfaces lack the field.

**Compatibility guarantee (pinned invariant).** For every existing member, the
canonical spelling must equal the *current post-`NormalizeDynamicToObject`
identity spelling byte-for-byte*, so no published Member Index selector changes.
This constrains what the **Member Index projection** may erase (the XML-doc
projection differs — it erases NRT; see the multi-projection table in the
[Recommendation](#recommendation) and the round-2 reconciliation):

- **`dynamic`→`object`:** erased. Already the scrub's behavior — zero digest
  change.
- **Tuple syntax + names:** erased to `System.ValueTuple<…>`. Zero churn because
  no prior baseline ever rendered tuple *display* into identity — these members
  used the `ValueTuple<…>` spelling before, which is exactly what canonical
  restores. This is the change's actual win, and it is name-insensitive by
  construction.
- **NRT `?`: NOT erased in this change.** Today's identity spelling *includes*
  `?` (there is no nullable scrub). Erasing it would change the digest of every
  NRT-annotated member — a pin violation, not a free win. Canonical therefore
  *preserves* `?` exactly as today. Closing the latent NRT leak is a separate,
  explicitly **versioned** identity-contract change (deferred; see
  [Deferred](#deferred-nrt-in-identity)), not smuggled in here.
- **Modifiers (`out`/`in`/`params`):** preserved. These come from `[Out]` /
  `[ParamArray]` attributes and are prepended by `TypeWithModifier`, but the CLR
  type tree models `out`/`in`/`ref` identically as byref (`&`) and knows nothing
  of `params`. So canonical is composed as **`CanonicalTypeWithModifier`** =
  structural type (byref stripped) + the *same* extracted `Modifier` today's
  identity uses — mirroring the existing `type.StartsWith("ref ")` surgery in
  `ApiSurfaceExtractor.cs` (~960). The refinement erasure is orthogonal to the
  modifier.

`RealAssemblySelfCompare_IsExact` plus a `modreq`/`modopt`/fnptr/array/byref/
pointer/nested-generic/`+`-nested corpus canary is the pin.

**Migration.** When the persisted identity key is absent (older JSON), fall back
to the current string-parse path, kept as a versioned compatibility shim. The
shim must be **hardened to never throw** on tuple *display* text (guard the
`IndexOf('(')`/`LastIndexOf(name, parenStart-1)` path so a leading `(` degrades
rather than crashing) and must not attempt to recover `ValueTuple<…>` from
`(…)` display. New surfaces never take the shim.

### Option B — One persisted `ApiMember.CanonicalSignature` string

Compute the whole identity canonical string once at extraction and persist it;
identity reads it directly.

- **Pros:** simplest consumer.
- **Cons:** denormalizes declaring-type/kind into the member; still a string, so
  less reusable than a per-type canonical projection; duplicates identity's
  digest-format knowledge into the extractor (two places to keep in sync).

### Option C — Persist a structured type node per slot

Replace the opaque string with a typed type-tree in the serialized surface;
display and identity each render from the structure.

- **Pros:** most principled; every future shape fact flows for free.
- **Cons:** large JSON schema change and blast radius; over-scoped now.
  Option A is the incremental step toward it — the canonical spelling is exactly
  the identity projection a typed node would compute.

## Recommendation

**Hybrid A+B, multi-projection (revised after two review rounds):** a
`RenderCanonical()` structural **seam** on `TypeNode` (the fact owner) that each
identity **projection** composes with its *own* presentation-erasure policy; the
finished keys are computed at extraction while `SignatureModel` is live and
**persisted whole** on `ApiMember`.

The second review round established that there is **no single canonical
spelling**. Identity is not one key — it is several, each with a *different*
erasure contract. Baking one "canonical" string and reusing it for all of them is
unsound (it breaks XML-doc lookup). So `RenderCanonical()` produces the
structural, presentation-free spelling (tuples→`ValueTuple<…>`,
`dynamic`→`object`), and each projection layers its own policy on top:

| Projection | Persisted on `ApiMember` | Tuple names | `dynamic` | NRT `?` | Byref/return syntax |
| --- | --- | --- | --- | --- | --- |
| **Member Index digest** (primary identity) | yes | erased | →`object` | **preserved** (pin — identity includes `?` today) | `ref readonly` preserved |
| **XML-doc member id** | yes | erased | →`System.Object` | **erased** (XML-doc ids never encode NRT) | byref `@`, generic `{…}` |
| **Extension-instance correspondence** soft key | yes | erased | →`object` | preserved (matches current) | per current `NormalizeCorrespondenceType` |

Steps:

1. Add `TypeNode.RenderCanonical()` — the structural, name-insensitive spelling.
   This is the single fact-owner **seam**, not a finished key. Modifiers are
   composed separately as `CanonicalTypeWithModifier` = byref-stripped structural
   type + the *same* extracted `Modifier` today's `TypeWithModifier` uses. A
   **canonical return formatter** mirrors `FormatMethodReturnType` so
   `ref readonly` (inserted from the return param's `modreq`/`InAttribute`, not
   the type node) survives — a plain `RenderCanonical()` sees only `ref X`.
2. Compute **all three** finished keys — Member Index digest, XML-doc id, and the
   extension-instance projection key — during extraction from the live
   `SignatureModel`, each with its own erasure policy from the table, and persist
   them as top-level `ApiMember` properties. Today all three are computed *late*
   (`GetCanonicalSignature`/`TryGetExtensionInstanceProjection` run at diff time
   from the member's display strings via `Anchor?.CanonicalSignature`, not
   persisted); moving them to extraction is the actual structural change.
3. Every identity/correspondence consumer reads the persisted key, never a
   rendered display string. This explicitly includes the extension-instance soft
   key built in `MetadataFindings.CreateMemberFindingKey` — otherwise a tuple
   leaks into the soft key and element renames break diff pairing.
4. **All** raw-signature parsers — `LegacyCanonicalMemberName`,
   `ExtractMemberNameWithGeneric`, `ExtractCanonicalParameterList`,
   `ExtractCanonicalIndexerParameterList`, `AbbreviateSignature` — demote to a
   hardened fallback used **only** for pre-key serialized surfaces. Every one that
   does `IndexOf('(')` then `LastIndexOf(name, parenStart-1)` must guard the
   leading-`(` case to degrade rather than throw, and none may attempt to recover
   `ValueTuple<…>` from `(…)` display text.

This fixes the root conflation, deletes the scrubs from the live path, keeps each
key computed exactly once (no round-trip skew), and preserves every existing
selector under the pinned corpus invariant. Tuples land purely as a **display**
refinement (`Render()`), never reaching any key. Option C (a typed type-tree in
the surface) remains the principled long-term shape; this hybrid is the
incremental step and leaves `RenderCanonical()` as the projection a typed node
would later compute.

## How each distinction flows after the change

| Distinction | Slot | Producer | Consumer |
| --- | --- | --- | --- |
| Display type (tuple syntax, `dynamic`, NRT `?`) | `ApiParameter.Type`, return/field/property strings | `TypeNode.Render()` | `member` command, Markdown/JSON output |
| Identity keys (structural) | three persisted `ApiMember` keys (digest / XML-doc / extension) | `RenderCanonical()` seam + per-projection policy, composed live at extraction | `ApiMemberIdentity`, correspondence, diff pairing, XML-doc lookup |
| Tuple element **names** | display render only | `TypeNode` (`TupleElementName`) | display only — never any key |
| NRT `?` | display + Member Index key (preserved), XML-doc key (erased) | `TypeNode` (`IsNullableAnnotated`) + per-projection policy | see table above; **diff delta**, see caveat below |

## Validation / invariants

- **Pinned:** `RealAssemblySelfCompare_IsExact` stays exact — the persisted
  identity key equals the prior identity spelling across the whole corpus,
  including `modreq`/`modopt`/fnptr/array/byref/pointer/nested-generic/`+`-nested
  and `out`/`in`/`params` members (the modifier and shape canary).
- **New:** renaming a tuple element does **not** change any identity key
  (name-insensitivity), including the extension-instance soft key — but the rename
  **does** appear as a diff delta (display `Signature` is still compared).
- **New:** a tuple **return type** member yields a valid identity key (no crash)
  and a `(...)` display; the hardened fallback also never throws on tuple display.
- **New:** every key (Member Index digest, XML-doc id, extension-instance soft
  key, conversion-`~ReturnType`) is composed from `RenderCanonical()` with its own
  policy — a tuple or `dynamic` in any of them no longer leaks.
- **New:** `ref readonly` returns keep `readonly` in the canonical key (canonical
  return formatter), so no correspondence churn.
- Existing 22 tuple display tests are unaffected (display is `Render()`,
  untouched).

## In-scope consumer switches (do not miss these)

Every identity/correspondence path that today reads a **display** string must
compose from `RenderCanonical()` (with its projection's policy) instead, or it
inherits the leak:

- `ApiMemberIdentity` primary digest (`ParameterTypesSummary` → `TypeWithModifier`)
  — NRT preserved.
- `TryGetExtensionInstanceProjection` — reads `Parameters[0].TypeWithModifier` and
  `ReturnType`; invoked *late* from `MetadataFindings.CreateMemberFindingKey`, so
  its key **must be persisted at extraction** or the tuple leaks into the soft key
  and element renames break pairing (both reviewers, round 2, blocking).
- `TryGetXmlDocMemberIdentity` / `NormalizeXmlDocParameterType` — **NRT erased**,
  XML-doc `@`/`{…}` syntax; has an NRT strip today but **no** tuple parser. Its
  persisted projection differs from the Member Index projection (NRT erased vs
  preserved) — they are not the same string.
- Conversion-operator `~ReturnType` suffix (`NormalizeDynamicToObject(ReturnType)`).
- `NormalizeCorrespondenceType` and any Finding soft/correspondence keys.

## Broader opportunity: structure over display

This conflation is not unique to member identity. Across the Metadata layer,
multiple consumers recover a **structural** fact by string-matching a **display**
spelling — the same anti-pattern, each independently fragile to any presentation
refinement (NRT `?`, `dynamic`, tuples):

- `EcosystemIntegrationScanner` — `signature.ReturnType == "…IServiceCollection"`,
  `.StartsWith("Aspire.Hosting.ApplicationModel.IResourceBuilder<")`.
- `OpenTelemetryScanner` — `ReturnType == "bool"`,
  `== "OpenTelemetry.Trace.TracerProviderBuilder"`, etc.
- `MethodClassificationScanner` — pointer return detected via
  `ReturnType.Contains('*')`.
- `NormalizeXmlDocParameterType` — an entire mini type-parser (arrays `[]`,
  pointers `*`, generic `{…}`, attribute stripping, dynamic scrub) reconstructing
  structure from the display string; reused by the CLI `XmlDocFileParser`.

The fix these share is the same one identity needs: a durable, presentation-
independent **structural type view**, asked structural questions directly instead
of pattern-matched as text.

**The codebase already contains the reference *discipline*** (not a droppable
implementation). `ILInspector.Analysis`'s `TypeRef`
(`src/ILInspector.Analysis/TypeRef.cs`) is a structurally-equal type model whose
own contract states *"Display names are for humans; equality is structural"*. It
carries `ElementType` / `TypeArguments` / `ContainsPointer()` and excludes
advisory provenance (`TrustedFrameworkAssembly`, spoof flags) from structural
identity — exactly the separation of concerns this design argues for. Analysis
consumers (`OpaqueUnsafe`, `LibraryBodyIndex`) ask `TypeRef` structural questions
and never string-match. **Metadata is the outlier:** it builds a `TypeNode` tree,
flattens it to a display string, discards the structure, then makes every
downstream consumer re-parse.

**Important caveat (round 2):** `TypeRef` cannot simply move below Metadata. It
carries Analysis-specific trust bits and its decoder *rejects* function pointers
and custom modifiers (`TypeRefDecoder` → `Unsupported`) — precisely the
`fnptr`/`modreq`/`modopt` shapes this design's pin **must** preserve. So the north
star is to give `TypeNode` a durable structural projection sharing `TypeRef`'s
*discipline*, not to hoist `TypeRef` itself; a real `TypeNode`↔`TypeRef`
convergence is a larger, separate effort with its own layering and coverage work.

**Scope discipline for this change.** The tuple slice does *not* unify all of the
above. It (a) establishes the `RenderCanonical()` structural seam on `TypeNode`
and the persisted multi-projection-key pattern in a reusable way, and (b)
migrates only the identity/correspondence/XML-doc consumers listed above. The
scanners operate on `GuardedSignatureText` display strings independent of
`ApiSurface` (so they do not crash on tuples today) and, together with a
`TypeNode`↔`TypeRef` convergence, are explicit **follow-ups** this design enables
but does not perform — each a separate PR with its own compatibility surface.

## Deferred: NRT in identity

Erasing NRT `?` from the identity key is *semantically* correct (nullability is
runtime-erased; you cannot overload on it) but is a **digest-changing** contract
break, so it is out of scope here. It ships, if at all, as an explicitly
versioned identity-contract v2 with a documented, bounded churn set — never
bundled into the tuple change. Until then canonical preserves `?` exactly.

**Correction (round 2).** An earlier draft claimed a nullability change "remains a
diff delta the diff command surfaces (correspondence pairs the member; the delta
reports the change)." That is **not** how the engine behaves today. The primary
member key is `handle.CanonicalSignature ?? handle.Identity` and the *only* soft
key is the extension-instance projection (`MetadataFindings.CreateMemberFindingKey`)
— there is **no NRT-agnostic soft key**. Because canonical preserves `?`, an NRT
change alters the primary key and nothing bridges it, so the member is reported as
a paired **Add + Remove**, not a single "nullability changed" delta (the change is
still *visible*, just not gracefully paired). Achieving the graceful paired delta
would require a new NRT-agnostic soft key (a `RenderCanonical()` projection with
`?` erased) — that is part of the deferred v2, not a property of the current
design.

## Non-goals / boundaries

- Not changing what tuples render as in display (settled, fixture-backed).
- Not introducing a typed type-tree in the serialized surface (Option C) —
  future.
- Not closing the NRT-in-identity leak in this change (deferred, versioned); NRT
  changes currently surface as Add/Remove, not paired deltas.
- Metadata-layer only. No decompiler / `ILInspector.CSharp` changes; product
  paths stay SRM-only, Roslyn-free, NativeAOT-friendly.
- Ecosystem scanners (`EcosystemIntegrationScanner`, `OpenTelemetryScanner`, …)
  that exact-match `ReturnType == "bool"` etc. would become annotation-robust by
  reading a structural view — noted as follow-up, not required here.

## Review reconciliation

Two adversarial review rounds, each by two models off the author's Claude Opus
4.8 (GPT-5.5, Gemini 3.1 Pro), per the roster. Round 1 reviewed bare Option A;
round 2 reviewed the revised hybrid. Findings and resolutions:

### Round 1 (Option A)

- **[Both, blocking] Missed identity consumers.** `TryGetExtensionInstanceProjection`
  and the XML-doc identity path read display strings; XML-doc cannot parse tuple
  `(…)` into `System.ValueTuple{…}`. → Promoted to explicit in-scope consumer
  switches.
- **[GPT, blocking] NRT vs byte-exact pin contradiction.** Today's identity
  includes `?`, so erasing it churns digests. → Canonical *preserves* NRT;
  leak-closure deferred to a versioned v2.
- **[Gemini, blocking] `out`/`in`/`params` divergence.** `TypeWithModifier`
  prepends attribute-derived modifiers the CLR type tree collapses to `ref`/none.
  → Canonical composed as `CanonicalTypeWithModifier`, preserving the modifier.
- **[Both, blocking] Fallback crashes on tuple display.** → Shim hardened.
- **[Both, design] Prefer persisting the finished key over parallel canonical type
  strings.** → Adopted the hybrid A+B.
- **[Gemini, validated] Correspondence vs delta** is the correct distinction.

### Round 2 (revised hybrid)

- **[Both, blocking] Extension-instance key leaks tuples.** The hybrid rejected
  persisting canonical *types* on `ApiParameter`, but `TryGetExtensionInstanceProjection`
  runs *late* on `ApiMember` (`CreateMemberFindingKey`) from display strings — so
  a tuple leaks into the soft key and element renames break pairing, violating the
  design's own invariant. Code-verified: primary key is
  `handle.CanonicalSignature ?? handle.Identity`; the sole soft key is this
  projection. → The extension-instance key is now **persisted at extraction** too,
  alongside the digest and XML-doc id.
- **[GPT, blocking] No single canonical spelling.** The XML-doc id must *erase*
  NRT (`M(string?)`→`M:T.M(System.String)`) while the Member Index digest must
  *preserve* it — one spelling for both breaks XML-doc lookup for every nullable
  API. → Recommendation restructured into an explicit **multi-projection** model:
  `RenderCanonical()` is a structural *seam*; each key applies its own erasure
  policy (table in the recommendation).
- **[GPT, blocking] `ref readonly` returns.** `FormatMethodReturnType` inserts
  `readonly` from the return param's `modreq`/`InAttribute`, which a bare
  `RenderCanonical()` on the type node loses → correspondence churn. Code-verified
  in `ApiSurfaceExtractor.FormatMethodReturnType`. Gemini missed this (only
  considered plain `ref`). → Added a canonical return formatter mirroring it.
- **[GPT, blocking] Fallback hardening incomplete.** `ExtractMemberNameWithGeneric`
  has the same `IndexOf('(')`/`LastIndexOf(name, parenStart-1)` throw as
  `LegacyCanonicalMemberName`. → Hardening now covers **all** raw-signature
  parsers.
- **[Gemini, correction] NRT diff-delta overclaim.** Corrected above — NRT changes
  currently surface as Add/Remove, not paired deltas, until a v2 NRT-agnostic soft
  key exists.
- **[GPT, non-blocking] `TypeRef` overstated.** It carries Analysis trust bits and
  its decoder *rejects* fnptr/modopt — the exact shapes the pin preserves. →
  Broader-opportunity section now says share `TypeRef`'s *discipline*, not hoist
  `TypeRef` itself.
- **[Gemini, validated] Scanners are safely deferrable** — they run on
  `GuardedSignatureText`, independent of `ApiSurface`, so they do not crash on
  tuples today. Modifier composition (A2) and per-kind key byte-match (B)
  confirmed sound by both.

## Residual open questions

1. Is a single-assembly self-compare a sufficient pin, or is a cross-build canary
   needed to prove the shape/modifier canonical is byte-stable?
2. Exact whitespace of each projection must be pinned (today
   `NormalizeCanonicalCommas` strips `", "`→`","`); the persisted keys must match
   byte-for-byte.
3. Should the deferred NRT-in-identity v2 land (with its NRT-agnostic soft key for
   graceful paired deltas), or is annotation-sensitive identity acceptable
   indefinitely?
