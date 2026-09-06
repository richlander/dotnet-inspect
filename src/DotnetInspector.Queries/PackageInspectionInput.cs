using System.Collections.Immutable;

using DotnetInspector.Artifacts;
using DotnetInspector.Packages;
using ILInspector.Metadata;

using PackageSourceCoordinate = NuGetFetch.PackageSourceCoordinate;

namespace DotnetInspector.Queries;

/// <summary>
/// One retained package input authorized for explicit entry inspection, not
/// an assertion that compile-asset selection succeeded.
/// </summary>
public sealed class PackageInspectionInput
{
    PackageInspectionInput(
        IPackageContent content,
        string? packageId,
        string? packageVersion,
        RealizedMemberCoordinate.Package? coordinate,
        PackageSourceCoordinate? sourceCoordinate = null)
    {
        Content = content;
        PackageId = packageId;
        PackageVersion = packageVersion;
        Coordinate = coordinate;
        SourceCoordinate = sourceCoordinate;
        ProducerKey = content.ProducerKey;
        ContentGenerationIdentity = content.GenerationIdentity;
    }

    internal IPackageContent Content { get; }
    public string? PackageId { get; }
    public string? PackageVersion { get; }
    public RealizedMemberCoordinate.Package? Coordinate { get; }
    public PackageSourceCoordinate? SourceCoordinate { get; }
    public string ProducerKey { get; }
    public PackageContentGenerationIdentity ContentGenerationIdentity { get; }

    /// <summary>
    /// Consumes an admitted source payload without requiring a portable Root
    /// coordinate or interpreting its producer as cache authority.
    /// </summary>
    public static PackageInspectionInput CreateFromPayload(AcquiredPackageSourcePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!payload.ProducerKey.Equals(payload.Content.ProducerKey, StringComparison.Ordinal))
            throw new ArgumentException(
                "The acquired payload and retained content name different producers.",
                nameof(payload));

        return new PackageInspectionInput(
            payload.Content, payload.Coordinate.PackageId, payload.Coordinate.Version,
            coordinate: null, sourceCoordinate: payload.Coordinate);
    }

    /// <summary>Retains the exact content and producer of an acquisition-issued binding.</summary>
    public static PackageInspectionInput CreateFromBinding(PackageRootBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!ReferenceEquals(
                binding.ContentGenerationIdentity,
                binding.Root.Content.GenerationIdentity)
            || !binding.Coordinate.Producer.Equals(
                binding.Root.Content.ProducerKey, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The package binding no longer identifies its retained content.",
                nameof(binding));
        }

        return new PackageInspectionInput(
            binding.Root.Content, binding.Root.PackageId,
            binding.Root.PackageVersion, binding.Coordinate);
    }

    /// <summary>
    /// Authorizes explicitly supplied local package content. Nuspec identity
    /// is descriptive provenance and never issues a canonical acquisition coordinate.
    /// </summary>
    public static PackageInspectionInput CreateLocal(
        IPackageContent content,
        string? nuspecPackageId = null,
        string? nuspecVersion = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        string? id = nuspecPackageId?.Trim();
        bool hasVersion = PackageExtractor.TryNormalizePackageVersion(
            nuspecVersion?.Trim(), out string version);
        bool hasIdentity = PackageCoordinateResolver.IsCanonicalPackageId(id) && hasVersion;
        return new PackageInspectionInput(
            content,
            hasIdentity ? id : null,
            hasIdentity ? version : null,
            coordinate: null);
    }

    /// <summary>
    /// Freezes the caller's exact, ordered entry selection. Missing entries
    /// remain unavailable results; they never authorize a different source.
    /// </summary>
    public PackageInspectionSelection SelectAssemblies(
        IEnumerable<PackageInspectionAssembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ImmutableArray<PackageInspectionAssembly> selected = [.. assemblies];
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (PackageInspectionAssembly assembly in selected)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            ArgumentException.ThrowIfNullOrWhiteSpace(assembly.Path);
            if (assembly.Path.Contains('\\')
                || assembly.Path.Split('/').Any(part => part is "" or "." or "..")
                || !paths.Add(assembly.Path))
            {
                throw new ArgumentException(
                    "Inspection selections require distinct package-relative entry paths.",
                    nameof(assemblies));
            }
        }

        return new PackageInspectionSelection(
            this, selected,
            Content.EnumerateEntries().ToImmutableHashSet(StringComparer.Ordinal));
    }

    internal AssemblyResolutionProvenance Provenance(string? framework) =>
        PackageId is not null && PackageVersion is not null
            ? AssemblyResolutionProvenance.Package(
                PackageId, PackageVersion, framework, rid: null)
            : AssemblyResolutionProvenance.Local("local package archive");
}

/// <summary>Literal inspection selection; it carries no compile-role authority.</summary>
public sealed record PackageInspectionAssembly(
    string Path,
    string? TargetFramework,
    string? ContextKey = null);

/// <summary>Opaque identity of one immutable explicit inspection selection.</summary>
public sealed class PackageInspectionSelectionIdentity
{
    internal PackageInspectionSelectionIdentity() { }
}

public sealed class PackageInspectionSelection
{
    internal PackageInspectionSelection(
        PackageInspectionInput input,
        ImmutableArray<PackageInspectionAssembly> assemblies,
        ImmutableHashSet<string> entries)
    {
        Input = input;
        Assemblies = assemblies;
        Entries = entries;
    }

    public PackageInspectionInput Input { get; }
    public PackageInspectionSelectionIdentity Identity { get; } = new();
    public ImmutableArray<PackageInspectionAssembly> Assemblies { get; }
    internal ImmutableHashSet<string> Entries { get; }
}

internal sealed record PackageInspectionArtifactProvenance(
    RealizedMemberCoordinate.Package? Coordinate,
    PackageSourceCoordinate? SourceCoordinate,
    string ProducerKey,
    PackageContentGenerationIdentity ContentGenerationIdentity,
    PackageInspectionSelectionIdentity SelectionIdentity,
    PackageInspectionAssembly Assembly) : IArtifactProvenance;
