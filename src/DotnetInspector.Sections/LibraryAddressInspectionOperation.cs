using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Libraries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.Research;
using ILInspector.SourceLink;
using InertText;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes one physical-address request through an exact Library lease.
/// </summary>
public static class LibraryAddressInspectionOperation
{
    private const string SharePath = "library-address-inspection/share";
    private const string ShareReason =
        "A complete portable Workspace scenario was not supplied.";

    public static InspectionEnvelope<LibraryAddressInspectionOutcome> Execute(
        LibraryAddressInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(request.Library, lease.Reference))
            {
                return Rejected(
                    LibraryAddressInspectionRejection
                        .LeaseReferenceMismatch);
            }

            LibraryContentReference? implementation =
                request.Library.ImplementationAssembly;
            if (implementation is null)
            {
                return Rejected(
                    LibraryAddressInspectionRejection
                        .MissingImplementationAssembly);
            }

            LibraryContentReference? portablePdb = null;
            if (RequiresSource(request.Intent))
            {
                LibraryContentReference[] companions =
                [
                    .. request.Library.Contents.Where(
                        content =>
                            content.HasRole(
                                LibraryContentRole.PortablePdb)
                            && ReferenceEquals(
                                content.AssociatedAssembly,
                                implementation)),
                ];
                if (companions.Length > 1)
                {
                    return Rejected(
                        LibraryAddressInspectionRejection
                            .PortablePdbCompanionAmbiguous);
                }
                portablePdb = companions.SingleOrDefault();
            }

            SnapshotOutcome snapshot;
            try
            {
                snapshot = Snapshot(
                    lease,
                    implementation,
                    portablePdb,
                    request.Limits,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ObjectDisposedException exception)
            {
                return Failed(
                    LibraryAddressInspectionFailure.ContentAccess,
                    exception);
            }
            catch (InvalidOperationException exception)
            {
                return Failed(
                    LibraryAddressInspectionFailure.ContentAccess,
                    exception);
            }
            catch (IOException exception)
            {
                return Failed(
                    LibraryAddressInspectionFailure.ContentAccess,
                    exception);
            }

            if (snapshot
                is SnapshotOutcome.Incomplete incomplete)
            {
                return Incomplete(
                    incomplete.Bound,
                    incomplete.Limit,
                    incomplete.Measured);
            }

            var ready = (SnapshotOutcome.Ready)snapshot;
            ResolvedAssemblyReference? descriptor;
            try
            {
                descriptor =
                    ResolvedAssemblyReference
                        .CreateFromArtifactIfManaged(
                            implementation.Registration,
                            () => new MemoryStream(
                                ready.Assembly,
                                writable: false),
                            AssemblyResolutionProvenance.Designated(
                                "Library Address implementation"));
            }
            catch (UnsupportedMetadataFormatException exception)
            {
                return Failed(
                    LibraryAddressInspectionFailure
                        .UnsupportedWindowsMetadata,
                    exception);
            }
            catch (MalformedMetadataRootException exception)
            {
                return Failed(
                    LibraryAddressInspectionFailure.MalformedMetadata,
                    exception);
            }
            catch (BadImageFormatException exception)
            {
                return Failed(
                    LibraryAddressInspectionFailure.MalformedMetadata,
                    exception);
            }
            catch (IOException exception)
            {
                return Failed(
                    LibraryAddressInspectionFailure.ContentAccess,
                    exception);
            }

            if (descriptor is null)
            {
                return Failed(
                    LibraryAddressInspectionFailure.NotManagedAssembly,
                    "The Library implementation content is not a managed assembly.");
            }

            if (implementation.AssemblyIdentity is not { } expectedIdentity
                || !descriptor.Identity.IsEquivalentTo(
                    expectedIdentity.Identity))
            {
                return Rejected(
                    LibraryAddressInspectionRejection
                        .AssemblyIdentityMismatch);
            }

            return request.Intent switch
            {
                LibraryAddressIntent.IlPoint point =>
                    ExecuteIl(
                        point,
                        descriptor,
                        ready,
                        request.Limits,
                        cancellationToken),
                LibraryAddressIntent.HeapPoint point =>
                    ExecuteHeap(
                        point,
                        descriptor,
                        cancellationToken),
                LibraryAddressIntent.Population population =>
                    ExecutePopulation(
                        population,
                        descriptor,
                        ready,
                        request.Limits,
                        cancellationToken),
                _ => throw new InvalidOperationException(
                    "Unknown Library Address intent."),
            };
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static InspectionEnvelope<LibraryAddressInspectionOutcome>
        ExecuteIl(
            LibraryAddressIntent.IlPoint point,
            ResolvedAssemblyReference descriptor,
            SnapshotOutcome.Ready snapshot,
            LibraryAddressInspectionLimits limits,
            CancellationToken cancellationToken)
    {
        SourceLinkService? source = null;
        ExceptionDispatchInfo? cancellation = null;
        InspectionEnvelope<LibraryAddressInspectionOutcome>? envelope = null;
        try
        {
            source = OpenSource(
                descriptor,
                snapshot.PortablePdb,
                IncludesSource(point.Capabilities),
                limits);
            if (snapshot.PortablePdb is not null
                && !source.HasPdb)
            {
                envelope = Rejected(
                    LibraryAddressInspectionRejection
                        .PortablePdbCorrespondenceMismatch);
            }
            else
            {
                AnalysisPreparation analysis = PrepareAnalysis(
                    snapshot.Assembly,
                    point.Capabilities,
                    [point.MethodToken],
                    cancellationToken);
                ILOffsetProjectionOutcome outcome =
                    Project(
                        source,
                        point.MethodToken,
                        point.ILOffset,
                        point.Capabilities,
                        analysis,
                        cancellationToken);
                LibraryIlAddressOutcome result = outcome.Succeeded
                    ? new LibraryIlAddressOutcome.Resolved(
                        point.MethodToken,
                        point.ILOffset,
                        outcome.Projection!)
                    : new LibraryIlAddressOutcome.Unresolved(
                        point.MethodToken,
                        point.ILOffset,
                        outcome.Failure!);
                envelope = Completed(
                    new LibraryAddressDocument.IlPoint(result),
                    outcome.Succeeded
                        ? []
                        : [IlDiagnostic(outcome.Failure!)]);
            }
        }
        catch (OperationCanceledException exception)
        {
            cancellation = ExceptionDispatchInfo.Capture(exception);
        }
        catch (PdbResourceLimitException exception)
        {
            envelope = Incomplete(
                LibraryAddressInspectionBound.EmbeddedPortablePdbBytes,
                exception.LimitBytes,
                exception.ActualBytes);
        }
        catch (UnsupportedMetadataFormatException exception)
        {
            envelope = Failed(
                LibraryAddressInspectionFailure
                    .UnsupportedWindowsMetadata,
                exception);
        }
        catch (MalformedMetadataRootException exception)
        {
            envelope = Failed(
                LibraryAddressInspectionFailure.MalformedMetadata,
                exception);
        }
        catch (BadImageFormatException exception)
        {
            envelope = Failed(
                LibraryAddressInspectionFailure.MalformedMetadata,
                exception);
        }
        catch (IOException exception)
        {
            envelope = Failed(
                LibraryAddressInspectionFailure.Inspection,
                exception);
        }
        catch (InvalidOperationException exception)
        {
            envelope = Failed(
                LibraryAddressInspectionFailure.Inspection,
                exception);
        }

        Exception? disposalFailure = source?.DisposeWithFailure();
        cancellation?.Throw();
        if (disposalFailure is not null)
        {
            return Failed(
                LibraryAddressInspectionFailure.ResourceDisposal,
                disposalFailure);
        }
        return envelope
            ?? throw new InvalidOperationException(
                "Library IL Address execution did not settle.");
    }

    private static InspectionEnvelope<LibraryAddressInspectionOutcome>
        ExecutePopulation(
            LibraryAddressIntent.Population population,
            ResolvedAssemblyReference descriptor,
            SnapshotOutcome.Ready snapshot,
            LibraryAddressInspectionLimits limits,
            CancellationToken cancellationToken)
    {
        SourceLinkService? source = null;
        ExceptionDispatchInfo? cancellation = null;
        InspectionEnvelope<LibraryAddressInspectionOutcome>? envelope = null;
        try
        {
            source = OpenSource(
                descriptor,
                snapshot.PortablePdb,
                IncludesSource(population.Capabilities),
                limits);
            if (snapshot.PortablePdb is not null
                && !source.HasPdb)
            {
                envelope = Rejected(
                    LibraryAddressInspectionRejection
                        .PortablePdbCorrespondenceMismatch);
            }
            else
            {
                int[] methodTokens =
                [
                    .. population.Records
                        .OfType<
                            LibraryAddressPopulationRecord.Coordinate>()
                        .Select(static record => record.MethodToken),
                ];
                AnalysisPreparation analysis = PrepareAnalysis(
                    snapshot.Assembly,
                    population.Capabilities,
                    methodTokens,
                    cancellationToken);
                var rows =
                    ImmutableArray.CreateBuilder<
                        LibraryAddressPopulationRow>(
                            population.Records.Length);
                var diagnostics =
                    ImmutableArray.CreateBuilder<InspectionDiagnostic>();
                foreach (LibraryAddressPopulationRecord record
                    in population.Records)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (record
                        is LibraryAddressPopulationRecord.Malformed malformed)
                    {
                        rows.Add(
                            new LibraryAddressPopulationRow.Malformed(
                                malformed.LineNumber,
                                malformed.Label,
                                malformed.Error));
                        diagnostics.Add(
                            new(
                                "library-address.population.malformed",
                                InspectionDiagnosticSeverity.Error,
                                malformed.Error,
                                $"line {malformed.LineNumber}"));
                        continue;
                    }

                    var coordinate =
                        (LibraryAddressPopulationRecord.Coordinate)record;
                    ILOffsetProjectionOutcome outcome =
                        Project(
                            source,
                            coordinate.MethodToken,
                            coordinate.ILOffset,
                            population.Capabilities,
                            analysis,
                            cancellationToken);
                    if (outcome.Succeeded)
                    {
                        rows.Add(
                            new LibraryAddressPopulationRow.Resolved(
                                coordinate.LineNumber,
                                coordinate.Value,
                                coordinate.Label,
                                coordinate.MethodToken,
                                coordinate.ILOffset,
                                outcome.Projection!));
                    }
                    else
                    {
                        rows.Add(
                            new LibraryAddressPopulationRow.Unresolved(
                                coordinate.LineNumber,
                                coordinate.Value,
                                coordinate.Label,
                                coordinate.MethodToken,
                                coordinate.ILOffset,
                                outcome.Failure!));
                        diagnostics.Add(
                            IlDiagnostic(
                                outcome.Failure!,
                                $"line {coordinate.LineNumber}"));
                    }
                }

                var document =
                    new LibraryAddressDocument.Population(
                        new(rows.ToImmutable()));
                envelope = document.Result.IsComplete
                    ? Completed(document)
                    : Partial(
                        document,
                        diagnostics.ToImmutable());
            }
        }
        catch (OperationCanceledException exception)
        {
            cancellation = ExceptionDispatchInfo.Capture(exception);
        }
        catch (PdbResourceLimitException exception)
        {
            envelope = Incomplete(
                LibraryAddressInspectionBound.EmbeddedPortablePdbBytes,
                exception.LimitBytes,
                exception.ActualBytes);
        }
        catch (UnsupportedMetadataFormatException exception)
        {
            envelope = Failed(
                LibraryAddressInspectionFailure
                    .UnsupportedWindowsMetadata,
                exception);
        }
        catch (MalformedMetadataRootException exception)
        {
            envelope = Failed(
                LibraryAddressInspectionFailure.MalformedMetadata,
                exception);
        }
        catch (BadImageFormatException exception)
        {
            envelope = Failed(
                LibraryAddressInspectionFailure.MalformedMetadata,
                exception);
        }
        catch (IOException exception)
        {
            envelope = Failed(
                LibraryAddressInspectionFailure.Inspection,
                exception);
        }
        catch (InvalidOperationException exception)
        {
            envelope = Failed(
                LibraryAddressInspectionFailure.Inspection,
                exception);
        }

        Exception? disposalFailure = source?.DisposeWithFailure();
        cancellation?.Throw();
        if (disposalFailure is not null)
        {
            return Failed(
                LibraryAddressInspectionFailure.ResourceDisposal,
                disposalFailure);
        }
        return envelope
            ?? throw new InvalidOperationException(
                "Library Address population execution did not settle.");
    }

    private static InspectionEnvelope<LibraryAddressInspectionOutcome>
        ExecuteHeap(
            LibraryAddressIntent.HeapPoint point,
            ResolvedAssemblyReference descriptor,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(descriptor);
            MetadataRootInspection? root =
                session.MetadataRoot(point.Root);
            if (root is null)
            {
                LibraryHeapAddressFailure reason =
                    point.Root is MetadataRootKind.Cli
                        ? LibraryHeapAddressFailure.MetadataUnavailable
                        : LibraryHeapAddressFailure.RootUnavailable;
                string detail =
                    point.Root is MetadataRootKind.Cli
                        ? "The implementation assembly carries no CLI metadata."
                        : "The implementation assembly carries no ReadyToRun manifest metadata.";
                return Completed(
                    new LibraryAddressDocument.HeapPoint(
                        HeapUnresolved(
                            point,
                            reason,
                            detail)),
                    [
                        new(
                            point.Root is MetadataRootKind.Cli
                                ? "library-address.heap.metadata-unavailable"
                                : "library-address.heap.root-unavailable",
                            InspectionDiagnosticSeverity.Error,
                            detail),
                    ]);
            }

            MetadataValue value = root.HeapValue(
                point.Heap,
                point.Address);
            if (value is MetadataValue.Malformed malformed)
            {
                return Completed(
                    new LibraryAddressDocument.HeapPoint(
                        new LibraryHeapAddressOutcome.Unresolved(
                            point.Root,
                            point.Heap,
                            point.Address,
                            LibraryHeapAddressFailure.MalformedValue,
                            malformed.Detail)),
                    [
                        new(
                            "library-address.heap.malformed-value",
                            InspectionDiagnosticSeverity.Error,
                            malformed.Detail.ToString()),
                    ]);
            }

            return Completed(
                new LibraryAddressDocument.HeapPoint(
                    new LibraryHeapAddressOutcome.Resolved(
                        point.Root,
                        root.Identity,
                        point.Heap,
                        point.Address,
                        value)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnsupportedMetadataFormatException exception)
        {
            return Failed(
                LibraryAddressInspectionFailure
                    .UnsupportedWindowsMetadata,
                exception);
        }
        catch (MalformedMetadataRootException exception)
        {
            return Completed(
                new LibraryAddressDocument.HeapPoint(
                    HeapUnresolved(
                        point,
                        LibraryHeapAddressFailure.MalformedRoot,
                        exception.Message)),
                [
                    new(
                        "library-address.heap.malformed-root",
                        InspectionDiagnosticSeverity.Error,
                        exception.Message),
                ]);
        }
        catch (BadImageFormatException exception)
        {
            return Failed(
                LibraryAddressInspectionFailure.MalformedMetadata,
                exception);
        }
        catch (IOException exception)
        {
            return Failed(
                LibraryAddressInspectionFailure.Inspection,
                exception);
        }
        catch (InvalidOperationException exception)
        {
            return Failed(
                LibraryAddressInspectionFailure.Inspection,
                exception);
        }
    }

    private static LibraryHeapAddressOutcome.Unresolved HeapUnresolved(
        LibraryAddressIntent.HeapPoint point,
        LibraryHeapAddressFailure reason,
        string detail) =>
        new(
            point.Root,
            point.Heap,
            point.Address,
            reason,
            detail);

    private static SourceLinkService OpenSource(
        ResolvedAssemblyReference descriptor,
        byte[]? portablePdb,
        bool includeSource,
        LibraryAddressInspectionLimits limits)
    {
        var readLimits = new SourceLinkReadLimits(
            limits.MaximumPortablePdbBytes,
            limits.MaximumSourceLinkMapBytes,
            limits.MaximumSourceLinkMappings,
            new PdbExpansionBudget(
                limits.MaximumPortablePdbBytes));
        if (!includeSource)
        {
            return SourceLinkService.OpenMetadataOnly(
                descriptor,
                log: null,
                cache: null,
                readLimits);
        }
        if (portablePdb is null)
        {
            return SourceLinkService.OpenEmbeddedPdbOnly(
                descriptor,
                readLimits,
                cache: null);
        }

        SourceLinkService source =
            SourceLinkService.OpenMetadataOnly(
                descriptor,
                log: null,
                cache: null,
                readLimits);
        bool loaded = false;
        try
        {
            source.LoadPdbFromStream(
                new MemoryStream(
                    portablePdb,
                    writable: false),
                pdbLocation: "Library Portable PDB companion",
                throwOnReadFailure: true);
            loaded = true;
            return source;
        }
        finally
        {
            if (!loaded)
                _ = source.DisposeWithFailure();
        }
    }

    private static AnalysisPreparation PrepareAnalysis(
        byte[] assembly,
        ILOffsetProjectionCapabilities capabilities,
        IEnumerable<int> methodTokens,
        CancellationToken cancellationToken)
    {
        LibraryBodyAnalysisFeatures features =
            AnalysisFeatures(capabilities);
        if (features is LibraryBodyAnalysisFeatures.None)
            return AnalysisPreparation.None;

        HashSet<int> bodyScope = [.. methodTokens];
        if (bodyScope.Count == 0)
            return AnalysisPreparation.None;

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            ImmutableArray<byte> image =
                ImmutableCollectionsMarshal.AsImmutableArray(assembly);
            LibraryBodyAnalysisExecution execution =
                LibraryBodyAnalysisService.ExecuteImage(
                    "Library Address implementation",
                    image,
                    LibraryBodyAnalysisRequest.Create(
                        features,
                        bodyScope));
            cancellationToken.ThrowIfCancellationRequested();
            return new(
                new ILOffsetAnalysisInput(
                    execution.Allocations,
                    execution.Safety,
                    execution.CallGraph),
                Failure: null);
        }
        catch (Exception exception) when (
            exception is BadImageFormatException
                or IOException
                or InvalidOperationException
                or ArgumentException
                or UnauthorizedAccessException)
        {
            return new(
                Input: null,
                $"IL-offset semantic analysis unavailable: "
                    + $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static ILOffsetProjectionOutcome Project(
        SourceLinkService source,
        int methodToken,
        int ilOffset,
        ILOffsetProjectionCapabilities capabilities,
        AnalysisPreparation analysis,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ILOffsetProjectionOutcome outcome =
            ResearchViews.ProjectILOffset(
                new(
                    source,
                    methodToken,
                    ilOffset,
                    capabilities,
                    BrowsableUrls: false,
                    Analysis: analysis.Input,
                    AnalysisFailure: analysis.Failure));
        cancellationToken.ThrowIfCancellationRequested();
        return outcome;
    }

    private static LibraryBodyAnalysisFeatures AnalysisFeatures(
        ILOffsetProjectionCapabilities capabilities)
    {
        LibraryBodyAnalysisFeatures features =
            LibraryBodyAnalysisFeatures.None;
        if ((capabilities
                & ILOffsetProjectionCapabilities.AllocationContext)
            != 0)
        {
            features |= LibraryBodyAnalysisFeatures.Allocations;
        }
        if ((capabilities
                & (ILOffsetProjectionCapabilities.SafetyContext
                    | ILOffsetProjectionCapabilities.CostContext))
            != 0)
        {
            features |= LibraryBodyAnalysisFeatures.MethodEvidence;
        }
        return features;
    }

    private static bool RequiresSource(
        LibraryAddressIntent intent) =>
        intent switch
        {
            LibraryAddressIntent.IlPoint point =>
                IncludesSource(point.Capabilities),
            LibraryAddressIntent.Population population =>
                IncludesSource(population.Capabilities),
            _ => false,
        };

    private static bool IncludesSource(
        ILOffsetProjectionCapabilities capabilities) =>
        (capabilities
            & ILOffsetProjectionCapabilities.SourceLocation)
        != 0;

    private static SnapshotOutcome Snapshot(
        LibraryOperationLease lease,
        LibraryContentReference implementation,
        LibraryContentReference? portablePdb,
        LibraryAddressInspectionLimits limits,
        CancellationToken cancellationToken) =>
        portablePdb is null
            ? lease.Snapshot(
                implementation,
                limits,
                static (view, state, token) =>
                    SnapshotAssembly(view, state, token),
                cancellationToken)
            : lease.SnapshotPair(
                implementation,
                portablePdb,
                limits,
                static (view, state, token) =>
                    SnapshotPair(view, state, token),
                cancellationToken);

    private static SnapshotOutcome SnapshotAssembly(
        scoped LibraryContentView assembly,
        LibraryAddressInspectionLimits limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (assembly.Content.Length > limits.MaximumAssemblyBytes)
        {
            return new SnapshotOutcome.Incomplete(
                LibraryAddressInspectionBound.AssemblyBytes,
                limits.MaximumAssemblyBytes,
                assembly.Content.Length);
        }
        return new SnapshotOutcome.Ready(
            assembly.Content.ToArray(),
            PortablePdb: null);
    }

    private static SnapshotOutcome SnapshotPair(
        scoped LibraryContentPairView content,
        LibraryAddressInspectionLimits limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (content.First.Content.Length
            > limits.MaximumAssemblyBytes)
        {
            return new SnapshotOutcome.Incomplete(
                LibraryAddressInspectionBound.AssemblyBytes,
                limits.MaximumAssemblyBytes,
                content.First.Content.Length);
        }
        if (content.Second.Content.Length
            > limits.MaximumPortablePdbBytes)
        {
            return new SnapshotOutcome.Incomplete(
                LibraryAddressInspectionBound.PortablePdbBytes,
                limits.MaximumPortablePdbBytes,
                content.Second.Content.Length);
        }
        return new SnapshotOutcome.Ready(
            content.First.Content.ToArray(),
            content.Second.Content.ToArray());
    }

    private static InspectionEnvelope<LibraryAddressInspectionOutcome>
        Completed(
            LibraryAddressDocument document,
            IEnumerable<InspectionDiagnostic>? diagnostics = null) =>
        Envelope(
            new LibraryAddressInspectionOutcome.Completed(document),
            diagnostics);

    private static InspectionEnvelope<LibraryAddressInspectionOutcome>
        Partial(
            LibraryAddressDocument.Population document,
            IEnumerable<InspectionDiagnostic> diagnostics) =>
        Envelope(
            new LibraryAddressInspectionOutcome.Partial(document),
            diagnostics);

    private static InspectionEnvelope<LibraryAddressInspectionOutcome>
        Rejected(
            LibraryAddressInspectionRejection reason) =>
        Envelope(
            new LibraryAddressInspectionOutcome.Rejected(reason),
            [
                new(
                    RejectionCode(reason),
                    InspectionDiagnosticSeverity.Error,
                    RejectionMessage(reason)),
            ]);

    private static InspectionEnvelope<LibraryAddressInspectionOutcome>
        Incomplete(
            LibraryAddressInspectionBound bound,
            long limit,
            long measured) =>
        Envelope(
            new LibraryAddressInspectionOutcome.Incomplete(
                bound,
                limit,
                measured),
            [
                new(
                    BoundCode(bound),
                    InspectionDiagnosticSeverity.Error,
                    $"Library Address inspection exceeded the {bound} "
                        + $"limit of {limit} bytes after observing "
                        + $"{measured} bytes."),
            ]);

    private static InspectionEnvelope<LibraryAddressInspectionOutcome>
        Failed(
            LibraryAddressInspectionFailure reason,
            Exception exception) =>
        Failed(
            reason,
            $"{exception.GetType().Name}: {exception.Message}");

    private static InspectionEnvelope<LibraryAddressInspectionOutcome>
        Failed(
            LibraryAddressInspectionFailure reason,
            string detail) =>
        Envelope(
            new LibraryAddressInspectionOutcome.Failed(
                reason,
                detail),
            [
                new(
                    FailureCode(reason),
                    InspectionDiagnosticSeverity.Error,
                    detail),
            ]);

    private static InspectionEnvelope<LibraryAddressInspectionOutcome>
        Envelope(
            LibraryAddressInspectionOutcome outcome,
            IEnumerable<InspectionDiagnostic>? diagnostics = null) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason),
            diagnostics);

    private static InspectionDiagnostic IlDiagnostic(
        ILOffsetProjectionFailure failure,
        string? correspondence = null) =>
        new(
            failure.Kind switch
            {
                ILOffsetProjectionFailureKind.NoMetadata =>
                    "library-address.il.no-metadata",
                ILOffsetProjectionFailureKind.MemberUnavailable =>
                    "library-address.il.member-unavailable",
                ILOffsetProjectionFailureKind.InstructionUnavailable =>
                    "library-address.il.instruction-unavailable",
                ILOffsetProjectionFailureKind.ExceptionUnavailable =>
                    "library-address.il.exception-unavailable",
                ILOffsetProjectionFailureKind.CallsiteUnavailable =>
                    "library-address.il.callsite-unavailable",
                ILOffsetProjectionFailureKind.ReturnAddressUnavailable =>
                    "library-address.il.return-address-unavailable",
                ILOffsetProjectionFailureKind.SourceUnavailable =>
                    "library-address.il.source-unavailable",
                ILOffsetProjectionFailureKind
                        .AllocationAnalysisUnavailable =>
                    "library-address.il.allocation-analysis-unavailable",
                ILOffsetProjectionFailureKind.SafetyAnalysisUnavailable =>
                    "library-address.il.safety-analysis-unavailable",
                ILOffsetProjectionFailureKind.CostAnalysisUnavailable =>
                    "library-address.il.cost-analysis-unavailable",
                _ => throw new InvalidOperationException(
                    "Unknown IL-offset projection failure."),
            },
            InspectionDiagnosticSeverity.Error,
            failure.Detail is { Length: > 0 }
                ? $"{failure.Message} {failure.Detail}"
                : failure.Message,
            correspondence);

    private static string RejectionCode(
        LibraryAddressInspectionRejection reason) =>
        reason switch
        {
            LibraryAddressInspectionRejection.LeaseReferenceMismatch =>
                "library-address.rejected.lease-reference-mismatch",
            LibraryAddressInspectionRejection
                    .MissingImplementationAssembly =>
                "library-address.rejected.missing-implementation-assembly",
            LibraryAddressInspectionRejection.AssemblyIdentityMismatch =>
                "library-address.rejected.assembly-identity-mismatch",
            LibraryAddressInspectionRejection
                    .PortablePdbCompanionAmbiguous =>
                "library-address.rejected.portable-pdb-ambiguous",
            LibraryAddressInspectionRejection
                    .PortablePdbCorrespondenceMismatch =>
                "library-address.rejected.portable-pdb-mismatch",
            _ => throw new InvalidOperationException(
                "Unknown Library Address rejection."),
        };

    private static string RejectionMessage(
        LibraryAddressInspectionRejection reason) =>
        reason switch
        {
            LibraryAddressInspectionRejection.LeaseReferenceMismatch =>
                "The Library lease does not belong to the requested Library.",
            LibraryAddressInspectionRejection
                    .MissingImplementationAssembly =>
                "The requested Library has no implementation assembly for physical-address inspection.",
            LibraryAddressInspectionRejection.AssemblyIdentityMismatch =>
                "The retained implementation identity does not match the Library correspondence.",
            LibraryAddressInspectionRejection
                    .PortablePdbCompanionAmbiguous =>
                "More than one Portable PDB companion names the Library implementation assembly.",
            LibraryAddressInspectionRejection
                    .PortablePdbCorrespondenceMismatch =>
                "The retained Portable PDB does not correspond to the Library implementation assembly.",
            _ => throw new InvalidOperationException(
                "Unknown Library Address rejection."),
        };

    private static string BoundCode(
        LibraryAddressInspectionBound bound) =>
        bound switch
        {
            LibraryAddressInspectionBound.AssemblyBytes =>
                "library-address.incomplete.assembly-bytes",
            LibraryAddressInspectionBound.PortablePdbBytes =>
                "library-address.incomplete.portable-pdb-bytes",
            LibraryAddressInspectionBound.EmbeddedPortablePdbBytes =>
                "library-address.incomplete.embedded-portable-pdb-bytes",
            _ => throw new InvalidOperationException(
                "Unknown Library Address bound."),
        };

    private static string FailureCode(
        LibraryAddressInspectionFailure reason) =>
        reason switch
        {
            LibraryAddressInspectionFailure.ContentAccess =>
                "library-address.failed.content-access",
            LibraryAddressInspectionFailure.NotManagedAssembly =>
                "library-address.failed.not-managed-assembly",
            LibraryAddressInspectionFailure.UnsupportedWindowsMetadata =>
                "library-address.failed.unsupported-windows-metadata",
            LibraryAddressInspectionFailure.MalformedMetadata =>
                "library-address.failed.malformed-metadata",
            LibraryAddressInspectionFailure.Inspection =>
                "library-address.failed.inspection",
            LibraryAddressInspectionFailure.ResourceDisposal =>
                "library-address.failed.resource-disposal",
            _ => throw new InvalidOperationException(
                "Unknown Library Address failure."),
        };

    private sealed record AnalysisPreparation(
        ILOffsetAnalysisInput? Input,
        string? Failure)
    {
        internal static AnalysisPreparation None { get; } =
            new(null, null);
    }

    private abstract record SnapshotOutcome
    {
        private protected SnapshotOutcome()
        {
        }

        internal sealed record Ready(
            byte[] Assembly,
            byte[]? PortablePdb)
            : SnapshotOutcome;

        internal sealed record Incomplete(
            LibraryAddressInspectionBound Bound,
            long Limit,
            long Measured)
            : SnapshotOutcome;
    }
}
