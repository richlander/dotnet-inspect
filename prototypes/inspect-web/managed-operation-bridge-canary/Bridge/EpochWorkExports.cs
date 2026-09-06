using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using InspectWeb.Engine;
using BodyResult =
    InspectWeb.Engine.BrowserManagedOperationBodyResult<string, string, string>;

namespace InspectWeb.ManagedOperationBridge.BrowserCanary;

public sealed record EpochWorkSnapshot(
    long LastSequence,
    int ActiveLeases,
    int FaultRecords,
    int PendingCallouts,
    bool AdmissionStopped,
    bool Registered);

public sealed record EpochVerificationReceipt(
    string Status,
    int Registrations,
    int ProducerStarts,
    int WaiterCalls,
    int CanceledWaiters,
    int BoundaryFailures,
    int StartAttempts,
    int FinishAttempts,
    int CompletedObservations,
    int FailedObservations,
    int DrainFailures,
    int Unregistrations,
    int ReleasedProducers);

public static partial class Exports
{
    private static BrowserManagedEpochWorkReporter<string>? s_epochReporter;
    private static BrowserManagedEpochWorkSource? s_epochSource;
    private static int s_epochRegistrations;
    private static int s_epochStarts;
    private static int s_epochFinishes;
    private static int s_epochCompletedObservations;
    private static int s_epochFailedObservations;
    private static int s_epochDrainFailures;
    private static int s_epochUnregistrations;
    private static int s_beforeEpochProducers;
    private static int s_beforeEpochWaiters;
    private static int s_beforeEpochCanceled;
    private static int s_beforeEpochCleanupFailures;
    private static int s_beforeEpochReleased;

    [JSExport]
    public static void RegisterEpochReporter(
        string allowance,
        [JSMarshalAs<JSType.Function<JSType.Number, JSType.String>>]
        Action<double, string> started,
        [JSMarshalAs<JSType.Function<JSType.Number>>]
        Action<double> finished)
    {
        if (s_epochReporter is not null)
            throw new InvalidOperationException("An epoch reporter is already registered.");
        if (s_epochRegistrations == 0)
        {
            s_beforeEpochProducers = s_sharedProducerStarts;
            s_beforeEpochWaiters = s_sharedWaiterCalls;
            s_beforeEpochCanceled = s_sharedResults.GetValueOrDefault(OperationResultKind.Canceled);
            s_beforeEpochCleanupFailures = s_sharedCleanupFailures;
            s_beforeEpochReleased = s_sharedReleasedProducers;
        }
        s_epochReporter = new(
            (sequence, value) =>
            {
                s_epochStarts++;
                started(sequence, value);
            },
            sequence =>
            {
                s_epochFinishes++;
                finished(sequence);
            });
        s_epochSource = s_epochReporter.ForProducer(allowance);
        s_epochRegistrations++;
    }

    [JSExport]
    public static void CreateLeasedSharedProducer(string producerId, string mode)
    {
        ArgumentException.ThrowIfNullOrEmpty(producerId);
        BrowserManagedEpochWorkSource source = s_epochSource
            ?? throw new InvalidOperationException("Register the epoch reporter first.");
        s_sharedControllers.Add(producerId, new ControlledSharedProducer(ParseSharedMode(mode), source));
    }

    [JSExport]
    public static string GetEpochWorkSnapshot()
    {
        BrowserManagedEpochWorkSnapshot state = GetEpochReporter().Snapshot();
        var snapshot = new EpochWorkSnapshot(
            state.LastSequence, state.ActiveLeases, state.FaultRecords,
            state.PendingCallouts, state.AdmissionStopped, state.Registered);
        return JsonSerializer.Serialize(snapshot, CanaryJsonContext.Default.EpochWorkSnapshot);
    }

    [JSExport]
    public static async Task<string> ObserveLeasedProducer(string producerId)
    {
        try
        {
            var result = await GetSharedController(producerId).Producer.ObserveCompletionAsync();
            if (result is not BodyResult.Succeeded success)
                throw new InvalidOperationException("The canary producer returned an unexpected result.");
            s_epochCompletedObservations++;
            return success.Value;
        }
        catch
        {
            s_epochFailedObservations++;
            throw;
        }
    }

    [JSExport]
    public static async Task DrainEpochReporter()
    {
        BrowserManagedEpochWorkReporter<string> reporter = GetEpochReporter();
        reporter.StopAdmission();
        try
        {
            await reporter.DrainAsync();
        }
        catch
        {
            s_epochDrainFailures++;
            throw;
        }
    }

    [JSExport]
    public static void UnregisterEpochReporter()
    {
        GetEpochReporter().Unregister();
        s_epochReporter = null;
        s_epochSource = null;
        s_epochUnregistrations++;
    }

    [JSExport]
    public static string VerifyEpochBaseline()
    {
        var receipt = new EpochVerificationReceipt(
            "managed-operation-bridge:epoch-ok",
            s_epochRegistrations,
            s_sharedProducerStarts - s_beforeEpochProducers,
            s_sharedWaiterCalls - s_beforeEpochWaiters,
            s_sharedResults.GetValueOrDefault(OperationResultKind.Canceled) - s_beforeEpochCanceled,
            s_sharedCleanupFailures - s_beforeEpochCleanupFailures,
            s_epochStarts, s_epochFinishes,
            s_epochCompletedObservations, s_epochFailedObservations,
            s_epochDrainFailures, s_epochUnregistrations,
            s_sharedReleasedProducers - s_beforeEpochReleased);
        if (receipt != new EpochVerificationReceipt(
                "managed-operation-bridge:epoch-ok", 3, 5, 7, 6, 1, 5, 4, 2, 3, 2, 3, 5)
            || s_epochReporter is not null || s_epochSource is not null
            || s_sharedControllers.Count != 0 || s_bridge.ActiveCount != 0)
        {
            throw new InvalidOperationException(
                "The managed-operation bridge canary did not execute every epoch scenario exactly once.");
        }
        return JsonSerializer.Serialize(receipt, CanaryJsonContext.Default.EpochVerificationReceipt);
    }

    private static BrowserManagedEpochWorkReporter<string> GetEpochReporter() =>
        s_epochReporter ?? throw new InvalidOperationException("No epoch reporter is registered.");
}
