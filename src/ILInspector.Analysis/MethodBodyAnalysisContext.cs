using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

/// <summary>
/// Shared per-method inputs produced once by the assembly analysis pipeline.
/// Topic-specific producers build their own interpretation over the common
/// Layer-0 instructions and blocks.
/// </summary>
internal sealed record MethodBodyAnalysisContext(
    MethodIdentity Method,
    MethodInstructions Instructions,
    ImmutableArray<ExceptionRegion> ExceptionRegions,
    IReadOnlyList<(int Start, int End)> LoopRegions,
    ImmutableArray<TypeRef> LocalTypes)
{
    ImmutableArray<TypeRef> _localTypes =
        LocalTypes.IsDefault ? [] : LocalTypes;

    public ImmutableArray<TypeRef> LocalTypes
    {
        get => _localTypes;
        init => _localTypes = value.IsDefault ? [] : value;
    }
}
