using DotnetInspector.Libraries;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.Packages;

public static class PackageHouseLibraryMaterializer
{
    public static async ValueTask<
        PackageHouseLibraryMaterializationOutcome> MaterializeAsync(
        PackageHouseSettlement.Acquired settlement,
        PackageHouseLibraryHandoff.Compile handoff,
        PackageHouseLibraryMaterializationLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(handoff);
        limits ??= new PackageHouseLibraryMaterializationLimits();
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        PackageHouseLibraryMaterializationFailureKind? invalid =
            ValidateInput(settlement, handoff);
        if (invalid is not null)
            return Terminal(settlement, handoff, invalid.Value);

        IPackageContent content = settlement.Payload.Content;
        string apiPath = handoff.Asset.Path;
        PackageCompileAsset? implementationAsset =
            handoff.ImplementationAsset;
        bool oneAssemblyServesBothRoles =
            implementationAsset is not null
            && ReferenceEquals(
                implementationAsset,
                handoff.Asset);
        string? implementationPath =
            implementationAsset is null
            || oneAssemblyServesBothRoles
                ? null
                : implementationAsset.Path;
        string? documentationPath =
            TryGetCompanionPath(apiPath, ".xml");
        if (documentationPath is null)
        {
            return Terminal(
                settlement,
                handoff,
                PackageHouseLibraryMaterializationFailureKind
                    .InvalidHandoff);
        }

        string? portablePdbPath =
            implementationAsset is null
                ? null
                : TryGetCompanionPath(
                    implementationAsset.Path,
                    ".pdb");

        var entries = new List<MaterializationEntry>(4);
        EntryPreparation apiPreparation = PrepareEntry(
            content,
            apiPath,
            required: true,
            limits.MaxContentBytes);
        if (!TryAcceptEntry(
                apiPreparation,
                entries,
                handoff.Asset,
                PackageHouseLibraryArtifactRole.ApiAssembly
                    | (oneAssemblyServesBothRoles
                        ? PackageHouseLibraryArtifactRole
                            .ImplementationAssembly
                        : 0),
                out PackageHouseLibraryMaterializationFailureKind
                    preparationFailure))
        {
            return Terminal(
                settlement,
                handoff,
                preparationFailure);
        }

        if (implementationPath is not null)
        {
            EntryPreparation implementationPreparation = PrepareEntry(
                content,
                implementationPath,
                required: true,
                limits.MaxContentBytes);
            if (!TryAcceptEntry(
                    implementationPreparation,
                    entries,
                    implementationAsset!,
                    PackageHouseLibraryArtifactRole
                        .ImplementationAssembly,
                    out preparationFailure))
            {
                return Terminal(
                    settlement,
                    handoff,
                    preparationFailure);
            }
        }

        EntryPreparation documentationPreparation = PrepareEntry(
            content,
            documentationPath,
            required: false,
            limits.MaxContentBytes);
        if (!TryAcceptEntry(
                documentationPreparation,
                entries,
                handoff.Asset,
                PackageHouseLibraryArtifactRole
                    .ApiCompiledXmlDocumentation,
                out preparationFailure))
        {
            return Terminal(
                settlement,
                handoff,
                preparationFailure);
        }

        EntryPreparation? portablePdbPreparation = null;
        if (portablePdbPath is not null)
        {
            portablePdbPreparation = PrepareEntry(
                content,
                portablePdbPath,
                required: false,
                limits.MaxContentBytes);
            if (!TryAcceptEntry(
                    portablePdbPreparation,
                    entries,
                    implementationAsset!,
                    PackageHouseLibraryArtifactRole
                        .ImplementationPortablePdb,
                    out preparationFailure))
            {
                return Terminal(
                    settlement,
                    handoff,
                    preparationFailure);
            }
        }

        long knownRetainedBytes = 0;
        foreach (MaterializationEntry entry in entries)
        {
            if (entry.DeclaredLength is null)
                continue;
            knownRetainedBytes = checked(
                knownRetainedBytes + entry.DeclaredLength.Value);
            if (knownRetainedBytes > limits.MaxRetainedBytes)
            {
                return Terminal(
                    settlement,
                    handoff,
                    PackageHouseLibraryMaterializationFailureKind
                        .RetainedByteLimit);
            }
        }

        var session = new ArtifactSetSession(
            new ArtifactSetSessionLimits
            {
                MaxArtifacts = entries.Count,
                MaxArtifactBytes = limits.MaxContentBytes,
                MaxRetainedBytes = limits.MaxRetainedBytes,
            });
        var contentLeases = new List<ArtifactContentLease>(
            entries.Count);
        ArtifactQueryLease? queryLease = null;
        try
        {
            var identities = new List<ArtifactIdentity>(
                entries.Count);
            await session.AddRequiredAcquisitionAsync(
                    (scope, _) =>
                    {
                        var contributions =
                            new List<ArtifactContribution>(
                                entries.Count);
                        foreach (MaterializationEntry entry in entries)
                        {
                            ArtifactContribution contribution =
                                scope.Register(
                                    new PackageHouseLibraryArtifactProvenance(
                                        handoff,
                                        entry.AssociatedAsset,
                                        entry.Path,
                                        entry.Roles),
                                    token => OpenEntry(
                                        content,
                                        entry.Path,
                                        limits.MaxContentBytes,
                                        token),
                                    ContentType(entry.Roles),
                                    ArtifactKind(entry.Roles));
                            contributions.Add(contribution);
                            entry.Artifact = contribution.Descriptor.Identity;
                            identities.Add(contribution.Descriptor.Identity);
                        }

                        return ValueTask.FromResult<
                            ArtifactAcquisitionOutcome>(
                                new ArtifactAcquisitionOutcome.Acquired(
                                    contributions,
                                    ArtifactAcquisitionLeases.None));
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var projections =
                new Dictionary<
                    ArtifactIdentity,
                    ArtifactAssemblyProjection>(
                        ReferenceEqualityComparer.Instance);
            bool metadataProjectionFailed = false;
            ArtifactSetPublicationOutcome publication =
                await session.SealWithProjectionAsync(
                        (view, token) =>
                        {
                            ArtifactIdentity artifact = view.Artifact;
                            MaterializationEntry entry =
                                entries.Single(candidate =>
                                    ReferenceEquals(
                                        candidate.Artifact,
                                        artifact));
                            if (IsCompanion(entry.Roles))
                            {
                                return null;
                            }

                            ArtifactAssemblyProjectionOutcome outcome =
                                ArtifactAssemblyInspection.Project(
                                    view,
                                    token);
                            if (outcome
                                is ArtifactAssemblyProjectionOutcome
                                    .Projected projected)
                            {
                                projections.Add(
                                    artifact,
                                    projected.Value);
                                return null;
                            }

                            metadataProjectionFailed = true;
                            return Failure(
                                "package-house-library.metadata-rejected",
                                "Metadata could not project one selected PackageHouse assembly.");
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            if (publication
                is ArtifactSetPublicationOutcome.NotPublished
                    notPublished)
            {
                var failures = new List<
                    PackageHouseLibraryMaterializationFailureKind>
                {
                    metadataProjectionFailed
                        ? PackageHouseLibraryMaterializationFailureKind
                            .MetadataProjection
                        : ClassifyPublicationFailure(notPublished),
                };
                if (notPublished.CleanupFailures.Count > 0)
                {
                    failures.Add(
                        PackageHouseLibraryMaterializationFailureKind
                            .ArtifactRetirement);
                }

                return new PackageHouseLibraryMaterializationOutcome
                    .Terminal(
                        new(
                            settlement.Result,
                            handoff,
                            failures));
            }

            MaterializationEntry apiEntry = entries[0];
            ArtifactAssemblyProjection apiProjection =
                projections[apiEntry.Artifact!];
            ArtifactAssemblyProjection? implementationProjection =
                implementationPath is null
                    ? oneAssemblyServesBothRoles
                        ? apiProjection
                        : null
                    : projections[entries[1].Artifact!];
            if (implementationProjection is not null
                && !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    apiProjection.Identity,
                    implementationProjection.Identity))
            {
                return await TerminalAfterCleanupAsync(
                        settlement,
                        handoff,
                        session,
                        queryLease,
                        contentLeases,
                        PackageHouseLibraryMaterializationFailureKind
                            .AssemblyIdentityMismatch)
                    .ConfigureAwait(false);
            }

            ArtifactQueryAuthorization authorization =
                session.CreateQueryAuthorization();
            queryLease = session.IssueLease(authorization);
            var references = new List<ArtifactContentReference>(
                entries.Count);
            foreach (ArtifactIdentity identity in identities)
            {
                ArtifactContentReference reference =
                    session.GetContentReference(
                        identity,
                        queryLease);
                references.Add(reference);
                contentLeases.Add(
                    session.IssueContentLease(
                        reference,
                        queryLease));
            }

            queryLease.Dispose();
            queryLease = null;

            ArtifactContentReference apiReference = references[0];
            ArtifactContentReference? implementationReference =
                implementationPath is not null
                    ? references[1]
                    : oneAssemblyServesBothRoles
                        ? apiReference
                        : null;
            var assemblyCorrespondence =
                new LibraryAssemblyCorrespondence(
                    apiReference,
                    new(apiProjection.Identity),
                    implementationReference,
                    implementationProjection is null
                        ? null
                        : new(
                            implementationProjection.Identity));
            var companions =
                new List<LibraryCompanionCorrespondence>(2);
            if (documentationPreparation.Kind
                == EntryPreparationKind.Present)
            {
                companions.Add(
                    new(
                        ReferenceForRole(
                            PackageHouseLibraryArtifactRole
                                .ApiCompiledXmlDocumentation),
                        LibraryContentRole
                            .CompiledXmlDocumentation,
                        apiReference));
            }
            if (portablePdbPreparation?.Kind
                    == EntryPreparationKind.Present
                && implementationReference is not null)
            {
                companions.Add(
                    new(
                        ReferenceForRole(
                            PackageHouseLibraryArtifactRole
                                .ImplementationPortablePdb),
                        LibraryContentRole.PortablePdb,
                        implementationReference));
            }
            var sourceCoordinate =
                new ExactLibrarySourceCoordinate.Package(
                    handoff.Coordinate,
                    new(apiProjection.Identity));
            LibraryReference library = LibraryReference.CreateFromSource(
                sourceCoordinate,
                assemblyCorrespondence,
                companions);
            var owner = new LibraryContentOwner(
                library,
                contentLeases);
            contentLeases.Clear();

            var receipt =
                new PackageHouseLibraryMaterializationReceipt(
                    settlement.Result,
                    handoff,
                    library);
            return new PackageHouseLibraryMaterializationOutcome
                .Completed(receipt, owner, session);

            ArtifactContentReference ReferenceForRole(
                PackageHouseLibraryArtifactRole role)
            {
                int index = entries.FindIndex(
                    entry => entry.Roles.HasFlag(role));
                if (index < 0)
                {
                    throw new InvalidOperationException(
                        $"The materialized Library has no {role} artifact.");
                }

                return references[index];
            }
        }
        catch (Exception failure)
        {
            IReadOnlyList<Exception> cleanup =
                await CleanupAsync(
                        session,
                        queryLease,
                        contentLeases)
                    .ConfigureAwait(false);
            ArtifactSetSession.AttachCleanupFailures(
                failure,
                cleanup);
            throw;
        }
    }

    private static PackageHouseLibraryMaterializationFailureKind
        ClassifyPublicationFailure(
        ArtifactSetPublicationOutcome.NotPublished publication)
    {
        if (publication.Failures.Any(failure =>
                failure.Diagnostic.Code.Equals(
                    "artifact.session.artifact-byte-limit",
                    StringComparison.Ordinal)))
        {
            return PackageHouseLibraryMaterializationFailureKind
                .ContentByteLimit;
        }

        if (publication.Failures.Any(failure =>
                failure.Diagnostic.Code.Equals(
                    "artifact.session.byte-limit",
                    StringComparison.Ordinal)))
        {
            return PackageHouseLibraryMaterializationFailureKind
                .RetainedByteLimit;
        }

        return PackageHouseLibraryMaterializationFailureKind
            .ArtifactPublication;
    }

    private static PackageHouseLibraryMaterializationFailureKind?
        ValidateInput(
        PackageHouseSettlement.Acquired settlement,
        PackageHouseLibraryHandoff.Compile handoff)
    {
        if (settlement.Result is not PackageHouseResult.Settled)
        {
            return PackageHouseLibraryMaterializationFailureKind
                .InvalidSettlement;
        }

        PackageHouseAcquisitionReceipt? acquisition =
            settlement.Result.Evidence.Acquisition;
        if (acquisition is null
            || !ReferenceEquals(acquisition, handoff.Acquisition)
            || !ReferenceEquals(
                acquisition.Generation,
                settlement.Payload.Content.GenerationIdentity))
        {
            return PackageHouseLibraryMaterializationFailureKind
                .InvalidSettlement;
        }

        if (settlement.Result.Evidence.Realization
                is not PackageHouseRealizationReceipt.Compile realization
            || !ReferenceEquals(realization.Receipt, handoff.Receipt)
            || !realization.LibraryHandoffs.Any(candidate =>
                ReferenceEquals(candidate, handoff)))
        {
            return PackageHouseLibraryMaterializationFailureKind
                .InvalidHandoff;
        }

        return null;
    }

    private static EntryPreparation PrepareEntry(
        IPackageContent content,
        string path,
        bool required,
        long maxContentBytes)
    {
        if (content is IPackageContentEntryManifest manifest)
        {
            if (!manifest.TryGetEntryLength(path, out long length))
            {
                return required
                    ? new(EntryPreparationKind.Missing, path)
                    : new(EntryPreparationKind.Absent, path);
            }
            if (length < 0 || length > maxContentBytes)
            {
                return new(
                    EntryPreparationKind.ContentByteLimit,
                    path);
            }

            return new(
                EntryPreparationKind.Present,
                path,
                length);
        }

        try
        {
            if (!content.TryOpenEntry(
                    path,
                    maxContentBytes,
                    out Stream? stream))
            {
                return required
                    ? new(EntryPreparationKind.Missing, path)
                    : new(EntryPreparationKind.Absent, path);
            }

            using (stream)
            {
                long? length = stream.CanSeek
                    ? stream.Length
                    : null;
                if (length > maxContentBytes)
                {
                    return new(
                        EntryPreparationKind.ContentByteLimit,
                        path);
                }

                return new(
                    EntryPreparationKind.Present,
                    path,
                    length);
            }
        }
        catch (InvalidDataException)
        {
            return new(
                EntryPreparationKind.ContentByteLimit,
                path);
        }
    }

    private static bool TryAcceptEntry(
        EntryPreparation preparation,
        ICollection<MaterializationEntry> entries,
        PackageCompileAsset associatedAsset,
        PackageHouseLibraryArtifactRole roles,
        out PackageHouseLibraryMaterializationFailureKind failure)
    {
        switch (preparation.Kind)
        {
            case EntryPreparationKind.Present:
                entries.Add(
                    new(
                        preparation.Path,
                        preparation.DeclaredLength,
                        associatedAsset,
                        roles));
                failure = default;
                return true;
            case EntryPreparationKind.Absent:
                failure = default;
                return true;
            case EntryPreparationKind.Missing:
                failure =
                    PackageHouseLibraryMaterializationFailureKind
                        .MissingPackageEntry;
                return false;
            case EntryPreparationKind.ContentByteLimit:
                failure =
                    PackageHouseLibraryMaterializationFailureKind
                        .ContentByteLimit;
                return false;
            default:
                throw new InvalidOperationException(
                    "Unknown package entry preparation.");
        }
    }

    private static string? TryGetCompanionPath(
        string assemblyPath,
        string companionExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            companionExtension);
        int separator = assemblyPath.LastIndexOf('/');
        int extension = assemblyPath.LastIndexOf('.');
        if (extension <= separator
            || !assemblyPath.AsSpan(extension).Equals(
                ".dll",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.Concat(
            assemblyPath.AsSpan(0, extension),
            companionExtension);
    }

    private static bool IsCompanion(
        PackageHouseLibraryArtifactRole roles) =>
        roles.HasFlag(
            PackageHouseLibraryArtifactRole
                .ApiCompiledXmlDocumentation)
        || roles.HasFlag(
            PackageHouseLibraryArtifactRole
                .ImplementationPortablePdb);

    private static string ContentType(
        PackageHouseLibraryArtifactRole roles) =>
        roles.HasFlag(
            PackageHouseLibraryArtifactRole
                .ApiCompiledXmlDocumentation)
            ? "application/xml"
            : roles.HasFlag(
                PackageHouseLibraryArtifactRole
                    .ImplementationPortablePdb)
                ? "application/vnd.microsoft.portable-pdb"
                : "application/vnd.microsoft.portable-executable";

    private static string ArtifactKind(
        PackageHouseLibraryArtifactRole roles) =>
        roles.HasFlag(
            PackageHouseLibraryArtifactRole
                .ApiCompiledXmlDocumentation)
            ? "compiled-xml-documentation"
            : roles.HasFlag(
                PackageHouseLibraryArtifactRole
                    .ImplementationPortablePdb)
                ? "portable-pdb"
                : "managed-assembly";

    private static Stream OpenEntry(
        IPackageContent content,
        string path,
        long maxContentBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!content.TryOpenEntry(
                path,
                maxContentBytes,
                out Stream? stream))
        {
            throw new InvalidDataException(
                "A prepared PackageHouse entry is no longer available.");
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static async ValueTask<
        PackageHouseLibraryMaterializationOutcome.Terminal>
        TerminalAfterCleanupAsync(
        PackageHouseSettlement.Acquired settlement,
        PackageHouseLibraryHandoff.Compile handoff,
        ArtifactSetSession session,
        ArtifactQueryLease? queryLease,
        IEnumerable<ArtifactContentLease> contentLeases,
        PackageHouseLibraryMaterializationFailureKind primary)
    {
        IReadOnlyList<Exception> cleanup =
            await CleanupAsync(
                    session,
                    queryLease,
                    contentLeases)
                .ConfigureAwait(false);
        var failures =
            new List<PackageHouseLibraryMaterializationFailureKind>
            {
                primary,
            };
        if (cleanup.Count > 0)
        {
            failures.Add(
                PackageHouseLibraryMaterializationFailureKind
                    .ArtifactRetirement);
        }

        return new(
            new(
                settlement.Result,
                handoff,
                failures));
    }

    private static async ValueTask<IReadOnlyList<Exception>>
        CleanupAsync(
        ArtifactSetSession session,
        ArtifactQueryLease? queryLease,
        IEnumerable<ArtifactContentLease> contentLeases)
    {
        var failures = new List<Exception>();
        try
        {
            queryLease?.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        foreach (ArtifactContentLease lease in contentLeases)
        {
            try
            {
                lease.Dispose();
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }

        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
        failures.AddRange(session.CleanupFailures);
        return failures;
    }

    private static PackageHouseLibraryMaterializationOutcome.Terminal
        Terminal(
        PackageHouseSettlement.Acquired settlement,
        PackageHouseLibraryHandoff.Compile handoff,
        PackageHouseLibraryMaterializationFailureKind failure) =>
        new(
            new(
                settlement.Result,
                handoff,
                [failure]));

    private static ArtifactSetAdmissionFailure Failure(
        string code,
        string summary) =>
        new(
            ArtifactSetAdmissionFailureKind.Failed,
            new MaterializationDiagnostic(code, summary));

    private sealed record MaterializationDiagnostic(
        string Code,
        string Summary) : IArtifactAcquisitionDiagnostic;

    private enum EntryPreparationKind
    {
        Present,
        Absent,
        Missing,
        ContentByteLimit,
    }

    private sealed record EntryPreparation(
        EntryPreparationKind Kind,
        string Path,
        long? DeclaredLength = null);

    private sealed class MaterializationEntry(
        string path,
        long? declaredLength,
        PackageCompileAsset associatedAsset,
        PackageHouseLibraryArtifactRole roles)
    {
        public string Path { get; } = path;
        public long? DeclaredLength { get; } = declaredLength;
        public PackageCompileAsset AssociatedAsset { get; } =
            associatedAsset;
        public PackageHouseLibraryArtifactRole Roles { get; } = roles;
        public ArtifactIdentity? Artifact { get; set; }
    }
}
