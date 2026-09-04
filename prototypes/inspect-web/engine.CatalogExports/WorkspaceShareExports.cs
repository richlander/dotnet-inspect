using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries.Definitions;

using InspectWeb.Engine;
using InspectWeb.Engine.CatalogFacade;

[SupportedOSPlatform("browser")]
public static partial class CatalogExports
{
    [JSExport]
    public static string DecodeWorkspaceShareState(string encoded)
    {
        BrowserWorkspaceShareDecodeResult result =
            BrowserWorkspaceShareOperations.Decode(encoded);
        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default.BrowserWorkspaceShareDecodeResult);
    }

    [JSExport]
    public static string EncodeWorkspaceShareState(string stateJson)
    {
        BrowserWorkspaceShareEncodeResult result;
        try
        {
            BrowserWorkspaceShareState? state = JsonSerializer.Deserialize(
                stateJson,
                BrowserCatalogJsonContext.Default.BrowserWorkspaceShareState);
            result = state is null
                ? BrowserWorkspaceShareOperations.InvalidState(
                    "Workspace share state must be one object.")
                : BrowserWorkspaceShareOperations.Encode(state);
        }
        catch (JsonException)
        {
            result = BrowserWorkspaceShareOperations.InvalidState(
                "Workspace share state is not valid Browser transport JSON.");
        }

        return JsonSerializer.Serialize(
            result,
            BrowserCatalogJsonContext.Default.BrowserWorkspaceShareEncodeResult);
    }
}

namespace InspectWeb.Engine.CatalogFacade
{
    internal static class BrowserWorkspaceShareOperations
    {
        internal static BrowserWorkspaceShareDecodeResult Decode(string encoded)
        {
            try
            {
                WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(encoded);
                WorkspaceSharePacketDefinitionSet definitions =
                    WorkspaceSharePacketTransposer.ToDefinitions(packet);

                BrowserWorkspaceShareTab[] tabs =
                [
                    .. packet.Tabs.Select((tab, index) =>
                    new BrowserWorkspaceShareTab(
                        definitions.Navigation.Tabs[index].Id,
                        tab.SourceKind == WorkspaceShareSourceKind.Package
                            ? "package"
                            : "group",
                        tab.Source,
                        tab.Version,
                        tab.Framework,
                        tab.RuntimeIdentifier)),
            ];
                BrowserWorkspaceShareContext[] contexts =
                [
                    .. packet.Contexts.Select((context, index) =>
                    new BrowserWorkspaceShareContext(
                        definitions.Workspace.Contexts[index].Name,
                        [
                            .. context.TabIndexes.Select(
                                tabIndex => definitions.Navigation.Tabs[tabIndex].Id),
                        ])),
            ];

                return new BrowserWorkspaceShareDecodeResult(
                    Succeeded: true,
                    new BrowserWorkspaceShareState(
                        tabs,
                        contexts,
                        definitions.Navigation.Focus,
                        definitions.Scenario.Context!,
                        new BrowserWorkspaceShareView(
                            definitions.View.Lens,
                            definitions.View.Type,
                            definitions.View.MemberAnchor,
                            definitions.View.MemberSignature,
                            definitions.View.Section,
                            [.. definitions.View.Libraries])),
                    Failure: null);
            }
            catch (WorkspaceSharePacketException ex)
            {
                return new BrowserWorkspaceShareDecodeResult(
                    Succeeded: false,
                    State: null,
                    Failure: new BrowserWorkspaceShareFailure(
                        ex.Kind.ToString(),
                        "packet",
                        ex.Message));
            }
        }

        internal static BrowserWorkspaceShareEncodeResult Encode(
            BrowserWorkspaceShareState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            try
            {
                WorkspaceSharePacketDefinitionSet definitions = ToDefinitions(state);
                WorkspaceSharePacketProjectionResult projection =
                    WorkspaceSharePacketTransposer.ToPacket(definitions);
                if (!projection.Succeeded)
                {
                    WorkspaceSharePacketProjectionFailure failure =
                        projection.Failure
                        ?? throw new InvalidOperationException(
                            "A failed workspace share projection requires a failure.");
                    return new BrowserWorkspaceShareEncodeResult(
                        Succeeded: false,
                        Packet: null,
                        Failure: new BrowserWorkspaceShareFailure(
                            failure.Kind.ToString(),
                            failure.Path,
                            failure.Message));
                }

                return new BrowserWorkspaceShareEncodeResult(
                    Succeeded: true,
                    WorkspaceSharePacketCodec.Encode(
                        projection.Packet
                        ?? throw new InvalidOperationException(
                            "A successful workspace share projection requires a packet.")),
                    Failure: null);
            }
            catch (WorkspaceSharePacketException ex)
            {
                return new BrowserWorkspaceShareEncodeResult(
                    Succeeded: false,
                    Packet: null,
                    Failure: new BrowserWorkspaceShareFailure(
                        ex.Kind.ToString(),
                        "state",
                        ex.Message));
            }
            catch (ArgumentException ex)
            {
                return InvalidState(ex.Message);
            }
        }

        internal static BrowserWorkspaceShareEncodeResult InvalidState(string message) =>
            new(
                Succeeded: false,
                Packet: null,
                Failure: new BrowserWorkspaceShareFailure(
                    "InvalidBrowserState",
                    "state",
                    message));

        private static WorkspaceSharePacketDefinitionSet ToDefinitions(
            BrowserWorkspaceShareState state)
        {
            if (state.Tabs is null
                || state.Contexts is null
                || state.View is null)
            {
                throw new ArgumentException(
                    "Workspace share state requires tabs, contexts, and view.",
                    nameof(state));
            }

            var tabsById = new Dictionary<string, BrowserWorkspaceShareTab>(
                StringComparer.Ordinal);
            var navigationTabs = new NavigationTabDefinition[state.Tabs.Length];
            for (int index = 0; index < state.Tabs.Length; index++)
            {
                BrowserWorkspaceShareTab tab = state.Tabs[index]
                    ?? throw new ArgumentException(
                        "Workspace share tabs cannot contain null.",
                        nameof(state));
                if (!tabsById.TryAdd(tab.Id, tab))
                {
                    throw new ArgumentException(
                        $"Workspace share tab id '{tab.Id}' is duplicated.",
                        nameof(state));
                }

                navigationTabs[index] = tab.Kind switch
                {
                    "package" => new NavigationTabDefinition(
                        tab.Id,
                        coordinate: PackageCoordinate(tab)),
                    "group" => new NavigationTabDefinition(
                        tab.Id,
                        subscribe: GroupSubscription(tab),
                        framework: tab.Framework,
                        runtimeIdentifier: tab.RuntimeIdentifier),
                    _ => throw new ArgumentException(
                        $"Workspace share tab '{tab.Id}' has unsupported kind '{tab.Kind}'.",
                        nameof(state)),
                };
            }

            var workspaceContexts =
                new WorkspaceContextDefinition[state.Contexts.Length];
            var contextIds = new HashSet<string>(StringComparer.Ordinal);
            for (int contextIndex = 0;
                contextIndex < state.Contexts.Length;
                contextIndex++)
            {
                BrowserWorkspaceShareContext context = state.Contexts[contextIndex]
                    ?? throw new ArgumentException(
                        "Workspace share contexts cannot contain null.",
                        nameof(state));
                if (!contextIds.Add(context.Id))
                {
                    throw new ArgumentException(
                        $"Workspace share context id '{context.Id}' is duplicated.",
                        nameof(state));
                }
                if (context.TabIds is null || context.TabIds.Length == 0)
                {
                    throw new ArgumentException(
                        $"Workspace share context '{context.Id}' requires at least one tab.",
                        nameof(state));
                }

                var members = new List<DefinitionMemberCoordinate>();
                string? subscribe = null;
                string? framework = null;
                string? runtimeIdentifier = null;
                var localTabIds = new HashSet<string>(StringComparer.Ordinal);
                for (int tabIndex = 0; tabIndex < context.TabIds.Length; tabIndex++)
                {
                    string tabId = context.TabIds[tabIndex];
                    if (!localTabIds.Add(tabId))
                    {
                        throw new ArgumentException(
                            $"Workspace share context '{context.Id}' repeats tab '{tabId}'.",
                            nameof(state));
                    }
                    if (!tabsById.TryGetValue(tabId, out BrowserWorkspaceShareTab? tab))
                    {
                        throw new ArgumentException(
                            $"Workspace share context '{context.Id}' names unknown tab '{tabId}'.",
                            nameof(state));
                    }

                    framework ??= tab.Framework;
                    runtimeIdentifier ??= tab.RuntimeIdentifier;
                    if (tab.Kind == "group")
                    {
                        if (tabIndex != 0 || subscribe is not null)
                        {
                            throw new ArgumentException(
                                $"Workspace share context '{context.Id}' may begin with one group tab.",
                                nameof(state));
                        }

                        subscribe = GroupSubscription(tab);
                    }
                    else if (tab.Kind == "package")
                    {
                        members.Add(PackageCoordinate(tab));
                    }
                    else
                    {
                        throw new ArgumentException(
                            $"Workspace share tab '{tab.Id}' has unsupported kind '{tab.Kind}'.",
                            nameof(state));
                    }
                }

                workspaceContexts[contextIndex] = new WorkspaceContextDefinition(
                    context.Id,
                    framework,
                    runtimeIdentifier,
                    subscribe,
                    members);
            }

            var workspace = new WorkspaceDefinition(
                InspectionDefinitionJson.CurrentSchemaVersion,
                WorkspaceSharePacketTransposer.WorkspaceId,
                workspaceContexts);
            var navigation = new NavigationDefinition(
                InspectionDefinitionJson.CurrentSchemaVersion,
                WorkspaceSharePacketTransposer.NavigationId,
                navigationTabs,
                state.ActiveTabId);
            var view = new ViewDefinition(
                InspectionDefinitionJson.CurrentSchemaVersion,
                WorkspaceSharePacketTransposer.ViewId,
                lens: state.View.Lens,
                type: state.View.Type,
                memberAnchor: state.View.MemberAnchor,
                memberSignature: state.View.MemberSignature,
                section: state.View.Section,
                libraries: state.View.Libraries);
            var scenario = new ScenarioDefinition(
                InspectionDefinitionJson.CurrentSchemaVersion,
                WorkspaceSharePacketTransposer.ScenarioId,
                workspace: workspace.Id,
                context: state.SelectedContextId,
                view: view.Id,
                navigation: navigation.Id);
            return new WorkspaceSharePacketDefinitionSet(
                workspace,
                navigation,
                view,
                scenario);
        }

        private static DefinitionMemberCoordinate.PackageCoordinate PackageCoordinate(
            BrowserWorkspaceShareTab tab) =>
            new(
                tab.Source,
                tab.Version,
                tab.Framework,
                tab.RuntimeIdentifier);

        private static string GroupSubscription(BrowserWorkspaceShareTab tab)
        {
            if (tab.Version is null)
                return tab.Source;

            int separator = tab.Source.IndexOfAny([':', '+'], 1);
            return separator < 0
                ? $"{tab.Source}@{tab.Version}"
                : tab.Source.Insert(separator, $"@{tab.Version}");
        }
    }
}
