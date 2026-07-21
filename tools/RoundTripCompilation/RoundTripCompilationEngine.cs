using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotnetInspector.RoundTripCompilation;

public enum RoundTripCompilationStatus
{
    Succeeded,
    Stopped,
    IterationBudget,
}

public sealed record RoundTripCompilationOptions
{
    public int MaxIterations { get; init; } = 80;

    public string AssemblyName { get; init; } = "round-trip-artifact";
}

public readonly record struct RoundTripGrowthResult(bool Grew, string? StopReason)
{
    public static RoundTripGrowthResult Continue { get; } = new(true, null);

    public static RoundTripGrowthResult Stop(string reason) => new(false, reason);
}

public sealed record RoundTripCompilationResult<TArtifact>(
    RoundTripCompilationStatus Status,
    TArtifact Artifact,
    int Attempts,
    ImmutableArray<Diagnostic> Diagnostics,
    Diagnostic? FirstError,
    byte[]? PeImage,
    string? StopReason)
{
    public bool Succeeded => Status == RoundTripCompilationStatus.Succeeded;
}

/// <summary>
/// Bounded tools-side orchestration for compiler-driven artifact closure. The
/// caller owns artifact production and growth policy; this engine owns only the
/// compose, parse, compile, emit, feedback, and retry lifecycle.
/// </summary>
public static class RoundTripCompilationEngine
{
    public static RoundTripCompilationResult<TArtifact> Compile<TArtifact>(
        Func<TArtifact> compose,
        Func<TArtifact, string> source,
        IReadOnlyList<MetadataReference> references,
        CSharpParseOptions parseOptions,
        CSharpCompilationOptions compilationOptions,
        Func<TArtifact, ImmutableArray<Diagnostic>, SemanticModel, RoundTripGrowthResult> grow,
        RoundTripCompilationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(compose);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(parseOptions);
        ArgumentNullException.ThrowIfNull(compilationOptions);
        ArgumentNullException.ThrowIfNull(grow);
        options ??= new RoundTripCompilationOptions();
        if (options.MaxIterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxIterations must be positive.");
        if (string.IsNullOrWhiteSpace(options.AssemblyName))
            throw new ArgumentException("AssemblyName must be non-empty.", nameof(options));

        Diagnostic? firstError = null;
        ImmutableArray<Diagnostic> lastDiagnostics = [];

        for (int attempt = 1; attempt <= options.MaxIterations; attempt++)
        {
            var artifact = compose();
            string unit = source(artifact)
                ?? throw new InvalidOperationException("Artifact source cannot be null.");
            var tree = CSharpSyntaxTree.ParseText(unit, parseOptions);
            var compilation = CSharpCompilation.Create(
                options.AssemblyName,
                [tree],
                references,
                compilationOptions);
            using var output = new MemoryStream();
            var emit = compilation.Emit(output);
            lastDiagnostics = emit.Diagnostics;
            if (emit.Success)
            {
                return new RoundTripCompilationResult<TArtifact>(
                    RoundTripCompilationStatus.Succeeded,
                    artifact,
                    attempt,
                    emit.Diagnostics,
                    firstError,
                    output.ToArray(),
                    StopReason: null);
            }

            var errors = emit.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();
            firstError ??= errors.FirstOrDefault();
            var growth = grow(artifact, errors, compilation.GetSemanticModel(tree));
            if (!growth.Grew)
            {
                return new RoundTripCompilationResult<TArtifact>(
                    RoundTripCompilationStatus.Stopped,
                    artifact,
                    attempt,
                    emit.Diagnostics,
                    firstError,
                    PeImage: null,
                    growth.StopReason ?? "closure-stalled");
            }
        }

        var finalArtifact = compose();
        return new RoundTripCompilationResult<TArtifact>(
            RoundTripCompilationStatus.IterationBudget,
            finalArtifact,
            options.MaxIterations,
            lastDiagnostics,
            firstError,
            PeImage: null,
            StopReason: "closure-iteration-budget");
    }
}
