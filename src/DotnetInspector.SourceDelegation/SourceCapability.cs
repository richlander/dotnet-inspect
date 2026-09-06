namespace DotnetInspector.SourceDelegation;

// The selection key a candidate requires and a source publishes. A capability
// is a key only: it never defines, implies, or extends the delegated work,
// which is carried by the candidate's operation prefix. Equality is
// owner-issued token identity; the name exists for diagnostics.
public sealed class SourceCapability
{
    private SourceCapability(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public static SourceCapability Create(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new(name);
    }

    public override string ToString() =>
        Name;
}

// The two candidate result shapes. They are distinct: a row handoff retains a
// caller-owned residual, an exact Count covers the complete resolved plan.
public enum DelegationResultShape
{
    RowHandoff,
    ExactCount,
}
