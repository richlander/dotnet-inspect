using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

internal sealed partial class ClassicInverseAccountant
{
    sealed record StackallocBinding(NewObject Creation, StackAllocate Allocation, StackAllocArray Planning,
        ImmutableArray<IrNode> Effects);

    readonly List<StackAllocArray> _planningStackallocs = [];
    readonly Dictionary<int, StackallocBinding> _stackallocs = [];
    readonly HashSet<IrNode> _stackallocCountStores = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<IrNode> _stackallocCountReads = new(ReferenceEqualityComparer.Instance);

    bool ProveStackallocs()
    {
        if (_planningStackallocs.Count == 0)
            return true;
        var creations = new Dictionary<int, NewObject?>();
        var locals = new Dictionary<int, List<IrNode>>();
        var positions = new Dictionary<IrNode, int>(ReferenceEqualityComparer.Instance);
        foreach (IrNode node in _request.ExecutionBody.Body.DescendantsAndSelfOutsideNestedFunctions)
        {
            if (!_budget.Charge())
                return Exhausted();
            if (node is NewObject creation && !creations.TryAdd(node.SourceOffset, creation))
                creations[node.SourceOffset] = null;
            if (node is Block block)
            {
                for (int i = 0; i < block.Children.Count; i++)
                {
                    if (!_budget.Charge())
                        return Exhausted();
                    positions.Add(block.Children[i], i);
                }
            }
            int index = node switch
            {
                LoadLocal read => read.Index,
                LoadLocalAddress address => address.Index,
                StoreLocal store => store.Index,
                _ => -1,
            };
            if (index >= 0)
            {
                if (!locals.TryGetValue(index, out var uses))
                    locals.Add(index, uses = []);
                uses.Add(node);
            }
        }
        foreach (StackAllocArray array in _planningStackallocs)
        {
            if (!_budget.Charge())
                return Exhausted();
            if (array.HasInitializer || array.SourceOffset < 0
                || !creations.TryGetValue(array.SourceOffset, out NewObject? creation)
                || creation is null || !MemberIdentity.IsStackAllocSpanConstructor(creation, out TypeRef? element)
                || !element.Equals(array.ElementType) || !Equals(creation.ResultType, array.ResultType)
                || creation.Arguments is not [StackAllocate allocation, IrExpression count])
                return Unproven();

            IrExpression producer = count;
            if (count is LoadLocal read)
            {
                if (!locals.TryGetValue(read.Index, out var uses) || uses.Count != 3
                    || uses.OfType<StoreLocal>().SingleOrDefault() is not StoreLocal store
                    || uses.OfType<LoadLocal>().Count() != 2
                    || !uses.Contains(read) || !Equals(store.Type, read.Type)
                    || !Equals(store.Type, store.Value.ResultType)
                    || !MemberIdentity.IsCoreLibraryType(store.Type, "System", "Int32"))
                    return Unproven();
                LoadLocal sizeRead = uses.OfType<LoadLocal>().Single(use => !ReferenceEquals(use, read));
                if (!Equals(sizeRead.Type, read.Type) || !Inside(sizeRead, allocation.Size))
                    return Unproven();
                IrNode statement = creation;
                while (statement.Parent is not null and not Block)
                {
                    if (!_budget.Charge())
                        return Exhausted();
                    statement = statement.Parent;
                }
                if (store.Parent is not Block block || !ReferenceEquals(statement.Parent, block)
                    || !positions.TryGetValue(store, out int defined)
                    || !positions.TryGetValue(statement, out int used) || used != defined + 1
                    || statement is not StoreLocal consumer
                    || !Admit(consumer.Value)
                    || !StackAllocSpanPass.ReachesAsOnlyPrecedingEffect(consumer.Value, creation))
                    return Unproven();
                producer = store.Value;
                _stackallocCountStores.Add(store);
                _stackallocCountReads.Add(read);
                _stackallocCountReads.Add(sizeRead);
            }
            else if (count is not Constant { Value: int value } || value < 0)
                return Unproven();

            if (!Admit(allocation.Size)
                || !StackAllocSpanPass.IsProvenByteSize(allocation.Size, count, element)
                || !ClassicInverseExpressionRules.SameTree(producer, array.Count, _budget))
                return Unproven();

            var effects = ImmutableArray.CreateBuilder<IrNode>();
            var origins = ImmutableArray.CreateBuilder<int>();
            if (!Collect(creation))
                return Exhausted();
            _stackallocs.Add(array.SourceOffset, new(creation, allocation, array, effects.ToImmutable()));
            _foldedValueOffsets[array.SourceOffset] = origins.ToImmutable();

            bool Collect(IrNode node)
            {
                if (!_budget.Charge())
                    return false;
                foreach (IrNode child in node.Children)
                    if (!Collect(child))
                        return false;
                if (node.SourceOffset < 0)
                {
                    _terminal = Decline(ClassicInverseDeclineReason.MissingImportCorrespondence,
                        "a stack allocation has an unanchored raw component");
                    return false;
                }
                origins.Add(node.SourceOffset);
                if (!_stackallocCountReads.Contains(node))
                    _rawSemanticValues.Add(node);
                if (ClassicInverseNodeFacts.EffectSignature(node, _shell.Machine) is not null)
                    effects.Add(node);
                return true;
            }
        }
        return true;

        bool Inside(IrNode node, IrNode root)
        {
            for (IrNode? current = node; current is not null; current = current.Parent)
            {
                if (!_budget.Charge())
                    return false;
                if (ReferenceEquals(current, root))
                    return true;
            }
            return false;
        }

        bool Admit(IrNode node)
        {
            foreach (IrNode _ in node.DescendantsAndSelfOutsideNestedFunctions)
                if (!_budget.Charge() || !_budget.Charge())
                    return false;
            return true;
        }

        bool Unproven() => _budget.Exhausted ? Exhausted()
            : DeclineFalse(ClassicInverseDeclineReason.UnrealizedSemanticEffect,
                "a stack allocation has no exact byte-size, count transfer, or span construction");

        bool Exhausted()
        {
            _terminal ??= Failure("stack allocation correspondence exhausted the planning budget");
            return false;
        }
    }

    bool VisitStackalloc(IrNode node, Action<IrNode> visit, ImmutableArray<string>.Builder? effects,
        Func<string, string>? qualify = null, bool bindOutputTypes = false)
    {
        if (node is not StackAllocArray array)
            return false;
        if (!_stackallocs.TryGetValue(array.SourceOffset, out StackallocBinding? binding))
        {
            _terminal = Decline(ClassicInverseDeclineReason.MissingImportCorrespondence,
                "a stack allocation has no raw construction receipt");
            return true;
        }
        visit(array.Count);
        if (effects is null)
            return true;
        foreach (IrNode effectNode in binding.Effects)
        {
            if (!_budget.Charge())
            {
                _terminal = Failure("stack allocation effect accounting exhausted the planning budget");
                return true;
            }
            string effect = ClassicInverseNodeFacts.EffectSignature(effectNode, _shell.Machine,
                bindOutputTypes ? _planning.TypeBinding : null, _budget)!;
            effects.Add(qualify?.Invoke(effect) ?? effect);
        }
        return true;
    }
}
