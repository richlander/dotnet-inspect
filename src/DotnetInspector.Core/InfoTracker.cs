using System.Diagnostics;

namespace DotnetInspector.Core;

/// <summary>
/// Static ambient tracker for operational metrics (output bytes, duration, HTTP calls, cache).
/// Call <see cref="Start"/> once at startup to begin tracking.
/// </summary>
public static class InfoTracker
{
    private static bool _enabled;
    private static readonly Stopwatch _stopwatch = new();
    private static int _httpRequests;
    private static int _cacheHits;
    private static int _cacheMisses;
    private static CountingTextWriter? _countingWriter;
    private static readonly object _detailsLock = new();
    private static readonly Dictionary<string, string> _details = new(StringComparer.OrdinalIgnoreCase);

    public static bool Enabled => _enabled;

    /// <summary>
    /// Starts tracking. Installs a <see cref="CountingTextWriter"/> on Console.Out.
    /// </summary>
    public static void Start()
    {
        _enabled = true;
        lock (_detailsLock)
            _details.Clear();
        _countingWriter = new CountingTextWriter(Console.Out);
        Console.SetOut(_countingWriter);
        _stopwatch.Start();
    }

    public static void RecordHttpRequest() => Interlocked.Increment(ref _httpRequests);
    public static void RecordCacheHit() => Interlocked.Increment(ref _cacheHits);
    public static void RecordCacheMiss() => Interlocked.Increment(ref _cacheMisses);

    public static void SetDetail(string key, string value)
    {
        if (!_enabled)
            return;

        lock (_detailsLock)
            _details[key] = value;
    }

    public static string? GetDetail(string key)
    {
        lock (_detailsLock)
            return _details.GetValueOrDefault(key);
    }

    internal static void ResetForTests()
    {
        _enabled = false;
        _stopwatch.Reset();
        _httpRequests = 0;
        _cacheHits = 0;
        _cacheMisses = 0;
        _countingWriter = null;
        lock (_detailsLock)
            _details.Clear();
    }

    public static long CharsWritten => _countingWriter?.CharCount ?? 0;
    public static TimeSpan Elapsed => _stopwatch.Elapsed;
    public static int HttpRequests => _httpRequests;
    public static int CacheHits => _cacheHits;
    public static int CacheMisses => _cacheMisses;
}
