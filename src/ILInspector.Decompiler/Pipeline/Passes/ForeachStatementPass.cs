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
///
/// <para><b>Rectangular array form</b> — a rank-2 array lowers to nested indexed
/// loops over hidden array-copy, upper-bound, and index locals, using
/// <c>GetLowerBound</c>, <c>GetUpperBound</c>, and the array's generated
/// <c>Get(i, j)</c> accessor.</para>
///
/// <para>The compiler may reuse one indexed-copy/index pair across several
/// sibling <c>foreach</c> loops in a method. The indexed phase therefore
/// collects every structural array/string candidate first and pools the
/// scaffold nodes per slot across all of them before the whole-function
/// uniqueness check, so each loop's reference to the shared slots reads as
/// scaffolding rather than a stray use. Every clean candidate is raised; passes
/// run once, so neither phase may stop after the first match, and the enumerator
/// phase falls through to the indexed phase.</para>
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
        }

        // Collect every structural indexed-foreach candidate first, then admit a
        // candidate only if its two scaffold slots are referenced solely by
        // recognized foreach scaffolding. Pooling the allowed nodes per slot
        // tolerates the compiler reusing one copy/index pair across several
        // foreach loops in the same method (which would otherwise make each loop
        // look like it has stray references to the others' nodes).
        var candidates = new List<IndexedCandidate>();
        foreach (var loop in function.Descendants.OfType<ForLoop>().ToList())
        {
            if (TryMatchRectangularArray2D(function, loop) is { } rectangular)
            {
                RaiseRectangularLoop(rectangular, context);
                continue;
            }
            if (TryMatchArray(function, loop) is { } candidate)
            {
                candidates.Add(candidate);
                continue;
            }
            if (TryMatchString(function, loop) is { } stringCandidate)
                candidates.Add(stringCandidate);
        }

        var allowedBySlot = new Dictionary<int, HashSet<IrNode>>();
        foreach (var candidate in candidates)
        {
            Pool(allowedBySlot, candidate.CollectionIndex, candidate.Allowed);
            Pool(allowedBySlot, candidate.IndexIndex, candidate.Allowed);
        }

        var clean = candidates
            .Where(c => ReferencedOnlyBy(function, c.CollectionIndex, allowedBySlot[c.CollectionIndex])
                && ReferencedOnlyBy(function, c.IndexIndex, allowedBySlot[c.IndexIndex]))
            .ToList();

        foreach (var candidate in clean)
        {
            var loop = candidate.Loop;
            var collection = candidate.CollectionCopy.Value;
            collection.Detach();
            candidate.ItemStore.Detach();
            var body = loop.Body;
            body.Detach();
            var foreachStatement = new ForeachStatement(candidate.ItemStore.Index, candidate.ItemStore.Type, collection, body);
            context.Stepper.StepOver(candidate.StepMessage, loop);
            loop.ReplaceWith(foreachStatement);
            candidate.CollectionCopy.Detach();
        }
    }

    static void RaiseRectangularLoop(RectangularArrayCandidate candidate, PassContext context)
    {
        var collection = candidate.ArrayCopy.Value;
        collection.Detach();
        candidate.ItemStore.Detach();
        var body = candidate.InnerLoop.Body;
        body.Detach();
        var foreachStatement = new ForeachStatement(candidate.ItemStore.Index, candidate.ItemStore.Type, collection, body);
        context.Stepper.StepOver("raise rectangular array loop to foreach", candidate.OuterLoop);
        candidate.OuterLoop.ReplaceWith(foreachStatement);
        candidate.ArrayCopy.Detach();
        candidate.Upper0Store.Detach();
        candidate.Upper1Store.Detach();
    }

    static void Pool(Dictionary<int, HashSet<IrNode>> allowedBySlot, int slot, IReadOnlyCollection<IrNode> allowed)
    {
        if (!allowedBySlot.TryGetValue(slot, out var set))
            allowedBySlot[slot] = set = new HashSet<IrNode>(ReferenceEqualityComparer.Instance);
        set.UnionWith(allowed);
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

    sealed record IndexedCandidate(
        ForLoop Loop,
        StoreLocal CollectionCopy,
        StoreLocal ItemStore,
        int CollectionIndex,
        int IndexIndex,
        IReadOnlyCollection<IrNode> Allowed,
        string StepMessage);

    static IndexedCandidate? TryMatchArray(IrFunction function, ForLoop loop)
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

        // The nodes that legitimately reference the scaffold slots for this loop.
        // Run pools these across every candidate so reuse of one slot pair by
        // several foreach loops still counts as pure scaffolding; a reference
        // outside the pool (hand-written IL wearing the shape, slot reuse by user
        // code) means the slots are not the hidden foreach scaffolding.
        var allowed = new HashSet<IrNode>(ReferenceEqualityComparer.Instance)
        {
            arrayCopy, initStore, comparison.Left, lengthArray, loop.Increment, incLeft, elementArray, elementIndex,
        };

        return new IndexedCandidate(loop, arrayCopy, itemStore, arrayIndex, indexIndex, allowed, "raise indexed array loop to foreach");
    }

    static IndexedCandidate? TryMatchString(IrFunction function, ForLoop loop)
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

        return new IndexedCandidate(loop, stringCopy, itemStore, stringIndex, indexIndex, allowed, "raise indexed string loop to foreach");
    }

    sealed record RectangularArrayCandidate(
        ForLoop OuterLoop,
        ForLoop InnerLoop,
        StoreLocal ArrayCopy,
        StoreLocal Upper0Store,
        StoreLocal Upper1Store,
        StoreLocal ItemStore);

    static RectangularArrayCandidate? TryMatchRectangularArray2D(IrFunction function, ForLoop outerLoop)
    {
        if (outerLoop.Parent is not Block block || outerLoop.ChildIndex < 3)
            return null;
        if (block.Children[outerLoop.ChildIndex - 3] is not StoreLocal arrayCopy
            || arrayCopy.Type is not { Kind: TypeRefKind.Array, Rank: 2, ElementType: { } elementType })
        {
            return null;
        }
        int arrayIndex = arrayCopy.Index;

        if (block.Children[outerLoop.ChildIndex - 2] is not StoreLocal { Value: Call upper0 } upper0Store
            || block.Children[outerLoop.ChildIndex - 1] is not StoreLocal { Value: Call upper1 } upper1Store
            || !IsArrayBoundCall(upper0, "GetUpperBound", arrayIndex, dimension: 0)
            || !IsArrayBoundCall(upper1, "GetUpperBound", arrayIndex, dimension: 1))
        {
            return null;
        }
        int upper0Index = upper0Store.Index;
        int upper1Index = upper1Store.Index;

        if (outerLoop.Initializer is not StoreLocal { Value: Call lower0 } outerInit
            || !IsArrayBoundCall(lower0, "GetLowerBound", arrayIndex, dimension: 0))
        {
            return null;
        }
        int outerIndex = outerInit.Index;

        if (!IsIndexLoop(outerLoop, outerIndex, upper0Index)
            || outerLoop.Body.Children is not [ForLoop innerLoop]
            || innerLoop.Initializer is not StoreLocal { Value: Call lower1 } innerInit
            || !IsArrayBoundCall(lower1, "GetLowerBound", arrayIndex, dimension: 1))
        {
            return null;
        }
        int innerIndex = innerInit.Index;

        if (!IsIndexLoop(innerLoop, innerIndex, upper1Index)
            || innerLoop.Body.Children is not [StoreLocal { Value: Call getElement } itemStore, ..]
            || !IsArrayGet(getElement, arrayIndex, outerIndex, innerIndex, arrayCopy.Type, elementType)
            || itemStore.Index == arrayIndex
            || itemStore.Index == upper0Index
            || itemStore.Index == upper1Index
            || itemStore.Index == outerIndex
            || itemStore.Index == innerIndex)
        {
            return null;
        }

        var scaffold = new[] { arrayIndex, upper0Index, upper1Index, outerIndex, innerIndex };
        if (scaffold.Distinct().Count() != scaffold.Length)
            return null;
        if (scaffold.Any(index => HasSourceLocalName(function, index)))
            return null;

        var allowed = new HashSet<IrNode>(ReferenceEqualityComparer.Instance)
        {
            arrayCopy,
            upper0Store,
            upper1Store,
            outerInit,
            innerInit,
            outerLoop.Condition,
            innerLoop.Condition,
            outerLoop.Increment,
            innerLoop.Increment,
        };
        AddDescendants(allowed, upper0);
        AddDescendants(allowed, upper1);
        AddDescendants(allowed, lower0);
        AddDescendants(allowed, lower1);
        AddDescendants(allowed, outerLoop.Condition);
        AddDescendants(allowed, innerLoop.Condition);
        AddDescendants(allowed, outerLoop.Increment);
        AddDescendants(allowed, innerLoop.Increment);
        AddDescendants(allowed, getElement);

        if (scaffold.Any(index => !ReferencedOnlyBy(function, index, allowed)))
            return null;

        return new RectangularArrayCandidate(outerLoop, innerLoop, arrayCopy, upper0Store, upper1Store, itemStore);
    }

    static void AddDescendants(HashSet<IrNode> set, IrNode node)
    {
        set.Add(node);
        foreach (var descendant in node.Descendants)
            set.Add(descendant);
    }

    static bool IsIndexLoop(ForLoop loop, int index, int upperBound)
        => loop.Condition is Comparison { Kind: ComparisonKind.LessThanOrEqual, IsUnsigned: false } comparison
            && IsLoad(comparison.Left, index)
            && IsLoad(comparison.Right, upperBound)
            && loop.Increment is StoreLocal { Index: var incIndex, Value: Binary { Kind: BinaryKind.Add, Left: var incLeft, Right: Constant { Value: 1 } } }
            && incIndex == index
            && IsLoad(incLeft, index);

    static bool IsArrayBoundCall(Call call, string name, int arrayIndex, int dimension)
        => call is
        {
            IsVirtual: true,
            Callee:
            {
                HasThis: true,
                Name: var methodName,
                DeclaringType: { Namespace: "System", Name: "Array" },
                ParameterTypes: [var parameter],
                ReturnType: { Namespace: "System", Name: "Int32" },
            },
            Arguments: [LoadLocal receiver, Constant { Value: int value }],
        }
        && methodName == name
        && receiver.Index == arrayIndex
        && parameter.Equals(TypeRef.CoreLib("System", "Int32"))
        && value == dimension;

    static bool IsArrayGet(Call call, int arrayIndex, int outerIndex, int innerIndex, TypeRef arrayType, TypeRef elementType)
        => call is
        {
            IsVirtual: false,
            Callee:
            {
                HasThis: true,
                Name: "Get",
                DeclaringType: var declaringType,
                ParameterTypes: [var p0, var p1],
                ReturnType: var returnType,
            },
            Arguments: [LoadLocal receiver, LoadLocal outer, LoadLocal inner],
        }
        && declaringType.Equals(arrayType)
        && returnType.Equals(elementType)
        && p0.Equals(TypeRef.CoreLib("System", "Int32"))
        && p1.Equals(TypeRef.CoreLib("System", "Int32"))
        && receiver.Index == arrayIndex
        && outer.Index == outerIndex
        && inner.Index == innerIndex;

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
