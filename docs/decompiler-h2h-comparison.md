# Decompiler vs Source: Head-to-Head Comparison

Comparison of `dotnet-inspect` decompiler output against the actual C# source from [dotnet/runtime](https://github.com/dotnet/runtime). All methods decompiled from the installed .NET runtime assemblies.

---

## Near-Perfect Matches

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
if (value != null)
{
    return value.Length == 0;
}
return true;
```

**Verdict:** Semantically identical. Source uses `||` short-circuit; decompiler uses if/return. The IL compiles both to the same branch pattern — no way to distinguish. Structure is clear and readable. **Grade: A-**

---

### `Math.Max`

**Source:**

```csharp
return (val1 >= val2) ? val1 : val2;
```

**Decompiled:**

```csharp
if (val1 >= val2)
{
    return val1;
}
return val2;
```

**Verdict:** Semantically identical. Ternary `? :` and if/else compile to identical IL. **Grade: A**

---

### `Math.Min`

**Source:**

```csharp
return (val1 <= val2) ? val1 : val2;
```

**Decompiled:**

```csharp
if (val1 <= val2)
{
    return val1;
}
return val2;
```

**Verdict:** Same as Max. Ternary vs if/return — indistinguishable in IL. **Grade: A**

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
    System.Math.ThrowMinMaxException(min, max);
}
if (value < min)
{
    return min;
}
if (value <= max)
{
    return value;
}
return max;
```

**Verdict:** Semantically identical. The `else if (value > max)` becomes `if (value <= max) return value` — just the condition inverted. Fully qualified `System.Math.` is cosmetic (no `using` context). **Grade: A**

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
    if (value >= 0) goto IL_0012;
}
System.Math.ThrowNegateTwosCompOverflow();
return value;
```

**Verdict:** Close but has a structural issue. The `goto IL_0012` skips the throw — semantically correct, but the source nests the throw inside the `if`. The decompiler doesn't reconstruct the nested-if-with-early-exit pattern. **Grade: B**

---

### `List<T>.Contains`

**Source:**

```csharp
return _size != 0 && IndexOf(item) >= 0;
```

**Decompiled:**

```csharp
if (this._size != 0)
{
    return base.IndexOf(item) >= 0;
}
return false;
```

**Verdict:** Semantically identical. `&&` short-circuit = if/return in IL. `base.IndexOf` instead of `IndexOf` is because the call goes through the vtable slot (`callvirt`). **Grade: A-**

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
int V_0;
V_0 = this._count;
if (V_0 > 0)
{
    System.Array.Clear(this._buckets);
    this._count = 0;
    this._freeList = -1;
    this._freeCount = 0;
    System.Array.Clear(this._entries, 0, V_0);
}
return;
```

**Verdict:** Excellent match. Only differences: `V_0` vs `count` (no PDB), explicit `this.`, FQN `System.Array`, trailing `return;`. All cosmetic. **Grade: A**

---

### `HashSet<T>.Contains` / `StringBuilder.Clear`

Both are one-liners and match perfectly:

| Method | Source | Decompiled | Grade |
| --- | --- | --- | --- |
| `HashSet.Contains` | `return FindItemIndex(item) >= 0;` | `return base.FindItemIndex(item) >= 0;` | **A** |
| `StringBuilder.Clear` | `Length = 0; return this;` | `base.Length = 0; return this;` | **A** |

---

## Structural Differences

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
    if (char.IsWhiteSpace(value[V_0]))
        goto IL_0019;
}
return false;
return true;
```

**Verdict:** Logic is inverted: source returns `false` when `!IsWhiteSpace`, decompiler returns `false` at end and has `goto IL_0019` to skip to `return true`. The `goto` is a missed `continue` pattern. The for-loop init folding and `V_0++` work correctly (thanks to the fidelity improvements). **Grade: B-**

**Gap: `goto` in loop body instead of `continue` or negated condition.**

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
this._version = this._version + 1;
if (RuntimeHelpers.IsReferenceOrContainsReferences())
{
    V_0 = this._size;
    this._size = 0;
    if (V_0 <= 0) goto IL_003C;
}
else
{
    this._size = 0;
}
System.Array.Clear(this._items, 0, V_0);
return;
return;
```

**Verdict:** Structure mostly preserved. Issues: `_version + 1` instead of `_version++` (compound assignment should catch this — the `this.field = this.field + 1` pattern needs field-level compound detection, not just locals). `goto IL_003C` instead of early return. Double `return;`. **Grade: B-**

**Gap: Compound assignment for fields (not just locals). `goto` instead of early-return from nested if.**

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
int V_0;
T[] V_1;
V_0 = this._size;
V_1 = this._array;
if (V_0 >= V_1.Length)
{
    base.PushWithResize(item);
    return;
}
V_1[V_0] = item;
this._version = this._version + 1;
this._size = V_0 + 1;
return;
```

**Verdict:** Semantically correct. The `(uint)` casts are optimized away by the JIT so the IL uses a simpler comparison, which is fine. The if/else structure is inverted (decompiler tests `>=` and puts the else-body first). `_version + 1` should be `_version++`. **Grade: B**

**Gap: Field compound assignment. Inverted branch ordering (minor).**

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
base.MoveNext(this._tail);
this._size = this._size + 1;
this._version = this._version + 1;
return;
```

**Verdict:** Very close. `_size + 1` / `_version + 1` should be `_size++` / `_version++`. `MoveNext(this._tail)` is missing the `ref` (decompiler doesn't reconstruct `ref` argument passing). **Grade: B**

**Gap: Field compound assignment. Missing `ref` on arguments.**

---

## Major Structural Gaps

### `String.Contains(string)`

**Source:**

```csharp
if (value == null)
    ThrowHelper.ThrowArgumentNullException(...);
if (RuntimeHelpers.IsKnownConstant(value)
    && value.Length == 1)
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
    System.ThrowHelper.ThrowArgumentNullException(7);
}
if (RuntimeHelpers.IsKnownConstant(value))
{
    if (value.Length != 1) goto IL_0028;
}
return base.Contains(value[0]);
return SpanHelpers.IndexOf(...) >= 0;
```

**Verdict:** The `&&` in `if (IsKnownConstant(value) && value.Length == 1)` is split into two nested ifs with a `goto`. The second `return` is dead-code-looking because the `goto` target skips past it. This is the classic "compound boolean condition to nested branches" IL pattern. **Grade: C+**

**Gap: `&&`/`||` boolean condition reconstruction. Dead-code-looking sequential returns.**

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
V_0 = this._size - 1;
V_1 = this._array;
if (V_0 >= V_1.Length)
{
    base.ThrowForEmptyStack();
}
this._version = this._version + 1;
this._size = V_0;
/* ldelem(ldloc.1 V_1, ldloc.0 V_0) */
if (RuntimeHelpers.IsReferenceOrContainsReferences())
{
    V_2 = default(T);
    V_1[V_0] = V_2;
    S_0 = S_in_0;
}
return S_in_0;
```

**Verdict:** The `ldelem` (generic array element load) falls back to a comment. `S_0`/`S_in_0` are stack-simulation artifacts that leak into the output. The `return item` becomes `return S_in_0` which is opaque. **Grade: D+**

**Gap: Generic `ldelem` (constrained element access) not handled. Stack variable leakage.**

---

### `Dictionary<K,V>.ContainsValue`

**Source:**

```csharp
Entry[]? entries = _entries;
if (value == null) { /* loop */ }
else if (typeof(TValue).IsValueType) { /* loop */ }
else { /* loop with cached comparer */ }
return false;
```

**Decompiled:**

```csharp
V_0 = this._entries;
if (value /* box TValue */ != null)
{
    if (!Type.GetTypeFromHandle(typeof(TValue)).IsValueType)
        goto IL_0097;
}
else { V_1 = 0; }
for (; V_1 < this._count; V_1++) { ... }
return true;
return 0;
for (V_2 = 0; ...) { ... }
return true;
return 0;
V_3 = EqualityComparer<T1>.Default;
for (V_4 = 0; ...) { ... }
return true;
```

**Verdict:** Complex generic method with 3 code paths. The decompiler produces sequential `return true; return 0;` pairs and `goto` jumps between paths. `return 0` instead of `return false` (missing bool context on integer return). Multiple for-loops are correctly structured. But the overall control flow is hard to follow. **Grade: C-**

**Gap: Multi-branch control flow flattening. `return 0` vs `return false`. Generic type parameter references (`T1` instead of `TValue`).**

---

## Summary Scorecard

| Method | Grade | Key Differences |
| --- | --- | --- |
| `String.IsNullOrEmpty` | **A-** | `\|\|` to if/return (unavoidable) |
| `Math.Max` | **A** | Ternary to if/return (unavoidable) |
| `Math.Min` | **A** | Ternary to if/return (unavoidable) |
| `Math.Clamp` | **A** | Minor condition inversion |
| `List.Contains` | **A-** | `&&` to if/return (unavoidable) |
| `Dictionary.Clear` | **A** | Variable names, FQN (cosmetic) |
| `HashSet.Contains` | **A** | `base.` prefix (cosmetic) |
| `StringBuilder.Clear` | **A** | `base.` prefix (cosmetic) |
| `Math.Abs` | **B** | `goto` instead of nested if |
| `Stack.Push` | **B** | Field compound assign, branch inversion |
| `Queue.Enqueue` | **B** | Field compound assign, missing `ref` |
| `String.IsNullOrWhiteSpace` | **B-** | `goto` in loop instead of continue/negated condition |
| `List.Clear` | **B-** | Field compound assign, double return |
| `String.Contains` | **C+** | `&&` split into nested ifs + goto |
| `Dictionary.ContainsValue` | **C-** | Complex control flow, `return 0` vs `false` |
| `Stack.Pop` | **D+** | Generic ldelem fails, stack variable leakage |
| `Queue.Dequeue` | **D+** | Generic ldelem fails, stack variable leakage |

### Overall: **B** average across 17 methods

**Strengths (what works well):**

- Simple methods with linear control flow: near-perfect
- Field access, method calls, comparisons: all correct
- For-loop structuring with init/increment: working well (post-PR)
- Nested if/else: generally correct

**Top remaining gaps for future work:**

1. **Field compound assignment** (`_version++` instead of `_version = _version + 1`) — easy
2. **Generic `ldelem`** (constrained array element access) — medium
3. **`&&`/`||` boolean condition reconstruction** — medium
4. **`goto` to `continue`/`break` in loops** — medium
5. **`ref` argument annotation** — easy
6. **Bool context on return values** (`return 0` to `return false`) — easy
