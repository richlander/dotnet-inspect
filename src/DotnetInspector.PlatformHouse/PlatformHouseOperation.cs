using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse;

/// <summary>The closed PlatformHouse operation kinds.</summary>
public enum PlatformHouseOperationKind
{
    Realize,
    ResolveAssemblyReference,
    ResolveTypeDefinition,
}

[Flags]
public enum PlatformLibraryContentDemand
{
    None = 0,
    CompiledXmlDocumentation = 1,
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
            PlatformViewDemand view,
            PlatformLibraryContentDemand contentDemand)
            : base(PlatformHouseOperationKind.Realize)
        {
            Population = population;
            View = view;
            ContentDemand = contentDemand;
        }

        public PlatformPopulationDemand Population { get; }
        public PlatformViewDemand View { get; }
        public PlatformLibraryContentDemand ContentDemand { get; }
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
            PlatformViewDemand view,
            PlatformLibraryContentDemand contentDemand =
                PlatformLibraryContentDemand.None)
            : base(CreateSnapshot(population, view, contentDemand))
        {
            Population = population;
            View = view;
            ContentDemand = contentDemand;
        }

        public PlatformPopulationDemand Population { get; }
        public PlatformViewDemand View { get; }
        public PlatformLibraryContentDemand ContentDemand { get; }

        static PlatformHouseOperationSnapshot CreateSnapshot(
            PlatformPopulationDemand population,
            PlatformViewDemand view,
            PlatformLibraryContentDemand contentDemand)
        {
            ArgumentNullException.ThrowIfNull(population);
            if (!Enum.IsDefined(view))
                throw new ArgumentOutOfRangeException(nameof(view));
            if ((contentDemand
                    & ~PlatformLibraryContentDemand
                        .CompiledXmlDocumentation) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(contentDemand));
            }
            if (contentDemand
                    .HasFlag(
                        PlatformLibraryContentDemand
                            .CompiledXmlDocumentation)
                && (view == PlatformViewDemand.Implementation
                    || population
                        is not PlatformPopulationDemand.Library))
            {
                throw new ArgumentException(
                    "Compiled XML documentation requires one Library realization with a reference view.",
                    nameof(contentDemand));
            }
            return new PlatformHouseOperationSnapshot.Realize(
                population,
                view,
                contentDemand);
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
}
