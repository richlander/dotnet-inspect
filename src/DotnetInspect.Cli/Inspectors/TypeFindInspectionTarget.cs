using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Cli.Inspectors;

public sealed record TypeFindNavigation(
    string? TypeCommand,
    string? MemberIndexCommand,
    string? UnavailableReason);

/// <summary>
/// Exact CLI inspection target projected from one selected locator
/// observation. Display fields are never inputs to this projection.
/// </summary>
internal sealed class TypeFindInspectionTarget
{
    readonly TypeDeclarationLocatorSectionCandidate _candidate;

    TypeFindInspectionTarget(
        TypeDeclarationLocatorSectionCandidate candidate)
    {
        _candidate = candidate;
    }

    internal static TypeFindInspectionTarget Create(
        TypeDeclarationLocatorSectionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new(candidate);
    }

    internal TypeOptions ApplyTo(TypeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string typeName = TypeName();
        return _candidate.Observation.Realization switch
        {
            TypeDeclarationLocatorRealization.PlatformRealization =>
                throw PlatformReopeningUnavailable(),
            TypeDeclarationLocatorRealization.PackageRealization package =>
                ApplyPackage(options, package, typeName),
            _ => throw new InvalidOperationException(
                "The selected locator candidate has no CLI Type reopening path."),
        };
    }

    internal MemberOptions ApplyTo(MemberOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string typeName = TypeName();
        return _candidate.Observation.Realization switch
        {
            TypeDeclarationLocatorRealization.PlatformRealization =>
                throw PlatformReopeningUnavailable(),
            TypeDeclarationLocatorRealization.PackageRealization package =>
                ApplyPackage(options, package, typeName),
            _ => throw new InvalidOperationException(
                "The selected locator candidate has no CLI Member reopening path."),
        };
    }

    internal TypeFindNavigation CreateNavigation(
        NuGetSourceOptions? sourceOptions,
        bool includeAll)
    {
        string type = ShellCommandText.Quote(TypeName());
        string sourceArguments;
        switch (_candidate.Observation.Realization)
        {
            case TypeDeclarationLocatorRealization.PackageRealization package:
                if (!CanReplayPackage(
                        package,
                        sourceOptions,
                        out string? unavailable))
                {
                    return new(null, null, unavailable);
                }
                if (!TryGetPackageSelection(
                        package,
                        out string selectedTfm,
                        out string assetPath,
                        out unavailable))
                {
                    return new(null, null, unavailable);
                }

                sourceArguments =
                    $"--package "
                    + ShellCommandText.Quote(
                        $"{package.PackageId}@{package.Version}")
                    + " --library "
                    + ShellCommandText.Quote(assetPath)
                    + (includeAll ? " --all" : "")
                    + " --tfm "
                    + ShellCommandText.Quote(selectedTfm);
                break;

            case TypeDeclarationLocatorRealization.PlatformRealization:
                return new(
                    null,
                    null,
                    "The selected Platform observation is implementation-pack "
                        + "content, but the public Type and Member CLI source "
                        + "syntax reopens the reference view.");

            default:
                return new(
                    null,
                    null,
                    "This source context has no portable CLI reopening form.");
        }

        return new(
            $"dotnet-inspect type {type} {sourceArguments}",
            $"dotnet-inspect member {type} {sourceArguments} "
                + "-S 'Member Index'",
            null);
    }

    string TypeName() =>
        MetadataTypeNameFormatter.FormatGenericTypeName(
            _candidate.Name.ToMetadataFullName());

    TypeOptions ApplyPackage(
        TypeOptions options,
        TypeDeclarationLocatorRealization.PackageRealization package,
        string typeName)
    {
        if (!TryGetPackageSelection(
                package,
                out string selectedTfm,
                out string assetPath,
                out string? unavailable))
        {
            throw new InvalidOperationException(unavailable);
        }

        return options with
        {
            TypeName = typeName,
            PackagePath = $"{package.PackageId}@{package.Version}",
            AssemblyPath = assetPath,
            PlatformAssembly = null,
            Tfm = selectedTfm,
            OriginalTypeQuery = typeName,
            PlatformPrefixQuery = null,
            AllowPlatformPrefixFallback = false,
        };
    }

    MemberOptions ApplyPackage(
        MemberOptions options,
        TypeDeclarationLocatorRealization.PackageRealization package,
        string typeName)
    {
        if (!TryGetPackageSelection(
                package,
                out string selectedTfm,
                out string assetPath,
                out string? unavailable))
        {
            throw new InvalidOperationException(unavailable);
        }

        return options with
        {
            TypeName = typeName,
            PackagePath = $"{package.PackageId}@{package.Version}",
            AssemblyPath = assetPath,
            PlatformAssembly = null,
            Tfm = selectedTfm,
        };
    }

    static InvalidOperationException PlatformReopeningUnavailable() =>
        new(
            "The selected Platform implementation observation has no exact "
                + "Type or Member reopening path.");

    bool TryGetPackageSelection(
        TypeDeclarationLocatorRealization.PackageRealization package,
        out string selectedTfm,
        out string assetPath,
        out string? unavailable)
    {
        if (_candidate.Observation.Selection
                is TypeDeclarationLocatorSelection.PackageSelection selection
            && string.Equals(
                selection.PackageId,
                package.PackageId,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                selection.PackageVersion,
                package.Version,
                StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(selection.Tfm)
            && !string.IsNullOrWhiteSpace(selection.AssetPath))
        {
            selectedTfm = selection.Tfm;
            assetPath = selection.AssetPath;
            unavailable = null;
            return true;
        }

        selectedTfm = "";
        assetPath = "";
        unavailable =
            "The selected package observation has no exact asset path and "
                + "target framework for Type or Member reopening.";
        return false;
    }

    static bool CanReplayPackage(
        TypeDeclarationLocatorRealization.PackageRealization package,
        NuGetSourceOptions? sourceOptions,
        out string? unavailable)
    {
        if (sourceOptions is not null
            && (sourceOptions.Sources.Length > 0
                || sourceOptions.AdditionalSources.Length > 0
                || sourceOptions.ConfigFile is not null
                || sourceOptions.ConfigDirectory is not null))
        {
            unavailable =
                "The selected package used configured source authority that "
                + "has no producer-pinned CLI reopening codec.";
            return false;
        }

        string nugetOrgKey =
            NuGetCache.GetSourceKey(PackageSource.NuGetOrg.Url);
        if (!string.Equals(
                package.Producer,
                nugetOrgKey,
                StringComparison.Ordinal))
        {
            unavailable =
                "The selected package producer cannot be represented by the "
                + "default NuGet.org CLI source.";
            return false;
        }

        unavailable = null;
        return true;
    }
}
