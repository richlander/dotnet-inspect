using ILInspector.Metadata;

namespace DotnetInspector.Services;

/// <summary>
/// Merges the API surfaces of a Library population into one endpoint surface,
/// in population order. Local assembly sets and package endpoints share it, so
/// one population yields one merged surface whatever supplied its assemblies.
/// </summary>
public static class ApiSurfacePopulation
{
    /// <summary>Adds one participant's surface, labeled with its source.</summary>
    public static void Merge(
        ApiSurface merged,
        ApiSurface surface,
        string source,
        Action<string>? log)
    {
        ArgumentNullException.ThrowIfNull(merged);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentException.ThrowIfNullOrEmpty(source);
        surface.SetInspectionSourceAssemblyPath(source);
        log?.Invoke(
            $"  + {Path.GetFileNameWithoutExtension(source)}: "
                + $"{surface.PublicTypeCount} types");
        merged.Types.AddRange(surface.Types);
        merged.TypeForwarders.AddRange(surface.TypeForwarders);
        merged.IsTypeForwardingAssembly |=
            surface.IsTypeForwardingAssembly;
        merged.MergeInspectionFailuresFrom(surface);
        merged.PublicTypeCount += surface.PublicTypeCount;
        merged.PublicMethodCount += surface.PublicMethodCount;
        merged.PublicPropertyCount += surface.PublicPropertyCount;
        merged.PublicEventCount += surface.PublicEventCount;
        merged.PublicFieldCount += surface.PublicFieldCount;
    }

    /// <summary>Orders the merged types by full name, stably across participants.</summary>
    public static void Complete(ApiSurface merged)
    {
        ArgumentNullException.ThrowIfNull(merged);
        merged.Types = merged.Types
            .OrderBy(static type => type.FullName)
            .ToList();
    }
}
