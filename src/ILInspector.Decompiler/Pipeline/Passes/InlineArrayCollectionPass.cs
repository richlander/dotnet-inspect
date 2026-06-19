using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the csc inline-array lowering of a span collection expression back into
/// a C# 12 collection expression — <c>[e0, e1, ...]</c> in a
/// <see cref="System.ReadOnlySpan{T}"/> context. A collection expression with
/// non-constant elements targeting a span, e.g.
/// <c>GetFlattenedIndex([index1, index2])</c>, is lowered to a
/// compiler-synthesized <c>&lt;&gt;y__InlineArrayN&lt;T&gt;</c> temporary that is
/// default-initialized, has each slot stored through
/// <c>&lt;PrivateImplementationDetails&gt;.InlineArrayElementRef&lt;…&gt;(ref tmp, i)</c>,
/// and is finally exposed as a span by
/// <c>&lt;PrivateImplementationDetails&gt;.InlineArrayAsReadOnlySpan&lt;…&gt;(tmp, N)</c>
/// (or <c>InlineArrayAsSpan</c> for a mutable <see cref="System.Span{T}"/> target).
/// Left flat the angle-bracketed compiler-internal type and method names never
/// parse — every such method is syntactically malformed.
///
/// <para>Scoped to the well-understood shape: the span source is a
/// <c>&lt;&gt;y__InlineArrayN</c> local (the runtime's own InlineArray structs such
/// as <c>TwoObjects</c> used by string.Format's params lowering are a distinct
/// shape and left untouched), default-initialized once, written by exactly N
/// element stores covering slots 0..N-1, and used by exactly one
/// AsReadOnlySpan — nothing else references the local. Anything outside this is
/// left flat. The whole shape is proven before any node is detached.</para>
/// </summary>
public sealed class InlineArrayCollectionPass : IIrPass
{
    const string PrivateImpl = "<PrivateImplementationDetails>";

    public string Name => "inline-array-collection";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var span in function.Descendants.OfType<Call>().ToList())
        {
            if (span.Parent is null)
                continue; // already rewritten in this pass (a nested match)
            if (!IsPrivateImpl(span.Callee, "InlineArrayAsReadOnlySpan") && !IsPrivateImpl(span.Callee, "InlineArrayAsSpan"))
                continue;
            // Only the compiler-synthesized inline arrays — not the runtime's own
            // InlineArray structs (TwoObjects/ThreeObjects) behind string.Format.
            if (span.Callee.TypeArguments is not [var arrayType, var element] || !IsSynthesizedInlineArray(arrayType))
                continue;
            if (span.Arguments is not [LoadLocalAddress { Index: var local }, Constant { Value: int count }])
                continue;
            if (span.ResultType is not { } spanType)
                continue;

            // Every reference to the inline-array local: the single init, the N
            // element stores, and this AsReadOnlySpan. A load of the local by
            // value, an address taken elsewhere, or a second span use means the
            // local escapes the shape — leave it flat.
            var addressRefs = function.Descendants.OfType<LoadLocalAddress>().Where(a => a.Index == local).ToList();
            if (function.Descendants.OfType<LoadLocal>().Any(l => l.Index == local))
                continue;
            if (addressRefs.Count != count + 2)
                continue;

            var init = FindInit(function, local);
            if (init is null)
                continue;

            var stores = CollectElementStores(function, local, count);
            if (stores is null)
                continue;

            // Shape proven. Detach each element value (discarding the
            // ElementRef address subtree), build the collection expression, and
            // replace the span source; then drop the init and the stores.
            var elements = stores.Select(s => (IrExpression)s.DetachChildren()[1]).ToList();
            var collection = new CollectionExpression(element, spanType, elements);
            context.Stepper.StepOver("raise inline-array lowering to collection expression", span);
            span.ReplaceWith(collection);
            init.Detach();
            foreach (var store in stores)
                store.Detach();
        }
    }

    static bool IsPrivateImpl(MethodRef callee, string name)
        => callee.Name == name && callee.DeclaringType.Name == PrivateImpl;

    /// <summary>
    /// True for a compiler-synthesized inline-array buffer — the span source csc
    /// emits for a collection expression. The name lives on the generic definition
    /// (the type is a generic instance) and carries an arity backtick. Roslyn spells
    /// it <c>&lt;&gt;y__InlineArray4`1</c> on older runtimes and <c>InlineArray4`1</c>
    /// on .NET 11+; both forms are accepted. The runtime's own params buffers
    /// (<c>TwoObjects</c>/<c>ThreeObjects</c>/<c>EightObjects</c>/<c>ArgumentData</c>)
    /// do not start with <c>InlineArray</c>, so they stay excluded.
    /// </summary>
    static bool IsSynthesizedInlineArray(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType : type;
        string name = definition?.Name ?? "";
        if (name.StartsWith("<>y__InlineArray"))
            return true;
        return name.StartsWith("InlineArray") && name.Length > "InlineArray".Length && char.IsAsciiDigit(name["InlineArray".Length]);
    }

    /// <summary>The single <c>initobj</c> that default-initializes the inline-array local; null if absent or not unique.</summary>
    static InitObject? FindInit(IrFunction function, int local)
    {
        var inits = function.Descendants.OfType<InitObject>()
            .Where(i => i.Address is LoadLocalAddress { } a && a.Index == local)
            .ToList();
        return inits.Count == 1 ? inits[0] : null;
    }

    /// <summary>
    /// The N element stores for the inline-array local, ordered by slot index, or
    /// null when they do not cover exactly slots 0..N-1 through
    /// <c>InlineArrayElementRef(ref local, i)</c> — each a statement directly in a
    /// block (its own slot, so it can be detached).
    /// </summary>
    static List<StoreIndirect>? CollectElementStores(IrFunction function, int local, int count)
    {
        var byIndex = new SortedDictionary<int, StoreIndirect>();
        foreach (var store in function.Descendants.OfType<StoreIndirect>())
        {
            if (store.Address is not Call elementRef || !IsPrivateImpl(elementRef.Callee, "InlineArrayElementRef"))
                continue;
            if (elementRef.Arguments is not [LoadLocalAddress { Index: var refLocal }, Constant { Value: int slot }] || refLocal != local)
                continue;
            if (store.Parent is not Block)
                return null;
            if (slot < 0 || slot >= count || byIndex.ContainsKey(slot))
                return null;
            byIndex[slot] = store;
        }
        if (byIndex.Count != count)
            return null;
        return byIndex.Values.ToList();
    }
}
