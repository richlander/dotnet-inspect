using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Typed result of reading audit-relevant assembly metadata.</summary>
public abstract record AuditMetadataResult
{
    private AuditMetadataResult()
    {
    }

    /// <summary>The image contains managed metadata and produced audit facts.</summary>
    public sealed record Available(AssemblyAuditMetadata Metadata) : AuditMetadataResult;

    /// <summary>The image contains no managed metadata.</summary>
    public sealed record NoMetadata : AuditMetadataResult;

    /// <summary>The query failed while reading audit metadata.</summary>
    public sealed record Failed(Exception Error) : AuditMetadataResult;
}

/// <summary>Reads audit-relevant facts from an already-open assembly session.</summary>
public static class AuditMetadataQuery
{
    public static InspectionQuery<AuditMetadataResult> Definition { get; } =
        new("Audit metadata", InspectionCost.NetworkFree);

    public static AuditMetadataResult Execute(AssemblyInspectionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            return session.HasMetadata
                ? new AuditMetadataResult.Available(session.AuditMetadata())
                : new AuditMetadataResult.NoMetadata();
        }
        catch (Exception ex)
        {
            return new AuditMetadataResult.Failed(ex);
        }
    }
}
