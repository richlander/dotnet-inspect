using System.Runtime.InteropServices.JavaScript;
using InspectWeb.Engine;

public static partial class InspectionEngine
{
    [JSExport]
    public static void RegisterEpochWorkReporter(
        string allowance,
        [JSMarshalAs<JSType.Function<JSType.Number, JSType.String>>]
        Action<double, string> started,
        [JSMarshalAs<JSType.Function<JSType.Number>>]
        Action<double> finished)
    {
        ArgumentNullException.ThrowIfNull(started);
        ArgumentNullException.ThrowIfNull(finished);
        BrowserManagedEpochWorkRegistration.Current.Register(
            allowance,
            (sequence, value) => started(sequence, value),
            sequence => finished(sequence));
    }

    [JSExport]
    public static Task DrainEpochWorkReporter() =>
        BrowserManagedEpochWorkRegistration.Current.StopAndDrainAsync();

    [JSExport]
    public static void UnregisterEpochWorkReporter() =>
        BrowserManagedEpochWorkRegistration.Current.Unregister();
}
