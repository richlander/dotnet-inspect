using System.Collections.Immutable;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

public static class TypeOverviewDocumentInspectionOperation
{
    private const string SharePath =
        "type-member-group-population-inspection/share";
    private const string ShareReason =
        "A complete portable Workspace scenario was not supplied.";

    public static InspectionEnvelope<TypeOverviewDocumentInspectionOutcome> Execute(
        TypeOverviewDocumentInspectionRequest request,
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
                        "A Type overview document requires a managed API assembly identity.");
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
            LibraryTypeOverviewDocumentInspectionOutcome source =
                LibraryTypeOverviewDocumentInspection.Execute(
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

    private static InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>
        Project(
            LibraryTypeOverviewDocumentInspectionOutcome outcome,
            TypeOverviewDocumentInspectionPlan plan,
            TypeMemberGroupPopulationInspectionPlan? declarationsPlan,
            TypeMemberGroupPopulationInspectionRejection?
                declarationRejection,
            int startOrdinal) =>
        outcome switch
        {
            LibraryTypeOverviewDocumentInspectionOutcome.Completed completed =>
                Project(
                    completed.Correspondence,
                    plan,
                    declarationsPlan,
                    declarationRejection,
                    startOrdinal),
            LibraryTypeOverviewDocumentInspectionOutcome.Rejected rejected =>
                Rejected(
                    rejected.Reason switch
                    {
                        LibraryTypeOverviewDocumentInspectionRejection
                                .LeaseReferenceMismatch =>
                            TypeOverviewDocumentInspectionRejection
                                .LeaseReferenceMismatch,
                        LibraryTypeOverviewDocumentInspectionRejection
                                .AssemblyIdentityMismatch =>
                            TypeOverviewDocumentInspectionRejection
                                .AssemblyIdentityMismatch,
                        _ => throw new InvalidOperationException(
                            "Unknown Library Type overview document rejection."),
                    }),
            LibraryTypeOverviewDocumentInspectionOutcome.Incomplete incomplete =>
                Incomplete(
                    TypeOverviewDocumentInspectionBound.MetadataRows,
                    plan.Bounds.MaxMetadataRows,
                    incomplete.Measured),
            LibraryTypeOverviewDocumentInspectionOutcome.Failed failed =>
                Failed(
                    failed.Reason switch
                    {
                        LibraryTypeOverviewDocumentInspectionFailure
                                .NotManagedAssembly =>
                            TypeOverviewDocumentInspectionFailure
                                .NotManagedAssembly,
                        LibraryTypeOverviewDocumentInspectionFailure.ManagedModule =>
                            TypeOverviewDocumentInspectionFailure.ManagedModule,
                        LibraryTypeOverviewDocumentInspectionFailure
                                .UnsupportedWindowsMetadata =>
                            TypeOverviewDocumentInspectionFailure
                                .UnsupportedWindowsMetadata,
                        LibraryTypeOverviewDocumentInspectionFailure
                                .MalformedMetadata =>
                            TypeOverviewDocumentInspectionFailure.MalformedMetadata,
                        LibraryTypeOverviewDocumentInspectionFailure
                                .EmptyModuleVersionId =>
                            TypeOverviewDocumentInspectionFailure
                                .EmptyModuleVersionId,
                        _ => throw new InvalidOperationException(
                            "Unknown Library Type overview document failure."),
                    }),
            _ => throw new InvalidOperationException(
                "Unknown Library Type overview document outcome."),
        };

    private static InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>
        Project(
            LibraryTypeOverviewDocumentCorrespondence correspondence,
            TypeOverviewDocumentInspectionPlan plan,
            TypeMemberGroupPopulationInspectionPlan? declarationsPlan,
            TypeMemberGroupPopulationInspectionRejection?
                declarationRejection,
            int startOrdinal) =>
        correspondence.Document switch
        {
            MetadataTypeOverviewDocumentInspectionOutcome.Available available =>
                Available(
                    correspondence,
                    plan,
                    declarationsPlan,
                    declarationRejection,
                    available.Document,
                    startOrdinal),
            MetadataTypeOverviewDocumentInspectionOutcome.TypeNotFound =>
                Rejected(TypeOverviewDocumentInspectionRejection.TypeNotFound),
            MetadataTypeOverviewDocumentInspectionOutcome.TypeAmbiguous =>
                Rejected(TypeOverviewDocumentInspectionRejection.TypeAmbiguous),
            MetadataTypeOverviewDocumentInspectionOutcome.Incomplete incomplete =>
                Incomplete(
                    TypeOverviewDocumentInspectionBound.MetadataRows,
                    incomplete.Limit,
                    incomplete.Measured),
            MetadataTypeOverviewDocumentInspectionOutcome.Failed =>
                Failed(TypeOverviewDocumentInspectionFailure.MalformedMetadata),
            _ => throw new InvalidOperationException(
                "Unknown Metadata Type overview document outcome."),
        };

    private static InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>
        Available(
            LibraryTypeOverviewDocumentCorrespondence correspondence,
            TypeOverviewDocumentInspectionPlan plan,
            TypeMemberGroupPopulationInspectionPlan? declarationsPlan,
            TypeMemberGroupPopulationInspectionRejection?
                declarationRejection,
            MetadataTypeOverviewDocument document,
            int startOrdinal)
    {
        LibraryAssemblyIdentity assembly =
            TypeMemberGroupPopulationInspectionOperation.PortableIdentity(
                correspondence.AssemblyIdentity);
        if (document.Subject.Type.ModuleVersionId == Guid.Empty)
        {
            return Failed(
                TypeOverviewDocumentInspectionFailure.EmptyModuleVersionId);
        }

        TypeOverviewDocumentDeclarations declarations =
            declarationRejection is { } rejected
                ? new TypeOverviewDocumentDeclarations.Rejected(rejected)
                : ProjectDeclarations(
                    correspondence,
                    plan,
                    declarationsPlan,
                    document.Declarations,
                    document.Subject.Type.ModuleVersionId,
                    startOrdinal);
        return Envelope(
            new TypeOverviewDocumentInspectionOutcome.Available(
                new(
                    new(
                        correspondence.Subject,
                        assembly,
                        document.Subject.Type.ModuleVersionId,
                        plan.Type,
                        document.Subject.Type.Definition.Value,
                        new(
                            ImmutableArray.CreateRange(
                                document.Subject.Signature
                                    .GenericParameters
                                    .Select(parameter =>
                                        new TypeOverviewDocumentGenericParameter(
                                            parameter
                                                .DefinitionSegmentIndex,
                                            parameter.MetadataIndex,
                                            parameter.Name,
                                            parameter.Attributes)))),
                        document.Subject.Category,
                        document.Subject.Attributes,
                        document.Subject.IsByRefLike,
                        document.Subject.DefinesCoreLibraryRoot,
                        document.Subject.DeclaringType?.Definition.Value),
                    declarations,
                    correspondence.AssemblyBytes)));
    }

    private static TypeOverviewDocumentDeclarations ProjectDeclarations(
        LibraryTypeOverviewDocumentCorrespondence correspondence,
        TypeOverviewDocumentInspectionPlan plan,
        TypeMemberGroupPopulationInspectionPlan? declarationsPlan,
        MetadataTypeOverviewDocumentDeclarations declarations,
        Guid moduleVersionId,
        int startOrdinal) =>
        declarations switch
        {
            MetadataTypeOverviewDocumentDeclarations.NotRequested
                when plan.Declarations is null =>
                    new TypeOverviewDocumentDeclarations.NotRequested(),
            MetadataTypeOverviewDocumentDeclarations.NotRequested =>
                throw new InvalidOperationException(
                    "The requested Type Member-group population was not inspected."),
            MetadataTypeOverviewDocumentDeclarations.BindingMismatch =>
                new TypeOverviewDocumentDeclarations.Rejected(
                    TypeMemberGroupPopulationInspectionRejection
                        .StaleContinuation),
            MetadataTypeOverviewDocumentDeclarations.Inspected inspected =>
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
                "Unknown Metadata Type overview document Member-group outcome."),
        };

    private static TypeOverviewDocumentDeclarations ProjectDeclarations(
        TypeMemberGroupPopulationInspectionOutcome outcome) =>
        outcome switch
        {
            TypeMemberGroupPopulationInspectionOutcome.Available available =>
                new TypeOverviewDocumentDeclarations.Available(
                    available.Content.Members),
            TypeMemberGroupPopulationInspectionOutcome.Rejected rejected =>
                new TypeOverviewDocumentDeclarations.Rejected(rejected.Reason),
            TypeMemberGroupPopulationInspectionOutcome.Incomplete
                incomplete =>
                    new TypeOverviewDocumentDeclarations.Incomplete(
                        incomplete.Bound,
                        incomplete.Limit,
                        incomplete.Measured),
            TypeMemberGroupPopulationInspectionOutcome.Failed failed =>
                new TypeOverviewDocumentDeclarations.Failed(failed.Reason),
            _ => throw new InvalidOperationException(
                "Unknown Type Member-group population outcome."),
        };

    private static InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>
        Rejected(TypeOverviewDocumentInspectionRejection reason) =>
        Envelope(new TypeOverviewDocumentInspectionOutcome.Rejected(reason));

    private static InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>
        Incomplete(
            TypeOverviewDocumentInspectionBound bound,
            long limit,
            long measured) =>
        Envelope(
            new TypeOverviewDocumentInspectionOutcome.Incomplete(
                bound,
                limit,
                measured));

    private static InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>
        Failed(TypeOverviewDocumentInspectionFailure reason) =>
        Envelope(new TypeOverviewDocumentInspectionOutcome.Failed(reason));

    private static InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>
        Envelope(TypeOverviewDocumentInspectionOutcome outcome) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason));
}
