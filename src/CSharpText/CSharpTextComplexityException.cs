namespace CSharpText;

/// <summary>
/// The source exceeds a bounded lexical-complexity limit.
/// </summary>
public sealed class CSharpTextComplexityException(int limit, string unit)
    : InvalidOperationException(
        $"C# source exceeds the lexical complexity limit of {limit:N0} {unit}.")
{
    /// <summary>The maximum number of <see cref="Unit"/> values allowed by the failed scan.</summary>
    public int Limit { get; } = limit;

    /// <summary>The lexical unit whose limit was exhausted.</summary>
    public string Unit { get; } = unit;
}
