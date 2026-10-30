using System.Collections.Immutable;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// The declared shapes of every type and member section, united across the
/// type listing and the three member catalogs. A name declares the same shape
/// wherever it appears, so one lookup answers "is this section a Text" for a
/// renderer that has no catalog in hand (a direct <c>WriteTypeOutputAsync</c>
/// caller) as well as for the preamble.
/// </summary>
internal static class ApiSectionShapes
{
    public static ImmutableDictionary<string, SectionShape> Union { get; } =
        new[]
            {
                ApiTypeSectionDescriptors.CreatePipeline().SectionShapes,
                ApiMemberSectionDescriptors.CreatePipeline().SectionShapes,
                ApiMemberOverloadSectionDescriptors.CreatePipeline().SectionShapes,
                ApiMemberDetailSectionDescriptors.CreatePipeline().SectionShapes,
            }
            .SelectMany(static shapes => shapes)
            .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToImmutableDictionary(
                static group => group.Key,
                static group => group.First().Value,
                StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="section"/> declares <see cref="SectionShape.Text"/>.</summary>
    public static bool IsText(string section) =>
        Union.TryGetValue(section, out SectionShape shape)
        && shape == SectionShape.Text;
}
