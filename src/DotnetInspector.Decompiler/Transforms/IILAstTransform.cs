namespace DotnetInspector.Decompiler.Transforms;

/// <summary>
/// A transformation over the ILAst, run between <see cref="ILAstBuilder"/> and
/// emission. Passes rewrite the tree toward source-level constructs so the
/// emitter prints structure instead of recognizing patterns mid-print.
/// </summary>
public interface IILAstTransform
{
    void Run(ILAstMethod method, TransformContext context);
}

/// <summary>
/// Shared state for a pipeline run: facts passes establish that emission
/// consumes (e.g. locals whose declarations are no longer needed).
/// </summary>
public sealed class TransformContext
{
    /// <summary>Locals eliminated by inlining; their declarations are suppressed.</summary>
    public HashSet<string> InlinedLocals { get; } = [];
}

/// <summary>The standard transform pipeline.</summary>
public static class TransformPipeline
{
    public static TransformContext Run(ILAstMethod method)
    {
        var context = new TransformContext();
        IILAstTransform[] passes =
        [
            new ExpressionInliner(),
            new StoreReturnMergeTransform(),
        ];
        foreach (var pass in passes)
            pass.Run(method, context);
        return context;
    }
}
