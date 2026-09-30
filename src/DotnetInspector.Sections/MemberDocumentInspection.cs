using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public sealed record MemberDocumentSelector
{
    public MemberDocumentSelector(
        int? baselineOrdinal = null,
        string? fingerprintPrefix = null)
    {
        bool hasOrdinal = baselineOrdinal.HasValue;
        bool hasFingerprint =
            !string.IsNullOrWhiteSpace(fingerprintPrefix);
        if (hasOrdinal == hasFingerprint)
        {
            throw new ArgumentException(
                "An exact Member selector requires either one baseline "
                    + "ordinal or one fingerprint prefix.");
        }
        if (baselineOrdinal is { } ordinal)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ordinal);
        if (hasFingerprint
            && !fingerprintPrefix!.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "An exact Member fingerprint prefix must be hexadecimal.",
                nameof(fingerprintPrefix));
        }

        BaselineOrdinal = baselineOrdinal;
        FingerprintPrefix = hasFingerprint
            ? fingerprintPrefix!.ToLowerInvariant()
            : null;
    }

    public int? BaselineOrdinal { get; }
    public string? FingerprintPrefix { get; }
}

public sealed record MemberSubject(
    MemberGroupSubject Group,
    MemberOverloadPopulationBinding Population,
    int MetadataToken,
    int BaselineOrdinal,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Fingerprint);

public sealed record MemberDocument(
    MemberSubject Subject,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString DisplaySignature,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString CanonicalSignature,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Accessibility,
    MemberReceiver Receiver);

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
        bool includeHidden = false)
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
    }

    public MemberGroupSubject Group { get; }
    public MemberDocumentSelector Selector { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
    public MemberOverloadAccessibilityFilter Accessibility { get; }
    public MemberOverloadReceiverFilter Receiver { get; }
    public bool IncludeHidden { get; }
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
        MemberOverloadPopulationInspectionFailure Reason)
        : MemberDocumentInspectionOutcome;
}

public static class MemberDocumentInspectionOperation
{
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
                new MemberDocumentInspectionOutcome.Failed(failed.Reason),
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
                        MemberOverloadPopulationInspectionFailure
                            .MalformedMetadata,
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
                    selected.BaselineOrdinal,
                    selected.Fingerprint),
                selected.DisplaySignature,
                selected.CanonicalSignature,
                selected.Accessibility,
                selected.Receiver));
    }
}
