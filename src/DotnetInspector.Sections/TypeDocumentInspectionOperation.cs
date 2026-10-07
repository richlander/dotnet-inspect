using System.Collections.Immutable;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

public static class TypeDocumentInspectionOperation
{
    private const string SharePath =
        "type-member-group-population-inspection/share";
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

            AssemblyReferenceIdentity requestedAssembly =
                request.Library.ApiAssembly.AssemblyIdentity
                    ?.Identity
                ?? throw new InvalidOperationException(
                    "A Type document requires a managed API assembly identity.");
            ResolvedTypeDocumentPlan execution =
                ResolvePlan(request.Plan, requestedAssembly);
            LibraryTypeDocumentInspectionOutcome source =
                LibraryTypeDocumentInspection.Execute(
                    new(
                        request.Library,
                        new(
                            request.Plan.Type,
                            execution.MetadataDeclarations,
                            execution.ExpectedType),
                        request.Plan.Bounds),
                    lease,
                    cancellationToken);
            return Project(
                source,
                request.Plan,
                execution.DeclarationsPlan,
                execution.DeclarationRejection,
                execution.StartOrdinal);
        }
        finally
        {
            lease.Dispose();
        }
    }

    public static InspectionEnvelope<TypeDocumentInspectionOutcome> Execute(
        ResolvedAssemblyReference assembly,
        TypeDocumentInspectionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        ResolvedTypeDocumentPlan execution =
            ResolvePlan(plan, assembly.Identity);
        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(assembly);
            return Execute(
                session,
                assembly,
                plan,
                execution,
                cancellationToken);
        }
        catch (UnsupportedMetadataFormatException)
        {
            return Failed(
                TypeDocumentInspectionFailure
                    .UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is MalformedMetadataRootException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return Failed(
                TypeDocumentInspectionFailure.MalformedMetadata);
        }
    }

    public static TypeDocumentExtensionPresenceInspectionResult
        ExecuteWithExtensionPresence(
            ResolvedAssemblyReference assembly,
            TypeDocumentInspectionPlan plan,
            CancellationToken cancellationToken = default)
        => ExecuteWithExtensionPresence(
            assembly,
            plan,
            includeNonPublic: false,
            cancellationToken);

    public static TypeDocumentExtensionPresenceInspectionResult
        ExecuteWithExtensionPresence(
            ResolvedAssemblyReference assembly,
            TypeDocumentInspectionPlan plan,
            bool includeNonPublic,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        ResolvedTypeDocumentPlan execution =
            ResolvePlan(plan, assembly.Identity);
        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(assembly);
            InspectionEnvelope<TypeDocumentInspectionOutcome> document =
                Execute(
                    session,
                    assembly,
                    plan,
                    execution,
                    cancellationToken);
            if (document.Content
                is not TypeDocumentInspectionOutcome.Available available)
            {
                return new(
                    document,
                    new TypeExtensionMethodPresenceInspectionOutcome
                        .Failed());
            }

            return new(
                document,
                TypeExtensionMethodPresenceInspectionOperation.Execute(
                    session,
                    assembly.Identity,
                    plan.Type,
                    plan.Bounds.MaxMetadataRows,
                    includeNonPublic,
                    cancellationToken,
                    MetadataTypeDefinitionAddress.FromToken(
                        available.Document.Subject.ModuleVersionId,
                        available.Document.Subject
                            .TypeDefinitionToken)));
        }
        catch (UnsupportedMetadataFormatException)
        {
            return FailedWithExtensionPresence(
                TypeDocumentInspectionFailure.UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is MalformedMetadataRootException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return FailedWithExtensionPresence(
                TypeDocumentInspectionFailure.MalformedMetadata);
        }
    }

    private static InspectionEnvelope<TypeDocumentInspectionOutcome>
        Execute(
            AssemblyInspectionSession session,
            ResolvedAssemblyReference assembly,
            TypeDocumentInspectionPlan plan,
            ResolvedTypeDocumentPlan execution,
            CancellationToken cancellationToken)
    {
        if (!session.HasMetadata)
        {
            return Failed(
                TypeDocumentInspectionFailure.NotManagedAssembly);
        }
        if (!session.IsAssembly)
        {
            return Failed(
                TypeDocumentInspectionFailure.ManagedModule);
        }

        AssemblyReferenceIdentity identity =
            session.AssemblyIdentity();
        if (!identity.IsEquivalentTo(assembly.Identity))
        {
            return Rejected(
                TypeDocumentInspectionRejection
                    .AssemblyIdentityMismatch);
        }
        Guid moduleVersionId = session.ModuleVersionId();
        if (moduleVersionId == Guid.Empty)
        {
            return Failed(
                TypeDocumentInspectionFailure.EmptyModuleVersionId);
        }

        using var metadataOperation = new MetadataOperationContext(
            new MetadataOperationPolicy(
                plan.Bounds.MaxMetadataRows));
        using MetadataDeclarationSession declaration =
            session.CreateDeclarationSession(metadataOperation);
        if (declaration.ImageAdmission
            is MetadataImageAdmissionResult.Rejected rejection)
        {
            return Incomplete(
                TypeDocumentInspectionBound.MetadataRows,
                plan.Bounds.MaxMetadataRows,
                rejection.Failure.ImageMetadataRows);
        }

        MetadataTypeDocumentInspectionOutcome document =
            declaration.InspectTypeDocument(
                new(
                    plan.Type,
                    execution.MetadataDeclarations,
                    execution.ExpectedType),
                plan.Bounds,
                cancellationToken);
        int assemblyBytes = checked((int)session.ImageLength);
        return Project(
            libraryCorrespondence: null,
            identity,
            assemblyBytes,
            document,
            plan,
            execution.DeclarationsPlan,
            execution.DeclarationRejection,
            execution.StartOrdinal);
    }

    private static TypeDocumentExtensionPresenceInspectionResult
        FailedWithExtensionPresence(
            TypeDocumentInspectionFailure failure) =>
        new(
            Failed(failure),
            new TypeExtensionMethodPresenceInspectionOutcome.Failed());

    private static ResolvedTypeDocumentPlan ResolvePlan(
        TypeDocumentInspectionPlan plan,
        AssemblyReferenceIdentity requestedAssembly)
    {
        TypeMemberGroupPopulationInspectionPlan? declarationsPlan =
            plan.Declarations is null
                ? null
                : new(
                    plan.Type,
                    plan.Declarations,
                    plan.Bounds);
        TypeMemberGroupRowsRequest? rows =
            declarationsPlan?.Query.Terminal
                    is QuerySpaceTerminalRequirement.Rows
                ? plan.Declarations!.Rows
                : null;
        TypeMemberGroupPopulationInspectionRejection?
            declarationRejection = null;
        if (rows?.Continuation is { } candidateContinuation
            && !TypeMemberGroupPopulationInspectionOperation.IsCompatible(
                candidateContinuation,
                requestedAssembly,
                plan.Type,
                declarationsPlan!.Query))
        {
            declarationRejection =
                TypeMemberGroupPopulationInspectionRejection
                    .IncompatibleContinuation;
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

        return new(
            declarationsPlan,
            declarationRejection,
            startOrdinal,
            metadataDeclarations,
            expectedType);
    }

    private sealed record ResolvedTypeDocumentPlan(
        TypeMemberGroupPopulationInspectionPlan? DeclarationsPlan,
        TypeMemberGroupPopulationInspectionRejection?
            DeclarationRejection,
        int StartOrdinal,
        MetadataTypeMemberGroupPopulationRequest?
            MetadataDeclarations,
        MetadataTypeDefinitionAddress? ExpectedType);

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
                    correspondence.AssemblyIdentity,
                    correspondence.AssemblyBytes,
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
        Project(
            LibraryTypeDocumentCorrespondence? libraryCorrespondence,
            AssemblyReferenceIdentity assemblyIdentity,
            int assemblyBytes,
            MetadataTypeDocumentInspectionOutcome document,
            TypeDocumentInspectionPlan plan,
            TypeMemberGroupPopulationInspectionPlan? declarationsPlan,
            TypeMemberGroupPopulationInspectionRejection?
                declarationRejection,
            int startOrdinal) =>
        document switch
        {
            MetadataTypeDocumentInspectionOutcome.Available available =>
                Available(
                    libraryCorrespondence,
                    assemblyIdentity,
                    assemblyBytes,
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
            LibraryTypeDocumentCorrespondence? libraryCorrespondence,
            AssemblyReferenceIdentity assemblyIdentity,
            int assemblyBytes,
            TypeDocumentInspectionPlan plan,
            TypeMemberGroupPopulationInspectionPlan? declarationsPlan,
            TypeMemberGroupPopulationInspectionRejection?
                declarationRejection,
            MetadataTypeDocument document,
            int startOrdinal)
    {
        LibraryAssemblyIdentity assembly =
            TypeMemberGroupPopulationInspectionOperation.PortableIdentity(
                assemblyIdentity);
        if (document.Subject.Type.ModuleVersionId == Guid.Empty)
        {
            return Failed(
                TypeDocumentInspectionFailure.EmptyModuleVersionId);
        }

        TypeDocumentDeclarations declarations =
            declarationRejection is { } rejected
                ? new TypeDocumentDeclarations.Rejected(rejected)
                : ProjectDeclarations(
                    assemblyIdentity,
                    assemblyBytes,
                    plan,
                    declarationsPlan,
                    document.Declarations,
                    document.Subject.Type.ModuleVersionId,
                    startOrdinal);
        TypeDocumentDeclarationSignature signature =
            new(
                ImmutableArray.CreateRange(
                    document.Subject.Signature
                        .GenericParameters
                        .Select(parameter =>
                            new TypeDocumentGenericParameter(
                                parameter.DefinitionSegmentIndex,
                                parameter.MetadataIndex,
                                parameter.Name,
                                parameter.Attributes))));
        TypeSubject subject = libraryCorrespondence is null
            ? new(
                assembly,
                document.Subject.Type.ModuleVersionId,
                plan.Type,
                document.Subject.Type.Definition.Value,
                signature,
                document.Subject.Category,
                document.Subject.Attributes,
                document.Subject.IsByRefLike,
                document.Subject.DefinesCoreLibraryRoot,
                document.Subject.DeclaringType?.Definition.Value)
            : ProjectSubject(
                libraryCorrespondence,
                plan.Type,
                document.Subject,
                assembly);
        return Envelope(
            new TypeDocumentInspectionOutcome.Available(
                new(
                    subject,
                    declarations,
                    assemblyBytes,
                    document.Subject.BaseKind,
                    document.Subject.InterfaceCount)));
    }

    internal static TypeSubject ProjectSubject(
        LibraryTypeDocumentCorrespondence correspondence,
        MetadataTypeDefinitionName type,
        MetadataTypeDeclarationEvidence subject,
        LibraryAssemblyIdentity assembly) =>
        new(
            correspondence.Subject,
            assembly,
            subject.Type.ModuleVersionId,
            type,
            subject.Type.Definition.Value,
            new(
                ImmutableArray.CreateRange(
                    subject.Signature.GenericParameters.Select(
                        parameter =>
                            new TypeDocumentGenericParameter(
                                parameter.DefinitionSegmentIndex,
                                parameter.MetadataIndex,
                                parameter.Name,
                                parameter.Attributes)))),
            subject.Category,
            subject.Attributes,
            subject.IsByRefLike,
            subject.DefinesCoreLibraryRoot,
            subject.DeclaringType?.Definition.Value);

    private static TypeDocumentDeclarations ProjectDeclarations(
        AssemblyReferenceIdentity assemblyIdentity,
        int assemblyBytes,
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
                            assemblyIdentity,
                            moduleVersionId,
                            assemblyBytes,
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
