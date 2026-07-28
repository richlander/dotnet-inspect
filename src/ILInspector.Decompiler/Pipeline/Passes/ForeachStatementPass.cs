using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises supported compiler <c>foreach</c> lowerings back to a
/// <see cref="ForeachStatement"/>.
///
/// <para><b>Async enumerator form</b> — the runtime-async
/// <c>IAsyncEnumerable&lt;T&gt;</c> lowering is first reduced to an
/// <c>await using</c> around <c>while (await e.MoveNextAsync())</c>. Exact core
/// interface identities plus the Current store recover <c>await foreach</c>.
/// Source-named enumerator or default-token locals veto the raise when symbols
/// are available; without symbols, the exact core-interface lowering is
/// accepted, matching the synchronous core-interface policy.</para>
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
/// <para><b>Rectangular array form</b> — a rank-N (N &gt;= 2) array lowers to N
/// nested indexed loops over hidden array-copy, per-dimension upper-bound, and
/// index locals, using <c>GetLowerBound(d)</c>, <c>GetUpperBound(d)</c>, and the
/// array's generated <c>Get(i0, .., iN-1)</c> accessor. The rank is read from the
/// array copy's type, so rank 2 and higher ranks match through one path.</para>
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
            if (TryMatchAsyncEnumerator(function, usingStatement) is { } asyncMatch)
            {
                var asyncCollection = asyncMatch.Collection;
                if (asyncCollection.Parent is not null)
                    asyncCollection.Detach();
                asyncMatch.CurrentStore.Detach();
                var asyncBody = asyncMatch.Loop.Body;
                asyncBody.Detach();
                var awaitForeachStatement = new ForeachStatement(
                    asyncMatch.CurrentStore.Index,
                    asyncMatch.CurrentStore.Type,
                    asyncCollection,
                    asyncBody,
                    asyncMatch.ConsumedMemberRefs,
                    isAwait: true);
                context.Stepper.StepOver("raise async enumerator loop to await foreach", usingStatement);
                usingStatement.ReplaceWith(awaitForeachStatement);
                asyncMatch.TokenInitialization.Detach();
                continue;
            }

            if (TryMatchEnumerator(function, usingStatement) is not { } match)
                continue;

            var collection = match.Collection;
            if (collection.Parent is not null)
                collection.Detach();

            int localIndex;
            if (match.HoistedCurrentStore is { } hoisted)
            {
                localIndex = hoisted.Index;
                hoisted.Detach();
            }
            else
            {
                // The single-use iteration variable was folded into its one use
                // before this pass ran (ExpressionInliningPass), so no `item =
                // e.Current` store survives to carry the slot. Allocate a fresh
                // foreach variable and rebind the inline Current read to it. The
                // enumerator is advanced only by the loop condition, so the read
                // is invariant across the body — hoisting it to the foreach
                // header is the exact inverse of the earlier inline.
                localIndex = function.AddLocal(match.LocalType);
                match.InlineCurrent!.ReplaceWith(new LoadLocal(localIndex, match.LocalType));
            }

            var body = match.Loop.Body;
            body.Detach();
            var foreachStatement = new ForeachStatement(localIndex, match.LocalType, collection, body, match.ConsumedMemberRefs);
            context.Stepper.StepOver("raise enumerator loop to foreach", usingStatement);
            usingStatement.ReplaceWith(foreachStatement);
        }

        foreach (var loop in function.Descendants.OfType<WhileLoop>().ToList())
        {
            if (TryMatchPattern(function, loop) is not { } match)
                continue;

            var collection = match.Collection;
            if (collection.Parent is not null)
                collection.Detach();
            match.CurrentStore.Detach();
            var body = loop.Body;
            body.Detach();
            var foreachStatement = new ForeachStatement(match.CurrentStore.Index, match.CurrentStore.Type, collection, body, match.ConsumedMemberRefs);
            context.Stepper.StepOver("raise pattern enumerator loop to foreach", loop);
            loop.ReplaceWith(foreachStatement);
            match.EnumeratorStore.Detach();
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
            if (TryMatchRectangularArray(function, loop) is { } rectangular)
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
        foreach (var upperStore in candidate.UpperStores)
            upperStore.Detach();
    }

    static void Pool(Dictionary<int, HashSet<IrNode>> allowedBySlot, int slot, IReadOnlyCollection<IrNode> allowed)
    {
        if (!allowedBySlot.TryGetValue(slot, out var set))
            allowedBySlot[slot] = set = new HashSet<IrNode>(ReferenceEqualityComparer.Instance);
        set.UnionWith(allowed);
    }

    sealed record EnumeratorMatch(
        IrExpression Collection,
        WhileLoop Loop,
        TypeRef LocalType,
        StoreLocal? HoistedCurrentStore,
        LoadProperty? InlineCurrent,
        ImmutableArray<MethodRef> ConsumedMemberRefs);

    sealed record AsyncEnumeratorMatch(
        InitObject TokenInitialization,
        IrExpression Collection,
        WhileLoop Loop,
        StoreLocal CurrentStore,
        ImmutableArray<MethodRef> ConsumedMemberRefs);

    static AsyncEnumeratorMatch? TryMatchAsyncEnumerator(
        IrFunction function,
        UsingStatement usingStatement)
    {
        int enumeratorIndex = usingStatement.LocalIndex;
        if (!usingStatement.IsAwait
            || HasSourceLocalName(function, enumeratorIndex)
            || usingStatement.Parent is not Block block
            || usingStatement.ChildIndex == 0
            || block.Children[usingStatement.ChildIndex - 1] is not InitObject
            {
                Type: var tokenType,
                Address: LoadLocalAddress tokenAddress,
            } tokenInitialization
            || !MemberIdentity.IsCoreLibraryType(
                tokenType,
                "System.Threading",
                "CancellationToken")
            || HasSourceLocalName(function, tokenAddress.Index)
            || usingStatement.Resource is not Call getAsyncEnumerator
            || !TryMatchGetAsyncEnumerator(
                getAsyncEnumerator,
                tokenAddress.Index,
                usingStatement.ResourceType,
                out var collectionReceiver,
                out var itemType))
        {
            return null;
        }

        if (usingStatement.Body.Blocks is not [{ Children: [WhileLoop loop] }]
            || loop.Condition is not AwaitExpression { Operand: Call moveNextAsync }
            || !IsMoveNextAsyncOn(
                moveNextAsync,
                enumeratorIndex,
                usingStatement.ResourceType)
            || loop.Body.Children is not
                [StoreLocal { Value: LoadProperty current } currentStore, ..]
            || !currentStore.Type.Equals(itemType)
            || !IsAsyncCurrentOn(
                current,
                enumeratorIndex,
                usingStatement.ResourceType,
                itemType)
            || loop.Body.Children.Skip(1)
                .Any(child => ReferencesLocal(child, enumeratorIndex))
            || !ReferenceOwnership.LocalReferencesOnlyWithin(
                function,
                tokenAddress.Index,
                [tokenInitialization, usingStatement]))
        {
            return null;
        }

        return new AsyncEnumeratorMatch(
            tokenInitialization,
            CollectionValue(collectionReceiver),
            loop,
            currentStore,
            [
                getAsyncEnumerator.Callee,
                moveNextAsync.Callee,
                current.Accessor,
                .. usingStatement.ConsumedMemberRefs,
            ]);
    }

    static bool TryMatchGetAsyncEnumerator(
        Call call,
        int tokenIndex,
        TypeRef resourceType,
        out IrExpression collection,
        out TypeRef itemType)
    {
        collection = null!;
        itemType = null!;
        if (call.Callee is not
            {
                Name: "GetAsyncEnumerator",
                HasThis: true,
                TypeArguments.IsEmpty: true,
                ParameterTypes: [var cancellationToken],
                DeclaringType:
                {
                    Kind: TypeRefKind.GenericInstance,
                    TypeArguments: [var declaredItemType],
                } declaringType,
                ReturnType:
                {
                    Kind: TypeRefKind.GenericInstance,
                    TypeArguments: [var returnedItemType],
                } returnType,
            }
            || call.Arguments is not [var receiver, LoadLocal token]
            || token.Index != tokenIndex
            || !MemberIdentity.IsCoreLibraryType(
                cancellationToken,
                "System.Threading",
                "CancellationToken")
            || !MemberIdentity.IsCoreLibraryType(
                declaringType,
                "System.Collections.Generic",
                "IAsyncEnumerable`1")
            || !MemberIdentity.IsCoreLibraryType(
                returnType,
                "System.Collections.Generic",
                "IAsyncEnumerator`1")
            || !declaredItemType.Equals(returnedItemType)
            || !returnType.Equals(resourceType))
        {
            return false;
        }

        collection = receiver;
        itemType = declaredItemType;
        return true;
    }

    static bool IsMoveNextAsyncOn(
        Call call,
        int enumeratorIndex,
        TypeRef enumeratorType)
        => call is
            {
                Callee:
                {
                    Name: "MoveNextAsync",
                    HasThis: true,
                    TypeArguments.IsEmpty: true,
                    ParameterTypes.IsEmpty: true,
                    DeclaringType: var declaringType,
                    ReturnType:
                    {
                        Kind: TypeRefKind.GenericInstance,
                        TypeArguments: [var resultType],
                    } returnType,
                },
                Arguments: [var receiver],
            }
            && MemberIdentity.IsCoreLibraryType(
                declaringType,
                "System.Collections.Generic",
                "IAsyncEnumerator`1")
            && declaringType.Equals(enumeratorType)
            && MemberIdentity.IsCoreLibraryType(
                returnType,
                "System.Threading.Tasks",
                "ValueTask`1")
            && MemberIdentity.IsCoreLibraryType(resultType, "System", "Boolean")
            && IsEnumeratorReceiver(receiver, enumeratorIndex);

    static bool IsAsyncCurrentOn(
        LoadProperty property,
        int enumeratorIndex,
        TypeRef enumeratorType,
        TypeRef itemType)
        => property is
            {
                HasInstance: true,
                PropertyName: "Current",
                Instance: { } receiver,
                Accessor:
                {
                    Name: "get_Current",
                    HasThis: true,
                    TypeArguments.IsEmpty: true,
                    ParameterTypes.IsEmpty: true,
                    DeclaringType:
                    {
                        Kind: TypeRefKind.GenericInstance,
                        TypeArguments: [var declaredItemType],
                    } declaringType,
                    ReturnType: var returnType,
                },
            }
            && MemberIdentity.IsCoreLibraryType(
                declaringType,
                "System.Collections.Generic",
                "IAsyncEnumerator`1")
            && declaringType.Equals(enumeratorType)
            && declaredItemType.Equals(itemType)
            && returnType.Equals(itemType)
            && IsEnumeratorReceiver(receiver, enumeratorIndex);

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
        // The collection is either a known IEnumerable (raised regardless of
        // symbols — a hand-written `using` over an IEnumerator is vanishingly
        // rare) or a custom pattern enumerator: a struct/class exposing
        // GetEnumerator/MoveNext/Current without the IEnumerable interface, such
        // as List<T>.Enumerator. A pattern enumerator shares its IL with a
        // hand-written `using (var e = x.GetEnumerator()) while (e.MoveNext())`,
        // so it raises only when the PDB proves the enumerator local is
        // compiler-hidden — a name slot exists and is empty (HasSourceLocalName
        // was already rejected above), the same discriminator the bare-while
        // pattern path uses. Without symbols the two are indistinguishable, so
        // the slot check declines.
        if (!IsSupportedEnumeratorCollection(getEnumerator)
            && !HasLocalNameSlot(function, enumeratorIndex))
        {
            return null;
        }

        if (usingStatement.Body.Blocks is not [{ Children: [WhileLoop loop] }]
            || !IsMoveNextOn(loop.Condition, enumeratorIndex))
        {
            return null;
        }

        var moveNext = (Call)loop.Condition;
        var collection = CollectionValue(getEnumerator.Arguments[0]);

        // Hoisted form: the loop body opens with `item = e.Current` — the
        // canonical compiler foreach lowering — and the enumerator is otherwise
        // unreferenced across the body.
        if (loop.Body.Children is [StoreLocal { Value: LoadProperty current } currentStore, ..]
            && IsCurrentOn(current, enumeratorIndex)
            && !loop.Body.Children.Skip(1).Any(child => ReferencesLocal(child, enumeratorIndex)))
        {
            return new EnumeratorMatch(
                collection,
                loop,
                currentStore.Type,
                currentStore,
                InlineCurrent: null,
                [getEnumerator.Callee, moveNext.Callee, current.Accessor, .. usingStatement.ConsumedMemberRefs]);
        }

        // Inline form: the iteration variable was used exactly once, so
        // ExpressionInliningPass folded its `item = e.Current` store into that use
        // before this pass ran (e.g. JsonElement.DeepEquals, #3164). No hoisted
        // store survives; the enumerator is then referenced only by the condition
        // MoveNext and one `e.Current` read somewhere in the body.
        //
        // The single read is only a real foreach header when csc emitted its
        // `get_Current` as the *first operation of the loop body*, leaving the
        // value live across the rest of the iteration; the live-range inliner
        // then sinks that read to its one use. Re-hoisting it to a fresh foreach
        // variable at the loop top is the exact inverse of that sink and
        // re-lowers opcode-identically — but only because the original read ran
        // first, every iteration. A hand-written `while` that reads `e.Current`
        // later in the body — inside an `if` (e.g. Enumerable.ElementAt's
        // `if (index == 0) return e.Current;`), on the right of `??=`, or after
        // any preceding side-effecting statement — is byte-for-byte identical
        // here after inlining, yet hoisting it to the loop top would change how
        // often `get_Current` runs and reorder it ahead of those operations
        // (observable if either throws or mutates shared state).
        // ReadOriginatesAtLoopBodyTop recovers that provenance from source
        // offsets: the read's subtree must carry the minimum IL offset of every
        // operation in the loop body's own scope. Also require the read to sit
        // outside any nested lambda/local function, where a hoist would change
        // when it runs.
        var currentReads = loop.Body.Descendants.OfType<LoadProperty>()
            .Where(p => IsCurrentOn(p, enumeratorIndex))
            .ToList();
        if (currentReads is [var inlineCurrent]
            && !IsInsideNestedFunction(inlineCurrent, loop.Body)
            && ReadOriginatesAtLoopBodyTop(inlineCurrent, loop.Body)
            && EnumeratorReferencedOnlyWithinLoopBy(loop, enumeratorIndex, moveNext, inlineCurrent))
        {
            return new EnumeratorMatch(
                collection,
                loop,
                inlineCurrent.Accessor.ReturnType,
                HoistedCurrentStore: null,
                inlineCurrent,
                [getEnumerator.Callee, moveNext.Callee, inlineCurrent.Accessor, .. usingStatement.ConsumedMemberRefs]);
        }

        return null;
    }

    // Within the loop (condition + body), the enumerator slot may be referenced
    // only by the MoveNext receiver and the single inline Current receiver — any
    // other read/write means the slot is not pure foreach scaffolding.
    static bool EnumeratorReferencedOnlyWithinLoopBy(
        WhileLoop loop, int enumeratorIndex, Call moveNext, LoadProperty inlineCurrent)
    {
        var allowed = new HashSet<IrNode>(ReferenceEqualityComparer.Instance);
        if (moveNext.Arguments is [var moveNextReceiver])
            allowed.Add(moveNextReceiver);
        if (inlineCurrent.Instance is { } currentReceiver)
            allowed.Add(currentReceiver);
        foreach (var node in loop.Descendants)
        {
            bool references = node switch
            {
                LoadLocal load => load.Index == enumeratorIndex,
                LoadLocalAddress address => address.Index == enumeratorIndex,
                StoreLocal store => store.Index == enumeratorIndex,
                _ => false,
            };
            if (references && !allowed.Contains(node))
                return false;
        }
        return true;
    }

    static bool IsInsideNestedFunction(IrNode node, Block boundary)
    {
        for (var current = node.Parent; current is not null && current != boundary; current = current.Parent)
            if (current is Lambda or LocalFunctionStatement)
                return true;
        return false;
    }

    // True iff the inline `e.Current` read is a genuine compiler foreach header:
    // csc emits a foreach header's `get_Current` as the *first instruction of the
    // loop body*, leaving the value live until the live-range inliner sinks it to
    // its one use. We prove that origin from the loop body block's import-stamped
    // entry offset (Block.StartOffset — the IL offset of its first instruction,
    // fixed at import and immune to later passes that erase interior node
    // offsets, e.g. NullCoalescingAssignmentPass dropping the `??=` null-check's
    // offset): the read's subtree must carry exactly that entry offset. A
    // conditional read (ElementAt's `if (index == 0) return e.Current;`), a `??=`
    // read, or a read after any preceding statement starts later than the entry,
    // so it is declined — re-hoisting it would move `get_Current` ahead of that
    // operation, changing how often it runs and its order (observable if either
    // throws or mutates shared state). Fails closed when the read carries no
    // offset. Exception regions carry no IL of their own (they are pure PE
    // metadata), so a hand-written `try { x = e.Current; }` at the body top has
    // `get_Current` as the first *executable* instruction yet is protected by the
    // handler; the offset check cannot see that, so we additionally reject a read
    // wrapped in any try/catch/finally within the body. (A real foreach whose
    // iteration variable is used inside a `try` must *store* it — crossing the
    // region boundary forbids the stack-cached single-use form — so it takes the
    // hoisted matcher, never this inline path; declining here loses no real
    // foreach.) A read inside a nested lambda/local function runs at a different
    // time, so it is excluded too.
    static bool ReadOriginatesAtLoopBodyTop(LoadProperty inlineCurrent, Block loopBody)
    {
        if (IsInsideExceptionRegion(inlineCurrent, loopBody))
            return false;

        int readMin = MinOffset(inlineCurrent);
        if (readMin == int.MaxValue)
            return false;

        return readMin == loopBody.StartOffset;

        static int MinOffset(IrNode node)
        {
            int min = node.SourceOffset >= 0 ? node.SourceOffset : int.MaxValue;
            foreach (var descendant in node.Descendants)
                if (descendant.SourceOffset >= 0 && descendant.SourceOffset < min)
                    min = descendant.SourceOffset;
            return min;
        }
    }

    // Walk the ancestor chain from the read up to (but excluding) the loop body,
    // reporting whether any step passes through an exception region. Such regions
    // emit no IL, so an offset comparison alone cannot tell a body-top read that
    // is protected by a handler from one that is not.
    static bool IsInsideExceptionRegion(IrNode node, Block loopBody)
    {
        for (var current = node.Parent; current is not null && !ReferenceEquals(current, loopBody); current = current.Parent)
            if (current is TryCatch or TryFinally or CatchClause)
                return true;
        return false;
    }

    sealed record PatternMatch(
        IrExpression Collection,
        StoreLocal EnumeratorStore,
        WhileLoop Loop,
        StoreLocal CurrentStore,
        ImmutableArray<MethodRef> ConsumedMemberRefs);

    static PatternMatch? TryMatchPattern(IrFunction function, WhileLoop loop)
    {
        if (loop.Parent is not Block block || loop.ChildIndex == 0)
            return null;
        if (block.Children[loop.ChildIndex - 1] is not StoreLocal { Value: Call getEnumerator } enumeratorStore
            || !IsGetEnumerator(getEnumerator)
            || getEnumerator.Arguments is not [_])
        {
            return null;
        }

        int enumeratorIndex = enumeratorStore.Index;
        if (!HasLocalNameSlot(function, enumeratorIndex)
            || HasSourceLocalName(function, enumeratorIndex))
        {
            return null;
        }

        if (loop.Condition is not Call moveNext
            || !IsMoveNextCall(moveNext)
            || moveNext.Arguments is not [var moveNextReceiver]
            || !IsEnumeratorReceiver(moveNextReceiver, enumeratorIndex)
            || loop.Body.Children is not [StoreLocal { Value: LoadProperty current } currentStore, ..]
            || !IsCurrentOn(current, enumeratorIndex))
        {
            return null;
        }

        if (loop.Body.Children.Skip(1).Any(child => ReferencesLocal(child, enumeratorIndex)))
            return null;

        var currentReceiver = current.Instance!;
        var allowed = new HashSet<IrNode>(ReferenceEqualityComparer.Instance)
        {
            enumeratorStore, moveNextReceiver, currentReceiver,
        };
        if (!ReferencedOnlyBy(function, enumeratorIndex, allowed))
            return null;

        return new PatternMatch(
            CollectionValue(getEnumerator.Arguments[0]),
            enumeratorStore,
            loop,
            currentStore,
            [getEnumerator.Callee, moveNext.Callee, current.Accessor]);
    }

    static IrExpression CollectionValue(IrExpression expression) => expression switch
    {
        LoadArgumentAddress address => new LoadArgument(address.Index, address.Name, address.Type),
        LoadLocalAddress address => new LoadLocal(address.Index, address.Type),
        LoadFieldAddress address => new LoadField(address.Field, DetachCollectionReceiver(address.Instance)),
        LoadElementAddress address => MakeLoadElement(address),
        _ => expression,
    };

    static LoadElement MakeLoadElement(LoadElementAddress address)
    {
        var array = address.Array;
        var index = address.Index;
        index.Detach();   // detach last child first to keep array's index stable
        array.Detach();
        return new LoadElement(address.ElementType, array, index);
    }

    static IrExpression? DetachCollectionReceiver(IrExpression? expression)
    {
        if (expression?.Parent is not null)
            expression.Detach();
        return expression;
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
        IReadOnlyList<StoreLocal> UpperStores,
        StoreLocal ItemStore);

    // A rank-N rectangular array lowers to N hoisted GetUpperBound stores
    // (dimensions 0..N-1, in order), an array copy ahead of them, then N nested
    // index loops each initialized from GetLowerBound(d) and bounded by upper
    // bound d, with the innermost body opening on `array.Get(i0, .., iN-1)`. The
    // rank comes from the array copy's type, so the same matcher covers rank 2
    // (the common case) and higher ranks uniformly.
    static RectangularArrayCandidate? TryMatchRectangularArray(IrFunction function, ForLoop outerLoop)
    {
        if (outerLoop.Parent is not Block block)
            return null;

        // The stores immediately before the outer loop are the per-dimension
        // GetUpperBound copies; collect them (last dimension first), then the
        // store ahead of them must be the array copy whose rank matches the
        // count. Validate each bound against its dimension once the array and
        // rank are known.
        var upperStores = new List<StoreLocal>();
        int cursor = outerLoop.ChildIndex - 1;
        while (cursor >= 0
            && block.Children[cursor] is StoreLocal { Value: Call ubCall } ubStore
            && ubCall.Callee is { Name: "GetUpperBound" })
        {
            upperStores.Add(ubStore);
            cursor--;
        }
        upperStores.Reverse();
        int rank = upperStores.Count;
        if (rank < 2)
            return null;

        if (cursor < 0
            || block.Children[cursor] is not StoreLocal arrayCopy
            || arrayCopy.Type is not { Kind: TypeRefKind.Array, ElementType: { } elementType } arrayType
            || arrayType.Rank != rank)
        {
            return null;
        }
        int arrayIndex = arrayCopy.Index;

        for (int d = 0; d < rank; d++)
            if (upperStores[d].Value is not Call upper || !IsArrayBoundCall(upper, "GetUpperBound", arrayIndex, d))
                return null;
        var upperIndices = upperStores.Select(store => store.Index).ToArray();

        // Walk the N nested index loops, matching each lower bound and condition
        // to its dimension.
        var loops = new List<ForLoop>(rank);
        var lowerInits = new List<StoreLocal>(rank);
        var loopIndices = new List<int>(rank);
        var loop = outerLoop;
        for (int d = 0; d < rank; d++)
        {
            if (loop.Initializer is not StoreLocal { Value: Call lower } lowerInit
                || !IsArrayBoundCall(lower, "GetLowerBound", arrayIndex, d)
                || !IsIndexLoop(loop, lowerInit.Index, upperIndices[d]))
            {
                return null;
            }
            loops.Add(loop);
            lowerInits.Add(lowerInit);
            loopIndices.Add(lowerInit.Index);
            if (d < rank - 1)
            {
                if (loop.Body.Children is not [ForLoop next])
                    return null;
                loop = next;
            }
        }

        var innerLoop = loops[^1];
        if (innerLoop.Body.Children is not [StoreLocal { Value: Call getElement } itemStore, ..]
            || !IsArrayGet(getElement, arrayIndex, loopIndices, arrayType, elementType))
        {
            return null;
        }

        var scaffold = new List<int> { arrayIndex };
        scaffold.AddRange(upperIndices);
        scaffold.AddRange(loopIndices);
        if (scaffold.Contains(itemStore.Index))
            return null;
        if (scaffold.Distinct().Count() != scaffold.Count)
            return null;
        if (scaffold.Any(index => HasSourceLocalName(function, index)))
            return null;

        var allowed = new HashSet<IrNode>(ReferenceEqualityComparer.Instance) { arrayCopy };
        foreach (var upper in upperStores)
            allowed.Add(upper);
        foreach (var lowerInit in lowerInits)
            allowed.Add(lowerInit);
        foreach (var nested in loops)
        {
            allowed.Add(nested.Condition);
            allowed.Add(nested.Increment);
            AddDescendants(allowed, nested.Condition);
            AddDescendants(allowed, nested.Increment);
        }
        foreach (var upper in upperStores)
            AddDescendants(allowed, upper.Value);
        foreach (var lowerInit in lowerInits)
            AddDescendants(allowed, lowerInit.Value);
        AddDescendants(allowed, getElement);

        if (scaffold.Any(index => !ReferencedOnlyBy(function, index, allowed)))
            return null;

        return new RectangularArrayCandidate(outerLoop, innerLoop, arrayCopy, upperStores, itemStore);
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

    // The rank-N element read is `array.Get(i0, i1, .., iN-1)` — a non-virtual
    // call carrying one Int32 parameter per dimension, the array receiver, and
    // each loop index in dimension order.
    static bool IsArrayGet(Call call, int arrayIndex, IReadOnlyList<int> loopIndices, TypeRef arrayType, TypeRef elementType)
    {
        if (call is not
            {
                IsVirtual: false,
                Callee: { HasThis: true, Name: "Get", DeclaringType: var declaringType, ParameterTypes: var parameters, ReturnType: var returnType },
                Arguments: var arguments,
            }
            || !declaringType.Equals(arrayType)
            || !returnType.Equals(elementType)
            || parameters.Length != loopIndices.Count
            || arguments.Count != loopIndices.Count + 1
            || arguments[0] is not LoadLocal receiver
            || receiver.Index != arrayIndex)
        {
            return false;
        }
        var int32 = TypeRef.CoreLib("System", "Int32");
        for (int d = 0; d < loopIndices.Count; d++)
            if (!parameters[d].Equals(int32) || arguments[d + 1] is not LoadLocal index || index.Index != loopIndices[d])
                return false;
        return true;
    }

    static bool HasSourceLocalName(IrFunction function, int index)
        => index >= 0
            && index < function.LocalNames.Length
            && !string.IsNullOrWhiteSpace(function.LocalNames[index]);

    static bool HasLocalNameSlot(IrFunction function, int index)
        => index >= 0 && index < function.LocalNames.Length;

    static bool IsGetEnumerator(Call call)
        => call.Callee is { Name: "GetEnumerator", HasThis: true } && call.Arguments.Count == 1;

    static bool IsSupportedEnumeratorCollection(Call getEnumerator)
        => MemberIdentity.IsCoreLibraryType(getEnumerator.Callee.DeclaringType, "System.Collections", "IEnumerable")
            || MemberIdentity.IsCoreLibraryType(getEnumerator.Callee.DeclaringType, "System.Collections.Generic", "IEnumerable`1");

    static bool IsMoveNextOn(IrExpression condition, int enumeratorIndex)
        => condition is Call call
            && IsMoveNextCall(call)
            && call.Arguments is [var receiver]
            && IsEnumeratorReceiver(receiver, enumeratorIndex);

    static bool IsMoveNextCall(Call call)
        => call is { Callee: { Name: "MoveNext", HasThis: true, ReturnType: var returnType } }
            && MemberIdentity.IsCoreLibraryType(returnType, "System", "Boolean");

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
