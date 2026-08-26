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

    internal static BrowserHomeDemoRunPlan ToCallGraphRunPlan(
        ResolvedScenario scenario)
    {
        ProductDemoRunPlan productPlan = ProductDemoRunPlan.Create(scenario);
        if (!string.Equals(
                productPlan.Section,
                ProductDemoSections.CallGraph,
                StringComparison.Ordinal))
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' does not select the Call Graph section.");
        }

        if (productPlan.Member is not
            {
                Anchor: { Length: > 0 } memberAnchor,
            } member)
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' Call Graph view must select a member anchor.");
        }

        BrowserPackageRequest[] requests =
        [
            .. productPlan.Context.Members.Select(coordinate =>
                ToPackageRequest(scenario.ScenarioId, coordinate)),
        ];
        ResolvedNavigationTab focusTab = productPlan.Focus
            ?? throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' browser execution requires navigation focus.");
        BrowserPackageRequest focus =
            ToPackageRequest(scenario.ScenarioId, focusTab.Coordinate);
        if (requests.Distinct().Count() != requests.Length)
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' browser workspace contains duplicate package coordinates.");
        }
        int focusIndex = Array.FindIndex(
            requests,
            request => request == focus);

        return new BrowserHomeDemoRunPlan(
            requests,
            focusIndex,
            productPlan.TypeName,
            member.Name,
            member.Kind,
            memberAnchor,
            MemberSection: "call-graph");
    }

    private static BrowserPackageRequest ToPackageRequest(
        string scenarioId,
        WorkspaceMemberCoordinate coordinate) =>
        coordinate switch
        {
            WorkspaceMemberCoordinate.PackageMember
            {
                Version: { Length: > 0 } version,
                Framework: { Length: > 0 } framework,
            } package =>
                new BrowserPackageRequest(
                    package.PackageId,
                    version,
                    framework),
            WorkspaceMemberCoordinate.PackageMember package =>
                throw new InspectionDefinitionException(
                    $"Home demo '{scenarioId}' package '{package.PackageId}' must pin version and framework for browser execution."),
            _ => throw new InspectionDefinitionException(
                $"Home demo '{scenarioId}' browser execution does not support coordinate kind '{coordinate.GetType().Name}'."),
        };

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

internal sealed record BrowserHomeDemoRunPlan(
    BrowserPackageRequest[] Requests,
    int FocusRequestIndex,
    string TypeId,
    string MemberName,
    string? MemberKind,
    string MemberAnchorDigest,
    string MemberSection);
