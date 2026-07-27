using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises selected csc collection-expression lowerings back into C# 12 collection
/// expressions: inline-array span targets (<c>[e0, e1, ...]</c>),
/// PDB-discriminated exact <c>List&lt;T&gt;</c> literals lowered through
/// <c>CollectionsMarshal.SetCount</c>/<c>AsSpan</c>, and the symbol-confirmed
/// array spread-with-tail shape (<c>[..source, tail]</c>). Also raises, as its
/// complement, a
/// direct inline-array span conversion back into a cast
/// (<c>(System.Span{T})place</c>). A collection expression with
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
/// AsReadOnlySpan — nothing else references the local. Each element store is
/// either a direct indirect store or, when the element value carries a branch,
/// an element-ref address spilled to a single-use evaluation-stack slot and
/// stored through; anything outside this is left flat. The whole shape is proven
/// before any node is detached.</para>
/// </summary>
public sealed class InlineArrayCollectionPass : IIrPass
{
    const string PrivateImpl = "<PrivateImplementationDetails>";

    public string Name => "inline-array-collection";

    public void Run(IrFunction function, PassContext context)
    {
        RaiseListCollectionExpressions(function, context);

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

            // The collection expression materializes at the AsSpan call site and
            // evaluates its elements there in ascending slot order, so the raise is
            // only order-preserving when the init and element stores are a
            // contiguous run of statements in the AsSpan's own block, in ascending
            // slot order, immediately before the statement that consumes the span.
            // csc always emits exactly that; arbitrary IL can interleave a statement
            // between the stores or emit them out of slot order, which would
            // silently re-sequence element side effects — leave those flat.
            if (!ElementStoresPreserveOrder(init, stores, span))
                continue;

            // Shape proven. Detach each element value from its store, build the
            // collection expression, replace the span source, then drop the init
            // and every consumed store/spill statement.
            var elements = new List<IrExpression>(stores.Count);
            foreach (var store in stores)
            {
                store.Value.Detach();
                elements.Add(store.Value);
            }

            var collection = new CollectionExpression(element, spanType, elements);
            context.Stepper.StepOver("raise inline-array lowering to collection expression", span);
            span.ReplaceWith(collection);
            init.Detach();
            foreach (var store in stores)
                foreach (var statement in store.Consumed)
                    statement.Detach();
        }

        RaisePlaceConversions(function, context);
        RaiseElementRefs(function, context);
        RaiseArraySpreadWithTail(function, context);
        RaiseSingleElementLists(function, context);
    }

    const string SingleElementListMetadataName = "<>z__ReadOnlySingleElementList`1";

    /// <summary>
    /// Raises the csc single-element read-only collection-expression lowering,
    /// <c>new &lt;&gt;z__ReadOnlySingleElementList&lt;T&gt;(element)</c> — emitted for
    /// a one-element collection expression targeting a read-only collection
    /// interface (<c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyCollection&lt;T&gt;</c>,
    /// or <c>IReadOnlyList&lt;T&gt;</c>) — back into <c>[element]</c>. The angle-
    /// bracketed synthesized type name never parses left flat.
    ///
    /// <para>Restored to <c>[element]</c> the expression is target-typed by the
    /// surrounding context, so the raise only fires when that context supplies a
    /// matching read-only collection interface target — one of the three interfaces
    /// above over the list's own element type — from a call or constructor argument
    /// slot's parameter type, a typed local store, or the element type of an
    /// enclosing raised collection expression (a nested <c>[[element]]</c>). An
    /// extension-method receiver slot (where <c>[element]</c> would not be a legal C#
    /// receiver), a wider or erased sink reached through a no-IL reference
    /// conversion (an <c>object</c> or covariant <c>IEnumerable&lt;object&gt;</c>
    /// parameter), and any other context are left flat, so fidelity degrades
    /// honestly rather than fabricating a target type or emitting output the sink
    /// cannot construct. The construction is matched on its exact reserved metadata
    /// name, so it never collides with a user or look-alike type.</para>
    /// </summary>
    static void RaiseSingleElementLists(IrFunction function, PassContext context)
    {
        foreach (var newObject in function.Descendants.OfType<NewObject>().ToList())
        {
            if (newObject.Parent is null)
                continue; // already rewritten in this pass
            if (!TryGetSingleElementListElement(newObject.Constructor.DeclaringType, out var elementType))
                continue;
            if (newObject.Arguments is not [_])
                continue;
            if (!TryResolveCollectionTargetType(newObject, out var targetType))
                continue;
            // Only raise when the resolved use-site target is exactly one of the
            // read-only collection interfaces `[element]` can construct, over the
            // same element type. A wider or erased sink (an `object` parameter, a
            // covariant `IEnumerable<object>`) reached through a reference
            // conversion that emits no IL would print a collection expression the
            // sink cannot construct (CS9174) or silently retype the elements, so
            // leave those flat.
            if (!IsConstructibleReadOnlyListTarget(targetType, elementType))
                continue;

            var element = (IrExpression)newObject.DetachChildren()[0];
            var collection = new CollectionExpression(elementType, targetType, [element]);
            context.Stepper.StepOver("raise single-element read-only list to collection expression", newObject);
            newObject.ReplaceWith(collection);
        }
    }

    /// <summary>
    /// The element type <c>T</c> of a
    /// <c>&lt;&gt;z__ReadOnlySingleElementList&lt;T&gt;</c> construction; false for any
    /// other type. The construction is a generic instance whose definition name
    /// carries the reserved <c>&lt;&gt;</c> prefix and an arity backtick.
    /// </summary>
    static bool TryGetSingleElementListElement(TypeRef type, out TypeRef elementType)
    {
        elementType = null!;
        if (type.Kind != TypeRefKind.GenericInstance || type.ElementType is not { } definition)
            return false;
        if (!string.Equals(definition.Name, SingleElementListMetadataName, System.StringComparison.Ordinal))
            return false;
        if (type.TypeArguments is not [var argument])
            return false;
        elementType = argument;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="target"/> is a read-only collection interface a
    /// <c>[element]</c> collection expression can construct — <c>IEnumerable&lt;T&gt;</c>,
    /// <c>IReadOnlyCollection&lt;T&gt;</c>, or <c>IReadOnlyList&lt;T&gt;</c> — over exactly the
    /// list's element type. Requiring the element types to match rejects covariant
    /// or erased sinks reached through a no-IL reference conversion, which would
    /// otherwise retype the elements or target a type the collection expression
    /// cannot construct.
    /// </summary>
    static bool IsConstructibleReadOnlyListTarget(TypeRef target, TypeRef elementType)
    {
        if (target.Kind != TypeRefKind.GenericInstance || target.ElementType is not { } definition)
            return false;
        if (!string.Equals(definition.Namespace, "System.Collections.Generic", System.StringComparison.Ordinal))
            return false;
        if (definition.Name is not ("IEnumerable`1" or "IReadOnlyCollection`1" or "IReadOnlyList`1"))
            return false;
        return target.TypeArguments is [var argument] && argument.Equals(elementType);
    }

    /// <summary>
    /// The spellable target type the raised <c>[element]</c> is inferred against,
    /// read from the construction's immediate use-site: a call or constructor
    /// argument slot's parameter type, a typed local store, or the element type of
    /// an enclosing raised collection expression (a nested <c>[[element]]</c>).
    /// False (no raise) for a receiver slot or any other context — the collection
    /// expression has no natural type, so without a target the fallback keeps the
    /// flat construction.
    /// </summary>
    static bool TryResolveCollectionTargetType(NewObject newObject, out TypeRef targetType)
    {
        targetType = null!;
        switch (newObject.Parent)
        {
            case Call call:
            {
                // An extension method's static call renders in reduced instance
                // form `arg0.M(rest)` (CSharpPrinter.Members), so the first argument
                // is a member-access receiver. A collection expression has no natural
                // type and cannot be a receiver (`[x].M()` is CS9176), so leave the
                // receiver slot flat even though it maps to a parameter.
                if (call.Callee.IsExtension == MetadataFactState.Yes && newObject.ChildIndex == 0)
                    return false;
                int parameter = call.Callee.HasThis ? newObject.ChildIndex - 1 : newObject.ChildIndex;
                var parameters = call.Callee.ParameterTypes;
                if (parameter < 0 || parameter >= parameters.Length)
                    return false;
                targetType = parameters[parameter];
                return true;
            }
            case NewObject construction:
            {
                var parameters = construction.Constructor.ParameterTypes;
                if (newObject.ChildIndex < 0 || newObject.ChildIndex >= parameters.Length)
                    return false;
                targetType = parameters[newObject.ChildIndex];
                return true;
            }
            case StoreLocal store:
                targetType = store.Type;
                return true;
            // A nested single-element list is re-parented under the outer raised
            // collection expression (`[[element]]`); each element is target-typed to
            // the collection's element type, so the inner list raises too.
            case CollectionExpression collection:
                targetType = collection.ElementType;
                return true;
            default:
                return false;
        }
    }

    static void RaiseArraySpreadWithTail(IrFunction function, PassContext context)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var block in function.Descendants.OfType<Block>().ToList())
            {
                for (int i = 0; i <= block.Children.Count - 9; i++)
                {
                    if (!TryRaiseArraySpreadWithTail(function, block, i, context.Stepper))
                        continue;
                    changed = true;
                    break;
                }

                if (changed)
                    break;
            }
        }
        while (changed);
    }

    static bool TryRaiseArraySpreadWithTail(IrFunction function, Block block, int start, Stepper stepper)
    {
        if (block.Children[start] is not StoreLocal sourceStore
            || block.Children[start + 1] is not StoreLocal indexInit
            || block.Children[start + 2] is not StoreLocal arrayStore
            || block.Children[start + 3] is not StoreLocal readOnlySpanStore
            || block.Children[start + 4] is not StoreLocal spanStore
            || block.Children[start + 5] is not ExpressionStatement { Expression: Call copyTo }
            || block.Children[start + 6] is not StoreLocal indexAdvance
            || block.Children[start + 7] is not StoreElement tailStore
            || block.Children[start + 8] is not Return { Value: LoadLocal returnedArray }
            || !IsHiddenLocal(function, sourceStore.Index)
            || !IsHiddenLocal(function, indexInit.Index)
            || !IsHiddenLocal(function, arrayStore.Index)
            || !IsHiddenLocal(function, readOnlySpanStore.Index)
            || !IsHiddenLocal(function, spanStore.Index)
            || !sourceStore.Type.Equals(arrayStore.Type)
            || sourceStore.Type is not { Kind: TypeRefKind.SzArray, ElementType: { } element }
            || !indexInit.Type.Equals(TypeRef.CoreLib("System", "Int32"))
            || indexInit.Value is not Constant { Value: 0 }
            || !TryReadArrayLengthPlusOne(arrayStore.Value, sourceStore.Index, element)
            || readOnlySpanStore.Value is not NewObject readOnlySpan
            || !MemberIdentity.IsReadOnlySpanArrayConstructor(readOnlySpan, out var spanElement)
            || !spanElement.Equals(element)
            || readOnlySpan.Arguments is not [LoadLocal readOnlySource]
            || readOnlySource.Index != sourceStore.Index
            || spanStore.Value is not NewObject span
            || !MemberIdentity.IsSpanArrayConstructor(span, element)
            || span.Arguments is not [LoadLocal spanArray]
            || spanArray.Index != arrayStore.Index
            || !MemberIdentity.IsReadOnlySpanCopyTo(copyTo, element)
            || copyTo.Arguments is not [LoadLocalAddress copySource, Call slice]
            || copySource.Index != readOnlySpanStore.Index
            || !MemberIdentity.IsSpanSlice(slice)
            || slice.Arguments is not [LoadLocalAddress sliceDestination, LoadLocal sliceStart, LoadProperty sliceLength]
            || sliceDestination.Index != spanStore.Index
            || sliceStart.Index != indexInit.Index
            || !IsLengthOfLocal(sliceLength, readOnlySpanStore.Index)
            || indexAdvance.Index != indexInit.Index
            || indexAdvance.Value is not Binary { Kind: BinaryKind.Add, IsChecked: false, Left: LoadLocal advanceLeft, Right: LoadProperty advanceRight }
            || advanceLeft.Index != indexInit.Index
            || !IsLengthOfLocal(advanceRight, readOnlySpanStore.Index)
            || tailStore.Array is not LoadLocal tailArray
            || tailArray.Index != arrayStore.Index
            || tailStore.Index is not LoadLocal tailIndex
            || tailIndex.Index != indexInit.Index
            || returnedArray.Index != arrayStore.Index
            || !HasOnlyArraySpreadLocalReferences(function, sourceStore.Index, indexInit.Index, arrayStore.Index, readOnlySpanStore.Index, spanStore.Index))
        {
            return false;
        }

        var source = (IrExpression)sourceStore.DetachChildren()[0];
        var tail = (IrExpression)tailStore.DetachChildren()[2];
        var collection = new CollectionExpression(element, arrayStore.Type, [new CollectionSpreadElement(source), tail]);
        collection.InheritSourceOffset(block.Children[start + 8]);
        stepper.StepOver("raise array spread collection expression", block.Children[start + 8]);
        returnedArray.ReplaceWith(collection);

        for (int i = start + 7; i >= start; i--)
            block.Children[i].Detach();
        return true;
    }

    static bool TryReadArrayLengthPlusOne(IrExpression value, int sourceLocal, TypeRef element)
    {
        if (value is not NewArray { ElementType: var arrayElement, Length: Binary { Kind: BinaryKind.Add, IsChecked: false } length }
            || !arrayElement.Equals(element))
        {
            return false;
        }

        return IsOnePlusSourceLength(length.Left, length.Right, sourceLocal)
            || IsOnePlusSourceLength(length.Right, length.Left, sourceLocal);
    }

    static bool IsOnePlusSourceLength(IrExpression one, IrExpression length, int sourceLocal)
        => one is Constant { Value: 1 }
            && length is ArrayLength { Array: LoadLocal array }
            && array.Index == sourceLocal;

    static bool IsLengthOfLocal(LoadProperty property, int local)
        => MemberIdentity.IsSpanLengthGetter(property)
            && property.Instance is LoadLocalAddress address
            && address.Index == local;

    static bool IsHiddenLocal(IrFunction function, int local)
        => function.LocalNames.Length > local
            && (function.LocalNames[local] is null || !CSharpNaming.IsUsableIdentifier(function.LocalNames[local]!));

    static bool HasOnlyArraySpreadLocalReferences(IrFunction function, int source, int index, int array, int readOnlySpan, int span)
    {
        var counts = new Dictionary<int, (int Stores, int Loads, int Addresses)>
        {
            [source] = default,
            [index] = default,
            [array] = default,
            [readOnlySpan] = default,
            [span] = default,
        };

        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case StoreLocal store when counts.ContainsKey(store.Index):
                    counts[store.Index] = counts[store.Index] with { Stores = counts[store.Index].Stores + 1 };
                    break;
                case LoadLocal load when counts.ContainsKey(load.Index):
                    counts[load.Index] = counts[load.Index] with { Loads = counts[load.Index].Loads + 1 };
                    break;
                case LoadLocalAddress address when counts.ContainsKey(address.Index):
                    counts[address.Index] = counts[address.Index] with { Addresses = counts[address.Index].Addresses + 1 };
                    break;
            }
        }

        return counts[source] == (Stores: 1, Loads: 2, Addresses: 0)
            && counts[index] == (Stores: 2, Loads: 3, Addresses: 0)
            && counts[array] == (Stores: 1, Loads: 3, Addresses: 0)
            && counts[readOnlySpan] == (Stores: 1, Loads: 0, Addresses: 3)
            && counts[span] == (Stores: 1, Loads: 0, Addresses: 1);
    }

    sealed record ListCollectionMatch(
        IReadOnlyList<IrNode> Consumed,
        TypeRef ListType,
        TypeRef ElementType,
        IReadOnlyList<StoreIndirect> ElementStores);

    static void RaiseListCollectionExpressions(IrFunction function, PassContext context)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            for (int i = 0; i < block.Children.Count; i++)
            {
                if (TryMatchListCollectionExpression(function, block, i) is not { } match)
                    continue;

                var elements = match.ElementStores
                    .Select(store => (IrExpression)store.DetachChildren()[1])
                    .ToList();
                var collection = new CollectionExpression(match.ElementType, match.ListType, elements);
                context.Stepper.StepOver("raise List<T> marshal fill to collection expression", match.Consumed[0]);
                match.Consumed[0].ReplaceWith(new Return(collection));
                for (int j = match.Consumed.Count - 1; j >= 1; j--)
                    match.Consumed[j].Detach();
                break;
            }
        }
    }

    static ListCollectionMatch? TryMatchListCollectionExpression(IrFunction function, Block block, int index)
    {
        var statements = block.Children;
        if (index + 6 > statements.Count
            || statements[index] is not StoreLocal
            {
                Type: var countType,
                Value: Constant { Value: int count },
            } countStore
            || count <= 0
            || !countType.Equals(TypeRef.CoreLib("System", "Int32"))
            || !HasKnownHiddenLocal(function, countStore.Index)
            || index + 6 + count > statements.Count
            || statements[index + 1] is not StoreStackSlot
            {
                Slot: var listSlot,
                Value: NewObject creation,
            }
            || !TryListCreation(creation, countStore.Index, out var listType, out var elementType)
            || statements[index + 2] is not ExpressionStatement { Expression: Call setCount }
            || !MemberIdentity.IsCollectionsMarshalSetCount(setCount, out var setCountElement)
            || !setCountElement.Equals(elementType)
            || setCount.Arguments is not [LoadStackSlot { Slot: var setCountSlot }, LoadLocal { Index: var setCountCountLocal }]
            || setCountSlot != listSlot
            || setCountCountLocal != countStore.Index
            || statements[index + 3] is not StoreStackSlot
            {
                Slot: var aliasSlot,
                Value: LoadStackSlot { Slot: var copiedListSlot },
            }
            || copiedListSlot != listSlot
            || statements[index + 4] is not StoreLocal
            {
                Index: var spanLocal,
                Value: Call asSpan,
            }
            || !HasKnownHiddenLocal(function, spanLocal)
            || !MemberIdentity.IsCollectionsMarshalAsSpan(asSpan, out var asSpanElement)
            || !asSpanElement.Equals(elementType)
            || asSpan.Arguments is not [LoadStackSlot { Slot: var asSpanSlot }]
            || asSpanSlot != aliasSlot)
        {
            return null;
        }

        var elementStores = new List<StoreIndirect>(count);
        for (int i = 0; i < count; i++)
        {
            if (statements[index + 5 + i] is not StoreIndirect
                {
                    IsVolatile: false,
                    Type: var storeType,
                    Address: LoadProperty item,
                } store
                || storeType is null
                || !storeType.Equals(elementType)
                || !TrySpanItemAddress(item, spanLocal, elementType, i))
            {
                return null;
            }

            elementStores.Add(store);
        }

        int returnIndex = index + 5 + count;
        if (statements[returnIndex] is not Return { Value: LoadStackSlot { Slot: var returnSlot } }
            || returnSlot != aliasSlot)
        {
            return null;
        }

        var consumed = statements.Skip(index).Take(count + 6).ToArray();
        if (!ReferencesLocalOnlyWithin(function, countStore.Index, consumed)
            || !ReferencesLocalOnlyWithin(function, spanLocal, consumed)
            || !ReferencesStackSlotOnlyWithin(function, listSlot, consumed)
            || !ReferencesStackSlotOnlyWithin(function, aliasSlot, consumed))
        {
            return null;
        }

        return new ListCollectionMatch(consumed, listType, elementType, elementStores);
    }

    static bool TryListCreation(NewObject creation, int countLocal, out TypeRef listType, out TypeRef elementType)
    {
        listType = null!;
        elementType = null!;
        if (creation.Constructor is not
            {
                Name: ".ctor",
                HasThis: true,
                ParameterTypes: [var capacity],
            }
            || !capacity.Equals(TypeRef.CoreLib("System", "Int32"))
            || creation.Arguments is not [LoadLocal { Index: var capacityLocal }]
            || capacityLocal != countLocal
            || !MemberIdentity.IsGenericListType(creation.Constructor.DeclaringType, out elementType))
        {
            return false;
        }

        listType = creation.Constructor.DeclaringType;
        return true;
    }

    static bool TrySpanItemAddress(LoadProperty item, int spanLocal, TypeRef elementType, int index)
        => MemberIdentity.IsSpanIndexerGetter(item)
            && item.Instance is LoadLocalAddress { Index: var local }
            && local == spanLocal
            && item.IndexArguments is [Constant { Value: int actualIndex, Type: var indexType }]
            && actualIndex == index
            && indexType.Equals(TypeRef.CoreLib("System", "Int32"))
            && item.ResultType is { Kind: TypeRefKind.ByRef, ElementType: { } actualElement }
            && actualElement.Equals(elementType);

    static bool ReferencesLocalOnlyWithin(IrFunction function, int local, IReadOnlyCollection<IrNode> allowed)
        => function.Descendants
            .Where(node => node switch
            {
                LoadLocal load => load.Index == local,
                StoreLocal store => store.Index == local,
                LoadLocalAddress address => address.Index == local,
                _ => false,
            })
            .All(node => ReferenceOwnership.IsInsideAny(node, allowed));

    static bool ReferencesStackSlotOnlyWithin(IrFunction function, int stackSlot, IReadOnlyCollection<IrNode> allowed)
        => function.Descendants
            .Where(node => node switch
            {
                LoadStackSlot load => load.Slot == stackSlot,
                StoreStackSlot store => store.Slot == stackSlot,
                _ => false,
            })
            .All(node => ReferenceOwnership.IsInsideAny(node, allowed));

    static bool HasKnownHiddenLocal(IrFunction function, int index)
        => index >= 0
            && index < function.LocalNames.Length
            && string.IsNullOrWhiteSpace(function.LocalNames[index]);

    /// <summary>
    /// Raises a direct inline-array span conversion — an
    /// <c>InlineArrayAsSpan</c>/<c>InlineArrayAsReadOnlySpan</c> over a
    /// pre-existing inline-array <em>place</em> (a field, parameter, array
    /// element, or init-only local buffer) — to a C# cast
    /// <c>(Span&lt;T&gt;)place</c>. This is the
    /// complement of the collection-expression recovery above: there is no
    /// synthesized buffer to fold, just an <c>[InlineArray(N)]</c> location an
    /// implicit/explicit span conversion was applied to (e.g.
    /// <c>BitVector256.Set</c> viewing its <c>_values</c> field as a
    /// <c>Span&lt;uint&gt;</c>).
    ///
    /// <para>Scoped to field/parameter/array-element places, plus locals that are
    /// only default-initialized and never written through
    /// <c>InlineArrayElementRef</c>. That keeps synthesized collection-expression
    /// temporaries disjoint from this direct-conversion path. The span the call
    /// produced is the cast's target type, so replacing the call leaves the
    /// surrounding type unchanged and the compiler re-lowers the cast to the same
    /// AsSpan call.</para>
    /// </summary>
    static void RaisePlaceConversions(IrFunction function, PassContext context)
    {
        foreach (var span in function.Descendants.OfType<Call>().ToList())
        {
            if (span.Parent is null)
                continue;
            if (!MemberIdentity.IsInlineArraySpanConversionHelper(span, out var arrayType))
                continue;
            if (span.ResultType is not { } spanType)
                continue;
            if (span.Arguments is not [var placeCandidate, Constant { Value: int }])
                continue;
            if (!CanRaisePlaceConversion(function, placeCandidate, arrayType))
                continue;

            var place = (IrExpression)span.DetachChildren()[0];
            var conversion = new InlineArraySpanConversion(spanType, place);
            context.Stepper.StepOver("raise inline-array span conversion to cast", span);
            span.ReplaceWith(conversion);
        }
    }

    static bool CanRaisePlaceConversion(IrFunction function, IrExpression place, TypeRef arrayType)
        => place switch
        {
            LoadFieldAddress field => field.Field.Type.Equals(arrayType),
            LoadArgumentAddress argument => argument.Type.Equals(arrayType),
            LoadElementAddress element => element.ElementType.Equals(arrayType),
            LoadLocalAddress local => local.Type.Equals(arrayType) && IsInitOnlyLocalBuffer(function, local.Index),
            _ => false,
        };

    static bool IsInitOnlyLocalBuffer(IrFunction function, int local)
    {
        if (function.Descendants.OfType<LoadLocal>().Any(load => load.Index == local)
            || function.Descendants.OfType<StoreLocal>().Any(store => store.Index == local)
            || function.Descendants.OfType<Call>().Any(call =>
                IsInlineArrayElementRef(call.Callee, out _, out _)
                && call.Arguments.FirstOrDefault() is LoadLocalAddress address
                && address.Index == local))
        {
            return false;
        }

        return function.Descendants.OfType<InitObject>()
            .Count(init => init.Address is LoadLocalAddress address && address.Index == local) == 1;
    }

    /// <summary>
    /// Raises the compiler's inline-array element-address helpers to ordinary
    /// inline-array indexer addresses. The helpers are emitted for direct
    /// reads/writes of a real <c>[InlineArray]</c> buffer, not just collection
    /// expressions; left flat, the <c>&lt;PrivateImplementationDetails&gt;</c>
    /// method name caps fidelity and cannot parse as C#.
    /// </summary>
    static void RaiseElementRefs(IrFunction function, PassContext context)
    {
        // Methods that also view the same inline-array buffers as spans often have
        // additional validity work (ref locals/unsigned stores). In that mixed
        // shape, raise only the first-element helper in instance methods (the
        // BigInteger.Add(uint) form); indexed helpers and static mixed arithmetic
        // bodies stay Partial for dedicated validity slices.
        bool hasSpanConversion = function.Descendants.OfType<InlineArraySpanConversion>().Any();

        foreach (var elementRef in function.Descendants.OfType<Call>().ToList())
        {
            if (elementRef.Parent is null)
                continue;
            if (!IsInlineArrayElementRef(elementRef.Callee, out bool first, out bool readOnly))
                continue;
            if (hasSpanConversion && (!first || !function.Signature.HasThis))
                continue;
            if (elementRef.Callee.TypeArguments is not [var arrayType, var element])
                continue;

            if (first)
            {
                if (elementRef.Arguments is not [_])
                    continue;
            }
            else
            {
                if (elementRef.Arguments is not [_, _])
                    continue;
            }
            if (!CanPlaceFromAddress(elementRef.Arguments[0], arrayType))
                continue;

            var children = elementRef.DetachChildren().Cast<IrExpression>().ToArray();
            var place = PlaceFromAddress(children[0], arrayType)!;
            var index = first ? new Constant(0, TypeRef.CoreLib("System", "Int32")) : children[1];
            var address = new LoadElementAddress(element, place, index, readOnly);
            context.Stepper.StepOver("raise inline-array element ref to indexed address", elementRef);
            elementRef.ReplaceWith(address);
        }
    }

    // The receiver address must be a nameable place whose storage type is the helper's
    // inline-array type argument (TBuffer). Requiring that equality — together with the
    // generated helper identity — proves the place is a real inline-array buffer, so a
    // genuine helper MethodSpec'd over a non-inline-array place is not raised to `plain[i]`
    // (#1365).
    static bool CanPlaceFromAddress(IrExpression address, TypeRef arrayType) => address switch
    {
        LoadLocalAddress local => local.Type.Equals(arrayType),
        LoadArgumentAddress argument => argument.Type.Equals(arrayType),
        LoadFieldAddress field => field.Field.Type.Equals(arrayType),
        LoadElementAddress element => element.ElementType.Equals(arrayType),
        LoadLocal { ResultType: { Kind: TypeRefKind.ByRef, ElementType: { } element } } => element.Equals(arrayType),
        _ => false,
    };

    static IrExpression? PlaceFromAddress(IrExpression address, TypeRef arrayType) => address switch
    {
        LoadLocalAddress local => new LoadLocal(local.Index, local.Type),
        LoadArgumentAddress argument => new LoadArgument(argument.Index, argument.Name, argument.Type),
        LoadFieldAddress field => new LoadField(
            field.Field,
            field.Instance is null ? null : (IrExpression)field.DetachChildren()[0]),
        LoadElementAddress element => element,
        LoadLocal { ResultType: { Kind: TypeRefKind.ByRef, ElementType: { } element } } local when element.Equals(arrayType)
            => new LoadIndirect(arrayType, local),
        _ => null,
    };

    static bool IsPrivateImpl(MethodRef callee, string name)
        => callee.Name == name && callee.DeclaringType.Name == PrivateImpl;

    static bool IsInlineArrayElementRef(MethodRef callee, out bool first, out bool readOnly)
        => GeneratedCodeIdentity.IsInlineArrayElementRefHelper(callee, out first, out readOnly);

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
    /// A single inline-array element store, resolved to the stored value
    /// expression and the block statements that must be removed when the store is
    /// folded into the collection expression. A direct store consumes one
    /// statement; a spilled store consumes its address spill, its indirect store,
    /// and (when the value is itself spilled) its value spill.
    /// </summary>
    sealed record InlineArrayElementStore(int Slot, IrExpression Value, IReadOnlyList<IrNode> Consumed);

    /// <summary>
    /// The N element stores for the inline-array local, ordered by slot index, or
    /// null when they do not cover exactly slots 0..N-1.
    ///
    /// <para>Two store shapes are recognized. The common shape is a direct
    /// <c>*(InlineArrayElementRef(ref local, i)) = value</c> statement. When an
    /// element's value contains a branch (a ternary or short-circuit), csc must
    /// evaluate the element-ref address before that branch and so spills the
    /// <c>ref</c> to an evaluation-stack slot: <c>slot_a =
    /// InlineArrayElementRef(ref local, i); [slot_v = value;] *slot_a =
    /// slot_v-or-value;</c>. That spilled shape is recognized too, but only when
    /// every spill slot is written once and read once — so the address computation
    /// (which has no observable effect once the collection expression drops it) and
    /// the value can be recovered and re-sequenced into slot order safely. Anything
    /// else (a multi-use spill slot, a volatile store, an unrecovered stack-slot
    /// value) returns null and the whole shape is left flat.</para>
    /// </summary>
    static List<InlineArrayElementStore>? CollectElementStores(IrFunction function, int local, int count)
    {
        var elementRefs = function.Descendants.OfType<Call>()
            .Where(c => IsPrivateImpl(c.Callee, "InlineArrayElementRef")
                && c.Arguments is [LoadLocalAddress { Index: var refLocal }, Constant { Value: int }]
                && refLocal == local)
            .ToList();
        if (elementRefs.Count != count)
            return null;

        var byIndex = new SortedDictionary<int, InlineArrayElementStore>();
        foreach (var elementRef in elementRefs)
        {
            if (elementRef.Arguments is not [_, Constant { Value: int slot }])
                return null;
            if (slot < 0 || slot >= count || byIndex.ContainsKey(slot))
                return null;
            if (ResolveElementStore(function, elementRef, slot) is not { } resolved)
                return null;
            byIndex[slot] = resolved;
        }

        return byIndex.Count == count ? byIndex.Values.ToList() : null;
    }

    /// <summary>
    /// Verifies that the collection's <paramref name="init"/> and element
    /// <paramref name="stores"/> can be lifted to the <paramref name="span"/> call
    /// site without re-sequencing any observable side effect.
    ///
    /// <para>The raise removes the init and store statements and evaluates the
    /// element values, in ascending slot order, at the position of the span call.
    /// That preserves program order only when those statements form a contiguous
    /// run in the span-consuming statement's own block, in ascending slot order,
    /// immediately before that statement — the exact shape csc emits (default the
    /// buffer, fill slots 0..N-1 in order, then read the span). Arbitrary IL
    /// (ilasm, obfuscators, other front ends) can interleave an unrelated statement
    /// within the fill or between the fill and the span, or store the slots out of
    /// order; lifting either would move element side effects across a statement or
    /// invert their evaluation order, silently emitting wrong C#. It can also
    /// evaluate an effectful expression inside the consumer before the span itself;
    /// lifting the elements to the span's position would then move them past that
    /// effect. Anything but the canonical run with a side-effect-free consumer
    /// prefix returns false so the shape is left flat.</para>
    /// </summary>
    static bool ElementStoresPreserveOrder(IrNode init, List<InlineArrayElementStore> stores, Call span)
    {
        // The span-consuming statement: the innermost ancestor of the span call
        // that is a direct child of a block.
        IrNode consumer = span;
        while (consumer.Parent is { } parent and not Block)
            consumer = parent;
        if (consumer.Parent is not Block block)
            return false;
        int consumerIndex = consumer.ChildIndex;

        // Every consumed statement (the init and each store's statements) keyed to
        // its owning slot; the init sorts first with a sentinel slot.
        var run = new List<(int Index, int Slot)>(1 + stores.Sum(s => s.Consumed.Count));
        if (init.Parent != block || init.ChildIndex >= consumerIndex)
            return false;
        run.Add((init.ChildIndex, -1));
        foreach (var store in stores)
            foreach (var statement in store.Consumed)
            {
                if (statement.Parent != block || statement.ChildIndex >= consumerIndex)
                    return false;
                run.Add((statement.ChildIndex, store.Slot));
            }

        run.Sort((a, b) => a.Index.CompareTo(b.Index));

        // Contiguous run of distinct statements ending immediately before the
        // consuming statement: no unrelated statement sits within the fill or
        // between the fill and the span.
        if (run[^1].Index != consumerIndex - 1)
            return false;
        for (int i = 1; i < run.Count; i++)
            if (run[i].Index != run[i - 1].Index + 1)
                return false;

        // Slots non-decreasing across the run (init's sentinel sorts first): the
        // block order of the element writes matches ascending slot = source order.
        for (int i = 1; i < run.Count; i++)
            if (run[i].Slot < run[i - 1].Slot)
                return false;

        // Block-level order is not sufficient on its own: the element values are
        // lifted to the span's position *inside* the consuming statement, so
        // anything the consumer evaluates before the span would move ahead of
        // those element side effects. csc never puts an effect there — a prefix
        // argument or receiver evaluated before the collection is spilled to a
        // stack slot (a side-effect-free load) ahead of the fill, so at the span
        // position only such loads precede it. Arbitrary IL (ilasm, obfuscators)
        // can instead evaluate an effectful expression inline before the span
        // (e.g. `Consume(PrefixEffect(), span)`); lifting the elements past it
        // would invert their order, so leave that flat.
        return NothingEffectfulBefore(consumer, span);
    }

    /// <summary>
    /// Whether every expression the <paramref name="consumer"/> statement evaluates
    /// before <paramref name="span"/> is side-effect free, walking the parent chain
    /// from the span up to (but excluding) the consumer and checking each earlier
    /// sibling. Mirrors the same-named guard in <see cref="RangeFromGetSubArrayPass"/>.
    /// </summary>
    static bool NothingEffectfulBefore(IrNode consumer, IrNode span)
    {
        for (IrNode node = span; node != consumer; node = node.Parent!)
        {
            if (node.Parent is not { } parent)
                return false;
            for (int i = 0; i < node.ChildIndex; i++)
                if (parent.Children[i] is not IrExpression sibling || !IsSideEffectFree(sibling))
                    return false;
        }

        return true;
    }

    /// <summary>
    /// Allow-list of non-observable expressions, mirroring the same-named helpers
    /// in <see cref="RangeFromGetSubArrayPass"/> and <see cref="StackAllocSpanPass"/>:
    /// anything outside the proven-safe set is treated as possibly effectful.
    /// </summary>
    static bool IsSideEffectFree(IrExpression expression) => expression switch
    {
        Constant or LoadLocal or LoadArgument or LoadStackSlot
            or LoadLocalAddress or LoadArgumentAddress or SizeOf => true,
        Unary unary => IsSideEffectFree(unary.Operand),
        Binary { Kind: BinaryKind.Divide or BinaryKind.Remainder } => false,
        Binary { IsChecked: true } => false,
        Binary binary => IsSideEffectFree(binary.Left) && IsSideEffectFree(binary.Right),
        Convert { IsChecked: true } => false,
        Convert convert => IsSideEffectFree(convert.Operand),
        _ => false,
    };

    /// <summary>
    /// Resolves the store that writes <paramref name="elementRef"/>'s slot: a
    /// direct indirect store, or an address spilled to a single-use evaluation-stack
    /// slot and stored through — recovering a single-use value spill so the element
    /// is the real expression, never a dangling stack-slot load. Null for any other
    /// shape.
    /// </summary>
    static InlineArrayElementStore? ResolveElementStore(IrFunction function, Call elementRef, int slot)
    {
        switch (elementRef.Parent)
        {
            // Direct: *(InlineArrayElementRef(ref local, slot)) = value.
            case StoreIndirect { IsVolatile: false, Parent: Block } direct when direct.Address == elementRef:
                return new InlineArrayElementStore(slot, direct.Value, [direct]);

            // Address spilled: slot_a = InlineArrayElementRef(ref local, slot); the
            // stored value flows through slot_a exactly once.
            case StoreStackSlot { Parent: Block, Slot: var addressSlot } addressSpill when addressSpill.Value == elementRef:
            {
                if (StackSlotStoreCount(function, addressSlot) != 1 || StackSlotLoadCount(function, addressSlot) != 1)
                    return null;
                var addressLoad = function.Descendants.OfType<LoadStackSlot>().Single(l => l.Slot == addressSlot);
                if (addressLoad.Parent is not StoreIndirect { IsVolatile: false, Parent: Block } indirect
                    || indirect.Address != addressLoad)
                {
                    return null;
                }

                var value = indirect.Value;
                List<IrNode> consumed = [addressSpill, indirect];

                // A stack-slot load cannot be a collection element on its own; when
                // the value was itself spilled, recover it — but only from a clean
                // single-def/single-use spill, else leave the whole shape flat.
                if (value is LoadStackSlot { Slot: var valueSlot })
                {
                    if (StackSlotStoreCount(function, valueSlot) != 1 || StackSlotLoadCount(function, valueSlot) != 1)
                        return null;
                    var valueSpill = function.Descendants.OfType<StoreStackSlot>().Single(s => s.Slot == valueSlot);
                    if (valueSpill.Parent is not Block)
                        return null;
                    value = valueSpill.Value;
                    consumed = [addressSpill, valueSpill, indirect];
                }

                return new InlineArrayElementStore(slot, value, consumed);
            }

            default:
                return null;
        }
    }

    static int StackSlotStoreCount(IrFunction function, int slot)
        => function.Descendants.OfType<StoreStackSlot>().Count(s => s.Slot == slot);

    static int StackSlotLoadCount(IrFunction function, int slot)
        => function.Descendants.OfType<LoadStackSlot>().Count(l => l.Slot == slot);
}
