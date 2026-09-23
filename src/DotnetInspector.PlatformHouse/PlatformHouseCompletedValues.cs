namespace DotnetInspector.PlatformHouse;

/// <summary>
/// Resource-free value returned by a completed type-definition operation.
/// Receipts retain the associated Metadata outcome identities.
/// </summary>
public abstract class PlatformTypeDefinitionValue
{
    private protected PlatformTypeDefinitionValue()
    {
    }

    public sealed class Reference<TReferenceOutcome> :
        PlatformTypeDefinitionValue
        where TReferenceOutcome : notnull
    {
        internal Reference(TReferenceOutcome outcome) => Outcome = outcome;

        public TReferenceOutcome Outcome { get; }
    }

    public sealed class Implementation<TImplementationOutcome> :
        PlatformTypeDefinitionValue
        where TImplementationOutcome : notnull
    {
        internal Implementation(TImplementationOutcome outcome) =>
            Outcome = outcome;

        public TImplementationOutcome Outcome { get; }
    }

    public sealed class ReferenceAndImplementation<
        TReferenceOutcome,
        TImplementationOutcome> : PlatformTypeDefinitionValue
        where TReferenceOutcome : notnull
        where TImplementationOutcome : notnull
    {
        internal ReferenceAndImplementation(
            TReferenceOutcome referenceOutcome,
            TImplementationOutcome implementationOutcome)
        {
            ReferenceOutcome = referenceOutcome;
            ImplementationOutcome = implementationOutcome;
        }

        public TReferenceOutcome ReferenceOutcome { get; }
        public TImplementationOutcome ImplementationOutcome { get; }
    }
}
