namespace DotnetInspector.PlatformHouse;

/// <summary>
/// Closed live value returned by a completed type-definition operation.
/// Resource-free receipts retain the associated Metadata outcome identities.
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
