using DotnetInspector.Queries;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGet.Versioning;

namespace DotnetInspector.Inspectors;

internal sealed record PackageIntegrationAssembly(
    string Path,
    string? TargetFramework,
    string? ContextKey = null);

internal sealed record PackageIntegrationPreflightFailure(
    string Reason,
    Exception? AdmissionException = null);

internal sealed class PackageIntegrationAcquisition
{
    readonly string? _packageId;
    readonly string? _packageVersion;

    PackageIntegrationAcquisition(
        string? packageId,
        string? packageVersion)
    {
        _packageId = packageId;
        _packageVersion = packageVersion;
    }

    internal static PackageIntegrationAcquisition Remote(
        string packageId,
        string packageVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageVersion);
        return new PackageIntegrationAcquisition(
            packageId.Trim(),
            packageVersion.Trim());
    }

    internal static PackageIntegrationAcquisition Remote(
        PackageExtractionResult resolution,
        string fallbackPackageId,
        string fallbackPackageVersion)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        return Remote(
            resolution.PackageName ?? fallbackPackageId,
            resolution.Version ?? fallbackPackageVersion);
    }

    internal static PackageIntegrationAcquisition Local(
        string? packageId,
        string? packageVersion)
    {
        string? normalizedId = NormalizePackageId(packageId);
        string? normalizedVersion =
            NuGetVersion.TryParse(packageVersion?.Trim(), out var parsed)
                ? parsed.ToNormalizedString()
                : null;
        return normalizedId is not null && normalizedVersion is not null
            ? new PackageIntegrationAcquisition(
                normalizedId,
                normalizedVersion)
            : new PackageIntegrationAcquisition(null, null);
    }

    internal AssemblyResolutionProvenance CreateProvenance(
        string? targetFramework) =>
        _packageId is not null && _packageVersion is not null
            ? AssemblyResolutionProvenance.Package(
                _packageId,
                _packageVersion,
                targetFramework,
                rid: null)
            : AssemblyResolutionProvenance.Local(
                "local package archive");

    static string? NormalizePackageId(string? packageId)
    {
        string? candidate = packageId?.Trim();
        if (candidate is not { Length: > 0 and <= 100 })
            return null;

        bool previousWasSeparator = false;
        for (int index = 0; index < candidate.Length; index++)
        {
            char character = candidate[index];
            bool asciiAlphaNumeric =
                character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9';
            bool word = asciiAlphaNumeric || character == '_';
            bool separator = character is '.' or '-';
            if (!word && !separator)
            {
                return null;
            }

            if (separator
                && (index == 0
                    || index == candidate.Length - 1
                    || previousWasSeparator))
            {
                return null;
            }

            previousWasSeparator = separator;
        }

        return candidate;
    }
}

/// <summary>
/// Owns the binding-consistent package groups used by one all-library
/// Integrations request.
/// </summary>
internal sealed class PackageIntegrationsWorkspace : IDisposable
{
    readonly InspectionWorkspace _workspace;
    readonly Dictionary<string, ParticipantResult> _participants;
    readonly Dictionary<string, PackageIntegrationPreflightFailure>
        _preflightFailures;
    readonly bool _includeIntegrationOpportunities;

    PackageIntegrationsWorkspace(
        InspectionWorkspace workspace,
        Dictionary<string, ParticipantResult> participants,
        Dictionary<string, PackageIntegrationPreflightFailure>
            preflightFailures,
        int contextGroupCount,
        bool includeIntegrationOpportunities)
    {
        _workspace = workspace;
        _participants = participants;
        _preflightFailures = preflightFailures;
        _includeIntegrationOpportunities =
            includeIntegrationOpportunities;
        ContextGroupCount = contextGroupCount;
    }

    internal int ContextGroupCount { get; }

    internal long RetainedImageBytes =>
        _participants.Values
            .Select(static participant => participant.Group)
            .Distinct()
            .Sum(static group => group.RetainedImageBytes);

    internal static PackageIntegrationsWorkspace Create(
        IEnumerable<PackageIntegrationAssembly> assemblies,
        string packageName,
        string packageVersion,
        bool includeIntegrationOpportunities = false) =>
        Create(
            assemblies,
            PackageIntegrationAcquisition.Remote(
                packageName,
                packageVersion),
            includeIntegrationOpportunities:
                includeIntegrationOpportunities);

    internal static PackageIntegrationsWorkspace Create(
        IEnumerable<PackageIntegrationAssembly> assemblies,
        PackageIntegrationAcquisition acquisition,
        long? maxRetainedImageBytes = null,
        bool includeIntegrationOpportunities = false)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(acquisition);

        var workspace = new InspectionWorkspace();
        try
        {
            var results = new Dictionary<string, ParticipantResult>(
                StringComparer.Ordinal);
            var preflightFailures =
                new Dictionary<
                    string,
                    PackageIntegrationPreflightFailure>(
                        StringComparer.Ordinal);
            int contextGroupCount = 0;
            foreach (IGrouping<string, PackageIntegrationAssembly> context
                in assemblies.GroupBy(
                    static assembly =>
                        assembly.ContextKey
                        ?? assembly.TargetFramework
                        ?? "",
                    StringComparer.OrdinalIgnoreCase))
            {
                List<Root> roots = [];
                foreach (PackageIntegrationAssembly assembly in context)
                {
                    var provenance = acquisition.CreateProvenance(
                        assembly.TargetFramework);
                    ResolvedAssemblyReference? reference;
                    try
                    {
                        reference =
                            ResolvedAssemblyReference
                                .CreateFromPathIfManaged(
                                    assembly.Path,
                                    provenance);
                    }
                    catch (UnsupportedMetadataFormatException ex)
                    {
                        preflightFailures.Add(
                            Path.GetFullPath(assembly.Path),
                            new PackageIntegrationPreflightFailure(
                                "The selected image uses an unsupported metadata format.",
                                ex));
                        continue;
                    }
                    catch (MalformedMetadataRootException ex)
                    {
                        preflightFailures.Add(
                            Path.GetFullPath(assembly.Path),
                            new PackageIntegrationPreflightFailure(
                                "The selected image contains invalid metadata.",
                                ex));
                        continue;
                    }
                    catch (Exception ex) when (
                        ex is BadImageFormatException
                            or ArgumentOutOfRangeException
                            or OverflowException)
                    {
                        preflightFailures.Add(
                            Path.GetFullPath(assembly.Path),
                            new PackageIntegrationPreflightFailure(
                                "The selected image contains invalid metadata."));
                        continue;
                    }
                    catch (Exception ex) when (
                        ex is IOException
                            or UnauthorizedAccessException
                            or NotSupportedException
                            or ObjectDisposedException)
                    {
                        preflightFailures.Add(
                            Path.GetFullPath(assembly.Path),
                            new PackageIntegrationPreflightFailure(
                                "The selected image could not be read."));
                        continue;
                    }

                    if (reference is null)
                        continue;

                    var policy = new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(
                            reference.Path!)
                        {
                            TargetFramework =
                                assembly.TargetFramework,
                        });
                    roots.Add(new Root(assembly, reference, policy));
                }

                if (roots.Count == 0)
                    continue;

                var groupPolicy =
                    new SourceRelativeAssemblyGroupBindingPolicy(
                        roots.Select(static root =>
                            (root.Reference,
                                (IAssemblyBindingPolicy)root.Policy)));
                List<AssemblyContextParticipant> participants =
                [
                    .. roots.Select(root =>
                        new AssemblyContextParticipant(
                            root.Reference,
                            groupPolicy)),
                ];
                AssemblyContextGroup group =
                    workspace.CreateAssemblyContextGroup(
                        participants,
                        maxRetainedImageBytes is long maxBytes
                            ? new AssemblyContextGroupOptions
                            {
                                MaxRetainedImageBytes = maxBytes,
                            }
                            : null);

                for (int index = 0; index < roots.Count; index++)
                {
                    Root root = roots[index];
                    results.Add(
                        Path.GetFullPath(root.Input.Path),
                        new ParticipantResult(
                            group,
                            participants[index]));
                }

                contextGroupCount++;
            }

            return new PackageIntegrationsWorkspace(
                workspace,
                results,
                preflightFailures,
                contextGroupCount,
                includeIntegrationOpportunities);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    internal async Task<TResult> UseAssemblyAsync<TResult>(
        string path,
        Func<
            ResolvedAssemblyReference?,
            AssemblyIntegrationsEntry?,
            AssemblyIntegrationOpportunitiesEntry?,
            Task<TResult>> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(callback);

        if (!_participants.TryGetValue(
                Path.GetFullPath(path),
                out ParticipantResult? participant))
        {
            return await callback(null, null, null).ConfigureAwait(false);
        }

        if (_includeIntegrationOpportunities)
        {
            return await AssemblyContextIntegrationOpportunitiesQuery
                .ExecuteParticipantAsync(
                    participant.Group,
                    participant.Participant,
                    callback)
                .ConfigureAwait(false);
        }

        return await AssemblyContextIntegrationsQuery
            .ExecuteParticipantAsync(
                participant.Group,
                participant.Participant,
                (retained, integrations) =>
                    callback(retained, integrations, null))
            .ConfigureAwait(false);
    }

    internal bool TryGetPreflightFailure(
        string path,
        out PackageIntegrationPreflightFailure failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_preflightFailures.TryGetValue(
                Path.GetFullPath(path),
                out PackageIntegrationPreflightFailure? result))
        {
            failure = result;
            return true;
        }

        failure = null!;
        return false;
    }

    public void Dispose() => _workspace.Dispose();

    sealed record Root(
        PackageIntegrationAssembly Input,
        ResolvedAssemblyReference Reference,
        AssemblyDependencyResolver Policy);

    sealed record ParticipantResult(
        AssemblyContextGroup Group,
        AssemblyContextParticipant Participant);
}
