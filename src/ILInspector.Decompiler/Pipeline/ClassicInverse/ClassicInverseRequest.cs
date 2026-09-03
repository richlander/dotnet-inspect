using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// One authenticated classic request at the inverse-core boundary.
/// <para>
/// The identities and the relationship certificate are opaque owner evidence:
/// the core neither recreates them from names or IR nor selects replacements.
/// The bodies are unmodified import snapshots the caller still owns; the core
/// reads them during planning and publishes nothing that points back into them.
/// </para>
/// </summary>
internal sealed class ClassicInverseRequest
{
    internal ClassicInverseRequest(
        MetadataMethodAddress? declaredMethod,
        MetadataMethodAddress? executionMethod,
        StateMachineRelationship? relationship,
        object? acquisitionGuard,
        IrFunction kickoffBody,
        IrFunction executionBody,
        int stateMachineLocal,
        int kickoffSourceOffset,
        ImmutableHashSet<int> executionImportOffsets,
        Action<IrFunction, ImmutableArray<IIrPass>>? runPasses = null)
    {
        DeclaredMethod = declaredMethod;
        ExecutionMethod = executionMethod;
        Relationship = relationship;
        AcquisitionGuard = acquisitionGuard;
        KickoffBody = kickoffBody;
        ExecutionBody = executionBody;
        StateMachineLocal = stateMachineLocal;
        KickoffSourceOffset = kickoffSourceOffset;
        ExecutionImportOffsets = executionImportOffsets;
        RunPasses = runPasses;
    }

    internal MetadataMethodAddress? DeclaredMethod { get; }

    internal MetadataMethodAddress? ExecutionMethod { get; }

    internal StateMachineRelationship? Relationship { get; }

    internal object? AcquisitionGuard { get; }

    /// <summary>The unmodified kickoff import snapshot.</summary>
    internal IrFunction KickoffBody { get; }

    /// <summary>
    /// The unmodified execution import snapshot. The core derives a separate
    /// planning view from a detached clone.
    /// </summary>
    internal IrFunction ExecutionBody { get; }

    /// <summary>The kickoff local that holds the state-machine value.</summary>
    internal int StateMachineLocal { get; }

    /// <summary>The kickoff IL offset a reconstructed body anchors to.</summary>
    internal int KickoffSourceOffset { get; }

    /// <summary>
    /// Every IL offset present in the unmodified execution import snapshot.
    /// A receipt that cites an offset outside this set has no import
    /// correspondence and cannot license consumption.
    /// </summary>
    internal ImmutableHashSet<int> ExecutionImportOffsets { get; }

    internal Action<IrFunction, ImmutableArray<IIrPass>>? RunPasses { get; }

    /// <summary>
    /// Checks that the request's identities and bodies describe one another.
    /// A request that fails this is not a healthy request outside the recipe
    /// domain; it is invalid, and the core reports
    /// <see cref="ClassicInverseFailureKind.InvalidCorrelation"/>.
    /// </summary>
    internal string? CorrelationFailure()
    {
        bool hasOwnerEvidence = DeclaredMethod is not null
            || ExecutionMethod is not null
            || Relationship is not null
            || AcquisitionGuard is not null;
        if (hasOwnerEvidence)
        {
            if (DeclaredMethod is not { } ownerDeclared
                || ExecutionMethod is not { } ownerExecution
                || Relationship is not { } ownerRelationship
                || AcquisitionGuard is null)
            {
                return "the owner-issued request evidence is incomplete";
            }
            if (ownerRelationship.Kind
                    != StateMachineClaimKind.ClassicAsync
                || ownerRelationship.Kickoff != ownerDeclared)
            {
                return "the declared MethodDef contradicts the classic relationship";
            }
            if (!ownerRelationship.TryGetMethod(
                    StateMachineMethodRole.MoveNext,
                    out MetadataMethodAddress relationshipExecution)
                || relationshipExecution != ownerExecution)
            {
                return "the execution MethodDef is not the relationship's MoveNext role";
            }
        }

        if (DeclaredMethod is { } declared
            && !MatchesMethod(KickoffBody, declared))
        {
            return "the kickoff body does not match its owner-issued MethodDef";
        }
        if (ExecutionMethod is { } execution
            && !MatchesMethod(ExecutionBody, execution))
        {
            return "the execution body does not match its owner-issued MethodDef";
        }

        if (ExecutionBody.Name != "MoveNext")
            return "the execution body is not a MoveNext body";

        if (!HasBodyReplacingBodies
            && (StateMachineLocal < 0
                || StateMachineLocal >= KickoffBody.Locals.Length))
        {
            return "the kickoff has no state-machine local";
        }

        TypeRef executing = ClassicInverseNodeFacts.Definition(
            ExecutionBody.DeclaringType);
        if (!HasBodyReplacingBodies)
        {
            TypeRef stateMachineType = ClassicInverseNodeFacts.Definition(
                KickoffBody.Locals[StateMachineLocal]);
            if (!stateMachineType.Equals(executing))
            {
                return "the execution body does not belong to the kickoff's state machine";
            }
        }

        if (Relationship is { } relationship
            && !executing.DefinitionHandle.IsNil)
        {
            MetadataTypeDefinitionAddress expected = relationship.StateMachineType;
            if (executing.DefinitionModuleVersionId != expected.ModuleVersionId
                || System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(
                        executing.DefinitionHandle)
                    != expected.Definition.Value)
            {
                return "the execution body contradicts the owner-issued relationship";
            }
        }

        return null;
    }

    internal bool HasBodyReplacingBodies =>
        Relationship is not null
        && IsBodyReplacing(KickoffBody)
        && IsBodyReplacing(ExecutionBody);

    static bool MatchesMethod(
        IrFunction body,
        MetadataMethodAddress method)
        => body.MetadataToken == method.Token
            && ClassicInverseNodeFacts.Definition(body.DeclaringType)
                .DefinitionModuleVersionId == method.ModuleVersionId;

    static bool IsBodyReplacing(IrFunction body)
        => body.Body.Blocks is
        [
            {
                Children:
                [
                    Throw
                    {
                        Value: Constant { Value: null },
                    },
                ],
            },
        ];

    /// <summary>
    /// The IL offsets an import snapshot carries. Used to bind receipts issued
    /// from a derived planning view back to the unmodified snapshot.
    /// </summary>
    internal static ImmutableHashSet<int> OffsetsOf(IrFunction function)
    {
        var offsets = ImmutableHashSet.CreateBuilder<int>();
        foreach (IrNode node in function.Body.Descendants.Prepend(function.Body))
        {
            if (node.SourceOffset >= 0)
                offsets.Add(node.SourceOffset);
        }
        return offsets.ToImmutable();
    }
}

internal sealed record ClassicInversePlanningView(
    IrFunction KickoffBody,
    IrFunction ExecutionBody)
{
    internal static ClassicInversePlanningView Derive(
        ClassicInverseRequest request)
    {
        var kickoff = (IrFunction)request.KickoffBody.Clone();
        var execution = (IrFunction)request.ExecutionBody.Clone();
        Run(
            kickoff,
            [.. IrPasses.Default.TakeWhile(
                pass => pass is not ClassicAsyncReconstructionPass)]);
        Run(
            execution,
            IrPasses.ForReconstruction<ClassicAsyncReconstructionPass>());
        return new ClassicInversePlanningView(kickoff, execution);

        void Run(IrFunction body, ImmutableArray<IIrPass> passes)
        {
            if (request.RunPasses is null)
                IrPasses.Run(body, passes);
            else
                request.RunPasses(body, passes);
        }
    }
}
