using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The per-run context threaded through every pass — ILSpy's
/// <c>ILTransformContext</c> in this codebase's vocabulary. A pass receives one
/// and records its rewrites through <see cref="Stepper"/>. The type is the seam
/// for the dataflow facts and diagnostics sink the IR design calls for
/// (docs/decompiler-ir.md); today it carries the stepper and the cross-method
/// import seam.
/// </summary>
public sealed class PassContext
{
    const int MaxCrossMethodPipelineDepth = 64;
    readonly List<string> _activeCrossMethodPipelines = [];

    public PassContext(
        Stepper stepper,
        StructuringDiagnostics? structuringDiagnostics = null,
        Func<MethodRef, IrFunction?>? importMethodBody = null)
    {
        Stepper = stepper;
        StructuringDiagnostics = structuringDiagnostics;
        ImportMethodBody = importMethodBody;
    }

    public Stepper Stepper { get; }

    /// <summary>
    /// Optional sink for <see cref="StructuringPass"/> stop reasons. Null on every
    /// normal run (the pass records nothing); set only by the <c>--structuring-stops</c>
    /// diagnostic to make the common-exit docket reproducible.
    /// </summary>
    public StructuringDiagnostics? StructuringDiagnostics { get; }

    /// <summary>
    /// The cross-method import seam: imports a sibling method's freshly-imported
    /// (un-raised) body by reference, or null when the method is absent. The
    /// pipeline is otherwise per-method — a pass sees only its one function — so
    /// this is the single sanctioned way for a pass to reach another body.
    /// Cross-method passes use it to pull in synthesized companion methods
    /// (lambdas, local functions, state machines). Null on runs that wired no
    /// source (stage dumps, direct pass tests, the lowered view); a pass that
    /// needs it must no-op when it is null. Passes that run a nested pipeline over
    /// the imported body must use the guarded helpers below rather than invoking
    /// this delegate directly.
    /// </summary>
    public Func<MethodRef, IrFunction?>? ImportMethodBody { get; }

    /// <summary>
    /// Imports a sibling body and runs a pass pipeline over it while marking that
    /// method active. Cross-method raising is allowed to nest, but recursive or
    /// pathologically deep sibling chains decline so one method cannot re-enter
    /// the whole pipeline indefinitely.
    /// </summary>
    public bool TryImportAndRunMethodBody(MethodRef method, ImmutableArray<IIrPass> passes, out IrFunction? body)
    {
        body = null;
        if (!TryEnterCrossMethodPipeline(method, out var scope))
            return false;
        using (scope)
        {
            body = scope.Import();
            if (body is not null)
                IrPasses.Run(body, passes, this);
            return true;
        }
    }

    /// <summary>
    /// Marks a sibling method active across any raw inspection and nested pass run
    /// the caller needs. Use when a pass must inspect the imported body before
    /// choosing which pipeline, if any, to run.
    /// </summary>
    public bool TryEnterCrossMethodPipeline(MethodRef method, out CrossMethodPipelineScope scope)
    {
        scope = null!;
        if (ImportMethodBody is null)
            return false;

        var key = CrossMethodPipelineKey(method);
        if (_activeCrossMethodPipelines.Contains(key)
            || _activeCrossMethodPipelines.Count >= MaxCrossMethodPipelineDepth)
            return false;

        _activeCrossMethodPipelines.Add(key);
        scope = new CrossMethodPipelineScope(this, method, key);
        return true;
    }

    void ExitCrossMethodPipeline(string key)
    {
        var last = _activeCrossMethodPipelines.Count - 1;
        if (last >= 0 && _activeCrossMethodPipelines[last] == key)
            _activeCrossMethodPipelines.RemoveAt(last);
        else
            _activeCrossMethodPipelines.Remove(key);
    }

    public sealed class CrossMethodPipelineScope : IDisposable
    {
        readonly PassContext _context;
        readonly MethodRef _method;
        readonly string _key;
        bool _disposed;

        internal CrossMethodPipelineScope(PassContext context, MethodRef method, string key)
        {
            _context = context;
            _method = method;
            _key = key;
        }

        public IrFunction? Import() => _context.ImportMethodBody!(_method);

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _context.ExitCrossMethodPipeline(_key);
        }
    }

    static string CrossMethodPipelineKey(MethodRef method)
    {
        var parameters = string.Join(",", method.ParameterTypes.Select(static p => p.ToDisplayString()));
        var typeArguments = method.TypeArguments.IsDefaultOrEmpty
            ? ""
            : $"<{string.Join(",", method.TypeArguments.Select(static t => t.ToDisplayString()))}>";
        var receiver = method.HasThis ? "instance" : "static";
        return $"{method.DeclaringType.ToDisplayString()}::{method.Name}{typeArguments}({parameters})->{method.ReturnType.ToDisplayString()}:{receiver}";
    }

    /// <summary>A context with stepping disabled — the default for normal runs and for tests that drive a pass directly.</summary>
    public static PassContext None { get; } = new(new Stepper(enabled: false));
}
