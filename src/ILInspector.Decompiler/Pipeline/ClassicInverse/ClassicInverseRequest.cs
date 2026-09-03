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
        IrFunction kickoffBody,
        IrFunction executionBody,
        int stateMachineLocal,
        int kickoffSourceOffset,
        ImmutableHashSet<int> executionImportOffsets)
    {
        DeclaredMethod = declaredMethod;
        ExecutionMethod = executionMethod;
        Relationship = relationship;
        KickoffBody = kickoffBody;
        ExecutionBody = executionBody;
        StateMachineLocal = stateMachineLocal;
        KickoffSourceOffset = kickoffSourceOffset;
        ExecutionImportOffsets = executionImportOffsets;
    }

    internal MetadataMethodAddress? DeclaredMethod { get; }

    internal MetadataMethodAddress? ExecutionMethod { get; }

    internal StateMachineRelationship? Relationship { get; }

    /// <summary>The unmodified kickoff import snapshot.</summary>
    internal IrFunction KickoffBody { get; }

    /// <summary>
    /// The execution import snapshot, raised through Decompiler-owned
    /// prerequisite passes into a planning view. Every receipt issued from it
    /// still maps back to <see cref="ExecutionImportOffsets"/>.
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

    /// <summary>
    /// Checks that the request's identities and bodies describe one another.
    /// A request that fails this is not a healthy request outside the recipe
    /// domain; it is invalid, and the core reports
    /// <see cref="ClassicInverseFailureKind.InvalidCorrelation"/>.
    /// </summary>
    internal string? CorrelationFailure()
    {
        if (ExecutionBody.Name != "MoveNext")
            return "the execution body is not a MoveNext body";

        if (StateMachineLocal < 0 || StateMachineLocal >= KickoffBody.Locals.Length)
            return "the kickoff has no state-machine local";

        TypeRef declared = ClassicInverseNodeFacts.Definition(
            KickoffBody.Locals[StateMachineLocal]);
        TypeRef executing = ClassicInverseNodeFacts.Definition(
            ExecutionBody.DeclaringType);
        if (!declared.Equals(executing))
        {
            return "the execution body does not belong to the kickoff's state machine";
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
