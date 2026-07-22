using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
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

public sealed record RoundTripReferenceProvenance(
    int Ordinal,
    string Display,
    string? Path,
    string? Sha256,
    Guid? ModuleVersionId,
    ImmutableArray<string> Aliases,
    bool EmbedInteropTypes);

public sealed record RoundTripCompilationProvenance(
    string CompilerVersion,
    string LanguageVersion,
    string SourceCodeKind,
    string DocumentationMode,
    ImmutableArray<string> ParseFeatures,
    string OutputKind,
    string OptimizationLevel,
    string Platform,
    bool CheckOverflow,
    bool AllowUnsafe,
    string NullableContextOptions,
    bool Deterministic,
    ImmutableArray<RoundTripReferenceProvenance> References)
{
    public bool HasExactReferenceContent
        => References.All(reference => reference.Sha256 is not null);
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
    string? StopReason,
    RoundTripCompilationProvenance Provenance)
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
        var frozenReferences = FreezeReferences(references, out var referenceProvenance);
        var provenance = CreateProvenance(referenceProvenance, parseOptions, compilationOptions);

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
                frozenReferences,
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
                    StopReason: null,
                    provenance);
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
                    growth.StopReason ?? "closure-stalled",
                    provenance);
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
            StopReason: "closure-iteration-budget",
            provenance);
    }

    static RoundTripCompilationProvenance CreateProvenance(
        ImmutableArray<RoundTripReferenceProvenance> references,
        CSharpParseOptions parseOptions,
        CSharpCompilationOptions compilationOptions)
        => new(
            typeof(CSharpCompilation).Assembly.GetName().Version?.ToString() ?? "unknown",
            parseOptions.LanguageVersion.ToString(),
            parseOptions.Kind.ToString(),
            parseOptions.DocumentationMode.ToString(),
            parseOptions.Features
                .OrderBy(feature => feature.Key, StringComparer.Ordinal)
                .Select(feature => $"{feature.Key}={feature.Value}")
                .ToImmutableArray(),
            compilationOptions.OutputKind.ToString(),
            compilationOptions.OptimizationLevel.ToString(),
            compilationOptions.Platform.ToString(),
            compilationOptions.CheckOverflow,
            compilationOptions.AllowUnsafe,
            compilationOptions.NullableContextOptions.ToString(),
            compilationOptions.Deterministic,
            references);

    static ImmutableArray<MetadataReference> FreezeReferences(
        IReadOnlyList<MetadataReference> references,
        out ImmutableArray<RoundTripReferenceProvenance> provenance)
    {
        var frozen = ImmutableArray.CreateBuilder<MetadataReference>(references.Count);
        var rows = ImmutableArray.CreateBuilder<RoundTripReferenceProvenance>(references.Count);
        for (int ordinal = 0; ordinal < references.Count; ordinal++)
        {
            var reference = references[ordinal];
            string? path = (reference as PortableExecutableReference)?.FilePath;
            string? hash = null;
            Guid? mvid = null;
            MetadataReference frozenReference = reference;
            if (path is { Length: > 0 } && File.Exists(path))
            {
                byte[] image = File.ReadAllBytes(path);
                hash = Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();
                using var pe = new PEReader(ImmutableArray.Create(image));
                if (pe.HasMetadata)
                {
                    var reader = pe.GetMetadataReader();
                    mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
                }
                frozenReference = MetadataReference.CreateFromImage(
                    ImmutableArray.Create(image),
                    reference.Properties,
                    filePath: path);
            }
            frozen.Add(frozenReference);
            rows.Add(new RoundTripReferenceProvenance(
                ordinal,
                reference.Display ?? path ?? $"reference:{ordinal}",
                path is null ? null : System.IO.Path.GetFullPath(path),
                hash,
                mvid,
                reference.Properties.Aliases.ToImmutableArray(),
                reference.Properties.EmbedInteropTypes));
        }
        provenance = rows.ToImmutable();
        return frozen.ToImmutable();
    }
}
