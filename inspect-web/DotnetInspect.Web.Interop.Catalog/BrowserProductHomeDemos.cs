using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Ecosystems;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Web.Interop.Catalog;

/// <summary>
/// Maps product-owned ecosystem demo descriptors and resolved scenarios to
/// browser-local transport records so
/// <c>ts-jsexport</c> can generate real TypeScript interfaces (same reason as
/// <see cref="BrowserVocabulary"/>).
/// </summary>
[SupportedOSPlatform("browser")]
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

        BrowserHomeDemoRunRequest[] requests =
        [
            .. productPlan.Context.Members.Select(coordinate =>
                ToRunRequest(
                    scenario.ScenarioId,
                    coordinate,
                    productPlan.Context.Framework)),
        ];
        if (requests.Length == 0)
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' browser workspace has no requests.");
        }
        if (requests.Select(request => request.GetType()).Distinct().Count() != 1)
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' browser execution does not support mixed package and Platform workspaces.");
        }
        ValidateRequests(scenario.ScenarioId, requests);
        ResolvedNavigationTab focusTab = productPlan.Focus
            ?? throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' browser execution requires navigation focus.");
        BrowserHomeDemoRunRequest focus =
            ToRunRequest(
                scenario.ScenarioId,
                focusTab.Coordinate,
                productPlan.Context.Framework);
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

    private static void ValidateRequests(
        string scenarioId,
        BrowserHomeDemoRunRequest[] requests)
    {
        if (requests[0] is BrowserHomeDemoRunRequest.Package)
        {
            if (requests.Distinct().Count() != requests.Length)
            {
                throw new InspectionDefinitionException(
                    $"Home demo '{scenarioId}' browser workspace contains duplicate package coordinates.");
            }
            return;
        }

        BrowserHomeDemoRunRequest.Platform[] platformRequests =
        [
            .. requests.Select(request =>
                request as BrowserHomeDemoRunRequest.Platform
                ?? throw new InvalidOperationException(
                    "A homogeneous Platform request set contains another request kind.")),
        ];
        BrowserHomeDemoRunRequest.Platform? unsupported =
            platformRequests.FirstOrDefault(request =>
                !BrowserPlatformWorkspace.IsSupportedFamily(request.Family));
        if (unsupported is not null)
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenarioId}' Platform family "
                + $"'{unsupported.Family}' is not supported by Browser execution.");
        }
        if (platformRequests
                .Select(request => request.Version)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != 1
            || platformRequests
                .Select(request => request.TargetFramework)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != 1)
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenarioId}' Browser Platform workspace must use "
                + "one exact target framework and Platform version.");
        }
        int distinctCoordinates = platformRequests
            .Select(request =>
                $"{request.Family}\0{request.Assembly}\0"
                + $"{request.Version}\0{request.TargetFramework}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (distinctCoordinates != platformRequests.Length)
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenarioId}' browser workspace contains duplicate Platform coordinates.");
        }
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

    private static BrowserHomeDemoRunRequest ToRunRequest(
        string scenarioId,
        WorkspaceMemberCoordinate coordinate,
        string? contextFramework) =>
        coordinate switch
        {
            WorkspaceMemberCoordinate.PackageMember
            {
                Version: { Length: > 0 } version,
                Framework: { Length: > 0 } framework,
            } package =>
                new BrowserHomeDemoRunRequest.Package(
                    new BrowserPackageRequest(
                        package.PackageId,
                        version,
                        framework)),
            WorkspaceMemberCoordinate.PackageMember package =>
                throw new InspectionDefinitionException(
                    $"Home demo '{scenarioId}' package '{package.PackageId}' must pin version and framework for browser execution."),
            WorkspaceMemberCoordinate.PlatformMember platform =>
                ToPlatformRunRequest(
                    scenarioId,
                    platform,
                    contextFramework),
            _ => throw new InspectionDefinitionException(
                $"Home demo '{scenarioId}' browser execution does not support coordinate kind '{coordinate.GetType().Name}'."),
        };

    private static BrowserHomeDemoRunRequest.Platform ToPlatformRunRequest(
        string scenarioId,
        WorkspaceMemberCoordinate.PlatformMember platform,
        string? contextFramework)
    {
        if (platform.Framework is { Length: > 0 } memberFramework
            && contextFramework is { Length: > 0 }
            && !string.Equals(
                memberFramework,
                contextFramework,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenarioId}' Platform coordinate "
                + $"'{platform.Family}:{platform.Assembly ?? "(all)"}' framework "
                + $"'{memberFramework}' conflicts with workspace context framework "
                + $"'{contextFramework}'.");
        }

        if (platform is not
            {
                Assembly: { Length: > 0 } assembly,
                Version: { Length: > 0 } platformVersion,
            }
            || (platform.Framework ?? contextFramework)
                is not { Length: > 0 } platformFramework)
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenarioId}' Platform coordinate "
                + $"'{platform.Family}:{platform.Assembly ?? "(all)"}' must pin "
                + "assembly, version, and framework for browser execution.");
        }

        if (platformVersion.Equals(
            "latest",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenarioId}' Platform coordinate "
                + $"'{platform.Family}:{assembly}' must pin an exact version "
                + "for browser execution.");
        }

        return new BrowserHomeDemoRunRequest.Platform(
            platform.Family,
            assembly,
            platformVersion,
            platformFramework);
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

internal sealed record BrowserHomeDemoRunPlan(
    BrowserHomeDemoRunRequest[] Requests,
    int FocusRequestIndex,
    string TypeId,
    string Section,
    BrowserHomeDemoRunMember? Member);

internal abstract record BrowserHomeDemoRunRequest
{
    private BrowserHomeDemoRunRequest()
    {
    }

    internal sealed record Package(
        BrowserPackageRequest Request) : BrowserHomeDemoRunRequest;

    internal sealed record Platform(
        string Family,
        string Assembly,
        string Version,
        string TargetFramework) : BrowserHomeDemoRunRequest;
}

internal sealed record BrowserHomeDemoRunMember(
    string Name,
    string? MemberKind,
    string AnchorDigest,
    string MemberSection);
