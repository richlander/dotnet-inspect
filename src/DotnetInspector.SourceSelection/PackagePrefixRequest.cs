namespace DotnetInspector.SourceSelection;

/// <summary>Inert, bounded package-prefix intent; construction does not authorize source access.</summary>
public sealed record PackagePrefixRequest
{
    public PackagePrefixRequest(
        string prefix,
        int maxPackages,
        bool includePrerelease = false)
        : this(new PackagePrefixDeclaration(prefix), maxPackages, includePrerelease)
    {
    }

    private PackagePrefixRequest(
        PackagePrefixDeclaration declaration,
        int maxPackages,
        bool includePrerelease)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPackages);
        Declaration = declaration;
        MaxPackages = maxPackages;
        IncludePrerelease = includePrerelease;
    }

    /// <summary>Applies consumer policy while retaining the exact supplied declaration.</summary>
    public static PackagePrefixRequest Create(
        PackagePrefixDeclaration declaration,
        int maxPackages,
        bool includePrerelease = false) =>
        new(declaration, maxPackages, includePrerelease);

    public PackagePrefixDeclaration Declaration { get; }
    public string Prefix => Declaration.Prefix;
    public int MaxPackages { get; }
    public bool IncludePrerelease { get; }
}
