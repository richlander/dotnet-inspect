using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using InspectWeb.Engine;
using BodyResult =
    InspectWeb.Engine.BrowserManagedOperationBodyResult<string, string, string>;
using BridgeResult =
    InspectWeb.Engine.BrowserManagedOperationResult<string, string, string>;

namespace InspectWeb.ManagedOperationBridge.BrowserCanary;

public sealed record SharedProducerSnapshot(
    int BodyStarts,
    int WaiterCount,
    int ActiveOperations,
    int Operations,
    int SettledOperations,
    int StopRequests,
    bool ProducerCanceled,
    bool EventsClosed,
    bool Finalizing,
    bool ProducerCompleted);

public sealed record SharedVerificationReceipt(
    string Status,
    int ProducerStarts,
    int WaiterCalls,
    int SucceededWaiters,
    int CanceledWaiters,
    int FailedWaiters,
    int ObserverFailures,
    int CleanupFailures,
    int OtherBoundaryFailures,
    int StopRequests,
    int ReleasedProducers);

public static partial class Exports
{
    private static readonly Dictionary<string, ControlledSharedProducer>
        s_sharedControllers = new(StringComparer.Ordinal);
    private static readonly Dictionary<SharedMode, int> s_sharedModeStarts = [];
    private static readonly Dictionary<OperationResultKind, int> s_sharedResults = [];
    private static int s_sharedProducerStarts;
    private static int s_sharedWaiterCalls;
    private static int s_sharedObserverFailures;
    private static int s_sharedCleanupFailures;
    private static int s_sharedOtherBoundaryFailures;
    private static int s_sharedStopRequests;
    private static int s_sharedReleasedProducers;

    [JSExport]
    public static void CreateSharedProducer(string producerId, string mode)
    {
        ArgumentException.ThrowIfNullOrEmpty(producerId);
        SharedMode parsed = mode switch
        {
            "natural-success" => SharedMode.NaturalSuccess,
            "stop-and-drain" => SharedMode.StopAndDrain,
            "late-failure" => SharedMode.LateFailure,
            "origin-cancellation" => SharedMode.OriginCancellation,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown shared canary mode."),
        };
        lock (s_sync)
        {
            if (s_sharedControllers.ContainsKey(producerId))
                throw new InvalidOperationException($"Shared producer '{producerId}' already exists.");
            s_sharedControllers.Add(producerId, new ControlledSharedProducer(parsed));
        }
    }

    [JSExport]
    public static async Task<string> RunSharedOperation(
        string operationId,
        string producerId,
        [JSMarshalAs<JSType.Function<JSType.Number, JSType.String, JSType.Boolean>>]
        Action<int, string, bool> progressCallback)
    {
        ControlledSharedProducer controller = GetSharedController(producerId);
        Interlocked.Increment(ref s_sharedWaiterCalls);
        try
        {
            Task<BridgeResult> operation = s_bridge.RunSharedAsync(
                BrowserManagedOperationId.From(operationId),
                progress => progressCallback(progress.Sequence, progress.Phase, progress.IsFinal),
                controller.Producer,
                static exception => new BrowserManagedOperationFailure<string, string>(
                    exception.GetType().Name,
                    exception.Message));
            controller.Track(operation);
            OperationResultEnvelope envelope = ToEnvelope(await operation);
            lock (s_sync)
                s_sharedResults[envelope.Kind] = s_sharedResults.GetValueOrDefault(envelope.Kind) + 1;
            return JsonSerializer.Serialize(envelope, CanaryJsonContext.Default.OperationResultEnvelope);
        }
        catch (BrowserManagedOperationBoundaryException exception)
        {
            if (exception.FailureKind == "event-callback")
                Interlocked.Increment(ref s_sharedObserverFailures);
            else if (exception.FailureKind == "cleanup")
                Interlocked.Increment(ref s_sharedCleanupFailures);
            else
                Interlocked.Increment(ref s_sharedOtherBoundaryFailures);
            throw;
        }
    }

    [JSExport]
    public static string GetSharedSnapshot(string producerId)
    {
        SharedProducerSnapshot snapshot = GetSharedController(producerId).Snapshot();
        return JsonSerializer.Serialize(snapshot, CanaryJsonContext.Default.SharedProducerSnapshot);
    }

    [JSExport]
    public static bool ReportSharedProgress(string producerId, int sequence)
    {
        var producer = GetSharedController(producerId).Producer;
        producer.Report(new CanaryProgress(sequence, "shared", false));
        return producer.IsClosed;
    }

    [JSExport]
    public static bool CompleteSharedProducer(string producerId) =>
        GetSharedController(producerId).Complete();

    [JSExport]
    public static bool FinishSharedFinalization(string producerId) =>
        GetSharedController(producerId).FinishFinalization();

    [JSExport]
    public static void ReleaseSharedProducer(string producerId)
    {
        ControlledSharedProducer controller = GetSharedController(producerId);
        SharedProducerSnapshot snapshot = controller.Snapshot();
        if (!snapshot.ProducerCompleted || !snapshot.EventsClosed
            || snapshot.WaiterCount != 0 || snapshot.ActiveOperations != 0
            || snapshot.Operations != snapshot.SettledOperations)
        {
            throw new InvalidOperationException("Shared canary producer was released before quiescence.");
        }
        lock (s_sync)
            s_sharedControllers.Remove(producerId);
        controller.Dispose();
        Interlocked.Increment(ref s_sharedReleasedProducers);
    }

    [JSExport]
    public static string VerifySharedBaseline()
    {
        SharedVerificationReceipt receipt;
        lock (s_sync)
        {
            receipt = new SharedVerificationReceipt(
                "managed-operation-bridge:shared-ok",
                s_sharedProducerStarts,
                s_sharedWaiterCalls,
                s_sharedResults.GetValueOrDefault(OperationResultKind.Succeeded),
                s_sharedResults.GetValueOrDefault(OperationResultKind.Canceled),
                s_sharedResults.GetValueOrDefault(OperationResultKind.Failed),
                s_sharedObserverFailures,
                s_sharedCleanupFailures,
                s_sharedOtherBoundaryFailures,
                s_sharedStopRequests,
                s_sharedReleasedProducers);
            if (receipt != new SharedVerificationReceipt(
                    "managed-operation-bridge:shared-ok", 6, 8, 2, 3, 1, 1, 1, 0, 1, 6)
                || s_sharedModeStarts.GetValueOrDefault(SharedMode.NaturalSuccess) != 3
                || s_sharedModeStarts.GetValueOrDefault(SharedMode.StopAndDrain) != 1
                || s_sharedModeStarts.GetValueOrDefault(SharedMode.LateFailure) != 1
                || s_sharedModeStarts.GetValueOrDefault(SharedMode.OriginCancellation) != 1
                || s_sharedControllers.Count != 0 || s_bridge.ActiveCount != 0)
            {
                throw new InvalidOperationException(
                    "The managed-operation bridge canary did not execute every shared scenario exactly once.");
            }
        }
        return JsonSerializer.Serialize(receipt, CanaryJsonContext.Default.SharedVerificationReceipt);
    }

    private static ControlledSharedProducer GetSharedController(string producerId)
    {
        lock (s_sync)
            return s_sharedControllers[producerId];
    }

    private enum SharedMode
    {
        NaturalSuccess,
        StopAndDrain,
        LateFailure,
        OriginCancellation,
    }

    private sealed class ControlledSharedProducer : IDisposable
    {
        private readonly SharedMode _mode;
        private readonly CancellationTokenSource _producerCancellation = new();
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finalizing =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finishFinalization =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<Task<BridgeResult>> _operations = [];
        private Task<BodyResult>? _physicalTask;
        private int _bodyStarts;
        private int _stopRequests;

        internal ControlledSharedProducer(SharedMode mode)
        {
            _mode = mode;
            Producer = new(
                Start,
                mode is SharedMode.StopAndDrain ? RequestStop : null,
                _producerCancellation.Token);
        }

        internal BrowserManagedSharedProducer<string, string, string, CanaryProgress>
            Producer { get; }

        internal void Track(Task<BridgeResult> operation)
        {
            lock (s_sync)
                _operations.Add(operation);
        }

        internal SharedProducerSnapshot Snapshot()
        {
            lock (s_sync)
            {
                return new SharedProducerSnapshot(
                    _bodyStarts, Producer.WaiterCount, s_bridge.ActiveCount,
                    _operations.Count, _operations.Count(operation => operation.IsCompleted),
                    _stopRequests, _producerCancellation.IsCancellationRequested,
                    Producer.IsClosed, _finalizing.Task.IsCompleted, _physicalTask?.IsCompleted == true);
            }
        }

        internal bool Complete() => _release.TrySetResult();
        internal bool FinishFinalization() => _finishFinalization.TrySetResult();
        public void Dispose() => _producerCancellation.Dispose();

        private Task<BodyResult> Start(IBrowserManagedOperationEvents<CanaryProgress> events)
        {
            lock (s_sync)
            {
                _bodyStarts++;
                s_sharedProducerStarts++;
                s_sharedModeStarts[_mode] = s_sharedModeStarts.GetValueOrDefault(_mode) + 1;
            }
            _physicalTask = ProduceAsync(events);
            return _physicalTask;
        }

        private void RequestStop()
        {
            Interlocked.Increment(ref _stopRequests);
            Interlocked.Increment(ref s_sharedStopRequests);
            _producerCancellation.Cancel();
        }

        private async Task<BodyResult> ProduceAsync(
            IBrowserManagedOperationEvents<CanaryProgress> events)
        {
            events.Report(new CanaryProgress(1, "started", false));
            if (_mode is SharedMode.OriginCancellation)
            {
                throw new OperationCanceledException(
                    "The shared producer supplied no accepted cancellation.");
            }
            try
            {
                await _release.Task.WaitAsync(_producerCancellation.Token);
                if (_mode is SharedMode.LateFailure)
                    throw new InvalidOperationException("The shared producer failed during final drain.");
                events.Report(new CanaryProgress(4, "completed", true));
                return new BodyResult.Succeeded("shared-success");
            }
            finally
            {
                _finalizing.SetResult();
                await _finishFinalization.Task;
            }
        }
    }
}
