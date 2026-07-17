using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// A copied method body that carries no reader-backed state.
/// </summary>
public sealed record MethodBodyData(
    ImmutableArray<byte> IL,
    ImmutableArray<ExceptionRegion> ExceptionRegions);
