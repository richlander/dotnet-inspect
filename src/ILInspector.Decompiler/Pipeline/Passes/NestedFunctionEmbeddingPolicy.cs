namespace ILInspector.Decompiler.Pipeline;

internal static class NestedFunctionEmbeddingPolicy
{
    internal static bool CanEmbed(IrFunction body)
        => !body.RequiresAsyncBodyModifier
        && body.ClassicAsyncStageResult
            is not ClassicAsyncStageResult.Failed
        && !body.Descendants
            .OfType<UnsupportedNode>()
            .Any();
}
