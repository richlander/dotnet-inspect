using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Sections;

/// <summary>
/// Host-neutral request for one exact Type and an optional Members terminal.
/// </summary>
public sealed record TypeInspectionRequest
{
    public TypeInspectionRequest(
        SelectedContextExactTypeInspectionRequest subject,
        QuerySpaceRequest? members = null)
    {
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        Members = members;
    }

    public SelectedContextExactTypeInspectionRequest Subject { get; }

    public QuerySpaceRequest? Members { get; }
}

/// <summary>Resource-free document for one exact Type subject.</summary>
public sealed record TypeInspectionDocument(
    SelectedContextExactTypeInspectionResult Subject,
    TypeMembersOutcome Members);

/// <summary>The closed outcome for one Type inspection request.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TypeInspectionContent.Completed), "completed")]
[JsonDerivedType(
    typeof(TypeInspectionContent.QueryRejected),
    "query-rejected")]
[JsonDerivedType(
    typeof(TypeInspectionContent.QueryIntentRejected),
    "query-intent-rejected")]
public abstract record TypeInspectionContent
{
    private protected TypeInspectionContent()
    {
    }

    public sealed record Completed(TypeInspectionDocument Document)
        : TypeInspectionContent;

    public sealed record QueryRejected(
        TypeMembersQueryRequestRejectionKind Reason)
        : TypeInspectionContent;

    public sealed record QueryIntentRejected(
        TypeMembersQueryIntentRejection Failure)
        : TypeInspectionContent;
}

/// <summary>
/// Produces one exact Type document and settles its optional Members
/// QuerySpace terminal while exact-Type authority is live.
/// </summary>
public static class TypeInspectionOperation
{
    public static InspectionEnvelope<TypeInspectionContent> Execute(
        WorkspaceRealizationOperationLease authority,
        WorkspaceDeclarationContext context,
        TypeInspectionRequest request,
        ApiSurfaceScope scope =
            ApiSurfaceScope.PublicWithNonPublicTypes)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        TypeMembersQueryPlan? membersPlan = ResolveMembersPlan(
            request,
            out InspectionEnvelope<TypeInspectionContent>? rejection);
        if (rejection is not null)
            return rejection;

        TypeMemberProjection projection =
            TypeMemberProjection.NotRequested;
        InspectionEnvelope<SelectedContextExactTypeInspectionResult>
            subjectEnvelope =
                membersPlan is null
                    ? SelectedContextExactTypeInspectionOperation.Execute(
                        authority,
                        context,
                        request.Subject,
                        scope)
                    : SelectedContextExactTypeInspectionOperation
                        .ExecuteWithLiveTarget(
                            authority,
                            context,
                            request.Subject,
                            target =>
                                projection = ProjectMembers(target),
                            scope);

        return Complete(
            subjectEnvelope,
            membersPlan,
            projection,
            scope);
    }

    public static InspectionEnvelope<TypeInspectionContent> Execute(
        InspectionWorkspace workspace,
        CompleteWorkspaceActivation activation,
        TypeInspectionRequest request,
        ApiSurfaceScope scope =
            ApiSurfaceScope.PublicWithNonPublicTypes)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        TypeMembersQueryPlan? membersPlan = ResolveMembersPlan(
            request,
            out InspectionEnvelope<TypeInspectionContent>? rejection);
        if (rejection is not null)
            return rejection;

        TypeMemberProjection projection =
            TypeMemberProjection.NotRequested;
        InspectionEnvelope<SelectedContextExactTypeInspectionResult>
            subjectEnvelope =
                membersPlan is null
                    ? SelectedContextExactTypeInspectionOperation.Execute(
                        workspace,
                        activation,
                        request.Subject,
                        scope: scope)
                    : SelectedContextExactTypeInspectionOperation
                        .ExecuteWithLiveTarget(
                            workspace,
                            activation,
                            request.Subject,
                            target =>
                                projection = ProjectMembers(target),
                            scope: scope);

        return Complete(
            subjectEnvelope,
            membersPlan,
            projection,
            scope);
    }

    private static TypeMembersQueryPlan? ResolveMembersPlan(
        TypeInspectionRequest request,
        out InspectionEnvelope<TypeInspectionContent>? rejection)
    {
        rejection = null;
        if (request.Members is null)
            return null;

        TypeMembersQueryRequestResult resolution =
            TypeInspectionMembersQuery.ResolveRequest(request.Members);
        switch (resolution)
        {
            case TypeMembersQueryRequestResult.Accepted accepted:
                return accepted.Plan;
            case TypeMembersQueryRequestResult.Rejected rejected:
                rejection = Rejected(rejected.Kind);
                return null;
            case TypeMembersQueryRequestResult.IntentRejected rejected:
                rejection = IntentRejected(rejected.Failure);
                return null;
            default:
                throw new InvalidOperationException(
                    "Unknown Type Members QuerySpace request result.");
        }
    }

    private static InspectionEnvelope<TypeInspectionContent> Complete(
        InspectionEnvelope<SelectedContextExactTypeInspectionResult>
            subjectEnvelope,
        TypeMembersQueryPlan? membersPlan,
        TypeMemberProjection projection,
        ApiSurfaceScope scope)
    {
        TypeMembersOutcome members =
            SettleMembers(
                subjectEnvelope.Content.Inspection,
                membersPlan,
                projection,
                scope);
        ImmutableArray<InspectionDiagnostic> diagnostics =
            members is TypeMembersOutcome.Failed failed
                ? subjectEnvelope.Diagnostics.Add(
                    FailureDiagnostic(failed.Reason))
                : members is TypeMembersOutcome.Rejected
                    ? subjectEnvelope.Diagnostics.Add(
                        new(
                            "type-inspection.members.selection-rejected",
                            InspectionDiagnosticSeverity.Error,
                            "The Type Members semantic selection could not "
                                + "be satisfied by the complete population."))
                    : subjectEnvelope.Diagnostics;
        InspectionShare share =
            membersPlan is null
                ? subjectEnvelope.Share
                : new InspectionShare.NonProjectable(
                    "type-inspection/members/share",
                    "The Type Members QuerySpace request cannot yet be "
                        + "represented by portable Share.");
        return new(
            new TypeInspectionContent.Completed(
                new(
                    subjectEnvelope.Content,
                    members)),
            share,
            diagnostics);
    }

    private static TypeMembersOutcome SettleMembers(
        ExactTypeInspectionResult subject,
        TypeMembersQueryPlan? plan,
        TypeMemberProjection projection,
        ApiSurfaceScope scope)
    {
        if (plan is null)
            return new TypeMembersOutcome.NotRequested();
        if (!subject.IsAvailable)
        {
            return new TypeMembersOutcome.Unavailable(
                TypeMembersUnavailableReason.SubjectUnavailable);
        }
        if (!subject.IsComplete)
        {
            return new TypeMembersOutcome.Incomplete(
                TypeMembersIncompleteReason.SubjectInspectionIncomplete);
        }
        if (projection.Failure is { } failure)
            return new TypeMembersOutcome.Failed(failure);
        if (projection.Members.IsDefault
            || projection.Type is null
            || subject.SupplierAssembly is null)
        {
            throw new InvalidOperationException(
                "An available exact Type requires a detached Member "
                    + "population projection.");
        }

        ImmutableArray<TypeMemberRow> rows =
        [
            .. projection.Members.Select(member =>
                member.ToRow(subject.SupplierAssembly)),
        ];
        RowSelectionResult<TypeMemberRow> selected =
            RowQueryExecutor.Apply(
                rows,
                plan.RowPlan);
        if (!selected.IsSuccess)
        {
            RowWindowFailure window =
                selected.Failure
                ?? throw new InvalidOperationException(
                    "A rejected Type Members selection requires one "
                        + "window failure.");
            return new TypeMembersOutcome.Rejected(
                new(
                    window.StageNumber,
                    window.RequiredPosition,
                    window.AvailableCount));
        }

        var binding = new TypeMemberPopulationBinding(
            subject.SupplierAssembly,
            projection.Type,
            scope);
        return plan.Request.Terminal switch
        {
            QuerySpaceTerminalRequirement.Rows =>
                new TypeMembersOutcome.Rows(
                    binding,
                    [.. selected.Values]),
            QuerySpaceTerminalRequirement.Count =>
                new TypeMembersOutcome.Count(
                    binding,
                    selected.Values.Count),
            _ => throw new InvalidOperationException(
                "Unknown Type Members terminal."),
        };
    }

    private static TypeMemberProjection ProjectMembers(
        SelectedContextExactTypeLiveTarget target)
    {
        if (target.Type.DefinitionName is not { } receiverDefinition)
        {
            return TypeMemberProjection.Failed(
                TypeMembersFailure.SubjectIdentityUnavailable);
        }

        ExactTypeDefinitionIdentity receiverType =
            ExactTypeDefinitionIdentity.From(receiverDefinition);
        var members = ImmutableArray.CreateBuilder<ProjectedTypeMember>(
            target.Type.Members.Count);
        foreach (ApiMember member in target.Type.Members)
        {
            if (MemberFilters.IsCompilerGenerated(member.Name))
                continue;

            ApiType declaringType = target.Type;
            ApiMember declaringMember = member;
            TypeMemberRowKind rowKind = TypeMemberRowKind.Declared;
            ExactTypeDefinitionIdentity? attachedReceiver = null;
            if (member.DeclaringTypeDefinitionName is { } declaringDefinition)
            {
                ApiType[] declaringTypes =
                [
                    .. target.Surface.Types.Where(
                        type => type.DefinitionName == declaringDefinition),
                ];
                if (declaringTypes.Length != 1)
                {
                    return TypeMemberProjection.Failed(
                        TypeMembersFailure.DeclaringTypeUnavailable);
                }
                declaringType = declaringTypes[0];
                if (member.MetadataToken is not { } methodToken)
                {
                    return TypeMemberProjection.Failed(
                        TypeMembersFailure.DeclaringMemberUnavailable);
                }

                ApiMember[] declaringMembers =
                [
                    .. declaringType.Members.Where(
                        candidate =>
                            candidate.MetadataToken == methodToken
                            && candidate.IsExtension),
                ];
                if (declaringMembers.Length != 1)
                {
                    return TypeMemberProjection.Failed(
                        TypeMembersFailure.DeclaringMemberUnavailable);
                }
                declaringMember = declaringMembers[0];
                rowKind = TypeMemberRowKind.AttachedExtension;
                attachedReceiver = receiverType;
            }

            MetadataTypeDefinitionName declarationDefinition =
                declaringType.DefinitionName
                ?? throw new InvalidOperationException(
                    "A declaring Type in a live API surface requires exact "
                        + "Metadata identity.");
            members.Add(
                new(
                    rowKind,
                    Receiver(member),
                    ExactTypeDefinitionIdentity.From(
                        declarationDefinition),
                    ApiMemberIdentity.GetMemberAnchor(
                        declaringType,
                        declaringMember),
                    member.Name,
                    declaringMember.Kind,
                    member.Signature,
                    attachedReceiver));
        }

        return TypeMemberProjection.Completed(
            receiverType,
            members.ToImmutable());
    }

    private static TypeMemberReceiver Receiver(ApiMember member) =>
        member.IsExtension
            ? TypeMemberReceiver.Extension
            : member.IsStatic
                ? TypeMemberReceiver.Static
                : TypeMemberReceiver.This;

    private static InspectionEnvelope<TypeInspectionContent> Rejected(
        TypeMembersQueryRequestRejectionKind reason) =>
        new(
            new TypeInspectionContent.QueryRejected(reason),
            new InspectionShare.NonProjectable(
                "type-inspection/members/query",
                "The Type Members QuerySpace request was rejected before "
                    + "subject acquisition."),
            [
                new(
                    "type-inspection.members.query-rejected",
                    InspectionDiagnosticSeverity.Error,
                    $"The Type Members QuerySpace request was rejected: "
                        + $"{reason}."),
            ]);

    private static InspectionEnvelope<TypeInspectionContent> IntentRejected(
        TypeMembersQueryIntentRejection failure) =>
        new(
            new TypeInspectionContent.QueryIntentRejected(failure),
            new InspectionShare.NonProjectable(
                "type-inspection/members/query-intent",
                "The Type Members row intent was rejected before subject "
                    + "acquisition."),
            [
                new(
                    "type-inspection.members.query-intent-rejected",
                    InspectionDiagnosticSeverity.Error,
                    $"The Type Members row intent was rejected: "
                        + $"{failure.Reason}."),
            ]);

    private static InspectionDiagnostic FailureDiagnostic(
        TypeMembersFailure failure) =>
        new(
            "type-inspection.members.failed",
            InspectionDiagnosticSeverity.Error,
            $"The Type Members population could not preserve exact "
                + $"declaration identity: {failure}.");

    private sealed record TypeMemberProjection(
        ExactTypeDefinitionIdentity? Type,
        ImmutableArray<ProjectedTypeMember> Members,
        TypeMembersFailure? Failure)
    {
        internal static TypeMemberProjection NotRequested { get; } =
            new(null, default, null);

        internal static TypeMemberProjection Completed(
            ExactTypeDefinitionIdentity type,
            ImmutableArray<ProjectedTypeMember> members) =>
            new(type, members, null);

        internal static TypeMemberProjection Failed(
            TypeMembersFailure failure) =>
            new(null, default, failure);
    }

    private sealed record ProjectedTypeMember(
        TypeMemberRowKind RowKind,
        TypeMemberReceiver Receiver,
        ExactTypeDefinitionIdentity DeclaringType,
        ILInspector.MetadataPrimitives.MemberAnchor Member,
        string Name,
        string DeclarationKind,
        string? Signature,
        ExactTypeDefinitionIdentity? AttachedReceiverType)
    {
        internal TypeMemberRow ToRow(
            ExactTypeAssemblyIdentity assembly) =>
            new(
                RowKind,
                Receiver,
                new(
                    new(
                        assembly,
                        DeclaringType),
                    Member),
                Name,
                DeclarationKind,
                Signature,
                AttachedReceiverType is null
                    ? null
                    : new(
                        assembly,
                        AttachedReceiverType));
    }
}
