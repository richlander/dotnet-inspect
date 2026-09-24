using System.Buffers.Binary;
using System.Collections.Immutable;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes one request-shaped Library inspection through an exact Library
/// lease.
/// </summary>
public static class LibraryInspectionOperation
{
    private const string SharePath = "library-inspection/share";
    private const string ShareReason =
        "A complete portable Workspace scenario was not supplied.";

    public static InspectionEnvelope<LibraryInspectionOutcome> Execute(
        LibraryInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            RowsPreparation rows = PrepareRows(request.Plan.Types);
            LibraryTypeDeclarationInventoryInspectionOutcome outcome =
                LibraryTypeDeclarationInventoryInspection.Execute(
                    new(
                        request.Library,
                        InventoryBounds(request.Plan.Bounds),
                        SourceRows(request.Plan.Types, rows)),
                    lease,
                    cancellationToken);
            return Project(
                outcome,
                request,
                rows,
                cancellationToken);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static InspectionEnvelope<LibraryInspectionOutcome> Project(
        LibraryTypeDeclarationInventoryInspectionOutcome outcome,
        LibraryInspectionRequest request,
        RowsPreparation rows,
        CancellationToken cancellationToken) =>
        outcome switch
        {
            LibraryTypeDeclarationInventoryInspectionOutcome.Completed
                completed =>
                Project(
                    completed.Correspondence,
                    request,
                    rows,
                    cancellationToken),
            LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete
                incomplete =>
                Project(incomplete, request, rows),
            LibraryTypeDeclarationInventoryInspectionOutcome.Rejected
                rejected =>
                Rejected(rejected.Kind),
            LibraryTypeDeclarationInventoryInspectionOutcome.Failed failed =>
                Failed(failed.Kind),
            _ => throw new InvalidOperationException(
                "Unknown Library declaration inventory outcome."),
        };

    private static InspectionEnvelope<LibraryInspectionOutcome> Project(
        LibraryTypeDeclarationInventoryCorrespondence correspondence,
        LibraryInspectionRequest request,
        RowsPreparation rows,
        CancellationToken cancellationToken)
    {
        var diagnostics =
            ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        LibraryTypePopulationCountOutcome? count = null;
        if (request.Plan.Types.Count is not null)
        {
            (
                count,
                ImmutableArray<InspectionDiagnostic> countDiagnostics) =
                Count(
                    correspondence.Inventory,
                    request.Plan.Types.DeclarationSelection,
                    request.Plan.Types.DefinitionKinds,
                    request.Plan.Bounds,
                    cancellationToken);
            diagnostics.AddRange(countDiagnostics);
        }
        LibraryTypePopulationRowsOutcome? rowResult = null;
        if (request.Plan.Types.Rows is { } rowRequest)
        {
            (
                rowResult,
                ImmutableArray<InspectionDiagnostic> rowDiagnostics) =
                Rows(
                    correspondence,
                    request.Plan.Types,
                    rowRequest,
                    request.Plan.Bounds,
                    rows,
                    cancellationToken);
            diagnostics.AddRange(rowDiagnostics);
        }

        return Available(
            correspondence.Subject,
            request,
            count,
            rowResult,
            correspondence.MetadataRows,
            correspondence.DeclarationCount,
            correspondence.RetainedTextCharacters,
            diagnostics.ToImmutable());
    }

    private static InspectionEnvelope<LibraryInspectionOutcome> Project(
        LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete incomplete,
        LibraryInspectionRequest request,
        RowsPreparation rows)
    {
        LibraryTypeDeclarationInventorySubject subject =
            incomplete.Subject
            ?? throw new InvalidOperationException(
                "Library inspection does not impose an assembly-byte bound.");
        (
            LibraryTypePopulationCountBound countBound,
            LibraryTypePopulationRowsBound rowsBound,
            long limit,
            long measured) = incomplete.Bound switch
            {
                LibraryTypeDeclarationInventoryInspectionBound.MetadataRows =>
                    (
                        LibraryTypePopulationCountBound.MetadataRows,
                        LibraryTypePopulationRowsBound.MetadataRows,
                        request.Plan.Bounds.MaxMetadataRows,
                        incomplete.MeasuredMetadataRows
                            ?? throw new InvalidOperationException(
                                "Metadata-row exhaustion omitted its measurement.")),
                LibraryTypeDeclarationInventoryInspectionBound
                        .RetainedDeclarations =>
                    (
                        LibraryTypePopulationCountBound.RetainedDeclarations,
                        LibraryTypePopulationRowsBound.RetainedDeclarations,
                        MaximumRetainedDeclarations(request.Plan.Bounds),
                        incomplete.MeasuredDeclarations
                            ?? throw new InvalidOperationException(
                                "Declaration retention exhaustion omitted its measurement.")),
                LibraryTypeDeclarationInventoryInspectionBound
                        .RetainedTextCharacters =>
                    (
                        LibraryTypePopulationCountBound
                            .RetainedTextCharacters,
                        LibraryTypePopulationRowsBound
                            .RetainedTextCharacters,
                        request.Plan.Bounds.MaxRetainedTextCharacters,
                        incomplete.MeasuredRetainedTextCharacters
                            ?? throw new InvalidOperationException(
                                "Text retention exhaustion omitted its measurement.")),
                LibraryTypeDeclarationInventoryInspectionBound.AssemblyBytes =>
                    throw new InvalidOperationException(
                        "Library inspection does not impose an assembly-byte bound."),
                _ => throw new InvalidOperationException(
                    "Unknown Library declaration inventory bound."),
            };
        var diagnostics =
            ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        LibraryTypePopulationCountOutcome? count = null;
        if (request.Plan.Types.Count is not null)
        {
            count = new LibraryTypePopulationCountOutcome.Incomplete(
                countBound,
                limit,
                measured);
            diagnostics.Add(
                new(
                    Code(countBound),
                    InspectionDiagnosticSeverity.Warning,
                    Message(countBound)));
        }

        LibraryTypePopulationRowsOutcome? rowResult = null;
        if (request.Plan.Types.Rows is not null)
        {
            LibraryTypePopulationRowsRejection? rejection =
                ContinuationRejection(
                    rows,
                    subject.ModuleVersionId);
            if (rejection is { } reason)
            {
                rowResult =
                    new LibraryTypePopulationRowsOutcome.Rejected(
                        reason);
                diagnostics.Add(RowsDiagnostic(reason));
            }
            else
            {
                rowResult =
                    new LibraryTypePopulationRowsOutcome.Incomplete(
                        rowsBound,
                        limit,
                        measured);
                diagnostics.Add(
                    new(
                        Code(rowsBound),
                        InspectionDiagnosticSeverity.Warning,
                        Message(rowsBound)));
            }
        }

        return Available(
            subject,
            request,
            count,
            rowResult,
            incomplete.MeasuredMetadataRows ?? 0,
            incomplete.MeasuredDeclarations ?? 0,
            incomplete.MeasuredRetainedTextCharacters ?? 0,
            diagnostics.ToImmutable());
    }

    private static InspectionEnvelope<LibraryInspectionOutcome> Available(
        LibraryTypeDeclarationInventorySubject subject,
        LibraryInspectionRequest request,
        LibraryTypePopulationCountOutcome? count,
        LibraryTypePopulationRowsOutcome? rows,
        long metadataRows,
        long retainedDeclarations,
        long retainedTextCharacters,
        ImmutableArray<InspectionDiagnostic> diagnostics)
    {
        LibraryAssemblyIdentity portableIdentity =
            PortableIdentity(subject.AssemblyIdentity);
        var binding = new LibraryTypePopulationBinding(
            subject.ModuleVersionId,
            request.Plan.Types.Accessibility,
            request.Plan.Types.DeclarationSelection,
            request.Plan.Types.DefinitionKinds);
        var document = new LibraryDocument(
            portableIdentity,
            subject.ModuleVersionId,
            new(binding, count, rows),
            new(
                subject.AssemblyBytes,
                metadataRows,
                retainedDeclarations,
                retainedTextCharacters),
            request.Plan.Bounds);
        return new(
            new LibraryInspectionOutcome.Available(document),
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason),
            diagnostics);
    }

    private static (
        LibraryTypePopulationRowsOutcome Rows,
        ImmutableArray<InspectionDiagnostic> Diagnostics)
        Rows(
            LibraryTypeDeclarationInventoryCorrespondence correspondence,
            LibraryTypePopulationRequest population,
            LibraryTypePopulationRowsRequest request,
            ApiSurfaceExtractionBounds bounds,
            RowsPreparation preparation,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ContinuationRejection(
                preparation,
                correspondence.ModuleVersionId)
            is { } continuationRejection)
        {
            return (
                new LibraryTypePopulationRowsOutcome.Rejected(
                    continuationRejection),
                [RowsDiagnostic(continuationRejection)]);
        }

        LibraryTypeDeclarationRowsInspectionOutcome source =
            correspondence.Rows
            ?? throw new InvalidOperationException(
                "A requested Type Rows terminal omitted its source outcome.");
        return source switch
        {
            LibraryTypeDeclarationRowsInspectionOutcome.Read read =>
                (
                    new LibraryTypePopulationRowsOutcome.Read(
                        request.Ordering,
                        [
                            .. read.Rows.Select(
                                row => Shape(
                                    row,
                                    correspondence.ModuleVersionId,
                                    request.MemberCount is not null))
                        ],
                        read.NextOrdinal is { } next
                            ? EncodeContinuation(
                                correspondence.ModuleVersionId,
                                population.Accessibility,
                                population.DeclarationSelection,
                                population.DefinitionKinds,
                                request.Ordering,
                                request.MemberCount is not null,
                                next)
                            : null),
                    []),
            LibraryTypeDeclarationRowsInspectionOutcome.Unavailable
                unavailable =>
                RowsUnavailable(unavailable.Reason),
            LibraryTypeDeclarationRowsInspectionOutcome.Rejected rejected =>
                RowsRejected(rejected.Kind),
            LibraryTypeDeclarationRowsInspectionOutcome.Incomplete
                incomplete =>
                RowsIncomplete(
                    correspondence,
                    bounds,
                    incomplete.Bound),
            LibraryTypeDeclarationRowsInspectionOutcome.Failed failed =>
                RowsFailed(failed.Kind),
            _ => throw new InvalidOperationException(
                "Unknown Library declaration Rows outcome."),
        };
    }

    private static LibraryTypeShape Shape(
        AssemblyTypeDeclarationRow row,
        Guid moduleVersionId,
        bool includeMemberCount)
    {
        AssemblyTypeDeclaration declaration = row.Declaration;
        LibraryTypeDeclarationKind declarationKind =
            declaration.Kind switch
            {
                AssemblyTypeDeclarationKind.Definition =>
                    LibraryTypeDeclarationKind.Definition,
                AssemblyTypeDeclarationKind.Forwarder =>
                    LibraryTypeDeclarationKind.Forwarder,
                _ => throw new InvalidOperationException(
                    "A Library Type row must be a definition or forwarder."),
            };
        ApiTypeInventoryKind? definitionKind =
            declaration.DefinitionKind switch
            {
                null => null,
                AssemblyTypeDefinitionKind.Class =>
                    ApiTypeInventoryKind.Class,
                AssemblyTypeDefinitionKind.ValueType =>
                    ApiTypeInventoryKind.Struct,
                AssemblyTypeDefinitionKind.Interface =>
                    ApiTypeInventoryKind.Interface,
                AssemblyTypeDefinitionKind.Enum =>
                    ApiTypeInventoryKind.Enum,
                AssemblyTypeDefinitionKind.Delegate =>
                    ApiTypeInventoryKind.Delegate,
                _ => throw new InvalidOperationException(
                    "Unknown Type definition kind."),
            };
        LibraryTypeDefinitionAccessibility? accessibility =
            declaration.IsDefinitionPublic switch
            {
                true => LibraryTypeDefinitionAccessibility.Public,
                false => LibraryTypeDefinitionAccessibility.NonPublic,
                null => null,
            };
        LibraryTypeForwardingEvidence? forwarding =
            row.Forwarding is null
                ? null
                : new(
                    moduleVersionId,
                    row.Forwarding.Declarations,
                    PortableIdentity(row.Forwarding.Target));
        LibraryTypeMemberCountOutcome? memberCount =
            !includeMemberCount
                ? null
                : declarationKind switch
                {
                    LibraryTypeDeclarationKind.Definition =>
                        new LibraryTypeMemberCountOutcome.Counted(
                            row.MemberCount
                                ?? throw new InvalidOperationException(
                                    "A requested definition Member Count was omitted.")),
                    LibraryTypeDeclarationKind.Forwarder =>
                        new LibraryTypeMemberCountOutcome.NotApplicable(
                            LibraryTypeMemberCountNotApplicableReason
                                .Forwarder),
                    _ => throw new InvalidOperationException(
                        "Unknown Library Type declaration kind."),
                };
        return new(
            declaration.Name,
            new InertString(TextPolicy.Field, row.DisplayName),
            new InertString(TextPolicy.Field, row.Namespace),
            declarationKind,
            definitionKind,
            accessibility,
            declaration.IsPublicSurface,
            forwarding,
            memberCount);
    }

    private static LibraryAssemblyIdentity PortableIdentity(
        AssemblyReferenceIdentity identity) =>
        new(
            new InertString(TextPolicy.Field, identity.Name),
            identity.Version
                ?? throw new InvalidOperationException(
                    "The assembly identity has no version."),
            identity.Culture is null
                ? null
                : new InertString(TextPolicy.Field, identity.Culture),
            identity.PublicKeyToken is null
                ? null
                : new InertString(
                    TextPolicy.Field,
                    identity.PublicKeyToken));

    private static (
        LibraryTypePopulationRowsOutcome Rows,
        ImmutableArray<InspectionDiagnostic> Diagnostics)
        RowsUnavailable(
            LibraryTypeDeclarationRowsInspectionUnavailableReason reason)
    {
        LibraryTypePopulationRowsUnavailableReason portable = reason switch
        {
            LibraryTypeDeclarationRowsInspectionUnavailableReason
                    .UnsupportedModuleExport =>
                LibraryTypePopulationRowsUnavailableReason
                    .UnsupportedModuleExport,
            _ => throw new InvalidOperationException(
                "Unknown Library Type Rows unavailability."),
        };
        return (
            new LibraryTypePopulationRowsOutcome.Unavailable(portable),
            [RowsDiagnostic(portable)]);
    }

    private static (
        LibraryTypePopulationRowsOutcome Rows,
        ImmutableArray<InspectionDiagnostic> Diagnostics)
        RowsRejected(
            LibraryTypeDeclarationRowsInspectionRejectionKind rejection)
    {
        LibraryTypePopulationRowsRejection portable = rejection switch
        {
            LibraryTypeDeclarationRowsInspectionRejectionKind
                    .StaleContinuation =>
                LibraryTypePopulationRowsRejection.StaleContinuation,
            LibraryTypeDeclarationRowsInspectionRejectionKind
                    .ContinuationOutOfRange =>
                LibraryTypePopulationRowsRejection
                    .ContinuationOutOfRange,
            _ => throw new InvalidOperationException(
                "Unknown Library Type Rows rejection."),
        };
        return (
            new LibraryTypePopulationRowsOutcome.Rejected(portable),
            [RowsDiagnostic(portable)]);
    }

    private static (
        LibraryTypePopulationRowsOutcome Rows,
        ImmutableArray<InspectionDiagnostic> Diagnostics)
        RowsIncomplete(
            LibraryTypeDeclarationInventoryCorrespondence correspondence,
            ApiSurfaceExtractionBounds bounds,
            LibraryTypeDeclarationRowsInspectionBound bound)
    {
        LibraryTypePopulationRowsBound portable = bound switch
        {
            LibraryTypeDeclarationRowsInspectionBound
                    .RetainedTextCharacters =>
                LibraryTypePopulationRowsBound.RetainedTextCharacters,
            _ => throw new InvalidOperationException(
                "Unknown Library Type Rows bound."),
        };
        var outcome = new LibraryTypePopulationRowsOutcome.Incomplete(
            portable,
            bounds.MaxRetainedTextCharacters,
            correspondence.RetainedTextCharacters);
        return (
            outcome,
            [
                new(
                    Code(portable),
                    InspectionDiagnosticSeverity.Warning,
                    Message(portable)),
            ]);
    }

    private static (
        LibraryTypePopulationRowsOutcome Rows,
        ImmutableArray<InspectionDiagnostic> Diagnostics)
        RowsFailed(
            LibraryTypeDeclarationRowsInspectionFailureKind failure)
    {
        LibraryTypePopulationRowsFailure portable = failure switch
        {
            LibraryTypeDeclarationRowsInspectionFailureKind
                    .MalformedMetadata =>
                LibraryTypePopulationRowsFailure.MalformedMetadata,
            _ => throw new InvalidOperationException(
                "Unknown Library Type Rows failure."),
        };
        return (
            new LibraryTypePopulationRowsOutcome.Failed(portable),
            [RowsDiagnostic(portable)]);
    }

    private static (
        LibraryTypePopulationCountOutcome Count,
        ImmutableArray<InspectionDiagnostic> Diagnostics)
        Count(
            AssemblyTypeDeclarationInventory inventory,
            LibraryTypeDeclarationSelection selection,
            ApiTypeInventoryKinds definitionKinds,
            ApiSurfaceExtractionBounds bounds,
            CancellationToken cancellationToken)
    {
        int forwarders = 0;
        int classes = 0;
        int structs = 0;
        int interfaces = 0;
        int enums = 0;
        int delegates = 0;
        foreach (
            AssemblyTypeDeclaration declaration
            in inventory.GetDeclarations())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (declaration.Kind)
            {
                case AssemblyTypeDeclarationKind.Definition:
                    if (!IncludesDefinitions(selection)
                        || !IncludesDefinitionKind(
                            definitionKinds,
                            declaration.DefinitionKind
                            ?? throw new InvalidOperationException(
                                "A Type definition declaration omitted its kind.")))
                    {
                        break;
                    }
                    switch (
                        declaration.DefinitionKind
                        ?? throw new InvalidOperationException(
                            "A Type definition declaration omitted its kind."))
                    {
                        case AssemblyTypeDefinitionKind.Class:
                            classes++;
                            break;
                        case AssemblyTypeDefinitionKind.ValueType:
                            structs++;
                            break;
                        case AssemblyTypeDefinitionKind.Interface:
                            interfaces++;
                            break;
                        case AssemblyTypeDefinitionKind.Enum:
                            enums++;
                            break;
                        case AssemblyTypeDefinitionKind.Delegate:
                            delegates++;
                            break;
                        default:
                            throw new InvalidOperationException(
                                "Unknown Type definition kind.");
                    }
                    break;
                case AssemblyTypeDeclarationKind.Forwarder:
                    if (IncludesForwarders(selection))
                        forwarders++;
                    break;
                case AssemblyTypeDeclarationKind.ModuleExport:
                    {
                        if (selection
                            != LibraryTypeDeclarationSelection
                                .DefinitionsAndForwarders)
                        {
                            break;
                        }
                        const LibraryTypePopulationCountUnavailableReason
                            reason =
                                LibraryTypePopulationCountUnavailableReason
                                    .UnsupportedModuleExport;
                        return (
                            new LibraryTypePopulationCountOutcome.Unavailable(
                                reason),
                            [
                                new(
                                    Code(reason),
                                    InspectionDiagnosticSeverity.Error,
                                    Message(reason)),
                            ]);
                    }
                default:
                    throw new InvalidOperationException(
                        "Unknown Type declaration kind.");
            }
        }

        int definitions =
            checked(classes + structs + interfaces + enums + delegates);
        if (definitions > bounds.MaxTypes)
        {
            return Incomplete(
                LibraryTypePopulationCountBound.Definitions,
                bounds.MaxTypes,
                definitions);
        }
        if (forwarders > bounds.MaxTypeForwarders)
        {
            return Incomplete(
                LibraryTypePopulationCountBound.Forwarders,
                bounds.MaxTypeForwarders,
                forwarders);
        }

        return (
            new LibraryTypePopulationCountOutcome.Counted(
                forwarders,
                classes,
                structs,
                interfaces,
                enums,
                delegates),
            []);
    }

    private static (
        LibraryTypePopulationCountOutcome Count,
        ImmutableArray<InspectionDiagnostic> Diagnostics)
        Incomplete(
            LibraryTypePopulationCountBound bound,
            long limit,
            long measured) =>
        (
            new LibraryTypePopulationCountOutcome.Incomplete(
                bound,
                limit,
                measured),
            [
                new(
                    Code(bound),
                    InspectionDiagnosticSeverity.Warning,
                    Message(bound)),
            ]);

    private static RowsPreparation PrepareRows(
        LibraryTypePopulationRequest population)
    {
        if (population.Rows is not { } rows)
            return RowsPreparation.Unrequested;
        if (rows.Continuation is null)
            return new RowsPreparation(0, ExpectedModuleVersionId: null);
        if (!TryDecodeContinuation(
                rows.Continuation,
                out ContinuationPayload payload))
        {
            return new RowsPreparation(
                LibraryTypePopulationRowsRejection.InvalidContinuation);
        }
        if (payload.Accessibility != population.Accessibility
            || payload.DeclarationSelection
                != population.DeclarationSelection
            || payload.DefinitionKinds
                != population.DefinitionKinds
            || payload.Ordering != rows.Ordering
            || payload.IncludeMemberCount
                != (rows.MemberCount is not null))
        {
            return new RowsPreparation(
                LibraryTypePopulationRowsRejection
                    .IncompatibleContinuation);
        }

        return new RowsPreparation(
            payload.NextOrdinal,
            payload.ModuleVersionId);
    }

    private static LibraryTypeDeclarationRowsInspectionRequest? SourceRows(
        LibraryTypePopulationRequest population,
        RowsPreparation preparation)
    {
        LibraryTypePopulationRowsRequest? request = population.Rows;
        return request is null || preparation.Rejection is not null
            ? null
            : new(
                preparation.StartOrdinal,
                request.MaximumRows,
                request.MemberCount is not null,
                preparation.ExpectedModuleVersionId,
                includeDefinitions:
                    IncludesDefinitions(
                        population.DeclarationSelection),
                includeForwarders:
                    IncludesForwarders(
                        population.DeclarationSelection),
                definitionKinds:
                    population.DefinitionKinds);
    }

    private static LibraryTypePopulationRowsRejection?
        ContinuationRejection(
            RowsPreparation preparation,
            Guid moduleVersionId)
    {
        if (preparation.Rejection is { } rejection)
            return rejection;
        return preparation.ExpectedModuleVersionId is { } expected
            && expected != moduleVersionId
                ? LibraryTypePopulationRowsRejection.StaleContinuation
                : null;
    }

    private static LibraryTypePopulationContinuation EncodeContinuation(
        Guid moduleVersionId,
        LibraryTypeAccessibility accessibility,
        LibraryTypeDeclarationSelection declarationSelection,
        ApiTypeInventoryKinds definitionKinds,
        LibraryTypePopulationOrdering ordering,
        bool includeMemberCount,
        int nextOrdinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nextOrdinal);
        Span<byte> payload = stackalloc byte[26];
        payload[0] = 3;
        if (!moduleVersionId.TryWriteBytes(payload[1..17]))
        {
            throw new InvalidOperationException(
                "The Library MVID could not be encoded.");
        }
        payload[17] = checked((byte)accessibility);
        payload[18] = checked((byte)declarationSelection);
        payload[19] = checked((byte)definitionKinds);
        payload[20] = checked((byte)ordering);
        payload[21] = includeMemberCount ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(
            payload[22..26],
            nextOrdinal);
        return new(
            new InertString(
                TextPolicy.Field,
                Convert.ToBase64String(payload)));
    }

    private static bool TryDecodeContinuation(
        LibraryTypePopulationContinuation continuation,
        out ContinuationPayload payload)
    {
        payload = default;
        Span<byte> bytes = stackalloc byte[26];
        if (!Convert.TryFromBase64String(
                continuation.Value.ToString(),
                bytes,
                out int written)
            || written != bytes.Length
            || bytes[0] != 3
            || bytes[21] > 1)
        {
            return false;
        }

        var accessibility =
            (LibraryTypeAccessibility)bytes[17];
        var declarationSelection =
            (LibraryTypeDeclarationSelection)bytes[18];
        var definitionKinds =
            (ApiTypeInventoryKinds)bytes[19];
        var ordering =
            (LibraryTypePopulationOrdering)bytes[20];
        int nextOrdinal =
            BinaryPrimitives.ReadInt32LittleEndian(
                bytes[22..26]);
        if (!Enum.IsDefined(accessibility)
            || !Enum.IsDefined(declarationSelection)
            || !Enum.IsDefined(ordering)
            || !IsValidDefinitionKinds(
                declarationSelection,
                definitionKinds)
            || nextOrdinal < 0)
        {
            return false;
        }

        payload = new(
            new Guid(bytes[1..17]),
            accessibility,
            declarationSelection,
            definitionKinds,
            ordering,
            bytes[21] == 1,
            nextOrdinal);
        return payload.ModuleVersionId != Guid.Empty;
    }

    private static LibraryTypeDeclarationInventoryInspectionBounds
        InventoryBounds(ApiSurfaceExtractionBounds bounds) =>
        new(
            maximumAssemblyBytes: int.MaxValue,
            maximumRetainedDeclarations:
                MaximumRetainedDeclarations(bounds),
            maximumMetadataRows: bounds.MaxMetadataRows,
            maximumRetainedTextCharacters:
                bounds.MaxRetainedTextCharacters);

    private static int MaximumRetainedDeclarations(
        ApiSurfaceExtractionBounds bounds) =>
        (int)Math.Min(
            (long)bounds.MaxTypes + bounds.MaxTypeForwarders,
            int.MaxValue);

    private readonly record struct RowsPreparation
    {
        internal static RowsPreparation Unrequested { get; } =
            new(0, ExpectedModuleVersionId: null);

        internal RowsPreparation(
            int startOrdinal,
            Guid? ExpectedModuleVersionId)
        {
            StartOrdinal = startOrdinal;
            this.ExpectedModuleVersionId = ExpectedModuleVersionId;
            Rejection = null;
        }

        internal RowsPreparation(
            LibraryTypePopulationRowsRejection rejection)
        {
            StartOrdinal = 0;
            ExpectedModuleVersionId = null;
            Rejection = rejection;
        }

        internal int StartOrdinal { get; }
        internal Guid? ExpectedModuleVersionId { get; }
        internal LibraryTypePopulationRowsRejection? Rejection { get; }
    }

    private readonly record struct ContinuationPayload(
        Guid ModuleVersionId,
        LibraryTypeAccessibility Accessibility,
        LibraryTypeDeclarationSelection DeclarationSelection,
        ApiTypeInventoryKinds DefinitionKinds,
        LibraryTypePopulationOrdering Ordering,
        bool IncludeMemberCount,
        int NextOrdinal);

    private static bool IncludesDefinitions(
        LibraryTypeDeclarationSelection selection) =>
        selection is LibraryTypeDeclarationSelection.Definitions
            or LibraryTypeDeclarationSelection.DefinitionsAndForwarders;

    private static bool IncludesForwarders(
        LibraryTypeDeclarationSelection selection) =>
        selection is LibraryTypeDeclarationSelection.Forwarders
            or LibraryTypeDeclarationSelection.DefinitionsAndForwarders;

    private static bool IncludesDefinitionKind(
        ApiTypeInventoryKinds selection,
        AssemblyTypeDefinitionKind kind) =>
        kind switch
        {
            AssemblyTypeDefinitionKind.Class =>
                (selection
                    & ApiTypeInventoryKinds.Classes)
                != 0,
            AssemblyTypeDefinitionKind.ValueType =>
                (selection
                    & ApiTypeInventoryKinds.Structs)
                != 0,
            AssemblyTypeDefinitionKind.Interface =>
                (selection
                    & ApiTypeInventoryKinds.Interfaces)
                != 0,
            AssemblyTypeDefinitionKind.Enum =>
                (selection
                    & ApiTypeInventoryKinds.Enums)
                != 0,
            AssemblyTypeDefinitionKind.Delegate =>
                (selection
                    & ApiTypeInventoryKinds.Delegates)
                != 0,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown Type definition kind."),
        };

    private static bool IsValidDefinitionKinds(
        LibraryTypeDeclarationSelection declarationSelection,
        ApiTypeInventoryKinds definitionKinds)
    {
        if ((definitionKinds
                & ~ApiTypeInventoryKinds.All)
            != 0)
        {
            return false;
        }

        return IncludesDefinitions(declarationSelection)
            ? definitionKinds
                != ApiTypeInventoryKinds.None
            : definitionKinds
                == ApiTypeInventoryKinds.None;
    }

    private static InspectionEnvelope<LibraryInspectionOutcome> Rejected(
        LibraryTypeDeclarationInventoryInspectionRejectionKind rejection)
    {
        LibraryInspectionRejection reason = rejection switch
        {
            LibraryTypeDeclarationInventoryInspectionRejectionKind
                    .LeaseReferenceMismatch =>
                LibraryInspectionRejection.LeaseReferenceMismatch,
            LibraryTypeDeclarationInventoryInspectionRejectionKind
                    .AssemblyIdentityMismatch =>
                LibraryInspectionRejection.AssemblyIdentityMismatch,
            _ => throw new InvalidOperationException(
                "Unknown Library declaration inventory rejection."),
        };
        string message = reason switch
        {
            LibraryInspectionRejection.LeaseReferenceMismatch =>
                "The Library lease does not belong to the requested Library.",
            LibraryInspectionRejection.AssemblyIdentityMismatch =>
                "The realized API assembly identity does not match the Library reference.",
            _ => throw new InvalidOperationException(
                "Unknown Library inspection rejection."),
        };
        return new(
            new LibraryInspectionOutcome.Rejected(reason),
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason),
            [
                new(
                    Code(reason),
                    InspectionDiagnosticSeverity.Error,
                    message),
            ]);
    }

    private static InspectionEnvelope<LibraryInspectionOutcome> Failed(
        LibraryTypeDeclarationInventoryInspectionFailureKind failure)
    {
        LibraryInspectionFailure reason = failure switch
        {
            LibraryTypeDeclarationInventoryInspectionFailureKind
                    .NotManagedAssembly =>
                LibraryInspectionFailure.NotManagedAssembly,
            LibraryTypeDeclarationInventoryInspectionFailureKind
                    .ManagedModule =>
                LibraryInspectionFailure.ManagedModule,
            LibraryTypeDeclarationInventoryInspectionFailureKind
                    .UnsupportedWindowsMetadata =>
                LibraryInspectionFailure.UnsupportedWindowsMetadata,
            LibraryTypeDeclarationInventoryInspectionFailureKind
                    .MalformedMetadata =>
                LibraryInspectionFailure.MalformedMetadata,
            LibraryTypeDeclarationInventoryInspectionFailureKind
                    .EmptyModuleVersionId =>
                LibraryInspectionFailure.EmptyModuleVersionId,
            _ => throw new InvalidOperationException(
                "Unknown Library declaration inventory failure."),
        };
        string message = reason switch
        {
            LibraryInspectionFailure.NotManagedAssembly =>
                "The Library API content is not a managed assembly.",
            LibraryInspectionFailure.ManagedModule =>
                "The Library API content is a managed module, not an assembly.",
            LibraryInspectionFailure.UnsupportedWindowsMetadata =>
                "Windows Metadata is not a supported Library input.",
            LibraryInspectionFailure.MalformedMetadata =>
                "The Library API content has malformed Metadata.",
            LibraryInspectionFailure.EmptyModuleVersionId =>
                "The Library API assembly has no MVID.",
            _ => throw new InvalidOperationException(
                "Unknown Library inspection failure."),
        };
        return new(
            new LibraryInspectionOutcome.Failed(reason),
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason),
            [
                new(
                    Code(reason),
                    InspectionDiagnosticSeverity.Error,
                    message),
            ]);
    }

    private static string Code(
        LibraryTypePopulationCountUnavailableReason reason) =>
        reason switch
        {
            LibraryTypePopulationCountUnavailableReason
                    .UnsupportedModuleExport =>
                "library-inspection.types.count.unavailable.unsupported-module-export",
            _ => throw new InvalidOperationException(
                "Unknown Library Type Count unavailability."),
        };

    private static string Message(
        LibraryTypePopulationCountUnavailableReason reason) =>
        reason switch
        {
            LibraryTypePopulationCountUnavailableReason
                    .UnsupportedModuleExport =>
                "The public Type Count encountered a module-export declaration that is not an admitted definition or forwarder.",
            _ => throw new InvalidOperationException(
                "Unknown Library Type Count unavailability."),
        };

    private static string Code(LibraryTypePopulationCountBound bound) =>
        bound switch
        {
            LibraryTypePopulationCountBound.MetadataRows =>
                "library-inspection.types.count.incomplete.metadata-rows",
            LibraryTypePopulationCountBound.RetainedDeclarations =>
                "library-inspection.types.count.incomplete.retained-declarations",
            LibraryTypePopulationCountBound.RetainedTextCharacters =>
                "library-inspection.types.count.incomplete.retained-text-characters",
            LibraryTypePopulationCountBound.Definitions =>
                "library-inspection.types.count.incomplete.definitions",
            LibraryTypePopulationCountBound.Forwarders =>
                "library-inspection.types.count.incomplete.forwarders",
            _ => throw new InvalidOperationException(
                "Unknown Library Type Count bound."),
        };

    private static string Message(LibraryTypePopulationCountBound bound) =>
        bound switch
        {
            LibraryTypePopulationCountBound.MetadataRows =>
                "The public Type Count exceeded its Metadata-row bound.",
            LibraryTypePopulationCountBound.RetainedDeclarations =>
                "The public Type Count exceeded its retained-declaration bound.",
            LibraryTypePopulationCountBound.RetainedTextCharacters =>
                "The public Type Count exceeded its retained-text bound.",
            LibraryTypePopulationCountBound.Definitions =>
                "The public Type Count exceeded its definition bound.",
            LibraryTypePopulationCountBound.Forwarders =>
                "The public Type Count exceeded its forwarder bound.",
            _ => throw new InvalidOperationException(
                "Unknown Library Type Count bound."),
        };

    private static InspectionDiagnostic RowsDiagnostic(
        LibraryTypePopulationRowsUnavailableReason reason) =>
        new(
            Code(reason),
            InspectionDiagnosticSeverity.Error,
            reason switch
            {
                LibraryTypePopulationRowsUnavailableReason
                        .UnsupportedModuleExport =>
                    "The public Type Rows population encountered a module-export declaration that is not an admitted definition or forwarder.",
                _ => throw new InvalidOperationException(
                    "Unknown Library Type Rows unavailability."),
            });

    private static InspectionDiagnostic RowsDiagnostic(
        LibraryTypePopulationRowsRejection reason) =>
        new(
            Code(reason),
            InspectionDiagnosticSeverity.Error,
            reason switch
            {
                LibraryTypePopulationRowsRejection.InvalidContinuation =>
                    "The Library Type continuation is malformed.",
                LibraryTypePopulationRowsRejection
                        .IncompatibleContinuation =>
                    "The Library Type continuation does not match the requested population projection.",
                LibraryTypePopulationRowsRejection.StaleContinuation =>
                    "The Library Type continuation belongs to a different Library generation.",
                LibraryTypePopulationRowsRejection
                        .ContinuationOutOfRange =>
                    "The Library Type continuation does not identify a resumable population position.",
                _ => throw new InvalidOperationException(
                    "Unknown Library Type Rows rejection."),
            });

    private static InspectionDiagnostic RowsDiagnostic(
        LibraryTypePopulationRowsFailure reason) =>
        new(
            Code(reason),
            InspectionDiagnosticSeverity.Error,
            reason switch
            {
                LibraryTypePopulationRowsFailure.MalformedMetadata =>
                    "The requested Library Type rows contain malformed Metadata.",
                _ => throw new InvalidOperationException(
                    "Unknown Library Type Rows failure."),
            });

    private static string Code(
        LibraryTypePopulationRowsUnavailableReason reason) =>
        reason switch
        {
            LibraryTypePopulationRowsUnavailableReason
                    .UnsupportedModuleExport =>
                "library-inspection.types.rows.unavailable.unsupported-module-export",
            _ => throw new InvalidOperationException(
                "Unknown Library Type Rows unavailability."),
        };

    private static string Code(
        LibraryTypePopulationRowsRejection reason) =>
        reason switch
        {
            LibraryTypePopulationRowsRejection.InvalidContinuation =>
                "library-inspection.types.rows.rejected.invalid-continuation",
            LibraryTypePopulationRowsRejection.IncompatibleContinuation =>
                "library-inspection.types.rows.rejected.incompatible-continuation",
            LibraryTypePopulationRowsRejection.StaleContinuation =>
                "library-inspection.types.rows.rejected.stale-continuation",
            LibraryTypePopulationRowsRejection.ContinuationOutOfRange =>
                "library-inspection.types.rows.rejected.continuation-out-of-range",
            _ => throw new InvalidOperationException(
                "Unknown Library Type Rows rejection."),
        };

    private static string Code(
        LibraryTypePopulationRowsFailure reason) =>
        reason switch
        {
            LibraryTypePopulationRowsFailure.MalformedMetadata =>
                "library-inspection.types.rows.failed.malformed-metadata",
            _ => throw new InvalidOperationException(
                "Unknown Library Type Rows failure."),
        };

    private static string Code(LibraryTypePopulationRowsBound bound) =>
        bound switch
        {
            LibraryTypePopulationRowsBound.MetadataRows =>
                "library-inspection.types.rows.incomplete.metadata-rows",
            LibraryTypePopulationRowsBound.RetainedDeclarations =>
                "library-inspection.types.rows.incomplete.retained-declarations",
            LibraryTypePopulationRowsBound.RetainedTextCharacters =>
                "library-inspection.types.rows.incomplete.retained-text-characters",
            _ => throw new InvalidOperationException(
                "Unknown Library Type Rows bound."),
        };

    private static string Message(LibraryTypePopulationRowsBound bound) =>
        bound switch
        {
            LibraryTypePopulationRowsBound.MetadataRows =>
                "The public Type Rows population exceeded its Metadata-row bound.",
            LibraryTypePopulationRowsBound.RetainedDeclarations =>
                "The public Type Rows population exceeded its retained-declaration bound.",
            LibraryTypePopulationRowsBound.RetainedTextCharacters =>
                "The public Type Rows population exceeded its retained-text bound.",
            _ => throw new InvalidOperationException(
                "Unknown Library Type Rows bound."),
        };

    private static string Code(LibraryInspectionRejection reason) =>
        reason switch
        {
            LibraryInspectionRejection.LeaseReferenceMismatch =>
                "library-inspection.rejected.lease-reference-mismatch",
            LibraryInspectionRejection.AssemblyIdentityMismatch =>
                "library-inspection.rejected.assembly-identity-mismatch",
            _ => throw new InvalidOperationException(
                "Unknown Library inspection rejection."),
        };

    private static string Code(LibraryInspectionFailure reason) =>
        reason switch
        {
            LibraryInspectionFailure.NotManagedAssembly =>
                "library-inspection.failed.not-managed-assembly",
            LibraryInspectionFailure.ManagedModule =>
                "library-inspection.failed.managed-module",
            LibraryInspectionFailure.UnsupportedWindowsMetadata =>
                "library-inspection.failed.unsupported-windows-metadata",
            LibraryInspectionFailure.MalformedMetadata =>
                "library-inspection.failed.malformed-metadata",
            LibraryInspectionFailure.EmptyModuleVersionId =>
                "library-inspection.failed.empty-module-version-id",
            _ => throw new InvalidOperationException(
                "Unknown Library inspection failure."),
        };
}
