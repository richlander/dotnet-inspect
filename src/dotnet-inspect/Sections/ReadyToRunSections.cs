using DotnetInspector.Models;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>Registers the explicit ReadyToRun image lens on <c>library</c>.</summary>
public static class ReadyToRunSections
{
    public static SectionPipeline<LibraryInspection> AddReadyToRunLens(
        this SectionPipeline<LibraryInspection> pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        pipeline.Add(new SectionEntry<LibraryInspection>
        {
            Name = ReadyToRunSectionNames.Image,
            IsExpensive = false,
            ExplicitOnly = true,
            ListedInCatalog = false,
            SizeClass = SectionSizeClass.Fixed,
            Cost = SectionCost.NetworkFree,
            Queries = [ReadyToRunImageQuery.Definition],
            HasExplicitApplicability = true,
            IsApplicable = HasReadyToRun,
            CanRender = HasReadyToRun,
        });

        pipeline.Add(new SectionEntry<LibraryInspection>
        {
            Name = ReadyToRunSectionNames.Sections,
            IsExpensive = false,
            ExplicitOnly = true,
            ListedInCatalog = false,
            SizeClass = SectionSizeClass.Verbose,
            Cost = SectionCost.NetworkFree,
            Queries = [ReadyToRunImageQuery.Definition],
            HasExplicitApplicability = true,
            IsApplicable = static model =>
                model.ReadyToRunOverview is { Sections.Length: > 0 },
            CanRender = static model =>
                model.ReadyToRunOverview is { Sections.Length: > 0 },
        });

        return pipeline.AddCategory(
            SectionCategoryNames.ReadyToRun,
            [.. ReadyToRunSectionNames.All]);
    }

    static bool HasReadyToRun(LibraryInspection model)
        => model.ReadyToRunOverview is not null;
}
