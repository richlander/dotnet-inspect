using System.Collections.Immutable;

namespace DotnetInspector.Sections;

/// <summary>Section names exposed by the explicit ReadyToRun library lens.</summary>
public static class ReadyToRunSectionNames
{
    public const string Prefix = "ReadyToRun: ";
    public const string Image = Prefix + "Image";
    public const string Sections = Prefix + "Sections";

    public static ImmutableArray<string> All { get; } = [Image, Sections];
}
