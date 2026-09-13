using DotnetInspector.Platforms;

namespace DotnetInspector.SourceSelection;

/// <summary>
/// Declares one relevant logical platform library population before realization.
/// </summary>
public sealed record PlatformLibraryPopulationDeclaration
{
    public PlatformLibraryPopulationDeclaration(PlatformFamily family)
    {
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));

        Family = family;
    }

    public PlatformFamily Family { get; }
}
