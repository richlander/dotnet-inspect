using System.Collections.Immutable;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

public static class TypeSubjectInspectionOperation
{
    private const string SharePath =
        "type-member-group-population-inspection/share";
    private const string ShareReason =
        "A complete portable Workspace scenario was not supplied.";

    public static InspectionEnvelope<TypeSubjectInspectionOutcome> Execute(
        TypeSubjectInspectionRequest request,
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
            ResolvedTypeSubjectPlan execution =
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

    public static InspectionEnvelope<TypeSubjectInspectionOutcome> Execute(
        ResolvedAssemblyReference assembly,
        TypeSubjectInspectionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        ResolvedTypeSubjectPlan execution =
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
                TypeSubjectInspectionFailure
                    .UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is MalformedMetadataRootException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return Failed(
                TypeSubjectInspectionFailure.MalformedMetadata);
        }
    }

    public static TypeSubjectExtensionPresenceInspectionResult
        ExecuteWithExtensionPresence(
            ResolvedAssemblyReference assembly,
            TypeSubjectInspectionPlan plan,
            CancellationToken cancellationToken = default)
        => ExecuteWithExtensionPresence(
            assembly,
            plan,
            includeNonPublic: false,
            cancellationToken);

    public static TypeSubjectExtensionPresenceInspectionResult
        ExecuteWithExtensionPresence(
            ResolvedAssemblyReference assembly,
            TypeSubjectInspectionPlan plan,
            bool includeNonPublic,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        ResolvedTypeSubjectPlan execution =
            ResolvePlan(plan, assembly.Identity);
        try
        {
            using AssemblyInspectionSession session =
                AssemblyInspectionSession.Open(assembly);
            InspectionEnvelope<TypeSubjectInspectionOutcome> document =
                Execute(
                    session,
                    assembly,
                    plan,
                    execution,
                    cancellationToken);
            if (document.Content
                is not TypeSubjectInspectionOutcome.Available available)
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
                TypeSubjectInspectionFailure.UnsupportedWindowsMetadata);
        }
        catch (Exception exception) when (
            exception is MalformedMetadataRootException
                or BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return FailedWithExtensionPresence(
                TypeSubjectInspectionFailure.MalformedMetadata);
        }
    }

    private static InspectionEnvelope<TypeSubjectInspectionOutcome>
        Execute(
            AssemblyInspectionSession session,
            ResolvedAssemblyReference assembly,
            TypeSubjectInspectionPlan plan,
            ResolvedTypeSubjectPlan execution,
            CancellationToken cancellationToken)
    {
        if (!session.HasMetadata)
        {
            return Failed(
                TypeSubjectInspectionFailure.NotManagedAssembly);
        }
        if (!session.IsAssembly)
        {
            return Failed(
                TypeSubjectInspectionFailure.ManagedModule);
        }

        AssemblyReferenceIdentity identity =
            session.AssemblyIdentity();
        if (!identity.IsEquivalentTo(assembly.Identity))
        {
            return Rejected(
                TypeSubjectInspectionRejection
                    .AssemblyIdentityMismatch);
        }
        Guid moduleVersionId = session.ModuleVersionId();
        if (moduleVersionId == Guid.Empty)
        {
            return Failed(
                TypeSubjectInspectionFailure.EmptyModuleVersionId);
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
                TypeSubjectInspectionBound.MetadataRows,
                plan.Bounds.MaxMetadataRows,
                rejection.Failure.ImageMetadataRows);
        }

        MetadataRootAdjacencyInspectionOutcome rootAdjacency =
            declaration.InspectRootAdjacency(cancellationToken);
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
            rootAdjacency,
            document,
            plan,
            execution.DeclarationsPlan,
            execution.DeclarationRejection,
            execution.StartOrdinal);
    }

    private static TypeSubjectExtensionPresenceInspectionResult
        FailedWithExtensionPresence(
            TypeSubjectInspectionFailure failure) =>
        new(
            Failed(failure),
            new TypeExtensionMethodPresenceInspectionOutcome.Failed());

    private static ResolvedTypeSubjectPlan ResolvePlan(
        TypeSubjectInspectionPlan plan,
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

    private sealed record ResolvedTypeSubjectPlan(
        TypeMemberGroupPopulationInspectionPlan? DeclarationsPlan,
        TypeMemberGroupPopulationInspectionRejection?
            DeclarationRejection,
        int StartOrdinal,
        MetadataTypeMemberGroupPopulationRequest?
            MetadataDeclarations,
        MetadataTypeDefinitionAddress? ExpectedType);

    private static InspectionEnvelope<TypeSubjectInspectionOutcome>
        Project(
            LibraryTypeDocumentInspectionOutcome outcome,
            TypeSubjectInspectionPlan plan,
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
                            TypeSubjectInspectionRejection
                                .LeaseReferenceMismatch,
                        LibraryTypeDocumentInspectionRejection
                                .AssemblyIdentityMismatch =>
                            TypeSubjectInspectionRejection
                                .AssemblyIdentityMismatch,
                        _ => throw new InvalidOperationException(
                            "Unknown Library Type document rejection."),
                    }),
            LibraryTypeDocumentInspectionOutcome.Incomplete incomplete =>
                Incomplete(
                    TypeSubjectInspectionBound.MetadataRows,
                    plan.Bounds.MaxMetadataRows,
                    incomplete.Measured),
            LibraryTypeDocumentInspectionOutcome.Failed failed =>
                Failed(
                    failed.Reason switch
                    {
                        LibraryTypeDocumentInspectionFailure
                                .NotManagedAssembly =>
                            TypeSubjectInspectionFailure
                                .NotManagedAssembly,
                        LibraryTypeDocumentInspectionFailure.ManagedModule =>
                            TypeSubjectInspectionFailure.ManagedModule,
                        LibraryTypeDocumentInspectionFailure
                                .UnsupportedWindowsMetadata =>
                            TypeSubjectInspectionFailure
                                .UnsupportedWindowsMetadata,
                        LibraryTypeDocumentInspectionFailure
                                .MalformedMetadata =>
                            TypeSubjectInspectionFailure.MalformedMetadata,
                        LibraryTypeDocumentInspectionFailure
                                .EmptyModuleVersionId =>
                            TypeSubjectInspectionFailure
                                .EmptyModuleVersionId,
                        _ => throw new InvalidOperationException(
                            "Unknown Library Type document failure."),
                    }),
            _ => throw new InvalidOperationException(
                "Unknown Library Type document outcome."),
        };

    private static InspectionEnvelope<TypeSubjectInspectionOutcome>
        Project(
            LibraryTypeDocumentCorrespondence correspondence,
            TypeSubjectInspectionPlan plan,
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
                    correspondence.RootAdjacency,
                    plan,
                    declarationsPlan,
                    declarationRejection,
                    available.Document,
                    startOrdinal),
            MetadataTypeDocumentInspectionOutcome.TypeNotFound =>
                Rejected(TypeSubjectInspectionRejection.TypeNotFound),
            MetadataTypeDocumentInspectionOutcome.TypeAmbiguous =>
                Rejected(TypeSubjectInspectionRejection.TypeAmbiguous),
            MetadataTypeDocumentInspectionOutcome.Incomplete incomplete =>
                Incomplete(
                    TypeSubjectInspectionBound.MetadataRows,
                    incomplete.Limit,
                    incomplete.Measured),
            MetadataTypeDocumentInspectionOutcome.Failed =>
                Failed(TypeSubjectInspectionFailure.MalformedMetadata),
            _ => throw new InvalidOperationException(
                "Unknown Metadata Type document outcome."),
        };

    private static InspectionEnvelope<TypeSubjectInspectionOutcome>
        Project(
            LibraryTypeDocumentCorrespondence? libraryCorrespondence,
            AssemblyReferenceIdentity assemblyIdentity,
            int assemblyBytes,
            MetadataRootAdjacencyInspectionOutcome rootAdjacency,
            MetadataTypeDocumentInspectionOutcome document,
            TypeSubjectInspectionPlan plan,
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
                    rootAdjacency,
                    plan,
                    declarationsPlan,
                    declarationRejection,
                    available.Document,
                    startOrdinal),
            MetadataTypeDocumentInspectionOutcome.TypeNotFound =>
                Rejected(TypeSubjectInspectionRejection.TypeNotFound),
            MetadataTypeDocumentInspectionOutcome.TypeAmbiguous =>
                Rejected(TypeSubjectInspectionRejection.TypeAmbiguous),
            MetadataTypeDocumentInspectionOutcome.Incomplete incomplete =>
                Incomplete(
                    TypeSubjectInspectionBound.MetadataRows,
                    incomplete.Limit,
                    incomplete.Measured),
            MetadataTypeDocumentInspectionOutcome.Failed =>
                Failed(TypeSubjectInspectionFailure.MalformedMetadata),
            _ => throw new InvalidOperationException(
                "Unknown Metadata Type document outcome."),
        };

    private static InspectionEnvelope<TypeSubjectInspectionOutcome>
        Available(
            LibraryTypeDocumentCorrespondence? libraryCorrespondence,
            AssemblyReferenceIdentity assemblyIdentity,
            int assemblyBytes,
            MetadataRootAdjacencyInspectionOutcome rootAdjacency,
            TypeSubjectInspectionPlan plan,
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
                TypeSubjectInspectionFailure.EmptyModuleVersionId);
        }

        TypeSubjectMemberGroups declarations =
            declarationRejection is { } rejected
                ? new TypeSubjectMemberGroups.Rejected(rejected)
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
                document.Subject.IsHidden,
                document.Subject.IsCompilerGenerated,
                document.Subject.IsByRefLike,
                document.Subject.IsReadOnly,
                document.Subject.DefinesCoreLibraryRoot,
                document.Subject.DeclaringType?.Definition.Value)
            : ProjectSubject(
                libraryCorrespondence,
                plan.Type,
                document.Subject,
                assembly);
        var outcome =
            new TypeSubjectInspectionOutcome.Available(
                new(
                    subject,
                    declarations,
                    assemblyBytes,
                    document.Subject.BaseKind,
                    document.Subject.InterfaceCount));
        return rootAdjacency
                is MetadataRootAdjacencyInspectionOutcome.Invalid invalid
            ? Envelope(
                outcome,
                [
                    new InspectionDiagnostic(
                        "type-document.root-adjacency-incomplete",
                        InspectionDiagnosticSeverity.Error,
                        invalid.Detail,
                        assembly.Name.ToString()),
                ])
            : Envelope(outcome);
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
            subject.IsHidden,
            subject.IsCompilerGenerated,
            subject.IsByRefLike,
            subject.IsReadOnly,
            subject.DefinesCoreLibraryRoot,
            subject.DeclaringType?.Definition.Value);

    private static TypeSubjectMemberGroups ProjectDeclarations(
        AssemblyReferenceIdentity assemblyIdentity,
        int assemblyBytes,
        TypeSubjectInspectionPlan plan,
        TypeMemberGroupPopulationInspectionPlan? declarationsPlan,
        MetadataTypeDocumentDeclarations declarations,
        Guid moduleVersionId,
        int startOrdinal) =>
        declarations switch
        {
            MetadataTypeDocumentDeclarations.NotRequested
                when plan.Declarations is null =>
                    new TypeSubjectMemberGroups.NotRequested(),
            MetadataTypeDocumentDeclarations.NotRequested =>
                throw new InvalidOperationException(
                    "The requested Type Member-group population was not inspected."),
            MetadataTypeDocumentDeclarations.BindingMismatch =>
                new TypeSubjectMemberGroups.Rejected(
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

    private static TypeSubjectMemberGroups ProjectDeclarations(
        TypeMemberGroupPopulationInspectionOutcome outcome) =>
        outcome switch
        {
            TypeMemberGroupPopulationInspectionOutcome.Available available =>
                new TypeSubjectMemberGroups.Available(
                    available.Content.Members),
            TypeMemberGroupPopulationInspectionOutcome.Rejected rejected =>
                new TypeSubjectMemberGroups.Rejected(rejected.Reason),
            TypeMemberGroupPopulationInspectionOutcome.Incomplete
                incomplete =>
                    new TypeSubjectMemberGroups.Incomplete(
                        incomplete.Bound,
                        incomplete.Limit,
                        incomplete.Measured),
            TypeMemberGroupPopulationInspectionOutcome.Failed failed =>
                new TypeSubjectMemberGroups.Failed(failed.Reason),
            _ => throw new InvalidOperationException(
                "Unknown Type Member-group population outcome."),
        };

    private static InspectionEnvelope<TypeSubjectInspectionOutcome>
        Rejected(TypeSubjectInspectionRejection reason) =>
        Envelope(new TypeSubjectInspectionOutcome.Rejected(reason));

    private static InspectionEnvelope<TypeSubjectInspectionOutcome>
        Incomplete(
            TypeSubjectInspectionBound bound,
            long limit,
            long measured) =>
        Envelope(
            new TypeSubjectInspectionOutcome.Incomplete(
                bound,
                limit,
                measured));

    private static InspectionEnvelope<TypeSubjectInspectionOutcome>
        Failed(TypeSubjectInspectionFailure reason) =>
        Envelope(new TypeSubjectInspectionOutcome.Failed(reason));

    private static InspectionEnvelope<TypeSubjectInspectionOutcome>
        Envelope(
            TypeSubjectInspectionOutcome outcome,
            IEnumerable<InspectionDiagnostic>? diagnostics = null) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason),
            diagnostics);
}
