using System.Net;

namespace DotnetInspector.Core;

/// <summary>
/// Why a source could not answer, as opposed to answering that a package is absent.
/// </summary>
public enum FeedFailureKind
{
    /// <summary>The source demanded credentials that were missing or rejected (HTTP 401).</summary>
    Authentication,

    /// <summary>The credentials were understood but do not grant access (HTTP 403).</summary>
    Authorization,

    /// <summary>The source failed to answer for any other reason.</summary>
    Unavailable
}

/// <summary>
/// A single request to a source that did not succeed and was not a plain "no such package".
/// </summary>
/// <param name="Url">The request URL that failed, already redacted of userinfo and sensitive query values.</param>
/// <param name="Status">The status the source returned, if a response arrived at all.</param>
/// <param name="Phase">The traffic kind in flight, which names what was being attempted.</param>
public readonly record struct FeedFailure(string Url, HttpStatusCode? Status, NetworkTrafficKind Phase)
{
    /// <summary>Classifies the failure for message selection.</summary>
    public FeedFailureKind Kind => Status switch
    {
        HttpStatusCode.Unauthorized => FeedFailureKind.Authentication,
        HttpStatusCode.Forbidden => FeedFailureKind.Authorization,
        _ => FeedFailureKind.Unavailable
    };

    /// <summary>The status as text, falling back to a phrase when no response arrived.</summary>
    public string StatusText => Status is { } status
        ? $"HTTP {(int)status} {status}"
        : "no response";

    /// <summary>A short description of what was being attempted, for use mid-sentence.</summary>
    public string PhaseText => Phase switch
    {
        NetworkTrafficKind.PackageVersionList => "listing versions",
        NetworkTrafficKind.PackageSourceDiscovery => "reading the service index",
        NetworkTrafficKind.PackageDownload => "downloading the package",
        NetworkTrafficKind.PackageManifest => "reading the manifest",
        NetworkTrafficKind.PackageMetadata => "reading package metadata",
        NetworkTrafficKind.PackageSearch => "searching",
        _ => "reading the source"
    };
}

/// <summary>
/// Collects source failures for the duration of a scope, so that a lookup which ends in
/// "no version found" can tell an absent package apart from a source that never answered.
/// </summary>
/// <remarks>
/// The status is known deep inside the HTTP helpers, but the signatures between there and the
/// caller return <c>string?</c> and <c>List&lt;string&gt;?</c>, so it cannot be returned. This
/// follows the ambient-scope shape already used by <see cref="NetworkTelemetry"/>: the scope
/// installs one collector that nested async work mutates in place.
/// </remarks>
public static class FeedFailureTelemetry
{
    private static readonly AsyncLocal<FeedFailureCollector?> CurrentValue = new();

    /// <summary>
    /// Begins collecting source failures. Disposing restores the previous collector.
    /// </summary>
    public static IDisposable Scope()
    {
        var previous = CurrentValue.Value;
        CurrentValue.Value = new FeedFailureCollector();
        return new CollectorScope(previous);
    }

    /// <summary>The collector for the current scope, or null when nothing is collecting.</summary>
    public static FeedFailureCollector? Current => CurrentValue.Value;

    /// <summary>
    /// Records a failed request against the current scope. Does nothing when no scope is open,
    /// so the HTTP helpers stay usable outside a collecting context.
    /// </summary>
    /// <remarks>
    /// The URL is redacted before it is stored, not merely before it is rendered. Some feeds
    /// carry a credential in the source URL, and this failure text is printed to the console.
    /// Redacting on the way in means the secret never reaches the collector, whose contents are
    /// publicly readable through <see cref="FeedFailureCollector.Failures"/>.
    /// </remarks>
    /// <param name="url">The request URL that failed.</param>
    /// <param name="status">The status returned, or null when no response arrived.</param>
    public static void Record(string url, HttpStatusCode? status)
    {
        CurrentValue.Value?.Add(new FeedFailure(
            NetworkRequestObservation.RedactSensitiveUrlText(url),
            status,
            NetworkTelemetry.CurrentTrafficKind));
    }

    private sealed class CollectorScope(FeedFailureCollector? previous) : IDisposable
    {
        public void Dispose() => CurrentValue.Value = previous;
    }
}

/// <summary>
/// The mutable set of failures gathered by one <see cref="FeedFailureTelemetry.Scope"/>.
/// </summary>
public sealed class FeedFailureCollector
{
    private readonly object _gate = new();
    private readonly List<FeedFailure> _failures = [];

    internal void Add(FeedFailure failure)
    {
        lock (_gate)
        {
            // A retried request reports the same URL more than once; one entry per URL and
            // status keeps the message from repeating itself.
            if (!_failures.Contains(failure))
                _failures.Add(failure);
        }
    }

    /// <summary>The failures recorded so far, in the order they were first seen.</summary>
    public IReadOnlyList<FeedFailure> Failures
    {
        get
        {
            lock (_gate)
            {
                return _failures.ToArray();
            }
        }
    }

    /// <summary>Whether any source failed to answer during the scope.</summary>
    public bool HasFailures
    {
        get
        {
            lock (_gate)
            {
                return _failures.Count > 0;
            }
        }
    }

    /// <summary>
    /// Builds the operator-facing explanation for a lookup that produced no result, or null
    /// when nothing failed and the package really is absent.
    /// </summary>
    /// <param name="packageName">The package that was being looked for.</param>
    public string? DescribeFailure(string packageName)
    {
        var failures = Failures;
        if (failures.Count == 0)
            return null;

        var lines = new List<string>();
        bool needsCredentials = failures.Any(f => f.Kind == FeedFailureKind.Authentication);

        lines.Add(needsCredentials
            ? $"Package '{packageName}' could not be resolved because a source requires credentials."
            : $"Package '{packageName}' could not be resolved because a source did not answer.");

        foreach (var failure in failures)
            lines.Add($"  {failure.Url} — {failure.StatusText} while {failure.PhaseText}");

        lines.Add(failures.Any(f => f.Kind is FeedFailureKind.Authentication or FeedFailureKind.Authorization)
            ? "The package may exist; the source was not readable. Supply credentials for this source and retry."
            : "The package may exist; the source was not readable. Retry, or check the source URL.");

        return string.Join(Environment.NewLine, lines);
    }
}
