using System.Collections.ObjectModel;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// One exact payload and its configured source identity, or attributed
/// failures.
/// </summary>
public sealed class ConfiguredPackagePayloadResult
{
    internal ConfiguredPackagePayloadResult(
        ConfiguredPackageAuthority? authority,
        PackageSourceResultIdentity? source,
        AcquiredPackageSourcePayload? payload,
        IReadOnlyList<PackageAuthorityFailure> failures,
        IReadOnlyList<ConfiguredPackageAuthority>? notFoundAuthorities = null,
        IReadOnlyList<ConfiguredPackageAuthority>? reportingAuthorities = null,
        bool selectionUsesOriginalSources = false)
    {
        if ((authority is null) != (source is null)
            || (authority is null) != (payload is null))
        {
            throw new ArgumentException(
                "An acquired configured payload requires its authority and exact source identity.");
        }
        if (authority is not null
            && !ReferenceEquals(
                source!.Association,
                authority.Association))
        {
            throw new ArgumentException(
                "The configured payload source must belong to its authority.",
                nameof(source));
        }
        if (payload is not null
            && !payload.ProducerKey.Equals(
                source!.Producer.Key,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The configured payload and source identify different producers.",
                nameof(payload));
        }

        Authority = authority;
        Source = source;
        Payload = payload;
        Failures = new ReadOnlyCollection<PackageAuthorityFailure>(
            [.. failures]);
        NotFoundAuthorities =
            new ReadOnlyCollection<ConfiguredPackageAuthority>(
                [.. notFoundAuthorities ?? []]);
        ReportingAuthorities = reportingAuthorities is null
            ? null
            : new ReadOnlyCollection<ConfiguredPackageAuthority>(
                [.. reportingAuthorities]);
        SelectionUsesOriginalSources = selectionUsesOriginalSources;
    }

    public ConfiguredPackageAuthority? Authority { get; }

    public PackageSourceResultIdentity? Source { get; }

    public AcquiredPackageSourcePayload? Payload { get; }

    public IReadOnlyList<PackageAuthorityFailure> Failures { get; }

    public IReadOnlyList<ConfiguredPackageAuthority> NotFoundAuthorities
        { get; }

    internal IReadOnlyList<ConfiguredPackageAuthority>? ReportingAuthorities
        { get; }

    internal bool SelectionUsesOriginalSources { get; }
}
