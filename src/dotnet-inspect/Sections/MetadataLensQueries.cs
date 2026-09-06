using DotnetInspector.Models;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

internal static class MetadataLensQueries
{
    internal static InspectionQuery<ReadyToRunInspection> ReadyToRun { get; } =
        new("ReadyToRun image", InspectionCost.NetworkFree);

    internal static MetadataImageResult Image(
        AssemblyInspectionSession session,
        LibraryInspection model)
    {
        if (model.RequestedMetadataRoot is not { } kind)
            return MetadataImageQuery.Execute(session);

        try
        {
            model.MetadataRoot = session.MetadataRoot(kind);
            return model.MetadataRoot is { } root
                ? new MetadataImageResult.Available(root.Image())
                : new MetadataImageResult.Failed(new InvalidOperationException(
                    $"The requested {kind} metadata root is absent."));
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or NotSupportedException)
        {
            return new MetadataImageResult.Failed(ex);
        }
    }

    internal static ReadyToRunInspection InspectReadyToRun(AssemblyInspectionSession session)
    {
        try
        {
            return session.ReadyToRunImage() is { } overview
                ? new ReadyToRunInspection.Available(overview)
                : new ReadyToRunInspection.Absent();
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException)
        {
            return new ReadyToRunInspection.Failed(ex);
        }
    }
}
