using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

internal sealed partial class ClassicInverseAccountant
{
    sealed record FamilyInput(IrExpression Raw, ImmutableArray<int> Path);
    sealed record FamilyStep(FamilyInput? Input, IrNode? Effect);
    sealed record FamilyExpansion(IrExpression Raw, IrExpression Planning,
        ImmutableArray<FamilyInput> Inputs, ImmutableArray<FamilyStep> Steps, bool InputsAlreadyVisited);

    readonly List<IrExpression> _planningFamilies = [];
    readonly Dictionary<int, FamilyExpansion> _families = [];
    readonly Dictionary<IrNode, FamilyExpansion> _rawFamilyValues = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<IrNode> _familySuppressedValues = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<IrNode> _familyStoreFrames = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<IrNode> _familyTraffic = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<int, List<StoreStackSlot>> _familySlotStores = [];
    readonly Dictionary<int, List<LoadStackSlot>> _familySlotReads = [];
    readonly Dictionary<int, List<IrNode>> _familyLocals = [];
    readonly Dictionary<IrNode, int> _familyPositions = new(ReferenceEqualityComparer.Instance);

    bool TryFamily(IrExpression node, out FamilyExpansion family)
        => _families.TryGetValue(node.SourceOffset, out family!)
            && node.GetType() == family.Planning.GetType();

    static IrNode AtPath(IrNode node, ImmutableArray<int> path)
    {
        foreach (int index in path)
            node = node.Children[index];
        return node;
    }

    bool IndexValueTransfers()
    {
        if (_planningFamilies.Count == 0 && _planningInterpolations.Count == 0)
            return true;
        foreach (IrNode node in _rawExecutionPaths.Keys)
        {
            if (!_budget.Charge())
                return FamilyFailure();
            if (node is StoreStackSlot slotStore)
                Group(_familySlotStores, slotStore.Slot, slotStore);
            if (node is LoadStackSlot slotRead)
                Group(_familySlotReads, slotRead.Slot, slotRead);
            int local = node switch
            {
                LoadLocal read => read.Index,
                LoadLocalAddress address => address.Index,
                StoreLocal store => store.Index,
                _ => -1,
            };
            if (local >= 0)
                Group(_familyLocals, local, node);
            if (node is Block block)
                for (int i = 0; i < block.Children.Count; i++)
                {
                    if (!_budget.Charge())
                        return FamilyFailure();
                    _familyPositions.Add(block.Children[i], i);
                }
        }
        return true;

        static void Group<T>(Dictionary<int, List<T>> map, int index, T node)
        {
            if (!map.TryGetValue(index, out var values))
                map.Add(index, values = []);
            values.Add(node);
        }
    }

    bool ProveExpressionFamilies()
    {
        foreach (IrExpression planning in _planningFamilies)
        {
            if (!_budget.Charge() || planning.SourceOffset < 0
                || !_rawExpressionsByOffset.TryGetValue(planning.SourceOffset, out IrExpression? raw)
                || raw is null)
                return FamilyFailure();
            var inputs = ImmutableArray.CreateBuilder<FamilyInput>();
            var steps = ImmutableArray.CreateBuilder<FamilyStep>();
            var origins = ImmutableArray.CreateBuilder<int>();
            bool priorInputs = false;

            bool Input(IrExpression value, params int[] path)
            {
                if (!_budget.Charge())
                    return false;
                var input = new FamilyInput(value, [.. path]);
                if (!ClassicInverseExpressionRules.SameTree(value, AtPath(planning, input.Path), _budget))
                    return false;
                inputs.Add(input);
                steps.Add(new(input, null));
                return true;
            }
            bool Effect(IrNode effect)
            {
                if (!_budget.Charge() || effect.SourceOffset < 0)
                    return false;
                steps.Add(new(null, effect));
                origins.Add(effect.SourceOffset);
                _rawSemanticValues.Add(effect);
                return true;
            }
            bool Retire(IrNode value)
            {
                foreach (IrNode part in value.DescendantsAndSelfOutsideNestedFunctions)
                {
                    if (!_budget.Charge() || part.SourceOffset < 0)
                        return false;
                    origins.Add(part.SourceOffset);
                    if (!_familyTraffic.Contains(part))
                        _rawSemanticValues.Add(part);
                }
                return true;
            }

            switch (planning)
            {
                case AnonymousObject anonymous when raw is NewObject creation:
                    if (!ClassicInverseExpressionRules.SameTree(creation, anonymous, _budget))
                        return FamilyFailure();
                    for (int i = 0; i < creation.Arguments.Count; i++)
                        if (!Input(creation.Arguments[i], i))
                            return FamilyFailure();
                    if (!Effect(creation))
                        return FamilyFailure();
                    break;

                case DelegateCreation creation when raw is NewObject construction:
                    if (!DelegateConstructionPass.IsDelegateConstructor(construction.Constructor)
                        || construction.Constructor != creation.Constructor
                        || !Equals(construction.ResultType, creation.DelegateType)
                        || construction.Arguments is not [IrExpression target, LoadFunctionPointer pointer]
                        || pointer.Method != creation.Method || pointer.IsVirtual != creation.IsVirtual)
                        return FamilyFailure();
                    if (pointer.IsVirtual)
                    {
                        if (target is not LoadStackSlot delegateReceiver || pointer.Children is not [LoadStackSlot duplicate]
                            || delegateReceiver.Slot != duplicate.Slot
                            || !BindSlot(delegateReceiver.Slot, construction, out StoreStackSlot delegateStore, 2)
                            || !ImmediatelyFollows(delegateStore, construction)
                            || !Equals(delegateReceiver.Type, delegateStore.Value.ResultType)
                            || !Equals(duplicate.Type, delegateStore.Value.ResultType) || !Input(delegateStore.Value, 0))
                            return FamilyFailure();
                        _familyTraffic.Add(delegateReceiver);
                        _familyTraffic.Add(duplicate);
                        _familyStoreFrames.Add(delegateStore);
                        priorInputs = true;
                        if (!Effect(pointer))
                            return FamilyFailure();
                    }
                    else if (pointer.Children.Count != 0 || !Input(target, 0))
                        return FamilyFailure();
                    if (!Retire(pointer) || !Effect(construction))
                        return FamilyFailure();
                    break;

                case ArrayLiteral array when raw is NewArray allocation:
                    if (!BindArray(array, allocation, Effect, Retire))
                        return FamilyFailure();
                    break;

                case SliceExpression slice when raw is Call call:
                    if (!MemberIdentity.IsRuntimeHelpersGetSubArray(call) || call.IsVirtual || call.ConstrainedTo is not null
                        || call.Callee != slice.SliceMethod || !Equals(call.ResultType, slice.ResultType)
                        || call.Arguments is not [IrExpression receiver, IrExpression range]
                        || !Input(receiver, 0)
                        || !Range(range, slice.Range) || !Effect(call))
                        return FamilyFailure();
                    break;

                case IndexFromEnd index when raw is Binary { Kind: BinaryKind.Subtract, IsChecked: false, IsUnsigned: false } subtract:
                    IrExpression length = subtract.Left is Convert
                        { IsChecked: false, IsUnsigned: false, Operand: ArrayLength arrayLength } conversion
                        && Equals(conversion.Target, arrayLength.ResultType) ? arrayLength : subtract.Left;
                    if (length is not ArrayLength { Array: LoadStackSlot lengthRead } arraySize
                        || subtract.Parent is not LoadElement { Array: LoadStackSlot receiverRead } access
                        || planning.Parent is not LoadElement plannedAccess
                        || lengthRead.Slot != receiverRead.Slot
                        || !BindSlot(receiverRead.Slot, access, out StoreStackSlot? stored, expectedReads: 2)
                        || !ImmediatelyFollows(stored, access)
                        || !Equals(lengthRead.Type, stored.Value.ResultType)
                        || !Equals(receiverRead.Type, stored.Value.ResultType)
                        || !ClassicInverseExpressionRules.SameTree(stored.Value, plannedAccess.Array, _budget)
                        || !Effect(arraySize) || !Input(subtract.Right, 0) || !Retire(subtract.Left))
                        return FamilyFailure();
                    _familyTraffic.Add(lengthRead);
                    _familyTraffic.Add(receiverRead);
                    _rawSemanticValues.Remove(lengthRead);
                    _rawSemanticValues.Remove(receiverRead);
                    _familyStoreFrames.Add(stored);
                    break;

                case CollectionExpression collection when raw is Call span:
                    if (!BindCollection(collection, span, inputs, steps, origins))
                        return FamilyFailure();
                    priorInputs = true;
                    break;

                default:
                    return FamilyFailure();
            }
            var expansion = new FamilyExpansion(raw, planning, inputs.ToImmutable(), steps.ToImmutable(), priorInputs);
            if (!_families.TryAdd(planning.SourceOffset, expansion) || !_rawFamilyValues.TryAdd(raw, expansion))
                return FamilyFailure();
            _foldedValueOffsets[planning.SourceOffset] = origins.ToImmutable();

            bool Endpoint(IrExpression source, IrExpression target, int[] path)
            {
                if (source is Call conversion && MemberIdentity.IsIndexFromStartConversion(conversion.Callee)
                    && !conversion.IsVirtual && conversion.ConstrainedTo is null
                    && conversion.Arguments is [IrExpression value] && target is not IndexFromEnd)
                    return Input(value, path) && Effect(conversion);
                if (source is NewObject end && MemberIdentity.IsIndexFromEndConstructor(end.Constructor)
                    && end.Arguments is [IrExpression offset, Constant polarity]
                    && polarity.Value is (1 or true) && target is IndexFromEnd)
                    return Input(offset, [.. path, 0]) && Retire(polarity) && Effect(end);
                return false;
            }

            bool Range(IrExpression source, RangeExpression range)
            {
                if (source.SourceOffset != range.SourceOffset)
                    return false;
                if (source is NewObject construction && MemberIdentity.IsRangeConstructor(construction.Constructor)
                    && construction.Arguments is [IrExpression start, IrExpression end]
                    && range.HasStart && range.HasEnd)
                    return Endpoint(start, range.Start!, [1, 0]) && Endpoint(end, range.End!, [1, 1]) && Effect(source);
                if (source is Call call && !call.IsVirtual && call.ConstrainedTo is null)
                {
                    if (MemberIdentity.IsRangeStartAt(call.Callee) && call.Arguments is [IrExpression startAt]
                        && range.HasStart && !range.HasEnd)
                        return Endpoint(startAt, range.Start!, [1, 0]) && Effect(source);
                    if (MemberIdentity.IsRangeEndAt(call.Callee) && call.Arguments is [IrExpression endAt]
                        && !range.HasStart && range.HasEnd)
                        return Endpoint(endAt, range.End!, [1, 0]) && Effect(source);
                    if (MemberIdentity.IsRangeAllGetter(call.Callee) && call.Arguments.Count == 0
                        && !range.HasStart && !range.HasEnd)
                        return Effect(source);
                }
                return false;
            }
        }
        return true;

    }

    bool BindSlot(int slot, IrNode use, out StoreStackSlot store, int expectedReads)
    {
        store = null!;
        if (!_budget.Charge() || _familySlotStores.GetValueOrDefault(slot) is not [StoreStackSlot definition]
            || !_familySlotReads.TryGetValue(slot, out var reads) || reads.Count != expectedReads
            || definition.Parent is not Block block)
            return false;
        IrNode current = use;
        while (current.Parent is not null and not Block)
        {
            if (!_budget.Charge())
                return false;
            current = current.Parent;
        }
        if (!ReferenceEquals(current.Parent, block) || !_familyPositions.TryGetValue(current, out int used)
            || !_familyPositions.TryGetValue(definition, out int defined) || used <= defined)
            return false;
        store = definition;
        return true;
    }

    bool ImmediatelyFollows(IrNode definition, IrNode value, int distance = 1)
    {
        IrNode statement = value;
        while (statement.Parent is not null and not Block)
        {
            if (!_budget.Charge())
                return false;
            statement = statement.Parent;
        }
        return ReferenceEquals(statement.Parent, definition.Parent)
            && _familyPositions.TryGetValue(statement, out int used)
            && _familyPositions.TryGetValue(definition, out int defined) && used == defined + distance;
    }

    bool BindArray(ArrayLiteral array, NewArray allocation, Func<IrNode, bool> effect, Func<IrNode, bool> retire)
    {
        if (allocation.Parent is not StoreStackSlot store || store.Parent is not Block block
            || !Equals(allocation.ElementType, array.ElementType) || !Equals(allocation.ResultType, array.ArrayType)
            || allocation.Length is not Constant { Value: int count } || count != array.Children.Count
            || !_familyPositions.TryGetValue(store, out int position) || position + 2 >= block.Children.Count
            || block.Children[position + 1] is not ExpressionStatement { Expression: Call initialize }
            || !MemberIdentity.IsRuntimeHelpersInitializeArray(initialize) || initialize.IsVirtual
            || initialize.ConstrainedTo is not null || initialize.Callee != array.InitializationMethod
            || initialize.Arguments is not [LoadStackSlot initialized, LoadToken { FieldRvaData: { } data } token]
            || initialized.Slot != store.Slot
            || _familySlotReads.GetValueOrDefault(store.Slot) is not { Count: 2 } reads
            || _familySlotStores.GetValueOrDefault(store.Slot) is not [StoreStackSlot only] || !ReferenceEquals(only, store))
            return false;
        foreach (byte _ in data)
            if (!_budget.Charge())
                return false;
        var decoded = RvaSpanPass.DecodeElements(_request.ExecutionBody, array.ElementType, data);
        if (decoded is null || decoded.Count != count)
            return false;
        for (int i = 0; i < count; i++)
            if (!_budget.Charge() || !ClassicInverseRealizationRules.PayloadEquals(decoded[i], array.Children[i]))
                return false;
        foreach (LoadStackSlot read in reads)
        {
            if (!_budget.Charge() || !Equals(read.Type, array.ArrayType))
                return false;
            if (!ReferenceEquals(read, initialized) && !ImmediatelyFollows(store, read, distance: 2))
                return false;
            _familyTraffic.Add(read);
        }
        _familyStoreFrames.Add(store);
        _familySuppressedValues.Add(initialize);
        return retire(allocation.Length) && retire(token) && effect(allocation) && effect(initialize);
    }

    bool BindCollection(CollectionExpression collection, Call span, ImmutableArray<FamilyInput>.Builder inputs,
        ImmutableArray<FamilyStep>.Builder steps, ImmutableArray<int>.Builder origins)
    {
        if (span.IsVirtual || span.ConstrainedTo is not null || span.Callee.HasThis
            || span.Callee.DeclaringTypeCompilerGenerated != MetadataFactState.Yes
            || !(InlineArrayCollectionPass.IsPrivateImpl(span.Callee, "InlineArrayAsSpan")
                || InlineArrayCollectionPass.IsPrivateImpl(span.Callee, "InlineArrayAsReadOnlySpan"))
            || span.Callee.TypeArguments is not [TypeRef bufferType, TypeRef element]
            || !InlineArrayCollectionPass.IsSynthesizedInlineArray(bufferType)
            || span.Callee.ParameterTypes is not [TypeRef bufferParameterType, TypeRef lengthParameterType]
            || !Equals(bufferParameterType, TypeRef.ByRef(bufferType))
            || !MemberIdentity.IsCoreLibraryType(lengthParameterType, "System", "Int32")
            || span.ResultType is not { Kind: TypeRefKind.GenericInstance, ElementType: { } spanDefinition,
                TypeArguments: [TypeRef spanElement] }
            || !Equals(spanElement, element)
            || !MemberIdentity.IsCoreLibraryType(spanDefinition, "System",
                span.Callee.Name == "InlineArrayAsSpan" ? "Span`1" : "ReadOnlySpan`1")
            || span.Arguments is not [LoadLocalAddress buffer, Constant { Value: int count }]
            || !Equals(buffer.Type, bufferType) || !Equals(element, collection.ElementType)
            || !Equals(span.ResultType, collection.TargetType) || collection.Children.Count != count
            || collection.ConsumedMemberRefs.Length != count + 1 || collection.ConsumedMemberRefs[^1] != span.Callee
            || !_familyLocals.TryGetValue(buffer.Index, out var uses) || uses.Count != count + 2)
            return false;
        InitObject? init = null;
        foreach (IrNode use in uses)
        {
            if (!_budget.Charge() || use is not LoadLocalAddress address || !Equals(address.Type, bufferType))
                return false;
            if (address.Parent is InitObject candidate)
            {
                if (init is not null || !Equals(candidate.Type, bufferType))
                    return false;
                init = candidate;
            }
            _familyTraffic.Add(address);
        }
        if (init?.Parent is not Block block || !_familyPositions.TryGetValue(init, out int start))
            return false;
        IrNode statement = span;
        while (statement.Parent is not null and not Block)
        {
            if (!_budget.Charge())
                return false;
            statement = statement.Parent;
        }
        if (!ReferenceEquals(statement.Parent, block) || !_familyPositions.TryGetValue(statement, out int end)
            || end != start + count + 1)
            return false;
        steps.Add(new(null, init));
        for (int i = 0; i < count; i++)
        {
            if (!_budget.Charge()
                || block.Children[start + i + 1] is not StoreIndirect { Address: Call address } store
                || address.Callee != collection.ConsumedMemberRefs[i] || address.IsVirtual || address.ConstrainedTo is not null
                || !GeneratedCodeIdentity.IsInlineArrayElementRefHelper(address.Callee, out bool first, out bool readOnly)
                || first || readOnly || !Equals(address.ResultType, TypeRef.ByRef(element))
                || address.Callee.TypeArguments is not [var sourceBuffer, var sourceElement]
                || !Equals(sourceBuffer, bufferType) || !Equals(sourceElement, element)
                || address.Callee.ParameterTypes is not [var bufferParameter, var indexParameter]
                || !Equals(bufferParameter, TypeRef.ByRef(bufferType))
                || !MemberIdentity.IsCoreLibraryType(indexParameter, "System", "Int32")
                || address.Arguments is not [LoadLocalAddress receiver, Constant { Value: int slot }]
                || receiver.Index != buffer.Index || slot != i || !Equals(store.Type, element)
                || !ClassicInverseExpressionRules.SameTree(store.Value, collection.Children[i], _budget))
                return false;
            var input = new FamilyInput(store.Value, [i]);
            inputs.Add(input);
            steps.Add(new(null, address));
            steps.Add(new(input, null));
            steps.Add(new(null, store));
            _familySuppressedValues.Add(address);
            foreach (IrNode component in address.DescendantsAndSelfOutsideNestedFunctions)
                if (!Retain(component))
                    return false;
        }
        steps.Add(new(null, span));
        foreach (IrNode node in span.Children.Append(init))
            if (!Retain(node))
                return false;
        return true;

        bool Retain(IrNode node)
        {
            if (!_budget.Charge() || node.SourceOffset < 0)
                return false;
            origins.Add(node.SourceOffset);
            if (!_familyTraffic.Contains(node))
                _rawSemanticValues.Add(node);
            return true;
        }
    }

    bool VisitFamily(IrNode node, Action<IrNode> visit, ImmutableArray<string>.Builder? effects,
        Func<string, string>? qualify = null, bool bindOutputTypes = false)
    {
        if (node is not IrExpression expression || !TryFamily(expression, out FamilyExpansion? family))
            return false;
        if (!ClassicInverseRealizationRules.PayloadEquals(expression, family.Planning)
            || expression.Children.Count != family.Planning.Children.Count)
        {
            FamilyFailure();
            return true;
        }
        foreach (FamilyStep step in family.Steps)
        {
            if (!_budget.Charge())
            {
                FamilyFailure();
                return true;
            }
            if (step.Input is { } input)
                visit(AtPath(node, input.Path));
            else if (effects is not null)
            {
                string effect = ClassicInverseNodeFacts.EffectSignature(step.Effect!, _shell.Machine,
                    bindOutputTypes ? _planning.TypeBinding : null, _budget)!;
                effects.Add(qualify?.Invoke(effect) ?? effect);
            }
            if (_terminal is not null)
                return true;
        }
        return true;
    }

    bool FamilyFailure()
    {
        _terminal = _budget.Exhausted ? Failure("expression family correspondence exhausted the planning budget")
            : Decline(ClassicInverseDeclineReason.UnrealizedSemanticEffect,
                "an expression family has no exact typed construction, transfer, or primitive effects");
        return false;
    }
}
