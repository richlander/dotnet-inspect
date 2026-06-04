namespace DotnetInspector.Models;

/// <summary>
/// A factual audit signal extracted from package, assembly, or NuGet metadata.
/// Signals are observations, not a security or trust verdict.
/// </summary>
public record AuditSignal(string Area, string Signal, string Value, string Evidence);
