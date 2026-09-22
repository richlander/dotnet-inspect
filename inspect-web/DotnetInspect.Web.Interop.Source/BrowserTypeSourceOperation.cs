using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;

namespace DotnetInspect.Web.Interop.Source;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BrowserTypeCodeView.Source), "source")]
[JsonDerivedType(typeof(BrowserTypeCodeView.ApiDeclarations), "apiDeclarations")]
public abstract record BrowserTypeCodeView
{
    private BrowserTypeCodeView() { }

    public sealed record Source(
        BrowserSource Value,
        InspectionShare Share,
        ImmutableArray<InspectionDiagnostic> Diagnostics)
        : BrowserTypeCodeView;

    public sealed record ApiDeclarations(
        InspectionEnvelope<TypeApiDeclarationResult> Inspection) : BrowserTypeCodeView;
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserTypeSourceResultKind>))]
public enum BrowserTypeSourceResultKind
{
    Succeeded,
    Failed,
    Canceled,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserTypeSourceFailureKind>))]
public enum BrowserTypeSourceFailureKind
{
    Expected,
    Unexpected,
}

[JsonConverter(typeof(JsonStringEnumConverter<BrowserTypeSourceCancellationKind>))]
public enum BrowserTypeSourceCancellationKind
{
    Requested,
    AlreadyRequested,
    NotActive,
}

public sealed record BrowserTypeSourceResult(
    int Version,
    BrowserTypeSourceResultKind Kind,
    BrowserTypeCodeView? Value,
    BrowserTypeSourceFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    string? Reason)
{
    internal static BrowserTypeSourceResult From(
        BrowserManagedOperationResult<BrowserTypeCodeView, string, string> result) =>
        result switch
        {
            BrowserManagedOperationResult<BrowserTypeCodeView, string, string>.Succeeded succeeded =>
                new(1, BrowserTypeSourceResultKind.Succeeded, succeeded.Value, null, null, null, null),
            BrowserManagedOperationResult<BrowserTypeCodeView, string, string>.Failed failed =>
                new(1, BrowserTypeSourceResultKind.Failed, null,
                    failed.FailureKind switch
                    {
                        BrowserManagedOperationFailureKind.Expected => BrowserTypeSourceFailureKind.Expected,
                        BrowserManagedOperationFailureKind.Unexpected => BrowserTypeSourceFailureKind.Unexpected,
                        _ => throw new ArgumentOutOfRangeException(nameof(result)),
                    },
                    failed.Error, failed.Diagnostic, null),
            BrowserManagedOperationResult<BrowserTypeCodeView, string, string>.Canceled canceled =>
                new(1, BrowserTypeSourceResultKind.Canceled, null, null, null, null,
                    BrowserTypeSourceCancellation.FormatReason(canceled.Reason)),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
}

public sealed record BrowserTypeSourceEvidenceAttachment(
    BrowserTypeCodeView.Source Inspection,
    BrowserTypeSourcePdbAcquisitionEvidence Evidence);

public sealed record BrowserTypeSourcePdbAcquisitionEvidence(
    string Disposition,
    bool UsedForAuthoredSource,
    bool UsedForDecompilation,
    string? AuthoredContribution,
    string? DecompilationContribution,
    bool? PdbReadyBeforeDecompilation,
    bool DecompilationStarted,
    string? Selection,
    BrowserPortablePdbAcquisitionEvidence ExternalAcquisition)
{
    internal static BrowserTypeSourcePdbAcquisitionEvidence From(
        TypeSourcePdbAcquisitionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return new(
            evidence.Disposition.ToString(),
            evidence.UsedForAuthoredSource,
            evidence.UsedForDecompilation,
            evidence.AuthoredContribution?.ToString(),
            evidence.DecompilationContribution?.ToString(),
            evidence.PdbReadyBeforeDecompilation,
            evidence.DecompilationStarted,
            evidence.Selection?.ToString(),
            BrowserPortablePdbAcquisitionEvidence.From(
                evidence.ExternalAcquisition));
    }
}

public sealed record BrowserPortablePdbAcquisitionEvidence(
    string Outcome,
    string? SymbolServer,
    bool FromCache,
    bool WindowsPdbDetected,
    string? StoreFailure,
    BrowserPortablePdbNetworkAttemptEvidence[] NetworkAttempts)
{
    internal static BrowserPortablePdbAcquisitionEvidence From(
        PortablePdbAcquisitionEvidenceDocument evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return new(
            evidence.Outcome.ToString(),
            evidence.SymbolServer,
            evidence.FromCache,
            evidence.WindowsPdbDetected,
            evidence.StoreFailure?.ToString(),
            [
                .. evidence.NetworkAttempts.Select(
                    BrowserPortablePdbNetworkAttemptEvidence.From),
            ]);
    }
}

public sealed record BrowserPortablePdbNetworkAttemptEvidence(
    string Route,
    InertString Url,
    int RequestCount,
    string Outcome,
    int? StatusCode,
    long BodyBytesRead,
    double ElapsedMilliseconds)
{
    internal static BrowserPortablePdbNetworkAttemptEvidence From(
        PortablePdbNetworkAttemptEvidence evidence) =>
        new(
            evidence.Route.ToString(),
            evidence.Url,
            evidence.RequestCount,
            evidence.Outcome.ToString(),
            evidence.StatusCode is { } statusCode
                ? (int)statusCode
                : null,
            evidence.BodyBytesRead,
            evidence.Elapsed.TotalMilliseconds);
}

public sealed record BrowserTypeSourceEvidenceResult(
    int Version,
    BrowserTypeSourceResultKind Kind,
    BrowserTypeSourceEvidenceAttachment? Value,
    BrowserTypeSourceFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    string? Reason)
{
    internal static BrowserTypeSourceEvidenceResult From(
        BrowserManagedOperationResult<
            BrowserTypeSourceEvidenceAttachment,
            string,
            string> result) =>
        result switch
        {
            BrowserManagedOperationResult<
                BrowserTypeSourceEvidenceAttachment,
                string,
                string>.Succeeded succeeded =>
                new(
                    1,
                    BrowserTypeSourceResultKind.Succeeded,
                    succeeded.Value,
                    null,
                    null,
                    null,
                    null),
            BrowserManagedOperationResult<
                BrowserTypeSourceEvidenceAttachment,
                string,
                string>.Failed failed =>
                new(
                    1,
                    BrowserTypeSourceResultKind.Failed,
                    null,
                    failed.FailureKind switch
                    {
                        BrowserManagedOperationFailureKind.Expected =>
                            BrowserTypeSourceFailureKind.Expected,
                        BrowserManagedOperationFailureKind.Unexpected =>
                            BrowserTypeSourceFailureKind.Unexpected,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(result)),
                    },
                    failed.Error,
                    failed.Diagnostic,
                    null),
            BrowserManagedOperationResult<
                BrowserTypeSourceEvidenceAttachment,
                string,
                string>.Canceled canceled =>
                new(
                    1,
                    BrowserTypeSourceResultKind.Canceled,
                    null,
                    null,
                    null,
                    null,
                    BrowserTypeSourceCancellation.FormatReason(
                        canceled.Reason)),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
}

public sealed record BrowserTypeSourceCancellation(
    BrowserTypeSourceCancellationKind Kind,
    string? Reason)
{
    internal static BrowserTypeSourceCancellation From(
        BrowserManagedCancellationRequestResult result) =>
        result switch
        {
            BrowserManagedCancellationRequestResult.Requested requested =>
                new(BrowserTypeSourceCancellationKind.Requested, FormatReason(requested.Reason)),
            BrowserManagedCancellationRequestResult.AlreadyRequested requested =>
                new(BrowserTypeSourceCancellationKind.AlreadyRequested, FormatReason(requested.Reason)),
            BrowserManagedCancellationRequestResult.NotActive =>
                new(BrowserTypeSourceCancellationKind.NotActive, null),
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };

    internal static string FormatReason(BrowserManagedOperationCancelReason reason) =>
        BrowserManagedOperationCancelReasons.Format(reason);

    internal static BrowserManagedOperationCancelReason ParseReason(string reason) =>
        BrowserManagedOperationCancelReasons.Parse(reason);
}
