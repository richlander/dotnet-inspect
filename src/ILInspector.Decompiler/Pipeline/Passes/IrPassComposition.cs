using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace ILInspector.Decompiler.Pipeline;

internal enum IrPassPipelineProfile
{
    Partial,
    Complete,
    CapturingLambdaPreparation,
    IntermediateBody,
    Reconstruction,
}

internal static class IrPassComposition
{
    internal static ImmutableArray<IIrPass> Validate(
        string pipelineName,
        ImmutableArray<IIrPass> passes,
        IrPassPipelineProfile profile,
        Type? requestingPass = null)
    {
        if (passes.IsDefault)
            Fail(pipelineName, "the pass list is uninitialized");

        int slotsOnlyInlining = SingleIndex<ExpressionInliningPass>(
            pipelineName,
            passes,
            static pass => pass.SlotsOnly);
        int producerOnlyRetirement =
            SingleIndex<ProducerOnlySlotRetirementPass>(pipelineName, passes);
        int materialization = SingleIndex<SlotMaterializationPass>(pipelineName, passes);
        int coercion = SingleIndex<CoercionInsertionPass>(pipelineName, passes);
        int residualBinding = SingleIndex<ResidualSlotBindingPass>(pipelineName, passes);
        int definiteAssignment = SingleIndex<DefiniteAssignmentPass>(pipelineName, passes);

        if (materialization >= 0)
        {
            Require(
                pipelineName,
                slotsOnlyInlining >= 0,
                $"{nameof(SlotMaterializationPass)} requires the slots-only {nameof(ExpressionInliningPass)}");
            Require(
                pipelineName,
                slotsOnlyInlining < materialization,
                $"the slots-only {nameof(ExpressionInliningPass)} must precede {nameof(SlotMaterializationPass)}");
            if (producerOnlyRetirement >= 0)
            {
                Require(
                    pipelineName,
                    producerOnlyRetirement < materialization,
                    $"{nameof(ProducerOnlySlotRetirementPass)} must precede {nameof(SlotMaterializationPass)}");
            }
        }

        if (materialization >= 0 && coercion >= 0)
        {
            Require(
                pipelineName,
                materialization < coercion,
                $"{nameof(SlotMaterializationPass)} must precede {nameof(CoercionInsertionPass)}");
        }

        if (residualBinding >= 0)
        {
            Require(
                pipelineName,
                materialization >= 0,
                $"{nameof(ResidualSlotBindingPass)} requires {nameof(SlotMaterializationPass)}");
            Require(
                pipelineName,
                coercion >= 0,
                $"{nameof(ResidualSlotBindingPass)} requires {nameof(CoercionInsertionPass)}");
            Require(
                pipelineName,
                residualBinding == coercion + 1,
                $"{nameof(ResidualSlotBindingPass)} must immediately follow {nameof(CoercionInsertionPass)}");
        }

        if (definiteAssignment >= 0)
        {
            Require(
                pipelineName,
                definiteAssignment == passes.Length - 1,
                $"{nameof(DefiniteAssignmentPass)} must be the final pass");
            if (residualBinding >= 0)
            {
                Require(
                    pipelineName,
                    residualBinding < definiteAssignment,
                    $"{nameof(ResidualSlotBindingPass)} must precede {nameof(DefiniteAssignmentPass)}");
            }
        }

        switch (profile)
        {
            case IrPassPipelineProfile.Partial:
                break;
            case IrPassPipelineProfile.Complete:
                RequireStorageTail(
                    pipelineName,
                    slotsOnlyInlining,
                    producerOnlyRetirement,
                    materialization,
                    coercion,
                    residualBinding,
                    definiteAssignment);
                break;
            case IrPassPipelineProfile.CapturingLambdaPreparation:
                Exclude<ExpressionInliningPass>(
                    pipelineName,
                    passes,
                    static pass => pass.SlotsOnly,
                    "the slots-only expression-inlining tail");
                Exclude<ProducerOnlySlotRetirementPass>(pipelineName, passes);
                Exclude<SlotMaterializationPass>(pipelineName, passes);
                Exclude<CoercionInsertionPass>(pipelineName, passes);
                Exclude<ResidualSlotBindingPass>(pipelineName, passes);
                Exclude<DefiniteAssignmentPass>(pipelineName, passes);
                break;
            case IrPassPipelineProfile.IntermediateBody:
                Require(
                    pipelineName,
                    slotsOnlyInlining >= 0
                        && producerOnlyRetirement >= 0
                        && materialization >= 0
                        && coercion >= 0,
                    "the intermediate-body pipeline requires slots-only inlining, producer-only retirement, materialization, and coercion insertion");
                Exclude<ResidualSlotBindingPass>(pipelineName, passes);
                Exclude<DefiniteAssignmentPass>(pipelineName, passes);
                break;
            case IrPassPipelineProfile.Reconstruction:
                ExcludeReconstructionPasses(pipelineName, passes, requestingPass);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(profile), profile, null);
        }

        return passes;
    }

    static void RequireStorageTail(
        string pipelineName,
        int slotsOnlyInlining,
        int producerOnlyRetirement,
        int materialization,
        int coercion,
        int residualBinding,
        int definiteAssignment)
    {
        Require(
            pipelineName,
            slotsOnlyInlining >= 0,
            $"the complete pipeline requires the slots-only {nameof(ExpressionInliningPass)}");
        Require(
            pipelineName,
            producerOnlyRetirement >= 0,
            $"the complete pipeline requires {nameof(ProducerOnlySlotRetirementPass)}");
        Require(
            pipelineName,
            materialization >= 0,
            $"the complete pipeline requires {nameof(SlotMaterializationPass)}");
        Require(
            pipelineName,
            coercion >= 0,
            $"the complete pipeline requires {nameof(CoercionInsertionPass)}");
        Require(
            pipelineName,
            residualBinding >= 0,
            $"the complete pipeline requires {nameof(ResidualSlotBindingPass)}");
        Require(
            pipelineName,
            definiteAssignment >= 0,
            $"the complete pipeline requires {nameof(DefiniteAssignmentPass)}");
    }

    static void ExcludeReconstructionPasses(
        string pipelineName,
        ImmutableArray<IIrPass> passes,
        Type? requestingPass)
    {
        if (requestingPass is null)
            Fail(pipelineName, "a reconstruction pipeline must name its requesting pass");

        foreach (var pass in passes)
        {
            Type passType = pass.GetType();
            if (requestingPass.IsAssignableFrom(passType))
                Fail(pipelineName, $"the requesting pass {requestingPass.Name} must be excluded");

            if (pass is ReferenceSlotTargetBindingPass
                or ReferenceCoalesceBindingPass
                or ReferenceConditionalBindingPass
                or PrimitiveJoinBindingPass
                or ProducerOnlySlotRetirementPass
                or SlotMaterializationPass
                or ResidualSlotBindingPass
                or PdbScopeEntryLocalPass
                or PdbLocalScopePass
                or CheckedIntegerOperandPass
                or ScalarSelfUpdatePass
                or DefiniteAssignmentPass)
            {
                Fail(pipelineName, $"{passType.Name} is prohibited in reconstruction pipelines");
            }
        }
    }

    static int SingleIndex<TPass>(
        string pipelineName,
        ImmutableArray<IIrPass> passes,
        Func<TPass, bool>? predicate = null)
        where TPass : class, IIrPass
    {
        int found = -1;
        for (int i = 0; i < passes.Length; i++)
        {
            if (passes[i] is not TPass pass || predicate is not null && !predicate(pass))
                continue;

            if (found >= 0)
                Fail(pipelineName, $"contains more than one governed {typeof(TPass).Name}");
            found = i;
        }
        return found;
    }

    static void Exclude<TPass>(
        string pipelineName,
        ImmutableArray<IIrPass> passes,
        Func<TPass, bool>? predicate = null,
        string? description = null)
        where TPass : class, IIrPass
    {
        if (passes.Any(pass => pass is TPass typed && (predicate is null || predicate(typed))))
            Fail(pipelineName, $"{description ?? typeof(TPass).Name} is prohibited");
    }

    static void Require(string pipelineName, bool condition, string detail)
    {
        if (!condition)
            Fail(pipelineName, detail);
    }

    [DoesNotReturn]
    static void Fail(string pipelineName, string detail)
        => throw new InvalidOperationException(
            $"IR pass pipeline '{pipelineName}' is invalid: {detail}.");
}
