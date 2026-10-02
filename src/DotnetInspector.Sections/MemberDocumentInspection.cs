using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;

namespace DotnetInspector.Sections;

public sealed record MemberDocumentSelector
{
    public MemberDocumentSelector(
        int? baselineOrdinal = null,
        string? fingerprintPrefix = null,
        int? metadataToken = null)
    {
        bool hasOrdinal = baselineOrdinal.HasValue;
        bool hasFingerprint =
            !string.IsNullOrWhiteSpace(fingerprintPrefix);
        if ((hasOrdinal ? 1 : 0)
            + (hasFingerprint ? 1 : 0)
            + (metadataToken.HasValue ? 1 : 0) != 1)
        {
            throw new ArgumentException(
                "An exact Member selector requires either one baseline "
                    + "ordinal, fingerprint prefix, or Metadata token.");
        }
        if (baselineOrdinal is { } ordinal)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ordinal);
        if (metadataToken is { } token)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(token);
        if (hasFingerprint
            && !fingerprintPrefix!.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "An exact Member fingerprint prefix must be hexadecimal.",
                nameof(fingerprintPrefix));
        }

        BaselineOrdinal = baselineOrdinal;
        MetadataToken = metadataToken;
        FingerprintPrefix = hasFingerprint
            ? fingerprintPrefix!.ToLowerInvariant()
            : null;
    }

    public int? BaselineOrdinal { get; }
    public int? MetadataToken { get; }
    public string? FingerprintPrefix { get; }
}

public sealed record MemberSubject(
    MemberGroupSubject Group,
    MemberOverloadPopulationBinding Population,
    int MetadataToken,
    MemberAnchor Anchor,
    int BaselineOrdinal,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Fingerprint,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString DocumentationId);

public sealed record MemberDocument(
    MemberSubject Subject,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString DisplaySignature,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString CanonicalSignature,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Accessibility,
    MemberReceiver Receiver,
    MemberDocumentationAttachment? Documentation = null,
    MemberSourceAttachment? Source = null);

public sealed record MemberDocumentInspectionPlan
{
    public MemberDocumentInspectionPlan(
        MemberGroupSubject group,
        MemberDocumentSelector selector,
        ApiSurfaceExtractionBounds bounds,
        MemberOverloadAccessibilityFilter accessibility =
            MemberOverloadAccessibilityFilter.Public,
        MemberOverloadReceiverFilter receiver =
            MemberOverloadReceiverFilter.All,
        bool includeHidden = false,
        MemberDocumentationAttachmentRequest? documentation = null,
        MemberSourceAttachmentRequest? source = null)
    {
        Group = group ?? throw new ArgumentNullException(nameof(group));
        Selector =
            selector ?? throw new ArgumentNullException(nameof(selector));
        Bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
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

        Accessibility = accessibility;
        Receiver = receiver;
        IncludeHidden = includeHidden;
        Documentation = documentation;
        Source = source;
    }

    public MemberGroupSubject Group { get; }
    public MemberDocumentSelector Selector { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
    public MemberOverloadAccessibilityFilter Accessibility { get; }
    public MemberOverloadReceiverFilter Receiver { get; }
    public bool IncludeHidden { get; }
    public MemberDocumentationAttachmentRequest? Documentation { get; }
    public MemberSourceAttachmentRequest? Source { get; }
}

public sealed record MemberDocumentInspectionRequest(
    LibraryReference Library,
    MemberDocumentInspectionPlan Plan);

public enum MemberDocumentInspectionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
    TypeNotFound,
    TypeAmbiguous,
    MemberGroupNotFound,
    BaselineOrdinalOutOfRange,
    FingerprintNotFound,
    FingerprintAmbiguous,
    RowsRejected,
    MetadataTokenNotFound,
}

public enum MemberDocumentInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
    DocumentationResultSetMismatch,
    DocumentationSubjectMismatch,
    SourceSubjectMismatch,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(
    typeof(MemberDocumentInspectionOutcome.Available),
    typeDiscriminator: "available")]
[JsonDerivedType(
    typeof(MemberDocumentInspectionOutcome.Rejected),
    typeDiscriminator: "rejected")]
[JsonDerivedType(
    typeof(MemberDocumentInspectionOutcome.Incomplete),
    typeDiscriminator: "incomplete")]
[JsonDerivedType(
    typeof(MemberDocumentInspectionOutcome.Failed),
    typeDiscriminator: "failed")]
public abstract record MemberDocumentInspectionOutcome
{
    private MemberDocumentInspectionOutcome()
    {
    }

    public sealed record Available(MemberDocument Document)
        : MemberDocumentInspectionOutcome;

    public sealed record Rejected(MemberDocumentInspectionRejection Reason)
        : MemberDocumentInspectionOutcome;

    public sealed record Incomplete(
        MemberOverloadPopulationBound Bound,
        long Limit,
        long Measured)
        : MemberDocumentInspectionOutcome;

    public sealed record Failed(
        MemberDocumentInspectionFailure Reason)
        : MemberDocumentInspectionOutcome;
}

public static class MemberDocumentInspectionOperation
{
    public static async ValueTask<
        InspectionEnvelope<MemberDocumentInspectionOutcome>>
        ExecuteAsync(
            MemberDocumentInspectionRequest request,
            LibraryOperationLease lease,
            MemberDocumentationAttachmentProvider documentationProvider,
            CancellationToken cancellationToken = default)
        => await ExecuteAsync(
                request,
                lease,
                documentationProvider,
                sourceProvider: null,
                cancellationToken)
            .ConfigureAwait(false);

    public static async ValueTask<
        InspectionEnvelope<MemberDocumentInspectionOutcome>>
        ExecuteAsync(
            MemberDocumentInspectionRequest request,
            LibraryOperationLease lease,
            MemberDocumentationAttachmentProvider?
                documentationProvider,
            MemberSourceAttachmentProvider? sourceProvider,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Plan.Documentation is not null)
            ArgumentNullException.ThrowIfNull(documentationProvider);
        if (request.Plan.Source is not null)
            ArgumentNullException.ThrowIfNull(sourceProvider);

        InspectionEnvelope<MemberDocumentInspectionOutcome> inspection =
            Execute(
                request,
                lease,
                cancellationToken);
        if (inspection.Content
            is not MemberDocumentInspectionOutcome.Available available)
        {
            return inspection;
        }

        MemberDocument document = available.Document;
        if (request.Plan.Documentation is { } documentation)
        {
            MemberDocumentationAttachmentResult attachment =
                await MemberDocumentationAttachmentOperation.ExecuteAsync(
                        [
                            document.Subject,
                        ],
                        documentation,
                        documentationProvider!,
                        cancellationToken)
                    .ConfigureAwait(false);
            switch (attachment)
            {
                case MemberDocumentationAttachmentResult.Attached attached:
                    document = document with
                    {
                        Documentation =
                            AssertSingle(attached.Attachments),
                    };
                    break;
                case MemberDocumentationAttachmentResult.Failed failed:
                    return new(
                        new MemberDocumentInspectionOutcome.Failed(
                            Map(failed.Reason)),
                        inspection.Share,
                        inspection.Diagnostics);
                default:
                    throw new InvalidOperationException(
                        "Unknown Member documentation attachment result.");
            }
        }

        if (request.Plan.Source is { } source)
        {
            MemberSourceAttachmentResult attachment =
                await MemberSourceAttachmentOperation.ExecuteAsync(
                        document.Subject,
                        source,
                        sourceProvider!,
                        cancellationToken)
                    .ConfigureAwait(false);
            switch (attachment)
            {
                case MemberSourceAttachmentResult.Attached attached:
                    document = document with
                    {
                        Source = attached.Attachment,
                    };
                    break;
                case MemberSourceAttachmentResult.Failed failed:
                    return new(
                        new MemberDocumentInspectionOutcome.Failed(
                            Map(failed.Reason)),
                        inspection.Share,
                        inspection.Diagnostics);
                default:
                    throw new InvalidOperationException(
                        "Unknown Member source attachment result.");
            }
        }

        return new(
            new MemberDocumentInspectionOutcome.Available(document),
            inspection.Share,
            inspection.Diagnostics);
    }

    public static InspectionEnvelope<MemberDocumentInspectionOutcome> Execute(
        MemberDocumentInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Library);
        ArgumentNullException.ThrowIfNull(request.Plan);
        ArgumentNullException.ThrowIfNull(lease);

        MemberDocumentInspectionPlan plan = request.Plan;
        int maximumRows =
            Math.Min(
                plan.Selector.BaselineOrdinal
                    ?? plan.Bounds.MaxMembers,
                plan.Bounds.MaxMembers);
        InspectionEnvelope<MemberOverloadPopulationInspectionOutcome>
            population =
                MemberOverloadPopulationInspectionOperation.Execute(
                    new(
                        request.Library,
                        new(
                            plan.Group,
                            new(
                                count: null,
                                new MemberOverloadRowsRequest(
                                    maximumRows),
                                plan.Accessibility,
                                plan.Receiver,
                                plan.IncludeHidden),
                            plan.Bounds)),
                    lease,
                    cancellationToken);
        return new(
            Project(population.Content, plan),
            population.Share,
            population.Diagnostics);
    }

    private static MemberDocumentInspectionOutcome Project(
        MemberOverloadPopulationInspectionOutcome outcome,
        MemberDocumentInspectionPlan plan) =>
        outcome switch
        {
            MemberOverloadPopulationInspectionOutcome.Available available =>
                ProjectAvailable(available.Content, plan),
            MemberOverloadPopulationInspectionOutcome.Rejected rejected =>
                new MemberDocumentInspectionOutcome.Rejected(
                    rejected.Reason switch
                    {
                        MemberOverloadPopulationInspectionRejection
                                .LeaseReferenceMismatch =>
                            MemberDocumentInspectionRejection
                                .LeaseReferenceMismatch,
                        MemberOverloadPopulationInspectionRejection
                                .AssemblyIdentityMismatch =>
                            MemberDocumentInspectionRejection
                                .AssemblyIdentityMismatch,
                        MemberOverloadPopulationInspectionRejection
                                .TypeNotFound =>
                            MemberDocumentInspectionRejection.TypeNotFound,
                        MemberOverloadPopulationInspectionRejection
                                .TypeAmbiguous =>
                            MemberDocumentInspectionRejection.TypeAmbiguous,
                        MemberOverloadPopulationInspectionRejection
                                .MemberGroupNotFound =>
                            MemberDocumentInspectionRejection
                                .MemberGroupNotFound,
                        _ => throw new InvalidOperationException(
                            "Unknown exact-Member population rejection."),
                    }),
            MemberOverloadPopulationInspectionOutcome.Incomplete incomplete =>
                new MemberDocumentInspectionOutcome.Incomplete(
                    incomplete.Bound,
                    incomplete.Limit,
                    incomplete.Measured),
            MemberOverloadPopulationInspectionOutcome.Failed failed =>
                new MemberDocumentInspectionOutcome.Failed(
                    Map(failed.Reason)),
            _ => throw new InvalidOperationException(
                "Unknown exact-Member population outcome."),
        };

    private static MemberDocumentInspectionOutcome ProjectAvailable(
        MemberOverloadPopulationContent content,
        MemberDocumentInspectionPlan plan)
    {
        if (content.Overloads.Rows
                is MemberOverloadRowsOutcome.Incomplete incomplete)
        {
            return new MemberDocumentInspectionOutcome.Incomplete(
                incomplete.Bound,
                incomplete.Limit,
                incomplete.Measured);
        }
        if (content.Overloads.Rows
                is MemberOverloadRowsOutcome.Failed failed)
        {
            return new MemberDocumentInspectionOutcome.Failed(
                failed.Reason switch
                {
                    MemberOverloadRowsFailure.MalformedMetadata =>
                        MemberDocumentInspectionFailure.MalformedMetadata,
                    _ => throw new InvalidOperationException(
                        "Unknown exact-Member Rows failure."),
                });
        }
        if (content.Overloads.Rows is not MemberOverloadRowsOutcome.Read rows)
        {
            return new MemberDocumentInspectionOutcome.Rejected(
                MemberDocumentInspectionRejection.RowsRejected);
        }
        if (!rows.IsComplete
            && plan.Selector.BaselineOrdinal is null)
        {
            return new MemberDocumentInspectionOutcome.Incomplete(
                MemberOverloadPopulationBound.Members,
                plan.Bounds.MaxMembers,
                checked(rows.Items.Length + 1L));
        }

        MemberDocumentSelector selector = plan.Selector;
        ImmutableArray<MemberOverloadShape> matches =
            selector.BaselineOrdinal is { } ordinal
                ? [
                    .. rows.Items.Where(
                        row => row.BaselineOrdinal == ordinal),
                ]
                : selector.MetadataToken is { } token
                    ? [
                        .. rows.Items.Where(
                            row => row.MetadataToken == token),
                    ]
                : [
                    .. rows.Items.Where(
                        row => row.Fingerprint.ToString().StartsWith(
                            selector.FingerprintPrefix!,
                            StringComparison.OrdinalIgnoreCase)),
                ];
        if (matches.Length == 0)
        {
            return new MemberDocumentInspectionOutcome.Rejected(
                selector.BaselineOrdinal.HasValue
                    ? MemberDocumentInspectionRejection
                        .BaselineOrdinalOutOfRange
                    : selector.MetadataToken.HasValue
                        ? MemberDocumentInspectionRejection
                            .MetadataTokenNotFound
                        : MemberDocumentInspectionRejection
                            .FingerprintNotFound);
        }
        if (matches.Length > 1)
        {
            return new MemberDocumentInspectionOutcome.Rejected(
                MemberDocumentInspectionRejection.FingerprintAmbiguous);
        }

        MemberOverloadShape selected = matches[0];
        return new MemberDocumentInspectionOutcome.Available(
            new(
                new(
                    content.Subject,
                    selected.Binding,
                    selected.MetadataToken,
                    selected.Anchor,
                    selected.BaselineOrdinal,
                    selected.Fingerprint,
                    selected.DocumentationId),
                selected.DisplaySignature,
                selected.CanonicalSignature,
                selected.Accessibility,
                selected.Receiver));
    }

    private static MemberDocumentationAttachment AssertSingle(
        ImmutableArray<MemberDocumentationAttachment> attachments) =>
        attachments.Length == 1
            ? attachments[0]
            : throw new InvalidOperationException(
                "One exact Member documentation request did not produce one attachment.");

    private static MemberDocumentInspectionFailure Map(
        MemberDocumentationAttachmentFailure failure) =>
        failure switch
        {
            MemberDocumentationAttachmentFailure.ResultSetMismatch =>
                MemberDocumentInspectionFailure
                    .DocumentationResultSetMismatch,
            MemberDocumentationAttachmentFailure.SubjectMismatch =>
                MemberDocumentInspectionFailure
                    .DocumentationSubjectMismatch,
            _ => throw new InvalidOperationException(
                "Unknown Member documentation attachment failure."),
        };

    private static MemberDocumentInspectionFailure Map(
        MemberSourceAttachmentFailure failure) =>
        failure switch
        {
            MemberSourceAttachmentFailure.SubjectMismatch =>
                MemberDocumentInspectionFailure.SourceSubjectMismatch,
            _ => throw new InvalidOperationException(
                "Unknown Member source attachment failure."),
        };

    private static MemberDocumentInspectionFailure Map(
        MemberOverloadPopulationInspectionFailure failure) =>
        failure switch
        {
            MemberOverloadPopulationInspectionFailure.NotManagedAssembly =>
                MemberDocumentInspectionFailure.NotManagedAssembly,
            MemberOverloadPopulationInspectionFailure.ManagedModule =>
                MemberDocumentInspectionFailure.ManagedModule,
            MemberOverloadPopulationInspectionFailure
                    .UnsupportedWindowsMetadata =>
                MemberDocumentInspectionFailure.UnsupportedWindowsMetadata,
            MemberOverloadPopulationInspectionFailure.MalformedMetadata =>
                MemberDocumentInspectionFailure.MalformedMetadata,
            MemberOverloadPopulationInspectionFailure.EmptyModuleVersionId =>
                MemberDocumentInspectionFailure.EmptyModuleVersionId,
            _ => throw new InvalidOperationException(
                "Unknown exact-Member population failure."),
        };
}
