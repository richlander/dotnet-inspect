using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Output;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Sections;

internal static class LibraryOutputItemBindings
{
    public static IReadOnlyDictionary<string, OutputItemBinding<LibraryInspection>> MetadataInventories
        { get; } = new Dictionary<string, OutputItemBinding<LibraryInspection>>(
            StringComparer.OrdinalIgnoreCase)
        {
            [SectionNames.Resources] = OutputItemBinding<LibraryInspection>.Create<ManifestResourceInfo>(
                SectionNames.Resources,
                static model => model.ResourceDisplayRows,
                static (model, rows) =>
                {
                    model.ResourceDisplayRows = rows;
                    return model;
                }),
            [SectionNames.TypeForwarders] = OutputItemBinding<LibraryInspection>.Create<TypeForwarderInfo>(
                SectionNames.TypeForwarders,
                static model => model.TypeForwarderDisplayRows,
                static (model, rows) =>
                {
                    model.TypeForwarderDisplayRows = rows;
                    return model;
                }),
        };
}
