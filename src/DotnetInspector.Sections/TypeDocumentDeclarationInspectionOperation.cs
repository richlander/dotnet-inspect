using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public sealed record TypeDocumentInspectionPlan
{
    public TypeDocumentInspectionPlan(
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

public sealed record TypeDocumentInspectionRequest
{
    public TypeDocumentInspectionRequest(
        LibraryReference library,
        TypeDocumentInspectionPlan plan)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Plan = plan
            ?? throw new ArgumentNullException(nameof(plan));
    }

    public LibraryReference Library { get; }
    public TypeDocumentInspectionPlan Plan { get; }
}

public enum TypeDocumentInspectionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
    TypeNotFound,
    TypeAmbiguous,
}

public enum TypeDocumentInspectionBound
{
    MetadataRows,
    Members,
    RetainedTextCharacters,
}

public enum TypeDocumentInspectionFailure
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
    typeof(TypeDocumentInspectionOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(TypeDocumentInspectionOutcome.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(TypeDocumentInspectionOutcome.Incomplete),
    "incomplete")]
[JsonDerivedType(
    typeof(TypeDocumentInspectionOutcome.Failed),
    "failed")]
public abstract record TypeDocumentInspectionOutcome
{
    private protected TypeDocumentInspectionOutcome()
    {
    }

    public sealed record Available(TypeDocument Document)
        : TypeDocumentInspectionOutcome;

    public sealed record Rejected(
        TypeDocumentInspectionRejection Reason)
        : TypeDocumentInspectionOutcome;

    public sealed record Incomplete(
        TypeDocumentInspectionBound Bound,
        long Limit,
        long Measured)
        : TypeDocumentInspectionOutcome;

    public sealed record Failed(
        TypeDocumentInspectionFailure Reason)
        : TypeDocumentInspectionOutcome;
}

public static class TypeDocumentInspectionOperation
{
    private const string SharePath =
        "type-member-group-population-inspection/share";
    private const string ShareReason =
        "A complete portable Workspace scenario was not supplied.";

    public static InspectionEnvelope<
            TypeDocumentInspectionOutcome>
        Execute(
            TypeDocumentInspectionRequest request,
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
            TypeDocumentInspectionOutcome>
        Project(
            LibraryTypeDocumentInspectionOutcome outcome,
            TypeDocumentInspectionPlan plan) =>
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
                            TypeDocumentInspectionRejection
                                .LeaseReferenceMismatch,
                        LibraryTypeDocumentInspectionRejection
                                .AssemblyIdentityMismatch =>
                            TypeDocumentInspectionRejection
                                .AssemblyIdentityMismatch,
                        _ => throw new InvalidOperationException(
                            "Unknown Library Type document rejection."),
                    }),
            LibraryTypeDocumentInspectionOutcome.Incomplete incomplete =>
                Incomplete(
                    TypeDocumentInspectionBound.MetadataRows,
                    plan.Bounds.MaxMetadataRows,
                    incomplete.Measured),
            LibraryTypeDocumentInspectionOutcome.Failed failed =>
                Failed(
                    failed.Reason switch
                    {
                        LibraryTypeDocumentInspectionFailure
                                .NotManagedAssembly =>
                            TypeDocumentInspectionFailure
                                .NotManagedAssembly,
                        LibraryTypeDocumentInspectionFailure.ManagedModule =>
                            TypeDocumentInspectionFailure
                                .ManagedModule,
                        LibraryTypeDocumentInspectionFailure
                                .UnsupportedWindowsMetadata =>
                            TypeDocumentInspectionFailure
                                .UnsupportedWindowsMetadata,
                        LibraryTypeDocumentInspectionFailure
                                .MalformedMetadata =>
                            TypeDocumentInspectionFailure
                                .MalformedMetadata,
                        LibraryTypeDocumentInspectionFailure
                                .EmptyModuleVersionId =>
                            TypeDocumentInspectionFailure
                                .EmptyModuleVersionId,
                        _ => throw new InvalidOperationException(
                            "Unknown Library Type document failure."),
                    }),
            _ => throw new InvalidOperationException(
                "Unknown Library Type document outcome."),
        };

    private static InspectionEnvelope<
            TypeDocumentInspectionOutcome>
        Project(
            LibraryTypeDocumentCorrespondence correspondence,
            TypeDocumentInspectionPlan plan) =>
        correspondence.Document switch
        {
            MetadataTypeDocumentInspectionOutcome.Available available =>
                Project(
                    correspondence,
                    plan,
                    available.Document),
            MetadataTypeDocumentInspectionOutcome.TypeNotFound =>
                Rejected(
                    TypeDocumentInspectionRejection.TypeNotFound),
            MetadataTypeDocumentInspectionOutcome.TypeAmbiguous =>
                Rejected(
                    TypeDocumentInspectionRejection.TypeAmbiguous),
            MetadataTypeDocumentInspectionOutcome.Incomplete incomplete =>
                Incomplete(
                    TypeDocumentInspectionBound.MetadataRows,
                    incomplete.Limit,
                    incomplete.Measured),
            MetadataTypeDocumentInspectionOutcome.Failed =>
                Failed(
                    TypeDocumentInspectionFailure
                        .MalformedMetadata),
            _ => throw new InvalidOperationException(
                "Unknown Metadata Type document outcome."),
        };

    private static InspectionEnvelope<
            TypeDocumentInspectionOutcome>
        Project(
            LibraryTypeDocumentCorrespondence correspondence,
            TypeDocumentInspectionPlan plan,
            MetadataTypeDocument source)
    {
        LibraryAssemblyIdentity assembly =
            TypeMemberGroupPopulationInspectionOperation.PortableIdentity(
                correspondence.AssemblyIdentity);
        if (source.Subject.Type.ModuleVersionId == Guid.Empty)
        {
            return Failed(
                TypeDocumentInspectionFailure.EmptyModuleVersionId);
        }
        TypeSubject subject =
            TypeSubjectInspectionOperation.ProjectSubject(
                correspondence,
                plan.Type,
                source.MatchedName
                    ?? throw new InvalidOperationException(
                        "An available Type document requires its matched byte-native Type identity."),
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
                                TypeDocumentInspectionBound.Members,
                            MetadataTypeDeclarationPopulationBound
                                    .RetainedTextCharacters =>
                                TypeDocumentInspectionBound
                                    .RetainedTextCharacters,
                            _ => throw new InvalidOperationException(
                                "Unknown complete Type declaration bound."),
                        },
                        incomplete.Limit,
                        incomplete.Measured),
            MetadataTypeDeclarationPopulationOutcome.Failed =>
                Failed(
                    TypeDocumentInspectionFailure
                        .MalformedMetadata),
            null => Failed(
                TypeDocumentInspectionFailure
                    .PopulationResultMismatch),
            _ => throw new InvalidOperationException(
                "Unknown complete Type declaration outcome."),
        };
    }

    private static InspectionEnvelope<
            TypeDocumentInspectionOutcome>
        Available(
            TypeSubject subject,
            LibraryAssemblyIdentity assembly,
            int assemblyBytes,
            TypeDocumentInspectionPlan plan,
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
                TypeDocumentInspectionFailure
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
            new TypeDocumentInspectionOutcome.Available(
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
            TypeDocumentInspectionOutcome>
        Rejected(TypeDocumentInspectionRejection reason) =>
        Envelope(
            new TypeDocumentInspectionOutcome.Rejected(reason));

    private static InspectionEnvelope<
            TypeDocumentInspectionOutcome>
        Incomplete(
            TypeDocumentInspectionBound bound,
            long limit,
            long measured) =>
        Envelope(
            new TypeDocumentInspectionOutcome.Incomplete(
                bound,
                limit,
                measured));

    private static InspectionEnvelope<
            TypeDocumentInspectionOutcome>
        Failed(TypeDocumentInspectionFailure reason) =>
        Envelope(
            new TypeDocumentInspectionOutcome.Failed(reason));

    private static InspectionEnvelope<
            TypeDocumentInspectionOutcome>
        Envelope(TypeDocumentInspectionOutcome outcome) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason));

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value);
}
