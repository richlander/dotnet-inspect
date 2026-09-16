namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Host-neutral lowering of one resolved product demo into the workspace,
/// navigation focus, type/member selection, and product section it runs.
/// Hosts may encode the plan differently, but do not parse the member
/// selection independently.
/// </summary>
public sealed record ProductDemoRunPlan(
    ResolvedScenario Scenario,
    ResolvedWorkspaceContext Context,
    ResolvedNavigationTab? Focus,
    string TypeName,
    ProductDemoMemberSelection? Member,
    string Section)
{
    public static ProductDemoRunPlan Create(ResolvedScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ProductDemoSections.EnsureHomeDemoBinding(scenario);

        var context = scenario.SelectedContext
            ?? throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' has no selected workspace context.");
        var view = scenario.View
            ?? throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' has no view.");
        if (view.Type is not { Length: > 0 } typeName)
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' view must set type.");
        }

        ResolvedNavigationTab? focus = scenario.Navigation?.FocusTab;
        if (focus is not null && !context.Members.Contains(focus.Coordinate))
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenario.ScenarioId}' navigation focus is not a member of its selected workspace context.");
        }

        ProductDemoMemberSelection? member = ProductDemoMemberSelection.Create(
            scenario.ScenarioId,
            view.MemberKey,
            view.MemberAnchor,
            view.MemberSignature);
        return new ProductDemoRunPlan(
            scenario,
            context,
            focus,
            typeName,
            member,
            view.Section!);
    }
}

/// <summary>Product member selector carried by a demo run plan.</summary>
public sealed record ProductDemoMemberSelection(
    string Name,
    string? Kind,
    string? Anchor,
    string? Signature)
{
    internal static ProductDemoMemberSelection? Create(
        string scenarioId,
        string? memberKey,
        string? memberAnchor,
        string? memberSignature)
    {
        bool hasMemberSelection =
            memberKey is { Length: > 0 }
            || memberAnchor is { Length: > 0 }
            || memberSignature is { Length: > 0 };
        if (!hasMemberSelection)
            return null;

        if (string.IsNullOrWhiteSpace(memberKey))
        {
            throw new InspectionDefinitionException(
                $"Home demo '{scenarioId}' member view must set memberKey for run.");
        }

        int separator = memberKey.IndexOf(':');
        if (separator <= 0 || separator >= memberKey.Length - 1)
        {
            return new ProductDemoMemberSelection(
                memberKey,
                Kind: null,
                memberAnchor,
                memberSignature);
        }

        string kind = memberKey[..separator];
        string name = memberKey[(separator + 1)..];
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name))
        {
            throw new InspectionDefinitionException(
                $"Invalid memberKey '{memberKey}' (expected kind:name).");
        }

        return new ProductDemoMemberSelection(
            name,
            kind,
            memberAnchor,
            memberSignature);
    }
}
