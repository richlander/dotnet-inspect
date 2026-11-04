using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

internal static class PackageOutputItemBindings
{
    public static IReadOnlyDictionary<string, OutputItemBinding<InspectionResult>> FileInventories
        { get; } = PackageFileFamily.Members
            .Where(member => PackageSectionCardinality.Declarations[member.Section].Kind
                == SectionCardinalityKind.Inventory)
            .ToDictionary(
                member => member.Section,
                member => OutputItemBinding<InspectionResult>.Create<PackageFile>(
                    member.Section,
                    model => [.. (model.PackageFiles ?? []).Where(member.Matches)],
                    static (model, rows) =>
                    {
                        model.PackageFiles = [.. rows];
                        return model;
                    },
                    documentContainsItems: false),
                StringComparer.OrdinalIgnoreCase);
}
