using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>The relationship role of the MethodDef entering the adapter.</summary>
internal enum ClassicAsyncHostRole
{
    DeclaredKickoff,
    Execution,
    Support,
    Ordinary,
}

/// <summary>
/// Metadata-owned evidence materialized for one imported MethodDef.
/// </summary>
internal sealed record ClassicAsyncOwnerEvidence(
    MetadataMethodAddress RequestedMethod,
    ClassicAsyncHostRole HostRole,
    MethodClassification? Classification,
    StateMachineRelationshipResult Relationship,
    object AcquisitionGuard);

/// <summary>
/// Exact identities that can seed a classic inverse request once the inverse
/// owner supplies its body-snapshot contract.
/// </summary>
internal sealed record ClassicAsyncRequestSeed(
    MetadataMethodAddress DeclaredMethod,
    MetadataMethodAddress ExecutionMethod,
    StateMachineRelationship Relationship,
    object AcquisitionGuard);

/// <summary>
/// Closed result of adapting Metadata's classification and relationship
/// evidence at the Decompiler import boundary.
/// </summary>
internal abstract record ClassicAsyncRequestAdapterResult
{
    private protected ClassicAsyncRequestAdapterResult()
    {
    }

    internal sealed record RequestAvailable
        : ClassicAsyncRequestAdapterResult
    {
        internal RequestAvailable(
            ClassicAsyncOwnerEvidence evidence,
            ClassicAsyncRequestSeed request)
        {
            Evidence = evidence;
            Request = request;
        }

        internal ClassicAsyncOwnerEvidence Evidence { get; }
        internal ClassicAsyncRequestSeed Request { get; }
    }

    internal sealed record OwnerUnavailable
        : ClassicAsyncRequestAdapterResult
    {
        internal OwnerUnavailable(ClassicAsyncOwnerEvidence evidence)
            => Evidence = evidence;

        internal ClassicAsyncOwnerEvidence Evidence { get; }
    }

    internal sealed record Filtered
        : ClassicAsyncRequestAdapterResult
    {
        internal Filtered(ClassicAsyncOwnerEvidence evidence)
            => Evidence = evidence;

        internal ClassicAsyncOwnerEvidence Evidence { get; }
    }

    internal sealed record AcquisitionFailed
        : ClassicAsyncRequestAdapterResult
    {
        internal AcquisitionFailed(
            int requestedMethodToken,
            MethodClassification? classification,
            StateMachineRelationshipResult relationship,
            object acquisitionGuard,
            string detail)
        {
            RequestedMethodToken = requestedMethodToken;
            Classification = classification;
            Relationship = relationship;
            AcquisitionGuard = acquisitionGuard;
            Detail = detail;
        }

        internal int RequestedMethodToken { get; }
        internal MethodClassification? Classification { get; }
        internal StateMachineRelationshipResult Relationship { get; }
        internal object AcquisitionGuard { get; }
        internal string Detail { get; }
    }
}

internal static class ClassicAsyncRequestAdapter
{
    internal static ClassicAsyncRequestAdapterResult Adapt(
        MetadataReader reader,
        StateMachineRelationshipIndex relationships,
        MethodDefinitionHandle method,
        MethodClassification? classification,
        object acquisitionGuard)
    {
        StateMachineRelationshipResult relationship =
            relationships.GetByKickoff(method);
        MetadataMethodAddress requested;
        try
        {
            requested = MetadataMethodAddress.Create(reader, method);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or InvalidOperationException
                or OverflowException)
        {
            return new ClassicAsyncRequestAdapterResult.AcquisitionFailed(
                MetadataTokens.GetToken(method),
                classification,
                relationship,
                acquisitionGuard,
                "The imported MethodDef has no durable module identity.");
        }

        ClassicAsyncHostRole role;

        if (relationship is StateMachineRelationshipResult.Absent)
        {
            relationship = relationships.GetByImplementation(method);
            role = ImplementationRole(requested, relationship);
        }
        else
        {
            role = KickoffRole(requested, relationship);
        }

        var evidence = new ClassicAsyncOwnerEvidence(
            requested,
            role,
            classification,
            relationship,
            acquisitionGuard);

        return Adapt(evidence);
    }

    internal static ClassicAsyncRequestAdapterResult Adapt(
        ClassicAsyncOwnerEvidence evidence)
    {
        if (evidence.Relationship is StateMachineRelationshipResult.Rejected
            {
                Failure.Kind:
                    StateMachineRelationshipFailureKind.BudgetExceeded,
            })
        {
            return new ClassicAsyncRequestAdapterResult.OwnerUnavailable(
                evidence);
        }

        if (evidence.Classification
            != MethodClassification.StateMachineAsync)
        {
            return new ClassicAsyncRequestAdapterResult.Filtered(evidence);
        }

        if (evidence.HostRole != ClassicAsyncHostRole.DeclaredKickoff)
        {
            return evidence.Relationship is
                StateMachineRelationshipResult.Resolved
                ? new ClassicAsyncRequestAdapterResult.Filtered(evidence)
                : new ClassicAsyncRequestAdapterResult.OwnerUnavailable(
                    evidence);
        }

        if (evidence.Relationship is StateMachineRelationshipResult.Resolved
            {
                Relationship:
                {
                    Kind: StateMachineClaimKind.ClassicAsync,
                } resolvedRelationship,
            }
            && resolvedRelationship.TryGetMethod(
                StateMachineMethodRole.MoveNext,
                out MetadataMethodAddress execution))
        {
            return new ClassicAsyncRequestAdapterResult.RequestAvailable(
                evidence,
                new ClassicAsyncRequestSeed(
                    evidence.RequestedMethod,
                    execution,
                    resolvedRelationship,
                    evidence.AcquisitionGuard));
        }

        return evidence.Relationship is StateMachineRelationshipResult.Resolved
            ? new ClassicAsyncRequestAdapterResult.Filtered(evidence)
            : new ClassicAsyncRequestAdapterResult.OwnerUnavailable(evidence);
    }

    static ClassicAsyncHostRole KickoffRole(
        MetadataMethodAddress requested,
        StateMachineRelationshipResult relationship) =>
        relationship switch
        {
            StateMachineRelationshipResult.Resolved =>
                ClassicAsyncHostRole.DeclaredKickoff,
            StateMachineRelationshipResult.Rejected rejected
                when rejected.Failure.KickoffCandidates.Contains(requested) =>
                ClassicAsyncHostRole.DeclaredKickoff,
            _ => ClassicAsyncHostRole.Ordinary,
        };

    static ClassicAsyncHostRole ImplementationRole(
        MetadataMethodAddress requested,
        StateMachineRelationshipResult relationship)
    {
        if (relationship is not StateMachineRelationshipResult.Resolved
            resolved)
        {
            return ClassicAsyncHostRole.Ordinary;
        }

        foreach (StateMachineRoleDisposition disposition
            in resolved.Relationship.Roles)
        {
            if (disposition is not StateMachineRoleDisposition.Present present
                || present.Method != requested)
            {
                continue;
            }

            return present.Role == StateMachineMethodRole.MoveNext
                ? ClassicAsyncHostRole.Execution
                : ClassicAsyncHostRole.Support;
        }

        return ClassicAsyncHostRole.Ordinary;
    }
}
