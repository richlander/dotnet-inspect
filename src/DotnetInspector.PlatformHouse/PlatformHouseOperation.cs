using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse;

/// <summary>The closed PlatformHouse operation kinds.</summary>
public enum PlatformHouseOperationKind
{
    Realize,
    ResolveAssemblyReference,
    ResolveTypeDefinition,
    ResolveDocumentationEvidence,
}

/// <summary>Resource-free evidence for one operation shape.</summary>
public abstract class PlatformHouseOperationSnapshot
{
    private protected PlatformHouseOperationSnapshot(
        PlatformHouseOperationKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        Kind = kind;
    }

    public PlatformHouseOperationKind Kind { get; }

    public sealed class Realize : PlatformHouseOperationSnapshot
    {
        internal Realize(
            PlatformPopulationDemand population,
            PlatformViewDemand view)
            : base(PlatformHouseOperationKind.Realize)
        {
            Population = population;
            View = view;
        }

        public PlatformPopulationDemand Population { get; }
        public PlatformViewDemand View { get; }
    }

    public sealed class ResolveAssemblyReference : PlatformHouseOperationSnapshot
    {
        internal ResolveAssemblyReference(
            PlatformMetadataRequestIdentity request,
            PlatformRoutePrerequisitesIdentity prerequisites,
            PlatformViewDemand requiredView)
            : base(PlatformHouseOperationKind.ResolveAssemblyReference)
        {
            Request = request;
            Prerequisites = prerequisites;
            RequiredView = requiredView;
        }

        public PlatformMetadataRequestIdentity Request { get; }
        public PlatformRoutePrerequisitesIdentity Prerequisites { get; }
        public PlatformViewDemand RequiredView { get; }
    }

    public sealed class ResolveTypeDefinition : PlatformHouseOperationSnapshot
    {
        internal ResolveTypeDefinition(
            PlatformMetadataRequestIdentity request,
            PlatformReferenceCandidateIdentity startingReference,
            PlatformViewDemand requiredView)
            : base(PlatformHouseOperationKind.ResolveTypeDefinition)
        {
            Request = request;
            StartingReference = startingReference;
            RequiredView = requiredView;
        }

        public PlatformMetadataRequestIdentity Request { get; }
        public PlatformReferenceCandidateIdentity StartingReference { get; }
        public PlatformViewDemand RequiredView { get; }
    }

    public sealed class ResolveDocumentationEvidence :
        PlatformHouseOperationSnapshot
    {
        internal ResolveDocumentationEvidence(
            PlatformDocumentationSubjectIdentity subject,
            PlatformReferenceEvidenceIdentity reference,
            PlatformDocumentationDemand demand,
            PlatformViewCorrespondenceIdentity? implementationCorrespondence)
            : base(PlatformHouseOperationKind.ResolveDocumentationEvidence)
        {
            Subject = subject;
            Reference = reference;
            Demand = demand;
            ImplementationCorrespondence = implementationCorrespondence;
        }

        public PlatformDocumentationSubjectIdentity Subject { get; }
        public PlatformReferenceEvidenceIdentity Reference { get; }
        public PlatformDocumentationDemand Demand { get; }
        public PlatformViewCorrespondenceIdentity? ImplementationCorrespondence
        {
            get;
        }
    }
}

/// <summary>
/// One executable PlatformHouse operation. Live owner inputs remain here and
/// are excluded from the resource-free request snapshot.
/// </summary>
public abstract class PlatformHouseOperation
{
    private protected PlatformHouseOperation(
        PlatformHouseOperationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
    }

    public PlatformHouseOperationKind Kind => Snapshot.Kind;
    public PlatformHouseOperationSnapshot Snapshot { get; }

    public sealed class Realize : PlatformHouseOperation
    {
        public Realize(
            PlatformPopulationDemand population,
            PlatformViewDemand view)
            : base(CreateSnapshot(population, view))
        {
            Population = population;
            View = view;
        }

        public PlatformPopulationDemand Population { get; }
        public PlatformViewDemand View { get; }

        static PlatformHouseOperationSnapshot CreateSnapshot(
            PlatformPopulationDemand population,
            PlatformViewDemand view)
        {
            ArgumentNullException.ThrowIfNull(population);
            if (!Enum.IsDefined(view))
                throw new ArgumentOutOfRangeException(nameof(view));
            return new PlatformHouseOperationSnapshot.Realize(population, view);
        }
    }

    public abstract class ResolveAssemblyReference : PlatformHouseOperation
    {
        private protected ResolveAssemblyReference(
            PlatformMetadataRequestEvidence<AssemblyBindingRequest> request,
            PlatformRoutePrerequisitesIdentity prerequisites,
            PlatformViewDemand requiredView)
            : base(CreateSnapshot(
                request,
                prerequisites,
                requiredView))
        {
            Request = request.Value;
            RequiredView = requiredView;
        }

        public AssemblyBindingRequest Request { get; }
        public PlatformViewDemand RequiredView { get; }

        static PlatformHouseOperationSnapshot CreateSnapshot(
            PlatformMetadataRequestEvidence<AssemblyBindingRequest> request,
            PlatformRoutePrerequisitesIdentity prerequisites,
            PlatformViewDemand requiredView)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(prerequisites);
            if (!Enum.IsDefined(requiredView))
                throw new ArgumentOutOfRangeException(nameof(requiredView));
            return new PlatformHouseOperationSnapshot.ResolveAssemblyReference(
                request.Identity,
                prerequisites,
                requiredView);
        }

        public sealed class WithPrerequisites<TPrerequisites> :
            ResolveAssemblyReference
            where TPrerequisites : notnull
        {
            public WithPrerequisites(
                PlatformMetadataRequestEvidence<AssemblyBindingRequest> request,
                PlatformRoutePrerequisitesEvidence<TPrerequisites> prerequisites,
                PlatformViewDemand requiredView)
                : base(
                    request,
                    (prerequisites
                        ?? throw new ArgumentNullException(
                            nameof(prerequisites))).Identity,
                    requiredView)
            {
                Prerequisites = prerequisites.Value;
            }

            public TPrerequisites Prerequisites { get; }
        }
    }

    public abstract class ResolveTypeDefinition : PlatformHouseOperation
    {
        private protected ResolveTypeDefinition(
            PlatformMetadataRequestEvidence<TypeResolutionRequest> request,
            PlatformReferenceCandidateIdentity startingReference,
            PlatformViewDemand requiredView)
            : base(CreateSnapshot(
                request,
                startingReference,
                requiredView))
        {
            Request = request.Value;
            RequiredView = requiredView;
        }

        public TypeResolutionRequest Request { get; }
        public PlatformViewDemand RequiredView { get; }

        static PlatformHouseOperationSnapshot CreateSnapshot(
            PlatformMetadataRequestEvidence<TypeResolutionRequest> request,
            PlatformReferenceCandidateIdentity startingReference,
            PlatformViewDemand requiredView)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(startingReference);
            if (!Enum.IsDefined(requiredView))
                throw new ArgumentOutOfRangeException(nameof(requiredView));
            return new PlatformHouseOperationSnapshot.ResolveTypeDefinition(
                request.Identity,
                startingReference,
                requiredView);
        }

        public sealed class FromReference<TStartingReference> :
            ResolveTypeDefinition
            where TStartingReference : notnull
        {
            public FromReference(
                PlatformMetadataRequestEvidence<TypeResolutionRequest> request,
                PlatformReferenceCandidateEvidence<TStartingReference>
                    startingReference,
                PlatformViewDemand requiredView)
                : base(
                    request,
                    (startingReference
                        ?? throw new ArgumentNullException(
                            nameof(startingReference))).Identity,
                    requiredView)
            {
                StartingReference = startingReference.Value;
            }

            public TStartingReference StartingReference { get; }
        }
    }

    public abstract class ResolveDocumentationEvidence : PlatformHouseOperation
    {
        private protected ResolveDocumentationEvidence(
            PlatformDocumentationSubjectIdentity subject,
            PlatformReferenceEvidenceIdentity reference,
            PlatformDocumentationDemand demand,
            PlatformViewCorrespondenceIdentity? correspondence)
            : base(CreateSnapshot(
                subject,
                reference,
                demand,
                correspondence))
        {
            Demand = demand;
        }

        public PlatformDocumentationDemand Demand { get; }

        static PlatformHouseOperationSnapshot CreateSnapshot(
            PlatformDocumentationSubjectIdentity subject,
            PlatformReferenceEvidenceIdentity reference,
            PlatformDocumentationDemand demand,
            PlatformViewCorrespondenceIdentity? correspondence)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(reference);
            if (!Enum.IsDefined(demand))
                throw new ArgumentOutOfRangeException(nameof(demand));
            if (demand is PlatformDocumentationDemand.SourceDerived
                    or PlatformDocumentationDemand.CompiledXmlAndSourceDerived
                && correspondence is null)
            {
                throw new ArgumentNullException(
                    nameof(correspondence));
            }

            return new PlatformHouseOperationSnapshot
                .ResolveDocumentationEvidence(
                    subject,
                    reference,
                    demand,
                    correspondence);
        }

        public sealed class CompiledXml<TSubject, TReference> :
            ResolveDocumentationEvidence
            where TSubject : notnull
            where TReference : notnull
        {
            public CompiledXml(
                PlatformDocumentationSubjectEvidence<TSubject> subject,
                PlatformReferenceEvidence<TReference> reference)
                : base(
                    (subject
                        ?? throw new ArgumentNullException(
                            nameof(subject))).Identity,
                    (reference
                        ?? throw new ArgumentNullException(
                            nameof(reference))).Identity,
                    PlatformDocumentationDemand.CompiledXml,
                    correspondence: null)
            {
                Subject = subject.Value;
                Reference = reference.Value;
            }

            public TSubject Subject { get; }
            public TReference Reference { get; }
        }

        public sealed class SourceDerived<
            TSubject,
            TReference,
            TCorrespondence> : ResolveDocumentationEvidence
            where TSubject : notnull
            where TReference : notnull
            where TCorrespondence : notnull
        {
            public SourceDerived(
                PlatformDocumentationSubjectEvidence<TSubject> subject,
                PlatformReferenceEvidence<TReference> reference,
                PlatformViewCorrespondenceEvidence<TCorrespondence>
                    implementationCorrespondence)
                : base(
                    (subject
                        ?? throw new ArgumentNullException(
                            nameof(subject))).Identity,
                    (reference
                        ?? throw new ArgumentNullException(
                            nameof(reference))).Identity,
                    PlatformDocumentationDemand.SourceDerived,
                    (implementationCorrespondence
                        ?? throw new ArgumentNullException(
                            nameof(implementationCorrespondence))).Identity)
            {
                Subject = subject.Value;
                Reference = reference.Value;
                ImplementationCorrespondence =
                    implementationCorrespondence.Value;
            }

            public TSubject Subject { get; }
            public TReference Reference { get; }
            public TCorrespondence ImplementationCorrespondence { get; }
        }

        public sealed class CompiledXmlAndSourceDerived<
            TSubject,
            TReference,
            TCorrespondence> : ResolveDocumentationEvidence
            where TSubject : notnull
            where TReference : notnull
            where TCorrespondence : notnull
        {
            public CompiledXmlAndSourceDerived(
                PlatformDocumentationSubjectEvidence<TSubject> subject,
                PlatformReferenceEvidence<TReference> reference,
                PlatformViewCorrespondenceEvidence<TCorrespondence>
                    implementationCorrespondence)
                : base(
                    (subject
                        ?? throw new ArgumentNullException(
                            nameof(subject))).Identity,
                    (reference
                        ?? throw new ArgumentNullException(
                            nameof(reference))).Identity,
                    PlatformDocumentationDemand.CompiledXmlAndSourceDerived,
                    (implementationCorrespondence
                        ?? throw new ArgumentNullException(
                            nameof(implementationCorrespondence))).Identity)
            {
                Subject = subject.Value;
                Reference = reference.Value;
                ImplementationCorrespondence =
                    implementationCorrespondence.Value;
            }

            public TSubject Subject { get; }
            public TReference Reference { get; }
            public TCorrespondence ImplementationCorrespondence { get; }
        }
    }
}
