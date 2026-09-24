namespace DotnetInspector.Networking;

public readonly record struct RequestCurrency(string? What, string? Why);

public static class RequestTelemetry
{
    private static readonly AsyncLocal<RequestCurrency?> CurrentValue = new();

    public static IDisposable Scope(string? what, string? why)
    {
        var previous = CurrentValue.Value;
        CurrentValue.Value = new RequestCurrency(what, why);
        return new RequestCurrencyScope(previous);
    }

    internal static RequestCurrency Current => CurrentValue.Value ?? default;

    private sealed class RequestCurrencyScope(RequestCurrency? previous) : IDisposable
    {
        public void Dispose() => CurrentValue.Value = previous;
    }
}
