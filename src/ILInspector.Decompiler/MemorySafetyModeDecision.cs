using ILInspector.Metadata;

namespace ILInspector.Decompiler;

/// <summary>
/// The C# memory-safety language model that rendering and compiler replay can
/// select when the defining module's normalized rules are recognized.
/// </summary>
public enum MemorySafetyMode
{
    Legacy,
    Updated,
}

/// <summary>
/// Converts Metadata's normalized module rules into the one language-mode
/// decision shared by the Decompiler and compile-back consumers.
/// </summary>
public abstract record MemorySafetyModeDecision
{
    public sealed record Available(MemorySafetyMode Mode)
        : MemorySafetyModeDecision;

    public sealed record Unavailable(MemorySafetyRulesResult Rules)
        : MemorySafetyModeDecision;

    public static Available Legacy { get; } = new(MemorySafetyMode.Legacy);

    public static Available Updated { get; } = new(MemorySafetyMode.Updated);

    public bool UsesUpdatedRules
        => this is Available { Mode: MemorySafetyMode.Updated };

    public static MemorySafetyModeDecision Resolve(
        MemorySafetyRulesResult rules,
        bool simulateUpdatedRules = false)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (simulateUpdatedRules)
            return Updated;

        return rules switch
        {
            MemorySafetyRulesResult.Available
            {
                State: MemorySafetyRulesState.Legacy,
            } => Legacy,
            MemorySafetyRulesResult.Available
            {
                State: MemorySafetyRulesState.Updated,
            } => Updated,
            _ => new Unavailable(rules),
        };
    }

    public static string DescribeUnavailable(MemorySafetyRulesResult rules)
        => rules switch
        {
            MemorySafetyRulesResult.Available available
                => $"module memory-safety rules are {available.State}",
            MemorySafetyRulesResult.Unavailable unavailable
                => "module memory-safety rules are unavailable: "
                    + $"{unavailable.Failure.Kind}: {unavailable.Failure.Detail}",
            _ => "module memory-safety rules are unavailable",
        };
}
