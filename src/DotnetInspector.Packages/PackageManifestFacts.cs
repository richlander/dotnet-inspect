using System.Collections.Immutable;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

public enum PackageLicenseDeclarationKind
{
    Expression,
    File,
    Url,
}

public sealed record PackageLicenseDeclaration(
    PackageLicenseDeclarationKind Kind,
    string Value);

/// <summary>Identifies the evidence used to establish a manifest coordinate.</summary>
public enum PackageManifestIdentityProvenance
{
    /// <summary>The manifest identity matched an independently supplied coordinate.</summary>
    ExpectedCoordinate,

    /// <summary>The coordinate was validated and normalized from the manifest itself.</summary>
    SelfAttested,
}

/// <summary>One dependency exactly as declared in a package manifest.</summary>
public sealed record DeclaredPackageDependency(
    string Id,
    string VersionRange);

/// <summary>One target-framework dependency group exactly as declared in a package manifest.</summary>
public sealed record DeclaredPackageDependencyGroup(
    string TargetFramework,
    ImmutableArray<DeclaredPackageDependency> Dependencies,
    bool IsImplicitManifestGroup = false);

/// <summary>
/// One framework-reference identity using NuGet's ordinal-ignore-case
/// semantics.
/// </summary>
public sealed class PackageFrameworkReferenceIdentity
    : IEquatable<PackageFrameworkReferenceIdentity>
{
    internal PackageFrameworkReferenceIdentity(string name) =>
        Name = name;

    /// <summary>The first source spelling for this semantic identity.</summary>
    public string Name { get; }

    public bool Equals(PackageFrameworkReferenceIdentity? other) =>
        other is not null
        && Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) =>
        obj is PackageFrameworkReferenceIdentity other && Equals(other);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(Name);
}

/// <summary>
/// One source occurrence associated with its group-local semantic identity.
/// </summary>
public sealed record PackageFrameworkReferenceOccurrence(
    string SourceName,
    PackageFrameworkReferenceIdentity Identity);

/// <summary>
/// One framework-reference group exactly as declared in a package manifest.
/// </summary>
public sealed record PackageManifestFrameworkReferenceGroup(
    string SourceTargetFramework,
    string CanonicalTargetFramework,
    ImmutableArray<PackageFrameworkReferenceOccurrence> Occurrences,
    ImmutableArray<PackageFrameworkReferenceIdentity> References);

/// <summary>
/// Complete ordered framework-reference groups from one package manifest.
/// </summary>
public sealed record PackageManifestFrameworkReferenceFacts(
    ImmutableArray<PackageManifestFrameworkReferenceGroup> Groups);

/// <summary>
/// Why a manifest's framework-reference section could not be projected.
/// </summary>
public enum PackageManifestFrameworkReferenceFailureReason
{
    InvalidTargetFramework,
    InvalidReferenceName,
    ConfiguredLimitExceeded,
}

/// <summary>A content-free framework-reference section failure.</summary>
public sealed record PackageManifestFrameworkReferenceFailure(
    PackageManifestFrameworkReferenceFailureReason Reason)
{
    public string Message => Reason switch
    {
        PackageManifestFrameworkReferenceFailureReason
            .InvalidTargetFramework =>
            "The package manifest contains an invalid framework-reference target.",
        PackageManifestFrameworkReferenceFailureReason
            .InvalidReferenceName =>
            "The package manifest contains an invalid framework-reference name.",
        PackageManifestFrameworkReferenceFailureReason
            .ConfiguredLimitExceeded =>
            "The package manifest framework-reference section exceeds a configured resource limit.",
        _ =>
            "The package manifest framework-reference section could not be projected.",
    };
}

/// <summary>
/// The typed outcome of projecting one manifest's framework-reference section.
/// </summary>
public abstract record PackageManifestFrameworkReferenceFactsResult
{
    private PackageManifestFrameworkReferenceFactsResult()
    {
    }

    public sealed record Available(
        PackageManifestFrameworkReferenceFacts Value) :
        PackageManifestFrameworkReferenceFactsResult;

    public sealed record Failed(
        PackageManifestFrameworkReferenceFailure Failure) :
        PackageManifestFrameworkReferenceFactsResult;
}

/// <summary>
/// Immutable facts declared by one validated package manifest.
/// </summary>
public sealed record PackageManifestFacts(
    PackageSourceCoordinate Coordinate,
    string ManifestVersion,
    InertString? Description,
    string? Authors,
    string? Repository,
    string? RepositoryType,
    string? RepositoryCommit,
    string? License,
    string? LicenseUrl,
    ImmutableArray<string> PackageTypes,
    bool IsToolPackage,
    string? ReadmeFile,
    ImmutableArray<DeclaredPackageDependencyGroup> DependencyGroups)
{
    public PackageLicenseDeclaration? LicenseDeclaration { get; init; }

    public string? IconFile { get; init; }

    public string? IconUrl { get; init; }

    public PackageManifestIdentityProvenance IdentityProvenance { get; init; } =
        PackageManifestIdentityProvenance.ExpectedCoordinate;

    public PackageManifestFrameworkReferenceFactsResult FrameworkReferences
    {
        get;
        init;
    } = new PackageManifestFrameworkReferenceFactsResult.Available(
        new PackageManifestFrameworkReferenceFacts([]));
}

/// <summary>
/// The stable reason one package manifest could not be projected.
/// </summary>
public enum PackageManifestFailureReason
{
    MalformedXml,
    UnsupportedDocumentShape,
    IdentityMismatch,
    InvalidDependencyContract,
    ConfiguredLimitExceeded,
    InvalidIdentityContract,
}

/// <summary>
/// A content-free package-manifest projection failure.
/// </summary>
public sealed record PackageManifestFailure
{
    public PackageManifestFailure(
        PackageManifestFailureReason reason,
        int lineNumber = 0,
        int linePosition = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lineNumber);
        ArgumentOutOfRangeException.ThrowIfNegative(linePosition);
        Reason = reason;
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    public PackageManifestFailureReason Reason { get; }

    /// <summary>
    /// The one-based XML line where parsing failed, or zero when unavailable.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// The one-based XML position where parsing failed, or zero when unavailable.
    /// </summary>
    public int LinePosition { get; }

    public string Message => Reason switch
    {
        PackageManifestFailureReason.MalformedXml
            when LineNumber > 0 && LinePosition > 0 =>
            $"Package manifest is not well-formed XML at line {LineNumber}, position {LinePosition}.",
        PackageManifestFailureReason.MalformedXml =>
            "Package manifest is not well-formed XML.",
        PackageManifestFailureReason.UnsupportedDocumentShape =>
            "The package manifest has an unsupported document shape or namespace.",
        PackageManifestFailureReason.IdentityMismatch =>
            "The package manifest identity does not match the requested package.",
        PackageManifestFailureReason.InvalidIdentityContract =>
            "The package manifest contains an invalid package identity.",
        PackageManifestFailureReason.InvalidDependencyContract =>
            "The package manifest contains an invalid dependency declaration.",
        PackageManifestFailureReason.ConfiguredLimitExceeded =>
            "The package manifest exceeds a configured resource limit.",
        _ => "The package manifest could not be projected.",
    };
}

/// <summary>
/// The typed outcome of projecting facts from one exact package manifest.
/// </summary>
public abstract record PackageManifestFactsResult
{
    private PackageManifestFactsResult()
    {
    }

    public sealed record Available(
        PackageManifestFacts Value) : PackageManifestFactsResult;

    public sealed record Failed(
        PackageManifestFailure Failure) : PackageManifestFactsResult;
}
