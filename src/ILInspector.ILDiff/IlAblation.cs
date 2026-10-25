namespace ILInspector.ILDiff;

/// <summary>
/// Experiment-only knobs (branch exp/exists-gap-ablation-2, never merged):
/// each removes one component of the repaired census cursor's per-advance
/// work, read once from IL_ABLATE as a comma-separated list.
/// </summary>
internal static class IlAblation
{
    static readonly string Env = Environment.GetEnvironmentVariable("IL_ABLATE") ?? string.Empty;

    /// <summary>Do not materialize value operand identities (immediates, slots, branch and switch targets).</summary>
    public static readonly bool NoImmediates = Has("no-immediates");

    /// <summary>Do not resolve metadata token operands through the side.</summary>
    public static readonly bool NoTokens = Has("no-tokens");

    /// <summary>Do not build per-atom target and target-index arrays.</summary>
    public static readonly bool NoTargets = Has("no-targets");

    /// <summary>Resolve token operands but treat any two as equal, so their comparison is not paid.</summary>
    public static readonly bool NoTokenCompare = Has("no-token-compare");

    /// <summary>Identify a user string by a shared placeholder instead of its formatted text.</summary>
    public static readonly bool NoUserStringText = Has("no-ustring");

    /// <summary>Skip the per-token admission checks (operand kind, compiler-controlled, duplicate Type row).</summary>
    public static readonly bool NoTokenChecks = Has("no-checks");

    /// <summary>Skip spelling the token's Symbol Key; identify it by a shared placeholder.</summary>
    public static readonly bool NoTokenSpelling = Has("no-spell");

    static bool Has(string name)
    {
        foreach (string part in Env.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(part, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
