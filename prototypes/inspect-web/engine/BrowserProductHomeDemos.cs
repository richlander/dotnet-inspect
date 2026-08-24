using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace InspectWeb.Engine;

/// <summary>
/// Maps product-owned <see cref="ProductInspectionDemos"/> catalog and
/// <see cref="ResolvedScenario"/> values to browser-local transport records so
/// <c>tsbindgen</c> can generate real TypeScript interfaces (same reason as
/// <see cref="BrowserVocabulary"/>).
/// </summary>
internal static class BrowserProductHomeDemos
{
    internal static BrowserHomeDemoCatalog ToCatalog(
        IReadOnlyList<ProductInspectionDemos.Entry> entries) =>
        new([.. entries.Select(static e => new BrowserHomeDemoCatalogEntry(
            e.Id,
            e.Title,
            e.Summary))]);

    internal static BrowserHomeDemoResolved ToResolved(ResolvedScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var selected = scenario.SelectedContext
            ?? throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' has no selected workspace context.");
        var view = scenario.View
            ?? throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' has no view.");
        var navigation = scenario.Navigation
            ?? throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' has no navigation.");

        return new BrowserHomeDemoResolved(
            scenario.ScenarioId,
            scenario.Title ?? scenario.ScenarioId,
            scenario.Description ?? "",
            [.. selected.Members.Select(ToMember)],
            [.. navigation.Tabs.Select(ToTab)],
            navigation.FocusIndex,
            new BrowserHomeDemoView(
                view.Library,
                view.Type,
                view.MemberAnchor,
                view.MemberKey,
                view.Section));
    }

    private static BrowserHomeDemoNavigationTab ToTab(ResolvedNavigationTab tab) =>
        new(tab.Id, ToMember(tab.Coordinate));

    private static BrowserHomeDemoMember ToMember(WorkspaceMemberCoordinate coordinate) =>
        coordinate switch
        {
            WorkspaceMemberCoordinate.PackageMember package =>
                new BrowserHomeDemoMember(
                    "package",
                    package.PackageId,
                    package.Version,
                    package.Framework,
                    Assembly: null),
            WorkspaceMemberCoordinate.PlatformMember platform =>
                new BrowserHomeDemoMember(
                    "platform",
                    platform.Family,
                    platform.Version,
                    platform.Framework,
                    platform.Assembly),
            _ => throw new InspectionDefinitionException(
                $"Home demo export does not support coordinate kind '{coordinate.GetType().Name}'."),
        };
}
