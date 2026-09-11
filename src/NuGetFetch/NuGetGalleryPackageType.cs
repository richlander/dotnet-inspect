namespace NuGetFetch;

/// <summary>A validated package-type name used by Gallery discovery.</summary>
public sealed record NuGetGalleryPackageType
{
    private NuGetGalleryPackageType(string name)
    {
        Name = name;
    }

    /// <summary>Gets the normalized lowercase package-type name.</summary>
    public string Name { get; }

    public static NuGetGalleryPackageType DotnetTool { get; } = Create("DotnetTool");
    public static NuGetGalleryPackageType Template { get; } = Create("Template");
    public static NuGetGalleryPackageType Dependency { get; } = Create("Dependency");

    /// <summary>Creates a package type, including custom NuGet package types.</summary>
    public static NuGetGalleryPackageType Create(string name)
    {
        // Gallery admits package-type filters using the package-ID grammar.
        if (!PackageCoordinateValidation.IsValidPackageId(name))
        {
            throw new ArgumentException(
                "A Gallery package type must use the NuGet package ID grammar.",
                nameof(name));
        }

        return new NuGetGalleryPackageType(name.ToLowerInvariant());
    }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
