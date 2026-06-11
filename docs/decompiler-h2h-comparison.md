# Decompiler vs Source: Head-to-Head Comparison

Comparison of `dotnet-inspect` decompiler output against the actual C# source from [dotnet/runtime](https://github.com/dotnet/runtime). All methods decompiled from the installed .NET 10 runtime assemblies (`System.Private.CoreLib`, `System.Collections`), which are Release builds — the IL shapes below are what the tool sees in the field.

Snapshot: June 2026. Previous snapshot graded a **B** average; the gap items it listed (field compound assignment, `&&`/`||` reconstruction, `goto`-to-`continue`, `ref` arguments, bool return context) have all since been closed.

---

## Exact and Near-Exact Matches

### `String.IsNullOrEmpty`

**Source (dotnet/runtime):**

```csharp
public static bool IsNullOrEmpty(string? value)
{
    return value == null || value.Length == 0;
}
```

**Decompiled:**

```csharp
return value == null || value.Length == 0;
```

**Verdict:** Exact. The `||` short-circuit is reconstructed from the branch chain. **Grade: A**

---

### `Math.Max` / `Math.Min` (ulong)

**Source:**

```csharp
return (val1 >= val2) ? val1 : val2;
```

**Decompiled:**

```csharp
return val1 >= val2 ? val1 : val2;
```

**Verdict:** Exact (modulo optional parens). The ternary is reconstructed, and no spurious `(ulong)` casts appear — the emitter knows the operands are already unsigned, so the IL's `bge.un` renders as a plain comparison. `Min` is identical with `<=`. **Grade: A**

---

### `Math.Clamp`

**Source:**

```csharp
if (min > max)
    ThrowMinMaxException(min, max);
if (value < min)
    return min;
else if (value > max)
    return max;
return value;
```

**Decompiled:**

```csharp
if (min > max)
{
    Math.ThrowMinMaxException(min, max);
}
if (value < min)
{
    return min;
}
return !(value > max) ? value : max;
```

**Verdict:** Semantically exact, including NaN behavior — `!(value > max)` is the precise rendering of the IL's unordered branch, not the (incorrect for NaN) `value <= max`. The `else if` + tail return is composed into a ternary. **Grade: A-**

---

### `List<T>.Contains`

**Source:**

```csharp
return _size != 0 && IndexOf(item) >= 0;
```

**Decompiled:**

```csharp
return this._size != 0 && base.IndexOf(item) >= 0;
```

**Verdict:** Exact. The `&&` chain is reconstructed. `this.`/`base.` prefixes are cosmetic (`base.` because the call binds through `callvirt`). **Grade: A**

---

### `Dictionary<K,V>.Clear`

**Source:**

```csharp
int count = _count;
if (count > 0)
{
    Array.Clear(_buckets);
    _count = 0;
    _freeList = -1;
    _freeCount = 0;
    Array.Clear(_entries, 0, count);
}
```

**Decompiled:**

```csharp
int V_0 = this._count;
if (V_0 > 0)
{
    Array.Clear(this._buckets);
    this._count = 0;
    this._freeList = -1;
    this._freeCount = 0;
    Array.Clear(this._entries, 0, V_0);
}
return;
```

**Verdict:** Exact, statement for statement. Only `V_0` vs `count` (no PDB for release CoreLib) and the trailing `return;` differ. **Grade: A**

---

### `Queue<T>.Enqueue`

**Source:**

```csharp
if (_size == _array.Length)
    Grow(_size + 1);
_array[_tail] = item;
MoveNext(ref _tail);
_size++;
_version++;
```

**Decompiled:**

```csharp
if (this._size == this._array.Length)
{
    base.Grow(this._size + 1);
}
this._array[this._tail] = item;
base.MoveNext(ref this._tail);
this._size++;
this._version++;
return;
```

**Verdict:** Exact. Field compound assignment (`_size++`) and the `ref` argument are both reconstructed — the two gaps this method exposed in the previous snapshot. **Grade: A**

---

### `HashSet<T>.Contains` / `StringBuilder.Clear`

Both are one-liners and match exactly:

| Method | Source | Decompiled | Grade |
| --- | --- | --- | --- |
| `HashSet.Contains` | `return FindItemIndex(item) >= 0;` | `return base.FindItemIndex(item) >= 0;` | **A** |
| `StringBuilder.Clear` | `Length = 0; return this;` | `base.Length = 0; return this;` | **A** |

---

### `String.IsNullOrWhiteSpace`

**Source:**

```csharp
if (value == null) return true;
for (int i = 0; i < value.Length; i++)
{
    if (!char.IsWhiteSpace(value[i]))
        return false;
}
return true;
```

**Decompiled:**

```csharp
int V_0;

if (value == null)
{
    return true;
}
for (V_0 = 0; V_0 < value.Length; V_0++)
{
    if (!char.IsWhiteSpace(value[V_0]))
    {
        return false;
    }
}
return true;
```

**Verdict:** Source-exact shape — loop structure, negated condition, and return polarity all match (the previous snapshot inverted the logic and leaked a `goto`). Only the hoisted `int V_0;` declaration differs, because the local's declaration point isn't recoverable without a PDB. **Grade: A-**

---

### `String.Contains(string)`

**Source:**

```csharp
if (value == null)
    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.value);
if (RuntimeHelpers.IsKnownConstant(value) && value.Length == 1)
{
    return Contains(value[0]);
}
return SpanHelpers.IndexOf(
    ref _firstChar, Length,
    ref value._firstChar, value.Length) >= 0;
```

**Decompiled:**

```csharp
if (value == null)
{
    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.value);
}
return Runtime.CompilerServices.RuntimeHelpers.IsKnownConstant(value) && value.Length == 1 ? base.Contains(value[0]) : SpanHelpers.IndexOf(ref this._firstChar, base.Length, ref value._firstChar, value.Length) >= 0;
```

**Verdict:** The `&&` condition is reconstructed exactly (previously split into nested ifs with a `goto`), the enum argument renders as `ExceptionArgument.value` instead of a bare `7`, and `ref` arguments are preserved. The if/return pair folds into one long ternary — correct, though the source's statement form reads better. **Grade: A-**

---

### `Stack<T>.Push`

**Source:**

```csharp
int size = _size;
T[] array = _array;
if ((uint)size < (uint)array.Length)
{
    array[size] = item;
    _version++;
    _size = size + 1;
}
else
{
    PushWithResize(item);
}
```

**Decompiled:**

```csharp
int V_0 = this._size;
T[] V_1 = this._array;
if ((uint)V_0 >= (uint)V_1.Length)
{
    base.PushWithResize(item);
    return;
}
V_1[V_0] = item;
this._version++;
this._size = V_0 + 1;
return;
```

**Verdict:** Semantically exact, and the `(uint)` bounds-check casts are preserved (they are load-bearing here — the operands are signed). The if/else is rendered as a guard clause with early return: the compiler emits the rare branch first, and the decompiler follows the IL order. **Grade: A-**

---

## Structural Differences

### `Math.Abs(short)`

**Source:**

```csharp
if (value < 0)
{
    value = (short)-value;
    if (value < 0)
        ThrowNegateTwosCompOverflow();
}
return value;
```

**Decompiled:**

```csharp
if (value < 0)
{
    value = (short)-value;
    if (value >= 0) goto IL_0012;
}
Math.ThrowNegateTwosCompOverflow();
return value;
```

**Verdict:** All statements present and the semantics are right, but the nested early-exit shape isn't re-nested: the throw is hoisted out and reached by fallthrough, with a `goto` skipping it. Worse, the `IL_0012` target label is not emitted, so the `goto` dangles as written. **Grade: B**

**Gap: nested-if early-exit re-nesting; `goto` targets must always render a label.**

---

### `List<T>.Clear`

**Source:**

```csharp
_version++;
if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
{
    int size = _size;
    _size = 0;
    if (size > 0)
        Array.Clear(_items, 0, size);
}
else
{
    _size = 0;
}
```

**Decompiled:**

```csharp
int V_0;

this._version++;
if (Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences())
{
    V_0 = this._size;
    this._size = 0;
    if (V_0 <= 0) goto IL_003C;
}
else
{
    this._size = 0;
}
Array.Clear(this._items, 0, V_0);
return;
return;
```

**Verdict:** The statements are all preserved and `_version++` now renders as a compound assignment, but the control flow is misleading: as written, the `else` arm falls through into `Array.Clear`, the `goto IL_003C` has no rendered label, and there is a double `return;`. The inner `if (size > 0) Array.Clear(...)` should be re-nested. **Grade: B-**

**Gap: re-nesting code after an inverted guard; unlabeled `goto`; duplicate trailing returns.**

---

## Major Structural Gaps

### `Stack<T>.Pop` / `Queue<T>.Dequeue`

**Source (Pop):**

```csharp
int size = _size - 1;
T[] array = _array;
if ((uint)size >= (uint)array.Length)
    ThrowForEmptyStack();
_version++;
_size = size;
T item = array[size];
if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
    array[size] = default!;
return item;
```

**Decompiled (Pop):**

```csharp
T V_2;

int V_0 = this._size - 1;
T[] V_1 = this._array;
if ((uint)V_0 >= (uint)V_1.Length)
{
    base.ThrowForEmptyStack();
}
this._version++;
this._size = V_0;
/* ldelem(ldloc.1 V_1, ldloc.0 V_0) */
if (Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences())
{
    V_2 = default;
    V_1[V_0] = V_2;
    S_0 = S_in_0;
}
return S_in_0;
```

**Verdict:** Everything is exact until `T item = array[size]`: the generic `ldelem` (type-parameter element load) falls back to an IL comment, and the value it should have produced leaks through as `S_0`/`S_in_0` stack-machine artifacts, ending in an opaque `return S_in_0`. `Queue.Dequeue` fails identically on the same pattern. This is now the single largest gap in the corpus. **Grade: D+**

**Gap: generic `ldelem` expression rendering; stack-value names must never reach output.**

---

### `Dictionary<K,V>.ContainsValue`

**Source:**

```csharp
Entry[]? entries = _entries;
if (value == null) { /* null-compare loop */ }
else if (typeof(TValue).IsValueType) { /* devirtualized loop */ }
else { /* cached-comparer loop */ }
return false;
```

**Decompiled (abbreviated):**

```csharp
Entry<TKey, TValue>[] V_0 = this._entries;
if ((object)value != null)
{
    if (Type.GetTypeFromHandle(typeof(TValue)).IsValueType)
    {
        V_2 = 0;
        if (ref V_0[V_2].next >= -1) { ... }
        V_2++;
        if (V_2 < this._count) goto IL_005E;
        return false;
    }
}
else
{
    V_1 = 0;
}
for (; V_1 < this._count; V_1++) { ... }
return false;
for (; V_2 < this._count; V_2++)
{
}
V_3 = EqualityComparer<T1>.Default;
for (V_4 = 0; V_4 < this._count; V_4++) { ... }
```

**Verdict:** Three independent loop paths sharing a return is still beyond the structuring pass: one loop is unrolled into if+goto (with the `IL_005E` label never emitted), another renders empty, and unreachable code trails the first `return false`. Expression-level artifacts compound it: `ref V_0[V_2].next` for a field-of-ref access (not valid C#), `Type.GetTypeFromHandle(typeof(TValue))` instead of `typeof(TValue)`, and `EqualityComparer<T1>` mis-naming the type parameter. The bool returns themselves are now correct (`return false`, not `return 0`). **Grade: D+**

**Gap: multi-path loop structuring; `ref` element/field access rendering; `typeof().IsValueType` idiom; generic parameter naming through member refs.**

---

## Summary Scorecard

| Method | Grade | Key Differences |
| --- | --- | --- |
| `String.IsNullOrEmpty` | **A** | exact |
| `Math.Max` | **A** | exact |
| `Math.Min` | **A** | exact |
| `List.Contains` | **A** | exact (`this.`/`base.` cosmetic) |
| `Dictionary.Clear` | **A** | exact (`V_0` naming) |
| `HashSet.Contains` | **A** | exact |
| `StringBuilder.Clear` | **A** | exact |
| `Queue.Enqueue` | **A** | exact |
| `Math.Clamp` | **A-** | else-if tail folded into ternary |
| `String.IsNullOrWhiteSpace` | **A-** | hoisted local declaration |
| `String.Contains` | **A-** | if/return folded into long ternary |
| `Stack.Push` | **A-** | guard-clause inversion of if/else |
| `Math.Abs` | **B** | un-nested early exit, unlabeled `goto` |
| `List.Clear` | **B-** | unlabeled `goto`, misleading fallthrough, double return |
| `Stack.Pop` | **D+** | generic `ldelem` comment, `S_in_0` leak |
| `Queue.Dequeue` | **D+** | generic `ldelem` comment, `S_in_0` leak |
| `Dictionary.ContainsValue` | **D+** | multi-path loops flattened, `ref` artifacts |

### Overall: **B+** average across 17 methods (12 of 17 at A-/A; previous snapshot: B average, 8 of 17)

**Closed since the previous snapshot:**

- `&&` / `||` short-circuit reconstruction (`IsNullOrEmpty`, `List.Contains`, `String.Contains` now exact)
- Ternary composition (`Max`, `Min`, `Clamp`)
- Field compound assignment (`_size++`, `_version++`)
- `ref` argument rendering (`MoveNext(ref this._tail)`)
- Loop `continue`/`break` and condition polarity (`IsNullOrWhiteSpace` now source-shaped)
- Bool return context (`return false`, not `return 0`)
- Enum argument names (`ExceptionArgument.value`, not `7`)
- Redundant unsigned casts dropped when operands are unsigned-typed; kept for the `(uint)i < (uint)length` idiom
- Statement preservation: condition-chain absorption and cross-block inlining no longer drop statements (`Math.Abs`, `Dictionary.Clear`)

**Top remaining gaps:**

1. **Generic `ldelem` rendering** (`Stack.Pop`, `Queue.Dequeue`) — element loads of type-parameter type fall back to an IL comment and leak `S_in_0` stack names — high impact
2. **Unlabeled `goto` targets** (`Abs`, `List.Clear`, `ContainsValue`) — every emitted `goto` must have a rendered label, or the output is not even syntactically honest — small fix
3. **Re-nesting inverted guards** (`List.Clear`, `Abs`) — code after `if (x <= 0) goto END;` belongs inside `if (x > 0) { ... }`
4. **Multi-path loop structuring** (`ContainsValue`) — parallel loops sharing exits unravel into goto soup
5. **`ref` element/field access** (`ContainsValue`) — `ref V_0[V_2].next` should render as a plain member access
6. **Duplicate trailing `return;`** — cosmetic, easy
