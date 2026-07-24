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
get_Prop` either way, so the `this.` prefix has no IL consequence. This is a
class-3 no-anchor spelling with **no binding hazard** (unlike target-typed
`new()` or argument elision): the prefix cannot rebind the member, it can only be
redundant. The shipped default renders the bare name, qualifying only where the
bare name would not bind to the member — a local/parameter shadow or a
member/type-name collision — matching how the runtime writes member access.

The always-qualified spelling is available as an opt-in, off by default so
default output stays byte-for-byte stable:

- `PrinterOptions.QualifyFieldAccess` mirrors `dotnet_style_qualification_for_field`.
- `PrinterOptions.QualifyPropertyAccess` mirrors `dotnet_style_qualification_for_property`.

```csharp
public int Compute() => _count + Extra;          // shipped default: bare
public int Compute() => this._count + this.Extra; // both knobs on
```

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
*multi-line* single expression folds too: a wrapped switch return (issue #3088)
or any other wrapped single `return <expr>;`, such as a fluent chain
(issue #3084). The arrow trails the signature line with the expression's opening
token after it, and the continuation lines re-indent one level under the
member — the natural multi-line extension of the single-line `head => expr;`
form.

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
```

The fold is gated on a typed structural signal the printer proves from the
emitted statement tree — the body is exactly one top-level `return` with no
lifted declarations, label, constructor chain, field initializer, async
modifier, or unsupported fallback — never a re-parse of the rendered text. A
member with any statement preceding the return keeps its brace block.

### Line wrapping

Breaking a long line across continuation lines is pure whitespace: it emits the
same tokens in the same order, so the wrapped and inline forms recompile to
identical IL and it selects the *same* member of the equivalence class — the
representative is unchanged, only its layout differs. Wrapping therefore sits below the three-class rule, alongside
brace style, with a single input from the oracle: `dotnet/runtime`'s 120-column
maximum line width decides *when* a single-line rendering is too wide to keep.

Two chain shapes wrap one element per continuation line when the flat form would
exceed that width and the chain has at least two elements:

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
churning every wide boolean `return` in the corpus. The always-on fluent wrapper
predates it and stays on.

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
the opt-in raised path, after the default pipeline, and the IL-anchored Annotated
view never applies it (it must stay byte-faithful for line/IL alignment).

## Names

Without a PDB, locals are slot names (`V_0`, `S_0`) shared with the Annotated IL view — the two views stay name-aligned by construction. With a PDB, source names are used. Synthesizing readable names (`size`, `array`, `item`) where no PDB exists is an open design question: it is the largest remaining cosmetic gap against source, but it would break view alignment unless opt-in.

## Style configuration

The oracle settles a single shipped default per equivalence class, but a few
class-3 no-anchor spellings are also exposed as opt-in knobs (see
[`this` member qualification](#this-member-qualification) and
[Line wrapping](#line-wrapping)), as is the first byte-divergent
[style lens](#style-lenses-behavior-faithful-byte-divergent). A tool-owned config
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
dotnet_style_prefer_conditional_expression_over_return = true
```

- `#` and `;` comment lines and `[section]` headers are ignored.
- An editorconfig `value:severity` suffix is tolerated — only the value token
  before `:` is read (`true:suggestion` is read as `true`).
- The editorconfig `root` key is recognized (so a file copied from an
  `.editorconfig` does not warn). Discovery already stops at the nearest file, so
  `root = true` drives no behavior on its own; it is the conventional, explicit
  way to mark a repository-root config as the boundary.
- Recognized keys map to `PrinterOptions`: the two `this`-qualification keys
  above (byte-preserving class-3 spellings) and
  `dotnet_style_prefer_conditional_expression_over_return` (the first
  byte-divergent [style lens](#style-lenses-behavior-faithful-byte-divergent)).
  The set grows as more knobs ship.
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

## Verification and soundness

How these rendering choices are held correct — the verification checks and floors, the
fixture gate, and the soundness checklist every IR-mutating pass answers — lives
in [decompiler-quality.md](decompiler-quality.md). A proposed rendering change
should arrive with its evidence per that doc: the IL shape it targets, the
argument for its class under the three-class rule above, a fixture covering both
configurations, and a `--pass-impact` blast-radius read.
