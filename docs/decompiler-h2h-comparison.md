# Decompiler vs Source: Head-to-Head Comparison

Comparison of `dotnet-inspect` decompiler output against the actual C# source from [dotnet/runtime](https://github.com/dotnet/runtime). All methods decompiled from the installed .NET runtime assemblies (`System.Private.CoreLib`, `System.Collections`), which are Release builds — the IL shapes below are what the tool sees in the field.

Snapshot: June 2026 (fourth). Trajectory across snapshots: **B → B+ → A- → A**. Twelve of seventeen methods are now exact (or differ only in slot names like `V_0` where no PDB is in play); all seventeen grade A- or better. The third snapshot's gap list (multi-path loop structuring, guard re-nesting, statement-form preference) is closed except for naming residuals.

Style target: where one IL shape admits several C# spellings, the decompiler renders the form dotnet/runtime's `.editorconfig` and code fixers encourage — see [decompiler-taste.md](decompiler-taste.md) for the design principles behind that choice and its hard limits.

Methodology note: grades compare against the original dotnet/runtime source. When refreshing this document, [ilspycmd](https://www.nuget.org/packages/ilspycmd) (`dotnet tool install -g ilspycmd`) is a useful second reference — decompiling the same methods with a mature decompiler distinguishes "information lost in compilation" (ILSpy can't recover it either) from "gap in our pipeline" (ILSpy renders it, we don't). It is an analysis aid only; no test or CI infrastructure depends on it.

---

## Exact Matches

### `String.IsNullOrEmpty`

**Source (dotnet/runtime):**

```csharp
return value == null || value.Length == 0;
```

**Decompiled:**

```csharp
return value == null || value.Length == 0;
```

**Verdict:** Character for character. **Grade: A**

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

**Verdict:** Exact (modulo optional parens); no spurious unsigned casts. `Min` is identical with `<=`. **Grade: A**

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
if (value > max)
{
    return max;
}
return value;
```

**Verdict:** Statement for statement, NaN behavior intact. The source's `else if` renders as a sequential `if` — compilation erases the distinction (the prior arm returns). **Grade: A**

---

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
    if (value < 0)
    {
        Math.ThrowNegateTwosCompOverflow();
    }
}
return value;
```

**Verdict:** Exact, including the nested guard. Three snapshots ago this method dropped the negation statement entirely; two ago it leaked a dangling `goto`. **Grade: A**

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

**Verdict:** Character for character — same-type non-virtual calls render bare names, as the source writes them. **Grade: A**

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

**Verdict:** Exact modulo the local's name (`V_0` vs `count`; PDB names recover this in the type command's flow). **Grade: A**

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

**Verdict:** Character for character (modulo braces on the single-statement if). **Grade: A**

---

### `HashSet<T>.Contains` / `StringBuilder.Clear`

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

**Verdict:** Exact — the counter declares in the for-initializer as the source writes it; only its name differs. **Grade: A**

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
if (Runtime.CompilerServices.RuntimeHelpers.IsKnownConstant(value) && value.Length == 1)
{
    return Contains(value[0]);
}
return SpanHelpers.IndexOf(ref _firstChar, Length, ref value._firstChar, value.Length) >= 0;
```

**Verdict:** Statement for statement — the `&&` chain, the enum argument, the guarded-return shape, and the bare member names all match. Only the semi-qualified `Runtime.CompilerServices.` prefix differs (the whole-type view's using-hoisting shortens it; this per-method probe has no using context). **Grade: A**

---

## Near-Exact

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

**Verdict:** Semantically exact with the `(uint)` bounds-check casts preserved. The if/else renders as a guard clause with early return — the compiler emits the rare branch first and the decompiler follows the IL order. **Grade: A-**

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

**Verdict:** Line-for-line structural match. The deltas are naming residuals: `S_0` is an eval-stack spill (stack values have no PDB name by nature — the source's `item`), and the `T V_2 = default;` temp is the lowering of `default!` written in place. `Queue.Dequeue` matches its source the same way. **Grade: A-**

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
    if (V_0 > 0)
    {
        Array.Clear(_items, 0, V_0);
        return;
    }
}
else
{
    _size = 0;
}
```

**Verdict:** Source structure recovered — re-nested guard, correct else. Residuals: the hoisted `int V_0;` (the local spans two rendered scopes inside the arm — the PDB-local-scopes refinement would place it) and an extra `return;` inside the guard reflecting the IL's separate exit. **Grade: A-**

---

### `Dictionary<K,V>.ContainsValue`

**Source:**

```csharp
Entry[]? entries = _entries;
if (value == null)
{
    for (int i = 0; i < _count; i++)
    {
        if (entries[i].next >= -1 && entries[i].value == null) return true;
    }
}
else if (typeof(TValue).IsValueType)
{
    for (int i = 0; i < _count; i++)
    {
        if (entries[i].next >= -1 && EqualityComparer<TValue>.Default.Equals(entries[i].value, value)) return true;
    }
}
else
{
    EqualityComparer<TValue> defaultComparer = EqualityComparer<TValue>.Default;
    for (int i = 0; i < _count; i++)
    {
        if (entries[i].next >= -1 && defaultComparer.Equals(entries[i].value, value)) return true;
    }
}
return false;
```

**Decompiled (abbreviated):**

```csharp
EqualityComparer<TValue> V_3;

Entry<TKey, TValue>[] V_0 = _entries;
if (value != null)
{
    if (typeof(TValue).IsValueType)
    {
        for (int V_2 = 0; V_2 < _count; V_2++)
        {
            if (V_0[V_2].next >= -1 && EqualityComparer<TValue>.Default.Equals(V_0[V_2].value, value))
            {
                return true;
            }
        }
        return false;
    }
    V_3 = EqualityComparer<TValue>.Default;
    for (int V_4 = 0; V_4 < _count; V_4++) { ... }
}
else
{
    for (int V_1 = 0; V_1 < _count; V_1++) { ... }
    return false;
}
return false;
```

**Verdict:** All three loop paths structure correctly in their arms, the loop-body `&&` conditions compose as the source writes them, and every expression matches. Remaining deltas: the branch order inverts (`value != null` first vs the source's `value == null`), the cached comparer stays hoisted (multi-block local), and the shared `return false` appears per path. **Grade: A-**

---

## Summary Scorecard

| Method | Grade | Key Differences |
| --- | --- | --- |
| `String.IsNullOrEmpty` | **A** | exact |
| `Math.Max` | **A** | exact |
| `Math.Min` | **A** | exact |
| `Math.Clamp` | **A** | else-if renders as sequential if |
| `Math.Abs` | **A** | exact |
| `List.Contains` | **A** | exact |
| `Dictionary.Clear` | **A** | local name only |
| `HashSet.Contains` | **A** | exact |
| `StringBuilder.Clear` | **A** | exact |
| `Queue.Enqueue` | **A** | exact |
| `String.IsNullOrWhiteSpace` | **A** | counter name only |
| `String.Contains` | **A** | namespace qualification only |
| `Stack.Push` | **A-** | guard-clause inversion of if/else |
| `Stack.Pop` | **A-** | spill/temp naming |
| `Queue.Dequeue` | **A-** | spill/temp naming |
| `List.Clear` | **A-** | hoisted multi-scope local; extra return |
| `Dictionary.ContainsValue` | **A-** | path order; hoisted comparer |

### Overall: **A** (12 of 17 exact; all 17 at A-/A; previous snapshots: B, B+, A-)

**Closed since the third snapshot:**

- `this.` qualification dropped per the style oracle (collision-guarded)
- `base.` restricted to genuine cross-type dispatch — same-type calls render bare names
- Implicit boxing renders bare (`value != null`, not `(object)value != null`)
- Trailing `return;` trimmed; expression-bodied properties in the type view
- Implicit base-ctor calls suppressed; explicit type args where C# requires them (`Array.Empty<T>()`)
- Loop counters declare in the for-initializer; same-block locals declare at their first store
- Spilled increment/decrement pairs fold back (`dst[--j] = src[i++]`)
- Statement form preferred over poorly-reading ternaries (`Clamp`, `String.Contains`)
- Three real bugs fixed (try-region ordering, int-call bool contexts, undeclared loop spills)

**Remaining gaps, all residual:**

1. **Naming** — eval-stack spills (`S_0`) and compiler temps (`V_2`) have no PDB names by nature; ordinary locals are `V_n` only when no PDB is in play (the type command acquires one). Synthesized names for the residuals are an open design question.
2. **Multi-scope local placement** (`List.Clear`'s `int V_0;`, `ContainsValue`'s comparer) — needs PDB local scopes for true source positions.
