using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public sealed record MemberDocumentResolutionPlan
{
    public MemberDocumentResolutionPlan(
        MemberGroupSubject group,
        ApiSurfaceExtractionBounds bounds,
        MemberDocumentSelector? selector = null,
        MemberOverloadAccessibilityFilter accessibility =
            MemberOverloadAccessibilityFilter.Public,
        MemberOverloadReceiverFilter receiver =
            MemberOverloadReceiverFilter.All,
        bool includeHidden = false)
    {
        Group = group ?? throw new ArgumentNullException(nameof(group));
        Bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
        if (Group.Category is not MemberGroupCategory.Method)
        {
            throw new ArgumentException(
                "Member document resolution currently supports method groups only.",
                nameof(group));
        }
        if (Group.Spelling is not TypeMemberGroupSpelling.CSharp)
        {
            throw new ArgumentException(
                "Member document resolution currently supports C# spelling only.",
                nameof(group));
        }
        if (Bounds.MaxMembers == 0)
        {
            throw new ArgumentException(
                "Member document resolution requires capacity for at least one exact Member.",
                nameof(bounds));
        }
        if (!Enum.IsDefined(accessibility))
        {
            throw new ArgumentOutOfRangeException(
                nameof(accessibility),
                accessibility,
                "Unknown exact-Member accessibility filter.");
        }
        if (!Enum.IsDefined(receiver))
        {
            throw new ArgumentOutOfRangeException(
                nameof(receiver),
                receiver,
                "Unknown exact-Member receiver filter.");
        }

        Selector = selector;
        Accessibility = accessibility;
        Receiver = receiver;
        IncludeHidden = includeHidden;
    }

    public MemberGroupSubject Group { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
    public MemberDocumentSelector? Selector { get; }
    public MemberOverloadAccessibilityFilter Accessibility { get; }
    public MemberOverloadReceiverFilter Receiver { get; }
    public bool IncludeHidden { get; }
}

public sealed record MemberDocumentResolutionRequest(
    LibraryReference Library,
    MemberDocumentResolutionPlan Plan);

public enum MemberDocumentResolutionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
    TypeNotFound,
    TypeAmbiguous,
    MemberGroupNotFound,
    BaselineOrdinalOutOfRange,
    FingerprintNotFound,
    FingerprintAmbiguous,
    MetadataTokenNotFound,
    RowsRejected,
}

public enum MemberDocumentResolutionFailure
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
    typeof(MemberDocumentResolutionOutcome.Overview),
    typeDiscriminator: "overview")]
[JsonDerivedType(
    typeof(MemberDocumentResolutionOutcome.Exact),
    typeDiscriminator: "exact")]
[JsonDerivedType(
    typeof(MemberDocumentResolutionOutcome.Rejected),
    typeDiscriminator: "rejected")]
[JsonDerivedType(
    typeof(MemberDocumentResolutionOutcome.Incomplete),
    typeDiscriminator: "incomplete")]
[JsonDerivedType(
    typeof(MemberDocumentResolutionOutcome.Failed),
    typeDiscriminator: "failed")]
public abstract record MemberDocumentResolutionOutcome
{
    private MemberDocumentResolutionOutcome()
    {
    }

    public sealed record Overview(MemberOverviewDocument Document)
        : MemberDocumentResolutionOutcome;

    public sealed record Exact(MemberDocument Document)
        : MemberDocumentResolutionOutcome;

    public sealed record Rejected(MemberDocumentResolutionRejection Reason)
        : MemberDocumentResolutionOutcome;

    public sealed record Incomplete(
        MemberOverloadPopulationBound Bound,
        long Limit,
        long Measured)
        : MemberDocumentResolutionOutcome;

    public sealed record Failed(MemberDocumentResolutionFailure Reason)
        : MemberDocumentResolutionOutcome;
}

public static class MemberDocumentResolutionOperation
{
    public static InspectionEnvelope<MemberDocumentResolutionOutcome> Execute(
        MemberDocumentResolutionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Library);
        ArgumentNullException.ThrowIfNull(request.Plan);
        ArgumentNullException.ThrowIfNull(lease);

        return request.Plan.Selector is null
            ? ExecuteNameOnly(request, lease, cancellationToken)
            : ExecuteExact(request, lease, cancellationToken);
    }

    private static InspectionEnvelope<MemberDocumentResolutionOutcome>
        ExecuteNameOnly(
            MemberDocumentResolutionRequest request,
            LibraryOperationLease lease,
            CancellationToken cancellationToken)
    {
        MemberDocumentResolutionPlan plan = request.Plan;
        InspectionEnvelope<MemberOverloadPopulationInspectionOutcome>
            population =
                MemberOverloadPopulationInspectionOperation.Execute(
                    new(
                        request.Library,
                        new(
                            plan.Group,
                            new(
                                new MemberOverloadCountRequest(),
                                new(
                                    plan.Bounds.MaxMembers),
                                plan.Accessibility,
                                plan.Receiver,
                                plan.IncludeHidden),
                            plan.Bounds)),
                    lease,
                    cancellationToken);
        return new(
            ProjectNameOnly(population.Content, plan),
            population.Share,
            population.Diagnostics);
    }

    private static InspectionEnvelope<MemberDocumentResolutionOutcome>
        ExecuteExact(
            MemberDocumentResolutionRequest request,
            LibraryOperationLease lease,
            CancellationToken cancellationToken)
    {
        MemberDocumentResolutionPlan plan = request.Plan;
        InspectionEnvelope<MemberDocumentInspectionOutcome> inspection =
            MemberDocumentInspectionOperation.Execute(
                new(
                    request.Library,
                    new(
                        plan.Group,
                        plan.Selector!,
                        plan.Bounds,
                        plan.Accessibility,
                        plan.Receiver,
                        plan.IncludeHidden)),
                lease,
                cancellationToken);
        return new(
            ProjectExact(inspection.Content),
            inspection.Share,
            inspection.Diagnostics);
    }

    private static MemberDocumentResolutionOutcome ProjectNameOnly(
        MemberOverloadPopulationInspectionOutcome outcome,
        MemberDocumentResolutionPlan plan) =>
        outcome switch
        {
            MemberOverloadPopulationInspectionOutcome.Available available =>
                ProjectNameOnly(available.Content, plan),
            MemberOverloadPopulationInspectionOutcome.Rejected rejected =>
                new MemberDocumentResolutionOutcome.Rejected(
                    Map(rejected.Reason)),
            MemberOverloadPopulationInspectionOutcome.Incomplete incomplete =>
                new MemberDocumentResolutionOutcome.Incomplete(
                    incomplete.Bound,
                    incomplete.Limit,
                    incomplete.Measured),
            MemberOverloadPopulationInspectionOutcome.Failed failed =>
                new MemberDocumentResolutionOutcome.Failed(
                    Map(failed.Reason)),
            _ => throw new InvalidOperationException(
                "Unknown exact-Member population outcome."),
        };

    private static MemberDocumentResolutionOutcome ProjectNameOnly(
        MemberOverloadPopulationContent content,
        MemberDocumentResolutionPlan plan)
    {
        if (content.Overloads.Count
            is not MemberOverloadCountOutcome.Counted count)
        {
            return PopulationMismatch();
        }
        if (content.Overloads.Rows
            is MemberOverloadRowsOutcome.Incomplete incomplete)
        {
            return new MemberDocumentResolutionOutcome.Incomplete(
                incomplete.Bound,
                incomplete.Limit,
                incomplete.Measured);
        }
        if (content.Overloads.Rows
            is MemberOverloadRowsOutcome.Failed failed)
        {
            return new MemberDocumentResolutionOutcome.Failed(
                failed.Reason switch
                {
                    MemberOverloadRowsFailure.MalformedMetadata =>
                        MemberDocumentResolutionFailure.MalformedMetadata,
                    _ => throw new InvalidOperationException(
                        "Unknown exact-Member Rows failure."),
                });
        }
        if (content.Overloads.Rows
            is MemberOverloadRowsOutcome.Rejected)
        {
            return new MemberDocumentResolutionOutcome.Rejected(
                MemberDocumentResolutionRejection.RowsRejected);
        }
        if (content.Overloads.Rows
            is not MemberOverloadRowsOutcome.Read rows)
        {
            return PopulationMismatch();
        }
        if (count.Value == 0)
        {
            return new MemberDocumentResolutionOutcome.Rejected(
                MemberDocumentResolutionRejection.MemberGroupNotFound);
        }
        if (!rows.IsComplete)
        {
            return new MemberDocumentResolutionOutcome.Incomplete(
                MemberOverloadPopulationBound.Members,
                plan.Bounds.MaxMembers,
                count.Value);
        }
        if (rows.Items.Length != count.Value)
            return PopulationMismatch();

        ImmutableArray<MemberDeclaration> declarations =
            [
                .. rows.Items.Select(
                    row => Declaration(content.Subject, row)),
            ];
        if (declarations.Length == 1)
        {
            return new MemberDocumentResolutionOutcome.Exact(
                Exact(declarations[0]));
        }

        return new MemberDocumentResolutionOutcome.Overview(
            new(
                content.Subject,
                content.Overloads.Binding,
                declarations));
    }

    private static MemberDocumentResolutionOutcome ProjectExact(
        MemberDocumentInspectionOutcome outcome) =>
        outcome switch
        {
            MemberDocumentInspectionOutcome.Available available =>
                new MemberDocumentResolutionOutcome.Exact(
                    new(
                        available.Document.Subject,
                        available.Document.DisplaySignature,
                        available.Document.CanonicalSignature,
                        available.Document.Accessibility,
                        available.Document.Receiver)),
            MemberDocumentInspectionOutcome.Rejected rejected =>
                new MemberDocumentResolutionOutcome.Rejected(
                    Map(rejected.Reason)),
            MemberDocumentInspectionOutcome.Incomplete incomplete =>
                new MemberDocumentResolutionOutcome.Incomplete(
                    incomplete.Bound,
                    incomplete.Limit,
                    incomplete.Measured),
            MemberDocumentInspectionOutcome.Failed failed =>
                new MemberDocumentResolutionOutcome.Failed(
                    Map(failed.Reason)),
            _ => throw new InvalidOperationException(
                "Unknown exact-Member document outcome."),
        };

    private static MemberDeclaration Declaration(
        MemberGroupSubject group,
        MemberOverloadShape shape) =>
        new(
            new(
                group,
                shape.Binding,
                shape.MetadataToken,
                shape.Anchor,
                shape.BaselineOrdinal,
                shape.Fingerprint,
                shape.DocumentationId),
            shape.DisplaySignature,
            shape.CanonicalSignature,
            shape.Accessibility,
            shape.Receiver);

    private static MemberDocument Exact(MemberDeclaration declaration) =>
        new(
            declaration.Subject,
            declaration.DisplaySignature,
            declaration.CanonicalSignature,
            declaration.Accessibility,
            declaration.Receiver);

    private static MemberDocumentResolutionOutcome PopulationMismatch() =>
        new MemberDocumentResolutionOutcome.Failed(
            MemberDocumentResolutionFailure.PopulationResultMismatch);

    private static MemberDocumentResolutionRejection Map(
        MemberOverloadPopulationInspectionRejection rejection) =>
        rejection switch
        {
            MemberOverloadPopulationInspectionRejection
                    .LeaseReferenceMismatch =>
                MemberDocumentResolutionRejection.LeaseReferenceMismatch,
            MemberOverloadPopulationInspectionRejection
                    .AssemblyIdentityMismatch =>
                MemberDocumentResolutionRejection.AssemblyIdentityMismatch,
            MemberOverloadPopulationInspectionRejection.TypeNotFound =>
                MemberDocumentResolutionRejection.TypeNotFound,
            MemberOverloadPopulationInspectionRejection.TypeAmbiguous =>
                MemberDocumentResolutionRejection.TypeAmbiguous,
            MemberOverloadPopulationInspectionRejection
                    .MemberGroupNotFound =>
                MemberDocumentResolutionRejection.MemberGroupNotFound,
            _ => throw new InvalidOperationException(
                "Unknown exact-Member population rejection."),
        };

    private static MemberDocumentResolutionRejection Map(
        MemberDocumentInspectionRejection rejection) =>
        rejection switch
        {
            MemberDocumentInspectionRejection.LeaseReferenceMismatch =>
                MemberDocumentResolutionRejection.LeaseReferenceMismatch,
            MemberDocumentInspectionRejection.AssemblyIdentityMismatch =>
                MemberDocumentResolutionRejection.AssemblyIdentityMismatch,
            MemberDocumentInspectionRejection.TypeNotFound =>
                MemberDocumentResolutionRejection.TypeNotFound,
            MemberDocumentInspectionRejection.TypeAmbiguous =>
                MemberDocumentResolutionRejection.TypeAmbiguous,
            MemberDocumentInspectionRejection.MemberGroupNotFound =>
                MemberDocumentResolutionRejection.MemberGroupNotFound,
            MemberDocumentInspectionRejection
                    .BaselineOrdinalOutOfRange =>
                MemberDocumentResolutionRejection
                    .BaselineOrdinalOutOfRange,
            MemberDocumentInspectionRejection.FingerprintNotFound =>
                MemberDocumentResolutionRejection.FingerprintNotFound,
            MemberDocumentInspectionRejection.FingerprintAmbiguous =>
                MemberDocumentResolutionRejection.FingerprintAmbiguous,
            MemberDocumentInspectionRejection.MetadataTokenNotFound =>
                MemberDocumentResolutionRejection.MetadataTokenNotFound,
            MemberDocumentInspectionRejection.RowsRejected =>
                MemberDocumentResolutionRejection.RowsRejected,
            _ => throw new InvalidOperationException(
                "Unknown exact-Member document rejection."),
        };

    private static MemberDocumentResolutionFailure Map(
        MemberOverloadPopulationInspectionFailure failure) =>
        failure switch
        {
            MemberOverloadPopulationInspectionFailure.NotManagedAssembly =>
                MemberDocumentResolutionFailure.NotManagedAssembly,
            MemberOverloadPopulationInspectionFailure.ManagedModule =>
                MemberDocumentResolutionFailure.ManagedModule,
            MemberOverloadPopulationInspectionFailure
                    .UnsupportedWindowsMetadata =>
                MemberDocumentResolutionFailure
                    .UnsupportedWindowsMetadata,
            MemberOverloadPopulationInspectionFailure.MalformedMetadata =>
                MemberDocumentResolutionFailure.MalformedMetadata,
            MemberOverloadPopulationInspectionFailure.EmptyModuleVersionId =>
                MemberDocumentResolutionFailure.EmptyModuleVersionId,
            _ => throw new InvalidOperationException(
                "Unknown exact-Member population failure."),
        };

    private static MemberDocumentResolutionFailure Map(
        MemberDocumentInspectionFailure failure) =>
        failure switch
        {
            MemberDocumentInspectionFailure.NotManagedAssembly =>
                MemberDocumentResolutionFailure.NotManagedAssembly,
            MemberDocumentInspectionFailure.ManagedModule =>
                MemberDocumentResolutionFailure.ManagedModule,
            MemberDocumentInspectionFailure.UnsupportedWindowsMetadata =>
                MemberDocumentResolutionFailure.UnsupportedWindowsMetadata,
            MemberDocumentInspectionFailure.MalformedMetadata =>
                MemberDocumentResolutionFailure.MalformedMetadata,
            MemberDocumentInspectionFailure.EmptyModuleVersionId =>
                MemberDocumentResolutionFailure.EmptyModuleVersionId,
            _ => throw new InvalidOperationException(
                "Unknown exact-Member document failure."),
        };
}
