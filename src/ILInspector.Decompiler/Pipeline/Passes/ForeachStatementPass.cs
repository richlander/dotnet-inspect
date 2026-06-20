namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises supported compiler <c>foreach</c> lowerings back to a
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
/// <para><b>Array form</b> — a single-dimension array lowers to an indexed for
/// loop over two hidden locals, an array copy and an index:
/// <code>
/// T[] a = collection;
/// for (int i = 0; i &lt; a.Length; i++)
/// {
///     T item = a[i];
///     BODY
/// }
/// </code>
/// (structuring + <see cref="ForLoopPass"/> leave it a <see cref="ForLoop"/>).
/// The array copy and index are the discriminator: a hand-written indexed for
/// loop reads the array directly and names its index, so it never matches; even
/// one that copies the array stays a for loop because its copy and index carry
/// source names. Both slots must be referenced <em>only</em> by the lowered
/// shape across the whole function — a reference anywhere else means the slots
/// are not the hidden foreach scaffolding.</para>
///
/// <para><b>String form</b> mirrors the array form, but indexes through
/// <c>string.Length</c> and <c>string.Chars</c> over hidden string-copy and index
/// locals.</para>
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
            if (TryMatchArray(function, loop) is not { } match)
            {
                if (TryMatchString(function, loop) is not { } stringMatch)
                    continue;

                RaiseIndexedLoop(loop, stringMatch.StringCopy, stringMatch.ItemStore, "raise indexed string loop to foreach", context);
                return;
            }

            RaiseIndexedLoop(loop, match.ArrayCopy, match.ItemStore, "raise indexed array loop to foreach", context);
            return;
        }
    }

    static void RaiseIndexedLoop(ForLoop loop, StoreLocal collectionCopy, StoreLocal itemStore, string message, PassContext context)
    {
        var collection = collectionCopy.Value;
        collection.Detach();
        itemStore.Detach();
        var body = loop.Body;
        body.Detach();
        var foreachStatement = new ForeachStatement(itemStore.Index, itemStore.Type, collection, body);
        context.Stepper.StepOver(message, loop);
        loop.ReplaceWith(foreachStatement);
        collectionCopy.Detach();
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

    sealed record ArrayMatch(StoreLocal ArrayCopy, StoreLocal ItemStore);

    static ArrayMatch? TryMatchArray(IrFunction function, ForLoop loop)
    {
        // The array copy is the statement immediately before the loop.
        if (loop.Parent is not Block block || loop.ChildIndex == 0)
            return null;
        if (block.Children[loop.ChildIndex - 1] is not StoreLocal arrayCopy)
            return null;
        int arrayIndex = arrayCopy.Index;

        // init: i = 0
        if (loop.Initializer is not StoreLocal { Value: Constant { Value: 0 } } initStore)
            return null;
        int indexIndex = initStore.Index;
        if (indexIndex == arrayIndex)
            return null;

        // condition: i < a.Length (signed)
        if (loop.Condition is not Comparison { Kind: ComparisonKind.LessThan, IsUnsigned: false } comparison
            || !IsLoad(comparison.Left, indexIndex)
            || comparison.Right is not ArrayLength { Array: var lengthArray }
            || !IsLoad(lengthArray, arrayIndex))
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

        // body opens with: item = a[i]
        if (loop.Body.Children is not [StoreLocal { Value: LoadElement { Array: var elementArray, Index: var elementIndex } } itemStore, ..]
            || !IsLoad(elementArray, arrayIndex)
            || !IsLoad(elementIndex, indexIndex))
        {
            return null;
        }
        if (itemStore.Index == arrayIndex || itemStore.Index == indexIndex)
            return null;

        // Both scaffold locals must be compiler-hidden; the item local may carry
        // the source foreach-variable name, but the copy and index never do.
        if (HasSourceLocalName(function, arrayIndex) || HasSourceLocalName(function, indexIndex))
            return null;

        // Whole-function safety: the copy and index slots are referenced only by
        // the nodes this shape consumes. Anything else means they are not the
        // hidden scaffolding (hand-written IL wearing the same shape, slot reuse).
        var allowed = new HashSet<IrNode>(ReferenceEqualityComparer.Instance)
        {
            arrayCopy, initStore, comparison.Left, lengthArray, loop.Increment, incLeft, elementArray, elementIndex,
        };
        if (!ReferencedOnlyBy(function, arrayIndex, allowed) || !ReferencedOnlyBy(function, indexIndex, allowed))
            return null;

        return new ArrayMatch(arrayCopy, itemStore);
    }

    sealed record StringMatch(StoreLocal StringCopy, StoreLocal ItemStore);

    static StringMatch? TryMatchString(IrFunction function, ForLoop loop)
    {
        if (loop.Parent is not Block block || loop.ChildIndex == 0)
            return null;
        if (block.Children[loop.ChildIndex - 1] is not StoreLocal stringCopy
            || !stringCopy.Type.Equals(TypeRef.CoreLib("System", "String")))
        {
            return null;
        }

        int stringIndex = stringCopy.Index;
        if (loop.Initializer is not StoreLocal { Value: Constant { Value: 0 } } initStore)
            return null;
        int indexIndex = initStore.Index;
        if (indexIndex == stringIndex)
            return null;

        if (loop.Condition is not Comparison { Kind: ComparisonKind.LessThan, IsUnsigned: false } comparison
            || !IsLoad(comparison.Left, indexIndex)
            || comparison.Right is not LoadProperty length
            || !MemberIdentity.IsStringLengthGetter(length)
            || length.Instance is not LoadLocal lengthReceiver
            || lengthReceiver.Index != stringIndex)
        {
            return null;
        }

        if (loop.Increment is not StoreLocal { Index: var incIndex, Value: Binary { Kind: BinaryKind.Add, Left: var incLeft, Right: Constant { Value: 1 } } }
            || incIndex != indexIndex
            || !IsLoad(incLeft, indexIndex))
        {
            return null;
        }

        if (loop.Body.Children is not [StoreLocal { Value: LoadProperty chars } itemStore, ..]
            || !MemberIdentity.IsStringCharsGetter(chars)
            || chars.Instance is not LoadLocal charsReceiver
            || charsReceiver.Index != stringIndex
            || chars.IndexArguments is not [LoadLocal charsIndex]
            || charsIndex.Index != indexIndex)
        {
            return null;
        }
        if (itemStore.Index == stringIndex || itemStore.Index == indexIndex)
            return null;

        if (HasSourceLocalName(function, stringIndex) || HasSourceLocalName(function, indexIndex))
            return null;

        var allowed = new HashSet<IrNode>(ReferenceEqualityComparer.Instance)
        {
            stringCopy, initStore, comparison.Left, lengthReceiver, loop.Increment, incLeft, charsReceiver, charsIndex,
        };
        if (!ReferencedOnlyBy(function, stringIndex, allowed) || !ReferencedOnlyBy(function, indexIndex, allowed))
            return null;

        return new StringMatch(stringCopy, itemStore);
    }

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
