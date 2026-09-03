namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The per-rule shape obligations plus the lockstep correspondence check that
/// together prove one realization.
/// <para>
/// Lockstep walks the input region and the output region at the same time. At
/// every position it demands either an identical node form, one of the named
/// remaps the recipe declared (hoisted field to local, parameter field to
/// argument, temporary local to its realized value), or a nested claim
/// boundary. Anything else fails. That is what makes a dropped input effect, an
/// invented output effect, a swapped operand, and a duplicated realization all
/// visible rather than plausible.
/// </para>
/// </summary>
internal static class ClassicInverseRealizationRules
{
    /// <summary>
    /// Normalizes one effect signature for comparison across a realization. The
    /// only normalization is exact and positive: the compiler's
    /// <c>awaiter.GetResult()</c> call is the input spelling of an
    /// <c>await</c>. A user method that happens to be named <c>GetResult</c>
    /// does not normalize, because its argument is not a proven awaiter slot.
    /// </summary>
    internal static string NormalizeEffect(
        IrNode node,
        string signature,
        ClassicInverseShellFacts shell,
        ClassicInverseRealizationRule rule)
        => IsAwaiterGetResult(node, shell) ? "await" : signature;

    internal static bool IsAwaiterGetResult(IrNode node, ClassicInverseShellFacts shell)
        => node is Call { Callee.Name: "GetResult" } call
            && call.Arguments.Count == 1
            && call.Arguments[0] is LoadLocalAddress awaiter
            && shell.AwaiterLocals.Contains(awaiter.Index);

    internal static bool Verify(
        ClassicInverseClaim claim,
        ClassicInverseCandidate candidate,
        ClassicInverseShellFacts shell,
        IReadOnlyDictionary<IrNode, ClassicInverseClaim> claimBySource,
        IReadOnlyDictionary<IrNode, ClassicInverseClaim> claimByOutput,
        out string failure)
    {
        var context = new Context(
            candidate,
            shell,
            claimBySource,
            claimByOutput,
            claim.Source,
            claim.Output);

        switch (claim.Rule)
        {
            case ClassicInverseRealizationRule.AwaitResult:
                return VerifyAwaitResult(claim, context, out failure);

            case ClassicInverseRealizationRule.AwaitedOperand:
                return VerifyAwaitedOperand(claim, context, out failure);

            case ClassicInverseRealizationRule.LoopElement:
                return VerifyLoopElement(claim, context, out failure);

            case ClassicInverseRealizationRule.LoopCollection:
                if (claim.Output.Parent is not ForeachStatement loop
                    || !ReferenceEquals(loop.Collection, claim.Output))
                {
                    failure = "loop collection does not realize as the foreach collection";
                    return false;
                }
                return Lockstep(claim.Source, claim.Output, context, out failure);

            case ClassicInverseRealizationRule.ControlCondition:
                return VerifyControlCondition(claim, context, out failure);

            case ClassicInverseRealizationRule.ResultStore:
            case ClassicInverseRealizationRule.LoopAccumulator:
                return VerifyStore(claim, context, out failure);

            case ClassicInverseRealizationRule.Statement:
                if (claim.Source is not ExpressionStatement
                    || claim.Output is not ExpressionStatement)
                {
                    failure = "statement realization is not statement-shaped";
                    return false;
                }
                return Lockstep(claim.Source, claim.Output, context, out failure);

            case ClassicInverseRealizationRule.ValueExpression:
                return Lockstep(claim.Source, claim.Output, context, out failure);

            default:
                failure = $"unknown realization rule '{claim.Rule}'";
                return false;
        }
    }

    static bool VerifyAwaitResult(
        ClassicInverseClaim claim,
        Context context,
        out string failure)
    {
        if (claim.Output is not AwaitExpression await)
        {
            failure = "await result does not realize as an await expression";
            return false;
        }

        IrNode value = claim.Source;
        if (value is StoreLocal store)
            value = store.Value;
        if (value is Convert { IsChecked: false } convert)
        {
            if (!CSharpConversionRules.IsImplicitNumericAssignment(
                    convert.Operand.ResultType ?? convert.Target,
                    convert.Target))
            {
                failure = "await result is wrapped in a conversion that is not an implicit numeric assignment";
                return false;
            }
            value = convert.Operand;
        }

        if (!IsAwaiterGetResult(value, context.Shell))
        {
            failure = "await result is not the compiler's awaiter GetResult call";
            return false;
        }

        var getResult = (Call)value;
        if (!Equals(await.ResultType, getResult.Callee.ReturnType))
        {
            failure = "await result type does not match the awaiter's GetResult return type";
            return false;
        }

        if (!context.ClaimByOutput.ContainsKey(await.Operand))
        {
            failure = "the await operand carries no realization of its own";
            return false;
        }

        failure = "";
        return true;
    }

    static bool VerifyAwaitedOperand(
        ClassicInverseClaim claim,
        Context context,
        out string failure)
    {
        if (claim.Source.Parent is not Call { Callee.Name: "GetAwaiter" } getAwaiter
            || getAwaiter.Arguments.Count != 1
            || !ReferenceEquals(getAwaiter.Arguments[0], claim.Source)
            || getAwaiter.Parent is not StoreLocal bind
            || !context.Shell.AwaiterLocals.Contains(bind.Index))
        {
            failure = "the claimed operand is not the compiler's GetAwaiter receiver";
            return false;
        }

        if (claim.Output.Parent is not AwaitExpression await
            || !ReferenceEquals(await.Operand, claim.Output))
        {
            failure = "the operand does not realize as an await operand";
            return false;
        }

        return Lockstep(claim.Source, claim.Output, context, out failure);
    }

    static bool VerifyLoopElement(
        ClassicInverseClaim claim,
        Context context,
        out string failure)
    {
        switch (claim.Source)
        {
            case StoreStackSlot
            {
                Value: LoadElement
                {
                    Array: LoadField array,
                    Index: LoadField index,
                },
            }
                when array.Instance is LoadArgument { Index: 0 }
                    && index.Instance is LoadArgument { Index: 0 }
                    && ClassicInverseNodeFacts.IsMachineField(
                        array.Field,
                        context.Shell.Machine)
                    && ClassicInverseNodeFacts.IsMachineField(
                        index.Field,
                        context.Shell.Machine)
                    && array.Field.Name.StartsWith("<>7__wrap", StringComparison.Ordinal)
                    && index.Field.Name.StartsWith("<>7__wrap", StringComparison.Ordinal):
                if (claim.Output is not Block body
                    || body.Parent is not ForeachStatement loop
                    || !ReferenceEquals(loop.Body, body))
                {
                    failure = "the hoisted loop element does not realize as a foreach binding";
                    return false;
                }
                failure = "";
                return true;

            case LoadStackSlot:
                if (claim.Output.Parent is not AwaitExpression await
                    || !ReferenceEquals(await.Operand, claim.Output)
                    || claim.Output is not LoadLocal read
                    || ForeachVariable(claim.Output) != read.Index)
                {
                    failure = "the spilled loop element does not realize as the foreach variable";
                    return false;
                }
                failure = "";
                return true;

            default:
                failure = "the claimed loop element is not the compiler's hoisted element read";
                return false;
        }
    }

    static int ForeachVariable(IrNode node)
    {
        IrNode? current = node;
        while (current is not null)
        {
            if (current.Parent is ForeachStatement loop
                && ReferenceEquals(loop.Body, current))
            {
                return loop.LocalIndex;
            }
            current = current.Parent;
        }
        return -1;
    }

    static bool VerifyControlCondition(
        ClassicInverseClaim claim,
        Context context,
        out string failure)
    {
        if (claim.Source is not ConditionalBranch { Condition: LogicalNot inverted })
        {
            failure = "the claimed control condition is not an inverted branch";
            return false;
        }

        if (claim.Output is not Conditional conditional)
        {
            failure = "the control condition does not realize as a conditional";
            return false;
        }

        return Lockstep(inverted.Operand, conditional.Condition, context, out failure);
    }

    static bool VerifyStore(
        ClassicInverseClaim claim,
        Context context,
        out string failure)
    {
        IrNode? sourceValue = claim.Source switch
        {
            StoreLocal store => store.Value,
            StoreField store => store.Value,
            _ => null,
        };
        if (sourceValue is null)
        {
            failure = "the claimed store is not a local or hoisted-field store";
            return false;
        }

        switch (claim.Output)
        {
            case Return { Value: { } returned }:
                if (claim.Source is StoreLocal resultStore
                    && context.Candidate.ResultLocal >= 0
                    && resultStore.Index != context.Candidate.ResultLocal)
                {
                    failure = "a non-result store realizes as the method result";
                    return false;
                }
                return Lockstep(sourceValue, returned, context, out failure);

            case StoreLocal:
            case ForeachStatement:
                return Lockstep(claim.Source, claim.Output, context, out failure);

            default:
                return Lockstep(sourceValue, claim.Output, context, out failure);
        }
    }

    // ---- Lockstep correspondence ---------------------------------------

    readonly record struct Context(
        ClassicInverseCandidate Candidate,
        ClassicInverseShellFacts Shell,
        IReadOnlyDictionary<IrNode, ClassicInverseClaim> ClaimBySource,
        IReadOnlyDictionary<IrNode, ClassicInverseClaim> ClaimByOutput,
        IrNode SourceRoot,
        IrNode OutputRoot);

    static bool Lockstep(
        IrNode source,
        IrNode output,
        Context context,
        out string failure)
    {
        failure = "";

        bool sourceIsNested = !ReferenceEquals(source, context.SourceRoot)
            && context.ClaimBySource.TryGetValue(source, out ClassicInverseClaim? nested);
        if (sourceIsNested)
        {
            ClassicInverseClaim claim = context.ClaimBySource[source];
            if (!ReferenceEquals(claim.Output, output))
            {
                failure = $"nested {claim.Rule} realization is not at its expected position";
                return false;
            }
            return true;
        }

        if (!ReferenceEquals(output, context.OutputRoot)
            && context.ClaimByOutput.TryGetValue(output, out ClassicInverseClaim? byOutput))
        {
            if (source is LoadLocal temp
                && context.Candidate.LocalValueRealizations.TryGetValue(
                    temp.Index,
                    out IrNode? realized)
                && ReferenceEquals(realized, output))
            {
                return true;
            }
            failure = $"output realized by {byOutput.Rule} appears where its input does not";
            return false;
        }

        switch (source)
        {
            case LoadField { Instance: LoadArgument { Index: 0 } } load
                when ClassicInverseNodeFacts.IsMachineField(
                    load.Field,
                    context.Shell.Machine):
                return MatchMachineRead(load.Field.Name, output, context, out failure);

            case LoadFieldAddress { Instance: LoadArgument { Index: 0 } } load
                when ClassicInverseNodeFacts.IsMachineField(
                    load.Field,
                    context.Shell.Machine):
                return MatchMachineRead(load.Field.Name, output, context, out failure);

            case LoadLocal local:
            {
                if (context.Candidate.LocalValueRealizations.TryGetValue(
                        local.Index,
                        out IrNode? realized))
                {
                    if (ReferenceEquals(realized, output))
                        return true;
                    failure = $"local {local.Index} realizes elsewhere";
                    return false;
                }
                if (context.Candidate.LocalRemap.TryGetValue(
                        local.Index,
                        out int mapped)
                    && output is LoadLocal outputLocal
                    && outputLocal.Index == mapped)
                {
                    return true;
                }
                failure = $"local {local.Index} has no declared realization";
                return false;
            }

            case StoreLocal store when output is StoreLocal outputStore:
            {
                if (!context.Candidate.LocalRemap.TryGetValue(
                        store.Index,
                        out int mapped)
                    || mapped != outputStore.Index)
                {
                    failure = $"store to local {store.Index} has no declared realization";
                    return false;
                }
                return Lockstep(store.Value, outputStore.Value, context, out failure);
            }

            case StoreField store
                when store.Instance is LoadArgument { Index: 0 }
                    && ClassicInverseNodeFacts.IsMachineField(
                        store.Field,
                        context.Shell.Machine)
                    && output is StoreLocal hoistedStore:
            {
                if (!context.Candidate.HoistedLocals.TryGetValue(
                        store.Field.Name,
                        out int mapped)
                    || mapped != hoistedStore.Index)
                {
                    failure = $"hoisted store '{store.Field.Name}' has no declared realization";
                    return false;
                }
                return Lockstep(store.Value, hoistedStore.Value, context, out failure);
            }

            case Convert { IsChecked: false } convert
                when output is not Convert
                    && CSharpConversionRules.IsImplicitNumericAssignment(
                        convert.Operand.ResultType ?? convert.Target,
                        convert.Target):
                return Lockstep(convert.Operand, output, context, out failure);

            case StoreStackSlot spill when output is not StoreStackSlot:
                failure = "a spilled value has no declared realization";
                return false;
        }

        if (source.GetType() != output.GetType())
        {
            failure = $"'{source.Describe()}' does not correspond to '{output.Describe()}'";
            return false;
        }

        if (!PayloadEquals(source, output))
        {
            failure = $"'{source.Describe()}' differs from '{output.Describe()}'";
            return false;
        }

        if (source.Children.Count != output.Children.Count)
        {
            failure = $"'{source.Describe()}' changed arity in the output";
            return false;
        }

        for (int i = 0; i < source.Children.Count; i++)
        {
            if (!Lockstep(source.Children[i], output.Children[i], context, out failure))
                return false;
        }

        return true;
    }

    static bool MatchMachineRead(
        string field,
        IrNode output,
        Context context,
        out string failure)
    {
        if (context.Candidate.HoistedLocals.TryGetValue(field, out int local)
            && output is LoadLocal loadLocal
            && loadLocal.Index == local)
        {
            failure = "";
            return true;
        }

        if (context.Candidate.ParameterFields.TryGetValue(field, out int argument)
            && output is LoadArgument loadArgument
            && loadArgument.Index == argument)
        {
            failure = "";
            return true;
        }

        failure = $"state-machine storage '{field}' has no declared realization";
        return false;
    }

    static bool PayloadEquals(IrNode source, IrNode output)
        => (source, output) switch
        {
            (Block, Block) => true,
            (BlockContainer, BlockContainer) => true,
            _ => string.Equals(
                source.Describe(),
                output.Describe(),
                StringComparison.Ordinal),
        };
}
