# Decompiler Taste

The principles that decide what `dotnet-inspect`'s decompiled C# looks like whenever more than one rendering is possible. This is a design document: when a new pattern, sugar, or simplification is proposed, it should be argued in these terms. The companion [decompiler.md](decompiler.md) governs how the pipeline is architected to make these decisions.

## The core stance: honest inspection

The decompiler's output renders **what the IL does**, not what the source probably said. It is built to sit next to the Annotated IL view of the same method, so a reader can move between the two and have them agree.

Two consequences:

- **Never print structure that hasn't been proven.** When structuring can establish a shape (a loop, a guard, an arm-only region), render it as that shape; when it can't, degrade to honest IL-flavored C# — a labeled `goto` — rather than a plausible guess. A correct `goto` beats an incorrect `if`.
- **Wrong semantics is the worst failure class.** Output that compiles and reads plausibly but computes something else (`!a & b` for the negation of `a & b`) is worse than ugly output. Style is graded cosmetically; semantics is graded pass/fail.

## Canonical forms

Decompilation is a many-to-one transform twice over: the compiler collapses many source programs into one IL shape, and the decompiler collapses each IL shape into one rendering. `return a >= b ? a : b;` and `if (a >= b) return a; return b;` are the same IL, so they must come back as the same C#. The decompiler's job is to pick **one canonical representative per IL equivalence class** — and the ideal property of the round trip is a fixed point: compiling our output and decompiling it again yields the same text.

That framing turns style questions into a single question: *which member of the equivalence class do we print?*

## The style oracle: dotnet/runtime's `.editorconfig`

Where one IL shape admits several C# spellings, render the form that **dotnet/runtime's `.editorconfig` and enabled IDE analyzers (code fixers) encourage**.

Two reasons:

1. **It is an established, versioned, externally documented choice.** Picking canonical representatives stops being a per-change taste debate, and the target moves with the runtime repo's own style evolution rather than ossifying into ours.
2. **It makes testing coherent.** The fidelity fixture corpus is runtime-shaped code written under that style — so fixer-style output and recompile fidelity are the same goal, not competing ones.

Two clarifications on scope. First, the oracle is a **default**, not a reconstruction of the input's own style: real assemblies — runtime included — are stylistically mixed, and an `.editorconfig` is *directional* (it names where a codebase is converging over time, not what its current text says), so "match the source style" is not a well-defined target. Picking a published, versioned community default is the honest substitute. Second, the oracle settles only **class-3 no-anchor cosmetics** (below); it never overrides a class-1 or class-2 IL distinction. Where a no-anchor choice is not yet a stable default it is exposed opt-in rather than imposed — see [Line wrapping](#line-wrapping) and [Names](#names).

The oracle has two facets, and it matters which one a given decision rests on.
Its primary voice is **declared** — the rules `dotnet/runtime`'s `.editorconfig`
and the enabled IDE fixers write down explicitly (the `dotnet_style_*` /
`csharp_style_*` keys). Where the declared oracle is *silent* — it has no rule for
the shape in question — the **revealed** oracle breaks the tie: the dominant style
of the runtime's own source. Both are the same authority (the runtime's taste);
they differ only in whether that taste is written down as a config rule or merely
practiced in the code. Several class-3 tiebreaks already rest on the revealed
facet precisely because `.editorconfig` says nothing about them — pointer member
access renders `p->Member`, and query syntax comes back as a fluent chain, because
that is what runtime code writes. So "the oracle is silent" always means the
*declared* oracle; the revealed oracle is silent only when the corpus itself shows
no dominant form. A shape can be declared-silent yet revealed-endorsed — which is
exactly the status of the fidelity-neutral formatting/synthesis knobs below.

## The three-class rule

Every proposed rendering falls into one of three classes, and the class decides the answer:

**1. IL-exact preferred forms — adopt freely.** The modern spelling is precisely what the IL in hand compiles from, so it is the better representative — sometimes the *more faithful* one:

- `is null` / `is not null` for null tests that compile to a reference `ceq`/branch. (`== null` could mean an `op_Equality` call; render `==` exactly when the IL calls the operator.)
- Is-pattern matching for the `isinst` + branch + captured-cast shape (`if (x is Foo f)`), where the stored-and-used cast binding is the anchor. Bare `x is Foo` with no binding is IL-identical to `(x as Foo) != null`, so *that* choice is a class-3 tiebreaker, not an IL-exact form.
- Switch expressions for switch-plus-returns shapes.
- Range indexer (`s[i..j]`) for the compiler's spilled `Substring`/`Slice(start, end - start)` range lowering — see [Range indexer](#range-indexer) for the spill-anchor discipline.
- `continue`/`break` for loop-edge branches.
- Compound assignment/increment, **but IL-anchored only where the target's storage location is computed once** — array and pointer elements compile to a distinct `ldelema` + `dup` address form (`a[i]++` differs from the re-indexing `a[i] = a[i] + 1`), and a member over a side-effecting receiver has *no* single-evaluation `x = x + 1` spelling at all, so the compound form is the only source for that IL. For a local, static field, `this`- or local-rooted instance field, or ordinary property, `x++`, `x += 1`, and `x = x + 1` are byte-identical IL, so *that* spelling is a class-3 no-anchor pick (below) the oracle settles (`x++`/`x--` for ±1, `x op= v` otherwise).

**2. Fidelity-erasing forms — decline, always.** A preferred form is never adopted when it would erase a distinction the IL actually makes:

- `&` is not rendered as `&&` when the IL evaluates both operands — even though the source almost certainly wrote `&&` and the compiler chose the non-short-circuit lowering.
- Float comparisons are never "simplified" across NaN behavior: `!(a <= b)` is not `a > b`.
- Debug-shaped and Release-shaped IL of the same source render differently, because the IL *is* different. The canonicalization dial is deliberately set weaker than a recompilation-oriented decompiler like ILSpy, which normalizes both into one clean form. Preserving IL-shape sensitivity is part of the inspection value, not a deficiency.

Conflicts between class 1 and class 2 are rare by construction — most fixer suggestions are codegen-identical — and when they occur, fidelity wins.

**3. No IL anchor — follow the oracle as a tiebreaker.** Conventions with no IL consequence at all (`var` policy, explicit types on declarations, brace style) follow the runtime `.editorconfig`, purely for corpus coherence.

- LINQ query syntax (`from x in xs select f`) compiles to the *same* `Enumerable.Where/Select/...` calls as the fluent chain — query expressions are translated during binding, before any lowering, so the two forms are IL-identical. With no anchor to choose between them, the oracle decides: the runtime writes fluent method chains, so that is what we render. We do not re-sugar back to `from..select`. (This is why the `Query` row in the lowering ledger is `Declined` — a no-anchor mechanism distinct from `Unhandled`/owed, not a gap to fill.)
- **Erased before lowering — render the constant.** Some constructs the compiler resolves to a bare constant before any lowering, leaving no IL to recover from. `nameof(x)` compiles to the string literal `ldstr "x"` — indistinguishable from writing `"x"` — so it comes back as the string; there is nothing to re-sugar. The same holds for constant folding (`60 * 60` → `3600`), primitive and reference `default` (`default(int)` → `0`, `default(string)` → `null`), and string spelling (verbatim, raw, and escaped forms collapse to one `ldstr`). A *struct* `default` is not in this set: `default(BigStruct)` emits `initobj`, an anchored shape recovered as `default`.

### Pointer member syntax

Pointer member access has a C# spelling choice for named members and a different
one for indexer-like access:

- Prefer `p->Member` for fields, properties, and instance methods on a pointer
  receiver.
- Prefer `(*p)[i]` for indexer access on the pointed-to value.
- Keep extension-shaped calls with pointer receiver parameters in static form
  (`Extensions.M(p)`) unless the receiver parameter is a by-ref `this ref T`
  extension over the pointee; C# does not allow `this T*`.

The style oracle decides the spelling where syntax permits more than one valid
form. `dotnet/runtime`'s `.editorconfig` has no pointer-specific rule, so the
dominant runtime source style is the oracle: runtime code commonly writes
`pMT->IsValueType`, `pMT->ComponentSize`, and `pException->InternalPreserveStackTrace()`,
while indexer-like pointer dereference appears as `(*array)[i]`. Those examples
also match the semantic boundary: `p->Member` is the direct pointer-member
syntax, while `p[i]` is pointer arithmetic, not an indexer call on the pointee.

### Target-typed `new()`

`T x = new T(args)` and `T x = new(args)` compile to the identical
`newobj T::.ctor(args)` — the constructed type is fixed by the IL token, so the
spelling has no IL consequence. This is a class-3 no-anchor choice, and the
oracle decides it: `dotnet/runtime`'s `.editorconfig` sets
`csharp_style_implicit_object_creation_when_type_is_apparent = true`, so when the
type is apparent from the left-hand side we drop the redundant type name.

```csharp
// the author writes `var sb = new StringBuilder(n)` — type apparent on the right;
// IL drops `var`, so the decompiler spells the explicit local type on the left.
StringBuilder sb = new StringBuilder(n);   // type named twice
StringBuilder sb = new(n);                 // target-typed: type apparent on the left
```

Unlike `var` or brace style, this spelling carries a **binding hazard**, so it is
adopted as a sound conservative over-approximation rather than everywhere the
oracle would fire — it declines whenever the shortened form could bind
differently or fail to compile:

- **Only when the contextual target type exactly `Equals` the constructed type.**
  `new()` binds to the target type, so a base-typed, interface-typed, `Nullable<T>`,
  or `ValueTuple` target would construct a different type than the original
  `newobj`.
- **Left-hand-side assignment/declaration positions only.** A target-typed `new()`
  in a call-argument position participates in overload resolution and could change
  binding; return positions are out of scope for now and kept explicit.
- **Declines a bare `object`/`dynamic` target.** `dynamic` erases to `object` in
  the IL, and target-typed `new()` is illegal for a `dynamic` target (CS8752);
  `new object()` carries no type name to drop anyway.
- **Declines covariant array element stores** where the `stelem` token is wider
  than the array's static element type, since `a[i] = new()` binds to the element
  type rather than the token.

The discipline generalizes: a no-IL-anchor spelling is still declined when
adopting it could change binding, and each decline is proven by recompile/corpus
fidelity. Optional-argument elision (below) is the other spelling that ships
under this binding-hazard discipline; any future one that shares the hazard —
apparent-type or argument-omission — is scoped the same way.

### Optional-argument elision

C# optional parameters are erased by the type system: the compiler bakes the
declared default in at every call site, so `ToWords(gender, null)` and
`ToWords(gender)` compile to the *same* call carrying the same trailing constant.
Dropping the argument is opcode-neutral — recompiling re-inserts the identical
default — so this is a taste transform (valid-but-different), not a fidelity
change, and it renders the shorter call the runtime source would write.

Like target-typed `new()`, it carries a **binding hazard**: a shorter argument
list can rebind to a different same-named overload. It is therefore a sound
conservative over-approximation, never a Roslyn overload-resolution clone. The
pass (`OptionalArgumentElisionPass`) does not decide safety itself — it is bounded
by the overload-safe count the importer stamps from metadata
(`MethodRef.SafeTrailingElidableCount`, computed in `OptionalArgumentFacts`), and
only drops a trailing argument that is still a bare constant equal to the
recovered default while the call still carries the callee's full parameter list.
Anything the count cannot prove safe stays explicit.

### `this` member qualification

`this.field` and `field`, and `this.Prop` and `Prop`, compile to the identical
IL — a `this`-rooted instance member load is `ldarg.0; ldfld` / `ldarg.0; call
get_Prop` either way, so the `this.` prefix has no IL consequence. The same holds
for an instance method call (`this.M()` and `M()` both emit `ldarg.0;
call/callvirt`), a method group over `this` (`this.M` and `M` both emit `ldarg.0;
ldftn`), and an event subscription (`this.E += h` and `E += h` both emit
`ldarg.0; ... call add_E`). This is a class-3 no-anchor spelling with **no binding
hazard** (unlike target-typed `new()` or argument elision): the prefix cannot
rebind the member, it can only be redundant. The shipped default renders the bare
name, qualifying only where the bare name would not bind to the member — a
local/parameter shadow or a member/type-name collision — matching how the runtime
writes member access.

The always-qualified spelling is available as an opt-in, off by default so
default output stays byte-for-byte stable:

- `PrinterOptions.QualifyFieldAccess` mirrors `dotnet_style_qualification_for_field`.
- `PrinterOptions.QualifyPropertyAccess` mirrors `dotnet_style_qualification_for_property`.
- `PrinterOptions.QualifyMethodAccess` mirrors `dotnet_style_qualification_for_method`
  (instance method calls and method groups over `this`).
- `PrinterOptions.QualifyEventAccess` mirrors `dotnet_style_qualification_for_event`
  (event `+=`/`-=` subscriptions).

The four knobs are independent — each qualifies only the member kind it governs.
Two consequences are worth calling out:

- A genuine non-virtual `base.M()` call is **never** rewritten to `this.M()`, even
  with method qualification on: the `base` call deliberately skips virtual
  dispatch, so `this.M()` would re-enable it and change behavior.
- Event subscriptions are governed by `QualifyEventAccess`, not
  `QualifyPropertyAccess` (they share a printer helper internally but are decoupled
  at the knob), matching the separate `_event`/`_property` editorconfig keys.

```csharp
public int Compute() => _count + Extra;          // shipped default: bare
public int Compute() => this._count + this.Extra; // both knobs on
```

When a knob adds `this.`, the printer records a byte-preserving taste decision
(category `taste`, keyed by the knob's `StyleOptionCatalog` id, e.g.
`qualify-field-access`) so the CLI **Applied Taste** section reports the opt-in
spelling. Only knob-attributed qualification is recorded: a mandatory `this.`
that disambiguates a shadow or type-name collision would appear with the knob off
too, so it is never attributed to the knob as a taste choice. Recording therefore
applies only to a genuine instance receiver — a static or extension method whose
first parameter is spelled `@this` (IL name `this`) reaches the same site but has
no implicit receiver, so it records nothing.

All four knobs also require the member to be declared on the **enclosing type at
its own instantiation** before recording. A base-declared member reached through
`this` — a field hidden by a `new` field of the same name (whose `base.X` load a
pre-existing emit gap mis-spells `this.X`, but `this.X` binds to the *derived*
field), or a merely-inherited field/property/event — is not a byte-preserving
self-type opt-in, so it records nothing. This uniformly under-records a legitimate
`this.` on an inherited member; a false-negative is safe, a false-positive is not.
Method qualification is the most guarded because a bare method name binds through
more rules than a field or property; it records a decision only when every one of
these holds:

- **enclosing type at its own instantiation.** The callee's declaring type must
  be the enclosing type at its exact instantiation, not merely the same generic
  definition. A callee that is not the exact self-type is one of: an inherited
  base method (whose bare/`this.` form would rebind — the non-virtual case
  already renders `base.M`); an **explicit interface implementation** invoked
  through `this`, which does not bind via `this.` at all (it requires a cast); or
  a **different instantiation** of the enclosing generic type — e.g.
  `((I<object>)this).M()` from within `I<T>`, where bare/`this.` `M()` binds to
  `I<T>::M`, not `I<object>::M`, so the qualifier is not byte-preserving.
  Definition-only equality is too loose for the last case, so the guard reuses
  the exact-instantiation test the static-call qualifier uses. This deliberately
  under-records a legitimate `this.BaseMethod()`; a false-negative is safe, a
  false-positive is not.
- **not shadowed.** A name shadowed by an in-scope local, parameter, or nested
  lambda binder makes the `this.` mandatory disambiguation, not a choice.
- **speakable target.** A compiler-generated group target — an unspeakable
  `<M>b__N` lambda or `<Outer>g__Local|N_M` local-function name — is never
  authored. The unspeakable check runs against the **raw** IL metadata name,
  before `CSharpNaming.SourceMethodName` strips its `<...>` decoration (a lifted
  local function otherwise arrives spelled as a plain identifier and would slip
  past the check).
- **not a generic method group.** A method *group* over a generic instance method
  (`this.Make<int>` bound to a `Func<int>`) is not recorded: `MethodGroupText`
  renders only the bare name, dropping the type arguments (a pre-existing emit gap
  the call and `&`-of paths avoid), so the emitted `this.Make` fails delegate
  return-type inference (CS0411) and does not round-trip. A generic method *call*
  (`this.Make<int>()`) still records normally.

Same-named overloads are recorded as distinct decisions; the dedup discriminator
is a structurally complete per-overload key (generic arity — so `M()` and `M<T>()`
stay two rows despite sharing an empty parameter list — generic instantiation,
array element type and rank, by-ref/pointer decoration, generic-parameter slot,
function-pointer return type / calling convention / parameter ref-kinds, plus the
full assembly-qualified namespace) so `M(List<int>)` and `M(List<string>)`,
`M(NsA.Widget)` and `M(NsB.Widget)`, or `M(delegate*<int>)` and
`M(delegate*<string>)`, stay two rows rather than collapsing into one. For a
generic method the key uses the DEFINITION signature (its type parameter left as
`!!0`) plus arity, not the MethodSpec-substituted parameters, so two
instantiations of one generic method — `this.Echo<int>(1)` and
`this.Echo<string>("s")` of `Echo<T>(T)` — collapse into a single row (they are
one source member) even when a parameter mentions the type parameter.
Qualifications inside a locals-bearing lambda body, which renders through an
isolated nested printer, are not currently surfaced as taste rows.

A method call the compiler lowered and the decompiler did **not** re-sugar — for
example a `this.GetEnumerator()` left behind when a `foreach` over `this` is not
raised back to `foreach` syntax — is still recorded when its qualifier is a valid,
byte-preserving, same-member choice on the rendered lowered code. This is
consistent with the decompiler rendering *what the IL does*: the row honestly
annotates the `this.` the knob applied to the faithfully-rendered call, and the
bare name binds to the same member. It is distinct from the suppressed cases
above, where the emitted `this.M()` would not compile, would not bind bare, or
would rebind to a different member. When the pattern *is* raised (the common
case), no such call is printed and nothing is recorded.

### Expression-bodied members

Rendering a value-returning member as `head => <expr>;` instead of a brace block
wrapping a lone `return <expr>;` is an IL-identical framing choice — the two
forms are a language-guaranteed equivalence — so it sits below the three-class
rule, alongside brace style and line wrapping. The oracle is `dotnet/runtime`'s
`.editorconfig` (`csharp_style_expression_bodied_methods` /
`_properties` / `_accessors = true`), so the expression-bodied form is the
shipped default.

A body that is exactly one statement folds on the simple single-line path (a
lone `return <expr>;`, a `throw`, or a statement-expression). A body that is one
*multi-line* single statement folds too: a wrapped switch return (issue #3088),
any other wrapped single `return <expr>;` such as a fluent chain (issue #3084),
or a void member whose one statement is a wrapped expression statement — a fluent
call chain with the result discarded (issue #3084). The arrow trails the
signature line with the statement's opening token after it (the value after a
stripped `return`, or the whole first line otherwise), and the continuation
lines re-indent one level under the member — the natural multi-line extension of
the single-line `head => expr;` form.

```csharp
public static string Pipeline(StringBuilder builder)
{
    return builder
        .Append("alphabet")
        .Append("bravissimo")
        .ToString();
}
// folds to:
public static string Pipeline(StringBuilder builder) => builder
    .Append("alphabet")
    .Append("bravissimo")
    .ToString();

public static void Drain(StringBuilder builder)
{
    builder
        .Append("alphabet")
        .Append("bravissimo")
        .Clear();
}
// folds to (no `return` to strip — the whole first line trails the arrow):
public static void Drain(StringBuilder builder) => builder
    .Append("alphabet")
    .Append("bravissimo")
    .Clear();
```

The fold is gated on a typed structural signal the printer proves from the
emitted statement tree — the body is exactly one top-level statement (a `return`
with a value, or an expression statement) with no lifted declarations, label,
constructor chain, field initializer, async modifier, or unsupported fallback —
never a re-parse of the rendered text. A `return` branch additionally requires
its printed form to begin with a bare `return` so a value the printer lifted
into a leading declaration (a `stackalloc`-to-pointer return) is not mistaken for
a foldable expression. A member with any statement preceding the folded one keeps
its brace block. (The extractor is shape-agnostic about the leading keyword, so a
wrapped `throw <expr>;` would fold identically should one ever print multi-line;
the printer does not currently produce multi-line throws, so that path is latent,
not reachable.)

### Line wrapping

Breaking a long line across continuation lines is pure whitespace: it emits the
same tokens in the same order, so the wrapped and inline forms recompile to
identical IL and it selects the *same* member of the equivalence class — the
representative is unchanged, only its layout differs. Wrapping therefore sits below the three-class rule, alongside
brace style, with a single input from the oracle: `dotnet/runtime`'s 120-column
maximum line width decides *when* a single-line rendering is too wide to keep.

Three constructs wrap one element per continuation line when the flat form would
exceed that width:

- **Fluent method chains — always on.** The runtime routinely breaks a long
  fluent chain one `.Member(args)` call per line, and the transform is
  token-identical (each line is spliced out of the single-line rendering by
  length arithmetic), so it is applied unconditionally.

  ```csharp
  return source.Where(predicate).Select(projection).OrderBy(key).ToList();
  // wraps to:
  return source
      .Where(predicate)
      .Select(projection)
      .OrderBy(key)
      .ToList();
  ```

- **Member signatures — always on (issue #3185).** When a member declaration's
  first physical line — its head plus whatever trails the closing `)` on that
  line (`=> expr;`, `{`, or `;`) — would exceed the width, the parameter list
  breaks one parameter per line under a four-space continuation indent, with the
  closing `)` (and any trailing `where`/base-`: this(...)` clause) kept on the
  last parameter's line and the `=>`/`{`/`;` staying on that line. This mirrors
  the corpus, which wraps a wide signature's parameters rather than the arrow.
  The rewrite is whitespace-only and token-identical, so it is applied
  unconditionally like the fluent wrapper. The parameter list is located
  conservatively — a signature whose parameter group cannot be unambiguously
  identified (e.g. a symbolic `operator +(...)`, or an indexer's `[...]` list)
  stays on today's single line rather than risk a mangled declaration.

  ```csharp
  public static JsonElement Parse([StringSyntax("Json")] ReadOnlySpan<byte> utf8Json, JsonDocumentOptions options = default) => JsonDocument.ParseValue(utf8Json, options).RootElement;
  // wraps to:
  public static JsonElement Parse(
      [StringSyntax("Json")] ReadOnlySpan<byte> utf8Json,
      JsonDocumentOptions options = default) => JsonDocument.ParseValue(utf8Json, options).RootElement;
  ```

- **Short-circuit `&&` / `||` chains — opt-in.** The boolean analog breaks each
  operand onto its own line with the operator trailing each broken line. It
  carries the same whitespace-only guarantee — it re-renders each flattened
  operand through the exact function the flat chain uses and declines unless the
  per-operand join reproduces the flat text byte-for-byte, so any cast, compound
  form, or pattern rewrite keeps the statement inline rather than risk dropping a
  token — but it is **off by default**
  (`PrinterOptions.WrapSplittableExpressions`) and surfaces as a taste decision
  when enabled.

  ```csharp
  return firstFlag && secondFlag && thirdFlag && fourthFlag && fifthFlag && sixthFlag;
  // with WrapSplittableExpressions, wraps to:
  return firstFlag &&
      secondFlag &&
      thirdFlag &&
      fourthFlag &&
      fifthFlag &&
      sixthFlag;
  ```

The asymmetry is deliberate and matches how this doc treats every cosmetic lens
that isn't yet a default: like readable name synthesis under [Names](#names), the
boolean-chain wrapper changes only layout, so it is introduced opt-in to keep
default output byte-for-byte stable until the choice proves out, rather than
churning every wide boolean `return` in the corpus. The always-on fluent and
signature wrappers reproduce the corpus and stay on.

**General one-liner opt-out.** A single knob,
`PrinterOptions.DisableOneLinerWrapping` (catalog id `disable-one-liner-wrapping`,
off by default), suppresses *all* always-on width wrapping — both the fluent-chain
and the member-signature wrappers — so a caller who wants wide constructs to stay
on one physical line gets true one-liners. It deliberately does **not** touch the
opt-in `WrapSplittableExpressions` wrappers, which are already off unless
explicitly enabled. Keeping a wide construct inline diverges from the corpus
(which wraps), so this knob is a user compactness preference endorsed by neither
the oracle nor the corpus — classified like the branchless-boolean lens.

### Range indexer

`s[start..end]` and `s.Substring(start, end - start)` (and the `Span<T>` /
`ReadOnlySpan<T>` `Slice` equivalents) do **not** compile to the same IL: the
range indexer lowers with the compiler spilling the start index into a hidden
`int` temp (`V = start; …(V, end - V)…`), while a hand-written two-argument
`Substring`/`Slice` call has no such spill. That spill is the anchor — it is
precisely the IL the range form compiles from, so recovering it to `s[start..end]`
is a class-1 IL-exact preferred form (recompiling the slice re-creates the same
spill), not a class-3 taste choice.

```csharp
// same lowering, so same recovered spelling:
token = text.Substring(start, i - start);  // hand-lowered shape...
token = text[start..i];                     // ...raised to the range indexer
```

Because the two calls share a method, the raise fires only on the spilled shape,
and declines otherwise:

- **Two-bound from-start only, spill-anchored.** The hidden start temp must be a
  compiler-generated `int` (no source name), stored once and read exactly twice
  (the range start and the `end - start` length), with no address-of — so
  detaching the spill drops no live reference.
- **Any consuming statement position.** The slice need not be the return value:
  return, local assignment (the `EscapeReservedKeywordIdentifiers` witness
  `token = text[start..i]`), and call-argument positions all qualify, provided
  the receiver and everything the statement evaluates before the call are
  side-effect free (the start re-spills at the slice site on recompile, so it
  must not move past an observable effect). The receiver guard also excludes a
  directly effectful receiver such as `Effect().Substring(V, end - V)`: the
  compiler always spills an effectful receiver ahead of the start, so that shape
  is a hand-written call, not a range lowering, and raising it would reorder the
  receiver past the start.
- **Declines one-sided forms.** `Substring(start)` (`s[start..]`) and
  `Substring(0, end)` (`s[..end]`) carry no start spill, so they are
  indistinguishable from hand-written slicing and stay as calls — a class-2
  fidelity concern. The from-end open form (`s[^n..]`), which the compiler spills
  distinctly, is recovered.

## Style lenses (behavior-faithful, byte-divergent)

The knobs above are all **byte-preserving**: every one selects a spelling that
recompiles to the identical IL (a class-3 no-anchor choice) or changes only
whitespace, so the default and the knob-on output are members of the same
compile-back equivalence class. A **style lens** is a different, explicitly
opt-in contract (#3138): it trades byte fidelity for a source-style preference
the oracle endorses but the fidelity-first default cannot take. A lens is
**behavior-preserving** — its output computes the identical result for every
input — but **not** opcode-faithful, so its output must never feed the
compile-back fidelity gates, and it is off by default.

The first lens is `PrinterOptions.PreferConditionalExpressionReturn`
(`dotnet_style_prefer_conditional_expression_over_return`, IDE0046). A guarded
boolean return the default leaves flat because no short-circuit fold of it is
opcode-faithful (see the short-circuit fidelity guard, #3114) is re-rendered as
the conditional expression:

```csharp
// shipped default (byte-faithful — the flat guard is what recompiles exactly):
if (a & b) { return false; }
return c;
// with PreferConditionalExpressionReturn (behavior-faithful, byte-divergent):
return a & b ? false : c;
```

The ternary is the *canonical* desugaring of the guard — same condition, same
arms, same evaluation order — so the rewrite is unconditionally
behavior-preserving; that is what lets the lens re-offer a fold the default had
to decline. It deliberately stops at IDE0046: the further `c ? true : d` → `c ||
d` collapse (IDE0075) is a separate future knob, so a literal-arm ternary such as
`a ? true : b` is kept as written rather than simplified. The lens runs only on
the opt-in raised path, after the default pipeline.

The second lens is `PrinterOptions.PreferBranchlessBoolean`
(`dotnet_inspect_style_prefer_branchless_boolean`). It targets the *same* declined
guarded return, but renders the compact short-circuit "bool hack" — the exact form
the default's short-circuit fold produced before #3114 guarded it — instead of the
ternary:

```csharp
// shipped default (byte-faithful — the flat guard is what recompiles exactly):
if (a) { return false; }
return b;
// with PreferBranchlessBoolean (behavior-faithful, byte-divergent):
return !a && b;
```

The four constant-arm shapes fold to `a && b`, `!a || b`, `a || b`, and `!a && b`
respectively. Unlike the ternary, this form is **not oracle-endorsed** —
dotnet/runtime's `.editorconfig` would never recommend it — so it is a user
*compactness/branchless* preference, opt-in only, and it is **never** part of a
"full taste" aggregate. It is exposed under a tool-owned
`dotnet_inspect_style_*` key rather than a `dotnet_style_*` key to make that
distinction explicit. Two hazards stay declined because they are about *behavior*,
not just bytes: a user-defined-truthiness condition and a managed by-ref surviving
operand (csc's branchless lowering would eagerly dereference a location the branch
had guarded). The lens declines *every* user-defined-truthiness condition
wholesale — anywhere in the condition subtree, including one wrapped in a negation.
Such a condition never yields the compact bool-hack this lens targets: lifting it
is either invalid (the printer strips `op_True`/`op_False` to a bare user-typed
receiver, so `t && b` fails to compile), behavior-divergent (were a user `&`/`|`
present, it would rebind to that operator's semantics), or valid but not branchless
(a negation spelled as the ternary `(t ? false : true)` re-embeds a branch).
Over-declining is always valid and faithful. When both lenses are enabled the
oracle-endorsed ternary wins the shared shape.

### Lenses and the Annotated view

The Annotated view renders the same member as Decompiled Source, so it applies
the same resolved style options: a member must not be spelled `this._count` in
one section and `_count` in the other. For every byte-preserving knob this is
free — the knob changes only the C# spelling, so the interleaved IL beneath each
statement is byte-identical to the default render, and the view demonstrates the
knob's contract rather than obscuring it.

A style lens cannot keep that bargain. Its render no longer reproduces the
member's opcodes, so anchoring the raw IL beneath it would assert a
statement-to-opcode correspondence that does not hold — the single claim this
view exists to make. When a lens **actually rewrites** (it records a
`style-lens.*` decision under the `style-lens` category), the Annotated view
therefore drops the interleaved IL for that member.

Suppression keys on what the render *did*, not on what the host *asked for*: a
lens that is enabled but finds no shape to rewrite is byte-faithful for that
member, so its IL stays. The fact-comment overlay also stays, because a fact is
a property of the member, not a claim about which opcodes a printed statement
reproduces.

### Reading applied taste in the Annotated view

The applied knobs travel on the result as typed decisions, and the Annotated view
renders them as a single trailing side comment on the member's signature — the
same shape the fact overlay uses for analysis, so style and analysis read alike:

```csharp
public object? Pick(bool useName, string suffix)  // taste.qualify-property-access(Name); taste.qualify-field-access(_count)
{
    if (useName)
        // IL_0000: ldarg.1                                              // arg: useName; stack: [bool]
        // IL_0001: brfalse.s    IL_0009                                 // stack: []
    {
        return new object();  // alloc.new(object; alloc=System.Object; path=branch; path-confidence=behind-branch; post-dominance=return-post-dominates; escape=escapes; escape-kind=escapes-return; multiplicity=conditional)
            // IL_0003: newobj       object::.ctor()  // alloc.new(object; alloc=System.Object; path=branch; path-confidence=behind-branch; post-dominance=return-post-dominates; escape=escapes; escape-kind=escapes-return; multiplicity=conditional); stack: [object]
            // IL_0008: ret                                                  // stack: []
    }
    return string.Concat(this.Name, suffix, this._count.ToString());
        // IL_0009: ldarg.0                                              // arg: this; stack: [Cache]
        // IL_000A: call         Cache::get_Name()                       // stack: [string]
        // IL_000F: ldarg.2                                              // arg: suffix; stack: [string, string]
        // IL_0010: ldarg.0                                              // arg: this; stack: [string, string, Cache]
        // IL_0011: ldflda       int Cache::_count                       // stack: [string, string, ref int]
        // IL_0016: call         int::ToString()                         // stack: [string, string, string]
        // IL_001B: call         string::Concat(string, string, string)  // stack: [string]
        // IL_0020: ret                                                  // stack: []
}
```

Both knobs above are byte-preserving, so the IL is byte-identical to the default
render. A byte-divergent lens reports its fidelity instead of its subject (the
subject is the enclosing method, already spelled on that line), and that is also
the signal explaining the absent IL:

```csharp
public bool Allow(bool trusted, bool cached)  // taste.qualify-property-access(Name); taste.qualify-field-access(_count); taste.prefer-conditional-return(fidelity=byte-divergent)
{
    return trusted ? cached : (this.Name.Length > this._count);
}
```

The comment is anchored to the signature rather than to a statement because a
style decision carries a subject but no IL offset. `-S "Applied Taste"` remains
the full account, with each rule's detail and fidelity as table rows.

Nothing is annotated by default: no style decision is recorded unless a knob was
requested, so the comment appears exactly when the reader asked for taste.
`--taste` is the one-invocation gesture for requesting the whole oracle-endorsed
set (the flag form of `dotnet_inspect_style_full_taste`); it folds on top of any
resolved config, so a per-knob line still refines it.

The lowered Annotated stage applies the byte-preserving knobs but never runs the
lenses at all: they are raised-altitude sugar, and the lowered pipeline exists to
show the shape beneath that sugar.

## Names

Without a PDB, locals are slot names (`V_0`, `S_0`) shared with the Annotated IL view — the two views stay name-aligned by construction. With a PDB, source names are used. Synthesizing readable names (`size`, `array`, `item`) where no PDB exists is an open design question: it is the largest remaining cosmetic gap against source, but it would break view alignment unless opt-in.

## Style configuration

The oracle settles a single shipped default per equivalence class, but a few
class-3 no-anchor spellings are also exposed as opt-in knobs (see
[`this` member qualification](#this-member-qualification) and
[Line wrapping](#line-wrapping)), as are the byte-divergent
[style lenses](#style-lenses-behavior-faithful-byte-divergent). A tool-owned config
file selects them without a per-run flag.

`dotnet-inspect` discovers a `.dotnet-inspectconfig` file by walking up from the
current working directory to the filesystem root; the **nearest** file wins (no
cross-level merge). Because there is no merge, the nearest file is a hard
boundary — nothing above it is read — so placing a `.dotnet-inspectconfig` at a
repository root isolates every nested run from configs higher up the tree. When
no file is found, output is byte-for-byte the shipped default.

The file is flat `key = value`, using the same key and value vocabulary as an
`.editorconfig` so lines copy across directly:

```ini
# .dotnet-inspectconfig
root = true
dotnet_style_qualification_for_field = true
dotnet_style_qualification_for_property = true
dotnet_style_qualification_for_method = true
dotnet_style_qualification_for_event = true
dotnet_style_prefer_conditional_expression_over_return = true
```

- `#` and `;` comment lines and `[section]` headers are ignored.
- An editorconfig `value:severity` suffix is tolerated — only the value token
  before `:` is read (`true:suggestion` is read as `true`).
- The editorconfig `root` key is recognized (so a file copied from an
  `.editorconfig` does not warn). Discovery already stops at the nearest file, so
  `root = true` drives no behavior on its own; it is the conventional, explicit
  way to mark a repository-root config as the boundary.
- Recognized keys map to `PrinterOptions`: the four `this`-qualification keys
  above (field, property, method, event — byte-preserving class-3 spellings),
  `dotnet_style_prefer_conditional_expression_over_return` (the oracle-endorsed
  ternary [style lens](#style-lenses-behavior-faithful-byte-divergent)), and
  `dotnet_inspect_style_prefer_branchless_boolean` (the non-oracle-endorsed
  branchless lens, under a tool-owned key). The set grows as more knobs ship.
- `dotnet_inspect_style_full_taste = true` is a tool-owned **aggregate** key: it
  enables the whole oracle-endorsed subset at once (the four `this`-qualifications
  and the ternary lens — everything the runtime `.editorconfig`/IDE oracle
  prefers) so a user need not copy each `dotnet_style_*` line. It deliberately
  excludes the non-endorsed branchless lens, so the guarded-boolean-return
  axis resolves to the ternary deterministically. It applies in file
  order like any other key, so a later explicit per-knob line overrides it
  (last-write-wins) — `full_taste = true` then
  `dotnet_style_qualification_for_field = false` is "full taste minus one knob".
- `--taste` on `member` and `type` is the same aggregate as a one-invocation
  gesture, for asking a single question without leaving a config file behind. It
  folds on top of the resolved config rather than replacing it, so a config that
  already refines a knob keeps that refinement. Because the aggregate includes
  the ternary lens, the Annotated view drops its interleaved IL for any member
  that lens actually rewrites.
- The recognized keys are not hand-maintained in the resolver: they come from the
  library-owned `StyleOptionCatalog` (see [Option catalog](#option-catalog)), so
  the CLI vocabulary and the option surface cannot drift.
- Unknown keys, malformed lines, and non-boolean values are reported as a
  `Warning:` on stderr and skipped — the rest of the file still applies. A bad
  config never fails the run silently.

Config warnings are emitted at the point styled decompiled source is actually
shown, exactly once: a decompiled-source or source-diff render reads the resolved
`PrinterOptions` and flushes any pending warnings to stderr there. Every other run
stays silent, and the rule is exact — the config is *consumed* precisely when its
styling is user-visible:

- A metadata projection (`--json`, `--count`, tabular, `--value`/`--urls`) returns
  before any source render.
- A selection that excludes source (`-S Facts`) renders no source.
- A fidelity-only projection (`-S "Fidelity Causes"`) reads the raised IR and
  recompile diagnostics, both style-invariant, and discards the printed string, so
  it renders with the shipped defaults and genuinely does not consume the config.
- Discovery (`-D`/`--discover`) lists which sections *would* render by probing them
  into a discarded view; that internal render is not user-visible styled source, so
  a discovery request never surfaces a config warning.
- A request that *selects* source but yields none renders nothing to style: a
  callers-only aggregation (`--directory`/`--bin` with no overload selector), a
  member with no IL body (e.g. a P/Invoke, which renders only a decode
  diagnostic), and an empty type whose whole-type projection has no body all
  request `Decompiled Source` yet produce no printed C#, so no warning fires.

Because emission is tied to the point of visible consumption rather than predicted
up front, the warning fires if and only if styled source reaches the user, exactly
once, independent of output mode or verbosity.

Only the tool-owned filename is auto-discovered; a foreign `.editorconfig` is not
read. Configuration is resolved once at the CLI edge and threaded into the render
as explicit `PrinterOptions`; the decompiler library itself stays a pure function
of the assembly and the options it is handed, so the config surface never changes
what the library computes for a given `PrinterOptions`. The knobs affect the
primary decompiled-source view; the Annotated Source and IR-stage views stay on
the shipped default so they remain aligned with the IL.

### Option catalog

The recognized knobs are described once, in the library, by
`StyleOptionCatalog` (`ILInspector.Decompiler.Pipeline`). Each
`StyleOptionDescriptor` carries a knob's stable id, human-facing title and
summary, its tier (`Formatting`, `Spelling`, `Lens`, or `Synthesis`), whether it
is `ByteDivergent`, and the **value domain** it ranges over — its ordered list of
`StyleOptionValue`s. Most knobs are two-state (a `false`/`true` domain); the
guarded-boolean-return knob is a single multi-value axis
(`flat` / `conditional-expression` / `branchless`). Each `StyleOptionValue`
carries its stable token, optional title, whether that value is `OracleEndorsed`
(declared) and `CorpusEndorsed` (revealed),
its `.dotnet-inspectconfig` key (`null` for the off/default value and for API-only
formatting/synthesis knobs), and NativeAOT-safe `IsSelected`/`SetSelected`
delegates that read and drive that value's backing state without reflection. The
descriptor reads and single-selects the whole axis through
`GetValue`/`WithValue`.

`OracleEndorsed` records **declared**-oracle endorsement specifically — the value
has a `.editorconfig` rule behind it. `CorpusEndorsed` records **revealed**
endorsement — the runtime's own source corpus reveals a dominant practice for the
value (typically where the declared oracle is silent, though the two facets are
independent). The two flags are independent (a value may be endorsed by both
facets, one, or neither), and each `CorpusEndorsed = true` is a deliberate,
documented judgment, never a silently-inferred or measured-heat claim. Today
exactly one knob is revealed-endorsed — `wrap-splittable-expressions`, because
runtime code wraps long boolean chains in line with its 120-column practice. The
other formatting/synthesis knobs are left un-endorsed on both axes: wrapping the
expression-body arrow actually *diverges* from the corpus (the runtime keeps `=>`
on the same line, which the shipped default already does), suppressing the
always-on width wrappers (`disable-one-liner-wrapping`) diverges too (the runtime
wraps wide constructs, which the shipped default also does), and a synthesized
local name is our own invention, not a corpus spelling. The catalog exposes the
revealed subset as `CorpusEndorsedOptions`; a future "house style" aggregate would
fold declared ∪ revealed while "full taste" stays declared-only
([#3179](https://github.com/richlander/dotnet-inspect/issues/3179)).

This makes the option surface discoverable and drift-proof for every host, not
just the CLI: the config resolver derives its recognized keys from every value's
`ConfigKey`, a Wasm UI can enumerate the knobs (presenting each axis's `Values` as
a mutually-exclusive choice and driving it through `GetValue`/`WithValue`), and the
"full taste" aggregate is exactly the endorsed value of each participating axis.
The catalog exposes the participating knobs as `OracleEndorsedOptions` and applies
the aggregate with `ApplyFullTaste(PrinterOptions, enabled)` — selecting each
axis's `EndorsedValue`; the CLI surfaces it as the
`dotnet_inspect_style_full_taste` config key. Because a multi-value axis is
single-select by construction — `GetValue` reports the first selected value in
`Values` order, so the oracle-endorsed `conditional-expression` wins over
`branchless` when both are set, exactly as the printer resolves it — the aggregate
selects the ternary and the axis never resolves ambiguously.

Every opt-in knob's value domain is carried by the descriptor directly: the
two-state knobs (including the expression-body arrow wrap, `WrapExpressionBodyArrow`)
have a `false`/`true` domain, and the guarded-boolean-return family is one
three-token axis rather than two coupled booleans. The catalog is exhaustive: a
test-only drift guard asserts every backing `PrinterOptions` boolean is reachable
through some descriptor value, so a new knob cannot land without a catalog entry.

The `var`-spelling family is a second multi-value axis, modeled as one
`var-spelling-style` descriptor with four tokens — `explicit` (the default) plus
the three site categories `var-for-built-in-types`, `var-when-type-apparent`, and
`var-elsewhere`, one per editorconfig key (`csharp_style_var_for_built_in_types`,
`csharp_style_var_when_type_is_apparent`, `csharp_style_var_elsewhere`). Because a
declaration site falls into exactly one category, the three category values back
*independent* booleans (a host may enable any subset), while the axis stays
single-select for display: `GetValue` reports the coarse first-enabled category
and `WithValue` selects one exclusively. `var` is byte-neutral — it erases the
declared type spelling but never changes the IL — so the family sits in the
`Spelling` tier with `ByteDivergent = false`. Every category value is
`OracleEndorsed = false` and `CorpusEndorsed = false`: dotnet/runtime's
`.editorconfig` sets all three `csharp_style_var_*` keys to `false` (prefer
explicit types), so no `var` value is endorsed by either facet. The family is
therefore **opt-in only** and never part of the "full taste" aggregate — the
shipped default spells explicit types everywhere, matching the runtime.

## Verification and soundness

How these rendering choices are held correct — the verification checks and floors, the
fixture gate, and the soundness checklist every IR-mutating pass answers — lives
in [decompiler-quality.md](decompiler-quality.md). A proposed rendering change
should arrive with its evidence per that doc: the IL shape it targets, the
argument for its class under the three-class rule above, a fixture covering both
configurations, and a `--pass-impact` blast-radius read.
