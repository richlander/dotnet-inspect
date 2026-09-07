namespace InspectWeb.Engine;

internal sealed class BrowserManagedEpochWorkRegistration
{
    internal static BrowserManagedEpochWorkRegistration Current { get; } = new();

    readonly object _sync = new();
    BrowserManagedEpochWorkReporter<string>? _reporter;
    BrowserManagedEpochWorkSource? _source;
    bool _registeredOnce;

    internal void Register(
        string allowance,
        Action<long, string> started,
        Action<long> finished)
    {
        ArgumentNullException.ThrowIfNull(allowance);
        lock (_sync)
        {
            if (_registeredOnce)
                throw new InvalidOperationException("The epoch-work reporter can register only once per realm.");

            var reporter = new BrowserManagedEpochWorkReporter<string>(started, finished);
            BrowserManagedEpochWorkSource source = reporter.ForProducer(allowance);
            _reporter = reporter;
            _source = source;
            _registeredOnce = true;
        }
    }

    internal BrowserManagedEpochWorkSource Source
    {
        get
        {
            lock (_sync)
                return _source
                    ?? throw new InvalidOperationException("No epoch-work reporter is registered.");
        }
    }

    internal BrowserManagedEpochWorkSource? SourceForAcquisition
    {
        get
        {
            lock (_sync)
                return !_registeredOnce
                    ? null
                    : _source
                        ?? throw new InvalidOperationException("The epoch-work reporter has been unregistered.");
        }
    }

    internal Task StopAndDrainAsync()
    {
        lock (_sync)
        {
            BrowserManagedEpochWorkReporter<string> reporter = GetReporter();
            reporter.StopAdmission();
            return reporter.DrainAsync();
        }
    }

    internal void Unregister()
    {
        lock (_sync)
        {
            GetReporter().Unregister();
            _reporter = null;
            _source = null;
        }
    }

    BrowserManagedEpochWorkReporter<string> GetReporter() =>
        _reporter ?? throw new InvalidOperationException("No epoch-work reporter is registered.");
}
