using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// The sources one host has authorized to serve one package id, and the reason
/// it authorized none when it can state one.
/// </summary>
/// <remarks>
/// An empty authorization is a result, not an absence: it says this host will
/// not let any producer answer for that id, and the caller turns it into a
/// typed unavailable rather than widening the search. A host that knows
/// <em>why</em> — package source mapping matched no pattern, say — says so
/// through <see cref="Deny"/>, so the reason survives to the caller instead of
/// being replaced by a generic one.
/// </remarks>
public sealed record PackageSourceAuthorization
{
    private readonly IReadOnlyDictionary<
        PackageSourceAssociation,
        ConfiguredPackageAuthority> _authoritiesByAssociation;

    PackageSourceAuthorization(
        IReadOnlyList<ConfiguredPackageAuthority> authorities,
        string? denialReason)
    {
        Authorities = authorities;
        Sources = new ReadOnlyCollection<PackageSource>(
            [.. authorities.Select(authority => authority.Source)]);
        IEqualityComparer<PackageSourceAssociation> associationComparer =
            ReferenceEqualityComparer.Instance;
        _authoritiesByAssociation =
            authorities.ToDictionary(
                authority => authority.Association,
                associationComparer);
        DenialReason = denialReason;
    }

    /// <summary>The configured package authorities for one package ID.</summary>
    public IReadOnlyList<ConfiguredPackageAuthority> Authorities { get; }

    /// <summary>
    /// The selected source representations, in consultation order.
    /// </summary>
    public IReadOnlyList<PackageSource> Sources { get; }

    /// <summary>
    /// Why no producer is authorized, when the host stated one. It is always
    /// <see langword="null"/> when <see cref="Sources"/> is non-empty, and may
    /// be null for an empty set the host had no more specific reason for.
    /// </summary>
    public string? DenialReason { get; }

    /// <summary>
    /// Authorizes each independently selected source as one authority, in
    /// consultation order.
    /// </summary>
    /// <remarks>
    /// A policy that owns configured aliases must select and collapse them
    /// before calling this method; endpoint resemblance alone does not grant
    /// this method enough policy evidence to combine authorities.
    /// </remarks>
    public static PackageSourceAuthorization Authorize(
        IEnumerable<PackageSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return new PackageSourceAuthorization(
            new ReadOnlyCollection<ConfiguredPackageAuthority>(
                [
                    .. sources.Select(source =>
                        new ConfiguredPackageAuthority(
                            source,
                            ConfiguredPackageAuthority.Classify(source))),
                ]),
            denialReason: null);
    }

    /// <summary>Authorizes nothing, for the stated reason.</summary>
    public static PackageSourceAuthorization Deny(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new PackageSourceAuthorization([], reason);
    }

    /// <summary>
    /// Recovers the exact configured authority for an owner-issued source
    /// association.
    /// </summary>
    public bool TryGetAuthority(
        PackageSourceAssociation association,
        [NotNullWhen(true)] out ConfiguredPackageAuthority? authority)
    {
        ArgumentNullException.ThrowIfNull(association);
        return _authoritiesByAssociation.TryGetValue(
            association,
            out authority);
    }
}

/// <summary>
/// Host-supplied authorization that answers, for one canonical package id,
/// which producers may serve it.
/// </summary>
/// <remarks>
/// <para>
/// Authorization is per package id because NuGet's own model is: package source
/// mapping selects a different producer set for each id, and a private feed is
/// frequently authorized for exactly one id prefix. A single union of every
/// source a context might use would let a package be fetched from — and served
/// out of the content cache by — a producer its own configuration never
/// authorized, which is the failure mode mapping exists to prevent.
/// </para>
/// <para>
/// An implementation returns an already-authorized, already-ordered set. It is
/// the host's decision, taken before the loader runs; the loader neither
/// discovers configuration nor widens what it is given.
/// </para>
/// </remarks>
public interface IPackageSourceAuthorization
{
    /// <summary>
    /// Returns the producers authorized to serve <paramref name="packageId"/>,
    /// in consultation order.
    /// </summary>
    /// <param name="packageId">
    /// The canonical (lowercase, grammar-validated) package id.
    /// </param>
    PackageSourceAuthorization AuthorizeSourcesFor(string packageId);
}

/// <summary>
/// One uniform policy for every package id, for a host that has no per-package
/// configuration to express — a browser host that was handed its feed list.
/// </summary>
/// <remarks>
/// It is explicit rather than implicit: a host that means "these sources serve
/// everything" says so by choosing this type, so a reader can tell that policy
/// apart from a per-package policy that happens to answer uniformly. Gated by
/// <c>WorkspaceContextLoaderTests.RealizedCoordinate_NamesTheProducerThatServedTheBytes</c>,
/// which realizes one coordinate from each of two feeds through this policy.
/// </remarks>
public sealed class UniformPackageSourceAuthorization : IPackageSourceAuthorization
{
    readonly PackageSourceAuthorization _authorization;

    /// <summary>
    /// Creates a policy that authorizes <paramref name="sources"/> for every
    /// package id.
    /// </summary>
    public UniformPackageSourceAuthorization(IEnumerable<PackageSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _authorization = PackageSourceAuthorization.Authorize(sources);
    }

    /// <inheritdoc />
    public PackageSourceAuthorization AuthorizeSourcesFor(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return _authorization;
    }
}

/// <summary>
/// Desktop authorization: the product's own source, mapping, and credential
/// policy answers for each package id.
/// </summary>
/// <remarks>
/// <para>
/// This is the adapter that keeps the browser-neutral loader free of ambient
/// configuration discovery while the desktop still gets exactly the sources
/// <c>nuget.config</c> and package source mapping select. Mapping and config
/// failures stay typed denials carrying their own message rather than becoming
/// an empty set with no explanation.
/// </para>
/// <para>
/// The interface takes a canonical package id, and the mapping vocabulary is
/// case-insensitive, so a canonical id selects the same patterns the CLI's own
/// spelling would.
/// </para>
/// <para>
/// Gated by
/// <c>NuGetSearchSourcesTests.SourcePolicyAuthorization_AnswersOneProducerSetPerPackageId</c>
/// for the per-id answer and its mapping denial, and by
/// <c>PackageCoordinateResolverTests.SourcePolicy_WithInvalidConfig_IsUnavailable</c>
/// for the unreadable-config denial reaching the resolver as a typed outcome.
/// </para>
/// </remarks>
public sealed class SourcePolicyPackageSourceAuthorization(
    NuGetSourceOptions? sourceOptions = null,
    string? workingDirectory = null) : IPackageSourceAuthorization
{
    /// <inheritdoc />
    public PackageSourceAuthorization AuthorizeSourcesFor(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        // An explicitly selected config that cannot be read is a denial, not an
        // exception escaping the seam: a caller holding this adapter is asking
        // a question that must have a typed answer.
        if (sourceOptions?.ConfigFile is { } configFile
            && NuGetSourceResolver.DescribeConfigProblem(configFile)
                is string configProblem)
        {
            return PackageSourceAuthorization.Deny(configProblem);
        }

        try
        {
            List<PackageSource> mapped =
                NuGetSourceResolver.ResolveSourcesForPackage(
                    sourceOptions,
                    packageId,
                    workingDirectory);
            return PackageSourceAuthorization.Authorize(
                NuGetSourceResolver.ResolveAuthorizedSources(
                    sourceOptions,
                    mapped));
        }
        catch (Exception ex) when (
            ex is PackageSourceMappingException or UnsupportedSourceException)
        {
            return PackageSourceAuthorization.Deny(ex.Message);
        }
        catch (InvalidDataException)
        {
            // A malformed <packageSourceMapping> — a source with no key, a
            // package with no pattern, a pattern that is neither an exact id
            // nor a prefix — is a configuration defect, not an absent answer.
            // It arrives as an exception from the config reader, and the seam's
            // contract is a typed answer, so it becomes a denial here rather
            // than escaping into the loader as an unhandled failure.
            //
            // The reader's message quotes the offending config text and path.
            // That text is not reproduced: a rule-based denial keeps the
            // failure attributable to the configuration the user selected
            // without carrying its contents into a message sink.
            return PackageSourceAuthorization.Deny(
                "The NuGet package source mapping configuration is malformed, so no source can be authorized.");
        }
    }
}
