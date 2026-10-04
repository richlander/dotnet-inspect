using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

public static class TypeDocumentInspectionOperation
{
    private const string SharePath =
        "type-document-inspection/share";
    private const string ShareReason =
        "A complete portable Workspace scenario was not supplied.";

    public static InspectionEnvelope<TypeDocumentInspectionOutcome> Execute(
        TypeDocumentInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            TypeMemberGroupPopulationInspectionPlan? declarationsPlan =
                request.Plan.Declarations is null
                    ? null
                    : new(
                        request.Plan.Type,
                        request.Plan.Declarations,
                        request.Plan.Bounds);
            TypeMemberGroupRowsRequest? rows =
                declarationsPlan?.Query.Terminal
                        is QuerySpaceTerminalRequirement.Rows
                    ? request.Plan.Declarations!.Rows
                    : null;
            TypeMemberGroupPopulationInspectionRejection?
                declarationRejection = null;
            if (rows?.Continuation is { } candidateContinuation)
            {
                AssemblyReferenceIdentity requestedAssembly =
                    request.Library.ApiAssembly.AssemblyIdentity
                        ?.Identity
                    ?? throw new InvalidOperationException(
                        "A Type document requires a managed API assembly identity.");
                if (!TypeMemberGroupPopulationInspectionOperation
                    .IsCompatible(
                        candidateContinuation,
                        requestedAssembly,
                        request.Plan.Type,
                        declarationsPlan!.Query))
                {
                    declarationRejection =
                        TypeMemberGroupPopulationInspectionRejection
                            .IncompatibleContinuation;
                }
            }

            int startOrdinal =
                rows?.Continuation?.NextOrdinal ?? 0;
            MetadataTypeMemberGroupPopulationRequest?
                metadataDeclarations =
                declarationsPlan is null
                    || declarationRejection is not null
                    ? null
                    : TypeMemberGroupPopulationInspectionOperation
                        .CreateMetadataRequest(
                            declarationsPlan,
                            startOrdinal);
            MetadataTypeDefinitionAddress? expectedType = null;
            if (metadataDeclarations is not null
                && rows?.Continuation is { } boundContinuation)
            {
                try
                {
                    expectedType =
                        MetadataTypeDefinitionAddress.FromToken(
                            boundContinuation.Binding.ModuleVersionId,
                            boundContinuation.Binding.TypeDefinitionToken);
                }
                catch (ArgumentException)
                {
                    declarationRejection =
                        TypeMemberGroupPopulationInspectionRejection
                            .IncompatibleContinuation;
                    metadataDeclarations = null;
                }
            }
            LibraryTypeDocumentInspectionOutcome source =
                LibraryTypeDocumentInspection.Execute(
                    new(
                        request.Library,
                        new(
                            request.Plan.Type,
                            metadataDeclarations,
                            expectedType),
                        request.Plan.Bounds),
                    lease,
                    cancellationToken);
            return Project(
                source,
                request.Plan,
                declarationsPlan,
                declarationRejection,
                startOrdinal);
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static InspectionEnvelope<TypeDocumentInspectionOutcome>
        Project(
            LibraryTypeDocumentInspectionOutcome outcome,
            TypeDocumentInspectionPlan plan,
            TypeMemberGroupPopulationInspectionPlan? declarationsPlan,
            TypeMemberGroupPopulationInspectionRejection?
                declarationRejection,
            int startOrdinal) =>
        outcome switch
        {
            LibraryTypeDocumentInspectionOutcome.Completed completed =>
                Project(
                    completed.Correspondence,
                    plan,
                    declarationsPlan,
                    declarationRejection,
                    startOrdinal),
            LibraryTypeDocumentInspectionOutcome.Rejected rejected =>
                Rejected(
                    rejected.Reason switch
                    {
                        LibraryTypeDocumentInspectionRejection
                                .LeaseReferenceMismatch =>
                            TypeDocumentInspectionRejection
                                .LeaseReferenceMismatch,
                        LibraryTypeDocumentInspectionRejection
                                .AssemblyIdentityMismatch =>
                            TypeDocumentInspectionRejection
                                .AssemblyIdentityMismatch,
                        _ => throw new InvalidOperationException(
                            "Unknown Library Type document rejection."),
                    }),
            LibraryTypeDocumentInspectionOutcome.Incomplete incomplete =>
                Incomplete(
                    TypeDocumentInspectionBound.MetadataRows,
                    plan.Bounds.MaxMetadataRows,
                    incomplete.Measured),
            LibraryTypeDocumentInspectionOutcome.Failed failed =>
                Failed(
                    failed.Reason switch
                    {
                        LibraryTypeDocumentInspectionFailure
                                .NotManagedAssembly =>
                            TypeDocumentInspectionFailure
                                .NotManagedAssembly,
                        LibraryTypeDocumentInspectionFailure.ManagedModule =>
                            TypeDocumentInspectionFailure.ManagedModule,
                        LibraryTypeDocumentInspectionFailure
                                .UnsupportedWindowsMetadata =>
                            TypeDocumentInspectionFailure
                                .UnsupportedWindowsMetadata,
                        LibraryTypeDocumentInspectionFailure
                                .MalformedMetadata =>
                            TypeDocumentInspectionFailure.MalformedMetadata,
                        LibraryTypeDocumentInspectionFailure
                                .EmptyModuleVersionId =>
                            TypeDocumentInspectionFailure
                                .EmptyModuleVersionId,
                        _ => throw new InvalidOperationException(
                            "Unknown Library Type document failure."),
                    }),
            _ => throw new InvalidOperationException(
                "Unknown Library Type document outcome."),
        };

    private static InspectionEnvelope<TypeDocumentInspectionOutcome>
        Project(
            LibraryTypeDocumentCorrespondence correspondence,
            TypeDocumentInspectionPlan plan,
            TypeMemberGroupPopulationInspectionPlan? declarationsPlan,
            TypeMemberGroupPopulationInspectionRejection?
                declarationRejection,
            int startOrdinal) =>
        correspondence.Document switch
        {
            MetadataTypeDocumentInspectionOutcome.Available available =>
                Available(
                    correspondence,
                    plan,
                    declarationsPlan,
                    declarationRejection,
                    available.Document,
                    startOrdinal),
            MetadataTypeDocumentInspectionOutcome.TypeNotFound =>
                Rejected(TypeDocumentInspectionRejection.TypeNotFound),
            MetadataTypeDocumentInspectionOutcome.TypeAmbiguous =>
                Rejected(TypeDocumentInspectionRejection.TypeAmbiguous),
            MetadataTypeDocumentInspectionOutcome.Incomplete incomplete =>
                Incomplete(
                    TypeDocumentInspectionBound.MetadataRows,
                    incomplete.Limit,
                    incomplete.Measured),
            MetadataTypeDocumentInspectionOutcome.Failed =>
                Failed(TypeDocumentInspectionFailure.MalformedMetadata),
            _ => throw new InvalidOperationException(
                "Unknown Metadata Type document outcome."),
        };

    private static InspectionEnvelope<TypeDocumentInspectionOutcome>
        Available(
            LibraryTypeDocumentCorrespondence correspondence,
            TypeDocumentInspectionPlan plan,
            TypeMemberGroupPopulationInspectionPlan? declarationsPlan,
            TypeMemberGroupPopulationInspectionRejection?
                declarationRejection,
            MetadataTypeDocument document,
            int startOrdinal)
    {
        LibraryAssemblyIdentity assembly =
            TypeMemberGroupPopulationInspectionOperation.PortableIdentity(
                correspondence.AssemblyIdentity);
        if (document.Subject.Type.ModuleVersionId == Guid.Empty)
        {
            return Failed(
                TypeDocumentInspectionFailure.EmptyModuleVersionId);
        }

        TypeDocumentDeclarations declarations =
            declarationRejection is { } rejected
                ? new TypeDocumentDeclarations.Rejected(rejected)
                : ProjectDeclarations(
                    correspondence,
                    plan,
                    declarationsPlan,
                    document.Declarations,
                    document.Subject.Type.ModuleVersionId,
                    startOrdinal);
        return Envelope(
            new TypeDocumentInspectionOutcome.Available(
                new(
                    new(
                        assembly,
                        document.Subject.Type.ModuleVersionId,
                        plan.Type,
                        document.Subject.Type.Definition.Value,
                        document.Subject.Signature.DisplayName,
                        document.Subject.Category,
                        document.Subject.Attributes,
                        document.Subject.IsByRefLike,
                        document.Subject.DefinesCoreLibraryRoot,
                        document.Subject.DeclaringType?.Definition.Value),
                    declarations,
                    correspondence.AssemblyBytes)));
    }

    private static TypeDocumentDeclarations ProjectDeclarations(
        LibraryTypeDocumentCorrespondence correspondence,
        TypeDocumentInspectionPlan plan,
        TypeMemberGroupPopulationInspectionPlan? declarationsPlan,
        MetadataTypeDocumentDeclarations declarations,
        Guid moduleVersionId,
        int startOrdinal) =>
        declarations switch
        {
            MetadataTypeDocumentDeclarations.NotRequested
                when plan.Declarations is null =>
                    new TypeDocumentDeclarations.NotRequested(),
            MetadataTypeDocumentDeclarations.NotRequested =>
                throw new InvalidOperationException(
                    "The requested Type Member-group population was not inspected."),
            MetadataTypeDocumentDeclarations.BindingMismatch =>
                new TypeDocumentDeclarations.Rejected(
                    TypeMemberGroupPopulationInspectionRejection
                        .StaleContinuation),
            MetadataTypeDocumentDeclarations.Inspected inspected =>
                ProjectDeclarations(
                    TypeMemberGroupPopulationInspectionOperation
                        .ProjectPopulation(
                            correspondence.AssemblyIdentity,
                            moduleVersionId,
                            correspondence.AssemblyBytes,
                            inspected.Outcome,
                            declarationsPlan
                                ?? throw new InvalidOperationException(
                                    "A Metadata population requires a Sections plan."),
                            startOrdinal)),
            _ => throw new InvalidOperationException(
                "Unknown Metadata Type document Member-group outcome."),
        };

    private static TypeDocumentDeclarations ProjectDeclarations(
        TypeMemberGroupPopulationInspectionOutcome outcome) =>
        outcome switch
        {
            TypeMemberGroupPopulationInspectionOutcome.Available available =>
                new TypeDocumentDeclarations.Available(
                    available.Content.Members),
            TypeMemberGroupPopulationInspectionOutcome.Rejected rejected =>
                new TypeDocumentDeclarations.Rejected(rejected.Reason),
            TypeMemberGroupPopulationInspectionOutcome.Incomplete
                incomplete =>
                    new TypeDocumentDeclarations.Incomplete(
                        incomplete.Bound,
                        incomplete.Limit,
                        incomplete.Measured),
            TypeMemberGroupPopulationInspectionOutcome.Failed failed =>
                new TypeDocumentDeclarations.Failed(failed.Reason),
            _ => throw new InvalidOperationException(
                "Unknown Type Member-group population outcome."),
        };

    private static InspectionEnvelope<TypeDocumentInspectionOutcome>
        Rejected(TypeDocumentInspectionRejection reason) =>
        Envelope(new TypeDocumentInspectionOutcome.Rejected(reason));

    private static InspectionEnvelope<TypeDocumentInspectionOutcome>
        Incomplete(
            TypeDocumentInspectionBound bound,
            long limit,
            long measured) =>
        Envelope(
            new TypeDocumentInspectionOutcome.Incomplete(
                bound,
                limit,
                measured));

    private static InspectionEnvelope<TypeDocumentInspectionOutcome>
        Failed(TypeDocumentInspectionFailure reason) =>
        Envelope(new TypeDocumentInspectionOutcome.Failed(reason));

    private static InspectionEnvelope<TypeDocumentInspectionOutcome>
        Envelope(TypeDocumentInspectionOutcome outcome) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason));
}
