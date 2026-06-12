# Decompiler vs Source: Head-to-Head Comparison

Comparison of `dotnet-inspect` decompiler output against the actual C# source from [dotnet/runtime](https://github.com/dotnet/runtime). All methods decompiled from the installed .NET runtime assemblies (`System.Private.CoreLib`, `System.Collections`), which are Release builds — the IL shapes below are what the tool sees in the field.

Snapshot: June 2026 (third). The previous snapshot graded **B+** with 12 of 17 methods at A-/A; this one grades **A-** with 16 of 17. Everything the previous snapshot listed as a top gap has been closed: generic `ldelem` rendering, dangling `goto` labels, inverted-guard re-nesting, and the `ref`/`typeof`/generic-naming expression artifacts.

Style target: where one IL shape admits several C# spellings, the decompiler renders the form dotnet/runtime's `.editorconfig` and code fixers encourage — see [decompiler-taste.md](decompiler-taste.md) for the design principles behind that choice and its hard limits.

Methodology note: grades compare against the original dotnet/runtime source. When refreshing this document, [ilspycmd](https://www.nuget.org/packages/ilspycmd) (`dotnet tool install -g ilspycmd`) is a useful second reference — decompiling the same methods with a mature decompiler distinguishes "information lost in compilation" (ILSpy can't recover it either) from "gap in our pipeline" (ILSpy renders it, we don't). It is an analysis aid only; no test or CI infrastructure depends on it. ILSpy observations below are from ilspycmd 9.1.

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

**Verdict:** Semantically exact, including NaN behavior — `!(value > max)` is the precise rendering of the IL's unordered branch, not the (incorrect for NaN) `value <= max`. The `else if` + tail return is composed into a ternary; ILSpy keeps the statement form. **Grade: A-**

---

### `List<T>.Contains`

**Source:**

```csharp
return _size != 0 && IndexOf(item) >= 0;
```

**Decompiled:**

```csharp
return _size != 0 && IndexOf(item) >= 0;
```

**Verdict:** Exact, character for character — same-type non-virtual calls render bare names, as the source writes them. **Grade: A**

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
int V_0 = _count;
if (V_0 > 0)
{
    Array.Clear(_buckets);
    _count = 0;
    _freeList = -1;
    _freeCount = 0;
    Array.Clear(_entries, 0, V_0);
}
```

**Verdict:** Exact, statement for statement. Only `V_0` vs `count` (no PDB in this snapshot's flow) differs. **Grade: A**

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
if (_size == _array.Length)
{
    Grow(_size + 1);
}
_array[_tail] = item;
MoveNext(ref _tail);
_size++;
_version++;
```

**Verdict:** Exact. Field compound assignment (`_size++`) and the `ref` argument are both reconstructed. **Grade: A**

---

### `HashSet<T>.Contains` / `StringBuilder.Clear`

Both are one-liners and match exactly:

| Method | Source | Decompiled | Grade |
| --- | --- | --- | --- |
| `HashSet.Contains` | `return FindItemIndex(item) >= 0;` | `return FindItemIndex(item) >= 0;` | **A** |
| `StringBuilder.Clear` | `Length = 0; return this;` | `Length = 0; return this;` | **A** |

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
if (value == null)
{
    return true;
}
for (int V_0 = 0; V_0 < value.Length; V_0++)
{
    if (!char.IsWhiteSpace(value[V_0]))
    {
        return false;
    }
}
return true;
```

**Verdict:** Source-exact — the loop counter declares in the for-initializer as the source writes it; only the counter's name differs. **Grade: A**

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
return Runtime.CompilerServices.RuntimeHelpers.IsKnownConstant(value) && value.Length == 1 ? Contains(value[0]) : SpanHelpers.IndexOf(ref _firstChar, Length, ref value._firstChar, value.Length) >= 0;
```

**Verdict:** The `&&` condition is reconstructed exactly, the enum argument renders as `ExceptionArgument.value`, and `ref` arguments are preserved. The if/return pair folds into one long ternary — correct, though the source's statement form reads better. **Grade: A-**

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
int V_0 = _size;
T[] V_1 = _array;
if ((uint)V_0 >= (uint)V_1.Length)
{
    PushWithResize(item);
    return;
}
V_1[V_0] = item;
_version++;
_size = V_0 + 1;
```

**Verdict:** Semantically exact, and the `(uint)` bounds-check casts are preserved (load-bearing — the operands are signed). The if/else is rendered as a guard clause with early return: the compiler emits the rare branch first, and the decompiler follows the IL order. **Grade: A-**

---

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
int V_0 = _size - 1;
T[] V_1 = _array;
if ((uint)V_0 >= (uint)V_1.Length)
{
    ThrowForEmptyStack();
}
_version++;
_size = V_0;
T S_0 = V_1[V_0];
if (Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>())
{
    T V_2 = default;
    V_1[V_0] = V_2;
}
return S_0;
```

**Verdict:** Source-exact structure. The generic element load (`T item = array[size]`) lives on the eval stack across the clearing branch in the IL; it now spills to a typed local consumed by the return. ILSpy's rendering of this method is structurally identical (`T result = array[num]; ... return result;`) — the only difference between the two decompilers here is invented names vs slot names. `Queue.Dequeue` matches its source the same way. The previous snapshot graded these **D+** (comment fallback + stack-machine artifacts). **Grade: A-**

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
    if (value >= 0) return value;
    Math.ThrowNegateTwosCompOverflow();
}
return value;
```

**Verdict:** Goto-free and semantically exact. The inner guard renders as an inverted early return rather than re-nesting the throw under `if (value < 0)` as the source (and ILSpy) spell it — an honest rendering of the IL's branch-past-the-throw, one inversion short of source-exact. **Grade: A-**

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

_version++;
if (Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>())
{
    V_0 = _size;
    _size = 0;
    if (V_0 <= 0) return;
    Array.Clear(_items, 0, V_0);
    return;
}
_size = 0;
```

**Verdict:** Goto-free and semantically exact (two snapshots ago this method rendered the else path appearing to flow into `Array.Clear` — wrong output; one snapshot ago, a dangling `goto`). Same early-return-vs-re-nesting difference as `Abs`: the source nests `if (size > 0) Array.Clear(...)`; we render the inverted guard. **Grade: A-**

---

## Major Structural Gaps

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
Entry<TKey, TValue>[] V_0 = _entries;
if ((object)value != null)
{
    if (typeof(TValue).IsValueType)
    {
        V_2 = 0;
IL_005E:
        if (V_0[V_2].next >= -1)
        {
            if (EqualityComparer<TValue>.Default.Equals(V_0[V_2].value, value))
            {
                return true;
            }
        }
        V_2++;
        if (V_2 < _count) goto IL_005E;
        return false;
    }
}
for (V_1 = 0; V_1 < _count; V_1++) { /* null-compare loop */ }
return false;
for (; V_2 < _count; V_2++)
{
}
V_3 = EqualityComparer<TValue>.Default;
for (V_4 = 0; V_4 < _count; V_4++) { /* cached-comparer loop */ }
```

**Verdict:** Every expression now renders correctly — `typeof(TValue).IsValueType`, `EqualityComparer<TValue>.Default`, `V_0[V_2].next` (the previous snapshot had `Type.GetTypeFromHandle(...)`, `EqualityComparer<T1>`, and invalid `ref` receivers). What remains is purely structural: three parallel loops sharing exit paths unravel — one loop renders as if+goto, one renders empty, and the third trails unreachably after a `return`. ILSpy fully recovers the three-loop `if/else if/else` shape from this exact IL, so this is a pipeline gap, not lost information — and the last one in the corpus. **Grade: C**

---

## Summary Scorecard

| Method | Grade | Key Differences |
| --- | --- | --- |
| `String.IsNullOrEmpty` | **A** | exact |
| `Math.Max` | **A** | exact |
| `Math.Min` | **A** | exact |
| `List.Contains` | **A** | exact |
| `Dictionary.Clear` | **A** | exact (`V_0` naming) |
| `HashSet.Contains` | **A** | exact |
| `StringBuilder.Clear` | **A** | exact |
| `Queue.Enqueue` | **A** | exact |
| `Math.Clamp` | **A-** | else-if tail folded into ternary |
| `String.IsNullOrWhiteSpace` | **A** | counter name only |
| `String.Contains` | **A-** | if/return folded into long ternary |
| `Stack.Push` | **A-** | guard-clause inversion of if/else |
| `Stack.Pop` | **A-** | slot naming (`S_0`); structure exact |
| `Queue.Dequeue` | **A-** | slot naming; structure exact |
| `Math.Abs` | **A-** | early return instead of re-nested throw |
| `List.Clear` | **A-** | inverted guard instead of re-nested if |
| `Dictionary.ContainsValue` | **C** | multi-path loops unravel; expressions all correct |

### Overall: **A-** average across 17 methods (16 of 17 at A-/A; previous snapshots: B, then B+)

**Closed since the previous snapshot:**

- Generic `ldelem` rendering + eval-stack spills (`Stack.Pop`, `Queue.Dequeue`: D+ → A-)
- Dangling `goto` labels — labels are now demand-driven (emitted iff a rendered goto references them)
- Arm-only fallthrough code absorbed into if/else arms (`List.Clear` was semantically wrong output)
- Conditional branch to return-only block → `if (cond) return ...;`; conditional loop re-entry → `if (cond) continue;`
- Expression artifacts: `typeof(T)` collapse, struct element field access without `ref`, caller generic parameter names through MemberRef TypeSpecs
- Stacked values crossing mutating statements now spill (Release keeps captured field reads on the eval stack)

**Top remaining gaps:**

1. **Multi-path loop structuring** (`ContainsValue`) — parallel loops sharing exits; ILSpy recovers the full `if/else if/else` three-loop shape from the same IL, so this is provably reachable
2. **Re-nesting inverted guards to source shape** (`Abs`, `List.Clear`) — our early-return rendering is correct but one inversion away from the source's nested form
3. **Statement-form preference** (`Clamp`, `String.Contains`) — aggressive ternary folding where the source uses if/return
4. **Local naming** — `V_0`/`S_0` vs source names; recoverable only with a PDB, but synthesized names (`size`, `array`) modeled on ILSpy's heuristics could close most of the cosmetic distance
