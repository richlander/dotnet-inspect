using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public sealed record CompleteTypeDocumentInspectionPlan
{
    public CompleteTypeDocumentInspectionPlan(
        MetadataTypeDefinitionName type,
        ApiSurfaceExtractionBounds bounds,
        TypeMemberGroupSpelling spelling =
            TypeMemberGroupSpelling.CSharp,
        TypeMemberGroupAccessibilityFilter accessibility =
            TypeMemberGroupAccessibilityFilter.Public,
        TypeMemberGroupReceiverFilter receiver =
            TypeMemberGroupReceiverFilter.All,
        bool includeHidden = false)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
        if (!Enum.IsDefined(spelling))
            throw new ArgumentOutOfRangeException(nameof(spelling));
        if (!Enum.IsDefined(accessibility))
        {
            throw new ArgumentOutOfRangeException(
                nameof(accessibility));
        }
        if (!Enum.IsDefined(receiver))
            throw new ArgumentOutOfRangeException(nameof(receiver));

        Spelling = spelling;
        Accessibility = accessibility;
        Receiver = receiver;
        IncludeHidden = includeHidden;
        MetadataRequest = new(
            type,
            TypeMemberGroupPopulationInspectionOperation.Spelling(
                spelling),
            includeHidden,
            TypeMemberGroupPopulationInspectionOperation.Accessibility(
                accessibility),
            TypeMemberGroupPopulationInspectionOperation.Receiver(
                receiver));
    }

    public MetadataTypeDefinitionName Type { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
    public TypeMemberGroupSpelling Spelling { get; }
    public TypeMemberGroupAccessibilityFilter Accessibility { get; }
    public TypeMemberGroupReceiverFilter Receiver { get; }
    public bool IncludeHidden { get; }
    internal MetadataTypeDeclarationPopulationRequest MetadataRequest
    { get; }
}

public sealed record CompleteTypeDocumentInspectionRequest
{
    public CompleteTypeDocumentInspectionRequest(
        LibraryReference library,
        CompleteTypeDocumentInspectionPlan plan)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Plan = plan
            ?? throw new ArgumentNullException(nameof(plan));
    }

    public LibraryReference Library { get; }
    public CompleteTypeDocumentInspectionPlan Plan { get; }
}

public enum CompleteTypeDocumentInspectionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
    TypeNotFound,
    TypeAmbiguous,
}

public enum CompleteTypeDocumentInspectionBound
{
    MetadataRows,
    Members,
    RetainedTextCharacters,
}

public enum CompleteTypeDocumentInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
    PopulationResultMismatch,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(
    typeof(CompleteTypeDocumentInspectionOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(CompleteTypeDocumentInspectionOutcome.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(CompleteTypeDocumentInspectionOutcome.Incomplete),
    "incomplete")]
[JsonDerivedType(
    typeof(CompleteTypeDocumentInspectionOutcome.Failed),
    "failed")]
public abstract record CompleteTypeDocumentInspectionOutcome
{
    private protected CompleteTypeDocumentInspectionOutcome()
    {
    }

    public sealed record Available(TypeDocument Document)
        : CompleteTypeDocumentInspectionOutcome;

    public sealed record Rejected(
        CompleteTypeDocumentInspectionRejection Reason)
        : CompleteTypeDocumentInspectionOutcome;

    public sealed record Incomplete(
        CompleteTypeDocumentInspectionBound Bound,
        long Limit,
        long Measured)
        : CompleteTypeDocumentInspectionOutcome;

    public sealed record Failed(
        CompleteTypeDocumentInspectionFailure Reason)
        : CompleteTypeDocumentInspectionOutcome;
}

public static class CompleteTypeDocumentInspectionOperation
{
    private const string SharePath =
        "type-member-group-population-inspection/share";
    private const string ShareReason =
        "A complete portable Workspace scenario was not supplied.";

    public static InspectionEnvelope<
            CompleteTypeDocumentInspectionOutcome>
        Execute(
            CompleteTypeDocumentInspectionRequest request,
            LibraryOperationLease lease,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            LibraryTypeDocumentInspectionOutcome source =
                LibraryTypeDocumentInspection.Execute(
                    new(
                        request.Library,
                        new(
                            request.Plan.Type,
                            memberDeclarations:
                                request.Plan.MetadataRequest),
                        request.Plan.Bounds),
                    lease,
                    cancellationToken);
            return Project(source, request.Plan);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static InspectionEnvelope<
            CompleteTypeDocumentInspectionOutcome>
        Project(
            LibraryTypeDocumentInspectionOutcome outcome,
            CompleteTypeDocumentInspectionPlan plan) =>
        outcome switch
        {
            LibraryTypeDocumentInspectionOutcome.Completed completed =>
                Project(completed.Correspondence, plan),
            LibraryTypeDocumentInspectionOutcome.Rejected rejected =>
                Rejected(
                    rejected.Reason switch
                    {
                        LibraryTypeDocumentInspectionRejection
                                .LeaseReferenceMismatch =>
                            CompleteTypeDocumentInspectionRejection
                                .LeaseReferenceMismatch,
                        LibraryTypeDocumentInspectionRejection
                                .AssemblyIdentityMismatch =>
                            CompleteTypeDocumentInspectionRejection
                                .AssemblyIdentityMismatch,
                        _ => throw new InvalidOperationException(
                            "Unknown Library Type document rejection."),
                    }),
            LibraryTypeDocumentInspectionOutcome.Incomplete incomplete =>
                Incomplete(
                    CompleteTypeDocumentInspectionBound.MetadataRows,
                    plan.Bounds.MaxMetadataRows,
                    incomplete.Measured),
            LibraryTypeDocumentInspectionOutcome.Failed failed =>
                Failed(
                    failed.Reason switch
                    {
                        LibraryTypeDocumentInspectionFailure
                                .NotManagedAssembly =>
                            CompleteTypeDocumentInspectionFailure
                                .NotManagedAssembly,
                        LibraryTypeDocumentInspectionFailure.ManagedModule =>
                            CompleteTypeDocumentInspectionFailure
                                .ManagedModule,
                        LibraryTypeDocumentInspectionFailure
                                .UnsupportedWindowsMetadata =>
                            CompleteTypeDocumentInspectionFailure
                                .UnsupportedWindowsMetadata,
                        LibraryTypeDocumentInspectionFailure
                                .MalformedMetadata =>
                            CompleteTypeDocumentInspectionFailure
                                .MalformedMetadata,
                        LibraryTypeDocumentInspectionFailure
                                .EmptyModuleVersionId =>
                            CompleteTypeDocumentInspectionFailure
                                .EmptyModuleVersionId,
                        _ => throw new InvalidOperationException(
                            "Unknown Library Type document failure."),
                    }),
            _ => throw new InvalidOperationException(
                "Unknown Library Type document outcome."),
        };

    private static InspectionEnvelope<
            CompleteTypeDocumentInspectionOutcome>
        Project(
            LibraryTypeDocumentCorrespondence correspondence,
            CompleteTypeDocumentInspectionPlan plan) =>
        correspondence.Document switch
        {
            MetadataTypeDocumentInspectionOutcome.Available available =>
                Project(
                    correspondence,
                    plan,
                    available.Document),
            MetadataTypeDocumentInspectionOutcome.TypeNotFound =>
                Rejected(
                    CompleteTypeDocumentInspectionRejection.TypeNotFound),
            MetadataTypeDocumentInspectionOutcome.TypeAmbiguous =>
                Rejected(
                    CompleteTypeDocumentInspectionRejection.TypeAmbiguous),
            MetadataTypeDocumentInspectionOutcome.Incomplete incomplete =>
                Incomplete(
                    CompleteTypeDocumentInspectionBound.MetadataRows,
                    incomplete.Limit,
                    incomplete.Measured),
            MetadataTypeDocumentInspectionOutcome.Failed =>
                Failed(
                    CompleteTypeDocumentInspectionFailure
                        .MalformedMetadata),
            _ => throw new InvalidOperationException(
                "Unknown Metadata Type document outcome."),
        };

    private static InspectionEnvelope<
            CompleteTypeDocumentInspectionOutcome>
        Project(
            LibraryTypeDocumentCorrespondence correspondence,
            CompleteTypeDocumentInspectionPlan plan,
            MetadataTypeDocument source)
    {
        LibraryAssemblyIdentity assembly =
            TypeMemberGroupPopulationInspectionOperation.PortableIdentity(
                correspondence.AssemblyIdentity);
        if (source.Subject.Type.ModuleVersionId == Guid.Empty)
        {
            return Failed(
                CompleteTypeDocumentInspectionFailure.EmptyModuleVersionId);
        }
        TypeSubject subject =
            TypeDocumentInspectionOperation.ProjectSubject(
                correspondence,
                plan.Type,
                source.Subject,
                assembly);
        return source.MemberDeclarations switch
        {
            MetadataTypeDeclarationPopulationOutcome.Available available =>
                Available(
                    subject,
                    assembly,
                    correspondence.AssemblyBytes,
                    plan,
                    available.Population),
            MetadataTypeDeclarationPopulationOutcome.Incomplete
                incomplete =>
                    Incomplete(
                        incomplete.Bound switch
                        {
                            MetadataTypeDeclarationPopulationBound
                                    .Members =>
                                CompleteTypeDocumentInspectionBound.Members,
                            MetadataTypeDeclarationPopulationBound
                                    .RetainedTextCharacters =>
                                CompleteTypeDocumentInspectionBound
                                    .RetainedTextCharacters,
                            _ => throw new InvalidOperationException(
                                "Unknown complete Type declaration bound."),
                        },
                        incomplete.Limit,
                        incomplete.Measured),
            MetadataTypeDeclarationPopulationOutcome.Failed =>
                Failed(
                    CompleteTypeDocumentInspectionFailure
                        .MalformedMetadata),
            null => Failed(
                CompleteTypeDocumentInspectionFailure
                    .PopulationResultMismatch),
            _ => throw new InvalidOperationException(
                "Unknown complete Type declaration outcome."),
        };
    }

    private static InspectionEnvelope<
            CompleteTypeDocumentInspectionOutcome>
        Available(
            TypeSubject subject,
            LibraryAssemblyIdentity assembly,
            int assemblyBytes,
            CompleteTypeDocumentInspectionPlan plan,
            MetadataTypeDeclarationPopulation population)
    {
        MetadataTypeMemberGroupPopulationBinding sourceBinding =
            population.Binding;
        if (sourceBinding.ModuleVersionId
                != subject.ModuleVersionId
            || sourceBinding.Type != subject.Type
            || sourceBinding.TypeDefinitionToken
                != subject.TypeDefinitionToken
            || sourceBinding.Spelling
                != TypeMemberGroupPopulationInspectionOperation.Spelling(
                    plan.Spelling)
            || sourceBinding.IncludeHidden != plan.IncludeHidden
            || sourceBinding.Accessibility
                != TypeMemberGroupPopulationInspectionOperation
                    .Accessibility(plan.Accessibility)
            || sourceBinding.Receiver
                != TypeMemberGroupPopulationInspectionOperation.Receiver(
                    plan.Receiver))
        {
            return Failed(
                CompleteTypeDocumentInspectionFailure
                    .PopulationResultMismatch);
        }

        var memberGroups = new TypeMemberGroupPopulationBinding(
            assembly,
            sourceBinding.ModuleVersionId,
            sourceBinding.Type,
            sourceBinding.TypeDefinitionToken,
            plan.Spelling,
            plan.IncludeHidden,
            plan.Accessibility,
            plan.Receiver,
            TypeMemberGroupOrdering.Metadata);
        ImmutableArray<MemberDeclaration> members =
            [
                .. population.Members.Select(member =>
                    ProjectMember(
                        member,
                        memberGroups)),
            ];
        return Envelope(
            new CompleteTypeDocumentInspectionOutcome.Available(
                new(
                    subject,
                    memberGroups,
                    members,
                    assemblyBytes)));
    }

    private static MemberDeclaration ProjectMember(
        MetadataTypeMemberDeclaration member,
        TypeMemberGroupPopulationBinding typePopulation)
    {
        MemberGroupCategory category =
            TypeMemberGroupPopulationInspectionOperation.Category(
                member.Category);
        var group = new MemberGroupSubject(
            typePopulation.Type,
            member.Name,
            category,
            MemberGroupRole.Declared,
            typePopulation.Spelling);
        var population = new MemberOverloadPopulationBinding(
            typePopulation.Assembly,
            typePopulation.ModuleVersionId,
            typePopulation.Type,
            typePopulation.TypeDefinitionToken,
            member.Name,
            category,
            MemberGroupRole.Declared,
            MemberOverloadOrdering.Metadata,
            Accessibility(typePopulation.Accessibility),
            Receiver(typePopulation.Receiver),
            typePopulation.IncludeHidden,
            typePopulation.Spelling);
        var subject = new MemberSubject(
            group,
            population,
            member.MetadataToken,
            member.Anchor,
            member.BaselineOrdinal,
            Field(member.Fingerprint),
            Field(member.DocumentationId));
        return new(
            subject,
            Field(member.DisplaySignature),
            Field(member.CanonicalSignature),
            Field(member.Accessibility),
            member.Receiver switch
            {
                MetadataMethodReceiver.Static => MemberReceiver.Static,
                MetadataMethodReceiver.This => MemberReceiver.This,
                MetadataMethodReceiver.Extension =>
                    MemberReceiver.Extension,
                _ => throw new InvalidOperationException(
                    "Unknown complete Type Member receiver."),
            });
    }

    private static MemberOverloadAccessibilityFilter Accessibility(
        TypeMemberGroupAccessibilityFilter accessibility) =>
        accessibility switch
        {
            TypeMemberGroupAccessibilityFilter.Public =>
                MemberOverloadAccessibilityFilter.Public,
            TypeMemberGroupAccessibilityFilter.Protected =>
                MemberOverloadAccessibilityFilter.Protected,
            TypeMemberGroupAccessibilityFilter.Internal =>
                MemberOverloadAccessibilityFilter.Internal,
            TypeMemberGroupAccessibilityFilter.Private =>
                MemberOverloadAccessibilityFilter.Private,
            TypeMemberGroupAccessibilityFilter.All =>
                MemberOverloadAccessibilityFilter.All,
            _ => throw new InvalidOperationException(
                "Unknown Type Member-group accessibility."),
        };

    private static MemberOverloadReceiverFilter Receiver(
        TypeMemberGroupReceiverFilter receiver) =>
        receiver switch
        {
            TypeMemberGroupReceiverFilter.All =>
                MemberOverloadReceiverFilter.All,
            TypeMemberGroupReceiverFilter.This =>
                MemberOverloadReceiverFilter.This,
            TypeMemberGroupReceiverFilter.Static =>
                MemberOverloadReceiverFilter.Static,
            TypeMemberGroupReceiverFilter.Extension =>
                MemberOverloadReceiverFilter.Extension,
            TypeMemberGroupReceiverFilter.NonExtension =>
                MemberOverloadReceiverFilter.NonExtension,
            _ => throw new InvalidOperationException(
                "Unknown Type Member-group receiver."),
        };

    private static InspectionEnvelope<
            CompleteTypeDocumentInspectionOutcome>
        Rejected(CompleteTypeDocumentInspectionRejection reason) =>
        Envelope(
            new CompleteTypeDocumentInspectionOutcome.Rejected(reason));

    private static InspectionEnvelope<
            CompleteTypeDocumentInspectionOutcome>
        Incomplete(
            CompleteTypeDocumentInspectionBound bound,
            long limit,
            long measured) =>
        Envelope(
            new CompleteTypeDocumentInspectionOutcome.Incomplete(
                bound,
                limit,
                measured));

    private static InspectionEnvelope<
            CompleteTypeDocumentInspectionOutcome>
        Failed(CompleteTypeDocumentInspectionFailure reason) =>
        Envelope(
            new CompleteTypeDocumentInspectionOutcome.Failed(reason));

    private static InspectionEnvelope<
            CompleteTypeDocumentInspectionOutcome>
        Envelope(CompleteTypeDocumentInspectionOutcome outcome) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason));

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value);
}
