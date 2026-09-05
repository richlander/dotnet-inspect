using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using InspectWeb.Engine;

namespace MultiFacade.Shared;

[JsonConverter(typeof(JsonStringEnumConverter<ManagedOperationCanaryResultKind>))]
public enum ManagedOperationCanaryResultKind
{
    Succeeded,
}

public sealed record ManagedOperationCanaryResult(
    ManagedOperationCanaryResultKind Kind,
    int Value);

[JsonSerializable(typeof(ManagedOperationCanaryResult))]
internal sealed partial class CanaryJsonContext : JsonSerializerContext;

[SupportedOSPlatform("browser")]
public static partial class Exports
{
    private static int s_managedOperationCalls;
    private static int s_retainedEventCalls;
    private static readonly BrowserManagedOperationBridge s_operationBridge =
        new();
    private static IBrowserManagedOperationEvents<ManagedOperationCanaryEvent>?
        s_retainedEvents;

    [JSExport]
    public static async Task<string> RunManagedOperationCanary(
        string operationId,
        [JSMarshalAs<JSType.Function<JSType.Number, JSType.String>>]
        Action<int, string> eventCallback)
    {
        ArgumentNullException.ThrowIfNull(eventCallback);
        s_managedOperationCalls++;
        BrowserManagedOperationResult<int, string, string> result =
            await s_operationBridge.RunAsync<
                int,
                string,
                string,
                ManagedOperationCanaryEvent>(
                BrowserManagedOperationId.From(operationId),
                operationEvent => eventCallback(
                    (int)operationEvent.Kind,
                    operationEvent.Value),
                async (_, events) =>
                {
                    s_retainedEvents = events;
                    events.Report(
                        new ManagedOperationCanaryEvent(
                            ManagedOperationCanaryEventKind.Progress,
                            "search:1/3"));
                    await Task.Yield();
                    events.Report(
                        new ManagedOperationCanaryEvent(
                            ManagedOperationCanaryEventKind.Item,
                            "Package.One"));
                    events.Report(
                        new ManagedOperationCanaryEvent(
                            ManagedOperationCanaryEventKind.ItemFailure,
                            "Package.Two"));
                    return new BrowserManagedOperationBodyResult<
                        int,
                        string,
                        string>.Succeeded(3);
                },
                static exception =>
                    new BrowserManagedOperationFailure<string, string>(
                        exception.GetType().Name,
                        exception.Message));

        int value = result switch
        {
            BrowserManagedOperationResult<
                int,
                string,
                string>.Succeeded succeeded => succeeded.Value,
            _ => throw new InvalidOperationException(
                "The managed operation canary did not succeed."),
        };
        return JsonSerializer.Serialize(
            new ManagedOperationCanaryResult(
                ManagedOperationCanaryResultKind.Succeeded,
                value),
            CanaryJsonContext.Default.ManagedOperationCanaryResult);
    }

    [JSExport]
    public static void ReportRetainedManagedOperationCanaryEvent(
        int kind,
        string value)
    {
        s_retainedEventCalls++;
        if (!Enum.IsDefined((ManagedOperationCanaryEventKind)kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (s_retainedEvents is null)
        {
            throw new InvalidOperationException(
                "No managed operation event sink has been retained.");
        }

        s_retainedEvents.Report(
            new ManagedOperationCanaryEvent(
                (ManagedOperationCanaryEventKind)kind,
                value));
    }

    internal static bool ManagedOperationCanaryWasInvokedExactlyOnce() =>
        s_managedOperationCalls == 1 && s_retainedEventCalls == 1;
}

internal enum ManagedOperationCanaryEventKind
{
    Progress,
    Item,
    ItemFailure,
}

internal readonly record struct ManagedOperationCanaryEvent(
    ManagedOperationCanaryEventKind Kind,
    string Value);
