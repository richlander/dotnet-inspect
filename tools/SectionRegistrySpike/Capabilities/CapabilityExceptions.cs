namespace SectionRegistrySpike.Capabilities;

/// <summary>Thrown when a capability plan references a <see cref="CapabilityKey"/> that was never registered.</summary>
public sealed class CapabilityNotRegisteredException(string message) : Exception(message);

/// <summary>Thrown when capability dependencies form a cycle. <see cref="Path"/> is the detected cycle, in traversal order.</summary>
public sealed class CapabilityCycleException(string message, IReadOnlyList<string> path) : Exception(message)
{
    public IReadOnlyList<string> Path { get; } = path;
}
