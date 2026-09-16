using DotnetInspect.Cli.Options;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Inspectors;

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

}
