using DotnetInspector.Services;
using ILInspector.Findings;
using ILInspector.SourceLink;

namespace DotnetInspector.Queries;

/// <summary>
/// Host-owned content and capabilities used by SourceLink queries.
/// </summary>
public sealed class SourceLinkQueryContext
{
    public SourceLinkQueryContext(
        SourceLinkService source,
        FindingSubject subject,
        HttpClient symbolClient,
        HttpClient sourceClient,
        string? packageName = null,
        string? packageVersion = null,
        bool isPlatformAssembly = false,
        ISourceLinkQueryCache? cache = null,
        Action<string>? log = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        SymbolClient = symbolClient ?? throw new ArgumentNullException(nameof(symbolClient));
        SourceClient = sourceClient ?? throw new ArgumentNullException(nameof(sourceClient));
        PackageName = packageName;
        PackageVersion = packageVersion;
        IsPlatformAssembly = isPlatformAssembly;
        Cache = cache;
        Log = log;
    }

    public SourceLinkService Source { get; }
    public FindingSubject Subject { get; }
    public HttpClient SymbolClient { get; }
    public HttpClient SourceClient { get; }
    public string? PackageName { get; }
    public string? PackageVersion { get; }
    public bool IsPlatformAssembly { get; }
    public ISourceLinkQueryCache? Cache { get; }
    public Action<string>? Log { get; }
}

/// <summary>Registers the shared SourceLink query family on a host registry.</summary>
public static class SourceLinkQueryRegistryExtensions
{
    public static InspectionQueryRegistry<THostContext> AddSourceLinkQueries<THostContext>(
        this InspectionQueryRegistry<THostContext> registry,
        Func<THostContext, SourceLinkQueryContext> getSourceLinkContext)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(getSourceLinkContext);

        return registry
            .AddAsync(
                SourceLinkDocumentsQuery.Definition,
                (context, cancellationToken) =>
                    SourceLinkDocumentsQuery.ExecuteAsync(
                        getSourceLinkContext(context),
                        cancellationToken))
            .AddAsync(
                SourceAvailabilityQuery.Definition,
                (context, results, cancellationToken) =>
                    SourceAvailabilityQuery.ExecuteAsync(
                        getSourceLinkContext(context),
                        results,
                        cancellationToken),
                SourceLinkDocumentsQuery.Definition)
            .AddAsync(
                SourceIntegrityQuery.Definition,
                (context, results, cancellationToken) =>
                    SourceIntegrityQuery.ExecuteAsync(
                        getSourceLinkContext(context),
                        results,
                        cancellationToken),
                SourceLinkDocumentsQuery.Definition);
    }
}

/// <summary>The portable-PDB source-document census used by SourceLink queries.</summary>
public sealed record SourceLinkDocumentsResult(
    FindingInspection<SourceDocumentObservation> Inspection);

/// <summary>The explicit outcome of a SourceLink reachability audit.</summary>
public abstract record SourceAvailabilityResult
{
    private SourceAvailabilityResult()
    {
    }

    public sealed record Available(SourceAvailabilitySummary Summary)
        : SourceAvailabilityResult;

    public sealed record Absent(string? Detail)
        : SourceAvailabilityResult;

    public sealed record Failed(string Reason)
        : SourceAvailabilityResult;
}

/// <summary>The explicit outcome of a SourceLink checksum audit.</summary>
public abstract record SourceIntegrityResult
{
    private SourceIntegrityResult()
    {
    }

    public sealed record Available(SourceIntegritySummary Summary)
        : SourceIntegrityResult;

    public sealed record Absent(string? Detail)
        : SourceIntegrityResult;

    public sealed record Failed(string Reason)
        : SourceIntegrityResult;
}

/// <summary>
/// Acquires a matching portable PDB when necessary and produces the SourceLink document census.
/// </summary>
public static class SourceLinkDocumentsQuery
{
    public static InspectionQuery<SourceLinkDocumentsResult> Definition { get; } =
        new("SourceLink documents", InspectionCost.Moderated);

    public static async ValueTask<SourceLinkDocumentsResult> ExecuteAsync(
        SourceLinkQueryContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await PdbAcquisitionService.AcquireAsync(
                context.Source.Context,
                context.SymbolClient,
                context.PackageName,
                context.PackageVersion,
                context.IsPlatformAssembly,
                context.Log).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (!context.Source.HasPdb)
            {
                string detail = context.Source.Context.WindowsPdbDetected
                    ? "The assembly uses an unsupported Windows PDB."
                    : "A matching portable PDB is unavailable.";
                return new SourceLinkDocumentsResult(
                    new FindingInspection<SourceDocumentObservation>(
                        new FindingInspection<SourceDocumentObservation>.Absent(detail)));
            }

            if (!context.Source.HasSourceLink)
            {
                return new SourceLinkDocumentsResult(
                    new FindingInspection<SourceDocumentObservation>(
                        new FindingInspection<SourceDocumentObservation>.Absent(
                            "The portable PDB carries no SourceLink map.")));
            }

            return new SourceLinkDocumentsResult(
                SourceLinkFindings.InspectSourceDocuments(
                    context.Source,
                    context.Subject));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FailedDocuments(
                context.Subject,
                $"Could not acquire or inspect SourceLink documents: {ex.Message}");
        }
    }

    private static SourceLinkDocumentsResult FailedDocuments(
        FindingSubject subject,
        string reason)
        => new(
            new FindingInspection<SourceDocumentObservation>(
                new FindingInspection<SourceDocumentObservation>.Failed(
                    new InspectionError(
                        subject,
                        SourceLinkFindings.SourceDocumentDescriptor,
                        reason))));
}

/// <summary>
/// Checks whether each compiler-language source document is embedded or reachable.
/// </summary>
public static class SourceAvailabilityQuery
{
    public static InspectionQuery<SourceAvailabilityResult> Definition { get; } =
        new("SourceLink availability", InspectionCost.Unbounded);

    public static async ValueTask<SourceAvailabilityResult> ExecuteAsync(
        SourceLinkQueryContext context,
        InspectionQueryResults prerequisites,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(prerequisites);

        SourceLinkDocumentsResult documents =
            prerequisites.Get(SourceLinkDocumentsQuery.Definition);
        switch (documents.Inspection.Value)
        {
            case FindingInspection<SourceDocumentObservation>.Absent absent:
                return new SourceAvailabilityResult.Absent(absent.Detail);

            case FindingInspection<SourceDocumentObservation>.Failed failed:
                return new SourceAvailabilityResult.Failed(failed.Error.Reason);

            case FindingInspection<SourceDocumentObservation>.Complete complete:
                try
                {
                    SourceAvailabilitySummary summary =
                        await SourceAvailabilityService.InspectAsync(
                            complete.Findings.Select(static finding => finding.Payload),
                            context.SourceClient,
                            context.Cache,
                            context.Log,
                            cancellationToken).ConfigureAwait(false);
                    return new SourceAvailabilityResult.Available(summary);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return new SourceAvailabilityResult.Failed(
                        $"Could not audit SourceLink availability: {ex.Message}");
                }

            default:
                throw new InvalidOperationException(
                    $"Unknown source-document result '{documents.Inspection.Value?.GetType().Name}'.");
        }
    }
}

/// <summary>
/// Downloads each verifiable source document and compares it with the portable-PDB checksum.
/// </summary>
public static class SourceIntegrityQuery
{
    public static InspectionQuery<SourceIntegrityResult> Definition { get; } =
        new("SourceLink integrity", InspectionCost.Unbounded);

    public static async ValueTask<SourceIntegrityResult> ExecuteAsync(
        SourceLinkQueryContext context,
        InspectionQueryResults prerequisites,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(prerequisites);

        SourceLinkDocumentsResult documents =
            prerequisites.Get(SourceLinkDocumentsQuery.Definition);
        switch (documents.Inspection.Value)
        {
            case FindingInspection<SourceDocumentObservation>.Absent absent:
                return new SourceIntegrityResult.Absent(absent.Detail);

            case FindingInspection<SourceDocumentObservation>.Failed failed:
                return new SourceIntegrityResult.Failed(failed.Error.Reason);

            case FindingInspection<SourceDocumentObservation>.Complete complete:
                try
                {
                    SourceIntegritySummary summary =
                        await SourceIntegrityService.InspectAsync(
                            complete.Findings.Select(static finding => finding.Payload),
                            context.SourceClient,
                            context.Cache,
                            context.Log,
                            cancellationToken).ConfigureAwait(false);
                    return new SourceIntegrityResult.Available(summary);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return new SourceIntegrityResult.Failed(
                        $"Could not audit SourceLink integrity: {ex.Message}");
                }

            default:
                throw new InvalidOperationException(
                    $"Unknown source-document result '{documents.Inspection.Value?.GetType().Name}'.");
        }
    }
}
