using DotnetInspector.Queries;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries.Definitions;

namespace InspectWeb.Engine.CatalogFacade;

/// <summary>
/// Maps product-owned ecosystem demo descriptors and resolved scenarios to
/// browser-local transport records so
/// <c>ts-jsexport</c> can generate real TypeScript interfaces (same reason as
/// <see cref="BrowserVocabulary"/>).
/// </summary>
internal static class BrowserProductHomeDemos
{
    internal static BrowserHomeDemoCatalog ToCatalog(
        IReadOnlyList<EcosystemDemoDescriptor> entries) =>
        new([.. entries.Select(static e => new BrowserHomeDemoCatalogEntry(
            e.ScenarioId,
            e.Title,
            e.Summary))]);

    internal static BrowserHomeDemoResolved ToResolved(
        EcosystemDemoSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        EcosystemDemoDescriptor descriptor = selection.Descriptor;
        ResolvedScenario scenario = selection.Scenario;
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
            descriptor.Title,
            descriptor.Summary,
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

    internal static BrowserHomeDemoRunPlan ToRunPlan(
        ResolvedScenario scenario)
    {
        ProductDemoRunPlan productPlan = ProductDemoRunPlan.Create(scenario);
        if (productPlan.Scenario.View?.Libraries.Count > 0)
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' browser execution does not support library-scoped views.");
        }
        EnsureNoRuntimeIdentifier(productPlan);

        BrowserHomeDemoRunMember? member = productPlan.Section switch
        {
            ProductDemoSections.Methods when productPlan.Member is null => null,
            ProductDemoSections.Methods =>
                throw new InspectionDefinitionException(
                    $"Home demo '{scenario.ScenarioId}' Methods view must not select a member."),
            ProductDemoSections.CallGraph =>
                ToCallGraphMember(scenario.ScenarioId, productPlan.Member),
            _ => throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' browser execution does not implement "
                + $"section '{productPlan.Section}' (supported: "
                + $"{ProductDemoSections.Methods}, {ProductDemoSections.CallGraph})."),
        };

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
        if (focusIndex < 0)
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' navigation focus is not present in its browser workspace requests.");
        }

        return new BrowserHomeDemoRunPlan(
            requests,
            focusIndex,
            productPlan.TypeName,
            productPlan.Section,
            member);
    }

    private static void EnsureNoRuntimeIdentifier(ProductDemoRunPlan plan)
    {
        bool hasRuntimeIdentifier =
            plan.Context.RuntimeIdentifier is not null
            || plan.Context.Members.Any(member =>
                member is WorkspaceMemberCoordinate.PackageMember
                {
                    RuntimeIdentifier: not null,
                })
            || plan.Focus?.RuntimeIdentifier is not null;
        if (hasRuntimeIdentifier)
        {
            throw new InspectionDefinitionException(
                $"Home demo '{plan.Scenario.ScenarioId}' browser execution does not support runtime-identifier-scoped package workspaces.");
        }
    }

    private static BrowserHomeDemoRunMember ToCallGraphMember(
        string scenarioId,
        ProductDemoMemberSelection? selection)
    {
        if (selection is not
            {
                Anchor: { Length: > 0 } memberAnchor,
            } member)
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenarioId}' Call Graph view must select a member anchor.");
        }

        return new BrowserHomeDemoRunMember(
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
    string Section,
    BrowserHomeDemoRunMember? Member);

internal sealed record BrowserHomeDemoRunMember(
    string Name,
    string? MemberKind,
    string AnchorDigest,
    string MemberSection);
