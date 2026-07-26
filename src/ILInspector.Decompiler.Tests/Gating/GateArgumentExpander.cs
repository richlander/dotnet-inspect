using System.Text;

namespace ILInspector.Decompiler.Tests.Gating;

/// <summary>
/// What the host should do after expanding a <c>--gate</c> request.
/// </summary>
public enum GateOutcome
{
    /// <summary>Run xUnit with <see cref="GateExpansion.Args"/>.</summary>
    Run,

    /// <summary>Print <see cref="GateExpansion.Message"/> to stdout and exit 0.</summary>
    Help,

    /// <summary>Print <see cref="GateExpansion.Message"/> to stderr and exit non-zero.</summary>
    Error,
}

/// <summary>
/// The result of expanding a <c>--gate</c> request into concrete xUnit
/// console-runner arguments.
/// </summary>
public sealed record GateExpansion(GateOutcome Outcome, IReadOnlyList<string> Args, string? Message);

/// <summary>
/// Translates a discoverable <c>--gate &lt;preset&gt;</c> flag into concrete
/// xUnit v3 console-runner arguments. This type carries no project-specific
/// knowledge: callers supply their own preset table, so the same expander can
/// be shared across test executables.
/// </summary>
public static class GateArgumentExpander
{
    /// <summary>The flag callers use to select a preset.</summary>
    public const string Flag = "--gate";

    private static readonly HashSet<string> HelpTokens =
        new(StringComparer.OrdinalIgnoreCase) { "list", "help", "?", "-h", "--help" };

    /// <summary>
    /// Scans <paramref name="args"/> for a single <c>--gate &lt;name&gt;</c>
    /// pair. When present and valid, the pair is removed and the matching
    /// preset's arguments are prepended ahead of the remaining arguments. When
    /// absent, the arguments pass through unchanged.
    /// </summary>
    public static GateExpansion Expand(IReadOnlyList<string> args, IReadOnlyList<GatePreset> presets)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(presets);

        int flagIndex = -1;
        for (int i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], Flag, StringComparison.OrdinalIgnoreCase))
            {
                if (flagIndex >= 0)
                {
                    return new GateExpansion(
                        GateOutcome.Error,
                        Array.Empty<string>(),
                        $"error: {Flag} may be specified only once.\n\n" + RenderTable(presets));
                }

                flagIndex = i;
            }
        }

        if (flagIndex < 0)
        {
            // No gate requested — pass everything through unchanged.
            return new GateExpansion(GateOutcome.Run, args, null);
        }

        string? name = flagIndex + 1 < args.Count ? args[flagIndex + 1] : null;

        if (name is null || HelpTokens.Contains(name))
        {
            return new GateExpansion(GateOutcome.Help, Array.Empty<string>(), RenderTable(presets));
        }

        GatePreset? preset = presets.FirstOrDefault(
            p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (preset is null)
        {
            return new GateExpansion(
                GateOutcome.Error,
                Array.Empty<string>(),
                $"error: unknown gate preset '{name}'.\n\n" + RenderTable(presets));
        }

        // Rebuild the argument list: drop the "--gate <name>" pair, then
        // prepend the preset's arguments ahead of everything else the caller
        // passed (so caller-supplied filters still apply on top).
        var passthrough = new List<string>(args.Count);
        for (int i = 0; i < args.Count; i++)
        {
            if (i == flagIndex || i == flagIndex + 1)
            {
                continue;
            }

            passthrough.Add(args[i]);
        }

        var expanded = new List<string>(preset.Args.Count + passthrough.Count);
        expanded.AddRange(preset.Args);
        expanded.AddRange(passthrough);
        return new GateExpansion(GateOutcome.Run, expanded, null);
    }

    /// <summary>Renders the human-readable preset table shown by <c>--gate list</c>.</summary>
    public static string RenderTable(IReadOnlyList<GatePreset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);

        int width = presets.Count == 0 ? 0 : presets.Max(p => p.Name.Length);
        var sb = new StringBuilder();
        sb.AppendLine($"Usage: {Flag} <preset> [additional xUnit args]");
        sb.AppendLine();
        sb.AppendLine("Presets:");
        foreach (GatePreset preset in presets)
        {
            string filter = preset.Args.Count == 0 ? "(no filter)" : string.Join(' ', preset.Args);
            sb.AppendLine($"  {preset.Name.PadRight(width)}  {preset.Summary}");
            sb.AppendLine($"  {new string(' ', width)}  -> {filter}");
        }

        return sb.ToString().TrimEnd();
    }
}
