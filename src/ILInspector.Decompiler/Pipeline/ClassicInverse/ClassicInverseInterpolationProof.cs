using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

internal sealed partial class ClassicInverseAccountant
{
    sealed record InterpolationBinding(NewObject Constructor, ImmutableArray<Call> Appends,
        Call End, InterpolatedStringExpression Planning);

    readonly List<InterpolatedStringExpression> _planningInterpolations = [];
    readonly Dictionary<int, InterpolationBinding> _interpolations = [];
    readonly HashSet<IrNode> _interpolationStores = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<IrNode> _interpolationAddresses = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<IrNode, IrExpression?> _interpolationRawParts = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<IrNode, InterpolatedStringExpression> _interpolationRawResults =
        new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<IrNode, List<IrNode>> _rawPriorValues = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<IrNode> _scheduledPriorValues = new(ReferenceEqualityComparer.Instance);

    bool ProveInterpolations()
    {
        if (_planningInterpolations.Count == 0)
            return true;
        var calls = new Dictionary<int, Call?>();
        var locals = new Dictionary<int, List<IrNode>>();
        var positions = new Dictionary<IrNode, int>(ReferenceEqualityComparer.Instance);
        foreach (IrNode node in _request.ExecutionBody.Body.DescendantsAndSelfOutsideNestedFunctions)
        {
            if (!_budget.Charge())
                return Exhausted();
            if (node is Call call && !calls.TryAdd(call.SourceOffset, call))
                calls[call.SourceOffset] = null;
            if (node is Block block)
            {
                for (int i = 0; i < block.Children.Count; i++)
                {
                    if (!_budget.Charge())
                        return Exhausted();
                    positions.Add(block.Children[i], i);
                }
            }
            int slot = node switch
            {
                StoreLocal store => store.Index,
                LoadLocal read => read.Index,
                LoadLocalAddress address => address.Index,
                _ => -1,
            };
            if (slot >= 0)
            {
                if (!locals.TryGetValue(slot, out var uses))
                    locals.Add(slot, uses = []);
                uses.Add(node);
            }
        }
        foreach (InterpolatedStringExpression interpolation in _planningInterpolations)
        {
            if (!_budget.Charge())
                return Exhausted();
            if (interpolation.SourceOffset < 0
                || !calls.TryGetValue(interpolation.SourceOffset, out Call? end)
                || end is null || !MemberIdentity.IsDefaultInterpolatedStringHandlerToStringAndClear(end)
                || end.ConstrainedTo is not null || end.Arguments is not [LoadLocalAddress receiver]
                || !Equals(receiver.Type, end.Callee.DeclaringType)
                || !locals.TryGetValue(receiver.Index, out var uses)
                || receiver.Index < _request.ExecutionBody.LocalNames.Length
                    && !string.IsNullOrWhiteSpace(_request.ExecutionBody.LocalNames[receiver.Index]))
                return Unproven();

            StoreLocal? store = null;
            foreach (IrNode use in uses)
            {
                if (!_budget.Charge())
                    return Exhausted();
                if (use is StoreLocal definition)
                {
                    if (store is not null)
                        return Unproven();
                    store = definition;
                }
                else if (use is not LoadLocalAddress address || !Equals(address.Type, receiver.Type))
                    return Unproven();
            }
            if (store is not { Value: NewObject constructor, Parent: Block block }
                || !Equals(store.Type, receiver.Type)
                || !MemberIdentity.IsDefaultInterpolatedStringHandlerConstructor(constructor)
                || !Equals(constructor.ResultType, receiver.Type)
                || constructor.Arguments is not
                    [Constant { Value: int literalLength } literalCount, Constant { Value: int formattedCount } holeCount]
                || !MemberIdentity.IsCoreLibraryType(literalCount.Type, "System", "Int32")
                || !MemberIdentity.IsCoreLibraryType(holeCount.Type, "System", "Int32")
                || interpolation.ConsumedMemberRefs.Length != interpolation.Parts.Length + 2
                || interpolation.ConsumedMemberRefs[0] != constructor.Constructor
                || interpolation.ConsumedMemberRefs[^1] != end.Callee)
                return Unproven();

            IrNode statement = end;
            while (statement.Parent is not null and not Block)
            {
                if (!_budget.Charge())
                    return Exhausted();
                statement = statement.Parent;
            }
            int count = interpolation.Parts.Length;
            if (!ReferenceEquals(statement.Parent, block)
                || !positions.TryGetValue(store, out int start)
                || !positions.TryGetValue(statement, out int finish)
                || finish != start + count + 1 || uses.Count != count + 2
                || !IsFirstInterpolationEffect(end, statement, store, interpolation))
                return Unproven();

            var appends = ImmutableArray.CreateBuilder<Call>();
            var origins = ImmutableArray.CreateBuilder<int>();
            if (!Retain(constructor) || !Retain(end))
                return Unproven();
            _interpolationStores.Add(store);
            _interpolationAddresses.Add(receiver);
            _interpolationRawParts.Add(constructor, null);
            long actualLiteralLength = 0;
            int values = 0;
            for (int i = 0; i < count; i++)
            {
                if (!_budget.Charge())
                    return Exhausted();
                InterpolatedStringPart part = interpolation.Parts[i];
                if (block.Children[start + i + 1] is not ExpressionStatement { Expression: Call append }
                    || append.ConstrainedTo is not null
                    || append.Arguments is not [LoadLocalAddress address, ..]
                    || address.Index != receiver.Index || !Equals(address.Type, receiver.Type)
                    || !Equals(append.Callee.DeclaringType, receiver.Type)
                    || append.Callee != interpolation.ConsumedMemberRefs[i + 1]
                    || !Retain(append) || !Retain(address))
                    return Unproven();
                _interpolationAddresses.Add(address);
                appends.Add(append);
                if (part.IsLiteral)
                {
                    if (part.ExpressionIndex != -1 || part.Format is not null
                        || !MemberIdentity.IsDefaultInterpolatedStringHandlerAppendLiteral(append)
                        || append.Arguments[1] is not Constant { Value: string literal } text
                        || !MemberIdentity.IsCoreLibraryType(text.Type, "System", "String")
                        || literal != part.Literal || !Retain(text))
                        return Unproven();
                    actualLiteralLength += literal.Length;
                    _interpolationRawParts.Add(append, null);
                    _rawSemanticValues.Add(text);
                }
                else
                {
                    if (!MemberIdentity.IsDefaultInterpolatedStringHandlerAppendFormatted(append)
                        || part.ExpressionIndex != values || values >= interpolation.Children.Count
                        || MemberIdentity.GetAppendFormattedFormat(append) != part.Format
                        || interpolation.Children[values] is not IrExpression plannedValue
                        || !Equals(append.Callee.ParameterTypes[0], plannedValue.ResultType)
                        || !append.Callee.TypeArguments.IsDefaultOrEmpty
                            && (append.Callee.TypeArguments.Length != 1
                                || !Equals(append.Callee.TypeArguments[0], append.Callee.ParameterTypes[0]))
                        || !ClassicInverseExpressionRules.SameTree(append.Arguments[1],
                            interpolation.Children[values], _budget))
                        return Unproven();
                    _interpolationRawParts.Add(append, append.Arguments[1]);
                    for (int argument = 2; argument < append.Arguments.Count; argument++)
                    {
                        if (!_budget.Charge() || !Retain(append.Arguments[argument])
                            || !Equals(append.Arguments[argument].ResultType, append.Callee.ParameterTypes[argument - 1]))
                            return Unproven();
                        _rawSemanticValues.Add(append.Arguments[argument]);
                    }
                    values++;
                }
            }
            if (values != interpolation.Children.Count || values != formattedCount
                || actualLiteralLength != literalLength || !Retain(literalCount) || !Retain(holeCount))
                return Unproven();
            _rawSemanticValues.Add(literalCount);
            _rawSemanticValues.Add(holeCount);
            _interpolationRawResults.Add(end, interpolation);
            _interpolations.Add(interpolation.SourceOffset, new(constructor, appends.ToImmutable(), end, interpolation));
            _foldedValueOffsets[interpolation.SourceOffset] = origins.ToImmutable();

            bool Retain(IrNode node)
            {
                if (!_budget.Charge() || node.SourceOffset < 0)
                    return false;
                origins.Add(node.SourceOffset);
                return true;
            }
        }
        return true;

        bool Unproven() => _budget.Exhausted ? Exhausted()
            : DeclineFalse(ClassicInverseDeclineReason.UnrealizedSemanticEffect,
                "an interpolation has no exact private handler, typed parts, ordered effects, or origins");

        bool Exhausted()
        {
            _terminal = Failure("interpolation correspondence exhausted the planning budget");
            return false;
        }
    }

    bool IsFirstInterpolationEffect(IrNode target, IrNode statement, StoreLocal handler,
        InterpolatedStringExpression interpolation)
    {
        var prior = new List<IrNode>();
        IrNode planned = interpolation;
        for (IrNode current = target; !ReferenceEquals(current, statement); current = current.Parent!)
        {
            if (!_budget.Charge() || current.Parent is not { } parent)
                return false;
            if (ReferenceEquals(parent, statement))
            {
                if (parent is not (StoreLocal or Return or ExpressionStatement or StoreStackSlot))
                    return false;
                _rawPriorValues.Add(handler, prior);
                foreach (IrNode value in prior)
                    if (!_scheduledPriorValues.Add(value))
                        return false;
                return true;
            }
            if (parent is not Call and not NewObject || planned.Parent is not { } plannedParent
                || parent.Children.Count != plannedParent.Children.Count
                || !ClassicInverseRealizationRules.PayloadEquals(parent, plannedParent))
                return false;
            var prefix = new List<IrNode>();
            for (int i = 0; i < parent.Children.Count; i++)
            {
                if (!_budget.Charge())
                    return false;
                IrNode earlier = parent.Children[i];
                if (ReferenceEquals(earlier, current))
                    break;
                if (earlier is LoadStackSlot read)
                {
                    if (!BindSlot(read.Slot, statement, out StoreStackSlot store, expectedReads: 1)
                        || !ReferenceEquals(store.Parent, handler.Parent)
                        || _familyPositions[store] >= _familyPositions[handler]
                        || !Equals(read.Type, store.Value.ResultType)
                        || !ClassicInverseExpressionRules.SameTree(store.Value, plannedParent.Children[i], _budget))
                        return false;
                    _familyStoreFrames.Add(store);
                    _familyTraffic.Add(read);
                    continue;
                }
                if (earlier is not Constant and not LoadLocal and not LoadArgument
                    and not LoadLocalAddress and not LoadArgumentAddress)
                    return false;
                if (!ClassicInverseExpressionRules.SameTree(earlier, plannedParent.Children[i], _budget))
                    return false;
                if (earlier.SourceOffset < handler.Value.SourceOffset)
                    prefix.Add(earlier);
            }
            prior.InsertRange(0, prefix);
            planned = plannedParent;
        }
        return true;
    }

    bool VisitInterpolation(IrNode node, Action<IrNode> visit, ImmutableArray<string>.Builder? effects,
        Func<string, string>? qualify = null, bool bindOutputTypes = false)
    {
        if (node is not InterpolatedStringExpression interpolation)
            return false;
        if (!_interpolations.TryGetValue(node.SourceOffset, out InterpolationBinding? binding))
        {
            _terminal = Decline(ClassicInverseDeclineReason.MissingImportCorrespondence,
                "an interpolation has no raw handler receipt");
            return true;
        }
        if (!interpolation.Parts.SequenceEqual(binding.Planning.Parts)
            || interpolation.Children.Count != binding.Planning.Children.Count)
        {
            _terminal = Decline(ClassicInverseDeclineReason.UnrealizedSemanticEffect,
                "an interpolation changed its ordered parts or values");
            return true;
        }
        Emit(binding.Constructor);
        for (int i = 0; i < interpolation.Parts.Length && _terminal is null; i++)
        {
            if (!_budget.Charge())
            {
                _terminal = Failure("interpolation effect accounting exhausted the planning budget");
                break;
            }
            InterpolatedStringPart part = interpolation.Parts[i];
            if (!part.IsLiteral)
                visit(interpolation.Children[part.ExpressionIndex]);
            Emit(binding.Appends[i]);
        }
        Emit(binding.End);
        return true;

        void Emit(IrNode effectNode)
        {
            if (_terminal is not null || effects is null)
                return;
            if (!_budget.Charge())
            {
                _terminal = Failure("interpolation effect accounting exhausted the planning budget");
                return;
            }
            string effect = ClassicInverseNodeFacts.EffectSignature(effectNode, _shell.Machine,
                bindOutputTypes ? _planning.TypeBinding : null, _budget)!;
            effects.Add(qualify?.Invoke(effect) ?? effect);
        }
    }
}
