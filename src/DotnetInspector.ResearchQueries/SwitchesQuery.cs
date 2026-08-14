using System.Collections.Immutable;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>Typed result of reading an assembly's declared and AppContext switches.</summary>
public abstract record SwitchesResult
{
    private SwitchesResult()
    {
    }

    /// <summary>The switches, in stable display order, which may be empty.</summary>
    public sealed record Available(ImmutableArray<SwitchInfo> Switches) : SwitchesResult;

    /// <summary>The query failed while reading switch evidence.</summary>
    public sealed record Failed(Exception Error) : SwitchesResult;
}

/// <summary>
/// Composes declared switch metadata with AppContext call-site evidence from an
/// already-open assembly session.
/// </summary>
public static class SwitchesQuery
{
    public static InspectionQuery<SwitchesResult> Definition { get; } =
        new("Switches", InspectionCost.NetworkFree);

    public static SwitchesResult Execute(AssemblyInspectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            HashSet<SwitchInfo> switches = [.. session.Switches()];
            if (session.HasMetadata)
            {
                foreach (AppContextSwitchOccurrence occurrence
                    in AppContextSwitchProjectionProducer.ProduceInventory(
                        session.MethodBodies))
                {
                    switches.Add(
                        new SwitchInfo(
                            "AppContext",
                            occurrence.Switch,
                            occurrence.Api));
                }
            }

            return new SwitchesResult.Available(
                switches
                    .OrderBy(s => s.Kind, StringComparer.Ordinal)
                    .ThenBy(s => s.Switch, StringComparer.Ordinal)
                    .ThenBy(s => s.Api, StringComparer.Ordinal)
                    .ToImmutableArray());
        }
        catch (Exception ex)
        {
            return new SwitchesResult.Failed(ex);
        }
    }
}
