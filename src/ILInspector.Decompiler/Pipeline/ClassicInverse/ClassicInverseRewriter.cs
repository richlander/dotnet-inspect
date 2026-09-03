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
    readonly Dictionary<Call, IrNode> _awaitClaimSources =
        new(ReferenceEqualityComparer.Instance);

    internal ClassicInverseRewriter(
        ClassicInversePlanningView planning,
        ClassicInverseShellFacts shell,
        ClassicInverseCandidate candidate)
    {
        _planning = planning;
        _shell = shell;
        _candidate = candidate;
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
        IrNode? output = RewriteCore(source);
        if (output is null)
            return null;
        foreach (IrNode node in output.Descendants.Prepend(output))
        {
            if (node is LoadElement load)
                load.ResultIsDynamic = IrImporter.ArrayElementDynamicFact(load.Array);
        }
        return output;
    }

    IrNode? RewriteCore(IrNode source)
    {
        if (ClassicInverseRealizationRules.IsAwaiterGetResult(source, _shell))
            return RewriteAwait((Call)source);

        switch (source)
        {
            case LoadField { Instance: LoadArgument { Index: 0 } } read
                when ClassicInverseNodeFacts.IsMachineField(read.Field, _shell.Machine):
                return MachineStorage(read.Field.Name);

            case LoadFieldAddress { Instance: LoadArgument { Index: 0 } } read
                when ClassicInverseNodeFacts.IsMachineField(read.Field, _shell.Machine):
                return MachineStorage(read.Field.Name);

            case LoadLocal local
                when _candidate.LocalRemap.TryGetValue(local.Index, out int mapped):
                return new LoadLocal(mapped, local.Type);

            case LoadArgument { Index: 0 }:
            case LoadLocal:
            case LoadStackSlot:
            case StoreStackSlot:
                return null;

            case Convert { IsChecked: false } convert
                when CSharpConversionRules.IsImplicitNumericAssignment(
                    convert.Operand.ResultType ?? convert.Target,
                    convert.Target):
                return RewriteCore(convert.Operand);
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
    {
        if (getResult.Arguments is not [LoadLocalAddress awaiterAddress])
            return null;

        List<IrNode> nodes = [.. _planning.ExecutionBody.Body.Descendants];
        int position = nodes.IndexOf(getResult);
        if (position < 0)
            return null;

        StoreLocal? bind = null;
        for (int i = 0; i < position; i++)
        {
            if (nodes[i] is StoreLocal { Value: Call { Callee.Name: "GetAwaiter" } call } store
                && store.Index == awaiterAddress.Index
                && call.Arguments.Count == 1)
            {
                bind = store;
            }
        }

        return bind?.Value is Call { Arguments: [IrExpression operand] } ? operand : null;
    }

    IrNode? MachineStorage(string field)
    {
        if (_candidate.HoistedLocals.TryGetValue(field, out int local)
            && _candidate.HoistedTypes.TryGetValue(field, out TypeRef? type))
        {
            return new LoadLocal(local, type);
        }

        IrFunction kickoff = _planning.KickoffBody;
        int argumentBase = kickoff.Signature.HasThis ? 1 : 0;
        for (int i = 0; i < kickoff.Signature.Parameters.Length; i++)
        {
            Parameter parameter = kickoff.Signature.Parameters[i];
            if (parameter.Name != field)
                continue;
            return new LoadArgument(
                argumentBase + i,
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
