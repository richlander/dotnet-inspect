namespace ILInspector.ILDiff;

/// <summary>
/// Experiment-only knobs (branch exp/exists-gap-ablation, never merged): each
/// removes one component of the census cursor's per-advance work so that the
/// gap between lockstep and Finding Diff Any's walk can be attributed
/// (docs/design/finding-census.md, Exists, cost records). Read once from
/// IL_ABLATE as a comma-separated list; absent means the product path.
/// </summary>
internal static class IlAblation
{
    static readonly string Env = Environment.GetEnvironmentVariable("IL_ABLATE") ?? string.Empty;

    /// <summary>Spell each opcode's family once into a table instead of per instruction.</summary>
    public static readonly bool FamilyTable = Has("family-table");

    /// <summary>Do not retain instructions or build the block graph that validates clauses.</summary>
    public static readonly bool NoClauses = Has("no-clauses");

    /// <summary>Skip clause validation and instruction retention only for a body with no exception regions.</summary>
    public static readonly bool SkipEmptyClauses = Has("skip-empty-clauses");

    /// <summary>Validate clauses through the exception-flow topology alone, without building blocks and edges.</summary>
    public static readonly bool TopologyOnly = Has("topology-only");

    /// <summary>Do not materialize value operand identities (immediates, slots, branch and switch targets).</summary>
    public static readonly bool NoImmediates = Has("no-immediates");

    /// <summary>Do not resolve metadata token operands through the side.</summary>
    public static readonly bool NoTokens = Has("no-tokens");

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
