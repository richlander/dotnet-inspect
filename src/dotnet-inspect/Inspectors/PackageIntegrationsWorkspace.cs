using DotnetInspector.Queries;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Metadata;
using NuGet.Versioning;

namespace DotnetInspector.Inspectors;

internal sealed record PackageIntegrationAssembly(
    string Path,
    string? TargetFramework);

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
    readonly Dictionary<string, string> _preflightFailures;

    PackageIntegrationsWorkspace(
        InspectionWorkspace workspace,
        Dictionary<string, ParticipantResult> participants,
        Dictionary<string, string> preflightFailures,
        int contextGroupCount)
    {
        _workspace = workspace;
        _participants = participants;
        _preflightFailures = preflightFailures;
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
        string packageVersion) =>
        Create(
            assemblies,
            PackageIntegrationAcquisition.Remote(
                packageName,
                packageVersion));

    internal static PackageIntegrationsWorkspace Create(
        IEnumerable<PackageIntegrationAssembly> assemblies,
        PackageIntegrationAcquisition acquisition,
        long? maxRetainedImageBytes = null)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(acquisition);

        var workspace = new InspectionWorkspace();
        try
        {
            var results = new Dictionary<string, ParticipantResult>(
                StringComparer.Ordinal);
            var preflightFailures = new Dictionary<string, string>(
                StringComparer.Ordinal);
            int contextGroupCount = 0;
            foreach (IGrouping<string, PackageIntegrationAssembly> context
                in assemblies.GroupBy(
                    static assembly => assembly.TargetFramework ?? "",
                    StringComparer.OrdinalIgnoreCase))
            {
                List<Root> roots = [];
                foreach (PackageIntegrationAssembly assembly in context)
                {
                    var provenance = acquisition.CreateProvenance(
                        assembly.TargetFramework);
                    if (!ResolvedAssemblyReference.TryCreateFromPath(
                            assembly.Path,
                            provenance,
                            out ResolvedAssemblyReference? reference,
                            out Exception? failure))
                    {
                        if (failure is
                            ArgumentOutOfRangeException
                            or OverflowException)
                        {
                            preflightFailures.Add(
                                Path.GetFullPath(assembly.Path),
                                "The selected image contains invalid metadata.");
                        }
                        continue;
                    }

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
                contextGroupCount);
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
            Task<TResult>> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(callback);

        if (!_participants.TryGetValue(
                Path.GetFullPath(path),
                out ParticipantResult? participant))
        {
            return await callback(null, null).ConfigureAwait(false);
        }

        return await AssemblyContextIntegrationsQuery
            .ExecuteParticipantAsync(
                participant.Group,
                participant.Participant,
                callback)
            .ConfigureAwait(false);
    }

    internal bool TryGetPreflightFailure(
        string path,
        out string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_preflightFailures.TryGetValue(
                Path.GetFullPath(path),
                out string? failure))
        {
            reason = failure;
            return true;
        }

        reason = "";
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
