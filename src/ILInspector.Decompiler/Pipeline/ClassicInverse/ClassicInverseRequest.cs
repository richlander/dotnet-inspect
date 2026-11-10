using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// One classic request at the general inverse's boundary.
/// <para>
/// The identities and the relationship certificate are opaque owner evidence:
/// the inverse neither recreates them from names or IR nor selects replacements.
/// The bodies are the unmodified import snapshots the request authenticates.
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
        int stateMachineLocal)
    {
        DeclaredMethod = declaredMethod;
        ExecutionMethod = executionMethod;
        Relationship = relationship;
        AcquisitionGuard = acquisitionGuard;
        KickoffBody = kickoffBody;
        ExecutionBody = executionBody;
        StateMachineLocal = stateMachineLocal;
    }

    internal MetadataMethodAddress? DeclaredMethod { get; }

    internal MetadataMethodAddress? ExecutionMethod { get; }

    internal StateMachineRelationship? Relationship { get; }

    internal object? AcquisitionGuard { get; }

    /// <summary>The unmodified kickoff import snapshot.</summary>
    internal IrFunction KickoffBody { get; }

    /// <summary>The unmodified execution import snapshot.</summary>
    internal IrFunction ExecutionBody { get; }

    /// <summary>The kickoff local that holds the state-machine value.</summary>
    internal int StateMachineLocal { get; }

    /// <summary>
    /// Checks that the request's identities and bodies describe one another.
    /// A request that fails this is not a healthy decline; it is invalid, and
    /// the pass reports it as a visible failure.
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
        if (ExecutionMethod is { } exceptionOwner
            && ExecutionBody.ExceptionFlow is
                InstructionExceptionFlowResult<
                    InstructionExceptionFlowFacts>.Available exceptionFlow
            && exceptionFlow.Value.Body.Method != exceptionOwner)
        {
            return "the Instructions exception-flow body is not the "
                + "relationship's MoveNext MethodDef";
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
}
