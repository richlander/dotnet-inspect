using System.Collections.Immutable;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Reconstructs classic async state-machine kickoffs (runtime-async=off) back to
/// async bodies. The source logic lives in <c>&lt;M&gt;d__N.MoveNext</c>; the public
/// kickoff only initializes the state machine and returns the builder's task.
/// <para>
/// The pass owns acquisition and application only. It seeds a request from
/// Metadata's authenticated relationship and the exact execution MethodDef,
/// imports that body, and hands the request to
/// <see cref="ClassicInverseCore"/>. Every reconstruction decision — and every
/// proof that licenses one — belongs to the core
/// (<c>docs/design/classic-async-reconstruction.md</c>). The pass never
/// reconstructs from shape resemblance of its own.
/// </para>
/// </summary>
public sealed class ClassicAsyncReconstructionPass : IIrPass
{
    public string Name => "classic-async-reconstruction";

    public void Run(IrFunction function, PassContext context)
    {
        if (TryAcknowledgeSupportMethod(function, context))
            return;

        if (context.ImportMethodBody is null)
            return;
        ClassicAsyncRequestSeed? seed =
            (function.ClassicAsyncRequest as
                ClassicAsyncRequestAdapterResult.RequestAvailable)?.Request;
        if (!TryGetKickoff(function, out var kickoff))
            return;
        if (seed is not null
            && !MatchesStateMachine(
                kickoff.StateMachineType,
                seed.Relationship.StateMachineType))
        {
            return;
        }

        var moveNextMethod = new MethodRef(
            kickoff.StateMachineType,
            "MoveNext",
            TypeRef.CoreLib("System", "Void"),
            [],
            HasThis: true);
        if (seed is not null)
        {
            moveNextMethod = moveNextMethod with
            {
                ExactDefinitionAddress = seed.ExecutionMethod,
                ExactDefinitionAcquisitionGuard =
                    seed.AcquisitionGuard,
            };
        }

        if (!context.TryEnterCrossMethodPipeline(moveNextMethod, out var scope))
            return;

        ClassicInverseDecision decision;
        using (scope)
        {
            IrFunction? rawMoveNext = scope.Import();
            if (rawMoveNext is null)
                return;
            if (seed is null)
                return;

            IrFunction rawKickoff = context.ImportMethodBody!(new MethodRef(
                function.DeclaringType,
                function.Name,
                function.Signature.ReturnType,
                [.. function.Signature.Parameters.Select(
                    static parameter => parameter.Type)],
                function.Signature.HasThis)
            {
                ExactDefinitionAddress = seed.DeclaredMethod,
                ExactDefinitionAcquisitionGuard = seed.AcquisitionGuard,
            })!;
            if (rawKickoff is null)
                return;

            ImmutableHashSet<int> importOffsets =
                ClassicInverseRequest.OffsetsOf(rawMoveNext);

            decision = ClassicInverseCore.Decide(
                ClassicInverseCore.Request(
                    rawKickoff,
                    kickoff.StateMachineLocal,
                    kickoff.SourceOffset,
                    rawMoveNext,
                    importOffsets,
                    seed,
                    scope.Run));
        }

        switch (decision)
        {
            case ClassicInverseDecision.Reconstruct { Plan: var plan }:
                Apply(function, kickoff, plan, context);
                return;

            case ClassicInverseDecision.Decline decline
                when !decline.IsRecipeDomainMiss:
                MarkUnconsumedExecutionRegion(
                    function,
                    kickoff,
                    context,
                    decline.Detail);
                return;

            case ClassicInverseDecision.Failed failed:
                MarkPlanningFailure(function, kickoff, context, failed.Failure);
                return;

            default:
                return;
        }
    }

    static void Apply(
        IrFunction function,
        Kickoff kickoff,
        ClassicInversePlan plan,
        PassContext context)
    {
        context.Stepper.StepOver(
            $"reconstruct classic async '{function.Name}' from "
            + $"{kickoff.StateMachineType.Name}.MoveNext via {plan.Recipe}");

        // The plan is detached: the body is materialized fresh here, from
        // immutable values, after every request tree has been released.
        BlockContainer body = plan.MaterializeBody();

        function.MergeTypeFactsFrom(plan.TypeFacts);
        function.ResetLocals(plan.Locals, plan.LocalNames);
        function.RequiresAsyncBodyModifier = true;
        function.Body.DetachChildren();
        foreach (var block in body.Blocks.ToList())
        {
            block.Detach();
            function.Body.Add(block);
        }
    }

    sealed record Kickoff(TypeRef StateMachineType, int StateMachineLocal, int SourceOffset);

    static bool MatchesStateMachine(
        TypeRef observed,
        MetadataTypeDefinitionAddress expected)
    {
        if (observed.Kind == TypeRefKind.GenericInstance
            && observed.ElementType is { } definition)
        {
            observed = definition;
        }

        return observed.DefinitionModuleVersionId == expected.ModuleVersionId
            && !observed.DefinitionHandle.IsNil
            && MetadataTokens.GetToken(observed.DefinitionHandle)
                == expected.Definition.Value;
    }

    static bool TryAcknowledgeSupportMethod(IrFunction function, PassContext context)
    {
        if (function.Name is not ("MoveNext" or "SetStateMachine"))
            return false;
        if (function.DeclaringTypeCompilerGenerated != MetadataFactState.Yes)
            return false;
        if (!LooksLikeClassicAsyncStateMachine(function))
            return false;

        context.Stepper.StepOver($"acknowledge generated classic async support method '{function.DeclaringType.Name}.{function.Name}'");
        function.ResetLocals([], []);
        function.Body.DetachChildren();
        var block = new Block(0);
        block.Add(new Return(null));
        function.Body.Add(block);
        return true;
    }

    static bool LooksLikeClassicAsyncStateMachine(IrFunction function)
        => IsStateMachineType(function.DeclaringType)
            && function.Descendants.Any(static node => node switch
            {
                LoadField { Field.Name: "<>t__builder" } => true,
                LoadFieldAddress { Field.Name: "<>t__builder" } => true,
                StoreField { Field.Name: "<>t__builder" } => true,
                _ => false,
            });

    static bool TryGetKickoff(IrFunction function, out Kickoff kickoff)
    {
        kickoff = null!;
        if (function.Body.Blocks is not [var block])
            return false;

        StoreField? builderStore = null;
        ExpressionStatement? startStatement = null;
        Return? returnTask = null;

        foreach (var statement in block.Children)
        {
            if (statement is StoreField { Field.Name: "<>t__builder", Instance: LoadLocalAddress builderLocal } store
                && builderStore is null)
            {
                builderStore = store;
                if (builderLocal.Index < 0 || builderLocal.Index >= function.Locals.Length)
                    return false;
                continue;
            }

            if (statement is ExpressionStatement { Expression: Call { Callee.Name: "Start" } } expression)
                startStatement = expression;
            else if (statement is Return { Value: LoadProperty { PropertyName: "Task" } } ret)
                returnTask = ret;
        }

        if (builderStore?.Instance is not LoadLocalAddress stateMachineAddress
            || startStatement is null
            || returnTask is null)
        {
            return false;
        }

        var stateMachineType = function.Locals[stateMachineAddress.Index];
        if (IsStateMachineType(stateMachineType))
        {
            kickoff = new Kickoff(stateMachineType, stateMachineAddress.Index, builderStore.SourceOffset);
            return true;
        }

        return false;
    }

    static bool IsStateMachineType(TypeRef type)
    {
        var name = MetadataName(type);
        return name.StartsWith("<", StringComparison.Ordinal)
            && name.Contains(">d__", StringComparison.Ordinal);
    }

    static string MetadataName(TypeRef type)
    {
        var name = type.Kind == TypeRefKind.GenericInstance && type.ElementType is { } definition
            ? definition.Name
            : type.Name;
        var nested = name.LastIndexOf('+');
        return nested >= 0 ? name[(nested + 1)..] : name;
    }

    internal static bool IsMachineFieldStore(
        StoreField store,
        TypeRef machine)
        => ClassicInverseNodeFacts.IsMachineField(store.Field, machine);

    static void MarkUnconsumedExecutionRegion(
        IrFunction function,
        Kickoff kickoff,
        PassContext context,
        string detail)
    {
        context.Stepper.StepOver(
            $"decline classic async '{function.Name}': execution region contains unconsumed user effects");

        PreserveKickoff(
            function,
            kickoff,
            "execution region contains unconsumed user effects; original kickoff preserved");
        function.Diagnostics.Add(new DecompilerDiagnostic(
            DiagnosticIds.UnsupportedConstruct,
            "classic async reconstruction declined: execution region contains "
                + $"unconsumed user effects ({detail})"));
    }

    static void MarkPlanningFailure(
        IrFunction function,
        Kickoff kickoff,
        PassContext context,
        ClassicInverseFailure failure)
    {
        context.Stepper.StepOver(
            $"classic async planning failed for '{function.Name}': {failure}");

        PreserveKickoff(
            function,
            kickoff,
            $"classic async planning failed ({failure.Kind}); original kickoff preserved");
        function.Diagnostics.Add(new DecompilerDiagnostic(
            DiagnosticIds.UnsupportedConstruct,
            $"classic async reconstruction failed: {failure}"));
    }

    static void PreserveKickoff(IrFunction function, Kickoff kickoff, string reason)
    {
        IReadOnlyList<Block> originalBlocks = function.Body.Blocks;
        function.Body.DetachChildren();

        var block = new Block(originalBlocks[0].StartOffset);
        var marker = new UnsupportedNode(
            kickoff.SourceOffset,
            "classic async",
            reason);
        marker.SetSourceOffset(kickoff.SourceOffset);
        var markerStatement = new ExpressionStatement(marker);
        markerStatement.SetSourceOffset(kickoff.SourceOffset);
        block.Add(markerStatement);

        foreach (Block originalBlock in originalBlocks)
        {
            foreach (IrNode statement in originalBlock.DetachChildren())
                block.Add(statement);
        }

        function.Body.Add(block);
        function.RequiresAsyncBodyModifier = false;
    }
}
