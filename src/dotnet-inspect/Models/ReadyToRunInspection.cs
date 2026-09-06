using ILInspector.Metadata;

namespace DotnetInspector.Models;

public abstract record ReadyToRunInspection
{
    private ReadyToRunInspection() { }

    public sealed record Available(ReadyToRunImageOverview Overview) : ReadyToRunInspection;
    public sealed record Absent : ReadyToRunInspection;
    public sealed record Failed(Exception Error) : ReadyToRunInspection;
}
