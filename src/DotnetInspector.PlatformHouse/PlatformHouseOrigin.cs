namespace DotnetInspector.PlatformHouse;

/// <summary>The typed origin of one PlatformHouse request.</summary>
public abstract class PlatformHouseRequestOrigin
{
    private protected PlatformHouseRequestOrigin()
    {
    }

    /// <summary>
    /// A standalone operation whose caller owns its lifetime. Workspace and
    /// PackageHouse origins are added only with their owner-issued contracts.
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
}
