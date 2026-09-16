using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Platforms.Packages;

/// <summary>
/// Realizes Platform reference distributions through host-authorized Package Source operations.
/// Each call consumes its operation lease; clients, stores and retained package content stay caller-owned.
/// </summary>
public sealed partial class PackagePlatformSource
{
    private readonly object _association = new();
    private readonly IPackageSourceAuthorization _authorization;
    private readonly PackagePayloadAcquisitionPlan _payloads;

    public PackagePlatformSource(
        IPackageSourceAuthorization authorization,
        PackagePayloadAcquisitionPlan payloads,
        PackagePlatformSourceLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(payloads);
        _authorization = authorization;
        _payloads = payloads;
        Limits = limits ?? new();
    }

    public PackagePlatformSourceLimits Limits { get; }

    public Task<PackagePlatformSourceOutcome<PackagePlatformTargetInventory>> DiscoverAsync(
        PackageReferenceDiscoveryRequest request, PackageSourceOperationLease operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return DiscoverCoreAsync(request, operation);
    }

    /// <summary>Consumes the unchanged package candidate issued by source discovery.</summary>
    public Task<PackagePlatformSourceOutcome<PackageReferenceRealization>> RealizeAsync(
        PackagePlatformTargetSelection selection,
        PackageReferencePopulationDemand population,
        PackageReferenceWorkBudget work,
        PackageSourceOperationLease operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RealizeCoreAsync(selection?.Coordinate, selection, population, work, operation);
    }

    /// <summary>Authorizes an exact target established outside this source's discovery.</summary>
    public Task<PackagePlatformSourceOutcome<PackageReferenceRealization>> RealizeAsync(
        PackageReferencePackCoordinate coordinate,
        PackageReferencePopulationDemand population,
        PackageReferenceWorkBudget work,
        PackageSourceOperationLease operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RealizeCoreAsync(coordinate, null, population, work, operation);
    }

    async Task<PackagePlatformSourceOutcome<PackagePlatformTargetInventory>> DiscoverCoreAsync(
        PackageReferenceDiscoveryRequest request, PackageSourceOperationLease operation)
    {
        using (operation)
        {
            ArgumentNullException.ThrowIfNull(request);
            var generation = new PackagePlatformSourceGeneration();
            ImmutableArray<PackageAuthorityFailure> packageFailures = [];
            try
            {
                operation.ThrowIfExpired();
                if (!IsSupported(request.TargetFramework))
                    return new PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Succeeded(
                        generation, new(generation, []));

                string packageId = PackageReferencePackCoordinate.ReferencePackageId(request.Family);
                PackageSourceAuthorization authorization = _authorization.AuthorizeSourcesFor(packageId);
                operation.ThrowIfExpired();
                if (authorization.Authorities.Count == 0 && authorization.Failures.Count == 0)
                    return Unavailable<PackagePlatformTargetInventory>(generation,
                        PackagePlatformSourceDiagnosticKind.AuthorizationDenied,
                        authorization.DenialReason ?? "No package authority is authorized for this reference distribution.");
                PackageVersionDiscoveryResult discovery = await operation.DiscoverVersionsAsync(
                    packageId, authorization,
                    PackageVersionDiscoveryContract.CompleteVersionEnumeration).ConfigureAwait(false);
                packageFailures = [.. discovery.Failures];
                operation.ThrowIfExpired();
                if (discovery.State != PackageVersionDiscoveryState.Authoritative)
                {
                    var diagnostic = Diagnostic(
                        PackagePlatformSourceDiagnosticKind.PackageFailure,
                        "Package Source could not establish a complete reference-pack version inventory.",
                        discovery.Failures);
                    return discovery.State == PackageVersionDiscoveryState.Partial
                        || discovery.Failures.Any(static failure => failure.Kind == PackageAuthorityFailureKind.Timeout)
                        ? new PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Incomplete(generation, diagnostic)
                        : new PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Failed(generation, diagnostic);
                }

                int maximum = Math.Min(request.MaxCandidates, Limits.MaxCandidates);
                var targets = new List<PackagePlatformTargetSelection>();
                foreach (string text in discovery.Versions)
                {
                    operation.ThrowIfExpired();
                    if (!PlatformVersion.TryParse(text, out PlatformVersion? version)
                        || version.BuildMetadata is not null
                        || version.Major != request.TargetFramework.Major
                        || version.Minor != request.TargetFramework.Minor)
                        continue;
                    if (targets.Count == maximum)
                        return Incomplete<PackagePlatformTargetInventory>(generation,
                            "The reference-pack target inventory exceeds the candidate allowance.");
                    var coordinate = new PackageReferencePackCoordinate(
                        new(request.Family, request.TargetFramework, version));
                    targets.Add(new(_association, generation, coordinate, discovery.SelectCandidate(text)));
                }

                targets.Sort(static (left, right) =>
                {
                    int precedence = PlatformVersion.SemanticPrecedenceComparer.Compare(
                        left.Target.Version, right.Target.Version);
                    return precedence != 0 ? precedence
                        : string.CompareOrdinal(left.Target.Version.Value, right.Target.Version.Value);
                });
                operation.ThrowIfExpired();
                return new PackagePlatformSourceOutcome<PackagePlatformTargetInventory>.Succeeded(
                    generation, new(generation, [.. targets]));
            }
            catch (NuGetOperationTimeoutException)
            {
                return Timeout<PackagePlatformTargetInventory>(generation, packageFailures);
            }
        }
    }

    async Task<PackagePlatformSourceOutcome<PackageReferenceRealization>> RealizeCoreAsync(
        PackageReferencePackCoordinate? coordinate,
        PackagePlatformTargetSelection? selection,
        PackageReferencePopulationDemand population,
        PackageReferenceWorkBudget work,
        PackageSourceOperationLease operation)
    {
        using (operation)
        {
            ArgumentNullException.ThrowIfNull(coordinate);
            ArgumentNullException.ThrowIfNull(population);
            ArgumentNullException.ThrowIfNull(work);
            var generation = new PackagePlatformSourceGeneration();
            ImmutableArray<PackageAuthorityFailure> packageFailures = [];
            try
            {
                operation.ThrowIfExpired();
                if (!IsSupported(coordinate.Target.TargetFramework))
                    return Unavailable<PackageReferenceRealization>(generation,
                        PackagePlatformSourceDiagnosticKind.UnsupportedTarget,
                        "This target predates the supported reference-pack distribution layout.");
                if (coordinate.Target.Version.BuildMetadata is not null
                    || !TryPackageCoordinate(coordinate, out PackageSourceCoordinate? packageCoordinate))
                    return Rejected<PackageReferenceRealization>(generation,
                        PackagePlatformSourceDiagnosticKind.InvalidCoordinate,
                        "The exact Platform version cannot be represented by a reference-package coordinate.");
                if (selection is not null && !ReferenceEquals(selection.Source, _association))
                    return Rejected<PackageReferenceRealization>(generation,
                        PackagePlatformSourceDiagnosticKind.InvalidSelection,
                        "The target selection belongs to another package-backed Platform source.");

                PackageAcquisitionCandidate candidate;
                if (selection is not null)
                {
                    candidate = selection.Candidate;
                }
                else
                {
                    PackageAcquisitionCandidateResult resolved =
                        await operation.ResolvePinnedCandidateAsync(_authorization, packageCoordinate!).ConfigureAwait(false);
                    packageFailures = [.. resolved.Failures];
                    operation.ThrowIfExpired();
                    if (resolved.Candidate is not { } pinned)
                    {
                        var diagnostic = Diagnostic(PackagePlatformSourceDiagnosticKind.AuthorizationDenied,
                            "Package Source did not authorize the exact reference package.", resolved.Failures);
                        return resolved.State == PackageAcquisitionCandidateResultState.Incomplete
                            ? new PackagePlatformSourceOutcome<PackageReferenceRealization>.Incomplete(generation, diagnostic)
                            : new PackagePlatformSourceOutcome<PackageReferenceRealization>.Unavailable(generation, diagnostic);
                    }
                    candidate = pinned;
                }

                ConfiguredPackagePayloadResult acquired = await operation.AcquireCandidatePayloadAsync(
                    candidate, _payloads.GetStore, _payloads.Log, _payloads.Limits,
                    _payloads.TransferPolicy).ConfigureAwait(false);
                packageFailures = [.. packageFailures, .. acquired.Failures];
                operation.ThrowIfExpired();
                if (acquired.Payload is not { } payload)
                {
                    var diagnostic = Diagnostic(PackagePlatformSourceDiagnosticKind.PackageUnavailable,
                        "The authorized reference package could not be acquired.",
                        packageFailures);
                    if (acquired.Failures.Any(static failure => failure.Kind == PackageAuthorityFailureKind.Timeout))
                        return new PackagePlatformSourceOutcome<PackageReferenceRealization>.Incomplete(generation, diagnostic);
                    if (acquired.Failures.Any(static failure => failure.Kind is PackageAuthorityFailureKind.Input
                        or PackageAuthorityFailureKind.InvalidResponse or PackageAuthorityFailureKind.ResponseRejected))
                        return Rejected<PackageReferenceRealization>(generation,
                            PackagePlatformSourceDiagnosticKind.PackageFailure,
                            "Authorized reference-package evidence was rejected during payload acquisition.", packageFailures);
                    return acquired.Failures.Count != 0
                        ? new PackagePlatformSourceOutcome<PackageReferenceRealization>.Failed(generation, diagnostic)
                        : new PackagePlatformSourceOutcome<PackageReferenceRealization>.Unavailable(generation, diagnostic);
                }
                if (payload.Coordinate != packageCoordinate)
                    return Rejected<PackageReferenceRealization>(generation,
                        PackagePlatformSourceDiagnosticKind.InvalidCoordinate,
                        "The acquired reference package does not match the exact Platform coordinate.", packageFailures);

                PackagePlatformSourceOutcome<PackageReferenceRealization> outcome = await SnapshotAsync(
                    generation, coordinate, population, work, candidate, acquired,
                    packageFailures, operation).ConfigureAwait(false);
                return outcome is PackagePlatformSourceOutcome<PackageReferenceRealization>.NotSucceeded failure
                    ? failure.WithPackageFailures(packageFailures)
                    : outcome;
            }
            catch (NuGetOperationTimeoutException)
            {
                return Timeout<PackageReferenceRealization>(generation, packageFailures);
            }
            catch (OperationCanceledException) when (
                !operation.CancellationToken.IsCancellationRequested
                && operation.OperationCancellationToken.IsCancellationRequested)
            {
                return Timeout<PackageReferenceRealization>(generation, packageFailures);
            }
            catch (InvalidDataException)
            {
                return Rejected<PackageReferenceRealization>(generation,
                    PackagePlatformSourceDiagnosticKind.InvalidLayout,
                    "The retained reference package contains invalid entry data.", packageFailures);
            }
            catch (IOException)
            {
                return new PackagePlatformSourceOutcome<PackageReferenceRealization>.Failed(generation,
                    Diagnostic(PackagePlatformSourceDiagnosticKind.ContentReadFailure,
                        "The retained reference package could not be read.", packageFailures));
            }
        }
    }

    async Task<PackagePlatformSourceOutcome<PackageReferenceRealization>> SnapshotAsync(
        PackagePlatformSourceGeneration generation,
        PackageReferencePackCoordinate coordinate,
        PackageReferencePopulationDemand population,
        PackageReferenceWorkBudget work,
        PackageAcquisitionCandidate candidate,
        ConfiguredPackagePayloadResult acquired,
        ImmutableArray<PackageAuthorityFailure> packageFailures,
        PackageSourceOperationLease operation)
    {
        AcquiredPackageSourcePayload payload = acquired.Payload!;
        IPackageContent content = payload.Content;
        string prefix = $"ref/{coordinate.Target.TargetFramework}/";
        string? requestedName = null;
        if (population is PackageReferencePopulationDemand.Assembly assembly)
        {
            if (string.IsNullOrEmpty(assembly.Identity.Name)
                || assembly.Identity.Name.AsSpan().ContainsAny('/', '\\', '\0'))
                return Rejected<PackageReferenceRealization>(generation,
                    PackagePlatformSourceDiagnosticKind.InvalidCoordinate,
                    "The assembly name cannot be projected to a reference-pack member.");
            requestedName = assembly.Identity.Name + ".dll";
        }

        int observed = 0;
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in content.EnumerateEntries())
        {
            operation.ThrowIfExpired();
            if (observed == Limits.MaxObservedEntries)
                return Incomplete<PackageReferenceRealization>(generation,
                    "Reference-pack enumeration exceeds the package-entry allowance.");
            observed++;
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            ReadOnlySpan<char> name = path.AsSpan(prefix.Length);
            if (name.Contains('/') || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || (requestedName is not null && !name.Equals(requestedName, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!seen.Add(path))
                return Rejected<PackageReferenceRealization>(generation,
                    PackagePlatformSourceDiagnosticKind.InvalidLayout,
                    "Reference-pack entries collide at the selected logical coordinate.");
            paths.Add(path);
        }

        if (paths.Count == 0)
            return Unavailable<PackageReferenceRealization>(generation,
                PackagePlatformSourceDiagnosticKind.MemberUnavailable,
                "The requested reference-pack membership is absent.");
        if (paths.Count > Math.Min(work.MaxAssemblies, Limits.MaxAssemblies))
            return Incomplete<PackageReferenceRealization>(generation,
                "The reference population exceeds the assembly allowance.");
        paths.Sort(StringComparer.Ordinal);

        var libraries = ImmutableArray.CreateBuilder<PackageReferenceLibrary>(paths.Count);
        var identities = new HashSet<AssemblyReferenceIdentity>(AssemblyReferenceIdentity.EquivalentComparer);
        long remaining = Math.Min(work.MaxBytes, Limits.MaxBytes);
        foreach (string path in paths)
        {
            operation.ThrowIfExpired();
            long allowed = Math.Min(remaining, Limits.MaxEntryBytes);
            if (content is IPackageContentEntryManifest manifest
                && manifest.TryGetEntryLength(path, out long length)
                && length > allowed)
                return Incomplete<PackageReferenceRealization>(generation,
                    "A reference assembly exceeds the byte allowance.");
            if (!content.TryOpenEntry(path, allowed, out Stream? stream))
                return Rejected<PackageReferenceRealization>(generation,
                    PackagePlatformSourceDiagnosticKind.InvalidLayout,
                    "A selected reference entry is missing from the retained package.");
            byte[] bytes;
            await using (stream)
            {
                if (stream.CanSeek && stream.Length > allowed)
                    return Incomplete<PackageReferenceRealization>(generation,
                        "A reference assembly exceeds the byte allowance.");
                using var snapshot = new MemoryStream();
                byte[] buffer = new byte[81920];
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, operation.OperationCancellationToken).ConfigureAwait(false);
                    operation.ThrowIfExpired();
                    if (read == 0)
                        break;
                    if (read > allowed - snapshot.Length)
                        return Incomplete<PackageReferenceRealization>(generation,
                            "A reference assembly exceeds the observed byte allowance.");
                    snapshot.Write(buffer, 0, read);
                }
                bytes = snapshot.ToArray();
            }

            AssemblyReferenceIdentity identity;
            try
            {
                using var pe = new PEReader(new MemoryStream(bytes, writable: false));
                if (!pe.HasMetadata)
                    return Malformed(generation);
                MetadataReader metadata = pe.GetMetadataReader();
                if (metadata.MetadataKind != MetadataKind.Ecma335 || !metadata.IsAssembly)
                    return Malformed(generation);
                identity = AssemblyReferenceIdentity.FromAssemblyDefinition(metadata);
            }
            catch (BadImageFormatException)
            {
                return Malformed(generation);
            }
            if (population is PackageReferencePopulationDemand.Assembly exact
                && !identity.IsEquivalentTo(exact.Identity))
                return Rejected<PackageReferenceRealization>(generation,
                    PackagePlatformSourceDiagnosticKind.AssemblyIdentityMismatch,
                    "The reference member does not match the requested assembly identity.");
            if (!identities.Add(identity))
                return Rejected<PackageReferenceRealization>(generation,
                    PackagePlatformSourceDiagnosticKind.DuplicateAssemblyIdentity,
                    "The reference population contains equivalent assembly identities.");
            libraries.Add(new(path, identity, bytes));
            remaining -= bytes.LongLength;
        }

        operation.ThrowIfExpired();
        return new PackagePlatformSourceOutcome<PackageReferenceRealization>.Succeeded(generation,
            new(generation, coordinate, population, candidate, acquired.Authority!, acquired.Source!,
                content.GenerationIdentity, payload.Origin, packageFailures, libraries.MoveToImmutable()));
    }

    static bool IsSupported(PlatformTargetFramework framework) =>
        framework.Form == PlatformTargetFrameworkForm.Net || framework.Major == 3;

    static bool TryPackageCoordinate(
        PackageReferencePackCoordinate coordinate, out PackageSourceCoordinate? package)
    {
        try
        {
            package = PackageSourceCoordinate.Create(coordinate.PackageId, coordinate.Target.Version.Value);
            return package.Version.Equals(coordinate.Target.Version.Value, StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            package = null;
            return false;
        }
    }

    static PackagePlatformSourceDiagnostic Diagnostic(
        PackagePlatformSourceDiagnosticKind kind, string summary,
        IEnumerable<PackageAuthorityFailure>? failures = null) =>
        new(kind, summary, failures is null ? [] : [.. failures]);

    static PackagePlatformSourceOutcome<T> Incomplete<T>(
        PackagePlatformSourceGeneration generation, string summary) where T : notnull =>
        new PackagePlatformSourceOutcome<T>.Incomplete(generation,
            Diagnostic(PackagePlatformSourceDiagnosticKind.WorkLimitExceeded, summary));

    static PackagePlatformSourceOutcome<T> Timeout<T>(
        PackagePlatformSourceGeneration generation, IEnumerable<PackageAuthorityFailure>? failures = null) where T : notnull =>
        new PackagePlatformSourceOutcome<T>.Incomplete(generation,
            Diagnostic(PackagePlatformSourceDiagnosticKind.Timeout,
                "Package-backed Platform work exceeded its operation deadline.", failures));

    static PackagePlatformSourceOutcome<T> Rejected<T>(
        PackagePlatformSourceGeneration generation, PackagePlatformSourceDiagnosticKind kind,
        string summary, IEnumerable<PackageAuthorityFailure>? failures = null) where T : notnull =>
        new PackagePlatformSourceOutcome<T>.Rejected(generation, Diagnostic(kind, summary, failures));

    static PackagePlatformSourceOutcome<T> Unavailable<T>(
        PackagePlatformSourceGeneration generation, PackagePlatformSourceDiagnosticKind kind,
        string summary) where T : notnull =>
        new PackagePlatformSourceOutcome<T>.Unavailable(generation, Diagnostic(kind, summary));

    static PackagePlatformSourceOutcome<PackageReferenceRealization> Malformed(
        PackagePlatformSourceGeneration generation) =>
        Rejected<PackageReferenceRealization>(generation, PackagePlatformSourceDiagnosticKind.MalformedAssembly,
            "A reference-pack DLL is not a supported ECMA-335 assembly.");
}
