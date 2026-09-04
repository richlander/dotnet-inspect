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
    /// does not normalize, because it is not the exact typed member of the
    /// proven awaiter slot.
    /// </summary>
    internal static string NormalizeEffect(
        IrNode node,
        string signature,
        ClassicInverseShellFacts shell,
        ClassicInverseRealizationRule rule)
        => IsAwaiterGetResult(node, shell) ? "await" : signature;

    /// <summary>
    /// Whether <paramref name="node"/> is the exact <c>GetResult</c> member of a
    /// proven awaiter slot: an instance method with no declared parameters,
    /// declared on the same type the slot's local, its <c>GetAwaiter</c> bind,
    /// and its <c>&lt;&gt;u__N</c> cache all carry, invoked on the address of
    /// that very slot. The awaiter family is not enumerated, so a
    /// compiler-produced custom awaiter still normalizes; a same-named helper
    /// taking the awaiter by reference does not, because it is neither an
    /// instance member of the awaiter type nor parameterless.
    /// </summary>
    internal static bool IsAwaiterGetResult(IrNode node, ClassicInverseShellFacts shell)
        => node is Call { Callee: { Name: "GetResult", HasThis: true } callee } call
            && callee.ParameterTypes.IsDefaultOrEmpty
            && call.Arguments is [LoadLocalAddress awaiter]
            && shell.AwaiterLocals.Contains(awaiter.Index)
            && shell.AwaiterTypes.TryGetValue(awaiter.Index, out TypeRef? awaiterType)
            && awaiterType.Equals(callee.DeclaringType)
            && (awaiter.Type is null || awaiterType.Equals(awaiter.Type));

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

        if (!context.ClaimByOutput.TryGetValue(
                await.Operand,
                out ClassicInverseClaim? operandClaim))
        {
            failure = "the await operand carries no realization of its own";
            return false;
        }
        if (!MatchesAwaiterBind(getResult, operandClaim.Source, context.Shell))
        {
            failure = "the await operand does not belong to this GetResult awaiter slot";
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
            || !context.Shell.Protocol.Proves(
                bind,
                ClassicInverseLoweringProof.AwaiterBind)
            || !context.Shell.Protocol.Proves(
                getAwaiter,
                ClassicInverseLoweringProof.GetAwaiterCall))
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
            case StoreStackSlot spill
                when context.Candidate.LoopStorage is { } storage
                    && storage.IsElementLoad(spill.Value, context.Shell.Machine):
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
        if (inverted.Operand is not LoadField
            {
                Instance: LoadArgument { Index: 0 },
            } condition
            || !ClassicInverseNodeFacts.IsMachineField(
                condition.Field,
                context.Shell.Machine))
        {
            failure = "the conditional recipe does not model a compound predicate";
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

        if (source is Convert { IsChecked: false } implicitConversion
            && output is not Convert
            && CSharpConversionRules.IsImplicitNumericAssignment(
                implicitConversion.Operand.ResultType ?? implicitConversion.Target,
                implicitConversion.Target))
        {
            return Lockstep(
                implicitConversion.Operand,
                output,
                context,
                out failure);
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
                return MatchMachineRead(load.Field, output, context, out failure);

            case LoadFieldAddress { Instance: LoadArgument { Index: 0 } } load
                when ClassicInverseNodeFacts.IsMachineField(
                    load.Field,
                    context.Shell.Machine):
                return MatchMachineRead(load.Field, output, context, out failure);

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
                        MachineFieldId.Of(store.Field),
                        out int mapped)
                    || mapped != hoistedStore.Index)
                {
                    failure = $"hoisted store '{store.Field.Name}' has no declared realization";
                    return false;
                }
                return Lockstep(store.Value, hoistedStore.Value, context, out failure);
            }

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
        FieldRef field,
        IrNode output,
        Context context,
        out string failure)
    {
        MachineFieldId id = MachineFieldId.Of(field);
        if (context.Candidate.HoistedLocals.TryGetValue(id, out int local)
            && output is LoadLocal loadLocal
            && loadLocal.Index == local
            && Equals(field.Type, loadLocal.Type))
        {
            failure = "";
            return true;
        }

        if (context.Candidate.ParameterFields.TryGetValue(id, out int argument)
            && output is LoadArgument loadArgument
            && loadArgument.Index == argument
            && Equals(field.Type, loadArgument.Type))
        {
            failure = "";
            return true;
        }

        failure = $"state-machine storage '{field.Name}' has no declared realization";
        return false;
    }

    static bool MatchesAwaiterBind(
        Call getResult,
        IrNode operand,
        ClassicInverseShellFacts shell)
    {
        if (getResult.Arguments is not [LoadLocalAddress awaiter]
            || operand.Parent is not Call
            {
                Callee.Name: "GetAwaiter",
                Parent: StoreLocal bind,
            } getAwaiter
            || getAwaiter.Arguments.Count != 1
            || !ReferenceEquals(getAwaiter.Arguments[0], operand)
            || bind.Index != awaiter.Index
            || !shell.Protocol.Proves(
                bind,
                ClassicInverseLoweringProof.AwaiterBind)
            || !shell.Protocol.Proves(
                getAwaiter,
                ClassicInverseLoweringProof.GetAwaiterCall))
        {
            return false;
        }

        // The suspension's awaiter type is one fact, carried by the bind that
        // produced it and by the member the result is read through.
        if (!shell.AwaiterTypes.TryGetValue(awaiter.Index, out TypeRef? awaiterType)
            || !awaiterType.Equals(getAwaiter.Callee.ReturnType)
            || !awaiterType.Equals(bind.Type)
            || !awaiterType.Equals(getResult.Callee.DeclaringType))
        {
            return false;
        }

        IrNode root = getResult;
        while (root.Parent is not null)
            root = root.Parent;
        List<IrNode> nodes = [.. root.Descendants.Prepend(root)];
        int resultPosition = nodes.IndexOf(getResult);
        if (resultPosition < 0)
            return false;

        StoreLocal? reachingBind = nodes
            .Take(resultPosition)
            .OfType<StoreLocal>()
            .LastOrDefault(candidate =>
                candidate.Index == awaiter.Index
                && candidate.Value is Call { Callee.Name: "GetAwaiter" });
        return ReferenceEquals(reachingBind, bind);
    }

    static bool PayloadEquals(IrNode source, IrNode output)
        => (source, output) switch
        {
            (Block left, Block right) =>
                left.StartOffset == right.StartOffset,
            (BlockContainer, BlockContainer) => true,
            (Return, Return) => true,
            (ExpressionStatement, ExpressionStatement) => true,
            (LogicalNot, LogicalNot) => true,
            (LoadArgument left, LoadArgument right) =>
                left.Index == right.Index
                && left.Name == right.Name
                && Equals(left.Type, right.Type)
                && left.IsDynamic == right.IsDynamic
                && left.ArrayElementIsDynamic == right.ArrayElementIsDynamic,
            (Constant left, Constant right) =>
                Equals(left.Value, right.Value)
                && Equals(left.Type, right.Type),
            (Binary left, Binary right) =>
                left.Kind == right.Kind
                && left.IsChecked == right.IsChecked
                && left.IsUnsigned == right.IsUnsigned,
            (Comparison left, Comparison right) =>
                left.Kind == right.Kind
                && left.IsUnsigned == right.IsUnsigned,
            (Conditional left, Conditional right) =>
                Equals(left.MergedType, right.MergedType),
            (Convert left, Convert right) =>
                Equals(left.Target, right.Target)
                && left.IsChecked == right.IsChecked
                && left.IsUnsigned == right.IsUnsigned,
            (Box left, Box right) => Equals(left.Type, right.Type),
            (Call left, Call right) =>
                left.Callee == right.Callee
                && left.IsVirtual == right.IsVirtual
                && Equals(left.ConstrainedTo, right.ConstrainedTo)
                && left.ExtensionSyntaxConflict
                    == right.ExtensionSyntaxConflict,
            (NewObject left, NewObject right) =>
                left.Constructor == right.Constructor
                && left.AnonymousPropertyNames.SequenceEqual(
                    right.AnonymousPropertyNames),
            (LoadElement left, LoadElement right) =>
                Equals(left.ElementType, right.ElementType)
                && right.ResultIsDynamic
                    == IrImporter.ArrayElementDynamicFact(right.Array),
            (TupleExpression left, TupleExpression right) =>
                Equals(left.TupleType, right.TupleType),
            (ObjectInitializerExpression left,
                ObjectInitializerExpression right) =>
                left.IsCollection == right.IsCollection
                && left.Members.SequenceEqual(right.Members)
                && left.ArgumentCounts.SequenceEqual(right.ArgumentCounts)
                && left.ConsumedMethods.SequenceEqual(
                    right.ConsumedMethods)
                && left.ConsumedFields.SequenceEqual(
                    right.ConsumedFields)
                && left.ConsumedMethodsAreVirtual.SequenceEqual(
                    right.ConsumedMethodsAreVirtual),
            (WithExpression left, WithExpression right) =>
                left.Members.SequenceEqual(right.Members)
                && left.ConsumedMethods.SequenceEqual(
                    right.ConsumedMethods)
                && left.ConsumedFields.SequenceEqual(
                    right.ConsumedFields)
                && left.ConsumedMethodsAreVirtual.SequenceEqual(
                    right.ConsumedMethodsAreVirtual),
            _ => false,
        };
}
