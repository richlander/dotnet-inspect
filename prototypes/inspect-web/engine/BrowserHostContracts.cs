using System.Text.Json.Serialization;

namespace InspectWeb.Engine;

/// <summary>
/// The host facade's browser wire contract.
/// </summary>
/// <remarks>
/// The host publishes exactly one wire record. Capability wire records live in their own export
/// assemblies; <c>ProductionFacadeWireContexts_AreAssemblyLocal</c> gates that ownership.
/// </remarks>
public sealed record BrowserBuildIdentity(
    string Version,
    string? Commit,
    string? BuiltAtUtc,
    string? CommitUrl);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserBuildIdentity))]
internal sealed partial class BrowserHostJsonContext : JsonSerializerContext;
