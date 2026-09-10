using System.Text.Json.Serialization;

namespace DotnetInspect.Web.Interop.Source;

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
    BrowserSource? Value,
    BrowserTypeSourceFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    string? Reason)
{
    internal static BrowserTypeSourceResult From(
        BrowserManagedOperationResult<BrowserSource, string, string> result) =>
        result switch
        {
            BrowserManagedOperationResult<BrowserSource, string, string>.Succeeded succeeded =>
                new(1, BrowserTypeSourceResultKind.Succeeded, succeeded.Value, null, null, null, null),
            BrowserManagedOperationResult<BrowserSource, string, string>.Failed failed =>
                new(1, BrowserTypeSourceResultKind.Failed, null,
                    failed.FailureKind switch
                    {
                        BrowserManagedOperationFailureKind.Expected => BrowserTypeSourceFailureKind.Expected,
                        BrowserManagedOperationFailureKind.Unexpected => BrowserTypeSourceFailureKind.Unexpected,
                        _ => throw new ArgumentOutOfRangeException(nameof(result)),
                    },
                    failed.Error, failed.Diagnostic, null),
            BrowserManagedOperationResult<BrowserSource, string, string>.Canceled canceled =>
                new(1, BrowserTypeSourceResultKind.Canceled, null, null, null, null,
                    BrowserTypeSourceCancellation.FormatReason(canceled.Reason)),
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
