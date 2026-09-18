using System.Collections.Immutable;

namespace ILInspector.Metadata;

/// <summary>
/// Opaque operation-local identity for one binding lineage in a detached
/// decision.
/// </summary>
public sealed class AssemblyBindingLineageIdentity
{
    internal AssemblyBindingLineageIdentity()
    {
    }
}

/// <summary>
/// Resource-free evidence for one physical assembly supplier.
/// </summary>
public sealed class AssemblyBindingSupplierEvidence
{
    internal AssemblyBindingSupplierEvidence(
        AssemblyAcquisitionRegistration registration,
        AssemblyReferenceIdentity identity,
        Guid? moduleVersionId,
        AssemblyResolutionProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(provenance);
        if (moduleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A projected module version identifier cannot be empty.",
                nameof(moduleVersionId));
        }

        Registration = registration;
        Identity = identity;
        ModuleVersionId = moduleVersionId;
        Provenance = provenance;
    }

    public AssemblyAcquisitionRegistration Registration { get; }
    public AssemblyReferenceIdentity Identity { get; }
    public Guid? ModuleVersionId { get; }
    public AssemblyResolutionProvenance Provenance { get; }
}

/// <summary>
/// Resource-free evidence for one selected supplier and its continuation
/// identity.
/// </summary>
public sealed class AssemblyBindingOccurrenceEvidence
{
    internal AssemblyBindingOccurrenceEvidence(
        AssemblyBindingSupplierEvidence supplier,
        AssemblyBindingLineageIdentity lineage)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        ArgumentNullException.ThrowIfNull(lineage);
        Supplier = supplier;
        Lineage = lineage;
    }

    public AssemblyBindingSupplierEvidence Supplier { get; }
    public AssemblyBindingLineageIdentity Lineage { get; }
}

/// <summary>A resource-free origin for one detached binding request.</summary>
public abstract class AssemblyBindingRequestOriginEvidence
{
    private protected AssemblyBindingRequestOriginEvidence()
    {
    }

    public sealed class Global : AssemblyBindingRequestOriginEvidence
    {
        internal Global()
        {
        }
    }

    public sealed class RequestingAssembly :
        AssemblyBindingRequestOriginEvidence
    {
        internal RequestingAssembly(
            AssemblyBindingSupplierEvidence supplier,
            AssemblyBindingLineageIdentity? lineage)
        {
            ArgumentNullException.ThrowIfNull(supplier);
            Supplier = supplier;
            Lineage = lineage;
        }

        public AssemblyBindingSupplierEvidence Supplier { get; }
        public AssemblyBindingLineageIdentity? Lineage { get; }
    }
}

/// <summary>Resource-free snapshot of one exact binding request.</summary>
public sealed class AssemblyBindingRequestEvidence
{
    internal AssemblyBindingRequestEvidence(
        AssemblyBindingTarget target,
        AssemblyBindingRequestOriginEvidence origin,
        AssemblyResolutionScope scope)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(origin);
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        Target = target;
        Origin = origin;
        Scope = scope;
    }

    public AssemblyBindingTarget Target { get; }
    public AssemblyBindingRequestOriginEvidence Origin { get; }
    public AssemblyResolutionScope Scope { get; }
}

/// <summary>
/// Metadata-owned resource-free projection of one frozen assembly-binding
/// outcome.
/// </summary>
public abstract class AssemblyBindingDecision
{
    private protected AssemblyBindingDecision(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        AssemblyBindingPolicyVersion policyVersion,
        AssemblyBindingRequestEvidence request)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(policyVersion);
        ArgumentNullException.ThrowIfNull(request);
        Catalog = catalog;
        Generation = generation;
        PolicyVersion = policyVersion;
        Request = request;
    }

    public AssemblyCatalogId Catalog { get; }
    public AssemblyCatalogGenerationId Generation { get; }
    public AssemblyBindingPolicyVersion PolicyVersion { get; }
    public AssemblyBindingRequestEvidence Request { get; }

    public sealed class Resolved : AssemblyBindingDecision
    {
        internal Resolved(
            AssemblyCatalogId catalog,
            AssemblyCatalogGenerationId generation,
            AssemblyBindingPolicyVersion policyVersion,
            AssemblyBindingRequestEvidence request,
            AssemblyBindingSupplierEvidence candidate,
            AssemblyBindingOccurrenceEvidence occurrence,
            ImmutableArray<AssemblyBindingSupplierEvidence>
                shadowedSuppliers)
            : base(catalog, generation, policyVersion, request)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            ArgumentNullException.ThrowIfNull(occurrence);
            if (!ReferenceEquals(candidate, occurrence.Supplier))
            {
                throw new ArgumentException(
                    "The resolved candidate and occurrence must identify one projected supplier.",
                    nameof(occurrence));
            }

            Candidate = candidate;
            Occurrence = occurrence;
            ShadowedSuppliers = shadowedSuppliers;
        }

        public AssemblyBindingSupplierEvidence Candidate { get; }
        public AssemblyBindingOccurrenceEvidence Occurrence { get; }
        public ImmutableArray<AssemblyBindingSupplierEvidence>
            ShadowedSuppliers { get; }
    }

    public sealed class Missing : AssemblyBindingDecision
    {
        internal Missing(
            AssemblyCatalogId catalog,
            AssemblyCatalogGenerationId generation,
            AssemblyBindingPolicyVersion policyVersion,
            AssemblyBindingRequestEvidence request,
            AssemblyBindingMissDisposition disposition)
            : base(catalog, generation, policyVersion, request)
        {
            if (!Enum.IsDefined(disposition))
                throw new ArgumentOutOfRangeException(nameof(disposition));
            Disposition = disposition;
        }

        public AssemblyBindingMissDisposition Disposition { get; }
    }

    public sealed class Unavailable : AssemblyBindingDecision
    {
        internal Unavailable(
            AssemblyCatalogId catalog,
            AssemblyCatalogGenerationId generation,
            AssemblyBindingPolicyVersion policyVersion,
            AssemblyBindingRequestEvidence request,
            AssemblyBindingFailure failure,
            ImmutableArray<AssemblyBindingSupplierEvidence>
                shadowedSuppliers)
            : base(catalog, generation, policyVersion, request)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
            ShadowedSuppliers = shadowedSuppliers;
        }

        public AssemblyBindingFailure Failure { get; }
        public ImmutableArray<AssemblyBindingSupplierEvidence>
            ShadowedSuppliers { get; }
    }

    public sealed class Ambiguous : AssemblyBindingDecision
    {
        internal Ambiguous(
            AssemblyCatalogId catalog,
            AssemblyCatalogGenerationId generation,
            AssemblyBindingPolicyVersion policyVersion,
            AssemblyBindingRequestEvidence request,
            ImmutableArray<AssemblyBindingSupplierEvidence> candidates,
            ImmutableArray<AssemblyBindingSupplierEvidence>
                shadowedSuppliers)
            : base(catalog, generation, policyVersion, request)
        {
            if (candidates.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "An ambiguous decision requires projected candidates.",
                    nameof(candidates));
            }

            Candidates = candidates;
            ShadowedSuppliers = shadowedSuppliers;
        }

        public ImmutableArray<AssemblyBindingSupplierEvidence> Candidates
        {
            get;
        }
        public ImmutableArray<AssemblyBindingSupplierEvidence>
            ShadowedSuppliers { get; }
    }

    public sealed class Rejected : AssemblyBindingDecision
    {
        internal Rejected(
            AssemblyCatalogId catalog,
            AssemblyCatalogGenerationId generation,
            AssemblyBindingPolicyVersion policyVersion,
            AssemblyBindingRequestEvidence request,
            AssemblyBindingFailure failure)
            : base(catalog, generation, policyVersion, request)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public AssemblyBindingFailure Failure { get; }
    }

    public sealed class ExpansionRequired : AssemblyBindingDecision
    {
        internal ExpansionRequired(
            AssemblyCatalogId catalog,
            AssemblyCatalogGenerationId generation,
            AssemblyBindingPolicyVersion policyVersion,
            AssemblyBindingRequestEvidence request)
            : base(catalog, generation, policyVersion, request)
        {
        }
    }
}

internal static class AssemblyBindingDecisionProjection
{
    internal static AssemblyBindingDecision Project(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        AssemblyBindingPolicyVersion policyVersion,
        AssemblyBindingRequest request,
        AssemblyBindingOutcome outcome,
        Func<ResolvedAssemblyCandidate, AssemblyInventorySnapshot>
            getInventory)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(policyVersion);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(getInventory);

        var projection = new ProjectionContext(getInventory);
        projection.Prepare(outcome);
        AssemblyBindingRequestEvidence requestEvidence =
            projection.Request(request);

        return outcome switch
        {
            AssemblyBindingOutcome.Resolved resolved =>
                new AssemblyBindingDecision.Resolved(
                    catalog,
                    generation,
                    policyVersion,
                    requestEvidence,
                    projection.Candidate(resolved.Candidate),
                    projection.Occurrence(resolved.Occurrence),
                    projection.Suppliers(resolved.ShadowedAssemblies)),
            AssemblyBindingOutcome.Missing missing =>
                new AssemblyBindingDecision.Missing(
                    catalog,
                    generation,
                    policyVersion,
                    requestEvidence,
                    missing.Disposition),
            AssemblyBindingOutcome.Unavailable unavailable =>
                new AssemblyBindingDecision.Unavailable(
                    catalog,
                    generation,
                    policyVersion,
                    requestEvidence,
                    unavailable.Failure,
                    projection.Suppliers(
                        unavailable.ShadowedAssemblies)),
            AssemblyBindingOutcome.Ambiguous ambiguous =>
                new AssemblyBindingDecision.Ambiguous(
                    catalog,
                    generation,
                    policyVersion,
                    requestEvidence,
                    projection.Candidates(ambiguous.Candidates),
                    projection.Suppliers(
                        ambiguous.ShadowedAssemblies)),
            AssemblyBindingOutcome.Rejected rejected =>
                new AssemblyBindingDecision.Rejected(
                    catalog,
                    generation,
                    policyVersion,
                    requestEvidence,
                    rejected.Failure),
            AssemblyBindingOutcome.ExpansionRequired =>
                new AssemblyBindingDecision.ExpansionRequired(
                    catalog,
                    generation,
                    policyVersion,
                    requestEvidence),
            _ => throw new InvalidOperationException(
                "Unknown assembly-binding outcome."),
        };
    }

    sealed class ProjectionContext(
        Func<ResolvedAssemblyCandidate, AssemblyInventorySnapshot>
            getInventory)
    {
        readonly Dictionary<
            AssemblyAcquisitionRegistration,
            AssemblyBindingSupplierEvidence> _suppliers =
                new(ReferenceEqualityComparer.Instance);
        readonly Dictionary<
            AssemblyBindingLineage,
            AssemblyBindingLineageIdentity> _lineages = [];

        internal void Prepare(AssemblyBindingOutcome outcome)
        {
            switch (outcome)
            {
                case AssemblyBindingOutcome.Resolved resolved:
                    _ = Candidate(resolved.Candidate);
                    _ = Suppliers(resolved.ShadowedAssemblies);
                    break;
                case AssemblyBindingOutcome.Unavailable unavailable:
                    _ = Suppliers(unavailable.ShadowedAssemblies);
                    break;
                case AssemblyBindingOutcome.Ambiguous ambiguous:
                    _ = Candidates(ambiguous.Candidates);
                    _ = Suppliers(ambiguous.ShadowedAssemblies);
                    break;
            }
        }

        internal AssemblyBindingRequestEvidence Request(
            AssemblyBindingRequest request)
        {
            AssemblyBindingRequestOriginEvidence origin =
                request.Origin switch
                {
                    AssemblyBindingOrigin.GlobalOrigin =>
                        new AssemblyBindingRequestOriginEvidence.Global(),
                    AssemblyBindingOrigin.RequestingAssembly requesting =>
                        new AssemblyBindingRequestOriginEvidence
                            .RequestingAssembly(
                                Supplier(requesting.Assembly),
                                requesting.Lineage is { } lineage
                                    ? Lineage(lineage)
                                    : null),
                    _ => throw new InvalidOperationException(
                        "Unknown assembly-binding origin."),
                };
            return new AssemblyBindingRequestEvidence(
                request.Target,
                origin,
                request.Scope);
        }

        internal AssemblyBindingSupplierEvidence Candidate(
            ResolvedAssemblyCandidate candidate)
        {
            AssemblyInventorySnapshot inventory = getInventory(candidate);
            return Supplier(
                candidate.Assembly,
                inventory.ModuleVersionId);
        }

        internal ImmutableArray<AssemblyBindingSupplierEvidence> Candidates(
            ImmutableArray<ResolvedAssemblyCandidate> candidates) =>
            [.. candidates.Select(Candidate)];

        internal AssemblyBindingOccurrenceEvidence Occurrence(
            AssemblyBindingOccurrence occurrence) =>
            new(
                Supplier(occurrence.Assembly),
                Lineage(occurrence.Lineage));

        internal ImmutableArray<AssemblyBindingSupplierEvidence> Suppliers(
            ImmutableArray<ResolvedAssemblyReference> suppliers) =>
            [.. suppliers.Select(
                supplier => Supplier(supplier))];

        AssemblyBindingSupplierEvidence Supplier(
            ResolvedAssemblyReference supplier,
            Guid? moduleVersionId = null)
        {
            moduleVersionId ??= supplier.Registration.ModuleVersionId;
            if (_suppliers.TryGetValue(
                    supplier.Registration,
                    out AssemblyBindingSupplierEvidence? evidence))
            {
                if (moduleVersionId is not null
                    && evidence.ModuleVersionId != moduleVersionId)
                {
                    throw new InvalidOperationException(
                        "One acquisition registration has inconsistent projected module identities.");
                }
                return evidence;
            }

            evidence = new AssemblyBindingSupplierEvidence(
                supplier.Registration,
                supplier.Identity,
                moduleVersionId,
                supplier.Provenance);
            _suppliers.Add(supplier.Registration, evidence);
            return evidence;
        }

        AssemblyBindingLineageIdentity Lineage(
            AssemblyBindingLineage lineage)
        {
            if (!_lineages.TryGetValue(
                    lineage,
                    out AssemblyBindingLineageIdentity? identity))
            {
                identity = new AssemblyBindingLineageIdentity();
                _lineages.Add(lineage, identity);
            }
            return identity;
        }
    }
}
