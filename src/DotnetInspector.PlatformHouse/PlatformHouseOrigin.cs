namespace DotnetInspector.PlatformHouse;

/// <summary>The typed origin of one PlatformHouse request.</summary>
public abstract class PlatformHouseRequestOrigin
{
    private protected PlatformHouseRequestOrigin()
    {
    }

    /// <summary>
    /// A standalone operation whose caller owns its lifetime.
    /// </summary>
    public sealed class Standalone : PlatformHouseRequestOrigin
    {
        public Standalone(PlatformStandaloneOperationIdentity operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            Operation = operation;
        }

        public PlatformStandaloneOperationIdentity Operation { get; }
    }

    /// <summary>
    /// An ordinary platform request caused by an upstream PackageHouse
    /// delegation. Orchestration retains the package decision receipt outside
    /// PlatformHouse and uses this opaque identity to associate the results.
    /// </summary>
    public sealed class Delegated : PlatformHouseRequestOrigin
    {
        public Delegated(PlatformDelegationAssociationIdentity association)
        {
            ArgumentNullException.ThrowIfNull(association);
            Association = association;
        }

        public PlatformDelegationAssociationIdentity Association { get; }
    }
}
