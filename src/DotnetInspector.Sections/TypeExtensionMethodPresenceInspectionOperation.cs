using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public abstract record TypeExtensionMethodPresenceInspectionOutcome
{
    private protected TypeExtensionMethodPresenceInspectionOutcome()
    {
    }

    public sealed record Available(bool Exists)
        : TypeExtensionMethodPresenceInspectionOutcome;

    public sealed record Incomplete
        : TypeExtensionMethodPresenceInspectionOutcome;

    public sealed record Failed
        : TypeExtensionMethodPresenceInspectionOutcome;
}

public sealed record TypeDocumentExtensionPresenceInspectionResult(
    InspectionEnvelope<TypeDocumentInspectionOutcome> Document,
    TypeExtensionMethodPresenceInspectionOutcome ExtensionPresence);

/// <summary>
/// Reports whether one assembly declares an extension for one exact Type.
/// </summary>
public static class TypeExtensionMethodPresenceInspectionOperation
{
    public static TypeExtensionMethodPresenceInspectionOutcome Execute(
        ResolvedAssemblyReference source,
        MetadataTypeDefinitionName receiver,
        long maxMetadataRows,
        CancellationToken cancellationToken = default)
        => Execute(
            source,
            receiver,
            maxMetadataRows,
            includeNonPublic: false,
            cancellationToken);

    public static TypeExtensionMethodPresenceInspectionOutcome Execute(
        ResolvedAssemblyReference source,
        MetadataTypeDefinitionName receiver,
        long maxMetadataRows,
        bool includeNonPublic,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(receiver);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxMetadataRows);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(source);
            return Execute(
                session,
                source.Identity,
                receiver,
                maxMetadataRows,
                includeNonPublic,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is UnsupportedMetadataFormatException
                or MalformedMetadataRootException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return new TypeExtensionMethodPresenceInspectionOutcome
                .Failed();
        }
    }

    internal static TypeExtensionMethodPresenceInspectionOutcome Execute(
        AssemblyInspectionSession session,
        AssemblyReferenceIdentity assembly,
        MetadataTypeDefinitionName receiver,
        long maxMetadataRows,
        bool includeNonPublic,
        CancellationToken cancellationToken,
        MetadataTypeDefinitionAddress? receiverDefinition = null)
    {
        MetadataExtensionRelationPresenceOutcome outcome =
            session.ExtensionRelationsExist(
                new(
                    new(
                        assembly,
                        receiver,
                        receiverDefinition),
                    new(maxMetadataRows),
                    includeNonPublic,
                    sourceAssembly: assembly),
                cancellationToken);
        return outcome switch
        {
            MetadataExtensionRelationPresenceOutcome.Available
                available =>
                new TypeExtensionMethodPresenceInspectionOutcome
                    .Available(available.Exists),
            MetadataExtensionRelationPresenceOutcome.Incomplete =>
                new TypeExtensionMethodPresenceInspectionOutcome
                    .Incomplete(),
            MetadataExtensionRelationPresenceOutcome.Failed =>
                new TypeExtensionMethodPresenceInspectionOutcome
                    .Failed(),
            _ => throw new InvalidOperationException(
                "Unknown Metadata extension presence outcome."),
        };
    }
}
