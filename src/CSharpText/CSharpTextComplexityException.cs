namespace CSharpText;

/// <summary>
/// The source exceeds a bounded lexical-complexity limit.
/// </summary>
public sealed class CSharpTextComplexityException(int maxTokenCount)
    : InvalidOperationException(
        $"C# source exceeds the lexical complexity limit of {maxTokenCount:N0} tokens.")
{
    /// <summary>The maximum retained token count allowed by the failed scan.</summary>
    public int MaxTokenCount { get; } = maxTokenCount;
}
