using System.Collections.Immutable;
using System.Reflection;
using ILInspector.Metadata;
using Inspector.Artifacts;

namespace ILInspector.Metadata.Tests;

public sealed class AssemblyBindingDecisionTests
{
    [Fact]
    public void ProjectedBindingDecision_SurvivesContextAndSourceRetirement()
    {
        byte[] image = File.ReadAllBytes(typeof(object).Assembly.Location);
        bool sourceAvailable = true;
        AssemblyResolutionProvenance provenance =
            AssemblyResolutionProvenance.Platform(
                "Microsoft.NETCore.App",
                typeof(object).Assembly
                    .GetName()
                    .Version?
                    .ToString(),
                "runtime");
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                Open,
                provenance)
            ?? throw new Xunit.Sdk.XunitException(
                "System.Private.CoreLib must be a managed assembly.");
        var request = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(assembly.Identity),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Platform);
        var policy = new FixedPolicy(
            AssemblyBindingSelection.Found(assembly));
        var catalog = new TypeResolutionCatalog();
        TypeResolutionContext context = catalog.CreateContext(
            policy,
            roots: [],
            bindingRequests: [request],
            requests: []);

        AssemblyCatalogId expectedCatalog = context.Catalog;
        AssemblyCatalogGenerationId expectedGeneration = context.Generation;
        var decision = Assert.IsType<AssemblyBindingDecision.Resolved>(
            context.ProjectBindingDecision(request));

        context.Dispose();
        catalog.Dispose();
        sourceAvailable = false;

        Assert.Equal(expectedCatalog, decision.Catalog);
        Assert.Same(expectedGeneration, decision.Generation);
        Assert.Same(policy.Version, decision.PolicyVersion);
        Assert.Same(request.Target, decision.Request.Target);
        Assert.IsType<AssemblyBindingRequestOriginEvidence.Global>(
            decision.Request.Origin);
        Assert.Equal(request.Scope, decision.Request.Scope);
        Assert.Same(decision.Candidate, decision.Occurrence.Supplier);
        Assert.Same(
            assembly.Registration,
            decision.Candidate.Registration);
        Assert.Equal(assembly.Identity, decision.Candidate.Identity);
        Assert.NotNull(decision.Candidate.ModuleVersionId);
        Assert.NotEqual(Guid.Empty, decision.Candidate.ModuleVersionId);
        Assert.Same(provenance, decision.Candidate.Provenance);
        Assert.Empty(decision.ShadowedSuppliers);
        Assert.Throws<IOException>(() => assembly.OpenRead());
        Assert.Throws<ObjectDisposedException>(
            () => context.ProjectBindingDecision(request));

        Stream Open() =>
            sourceAvailable
                ? new MemoryStream(image, writable: false)
                : throw new IOException("The original source is retired.");
    }

    [Fact]
    public void ProjectedBindingDecision_PreservesClosedOutcomeAlgebra()
    {
        byte[] image = File.ReadAllBytes(typeof(object).Assembly.Location);
        ResolvedAssemblyReference first = Descriptor(image, "first");
        ResolvedAssemblyReference second = Descriptor(image, "second");
        ResolvedAssemblyReference third = Descriptor(image, "third");
        AssemblyBindingRequest request = GlobalRequest(first.Identity);

        var resolved = Assert.IsType<AssemblyBindingDecision.Resolved>(
            Project(
                AssemblyBindingSelection.Finalized(
                    AssemblyBindingOccurrence.Seed(first),
                    [second, third]),
                request));
        Assert.Same(first.Registration, resolved.Candidate.Registration);
        Assert.Equal(
            [second.Registration, third.Registration],
            resolved.ShadowedSuppliers
                .Select(supplier => supplier.Registration));

        foreach (AssemblyBindingMissDisposition disposition
            in Enum.GetValues<AssemblyBindingMissDisposition>())
        {
            var missing = Assert.IsType<AssemblyBindingDecision.Missing>(
                Project(Missing(disposition), request));
            Assert.Equal(disposition, missing.Disposition);
        }

        var failure = new AssemblyBindingFailure(
            AssemblyBindingFailureKind.CandidateUnavailable,
            CandidateOpenFailureKind.InvalidImage)
        {
            MetadataRootReason =
                MetadataRootMalformedReason.InvalidSignature,
        };
        var unavailable =
            Assert.IsType<AssemblyBindingDecision.Unavailable>(
                Project(
                    AssemblyBindingSelection.CannotSelect(failure),
                    request));
        Assert.Same(failure, unavailable.Failure);

        var ambiguous = Assert.IsType<AssemblyBindingDecision.Ambiguous>(
            Project(
                AssemblyBindingSelection.Finalized(
                    [first, second],
                    [third]),
                request));
        Assert.Equal(
            [first.Registration, second.Registration],
            ambiguous.Candidates.Select(
                candidate => candidate.Registration));
        Assert.Equal(
            [third.Registration],
            ambiguous.ShadowedSuppliers.Select(
                supplier => supplier.Registration));

        var rejectedFailure = new AssemblyBindingFailure(
            AssemblyBindingFailureKind.InvalidPolicyResult);
        var rejected = Assert.IsType<AssemblyBindingDecision.Rejected>(
            Project(
                AssemblyBindingSelection.Invalid(rejectedFailure),
                request));
        Assert.Same(rejectedFailure, rejected.Failure);

        AssemblyBindingOccurrence origin =
            AssemblyBindingOccurrence.Seed(first);
        var unplanned = new AssemblyBindingRequest(
            AssemblyBindingTarget.Reference(second.Identity),
            AssemblyBindingOrigin.FromOccurrence(origin),
            AssemblyResolutionScope.Platform);
        var expansionPolicy = new FixedPolicy(
            AssemblyBindingSelection.NameNotOwned());
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            expansionPolicy,
            roots: [first],
            bindingRequests: [],
            requests: []);

        var expansion =
            Assert.IsType<AssemblyBindingDecision.ExpansionRequired>(
                context.ProjectBindingDecision(unplanned));
        var requesting = Assert.IsType<
            AssemblyBindingRequestOriginEvidence.RequestingAssembly>(
                expansion.Request.Origin);
        Assert.Same(first.Registration, requesting.Supplier.Registration);
        Assert.NotNull(requesting.Lineage);
        Assert.Empty(expansionPolicy.Requests);
    }

    [Fact]
    public void ProjectedBindingDecision_PublicClosureIsResourceFree()
    {
        Type[] ownerConstructedTypes =
        [
            typeof(AssemblyBindingLineageIdentity),
            typeof(AssemblyBindingSupplierEvidence),
            typeof(AssemblyBindingOccurrenceEvidence),
            typeof(AssemblyBindingRequestOriginEvidence.Global),
            typeof(AssemblyBindingRequestOriginEvidence.RequestingAssembly),
            typeof(AssemblyBindingRequestEvidence),
            .. typeof(AssemblyBindingDecision).GetNestedTypes(
                BindingFlags.Public),
        ];
        Assert.All(
            ownerConstructedTypes,
            type => Assert.Empty(
                type.GetConstructors(
                    BindingFlags.Public
                    | BindingFlags.Instance)));

        Type[] prohibited =
        [
            typeof(ResolvedAssemblyReference),
            typeof(ResolvedAssemblyCandidate),
            typeof(AssemblyBindingOccurrence),
            typeof(AssemblyBindingLineage),
            typeof(TypeResolutionContext),
            typeof(TypeResolutionCatalog),
            typeof(Delegate),
            typeof(Stream),
            typeof(Exception),
            typeof(IDisposable),
            typeof(IAsyncDisposable),
        ];

        Assert.DoesNotContain(
            PublicTypeClosure(
                typeof(AssemblyBindingDecision),
                typeof(AssemblyBindingRequestOriginEvidence)),
            type => prohibited.Any(
                denied => denied.IsAssignableFrom(type)));
    }

    static AssemblyBindingDecision Project(
        AssemblyBindingSelection selection,
        AssemblyBindingRequest request)
    {
        var policy = new FixedPolicy(selection);
        using var catalog = new TypeResolutionCatalog();
        using TypeResolutionContext context = catalog.CreateContext(
            policy,
            roots: [],
            bindingRequests: [request],
            requests: []);
        AssemblyBindingDecision decision =
            context.ProjectBindingDecision(request);
        Assert.Single(policy.Requests);
        return decision;
    }

    static ResolvedAssemblyReference Descriptor(
        byte[] image,
        string source) =>
        ResolvedAssemblyReference.CreateFromStreamIfManaged(
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Platform(
                "Microsoft.NETCore.App",
                typeof(object).Assembly
                    .GetName()
                    .Version?
                    .ToString(),
                source))
        ?? throw new Xunit.Sdk.XunitException(
            "System.Private.CoreLib must be a managed assembly.");

    static AssemblyBindingRequest GlobalRequest(
        AssemblyReferenceIdentity identity) =>
        new(
            AssemblyBindingTarget.Reference(identity),
            AssemblyBindingOrigin.Global(),
            AssemblyResolutionScope.Platform);

    static AssemblyBindingSelection Missing(
        AssemblyBindingMissDisposition disposition) =>
        disposition switch
        {
            AssemblyBindingMissDisposition.Undifferentiated =>
                AssemblyBindingSelection.NotFound(),
            AssemblyBindingMissDisposition.NoNameOwner =>
                AssemblyBindingSelection.NameNotOwned(),
            AssemblyBindingMissDisposition.NameOwnedNoMatch =>
                AssemblyBindingSelection.NameOwnedButNoMatch(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(disposition)),
        };

    static IReadOnlyCollection<Type> PublicTypeClosure(
        params Type[] roots)
    {
        var closure = new HashSet<Type>();
        var pending = new Queue<Type>(
            roots.SelectMany(
                root => root
                    .GetNestedTypes(BindingFlags.Public)
                    .Prepend(root)));
        while (pending.TryDequeue(out Type? type))
        {
            type = ElementType(type);
            if (!closure.Add(type))
                continue;
            if (type.Namespace?.StartsWith(
                    "System",
                    StringComparison.Ordinal)
                == true)
            {
                continue;
            }

            foreach (Type nested in type.GetNestedTypes(
                BindingFlags.Public))
            {
                pending.Enqueue(nested);
            }
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                pending.Enqueue(property.PropertyType);
            }
        }

        return closure;
    }

    static Type ElementType(Type type)
    {
        if (type.IsArray)
            return ElementType(type.GetElementType()!);
        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Nullable<>)
                || definition == typeof(ImmutableArray<>)
                || definition == typeof(IReadOnlyList<>)
                || definition == typeof(IEnumerable<>))
            {
                return ElementType(type.GetGenericArguments()[0]);
            }
        }
        return type;
    }

    sealed class FixedPolicy(
        AssemblyBindingSelection selection)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();
        public List<AssemblyBindingRequest> Requests { get; } = [];

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            Requests.Add(request);
            return new AssemblyBindingSelectionSnapshot(
                Version,
                selection);
        }
    }
}
