using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

internal static class PackageAuthorityFailureAdapter
{
    internal static PackageAuthorityFailure DescribeVersionFailure(
        PackageSource source,
        PackageSourceFailure failure)
    {
        InertString authority = PackageSourceDisplay.ForDiagnostics(source);
        PackageAuthorityFailureKind kind = Classify(failure.Kind);
        string message = kind switch
        {
            PackageAuthorityFailureKind.AuthenticationRequired =>
                $"Package source {authority} requires credentials or rejected the supplied credentials.",
            PackageAuthorityFailureKind.Timeout =>
                $"Package source {authority} timed out while enumerating versions.",
            PackageAuthorityFailureKind.Unsupported =>
                $"Package source {authority} does not support version enumeration.",
            PackageAuthorityFailureKind.IncompleteMetadata =>
                $"Package source {authority} did not provide complete version metadata.",
            PackageAuthorityFailureKind.InvalidResponse =>
                $"Package source {authority} returned invalid version metadata.",
            PackageAuthorityFailureKind.ResponseRejected =>
                $"Package source {authority} returned version metadata outside the configured safety limits.",
            PackageAuthorityFailureKind.Transport =>
                $"Package source {authority} could not be reached while enumerating versions.",
            _ => failure.Message,
        };
        return new PackageAuthorityFailure(authority, kind, message)
        {
            SourceFailure = failure,
            ResultSource = failure.Source,
        };
    }

    private static PackageAuthorityFailureKind Classify(
        PackageSourceFailureKind kind) => kind switch
    {
        PackageSourceFailureKind.AuthenticationRequired =>
            PackageAuthorityFailureKind.AuthenticationRequired,
        PackageSourceFailureKind.Timeout =>
            PackageAuthorityFailureKind.Timeout,
        PackageSourceFailureKind.Unsupported =>
            PackageAuthorityFailureKind.Unsupported,
        PackageSourceFailureKind.InvalidResponse =>
            PackageAuthorityFailureKind.InvalidResponse,
        PackageSourceFailureKind.ResponseRejected =>
            PackageAuthorityFailureKind.ResponseRejected,
        PackageSourceFailureKind.Transport =>
            PackageAuthorityFailureKind.Transport,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
