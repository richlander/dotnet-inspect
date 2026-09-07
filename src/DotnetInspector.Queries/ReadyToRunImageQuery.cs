using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>The typed result of ReadyToRun image inspection.</summary>
public abstract record ReadyToRunImageResult
{
    private ReadyToRunImageResult()
    {
    }

    /// <summary>The image contains a validated ReadyToRun envelope.</summary>
    public sealed record Available(ReadyToRunImageOverview Overview)
        : ReadyToRunImageResult;

    /// <summary>The image has no canonical ReadyToRun advertisement.</summary>
    public sealed record NotReadyToRun : ReadyToRunImageResult;

    /// <summary>ReadyToRun inspection failed after encountering advertised structure.</summary>
    public sealed record Failed(Exception Error) : ReadyToRunImageResult;
}

/// <summary>Produces the ReadyToRun envelope from an already-open assembly session.</summary>
public static class ReadyToRunImageQuery
{
    public static InspectionQuery<ReadyToRunImageResult> Definition { get; } =
        new("ReadyToRun image", InspectionCost.NetworkFree);

    public static ReadyToRunImageResult Execute(AssemblyInspectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            return session.ReadyToRunImage() is { } overview
                ? new ReadyToRunImageResult.Available(overview)
                : new ReadyToRunImageResult.NotReadyToRun();
        }
        catch (Exception ex)
        {
            return new ReadyToRunImageResult.Failed(ex);
        }
    }
}
