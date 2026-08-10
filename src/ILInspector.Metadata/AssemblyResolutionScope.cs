namespace ILInspector.Metadata;

/// <summary>
/// Where a caller allows an assembly identity to resolve from.
/// </summary>
/// <remarks>
/// <see cref="Platform"/> asserts the reference is a runtime/framework
/// assembly, so policy may resolve it only from platform/framework sources,
/// never a confusable local copy. <see cref="Any"/> places no source
/// constraint.
/// </remarks>
public enum AssemblyResolutionScope
{
    Any,
    Platform,
}
