namespace SectionRegistrySpike.Capabilities;

/// <summary>Thrown before execution when a plan contains work not authorized for the selected strategy.</summary>
public sealed class CapabilityNotAuthorizedException(string message) : Exception(message);
