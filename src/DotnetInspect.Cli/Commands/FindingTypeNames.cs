using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspect.Cli.Commands;

internal static class FindingTypeNames
{
    public static IEnumerable<string> EnumerateResolvable(ApiSurface surface)
    {
        foreach (ApiType type in surface.Types)
            yield return type.FullName;

        foreach (ApiSurfaceInspectionFailure failure
            in surface.InspectionFailures)
        {
            if (failure.OwningTypeDefinition is { } owner)
                yield return owner.ToMetadataFullName();

            if (failure.AffectedTypeDefinitions.IsDefaultOrEmpty)
                continue;

            foreach (MetadataTypeDefinitionName affected in
                failure.AffectedTypeDefinitions)
            {
                yield return affected.ToMetadataFullName();
            }
        }
    }
}
