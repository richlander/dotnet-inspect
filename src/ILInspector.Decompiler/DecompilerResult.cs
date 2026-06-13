namespace ILInspector.Decompiler;

/// <summary>
/// Fidelity of a decompilation result, ordered from worst to best. The
/// pipeline-replacement shipping plan routes on these levels
/// (docs/decompiler-pipeline.md): the current pipeline reports
/// <see cref="Full"/> or <see cref="Failed"/>; the intermediate levels exist
/// for the replacement pipeline's honest degradation.
/// </summary>
public enum DecompilationFidelity
{
    /// <summary>No output could be produced.</summary>
    Failed,

    /// <summary>No C# rendering; IL projections are still available.</summary>
    IlOnly,

    /// <summary>Structured control flow over low-level expressions.</summary>
    StructuredOnly,

    /// <summary>C# containing explicit unrepresentable nodes.</summary>
    Partial,

    /// <summary>Every construct raised; representable C#.</summary>
    Full,
}

/// <summary>
/// A diagnostic with a stable machine-readable identifier. Identifiers drive
/// fallback routing and CI triage, so they are stable across releases; the
/// message is for humans and carries no contract.
/// </summary>
public readonly record struct DecompilerDiagnostic(string Id, string Message)
{
    public override string ToString() => $"{Id}: {Message}";
}

/// <summary>Stable diagnostic identifiers. Never renumber or reuse.</summary>
public static class DiagnosticIds
{
    /// <summary>The pipeline threw while decompiling.</summary>
    public const string InternalError = "DEC0001";

    /// <summary>A method body context could not be created.</summary>
    public const string ContextUnavailable = "DEC0002";

    /// <summary>A projection that always has output for a method with a body produced none (e.g. IL views). Body-only C# projections legitimately render empty bodies and do not use this.</summary>
    public const string EmptyOutput = "DEC0003";

    /// <summary>IL the pipeline does not represent; rendered explicitly, lowers fidelity.</summary>
    public const string UnsupportedConstruct = "DEC0004";
}

/// <summary>
/// The result of a decompilation: output, diagnostics, and a fidelity level.
/// The Roslyn-shaped contract (<c>Compilation.Emit</c> returns an
/// <c>EmitResult</c>): failures are values, never silently swallowed and
/// never forced on callers as exceptions.
/// </summary>
public sealed record DecompilerResult(
    string? Output,
    DecompilationFidelity Fidelity,
    IReadOnlyList<DecompilerDiagnostic> Diagnostics)
{
    public bool Succeeded => Output is not null;

    public static DecompilerResult Success(string output)
        => new(output, DecompilationFidelity.Full, []);

    public static DecompilerResult Failure(string diagnosticId, string message)
        => new(null, DecompilationFidelity.Failed, [new DecompilerDiagnostic(diagnosticId, message)]);

    /// <summary>
    /// Runs a pipeline entry point, converting exceptions into diagnostics.
    /// Empty-output policy is projection-specific: IL projections always have
    /// output for a real body, so emptiness is a failure there; a body-only
    /// C# projection legitimately renders an empty body (e.g. <c>void M() { }</c>).
    /// </summary>
    internal static DecompilerResult Run(Func<string> pipeline, bool emptyOutputIsFailure = false)
    {
        string output;
        try
        {
            output = pipeline();
        }
        catch (Exception ex)
        {
            return Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
        return emptyOutputIsFailure && string.IsNullOrWhiteSpace(output)
            ? Failure(DiagnosticIds.EmptyOutput, "projection produced no output for a method with a body")
            : Success(output);
    }
}
