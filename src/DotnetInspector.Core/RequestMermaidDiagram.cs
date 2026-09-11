using System.Text;
using DotnetInspector.Networking;

namespace DotnetInspector.Core;

public sealed class RequestMermaidDiagram :
    IObserver<NetworkRequestObservation>,
    IObserver<CacheObservation>,
    IObserver<BreadcrumbObservation>,
    IDisposable
{
    private readonly object _gate = new();
    private readonly List<string> _nodes = [];
    private readonly HashSet<string> _cacheHitsShown = new(StringComparer.Ordinal);
    private readonly HashSet<string> _cacheStoresShown = new(StringComparer.Ordinal);
    private readonly IDisposable _networkSubscription;
    private readonly IDisposable _cacheSubscription;
    private readonly IDisposable _breadcrumbSubscription;

    private RequestMermaidDiagram()
    {
        _networkSubscription = NetworkTelemetry.Subscribe(
            (IObserver<NetworkRequestObservation>)this);
        _cacheSubscription = CacheTelemetry.Subscribe(this);
        _breadcrumbSubscription = BreadcrumbTelemetry.Subscribe(this);
    }

    public static RequestMermaidDiagram Start() => new();

    public void OnNext(NetworkRequestObservation observation)
    {
        lock (_gate)
        {
            _nodes.Add(
                $"{observation.TrafficKind.ToTelemetryName()}<br/>{observation.Method} {observation.Url}");
        }
    }

    public void OnNext(CacheObservation observation)
    {
        lock (_gate)
        {
            var cacheKey = $"{observation.Category}\0{observation.Key}";
            if (observation.Result == CacheAccessResult.Store)
                _cacheStoresShown.Add(cacheKey);

            if (observation.Result == CacheAccessResult.Hit)
            {
                if (_cacheStoresShown.Contains(cacheKey)
                    || !_cacheHitsShown.Add(cacheKey))
                {
                    return;
                }
            }

            _nodes.Add(
                $"cache {observation.Result.ToTelemetryName()}<br/>{observation.Category} {observation.Key}");
        }
    }

    public void OnNext(BreadcrumbObservation observation)
    {
        lock (_gate)
        {
            _nodes.Add($"{observation.Stage}<br/>{observation.Detail}");
        }
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void Dispose()
    {
        _networkSubscription.Dispose();
        _cacheSubscription.Dispose();
        _breadcrumbSubscription.Dispose();
    }

    public void WriteTo(TextWriter writer, Func<string, string> containLabel)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(containLabel);
        writer.Write(ToMermaid(containLabel));
    }

    public string ToMermaid(Func<string, string> containLabel)
    {
        ArgumentNullException.ThrowIfNull(containLabel);

        List<string> nodes;
        lock (_gate)
        {
            nodes = [.. _nodes];
        }

        var builder = new StringBuilder();
        builder.AppendLine("flowchart TD");
        builder.AppendLine("  n0[\"dotnet-inspect\"]");

        if (nodes.Count == 0)
        {
            builder.AppendLine(
                "  n0 --> n1[\"no managed HTTP/cache activity observed\"]");
            return builder.ToString();
        }

        var previous = "n0";
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = $"n{i + 1}";
            builder.AppendLine(
                $"  {node}[\"{EscapeMermaidLabel(containLabel(nodes[i]))}\"]");
            builder.AppendLine($"  {previous} --> {node}");
            previous = node;
        }

        return builder.ToString();
    }

    private static string EscapeMermaidLabel(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
