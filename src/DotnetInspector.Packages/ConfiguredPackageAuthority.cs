using System.Security.Cryptography;
using System.Text;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>The configuration family that established one package authority.</summary>
public enum ConfiguredPackageAuthorityKind
{
    /// <summary>An absolute HTTP or HTTPS package source.</summary>
    Http,

    /// <summary>A canonical local-folder package source.</summary>
    LocalFolder,
}

/// <summary>
/// One package-owned configured authority and its source-result association.
/// </summary>
/// <remarks>
/// Object identity is the authority identity. Endpoint, producer, display, and
/// transport values are evidence about the authority rather than substitutes
/// for that identity.
/// </remarks>
public sealed class ConfiguredPackageAuthority
{
    private const string PersistentKeyNamespace = "authority-v1";

    internal ConfiguredPackageAuthority(
        PackageSource source,
        ClassifiedPackageSourceIdentity classification)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(classification);
        Source = source;
        Classification = classification;
        HttpEndpoint = classification.Kind is ConfiguredPackageAuthorityKind.Http
            ? new Uri(source.Url, UriKind.Absolute)
            : null;
        Association = PackageSourceAssociation.Create();
        PersistentCacheKey = CreatePersistentCacheKey(
            source,
            classification,
            HttpEndpoint);
    }

    /// <summary>Gets the selected configured source representation.</summary>
    public PackageSource Source { get; }

    /// <summary>Gets the classified authority family.</summary>
    public ConfiguredPackageAuthorityKind Kind => Classification.Kind;

    /// <summary>Gets the source-result association minted for this authority.</summary>
    public PackageSourceAssociation Association { get; }

    /// <summary>
    /// Gets the versioned persistent cache key when one can be formed without
    /// credentials, or <see langword="null"/> for process-local authority.
    /// </summary>
    public string? PersistentCacheKey { get; }

    /// <summary>Gets the canonical local identity for a local authority.</summary>
    public LocalPackageSourceIdentity? LocalIdentity =>
        Classification.LocalIdentity;

    /// <summary>Gets the configured endpoint for an HTTP authority.</summary>
    public Uri? HttpEndpoint { get; }

    internal ClassifiedPackageSourceIdentity Classification { get; }

    internal static ClassifiedPackageSourceIdentity Classify(
        PackageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (LocalPackageSourceIdentity.IsLocalSource(source.Url))
        {
            return ClassifiedPackageSourceIdentity.Local(
                LocalPackageSourceIdentity.CreateAbsolute(source.Url));
        }

        // PackageSource construction already admitted only absolute HTTP(S)
        // endpoints or canonical local paths.
        return ClassifiedPackageSourceIdentity.Http(
            new Uri(source.Url, UriKind.Absolute));
    }

    private static string? CreatePersistentCacheKey(
        PackageSource source,
        ClassifiedPackageSourceIdentity classification,
        Uri? httpEndpoint)
    {
        if (source.Credential is not null)
            return null;

        string stableIdentity;
        if (classification.LocalIdentity is { } local)
        {
            stableIdentity = $"local:{local.PersistentValue}";
        }
        else
        {
            Uri endpoint = httpEndpoint!;
            if (endpoint.Query.Length > 0
                || endpoint.Fragment.Length > 0
                || !UrlRedaction.ForPathComponent(endpoint.AbsolutePath)
                    .ToString()
                    .Equals(endpoint.AbsolutePath, StringComparison.Ordinal))
            {
                return null;
            }

            stableIdentity =
                $"http:{PackageSourceIdentity.ForHttpEndpoint(endpoint).Value}";
        }

        byte[] digest = SHA256.HashData(
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true)
                .GetBytes(stableIdentity));
        return $"{PersistentKeyNamespace}-"
            + Convert.ToHexStringLower(digest.AsSpan(0, 16));
    }
}

/// <summary>
/// The package-owned classification key used to decide whether aliases may
/// collapse. Authority equality additionally requires all route policy to
/// agree and is represented at runtime by the authority object itself.
/// </summary>
internal sealed record ClassifiedPackageSourceIdentity
{
    private ClassifiedPackageSourceIdentity(
        ConfiguredPackageAuthorityKind kind,
        PackageSourceIdentity? httpIdentity,
        LocalPackageSourceIdentity? localIdentity)
    {
        Kind = kind;
        HttpIdentity = httpIdentity;
        LocalIdentity = localIdentity;
    }

    public ConfiguredPackageAuthorityKind Kind { get; }
    public PackageSourceIdentity? HttpIdentity { get; }
    public LocalPackageSourceIdentity? LocalIdentity { get; }

    public static ClassifiedPackageSourceIdentity Http(Uri endpoint) =>
        new(
            ConfiguredPackageAuthorityKind.Http,
            PackageSourceIdentity.ForHttpEndpoint(endpoint),
            localIdentity: null);

    public static ClassifiedPackageSourceIdentity Local(
        LocalPackageSourceIdentity identity) =>
        new(
            ConfiguredPackageAuthorityKind.LocalFolder,
            httpIdentity: null,
            identity);
}
