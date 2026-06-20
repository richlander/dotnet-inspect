namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the compiler's two <c>foreach</c> lowerings back to a
/// <see cref="ForeachStatement"/>.
///
/// <para><b>Enumerator form</b> — the general <c>IEnumerable</c> path:
/// <code>
/// using (var e = collection.GetEnumerator())
/// {
///     while (e.MoveNext())
///     {
///         T item = e.Current;
///         BODY
///     }
/// }
/// </code>
/// The enumerator local must be compiler-hidden (no source local name) so
/// hand-written using/while loops stay at their source altitude.</para>
///
/// <para><b>Array and string form</b> — a single-dimension array or a string
/// lowers to an indexed for loop over two hidden locals, a collection copy and an
/// index:
/// <code>
/// T[] a = collection;            string a = collection;
/// for (int i = 0; i &lt; a.Length; i++)   for (int i = 0; i &lt; a.Length; i++)
/// {                                 {
///     T item = a[i];                    char item = a[i];
///     BODY                              BODY
/// }                                 }
/// </code>
/// (structuring + <see cref="ForLoopPass"/> leave it a <see cref="ForLoop"/>).
/// The two spell length and element differently — arrays use <c>ldlen</c> /
/// <c>ldelem</c>, strings use <c>String.get_Length</c> / <c>get_Chars</c> — but
/// share the discriminator: the copy and index are the lowering's scaffolding. A
/// hand-written indexed for loop reads the collection directly and names its
/// index, so it never matches; even one that copies the collection stays a for
/// loop because its copy and index carry source names. Both slots must be
/// referenced <em>only</em> by the lowered shape across the whole function — a
/// reference anywhere else means the slots are not the hidden foreach
/// scaffolding.</para>
/// </summary>
public sealed class ForeachStatementPass : IIrPass
{
    public string Name => "foreach-statement";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var usingStatement in function.Descendants.OfType<UsingStatement>().ToList())
        {
            if (TryMatchEnumerator(function, usingStatement) is not { } match)
                continue;

            var collection = match.Collection;
            collection.Detach();
            match.CurrentStore.Detach();
            var body = match.Loop.Body;
            body.Detach();
            var foreachStatement = new ForeachStatement(match.CurrentStore.Index, match.CurrentStore.Type, collection, body);
            context.Stepper.StepOver("raise enumerator loop to foreach", usingStatement);
            usingStatement.ReplaceWith(foreachStatement);
            return;
        }

        foreach (var loop in function.Descendants.OfType<ForLoop>().ToList())
        {
            if (TryMatchIndexed(function, loop) is not { } match)
                continue;

            var collection = match.CollectionCopy.Value;
            collection.Detach();
            match.ItemStore.Detach();
            var body = loop.Body;
            body.Detach();
            var foreachStatement = new ForeachStatement(match.ItemStore.Index, match.ItemStore.Type, collection, body);
            context.Stepper.StepOver("raise indexed array/string loop to foreach", loop);
            loop.ReplaceWith(foreachStatement);
            match.CollectionCopy.Detach();
            return;
        }
    }

    sealed record EnumeratorMatch(IrExpression Collection, WhileLoop Loop, StoreLocal CurrentStore);

    static EnumeratorMatch? TryMatchEnumerator(IrFunction function, UsingStatement usingStatement)
    {
        int enumeratorIndex = usingStatement.LocalIndex;
        if (HasSourceLocalName(function, enumeratorIndex))
            return null;

        if (usingStatement.Resource is not Call getEnumerator
            || !IsGetEnumerator(getEnumerator)
            || getEnumerator.Arguments is not [_])
        {
            return null;
        }

        if (usingStatement.Body.Blocks is not [{ Children: [WhileLoop loop] }]
            || !IsMoveNextOn(loop.Condition, enumeratorIndex)
            || loop.Body.Children is not [StoreLocal { Value: LoadProperty current } currentStore, ..]
            || !IsCurrentOn(current, enumeratorIndex))
        {
            return null;
        }

        if (loop.Body.Children.Skip(1).Any(child => ReferencesLocal(child, enumeratorIndex)))
            return null;

        return new EnumeratorMatch(getEnumerator.Arguments[0], loop, currentStore);
    }

    sealed record IndexedMatch(StoreLocal CollectionCopy, StoreLocal ItemStore);

    // The array and string foreach lowerings share one shape — a hidden copy of the
    // collection, then an indexed for loop reading length and element through that
    // copy. They differ only in how length and element are spelled: arrays use
    // `ldlen` / `ldelem`, strings use String.get_Length / get_Chars.
    static IndexedMatch? TryMatchIndexed(IrFunction function, ForLoop loop)
    {
        // The collection copy is the statement immediately before the loop.
        if (loop.Parent is not Block block || loop.ChildIndex == 0)
            return null;
        if (block.Children[loop.ChildIndex - 1] is not StoreLocal collectionCopy)
            return null;
        int copyIndex = collectionCopy.Index;

        // init: i = 0
        if (loop.Initializer is not StoreLocal { Value: Constant { Value: 0 } } initStore)
            return null;
        int indexIndex = initStore.Index;
        if (indexIndex == copyIndex)
            return null;

        // condition: i < copy.Length (signed), where Length reads the copy
        if (loop.Condition is not Comparison { Kind: ComparisonKind.LessThan, IsUnsigned: false } comparison
            || !IsLoad(comparison.Left, indexIndex)
            || LengthReceiver(comparison.Right) is not { } lengthReceiver
            || lengthReceiver.Index != copyIndex)
        {
            return null;
        }

        // increment: i = i + 1
        if (loop.Increment is not StoreLocal { Index: var incIndex, Value: Binary { Kind: BinaryKind.Add, Left: var incLeft, Right: Constant { Value: 1 } } }
            || incIndex != indexIndex
            || !IsLoad(incLeft, indexIndex))
        {
            return null;
        }

        // body opens with: item = copy[i]
        if (loop.Body.Children is not [StoreLocal itemStore, ..]
            || ElementReads(itemStore.Value) is not var (elementReceiver, elementIndex)
            || elementReceiver.Index != copyIndex
            || elementIndex.Index != indexIndex)
        {
            return null;
        }
        if (itemStore.Index == copyIndex || itemStore.Index == indexIndex)
            return null;

        // Both scaffold locals must be compiler-hidden; the item local may carry
        // the source foreach-variable name, but the copy and index never do.
        if (HasSourceLocalName(function, copyIndex) || HasSourceLocalName(function, indexIndex))
            return null;

        // Whole-function safety: the copy and index slots are referenced only by
        // the nodes this shape consumes. Anything else means they are not the
        // hidden scaffolding (hand-written IL wearing the same shape, slot reuse).
        var allowed = new HashSet<IrNode>(ReferenceEqualityComparer.Instance)
        {
            collectionCopy, initStore, comparison.Left, lengthReceiver, loop.Increment, incLeft, elementReceiver, elementIndex,
        };
        if (!ReferencedOnlyBy(function, copyIndex, allowed) || !ReferencedOnlyBy(function, indexIndex, allowed))
            return null;

        return new IndexedMatch(collectionCopy, itemStore);
    }

    // The local the length is read from: an array's ldlen operand, or a string's
    // get_Length receiver. Null when the condition is not a recognized length read.
    static LoadLocal? LengthReceiver(IrExpression condition) => condition switch
    {
        ArrayLength { Array: LoadLocal load } => load,
        LoadProperty property when MemberIdentity.IsStringLengthGetter(property) && property.Instance is LoadLocal load => load,
        _ => null,
    };

    // The (collection, index) locals an element read loads from: an array's ldelem
    // operands, or a string's get_Chars receiver and index. Null otherwise.
    static (LoadLocal Receiver, LoadLocal Index)? ElementReads(IrExpression value) => value switch
    {
        LoadElement { Array: LoadLocal receiver, Index: LoadLocal index } => (receiver, index),
        LoadProperty property when MemberIdentity.IsStringCharsGetter(property)
            && property.Instance is LoadLocal receiver
            && property.IndexArguments is [LoadLocal index] => (receiver, index),
        _ => null,
    };

    static bool HasSourceLocalName(IrFunction function, int index)
        => index >= 0
            && index < function.LocalNames.Length
            && !string.IsNullOrWhiteSpace(function.LocalNames[index]);

    static bool IsGetEnumerator(Call call)
        => call.Callee is { Name: "GetEnumerator", HasThis: true } && call.Arguments.Count == 1;

    static bool IsMoveNextOn(IrExpression condition, int enumeratorIndex)
        => condition is Call
        {
            Callee: { Name: "MoveNext", HasThis: true, ReturnType: { Namespace: "System", Name: "Boolean" } },
            Arguments: [var receiver],
        } && IsEnumeratorReceiver(receiver, enumeratorIndex);

    static bool IsCurrentOn(LoadProperty property, int enumeratorIndex)
        => property is { HasInstance: true, PropertyName: "Current", Instance: { } receiver }
            && IsEnumeratorReceiver(receiver, enumeratorIndex);

    static bool IsEnumeratorReceiver(IrExpression receiver, int enumeratorIndex) => receiver switch
    {
        LoadLocal load => load.Index == enumeratorIndex,
        LoadLocalAddress address => address.Index == enumeratorIndex,
        _ => false,
    };

    static bool IsLoad(IrExpression expression, int index)
        => expression is LoadLocal load && load.Index == index;

    static bool ReferencesLocal(IrNode node, int index)
        => node.Descendants.Prepend(node).Any(candidate => candidate switch
        {
            LoadLocal load => load.Index == index,
            LoadLocalAddress address => address.Index == index,
            StoreLocal store => store.Index == index,
            _ => false,
        });

    static bool ReferencedOnlyBy(IrFunction function, int index, HashSet<IrNode> allowed)
    {
        foreach (var node in function.Descendants)
        {
            bool references = node switch
            {
                LoadLocal load => load.Index == index,
                LoadLocalAddress address => address.Index == index,
                StoreLocal store => store.Index == index,
                _ => false,
            };
            if (references && !allowed.Contains(node))
                return false;
        }
        return true;
    }
}
