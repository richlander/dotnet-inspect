namespace DotnetInspect.Cli.CommandLine;

/// <summary>One routing decision the bare-input router made.</summary>
internal sealed record RouterDecision(string Stage, string Detail);

/// <summary>
/// Records the bare-input router's decisions for a caller that asked to
/// capture them in its own async flow. Nothing is recorded, retained, or
/// delivered unless a capture is active; the CLI harness is its consumer, and
/// no host gesture exposes it.
/// </summary>
internal static class RouterDecisionLog
{
    private static readonly AsyncLocal<Capture?> Current = new();

    /// <summary>Captures the decisions made in the current async flow until disposed.</summary>
    public static Capture Begin()
    {
        var capture = new Capture(Current.Value);
        Current.Value = capture;
        return capture;
    }

    public static void Record(string stage, string detail) =>
        Current.Value?.Add(new RouterDecision(stage, detail));

    internal sealed class Capture(Capture? previous) : IDisposable
    {
        private readonly List<RouterDecision> _decisions = [];

        public IReadOnlyList<RouterDecision> Decisions
        {
            get
            {
                lock (_decisions)
                    return [.. _decisions];
            }
        }

        internal void Add(RouterDecision decision)
        {
            lock (_decisions)
                _decisions.Add(decision);
        }

        public void Dispose() => Current.Value = previous;
    }
}
