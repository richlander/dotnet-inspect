using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

internal static class ApiTypeSectionCardinality
{
    public static IReadOnlyDictionary<
        string,
        SectionCardinalityDeclaration> Declarations { get; } =
        new Dictionary<string, SectionCardinalityDeclaration>(
            StringComparer.OrdinalIgnoreCase)
        {
            [SectionNames.ApiInfo] =
                SectionCardinalityDeclaration.Scalar,
            [SectionNames.Classes] =
                SectionCardinalityDeclaration.Inventory,
            [SectionNames.Structs] =
                SectionCardinalityDeclaration.Inventory,
            [SectionNames.Interfaces] =
                SectionCardinalityDeclaration.Inventory,
            [SectionNames.Enums] =
                SectionCardinalityDeclaration.Inventory,
            [SectionNames.Delegates] =
                SectionCardinalityDeclaration.Inventory,
            [SectionNames.TypeForwarders] =
                SectionCardinalityDeclaration.Inventory,
            [SectionNames.InspectionFailures] =
                SectionCardinalityDeclaration.Inventory,
        };
}
