using System.Collections.Immutable;
using ILInspector.Analysis;

namespace DotnetInspector.PackageQueries;

public enum PackageAssemblyPatternRole
{
    CompileSurface,
    ImplementationBody,
}

public sealed record PackageAssemblyPatternDescriptor(
    string Id,
    string Label,
    string Summary,
    PackageAssemblyPatternRole Role,
    string SemanticProducerId,
    int MaximumOperandLength);

public sealed class PackageAssemblyPatternRequest
{
    internal PackageAssemblyPatternRequest(
        PackageAssemblyPatternDescriptor pattern,
        StringLiteralUseOperand operand)
    {
        Pattern = pattern;
        Operand = operand;
    }

    public PackageAssemblyPatternDescriptor Pattern { get; }
    public StringLiteralUseOperand Operand { get; }
}

public static class PackageAssemblyPatterns
{
    public const string StringLiteralContains = "il-string-literal-contains";

    static readonly PackageAssemblyPatternDescriptor LiteralPattern = new(
        StringLiteralContains,
        "IL string literal contains",
        "Find an ordinal substring in decoded ldstr occurrences in the selector-issued primary implementation assembly.",
        PackageAssemblyPatternRole.ImplementationBody,
        StringLiteralUsePatternAnalysis.ProducerId,
        StringLiteralUseOperand.MaximumLength);

    public static ImmutableArray<PackageAssemblyPatternDescriptor> Descriptors { get; } =
        [LiteralPattern];

    public static PackageAssemblyPatternRequest CreateRequest(
        string patternId,
        string operand)
    {
        ArgumentNullException.ThrowIfNull(patternId);
        ArgumentNullException.ThrowIfNull(operand);
        if (patternId != StringLiteralContains)
        {
            throw new ArgumentException(
                "The assembly pattern is not registered.",
                nameof(patternId));
        }
        return new(LiteralPattern, StringLiteralUseOperand.Create(operand));
    }
}

public sealed class PackageAssemblyEvaluationBudget
{
    public static PackageAssemblyEvaluationBudget Default { get; } = new(
        maximumEntryBytes: 16L * 1024 * 1024,
        maximumRetainedImageBytes: 32L * 1024 * 1024,
        StringLiteralUsePatternBudget.Default,
        TimeSpan.FromMinutes(1));

    public PackageAssemblyEvaluationBudget(
        long maximumEntryBytes,
        long maximumRetainedImageBytes,
        StringLiteralUsePatternBudget semanticBudget,
        TimeSpan maximumDuration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntryBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumEntryBytes, Array.MaxLength);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRetainedImageBytes, 2);
        ArgumentNullException.ThrowIfNull(semanticBudget);
        if (maximumDuration < TimeSpan.FromMilliseconds(1)
            || maximumDuration > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDuration),
                "An assembly evaluation deadline must be between one millisecond and five minutes.");
        }

        MaximumEntryBytes = maximumEntryBytes;
        MaximumRetainedImageBytes = maximumRetainedImageBytes;
        SemanticBudget = semanticBudget;
        MaximumDuration = maximumDuration;
    }

    public long MaximumEntryBytes { get; }
    public long MaximumRetainedImageBytes { get; }
    public StringLiteralUsePatternBudget SemanticBudget { get; }
    public TimeSpan MaximumDuration { get; }
}
