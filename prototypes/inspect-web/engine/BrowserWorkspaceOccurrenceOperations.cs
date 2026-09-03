using System.Collections.Immutable;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Runtime.InteropServices.JavaScript;
using DotnetInspector.Queries;

namespace InspectWeb.Engine
{

internal sealed record BrowserWorkspaceOccurrenceSnapshot(
    BrowserWorkspacePackageOccurrenceView View,
    ImmutableDictionary<
        string,
        BrowserWorkspaceOccurrenceSelection> Selections);

internal sealed record BrowserWorkspaceOccurrenceSelection(
    InspectionWorkspacePackageOccurrenceAction Action,
    BrowserPackageCoordinate Coordinate);

[SupportedOSPlatform("browser")]
internal static class BrowserWorkspaceOccurrenceOperations
{
    static BrowserWorkspaceOccurrenceSession? _current;
    static long _generation;

    internal static async Task<BrowserWorkspacePackageOccurrenceView>
        QueryAsync(IReadOnlyList<BrowserPackageRequest> requests)
        => await QueryAsync(
            requests,
            static request => BrowserPackageWorkspace.ResolveAsync(
                request.PackageId,
                request.Version,
                request.TargetFramework));

    internal static async Task<BrowserWorkspacePackageOccurrenceView>
        QueryAsync(
            IReadOnlyList<BrowserPackageRequest> requests,
            Func<BrowserPackageRequest, Task<BrowserPackageCoordinate>>
                resolveAsync)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(resolveAsync);
        long generation = BeginReplacement();
        var coordinates =
            new List<BrowserPackageCoordinate>(requests.Count);
        var leases = new BrowserPackageWorkspace.PackageLeaseSet();
        try
        {
            foreach (BrowserPackageRequest request in requests)
            {
                BrowserPackageCoordinate coordinate =
                    await resolveAsync(request);
                leases.Lease(coordinate);
                coordinates.Add(coordinate);
            }

            return ReplaceCurrent(coordinates, leases, generation);
        }
        catch
        {
            leases.Dispose();
            throw;
        }
    }

    internal static BrowserWorkspacePackageOccurrenceView ReplaceCurrent(
        IReadOnlyList<BrowserPackageCoordinate> coordinates)
    {
        long generation = BeginReplacement();
        return ReplaceCurrent(
            coordinates,
            new BrowserPackageWorkspace.PackageLeaseSet(),
            generation);
    }

    static BrowserWorkspacePackageOccurrenceView ReplaceCurrent(
        IReadOnlyList<BrowserPackageCoordinate> coordinates,
        BrowserPackageWorkspace.PackageLeaseSet leases,
        long generation)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        ArgumentNullException.ThrowIfNull(leases);

        BrowserWorkspaceOccurrenceSession replacement =
            BrowserWorkspaceOccurrenceSession.Create(
                coordinates,
                leases);
        if (generation != _generation)
        {
            replacement.Dispose();
            return replacement.View with { Superseded = true };
        }

        BrowserWorkspaceOccurrenceSession? previous = _current;
        _current = replacement;
        previous?.Dispose();
        return replacement.View;
    }

    static long BeginReplacement()
    {
        long generation = ++_generation;
        ClearCurrentCore();
        return generation;
    }

    internal static void ClearCurrent()
    {
        _generation++;
        ClearCurrentCore();
    }

    static void ClearCurrentCore()
    {
        BrowserWorkspaceOccurrenceSession? previous = _current;
        _current = null;
        previous?.Dispose();
    }

    internal static BrowserWorkspaceOccurrenceSelection? Activate(
        string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return _current?.Activate(action);
    }

    sealed class BrowserWorkspaceOccurrenceSession : IDisposable
    {
        readonly InspectionWorkspace _workspace;
        readonly BrowserPackageWorkspace.PackageLeaseSet _leases;
        readonly InspectionWorkspacePackageOccurrenceView _productView;
        readonly ImmutableDictionary<
            string,
            BrowserWorkspaceOccurrenceSelection> _selections;

        BrowserWorkspaceOccurrenceSession(
            InspectionWorkspace workspace,
            BrowserPackageWorkspace.PackageLeaseSet leases,
            InspectionWorkspacePackageOccurrenceView productView,
            BrowserWorkspaceOccurrenceSnapshot snapshot)
        {
            _workspace = workspace;
            _leases = leases;
            _productView = productView;
            View = snapshot.View;
            _selections = snapshot.Selections;
        }

        internal BrowserWorkspacePackageOccurrenceView View { get; }

        internal static BrowserWorkspaceOccurrenceSession Create(
            IReadOnlyList<BrowserPackageCoordinate> coordinates,
            BrowserPackageWorkspace.PackageLeaseSet leases)
        {
            var workspace = new InspectionWorkspace();
            try
            {
                PackageRootBinding[] bindings =
                [
                    .. coordinates.Select(coordinate =>
                        coordinate.Binding
                        ?? throw new InvalidOperationException(
                            "A browser Workspace occurrence requires an acquisition-issued package Root binding.")),
                ];
                InspectionWorkspacePackageOccurrenceView productView =
                    workspace.CreatePackageOccurrenceView(bindings);
                BrowserWorkspaceOccurrenceSnapshot snapshot =
                    Project(productView, coordinates);
                return new BrowserWorkspaceOccurrenceSession(
                    workspace,
                    leases,
                    productView,
                    snapshot);
            }
            catch
            {
                workspace.Dispose();
                leases.Dispose();
                throw;
            }
        }

        internal BrowserWorkspaceOccurrenceSelection? Activate(
            string action)
        {
            if (!_selections.TryGetValue(
                    action,
                    out BrowserWorkspaceOccurrenceSelection? selection))
            {
                return null;
            }

            if (_productView.Activate(selection.Action)
                is not InspectionWorkspacePackageOccurrenceActivation.Activated
                activated)
            {
                return null;
            }
            if (!ReferenceEquals(
                    activated.Occurrence.RootBinding,
                    selection.Coordinate.Binding))
            {
                throw new InvalidOperationException(
                    "The activated product occurrence does not match its browser coordinate.");
            }

            return selection;
        }

        public void Dispose()
        {
            try
            {
                _workspace.Dispose();
            }
            finally
            {
                _leases.Dispose();
            }
        }

        static BrowserWorkspaceOccurrenceSnapshot Project(
            InspectionWorkspacePackageOccurrenceView view,
            IReadOnlyList<BrowserPackageCoordinate> coordinates)
        {
            var rows =
                ImmutableArray.CreateBuilder<
                    BrowserWorkspacePackageOccurrence>(
                        view.Occurrences.Length);
            var selections =
                ImmutableDictionary.CreateBuilder<
                    string,
                    BrowserWorkspaceOccurrenceSelection>(
                        StringComparer.Ordinal);
            for (int index = 0; index < view.Occurrences.Length; index++)
            {
                InspectionWorkspacePackageOccurrenceDescriptor descriptor =
                    view.Occurrences[index];
                BrowserPackageCoordinate coordinate = coordinates[index];
                if (!ReferenceEquals(
                        coordinate.Binding,
                        descriptor.Occurrence.RootBinding))
                {
                    throw new InvalidOperationException(
                        "The product occurrence order did not preserve its package Root inputs.");
                }
                string action = Guid.NewGuid().ToString("N");
                rows.Add(
                    new BrowserWorkspacePackageOccurrence(
                        action,
                        descriptor.PackageId,
                        descriptor.Version,
                        descriptor.Framework ?? ""));
                selections.Add(
                    action,
                    new BrowserWorkspaceOccurrenceSelection(
                        descriptor.Action,
                        coordinate));
            }

            return new BrowserWorkspaceOccurrenceSnapshot(
                new BrowserWorkspacePackageOccurrenceView(
                    rows.ToArray(),
                    Superseded: false),
                selections.ToImmutable());
        }
    }
}

}

[SupportedOSPlatform("browser")]
public static partial class InspectionEngine
{
    [JSExport]
    public static void ClearWorkspacePackageOccurrences() =>
        InspectWeb.Engine.BrowserWorkspaceOccurrenceOperations.ClearCurrent();

    [JSExport]
    public static async Task<string> QueryWorkspacePackageOccurrences(
        string workspaceJson)
    {
        InspectWeb.Engine.BrowserWorkspacePackage[] workspace =
            JsonSerializer.Deserialize(
                workspaceJson,
                InspectWeb.Engine.BrowserJsonContext.Default
                    .BrowserWorkspacePackageArray)
            ?? [];
        InspectWeb.Engine.BrowserPackageRequest[] requests =
        [
            .. workspace.Select(entry =>
                new InspectWeb.Engine.BrowserPackageRequest(
                entry.Package,
                entry.Version,
                string.IsNullOrWhiteSpace(entry.Framework)
                    ? null
                    : entry.Framework)),
        ];
        InspectWeb.Engine.BrowserWorkspacePackageOccurrenceView view =
            await InspectWeb.Engine.BrowserWorkspaceOccurrenceOperations
                .QueryAsync(requests);
        return JsonSerializer.Serialize(
            view,
            InspectWeb.Engine.BrowserJsonContext.Default
                .BrowserWorkspacePackageOccurrenceView);
    }

    [JSExport]
    public static string ActivateWorkspacePackageOccurrence(string action)
    {
        InspectWeb.Engine.BrowserWorkspaceOccurrenceSelection? selection =
            InspectWeb.Engine.BrowserWorkspaceOccurrenceOperations
                .Activate(action);
        if (selection is null)
        {
            return JsonSerializer.Serialize(
                new InspectWeb.Engine
                    .BrowserWorkspacePackageOccurrenceActivation(
                    Activated: false,
                    Superseded: true,
                    Package: null),
                InspectWeb.Engine.BrowserJsonContext.Default
                    .BrowserWorkspacePackageOccurrenceActivation);
        }

        InspectWeb.Engine.BrowserInspectionScope scope =
            InspectWeb.Engine.BrowserPackageWorkspace.OpenScope(
                [selection.Coordinate]);
        InspectWeb.Engine.BrowserPackageSurface package =
            ProjectPackageSurface(scope, scope.Coordinate(selection.Coordinate));
        return JsonSerializer.Serialize(
            new InspectWeb.Engine.BrowserWorkspacePackageOccurrenceActivation(
                Activated: true,
                Superseded: false,
                package),
            InspectWeb.Engine.BrowserJsonContext.Default
                .BrowserWorkspacePackageOccurrenceActivation);
    }
}
