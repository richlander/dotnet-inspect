#pragma warning disable CS0649
using System.Diagnostics.Tracing;

// Aggregates GC AllocationTick samples (about one per 100 KB allocated) by type.
sealed class AllocProfile : EventListener
{
    readonly Dictionary<string, long> _bytes = new(StringComparer.Ordinal);
    long _total;

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
            EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1); // GC keyword
    }

    public static bool Debug;
    readonly HashSet<string> _seen = [];

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (Debug)
            lock (_seen)
                if (_seen.Add($"{e.EventSource.Name}:{e.EventName}:{e.EventId}"))
                    Console.WriteLine($"  event {e.EventSource.Name} {e.EventName} id={e.EventId} payload=[{string.Join(",", e.PayloadNames ?? [])}]");
        if (e.EventName is not { } name || !name.StartsWith("GCAllocationTick", StringComparison.Ordinal) || e.Payload is null)
            return;
        int typeIndex = e.PayloadNames!.IndexOf("TypeName");
        int amountIndex = e.PayloadNames.IndexOf("AllocationAmount64");
        if (typeIndex < 0 || amountIndex < 0)
            return;
        string type = e.Payload[typeIndex] as string ?? "?";
        long amount = Convert.ToInt64(e.Payload[amountIndex]);
        lock (_bytes)
        {
            _bytes[type] = _bytes.GetValueOrDefault(type) + amount;
            _total += amount;
        }
    }

    public void Report(int calls, int top = 15)
    {
        Thread.Sleep(500);
        lock (_bytes)
        {
            Console.WriteLine($"  sampled total {_total / calls / 1024.0:F1} KB/call");
            foreach (var (type, bytes) in _bytes.OrderByDescending(p => p.Value).Take(top))
                Console.WriteLine($"  {bytes / calls / 1024.0,9:F1} KB/call  {bytes * 100.0 / _total,5:F1}%  {type}");
        }
    }
}
