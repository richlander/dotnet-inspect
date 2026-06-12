namespace DotnetInspector.Decompiler;

/// <summary>
/// One analysis, many projections (docs/decompiler-pipeline.md): computes
/// each pipeline stage for a method exactly once and hands the results to
/// every consumer — the C# emitter, the annotated IL emitter, stage dumps,
/// and any future front end. Stages are computed on first use, in pipeline
/// order; the IL projections consume only the pre-transform stages
/// (<see cref="Cfg"/>, <see cref="Stack"/>), so the IL views stay ground
/// truth even after raising transforms rewrite the <see cref="Ast"/>.
/// Not thread-safe; analyze per method, per thread.
/// </summary>
public sealed class MethodAnalysis
{
    ControlFlowGraph? _cfg;
    StackSimulationResult? _stack;
    ILAstMethod? _ast;
    Transforms.TransformContext? _transforms;
    StructuredControlFlow? _structure;

    MethodAnalysis(MethodBodyContext context) => Context = context;

    public static MethodAnalysis Create(MethodBodyContext context) => new(context);

    public MethodBodyContext Context { get; }

    public ControlFlowGraph Cfg => _cfg ??= ControlFlowGraph.Create(Context);

    public StackSimulationResult Stack => _stack ??= StackSimulator.Simulate(Context, Cfg);

    /// <summary>The IL AST after the raising transform pipeline has run.</summary>
    public ILAstMethod Ast
    {
        get
        {
            EnsureAstAndTransforms();
            return _ast!;
        }
    }

    /// <summary>Results of the transform pipeline (inlined locals, etc.).</summary>
    public Transforms.TransformContext TransformResults
    {
        get
        {
            EnsureAstAndTransforms();
            return _transforms!;
        }
    }

    public StructuredControlFlow Structure
        => _structure ??= StructuredControlFlow.Analyze(Context, Cfg, Ast);

    void EnsureAstAndTransforms()
    {
        if (_ast is not null)
            return;
        var ast = ILAstBuilder.Build(Context, Cfg, Stack);
        // Transforms mutate the AST in place; build and run as one step so
        // no consumer can observe the untransformed tree through this property.
        _transforms = Transforms.TransformPipeline.Run(ast);
        _ast = ast;
    }
}
