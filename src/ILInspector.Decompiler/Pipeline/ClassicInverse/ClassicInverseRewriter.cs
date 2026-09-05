namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Builds one recipe's output subtree from an input subtree, applying only the
/// named correspondences the candidate has declared:
/// <list type="bullet">
/// <item>the compiler's <c>awaiter.GetResult()</c> becomes an <c>await</c>, and
/// the operand the compiler passed to <c>GetAwaiter</c> becomes that await's
/// operand;</item>
/// <item>a state-machine parameter field becomes the kickoff argument;</item>
/// <item>a hoisted state-machine local becomes the output local; and</item>
/// <item>a remapped execution local becomes the output local.</item>
/// </list>
/// Every other node is reproduced structurally. Anything the rewriter cannot
/// place — a read of the machine reference itself, an unmapped hoisted field, a
/// spilled stack slot — fails the rewrite, so the recipe produces no candidate
/// rather than a body with an invented or dropped value.
/// <para>
/// The rewriter only proposes. <see cref="ClassicInverseAccountant"/> re-derives
/// the correspondence in lockstep, so a rewriting bug cannot license a plan.
/// </para>
/// </summary>
internal sealed class ClassicInverseRewriter
{
    readonly ClassicInversePlanningView _planning;
    readonly ClassicInverseShellFacts _shell;
    readonly ClassicInverseCandidate _candidate;
    readonly ClassicInverseBudget _budget;
    readonly IReadOnlyDictionary<Call, IrExpression> _awaitOperands;
    readonly Dictionary<Call, IrNode> _awaitClaimSources =
        new(ReferenceEqualityComparer.Instance);

    internal ClassicInverseRewriter(
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        ClassicInverseCandidate candidate,
        ClassicInverseBudget budget,
        IReadOnlyDictionary<Call, IrExpression> awaitOperands)
    {
        _planning = planning;
        _shell = shell;
        _candidate = candidate;
        _budget = budget;
        _awaitOperands = awaitOperands;
        foreach (Block block in planning.KickoffBody.Body.Blocks)
        {
            if (!budget.Charge())
                return;
            foreach (IrNode statement in block.Children)
            {
                if (!budget.Charge())
                    return;
                if (statement is StoreField
                    {
                        Instance: LoadLocalAddress,
                        Value: LoadArgument argument,
                    } transfer
                    && ClassicInverseNodeFacts.IsMachineField(
                        transfer.Field,
                        shell.Machine)
                    && Equals(transfer.Field.Type, argument.Type))
                {
                    candidate.MapParameterField(transfer.Field, argument.Index);
                }
            }
        }
    }

    /// <summary>
    /// Attributes the <c>AwaitResult</c> claim for one <c>GetResult</c> call to
    /// an enclosing node — the compiler temporary the recipe consumes as a
    /// whole. Without this the temporary store would have no disposition.
    /// </summary>
    internal void AttributeAwaitTo(Call getResult, IrNode source)
        => _awaitClaimSources[getResult] = source;

    internal IrNode? Rewrite(IrNode source)
    {
        if (_budget.Exhausted)
            return null;
        IrNode? output = RewriteCore(source);
        if (output is null)
            return null;
        foreach (IrNode node in output.Descendants.Prepend(output))
        {
            if (!_budget.Charge())
                return null;
            if (node is LoadElement load)
                load.ResultIsDynamic = IrImporter.ArrayElementDynamicFact(load.Array);
        }
        return output;
    }

    IrNode? RewriteCore(IrNode source)
    {
        if (!_budget.Charge())
            return null;
        if (ClassicInverseRealizationRules.IsAwaiterGetResult(source, _shell))
            return RewriteAwait((Call)source);

        switch (source)
        {
            case LoadField { Instance: LoadArgument { Index: 0 } } read
                when ClassicInverseNodeFacts.IsMachineField(read.Field, _shell.Machine):
                return MachineStorage(read.Field);

            case LoadFieldAddress { Instance: LoadArgument { Index: 0 } } read
                when ClassicInverseNodeFacts.IsMachineField(read.Field, _shell.Machine):
                return MachineStorage(read.Field);

            case LoadLocal local
                when _candidate.LocalRemap.TryGetValue(local.Index, out int mapped):
                return new LoadLocal(mapped, local.Type);

            case LoadLocalAddress address
                when _candidate.LocalRemap.TryGetValue(address.Index, out int mapped):
                return new LoadLocalAddress(mapped, address.Type);

            case LoadArgument { Index: 0 }:
            case LoadLocal:
            case LoadLocalAddress:
            case LoadStackSlot:
            case StoreStackSlot:
                return null;
        }

        foreach (IrNode unused in source.Descendants)
        {
            if (!_budget.Charge())
                return null;
        }
        IrNode clone = source.Clone();
        for (int i = 0; i < source.Children.Count; i++)
        {
            IrNode? child = RewriteCore(source.Children[i]);
            if (child is null)
                return null;
            clone.SetChild(i, child);
        }
        return clone;
    }

    IrNode? RewriteAwait(Call getResult)
    {
        IrExpression? operand = AwaitedOperand(getResult);
        if (operand is null)
            return null;
        IrNode? rewritten = RewriteCore(operand);
        if (rewritten is not IrExpression operandOutput)
            return null;

        var await = new AwaitExpression(
            operandOutput,
            getResult.Callee.ReturnType,
            getResult.Callee.ReturnIsDynamic);
        _candidate.Claim(
            _awaitClaimSources.TryGetValue(getResult, out IrNode? attributed)
                ? attributed
                : getResult,
            await,
            ClassicInverseRealizationRule.AwaitResult);
        _candidate.Claim(
            operand,
            operandOutput,
            ClassicInverseRealizationRule.AwaitedOperand);
        return await;
    }

    IrExpression? AwaitedOperand(Call getResult)
        => _budget.Charge() ? _awaitOperands.GetValueOrDefault(getResult) : null;

    IrNode? MachineStorage(FieldRef field)
    {
        MachineFieldId id = MachineFieldId.Of(field);
        if (_candidate.HoistedLocals.TryGetValue(id, out int local))
        {
            return new LoadLocal(local, field.Type);
        }

        IrFunction kickoff = _planning.KickoffBody;
        int argumentBase = kickoff.Signature.HasThis ? 1 : 0;
        if (_candidate.ParameterFields.TryGetValue(id, out int argument))
        {
            if (argumentBase == 1 && argument == 0
                && Equals(field.Type, kickoff.DeclaringType))
            {
                return new LoadArgument(0, "this", kickoff.DeclaringType);
            }
            int parameterIndex = argument - argumentBase;
            if (parameterIndex < 0
                || parameterIndex >= kickoff.Signature.Parameters.Length)
                return null;

            Parameter parameter = kickoff.Signature.Parameters[parameterIndex];
            if (!Equals(parameter.Type, field.Type))
                return null;
            return new LoadArgument(
                argument,
                parameter.Name,
                parameter.Type)
            {
                IsDynamic = parameter.IsDynamic,
                ArrayElementIsDynamic = parameter.ArrayElementIsDynamic,
            };
        }

        return null;
    }
}
