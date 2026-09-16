using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Packages;
using DotnetInspector.Platforms.Formats;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Platforms.Packages;

public sealed partial class PackagePlatformSource
{
    private static readonly PlatformFrameworkName DotNetRuntimeName =
        PlatformFrameworkName.Parse("Microsoft.NETCore.App");
    private static readonly PlatformFrameworkName AspNetCoreName =
        PlatformFrameworkName.Parse("Microsoft.AspNetCore.App");

    /// <summary>
    /// Realizes one manifest-defined implementation closure from authorized
    /// RID-specific runtime packs.
    /// </summary>
    public Task<PackagePlatformSourceOutcome<PackageImplementationRealization>>
        RealizeImplementationAsync(
            PackageImplementationPlatformCoordinate coordinate,
            PackageImplementationWorkBudget work,
            PackageSourceOperationLease operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RealizeImplementationCoreAsync(
            coordinate,
            work,
            operation);
    }

    private async Task<
        PackagePlatformSourceOutcome<PackageImplementationRealization>>
        RealizeImplementationCoreAsync(
            PackageImplementationPlatformCoordinate coordinate,
            PackageImplementationWorkBudget work,
            PackageSourceOperationLease operation)
    {
        using (operation)
        {
            ArgumentNullException.ThrowIfNull(coordinate);
            ArgumentNullException.ThrowIfNull(work);
            var generation = new PackagePlatformSourceGeneration();
            var attempt = new ImplementationAttempt(
                this,
                coordinate,
                work,
                generation,
                operation);
            try
            {
                operation.ThrowIfExpired();
                if (!IsSupported(coordinate.Target.TargetFramework))
                {
                    return Unavailable<PackageImplementationRealization>(
                        generation,
                        PackagePlatformSourceDiagnosticKind.UnsupportedTarget,
                        "This target predates the supported runtime-pack distribution layout.");
                }
                if (coordinate.Target.Version.BuildMetadata is not null)
                {
                    return Rejected<PackageImplementationRealization>(
                        generation,
                        PackagePlatformSourceDiagnosticKind.InvalidCoordinate,
                        "The exact Platform version cannot be represented by a runtime-package coordinate.");
                }
                if (!attempt.HasPositiveBudget)
                {
                    return Incomplete<PackageImplementationRealization>(
                        generation,
                        "The request work budget does not permit implementation realization.");
                }

                PackageImplementationRealization realization =
                    await attempt.RunAsync().ConfigureAwait(false);
                return new PackagePlatformSourceOutcome<
                    PackageImplementationRealization>.Succeeded(
                        generation,
                        realization);
            }
            catch (NuGetOperationTimeoutException)
            {
                return Timeout<PackageImplementationRealization>(
                    generation,
                    attempt.PackageFailures);
            }
            catch (OperationCanceledException) when (
                !operation.CancellationToken.IsCancellationRequested
                && operation.OperationCancellationToken
                    .IsCancellationRequested)
            {
                return Timeout<PackageImplementationRealization>(
                    generation,
                    attempt.PackageFailures);
            }
            catch (ImplementationAttemptException failure)
            {
                PackagePlatformSourceDiagnostic diagnostic = Diagnostic(
                    failure.Kind,
                    failure.Message,
                    attempt.PackageFailures);
                return failure.Outcome switch
                {
                    ImplementationAttemptOutcome.Unavailable =>
                        new PackagePlatformSourceOutcome<
                            PackageImplementationRealization>.Unavailable(
                                generation,
                                diagnostic),
                    ImplementationAttemptOutcome.Rejected =>
                        new PackagePlatformSourceOutcome<
                            PackageImplementationRealization>.Rejected(
                                generation,
                                diagnostic),
                    ImplementationAttemptOutcome.Incomplete =>
                        new PackagePlatformSourceOutcome<
                            PackageImplementationRealization>.Incomplete(
                                generation,
                                diagnostic),
                    ImplementationAttemptOutcome.Failed =>
                        new PackagePlatformSourceOutcome<
                            PackageImplementationRealization>.Failed(
                                generation,
                                diagnostic),
                    _ => throw new InvalidOperationException(
                        "Unknown implementation-attempt outcome."),
                };
            }
            catch (InvalidDataException)
            {
                return Rejected<PackageImplementationRealization>(
                    generation,
                    PackagePlatformSourceDiagnosticKind.InvalidLayout,
                    "A retained runtime package contains invalid entry data.",
                    attempt.PackageFailures);
            }
            catch (IOException)
            {
                return new PackagePlatformSourceOutcome<
                    PackageImplementationRealization>.Failed(
                        generation,
                        Diagnostic(
                            PackagePlatformSourceDiagnosticKind
                                .ContentReadFailure,
                            "A retained runtime package could not be read.",
                            attempt.PackageFailures));
            }
        }
    }

    private sealed class ImplementationAttempt
    {
        private readonly PackagePlatformSource _source;
        private readonly PackageImplementationPlatformCoordinate _coordinate;
        private readonly PackageImplementationWorkBudget _work;
        private readonly PackagePlatformSourceGeneration _generation;
        private readonly PackageSourceOperationLease _operation;
        private readonly List<PackageAuthorityFailure> _packageFailures = [];
        private long _remainingBytes;
        private int _remainingManifestLibraries;
        private int _remainingManifestAssets;
        private int _observedEntries;
        private int _resolutionSteps;

        internal ImplementationAttempt(
            PackagePlatformSource source,
            PackageImplementationPlatformCoordinate coordinate,
            PackageImplementationWorkBudget work,
            PackagePlatformSourceGeneration generation,
            PackageSourceOperationLease operation)
        {
            _source = source;
            _coordinate = coordinate;
            _work = work;
            _generation = generation;
            _operation = operation;
            _remainingBytes = Math.Min(work.MaxBytes, source.Limits.MaxBytes);
            _remainingManifestLibraries = Math.Min(
                work.MaxManifestLibraries,
                source.Limits.MaxManifestLibraries);
            _remainingManifestAssets = Math.Min(
                work.MaxManifestAssets,
                source.Limits.MaxManifestAssets);
        }

        internal bool HasPositiveBudget =>
            Math.Min(_work.MaxFrameworks, _source.Limits.MaxFrameworks) > 0
            && Math.Min(
                    _work.MaxResolutionSteps,
                    _source.Limits.MaxResolutionSteps) > 0
            && Math.Min(
                    _work.MaxManifestLibraries,
                    _source.Limits.MaxManifestLibraries) > 0
            && Math.Min(
                    _work.MaxManifestAssets,
                    _source.Limits.MaxManifestAssets) > 0
            && Math.Min(
                    _work.MaxAssemblies,
                    _source.Limits.MaxAssemblies) > 0
            && _source.Limits.MaxObservedEntries > 0
            && _source.Limits.MaxManifestBytes > 0
            && _source.Limits.MaxEntryBytes > 0
            && _remainingBytes > 0;

        internal IReadOnlyList<PackageAuthorityFailure> PackageFailures =>
            _packageFailures;

        internal async Task<PackageImplementationRealization> RunAsync()
        {
            PlatformFamily rootFamily = _coordinate.Target.Family;
            Step();
            FrameworkPayload rootPayload =
                await AcquireExactFrameworkAsync(
                        rootFamily,
                        _coordinate.Target.Version)
                    .ConfigureAwait(false);
            FrameworkSnapshot root =
                await SnapshotFrameworkAsync(rootPayload)
                    .ConfigureAwait(false);

            var frameworks = new List<FrameworkSnapshot> { root };
            IReadOnlyList<PlatformFrameworkReference> references =
                root.RuntimeConfiguration.Frameworks;
            if (rootFamily == PlatformFamily.DotNetRuntime)
            {
                if (references.Count != 0)
                {
                    throw Reject(
                        PackagePlatformSourceDiagnosticKind
                            .InvalidFrameworkGraph,
                        "The .NET runtime pack cannot introduce another shared-framework dependency.");
                }
            }
            else
            {
                if (references.Count > 1)
                {
                    throw Reject(
                        PackagePlatformSourceDiagnosticKind
                            .InvalidFrameworkGraph,
                        "The ASP.NET Core runtime pack introduces more than one shared-framework dependency.");
                }

                foreach (PlatformFrameworkReference reference in references)
                {
                    _operation.ThrowIfExpired();
                    if (!reference.Name.Equals(DotNetRuntimeName))
                    {
                        throw Reject(
                            PackagePlatformSourceDiagnosticKind
                                .InvalidFrameworkGraph,
                            "The ASP.NET Core runtime pack names an unsupported shared-framework dependency.");
                    }

                    EnsureFrameworkCapacity(frameworks.Count + 1);
                    Step();
                    FrameworkPayload supportPayload =
                        await AcquireCompatibleFrameworkAsync(
                                PlatformFamily.DotNetRuntime,
                                reference)
                            .ConfigureAwait(false);
                    FrameworkSnapshot support =
                        await SnapshotFrameworkAsync(supportPayload)
                            .ConfigureAwait(false);
                    if (support.RuntimeConfiguration.Frameworks.Count != 0)
                    {
                        throw Reject(
                            PackagePlatformSourceDiagnosticKind
                                .InvalidFrameworkGraph,
                            "The .NET runtime support pack cannot introduce another shared-framework dependency.");
                    }

                    frameworks.Insert(0, support);
                }
            }

            EnsureFrameworkCapacity(frameworks.Count);
            ImmutableArray<PackageImplementationLibrary> libraries =
                await RealizeLibrariesAsync(frameworks)
                    .ConfigureAwait(false);
            _operation.ThrowIfExpired();
            return new PackageImplementationRealization(
                _generation,
                _coordinate,
                [.. frameworks.Select(
                    static framework => framework.Evidence)],
                libraries);
        }

        private async Task<FrameworkPayload> AcquireExactFrameworkAsync(
            PlatformFamily family,
            PlatformVersion version)
        {
            int failureStart = _packageFailures.Count;
            string packageId =
                PackageImplementationPlatformCoordinate.RuntimePackageId(
                    family,
                    _coordinate.RuntimeIdentifier);
            PackageSourceCoordinate packageCoordinate =
                RequirePackageCoordinate(packageId, version);
            PackageAcquisitionCandidateResult resolved =
                await _operation.ResolvePinnedCandidateAsync(
                        _source._authorization,
                        packageCoordinate)
                    .ConfigureAwait(false);
            AddFailures(resolved.Failures);
            _operation.ThrowIfExpired();
            if (resolved.Candidate is not { } candidate)
            {
                if (resolved.State
                    == PackageAcquisitionCandidateResultState.Incomplete)
                {
                    throw Incomplete(
                        "Package Source could not completely authorize the exact runtime package.");
                }

                throw Unavailable(
                    PackagePlatformSourceDiagnosticKind.AuthorizationDenied,
                    "Package Source did not authorize the exact runtime package.");
            }

            return await AcquireCandidateAsync(
                    family,
                    version,
                    packageCoordinate,
                    candidate,
                    failureStart)
                .ConfigureAwait(false);
        }

        private async Task<FrameworkPayload>
            AcquireCompatibleFrameworkAsync(
                PlatformFamily family,
                PlatformFrameworkReference reference)
        {
            int failureStart = _packageFailures.Count;
            string packageId =
                PackageImplementationPlatformCoordinate.RuntimePackageId(
                    family,
                    _coordinate.RuntimeIdentifier);
            PackageSourceAuthorization authorization =
                _source._authorization.AuthorizeSourcesFor(packageId);
            _operation.ThrowIfExpired();
            if (authorization.Authorities.Count == 0
                && authorization.Failures.Count == 0)
            {
                throw Unavailable(
                    PackagePlatformSourceDiagnosticKind.AuthorizationDenied,
                    "No package authority is authorized for a required runtime support distribution.");
            }

            PackageVersionDiscoveryResult discovery =
                await _operation.DiscoverVersionsAsync(
                        packageId,
                        authorization,
                        PackageVersionDiscoveryContract
                            .CompleteVersionEnumeration)
                    .ConfigureAwait(false);
            AddFailures(discovery.Failures);
            _operation.ThrowIfExpired();
            if (discovery.State != PackageVersionDiscoveryState.Authoritative)
            {
                if (discovery.State
                        == PackageVersionDiscoveryState.Partial
                    || discovery.Failures.Any(
                        static failure =>
                            failure.Kind
                            == PackageAuthorityFailureKind.Timeout))
                {
                    throw Incomplete(
                        "Package Source could not establish a complete runtime-pack version inventory.");
                }

                throw Failed(
                    "Package Source failed to establish a runtime-pack version inventory.");
            }

            var candidates = new List<VersionCandidate>();
            foreach (string text in discovery.Versions)
            {
                _operation.ThrowIfExpired();
                if (!PlatformVersion.TryParse(
                        text,
                        out PlatformVersion? version)
                    || version.BuildMetadata is not null
                    || version.Major
                        != _coordinate.Target.TargetFramework.Major
                    || version.Minor
                        != _coordinate.Target.TargetFramework.Minor)
                {
                    continue;
                }
                if (candidates.Count
                    == _source.Limits.MaxCandidates)
                {
                    throw Incomplete(
                        "The runtime-pack version inventory exceeds the candidate allowance.");
                }

                candidates.Add(new(text, version));
            }

            VersionCandidate? selected = SelectVersion(
                candidates,
                EffectiveFrameworkReference.From(reference));
            if (selected is null)
            {
                throw Unavailable(
                    PackagePlatformSourceDiagnosticKind.PackageUnavailable,
                    "No authorized runtime support package satisfies the manifest reference.");
            }

            PackageSourceCoordinate packageCoordinate =
                RequirePackageCoordinate(packageId, selected.Version);
            PackageAcquisitionCandidate candidate =
                discovery.SelectCandidate(selected.Text);
            return await AcquireCandidateAsync(
                    family,
                    selected.Version,
                    packageCoordinate,
                    candidate,
                    failureStart)
                .ConfigureAwait(false);
        }

        private async Task<FrameworkPayload> AcquireCandidateAsync(
            PlatformFamily family,
            PlatformVersion version,
            PackageSourceCoordinate packageCoordinate,
            PackageAcquisitionCandidate candidate,
            int failureStart)
        {
            ConfiguredPackagePayloadResult acquired =
                await _operation.AcquireCandidatePayloadAsync(
                        candidate,
                        _source._payloads.GetStore,
                        _source._payloads.Log,
                        _source._payloads.Limits,
                        _source._payloads.TransferPolicy)
                    .ConfigureAwait(false);
            AddFailures(acquired.Failures);
            _operation.ThrowIfExpired();
            if (acquired.Payload is not { } payload)
            {
                if (acquired.Failures.Any(
                    static failure =>
                        failure.Kind == PackageAuthorityFailureKind.Timeout))
                {
                    throw Incomplete(
                        "The authorized runtime package exceeded its operation deadline.");
                }
                if (acquired.Failures.Any(
                    static failure =>
                        failure.Kind
                            is PackageAuthorityFailureKind.Input
                            or PackageAuthorityFailureKind.InvalidResponse
                            or PackageAuthorityFailureKind.ResponseRejected))
                {
                    throw Reject(
                        PackagePlatformSourceDiagnosticKind.PackageFailure,
                        "Authorized runtime-package evidence was rejected during payload acquisition.");
                }
                if (acquired.Failures.Count != 0)
                {
                    throw Failed(
                        "The authorized runtime package could not be acquired.");
                }

                throw Unavailable(
                    PackagePlatformSourceDiagnosticKind.PackageUnavailable,
                    "The authorized runtime package is unavailable.");
            }
            if (payload.Coordinate != packageCoordinate)
            {
                throw Reject(
                    PackagePlatformSourceDiagnosticKind.InvalidCoordinate,
                    "The acquired runtime package does not match its exact framework coordinate.");
            }

            PlatformFrameworkName name = FrameworkName(family);
            return new FrameworkPayload(
                name,
                family,
                version,
                packageCoordinate.PackageId,
                candidate,
                acquired.Authority!,
                acquired.Source!,
                payload.Content,
                payload.Origin,
                [.. _packageFailures.Skip(failureStart)]);
        }

        private async Task<FrameworkSnapshot> SnapshotFrameworkAsync(
            FrameworkPayload payload)
        {
            string prefix =
                $"runtimes/{_coordinate.RuntimeIdentifier}/lib/"
                + $"{_coordinate.Target.TargetFramework}/";
            var entries = new Dictionary<string, string>(
                StringComparer.Ordinal);
            var logicalNames = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string path in payload.Content.EnumerateEntries())
            {
                _operation.ThrowIfExpired();
                if (_observedEntries
                    == _source.Limits.MaxObservedEntries)
                {
                    throw Incomplete(
                        "Runtime-pack enumeration exceeds the package-entry allowance.");
                }
                _observedEntries++;
                if (!path.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                string name = path[prefix.Length..];
                if (name.Length == 0 || name.Contains('/'))
                    continue;
                if (!logicalNames.Add(name))
                {
                    throw Reject(
                        PackagePlatformSourceDiagnosticKind
                            .DuplicateLogicalCoordinate,
                        "Runtime-pack entries collide at one logical member coordinate.");
                }
                entries.Add(name, path);
            }

            string manifestBase = payload.Name.Value;
            byte[] runtimeConfigurationBytes =
                await ReadRequiredEntryAsync(
                        payload.Content,
                        entries,
                        manifestBase + ".runtimeconfig.json",
                        _source.Limits.MaxManifestBytes,
                        PackagePlatformSourceDiagnosticKind.InvalidManifest,
                        "A selected runtime pack has no exact runtime configuration.")
                    .ConfigureAwait(false);
            byte[] dependencyManifestBytes =
                await ReadRequiredEntryAsync(
                        payload.Content,
                        entries,
                        manifestBase + ".deps.json",
                        _source.Limits.MaxManifestBytes,
                        PackagePlatformSourceDiagnosticKind.InvalidManifest,
                        "A selected runtime pack has no exact dependency manifest.")
                    .ConfigureAwait(false);

            PlatformRuntimeConfiguration runtimeConfiguration =
                ParseRuntimeConfiguration(runtimeConfigurationBytes);
            PlatformDependencyManifest dependencyManifest =
                ParseDependencyManifest(dependencyManifestBytes);
            string expectedRuntimeTarget =
                $".NETCoreApp,Version=v{_coordinate.Target.TargetFramework.Major}."
                + $"{_coordinate.Target.TargetFramework.Minor}/"
                + _coordinate.RuntimeIdentifier;
            if (!string.Equals(
                    dependencyManifest.RuntimeTargetName,
                    expectedRuntimeTarget,
                    StringComparison.Ordinal))
            {
                throw Reject(
                    PackagePlatformSourceDiagnosticKind.InvalidManifest,
                    "The dependency manifest runtime target does not match the requested target framework and RID.");
            }
            var evidence = new PackageImplementationFramework(
                payload.Name,
                payload.Family,
                payload.Version,
                payload.PackageId,
                _coordinate.RuntimeIdentifier,
                payload.Candidate,
                payload.Authority,
                payload.Source,
                payload.Content.GenerationIdentity,
                payload.Origin,
                payload.PackageFailures,
                PackagePlatformContentDigest.FromBytes(
                    runtimeConfigurationBytes),
                PackagePlatformContentDigest.FromBytes(
                    dependencyManifestBytes));
            _operation.ThrowIfExpired();
            return new FrameworkSnapshot(
                evidence,
                payload.Content,
                entries,
                runtimeConfiguration,
                dependencyManifest);
        }

        private async Task<ImmutableArray<PackageImplementationLibrary>>
            RealizeLibrariesAsync(
                IReadOnlyList<FrameworkSnapshot> frameworks)
        {
            var libraries = new List<PackageImplementationLibrary>();
            var identities = new HashSet<AssemblyReferenceIdentity>(
                AssemblyReferenceIdentity.EquivalentComparer);
            var logicalCoordinates = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            int maximumAssemblies = Math.Min(
                _work.MaxAssemblies,
                _source.Limits.MaxAssemblies);

            foreach (FrameworkSnapshot framework in frameworks)
            {
                var frameworkCoordinates = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (PlatformManifestAssetCoordinate asset
                    in framework.DependencyManifest.ManagedAssets)
                {
                    _operation.ThrowIfExpired();
                    if (!asset.FileName.EndsWith(
                            ".dll",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw Reject(
                            PackagePlatformSourceDiagnosticKind.InvalidMember,
                            "A manifest-selected implementation member is not a DLL.");
                    }
                    if (!frameworkCoordinates.Add(asset.FileName)
                        || !logicalCoordinates.Add(asset.FileName))
                    {
                        throw Reject(
                            PackagePlatformSourceDiagnosticKind
                                .DuplicateLogicalCoordinate,
                            "Implementation manifests project more than one asset to the same runtime member.");
                    }
                }
            }

            foreach (FrameworkSnapshot framework in frameworks)
            {
                foreach (PlatformManifestAssetCoordinate asset
                    in framework.DependencyManifest.ManagedAssets)
                {
                    _operation.ThrowIfExpired();
                    if (libraries.Count == maximumAssemblies)
                    {
                        throw Incomplete(
                            "The implementation closure exceeds the assembly allowance.");
                    }

                    byte[] content = await ReadRequiredEntryAsync(
                            framework.Content,
                            framework.Entries,
                            asset.FileName,
                            _source.Limits.MaxEntryBytes,
                            PackagePlatformSourceDiagnosticKind.InvalidMember,
                            "A manifest-declared implementation member is absent.")
                        .ConfigureAwait(false);
                    AssemblyReferenceIdentity identity =
                        ReadAssemblyIdentity(content);
                    string expectedName = asset.FileName[..^4];
                    if (!string.Equals(
                            identity.Name,
                            expectedName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw Reject(
                            PackagePlatformSourceDiagnosticKind
                                .AssemblyIdentityMismatch,
                            "A manifest-declared implementation member does not match its assembly identity.");
                    }
                    if (!identities.Add(identity))
                    {
                        throw Reject(
                            PackagePlatformSourceDiagnosticKind
                                .DuplicateAssemblyIdentity,
                            "The implementation closure contains duplicate assembly identities.");
                    }
                    PackagePlatformContentDigest digest =
                        PackagePlatformContentDigest.FromBytes(content);
                    _operation.ThrowIfExpired();

                    libraries.Add(
                        new PackageImplementationLibrary(
                            framework.Evidence,
                            asset,
                            identity,
                            digest,
                            content));
                }
            }

            libraries.Sort(
                static (left, right) =>
                {
                    int result =
                        StringComparer.OrdinalIgnoreCase.Compare(
                            left.Identity.Name,
                            right.Identity.Name);
                    if (result != 0)
                        return result;
                    result = Comparer<Version?>.Default.Compare(
                        left.Identity.Version,
                        right.Identity.Version);
                    if (result != 0)
                        return result;
                    result = string.CompareOrdinal(
                        left.Framework.Name.Value,
                        right.Framework.Name.Value);
                    return result != 0
                        ? result
                        : string.CompareOrdinal(
                            left.ManifestCoordinate.Value,
                            right.ManifestCoordinate.Value);
                });
            _operation.ThrowIfExpired();
            return [.. libraries];
        }

        private async Task<byte[]> ReadRequiredEntryAsync(
            IPackageContent content,
            IReadOnlyDictionary<string, string> entries,
            string logicalName,
            long maximumEntryBytes,
            PackagePlatformSourceDiagnosticKind missingKind,
            string missingSummary)
        {
            if (!entries.TryGetValue(
                    logicalName,
                    out string? path))
            {
                if (entries.Keys.Any(
                    candidate => string.Equals(
                        candidate,
                        logicalName,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    throw Reject(
                        PackagePlatformSourceDiagnosticKind
                            .DuplicateLogicalCoordinate,
                        "A runtime-pack member differs from its manifest-defined coordinate only by case.");
                }

                throw Reject(missingKind, missingSummary);
            }

            long allowed = Math.Min(
                Math.Min(maximumEntryBytes, _remainingBytes),
                Array.MaxLength);
            if (allowed <= 0)
            {
                throw Incomplete(
                    "The implementation closure exceeds the byte allowance.");
            }
            if (content is IPackageContentEntryManifest manifest
                && manifest.TryGetEntryLength(path, out long length)
                && length > allowed)
            {
                throw Incomplete(
                    "A selected runtime-pack member exceeds the byte allowance.");
            }
            if (!content.TryOpenEntry(path, allowed, out Stream? stream))
                throw Reject(missingKind, missingSummary);

            await using (stream)
            {
                if (stream.CanSeek && stream.Length > allowed)
                {
                    throw Incomplete(
                        "A selected runtime-pack member exceeds the byte allowance.");
                }

                using var snapshot = new MemoryStream();
                byte[] buffer = new byte[81920];
                while (true)
                {
                    int read = await stream.ReadAsync(
                            buffer,
                            _operation.OperationCancellationToken)
                        .ConfigureAwait(false);
                    _operation.ThrowIfExpired();
                    if (read == 0)
                        break;
                    if (read > allowed - snapshot.Length)
                    {
                        throw Incomplete(
                            "A selected runtime-pack member exceeds the observed byte allowance.");
                    }
                    snapshot.Write(buffer, 0, read);
                }

                byte[] bytes = snapshot.ToArray();
                _remainingBytes -= bytes.LongLength;
                return bytes;
            }
        }

        private PlatformRuntimeConfiguration ParseRuntimeConfiguration(
            byte[] content)
        {
            PlatformManifestParseOutcome<PlatformRuntimeConfiguration>
                outcome = PlatformRuntimeConfigurationReader.Parse(
                    content,
                    RuntimeConfigurationBudget(),
                    _operation.OperationCancellationToken);
            return outcome switch
            {
                PlatformManifestParseOutcome<
                    PlatformRuntimeConfiguration>.Succeeded succeeded =>
                    succeeded.Value,
                PlatformManifestParseOutcome<
                    PlatformRuntimeConfiguration>.Rejected rejected =>
                    throw Reject(
                        PackagePlatformSourceDiagnosticKind.InvalidManifest,
                        rejected.Diagnostic.Summary),
                PlatformManifestParseOutcome<
                    PlatformRuntimeConfiguration>.Incomplete incomplete =>
                    throw Incomplete(incomplete.Diagnostic.Summary),
                _ => throw new InvalidOperationException(
                    "Unknown runtime-configuration parse outcome."),
            };
        }

        private PlatformDependencyManifest ParseDependencyManifest(
            byte[] content)
        {
            if (_remainingManifestLibraries == 0
                || _remainingManifestAssets == 0)
            {
                throw Incomplete(
                    "The implementation closure exceeds its manifest allowance.");
            }

            PlatformManifestParseOutcome<PlatformDependencyManifest>
                outcome = PlatformDependencyManifestReader.Parse(
                    content,
                    DependencyManifestBudget(),
                    _operation.OperationCancellationToken);
            PlatformDependencyManifest manifest = outcome switch
            {
                PlatformManifestParseOutcome<
                    PlatformDependencyManifest>.Succeeded succeeded =>
                    succeeded.Value,
                PlatformManifestParseOutcome<
                    PlatformDependencyManifest>.Rejected rejected =>
                    throw Reject(
                        rejected.Diagnostic.Kind
                            == PlatformManifestDiagnosticKind
                                .InvalidAssetCoordinate
                            ? PackagePlatformSourceDiagnosticKind.InvalidMember
                            : PackagePlatformSourceDiagnosticKind
                                .InvalidManifest,
                        rejected.Diagnostic.Summary),
                PlatformManifestParseOutcome<
                    PlatformDependencyManifest>.Incomplete incomplete =>
                    throw Incomplete(incomplete.Diagnostic.Summary),
                _ => throw new InvalidOperationException(
                    "Unknown dependency-manifest parse outcome."),
            };
            _remainingManifestLibraries -= manifest.LibraryCount;
            _remainingManifestAssets -= manifest.AssetCount;
            return manifest;
        }

        private PlatformManifestParseBudget RuntimeConfigurationBudget() =>
            new(
                _source.Limits.MaxManifestBytes,
                Math.Min(
                    _work.MaxFrameworks,
                    _source.Limits.MaxFrameworks),
                1,
                1);

        private PlatformManifestParseBudget DependencyManifestBudget() =>
            new(
                _source.Limits.MaxManifestBytes,
                1,
                Math.Min(
                    _remainingManifestLibraries,
                    _source.Limits.MaxManifestLibraries),
                Math.Min(
                    _remainingManifestAssets,
                    _source.Limits.MaxManifestAssets));

        private static AssemblyReferenceIdentity ReadAssemblyIdentity(
            byte[] content)
        {
            try
            {
                using var pe = new PEReader(
                    content.ToImmutableArray());
                if (!pe.HasMetadata)
                {
                    throw Reject(
                        PackagePlatformSourceDiagnosticKind.MalformedAssembly,
                        "A manifest-declared implementation member has no ECMA-335 metadata.");
                }

                MetadataReader metadata = pe.GetMetadataReader();
                if (metadata.MetadataKind != MetadataKind.Ecma335
                    || !metadata.IsAssembly)
                {
                    throw Reject(
                        PackagePlatformSourceDiagnosticKind.MalformedAssembly,
                        "A manifest-declared implementation member is not a supported ECMA-335 assembly.");
                }

                return AssemblyReferenceIdentity.FromAssemblyDefinition(
                    metadata);
            }
            catch (BadImageFormatException)
            {
                throw Reject(
                    PackagePlatformSourceDiagnosticKind.MalformedAssembly,
                    "A manifest-declared implementation member contains malformed metadata.");
            }
        }

        private void EnsureFrameworkCapacity(int count)
        {
            if (count
                > Math.Min(
                    _work.MaxFrameworks,
                    _source.Limits.MaxFrameworks))
            {
                throw Incomplete(
                    "The implementation closure exceeds the framework allowance.");
            }
        }

        private void Step()
        {
            if (_resolutionSteps
                == Math.Min(
                    _work.MaxResolutionSteps,
                    _source.Limits.MaxResolutionSteps))
            {
                throw Incomplete(
                    "The implementation closure exceeds the resolution-step allowance.");
            }
            _resolutionSteps++;
        }

        private void AddFailures(
            IEnumerable<PackageAuthorityFailure> failures) =>
            _packageFailures.AddRange(failures);

        private static PackageSourceCoordinate RequirePackageCoordinate(
            string packageId,
            PlatformVersion version)
        {
            try
            {
                PackageSourceCoordinate coordinate =
                    PackageSourceCoordinate.Create(
                        packageId,
                        version.Value);
                if (!coordinate.Version.Equals(
                        version.Value,
                        StringComparison.Ordinal))
                {
                    throw Reject(
                        PackagePlatformSourceDiagnosticKind
                            .InvalidCoordinate,
                        "A runtime package version cannot preserve the exact Platform version identity.");
                }

                return coordinate;
            }
            catch (ArgumentException)
            {
                throw Reject(
                    PackagePlatformSourceDiagnosticKind.InvalidCoordinate,
                    "A runtime package coordinate cannot represent the selected framework version.");
            }
        }

        private static VersionCandidate? SelectVersion(
            IReadOnlyList<VersionCandidate> versions,
            EffectiveFrameworkReference reference)
        {
            if (reference.Range == FrameworkCompatibilityRange.Exact
                || reference.Range == FrameworkCompatibilityRange.Patch
                    && !reference.ApplyPatches
                    && !reference.Version.IsPrerelease)
            {
                return versions.FirstOrDefault(
                    candidate => candidate.Version == reference.Version);
            }

            VersionCandidate? selected = reference.PreferRelease
                ? SelectVersionCore(
                    versions,
                    reference,
                    releaseOnly: true)
                : null;
            return selected
                ?? SelectVersionCore(
                    versions,
                    reference,
                    releaseOnly: false);
        }

        private static VersionCandidate? SelectVersionCore(
            IReadOnlyList<VersionCandidate> versions,
            EffectiveFrameworkReference reference,
            bool releaseOnly)
        {
            VersionCandidate? best = null;
            bool ambiguous = false;
            bool highest =
                reference.Range != FrameworkCompatibilityRange.Patch
                && reference.RollToHighestVersion;
            foreach (VersionCandidate candidate in versions)
            {
                if (releaseOnly && candidate.Version.IsPrerelease
                    || !IsCompatibleWith(
                        reference,
                        candidate.Version))
                {
                    continue;
                }

                ConsiderCandidate(
                    ref best,
                    ref ambiguous,
                    candidate,
                    highest);
            }

            if (best is null
                || best.Version.IsPrerelease
                || reference.Range < FrameworkCompatibilityRange.Patch)
            {
                return RequireUniqueWinner(best, ambiguous);
            }

            PlatformVersion patchBase = best.Version;
            best = null;
            ambiguous = false;
            foreach (VersionCandidate candidate in versions)
            {
                PlatformVersion version = candidate.Version;
                if (releaseOnly && version.IsPrerelease
                    || version.Major != patchBase.Major
                    || version.Minor != patchBase.Minor
                    || !reference.ApplyPatches
                        && version.Patch != patchBase.Patch
                    || PlatformVersion.SemanticPrecedenceComparer.Compare(
                        version,
                        patchBase) < 0)
                {
                    continue;
                }

                ConsiderCandidate(
                    ref best,
                    ref ambiguous,
                    candidate,
                    highest: true);
            }

            return RequireUniqueWinner(best, ambiguous);
        }

        private static void ConsiderCandidate(
            ref VersionCandidate? current,
            ref bool ambiguous,
            VersionCandidate candidate,
            bool highest)
        {
            if (current is null)
            {
                current = candidate;
                ambiguous = false;
                return;
            }

            int comparison =
                PlatformVersion.SemanticPrecedenceComparer.Compare(
                    current.Version,
                    candidate.Version);
            if (comparison == 0
                && current.Version != candidate.Version)
            {
                ambiguous = true;
                return;
            }
            if (highest ? comparison < 0 : comparison > 0)
            {
                current = candidate;
                ambiguous = false;
            }
        }

        private static VersionCandidate? RequireUniqueWinner(
            VersionCandidate? winner,
            bool ambiguous)
        {
            if (ambiguous)
            {
                throw Reject(
                    PackagePlatformSourceDiagnosticKind
                        .InvalidFrameworkGraph,
                    "Runtime-pack versions with equal winning precedence are ambiguous.");
            }

            return winner;
        }

        private static bool IsCompatibleWith(
            EffectiveFrameworkReference reference,
            PlatformVersion candidate)
        {
            int comparison =
                PlatformVersion.SemanticPrecedenceComparer.Compare(
                    reference.Version,
                    candidate);
            if (comparison > 0)
                return false;
            if (comparison == 0)
            {
                return reference.Range
                        != FrameworkCompatibilityRange.Exact
                    || reference.Version == candidate;
            }
            if (reference.Range == FrameworkCompatibilityRange.Exact)
                return false;
            if (reference.Version.Major != candidate.Major
                && reference.Range < FrameworkCompatibilityRange.Major)
            {
                return false;
            }
            if (reference.Version.Minor != candidate.Minor
                && reference.Range < FrameworkCompatibilityRange.Minor)
            {
                return false;
            }
            if (reference.Version.Patch != candidate.Patch
                && reference.Range == FrameworkCompatibilityRange.Patch
                && !reference.ApplyPatches)
            {
                return false;
            }

            return true;
        }

        private static PlatformFrameworkName FrameworkName(
            PlatformFamily family) =>
            family switch
            {
                PlatformFamily.DotNetRuntime => DotNetRuntimeName,
                PlatformFamily.AspNetCore => AspNetCoreName,
                _ => throw new ArgumentOutOfRangeException(nameof(family)),
            };

        private static ImplementationAttemptException Reject(
            PackagePlatformSourceDiagnosticKind kind,
            string summary) =>
            new(
                ImplementationAttemptOutcome.Rejected,
                kind,
                summary);

        private static ImplementationAttemptException Incomplete(
            string summary) =>
            new(
                ImplementationAttemptOutcome.Incomplete,
                PackagePlatformSourceDiagnosticKind.WorkLimitExceeded,
                summary);

        private static ImplementationAttemptException Unavailable(
            PackagePlatformSourceDiagnosticKind kind,
            string summary) =>
            new(
                ImplementationAttemptOutcome.Unavailable,
                kind,
                summary);

        private static ImplementationAttemptException Failed(
            string summary) =>
            new(
                ImplementationAttemptOutcome.Failed,
                PackagePlatformSourceDiagnosticKind.PackageFailure,
                summary);

        private sealed record FrameworkPayload(
            PlatformFrameworkName Name,
            PlatformFamily Family,
            PlatformVersion Version,
            string PackageId,
            PackageAcquisitionCandidate Candidate,
            ConfiguredPackageAuthority Authority,
            PackageSourceResultIdentity Source,
            IPackageContent Content,
            PackagePayloadOrigin Origin,
            ImmutableArray<PackageAuthorityFailure> PackageFailures);

        private sealed record FrameworkSnapshot(
            PackageImplementationFramework Evidence,
            IPackageContent Content,
            IReadOnlyDictionary<string, string> Entries,
            PlatformRuntimeConfiguration RuntimeConfiguration,
            PlatformDependencyManifest DependencyManifest);

        private sealed record VersionCandidate(
            string Text,
            PlatformVersion Version);

        private sealed record EffectiveFrameworkReference(
            PlatformVersion Version,
            FrameworkCompatibilityRange Range,
            bool RollToHighestVersion,
            bool ApplyPatches,
            bool PreferRelease)
        {
            internal static EffectiveFrameworkReference From(
                PlatformFrameworkReference reference)
            {
                FrameworkCompatibilityRange range =
                    reference.RollForward switch
                    {
                        PlatformFrameworkRollForward.Disable =>
                            FrameworkCompatibilityRange.Exact,
                        PlatformFrameworkRollForward.LatestPatch =>
                            FrameworkCompatibilityRange.Patch,
                        PlatformFrameworkRollForward.Minor
                            or PlatformFrameworkRollForward.LatestMinor =>
                            FrameworkCompatibilityRange.Minor,
                        PlatformFrameworkRollForward.Major
                            or PlatformFrameworkRollForward.LatestMajor =>
                            FrameworkCompatibilityRange.Major,
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(reference)),
                    };
                bool highest = reference.RollForward
                    is PlatformFrameworkRollForward.LatestMinor
                    or PlatformFrameworkRollForward.LatestMajor;
                return new EffectiveFrameworkReference(
                    reference.Version,
                    range,
                    highest,
                    reference.ApplyPatches,
                    PreferRelease: !reference.Version.IsPrerelease);
            }
        }
    }

    private sealed class ImplementationAttemptException : Exception
    {
        internal ImplementationAttemptException(
            ImplementationAttemptOutcome outcome,
            PackagePlatformSourceDiagnosticKind kind,
            string message)
            : base(message)
        {
            Outcome = outcome;
            Kind = kind;
        }

        internal ImplementationAttemptOutcome Outcome { get; }
        internal PackagePlatformSourceDiagnosticKind Kind { get; }
    }

    private enum FrameworkCompatibilityRange
    {
        Exact,
        Patch,
        Minor,
        Major,
    }

    private enum ImplementationAttemptOutcome
    {
        Unavailable,
        Rejected,
        Incomplete,
        Failed,
    }
}
