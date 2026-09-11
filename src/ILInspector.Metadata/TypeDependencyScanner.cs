using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Runtime.ExceptionServices;
using System.Reflection.PortableExecutable;
using CSharpText;

namespace ILInspector.Metadata;

/// <summary>
/// A node in a type dependency tree (base classes and interfaces).
/// </summary>
public record TypeDependencyNode(string TypeName, List<TypeDependencyNode> Children);

/// <summary>The metadata relationship that makes one type depend on another.</summary>
public enum TypeDependencyRelationshipKind
{
    BaseType,
    Interface,
}

/// <summary>
/// One direct type dependency retained independently of tree expansion. Shared
/// targets and revisits therefore keep every incoming relationship.
/// </summary>
public sealed record TypeDependencyRelationship(
    string SourceTypeName,
    string TargetTypeName,
    TypeDependencyRelationshipKind Kind,
    int Ordinal);

/// <summary>
/// Identifies a type whose outgoing relationships were excluded by a depth
/// bound.
/// </summary>
public sealed record TypeDependencyDepthBoundary(
    string TypeName,
    int MaximumDepth);

/// <summary>
/// Identifies why a candidate assembly was rejected during a dependency scan.
/// </summary>
public enum TypeDependencyRejectionKind
{
    UnsupportedMetadataFormat,
    MalformedMetadataRoot,
    InvalidImage,
}

/// <summary>
/// Records a candidate assembly that a dependency scan rejected. A rejection
/// scopes to its own participant and never aborts the surrounding scan.
/// </summary>
public sealed record TypeDependencyRejection(
    string AssemblyPath,
    TypeDependencyRejectionKind Kind)
{
    public MetadataRootMalformedReason? MetadataRootReason { get; init; }
}

/// <summary>
/// Every candidate in a dependency scan was rejected, so the scan has no
/// surviving participant to scope its rejections against. Each rejection is
/// an independent outcome: throwing one would discard the rest, so they are
/// carried together. <see cref="Rejections"/> holds the typed record for each
/// rejected candidate, keeping the path-to-mechanism correspondence available
/// as data rather than only in the rendered message.
/// </summary>
public sealed class AllCandidatesRejectedException : AggregateException
{
    private readonly string renderedMessage;

    internal AllCandidatesRejectedException(
        string message,
        ImmutableArray<TypeDependencyRejection> rejections,
        IEnumerable<Exception> mechanisms)
        : base(message, mechanisms)
    {
        renderedMessage = message;
        Rejections = rejections;
    }

    /// <summary>
    /// The rendered path-to-mechanism pairing, without the inner-message list
    /// <see cref="AggregateException"/> appends to its own message. That
    /// default would print every mechanism a second time at a command
    /// boundary, unpaired with the path it belongs to.
    /// </summary>
    public override string Message => renderedMessage;

    /// <summary>
    /// The rejected candidates, in scan order. Each entry corresponds to the
    /// inner exception at the same index.
    /// </summary>
    public ImmutableArray<TypeDependencyRejection> Rejections { get; }
}

/// <summary>
/// Result of building a type dependency tree.
/// </summary>
public record TypeDependencyResult(string? MatchedType, List<TypeDependencyNode> Tree)
{
    public bool Found => MatchedType != null;

    /// <summary>
    /// Direct owner-issued relationships in deterministic traversal order.
    /// </summary>
    public IReadOnlyList<TypeDependencyRelationship> Relationships { get; init; } = [];

    /// <summary>
    /// Exact type identities whose outgoing relationships were excluded by the
    /// requested depth.
    /// </summary>
    public IReadOnlyList<TypeDependencyDepthBoundary> DepthBoundaries { get; init; } = [];

    /// <summary>
    /// Candidate assemblies the scan rejected on metadata-format grounds.
    /// </summary>
    public IReadOnlyList<TypeDependencyRejection> Rejections { get; init; } = [];
}

/// <summary>
/// One acquisition-issued candidate's resource-free dependency-scan outcome.
/// </summary>
public abstract class TypeDependencyCandidateOutcome
{
    private protected TypeDependencyCandidateOutcome(
        AssemblyAcquisitionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        Registration = registration;
    }

    public AssemblyAcquisitionRegistration Registration { get; }

    /// <summary>The candidate contributed its complete staged metadata rows.</summary>
    public sealed class Completed : TypeDependencyCandidateOutcome
    {
        internal Completed(
            AssemblyAcquisitionRegistration registration)
            : base(registration)
        {
        }
    }

    /// <summary>The candidate was excluded before any staged rows were published.</summary>
    public sealed class Rejected : TypeDependencyCandidateOutcome
    {
        internal Rejected(
            AssemblyAcquisitionRegistration registration,
            CandidateOpenFailure failure)
            : base(registration)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public CandidateOpenFailure Failure { get; }
    }
}

/// <summary>
/// Dependency graph facts and ordered candidate outcomes for one retained
/// descriptor population.
/// </summary>
public sealed class TypeDependencyPopulationResult
{
    internal TypeDependencyPopulationResult(
        TypeDependencyResult dependency,
        ImmutableArray<TypeDependencyCandidateOutcome> candidates,
        AssemblyAcquisitionRegistration? matchedRegistration)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        Dependency = dependency;
        Candidates = candidates;
        MatchedRegistration = matchedRegistration;
    }

    public TypeDependencyResult Dependency { get; }
    public ImmutableArray<TypeDependencyCandidateOutcome> Candidates { get; }

    /// <summary>
    /// The acquisition registration that contributed the selected root, or
    /// <see langword="null"/> when the target was not found.
    /// </summary>
    public AssemblyAcquisitionRegistration? MatchedRegistration { get; }

    /// <summary>
    /// Whether at least one candidate contributed its complete staged rows.
    /// </summary>
    public bool HasSurvivingParticipant =>
        Candidates.Any(
            static candidate =>
                candidate is TypeDependencyCandidateOutcome.Completed);

    /// <summary>
    /// Whether at least one candidate survived and every selected candidate
    /// completed.
    /// </summary>
    public bool IsComplete =>
        HasSurvivingParticipant
        && Candidates.All(
            static candidate =>
                candidate is TypeDependencyCandidateOutcome.Completed);
}

/// <summary>
/// Walks the inheritance and interface implementation graph upward from a type.
/// This is the inverse of <see cref="TypeHierarchyScanner.FindImplementers"/> —
/// it shows what a type depends on, not what depends on it.
/// </summary>
public static class TypeDependencyScanner
{
    /// <summary>
    /// Builds the dependency tree for a type found in the given assemblies.
    /// Returns the direct base types and interfaces as root-level nodes,
    /// each with their own recursive dependencies.
    /// </summary>
    public static TypeDependencyResult BuildDependencyTree(
        string targetType,
        IReadOnlyList<string> assemblyPaths,
        int? maximumDepth = null)
    {
        ValidateMaximumDepth(maximumDepth);
        var typeIndex = CreateTypeIndex();
        var images = new List<CandidateImage>();
        var rejections = new List<TypeDependencyRejection>();
        var admittedAny = false;
        ExceptionDispatchInfo? firstInvalidImage = null;

        // The decoder's exception carries detail that a reconstructed one
        // would lose, so each invalid image keeps its own captured cause.
        var invalidImageCauses =
            new Dictionary<string, BadImageFormatException>(
                StringComparer.Ordinal);

        try
        {
            foreach (var path in assemblyPaths)
            {
                try
                {
                    CandidateImage image =
                        CandidateImage.Open(() => File.OpenRead(path));
                    images.Add(image);

                    try
                    {
                        CandidateStage stage =
                            StageCandidate(
                                image.PeReader,
                                descriptor: null);
                        if (!stage.HasManagedMetadata)
                            continue;

                        PublishStage(typeIndex, stage);

                        // Only a participant that decoded all the way through
                        // counts as surviving. A partially indexed one cannot
                        // scope another participant's rejection.
                        admittedAny = true;
                    }
                    // Admission passed but the metadata itself did not decode.
                    // That is an ordinary invalid-image outcome rather than an
                    // admission failure, and it still has to stay visible
                    // instead of silently dropping the participant.
                    // MalformedMetadataRootException derives from
                    // BadImageFormatException, so it is excluded here to reach
                    // its own handler and keep its exact root reason.
                    catch (Exception invalidImage) when (
                        invalidImage is not MalformedMetadataRootException
                        && invalidImage is BadImageFormatException
                            or OverflowException)
                    {
                        BadImageFormatException cause =
                            invalidImage as BadImageFormatException
                            ?? new BadImageFormatException(
                                "The selected image metadata is invalid.",
                                invalidImage);
                        firstInvalidImage ??=
                            ExceptionDispatchInfo.Capture(cause);
                        invalidImageCauses[path] = cause;
                        rejections.Add(
                            new TypeDependencyRejection(
                                path,
                                TypeDependencyRejectionKind.InvalidImage));
                    }
                }
                // A rejected candidate scopes to itself: record it exactly and
                // keep scanning the remaining assemblies.
                catch (UnsupportedMetadataFormatException)
                {
                    rejections.Add(
                        new TypeDependencyRejection(
                            path,
                            TypeDependencyRejectionKind
                                .UnsupportedMetadataFormat));
                }
                catch (MalformedMetadataRootException ex)
                {
                    rejections.Add(
                        new TypeDependencyRejection(
                            path,
                            TypeDependencyRejectionKind.MalformedMetadataRoot)
                        {
                            MetadataRootReason = ex.Reason,
                        });
                }
                // Skip assemblies that can't be read
                catch (Exception ex) when (
                    ex is not UnsupportedMetadataFormatException
                        and not MalformedMetadataRootException)
                {
                }
            }

            // Scoping only applies when the scan had a surviving participant.
            // If every candidate was rejected there is nothing to scope the
            // rejection against, so it stays the caller's exact outcome.
            if (!admittedAny && rejections.Count > 0)
            {
                // One rejection is the caller's exact outcome, so it keeps its
                // typed mechanism. Several are independent outcomes with no
                // single exact answer: throwing one would silently discard the
                // rest, which is the evidence loss this contract exists to
                // prevent, so every mechanism travels in an aggregate.
                if (rejections.Count > 1)
                {
                    // The typed records keep the path-to-mechanism
                    // correspondence as data; the message repeats it only so a
                    // rendered error is readable. Callers read Rejections.
                    Exception[] mechanisms =
                    [
                        .. rejections.Select(rejection =>
                            ToRejectionException(
                                rejection,
                                invalidImageCauses)),
                    ];
                    string rendered = string.Join(
                        "; ",
                        rejections.Zip(
                            mechanisms,
                            (rejection, mechanism) =>
                                $"'{rejection.AssemblyPath}': "
                                + mechanism.Message));
                    throw new AllCandidatesRejectedException(
                        "Every candidate assembly was rejected before the "
                            + $"dependency scan could run ({rendered})",
                        [.. rejections],
                        mechanisms);
                }

                TypeDependencyRejection soleRejection = rejections[0];

                // The captured invalid-image exception carries the decoder's
                // exact detail, which a reconstructed one would lose.
                if (soleRejection.Kind
                    == TypeDependencyRejectionKind.InvalidImage)
                {
                    firstInvalidImage?.Throw();
                }

                throw ToRejectionException(
                    soleRejection,
                    invalidImageCauses);
            }

            DependencyGraphBuild graph =
                BuildGraph(
                    targetType,
                    typeIndex,
                    requireExactMatch: false,
                    maximumDepth);
            return graph.Dependency with
            {
                Rejections = rejections,
            };
        }
        finally
        {
            foreach (CandidateImage image in images)
                image.Dispose();
        }
    }

    /// <summary>
    /// Builds dependency graph facts over acquisition-issued descriptors while
    /// retaining one ordered, registration-keyed outcome per candidate.
    /// </summary>
    public static TypeDependencyPopulationResult BuildDependencyPopulation(
        string targetType,
        IReadOnlyList<ResolvedAssemblyReference> assemblies,
        int? maximumDepth = null) =>
        BuildDependencyPopulationCore(
            targetType,
            assemblies,
            requireExactMatch: false,
            maximumDepth);

    /// <summary>
    /// Builds dependency graph facts only when the target resolves by its exact
    /// normalized type name.
    /// </summary>
    public static TypeDependencyPopulationResult
        BuildExactDependencyPopulation(
            string targetType,
            IReadOnlyList<ResolvedAssemblyReference> assemblies,
            int? maximumDepth = null) =>
        BuildDependencyPopulationCore(
            targetType,
            assemblies,
            requireExactMatch: true,
            maximumDepth);

    private static TypeDependencyPopulationResult
        BuildDependencyPopulationCore(
            string targetType,
            IReadOnlyList<ResolvedAssemblyReference> assemblies,
            bool requireExactMatch,
            int? maximumDepth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        ArgumentNullException.ThrowIfNull(assemblies);
        ValidateMaximumDepth(maximumDepth);

        var typeIndex = CreateTypeIndex();
        var images = new List<CandidateImage>();
        var outcomes =
            ImmutableArray.CreateBuilder<TypeDependencyCandidateOutcome>(
                assemblies.Count);
        var registrations =
            new HashSet<AssemblyAcquisitionRegistration>(
                ReferenceEqualityComparer.Instance);

        try
        {
            foreach (ResolvedAssemblyReference assembly in assemblies)
            {
                if (assembly is null)
                {
                    throw new ArgumentException(
                        "A dependency-scan candidate cannot be null.",
                        nameof(assemblies));
                }
                if (!registrations.Add(assembly.Registration))
                {
                    throw new ArgumentException(
                        "An acquisition registration may appear only once in a dependency-scan population.",
                        nameof(assemblies));
                }

                CandidateImage? image = null;
                bool outcomeEstablished = false;
                try
                {
                    image = CandidateImage.Open(assembly.OpenRead);
                    CandidateStage stage =
                        StageCandidate(image.PeReader, assembly);
                    if (!stage.HasManagedMetadata)
                    {
                        outcomes.Add(
                            Rejected(
                                assembly,
                                CandidateOpenFailureKind.InvalidImage,
                                "The selected image has no managed metadata."));
                        outcomeEstablished = true;
                        continue;
                    }

                    images.Add(image);
                    image = null;
                    PublishStage(typeIndex, stage);
                    outcomes.Add(
                        new TypeDependencyCandidateOutcome.Completed(
                            assembly.Registration));
                }
                catch (UnsupportedMetadataFormatException)
                {
                    outcomes.Add(
                        Rejected(
                            assembly,
                            CandidateOpenFailureKind
                                .UnsupportedMetadataFormat,
                            "The selected image uses an unsupported metadata format."));
                    outcomeEstablished = true;
                }
                catch (MalformedMetadataRootException ex)
                {
                    outcomes.Add(
                        Rejected(
                            assembly,
                            CandidateOpenFailureKind.InvalidImage,
                            $"The selected image has a malformed metadata root ({ex.Reason}).",
                            ex.Reason));
                    outcomeEstablished = true;
                }
                catch (Exception ex) when (
                    ex is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
                        or ObjectDisposedException)
                {
                    outcomes.Add(
                        Rejected(
                            assembly,
                            CandidateOpenFailureKind.Unreadable,
                            "The selected image could not be read."));
                    outcomeEstablished = true;
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException
                        or ArgumentOutOfRangeException
                        or OverflowException)
                {
                    outcomes.Add(
                        Rejected(
                            assembly,
                            CandidateOpenFailureKind.InvalidImage,
                            "The selected image contains invalid metadata."));
                    outcomeEstablished = true;
                }
                catch (Exception ex)
                {
                    OwnedResourceCleanup.DisposeAfterFailure(
                        image,
                        ex);
                    image = null;
                    throw;
                }
                finally
                {
                    if (outcomeEstablished)
                    {
                        OwnedResourceCleanup
                            .DisposeWithoutReplacingOutcome(
                                image);
                    }
                    else
                    {
                        image?.Dispose();
                    }
                }
            }

            ImmutableArray<TypeDependencyCandidateOutcome> candidates =
                outcomes.MoveToImmutable();
            DependencyGraphBuild graph =
                BuildGraph(
                    targetType,
                    typeIndex,
                    requireExactMatch,
                    maximumDepth);
            TypeDependencyPopulationResult result = new(
                graph.Dependency,
                candidates,
                graph.MatchedRegistration);
            DisposeAll(images);
            return result;
        }
        catch (Exception ex)
        {
            foreach (CandidateImage image in images)
            {
                OwnedResourceCleanup.DisposeAfterFailure(
                    image,
                    ex);
            }
            images.Clear();
            throw;
        }
        finally
        {
            foreach (CandidateImage image in images)
            {
                OwnedResourceCleanup
                    .DisposeWithoutReplacingOutcome(image);
            }
        }
    }

    private static Dictionary<string, IndexedType> CreateTypeIndex() =>
        new(StringComparer.Ordinal);

    private static CandidateStage StageCandidate(
        PEReader peReader,
        ResolvedAssemblyReference? descriptor)
    {
        if (!MetadataFormatAdmission.AdmitImage(peReader))
            return CandidateStage.Descriptorless;

        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(peReader);
        if (descriptor is not null)
        {
            descriptor.ValidateArtifactContent(peReader);
            if (!reader.IsAssembly)
            {
                throw new BadImageFormatException(
                    "The opened image is a module, not an assembly.");
            }

            AssemblyReferenceIdentity actual =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    reader);
            if (!AssemblyImageSnapshot.IdentityMatches(
                    descriptor.Identity,
                    actual))
            {
                throw new BadImageFormatException(
                    "The opened image identity does not match the acquisition descriptor.");
            }
        }

        // Stage this participant's rows separately. A rejection must exclude
        // the whole participant, including rows decoded before a later
        // relationship failure.
        var staged =
            new Dictionary<string, IndexedType>(
                StringComparer.Ordinal);
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition definition =
                reader.GetTypeDefinition(handle);
            if (!definition.IsPublic)
                continue;

            string name = reader.GetString(definition.Name);
            if (TypeFilters.IsCompilerGenerated(name))
                continue;

            string ns = reader.GetString(definition.Namespace);
            string fullName = TypeResolver.GetFullName(ns, name);
            ValidateRelationships(reader, definition);
            staged.TryAdd(
                fullName,
                new IndexedType(
                    reader,
                    definition,
                    descriptor?.Registration));
        }

        return new CandidateStage(staged);
    }

    private static void PublishStage(
        Dictionary<string, IndexedType> typeIndex,
        CandidateStage stage)
    {
        foreach ((string name, IndexedType type) in stage.Types)
            typeIndex.TryAdd(name, type);
    }

    private static DependencyGraphBuild BuildGraph(
        string targetType,
        Dictionary<string, IndexedType> typeIndex,
        bool requireExactMatch,
        int? maximumDepth)
    {
        string normalizedTarget =
            FqnParser.NormalizeTypeName(targetType);
        // User lookup stays fuzzy, but exact casing selects the exact CLR
        // identity when metadata contains case-distinct type names.
        string? matchKey = typeIndex.ContainsKey(normalizedTarget)
            ? normalizedTarget
            : requireExactMatch
                ? null
                : typeIndex.Keys.FirstOrDefault(key =>
                    TypeMatcher.Matches(key, normalizedTarget));
        if (matchKey is null)
            return new(
                new TypeDependencyResult(null, []),
                MatchedRegistration: null);

        IndexedType match = typeIndex[matchKey];
        var treeExpansionBudgets =
            new Dictionary<string, int>(StringComparer.Ordinal);
        var relationshipExpansionBudgets =
            new Dictionary<string, int>(StringComparer.Ordinal);
        var emittedRelationships =
            new HashSet<(
                string Source,
                string Target,
                TypeDependencyRelationshipKind Kind)>();
        var activeDefinitions =
            new HashSet<string>(StringComparer.Ordinal)
            {
                matchKey,
            };
        var relationships = new List<TypeDependencyRelationship>();
        var depthBoundaries =
            new Dictionary<string, TypeDependencyDepthBoundary>(
                StringComparer.Ordinal);
        string matchedType = TypeResolver.FormatDisplayName(matchKey);
        List<TypeDependencyNode> tree = BuildNode(
            matchedType,
            match.Reader,
            match.Definition,
            GenericContext.ForType(match.Reader, match.Definition),
            typeIndex,
            treeExpansionBudgets,
            relationshipExpansionBudgets,
            emittedRelationships,
            activeDefinitions,
            relationships,
            depthBoundaries,
            includeTree: true,
            collectRelationships: true,
            currentDepth: 0,
            maximumDepth);
        return new(
            new TypeDependencyResult(matchedType, tree)
            {
                Relationships = relationships,
                DepthBoundaries =
                [
                    .. depthBoundaries.Values.OrderBy(
                        static boundary => boundary.TypeName,
                        StringComparer.Ordinal),
                ],
            },
            match.Registration);
    }

    private static void ValidateMaximumDepth(int? maximumDepth)
    {
        if (maximumDepth is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDepth),
                maximumDepth,
                "A maximum dependency depth cannot be negative.");
        }
    }

    private static TypeDependencyCandidateOutcome.Rejected Rejected(
        ResolvedAssemblyReference assembly,
        CandidateOpenFailureKind kind,
        string detail,
        MetadataRootMalformedReason? metadataRootReason = null) =>
        new(
            assembly.Registration,
            new CandidateOpenFailure(kind, detail)
            {
                MetadataRootReason = metadataRootReason,
            });

    private static void DisposeAll(
        List<CandidateImage> images)
    {
        List<Exception>? failures = null;
        foreach (CandidateImage image in images)
        {
            try
            {
                image.Dispose();
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }
        images.Clear();

        if (failures is null)
            return;
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        throw new AggregateException(
            "One or more dependency-scan images could not be released.",
            failures);
    }

    /// <summary>
    /// Touches the base-type and interface tokens a dependency tree reads, so
    /// a malformed relationship surfaces while the owning participant is still
    /// in scope. Resolution results are discarded; only reachability matters
    /// here, and <see cref="BuildNode"/> re-reads them for the small subset it
    /// actually visits.
    /// </summary>
    /// <remarks>
    /// Resolution is strict. The nullable <c>GetTypeName</c> overload collapses
    /// a signature rejection to <see langword="null"/>, and <see cref="BuildNode"/>
    /// silently drops a null dependency — so a malformed <c>TypeSpecification</c>
    /// base or interface would publish an edge-less type carrying no rejection,
    /// which is the certified-absence shape this scanner exists to prevent.
    /// </remarks>
    private static void ValidateRelationships(
        MetadataReader reader,
        TypeDefinition typeDef)
    {
        var context = GenericContext.ForType(reader, typeDef);

        ThrowIfRejected(
            TypeResolver.ResolveTypeName(reader, typeDef.BaseType, context));

        foreach (var ifaceHandle in typeDef.GetInterfaceImplementations())
        {
            var iface = reader.GetInterfaceImplementation(ifaceHandle);
            ThrowIfRejected(
                TypeResolver.ResolveTypeName(reader, iface.Interface, context));
        }
    }

    /// <summary>
    /// Raises a rejected type-name resolution as the invalid-image outcome the
    /// participant scope already handles, preserving the mechanism and detail.
    /// </summary>
    private static void ThrowIfRejected(MetadataTypeNameResult result)
    {
        if (result is not MetadataTypeNameResult.Rejected rejected)
            return;

        throw new BadImageFormatException(
            $"Metadata relationship traversal rejected ({rejected.Failure.Kind}): "
            + rejected.Failure.Detail);
    }

    /// <summary>
    /// Builds the child nodes for a type. Computes the "minimal" direct
    /// dependencies by removing interfaces that are transitively inherited
    /// through other direct interfaces. De-duplicates across the tree.
    /// </summary>
    private static List<TypeDependencyNode> BuildNode(
        string sourceTypeName,
        MetadataReader reader,
        TypeDefinition typeDef,
        GenericContext context,
        Dictionary<string, IndexedType> typeIndex,
        Dictionary<string, int> treeExpansionBudgets,
        Dictionary<string, int> relationshipExpansionBudgets,
        HashSet<(
            string Source,
            string Target,
            TypeDependencyRelationshipKind Kind)> emittedRelationships,
        HashSet<string> activeDefinitions,
        List<TypeDependencyRelationship> relationships,
        Dictionary<string, TypeDependencyDepthBoundary> depthBoundaries,
        bool includeTree,
        bool collectRelationships,
        int currentDepth,
        int? maximumDepth)
    {
        // Gather all declared dependencies (base type + interfaces)
        var allDeps =
            new List<(string Name, TypeDependencyRelationshipKind Kind)>();

        if (!typeDef.BaseType.IsNil)
        {
            var baseTypeName = TypeResolver.GetTypeName(reader, typeDef.BaseType, context);
            if (baseTypeName != null && !IsSystemRoot(baseTypeName))
            {
                allDeps.Add(
                    (baseTypeName,
                        TypeDependencyRelationshipKind.BaseType));
            }
        }

        foreach (var ifaceHandle in typeDef.GetInterfaceImplementations())
        {
            var iface = reader.GetInterfaceImplementation(ifaceHandle);
            var ifaceName = TypeResolver.GetTypeName(reader, iface.Interface, context);
            if (ifaceName != null)
            {
                allDeps.Add(
                    (ifaceName,
                        TypeDependencyRelationshipKind.Interface));
            }
        }

        if (allDeps.Count == 0)
            return [];

        // Compute transitive closure for each dep to find which are redundant
        var transitivelyReachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dep in allDeps)
        {
            CollectTransitive(
                dep.Name,
                typeIndex,
                transitivelyReachable,
                new HashSet<string>(StringComparer.Ordinal));
        }

        // A dep is "direct" if it's not transitively reachable through another dep
        var directDeps = allDeps
            .Where(d => !transitivelyReachable.Contains(
                ExpansionKey(d.Name)))
            .ToList();

        string sourceIdentity = ExpansionKey(sourceTypeName);
        if (maximumDepth is { } bounded && currentDepth >= bounded)
        {
            if (directDeps.Count > 0)
            {
                depthBoundaries.TryAdd(
                    sourceIdentity,
                    new TypeDependencyDepthBoundary(
                        sourceTypeName,
                        bounded));
            }
            return [];
        }

        // A shorter path can reach a node after an earlier depth-boundary
        // encounter. Once its outgoing relationships are admitted, it is no
        // longer a bounded semantic endpoint.
        depthBoundaries.Remove(sourceIdentity);

        // Build tree nodes for direct deps only
        var results = new List<TypeDependencyNode>();
        foreach (var dep in directDeps)
        {
            if (collectRelationships
                && emittedRelationships.Add(
                    (
                        ExpansionKey(sourceTypeName),
                        ExpansionKey(dep.Name),
                        dep.Kind)))
            {
                relationships.Add(
                    new TypeDependencyRelationship(
                        sourceTypeName,
                        dep.Name,
                        dep.Kind,
                        relationships.Count));
            }

            int childDepth = currentDepth + 1;
            bool expandTree = includeTree
                && ClaimExpansionBudget(
                    treeExpansionBudgets,
                    dep.Name,
                    childDepth,
                    maximumDepth);
            bool expandRelationships = collectRelationships
                && ClaimExpansionBudget(
                    relationshipExpansionBudgets,
                    dep.Name,
                    childDepth,
                    maximumDepth);
            List<TypeDependencyNode> children =
                expandTree || expandRelationships
                    ? ResolveChildren(
                        dep.Name,
                        typeIndex,
                        treeExpansionBudgets,
                        relationshipExpansionBudgets,
                        emittedRelationships,
                        activeDefinitions,
                        relationships,
                        depthBoundaries,
                        expandTree,
                        expandRelationships,
                        childDepth,
                        maximumDepth)
                    : [];

            if (!includeTree)
                continue;

            if (!expandTree)
            {
                // Already shown at a shallower level — include as leaf
                results.Add(new TypeDependencyNode(dep.Name, []));
                continue;
            }

            results.Add(new TypeDependencyNode(dep.Name, children));
        }

        return results;
    }

    /// <summary>
    /// Collects all interfaces transitively reachable from a type's dependencies
    /// (not including the type itself).
    /// </summary>
    private static void CollectTransitive(
        string typeName,
        Dictionary<string, IndexedType> typeIndex,
        HashSet<string> result,
        HashSet<string> visited)
    {
        string expansionKey = ExpansionKey(typeName);
        if (!visited.Add(expansionKey))
            return;

        var normalized = FqnParser.NormalizeTypeName(typeName);
        if (!typeIndex.TryGetValue(normalized, out var match))
            return;

        MetadataReader mdReader = match.Reader;
        TypeDefinition typeDef = match.Definition;
        GenericContext context =
            ContextForConstructedType(mdReader, typeDef, typeName);

        // Base type
        if (!typeDef.BaseType.IsNil)
        {
            var baseTypeName = TypeResolver.GetTypeName(mdReader, typeDef.BaseType, context);
            if (baseTypeName != null && !IsSystemRoot(baseTypeName))
            {
                result.Add(ExpansionKey(baseTypeName));
                CollectTransitive(baseTypeName, typeIndex, result, visited);
            }
        }

        // Interfaces
        foreach (var ifaceHandle in typeDef.GetInterfaceImplementations())
        {
            var iface = mdReader.GetInterfaceImplementation(ifaceHandle);
            var ifaceName = TypeResolver.GetTypeName(mdReader, iface.Interface, context);
            if (ifaceName != null)
            {
                result.Add(ExpansionKey(ifaceName));
                CollectTransitive(ifaceName, typeIndex, result, visited);
            }
        }
    }

    private static GenericContext ContextForConstructedType(
        MetadataReader reader,
        TypeDefinition typeDef,
        string typeName)
    {
        IReadOnlyList<string> arguments = GenericArguments(typeName);
        return arguments.Count == typeDef.GetGenericParameters().Count
            ? new GenericContext(arguments, [])
            : GenericContext.ForType(reader, typeDef);
    }

    private static IReadOnlyList<string> GenericArguments(string typeName)
    {
        var arguments = new List<string>();
        int angleDepth = 0;
        int squareDepth = 0;
        int parenthesisDepth = 0;
        int argumentStart = -1;
        for (int i = 0; i < typeName.Length; i++)
        {
            switch (typeName[i])
            {
                case '<':
                    angleDepth++;
                    if (angleDepth == 1)
                        argumentStart = i + 1;
                    break;
                case '>':
                    if (angleDepth == 1 && argumentStart >= 0)
                    {
                        AddArgument(i);
                        argumentStart = -1;
                    }
                    angleDepth--;
                    break;
                case ',' when angleDepth == 1
                    && squareDepth == 0
                    && parenthesisDepth == 0:
                    AddArgument(i);
                    argumentStart = i + 1;
                    break;
                case '[':
                    squareDepth++;
                    break;
                case ']':
                    squareDepth--;
                    break;
                case '(':
                    parenthesisDepth++;
                    break;
                case ')':
                    parenthesisDepth--;
                    break;
            }
        }
        return arguments;

        void AddArgument(int end)
        {
            string argument = typeName[argumentStart..end].Trim();
            if (argument.Length > 0)
                arguments.Add(argument);
        }
    }

    private static string ExpansionKey(string typeName) =>
        typeName.Trim();

    private static bool ClaimExpansionBudget(
        Dictionary<string, int> expansionBudgets,
        string typeName,
        int currentDepth,
        int? maximumDepth)
    {
        int remainingBudget = maximumDepth is { } bounded
            ? bounded - currentDepth
            : int.MaxValue;
        string identity = ExpansionKey(typeName);

        if (expansionBudgets.TryGetValue(identity, out int previousBudget)
            && previousBudget >= remainingBudget)
        {
            return false;
        }

        expansionBudgets[identity] = remainingBudget;
        return true;
    }

    private static List<TypeDependencyNode> ResolveChildren(
        string typeName,
        Dictionary<string, IndexedType> typeIndex,
        Dictionary<string, int> treeExpansionBudgets,
        Dictionary<string, int> relationshipExpansionBudgets,
        HashSet<(
            string Source,
            string Target,
            TypeDependencyRelationshipKind Kind)> emittedRelationships,
        HashSet<string> activeDefinitions,
        List<TypeDependencyRelationship> relationships,
        Dictionary<string, TypeDependencyDepthBoundary> depthBoundaries,
        bool includeTree,
        bool collectRelationships,
        int currentDepth,
        int? maximumDepth)
    {
        var normalizedName = FqnParser.NormalizeTypeName(typeName);

        if (!typeIndex.TryGetValue(normalizedName, out var match))
            return [];
        if (!activeDefinitions.Add(normalizedName))
            return [];

        try
        {
            return BuildNode(
                typeName,
                match.Reader,
                match.Definition,
                ContextForConstructedType(
                    match.Reader,
                    match.Definition,
                    typeName),
                typeIndex,
                treeExpansionBudgets,
                relationshipExpansionBudgets,
                emittedRelationships,
                activeDefinitions,
                relationships,
                depthBoundaries,
                includeTree,
                collectRelationships,
                currentDepth,
                maximumDepth);
        }
        finally
        {
            activeDefinitions.Remove(normalizedName);
        }
    }

    private static Exception ToRejectionException(
        TypeDependencyRejection rejection,
        IReadOnlyDictionary<string, BadImageFormatException> invalidImageCauses)
        => rejection.Kind switch
        {
            TypeDependencyRejectionKind.UnsupportedMetadataFormat =>
                new UnsupportedMetadataFormatException(),
            TypeDependencyRejectionKind.MalformedMetadataRoot
                when rejection.MetadataRootReason is { } reason =>
                new MalformedMetadataRootException(reason),
            TypeDependencyRejectionKind.InvalidImage =>
                invalidImageCauses.TryGetValue(
                    rejection.AssemblyPath,
                    out BadImageFormatException? cause)
                    ? cause
                    : new BadImageFormatException(
                        $"'{rejection.AssemblyPath}' has invalid metadata."),
            _ => new InvalidOperationException(
                "Unknown metadata-format rejection."),
        };

    private static bool IsSystemRoot(string typeName)
    {
        return typeName is "System.Object" or "System.ValueType" or "System.Enum"
            or "System.Delegate" or "System.MulticastDelegate";
    }

    private sealed record IndexedType(
        MetadataReader Reader,
        TypeDefinition Definition,
        AssemblyAcquisitionRegistration? Registration);

    private sealed record DependencyGraphBuild(
        TypeDependencyResult Dependency,
        AssemblyAcquisitionRegistration? MatchedRegistration);

    private sealed class CandidateStage
    {
        private CandidateStage(
            bool hasManagedMetadata,
            IReadOnlyDictionary<string, IndexedType> types)
        {
            HasManagedMetadata = hasManagedMetadata;
            Types = types;
        }

        internal static CandidateStage Descriptorless { get; } =
            new(
                hasManagedMetadata: false,
                new Dictionary<string, IndexedType>());

        internal CandidateStage(
            IReadOnlyDictionary<string, IndexedType> types)
            : this(hasManagedMetadata: true, types)
        {
        }

        internal bool HasManagedMetadata { get; }
        internal IReadOnlyDictionary<string, IndexedType> Types { get; }
    }

    private sealed class CandidateImage : IDisposable
    {
        private readonly Stream stream;

        private CandidateImage(
            Stream stream,
            PEReader peReader)
        {
            this.stream = stream;
            PeReader = peReader;
        }

        internal PEReader PeReader { get; }

        internal static CandidateImage Open(
            Func<Stream> openRead)
        {
            Stream? stream = null;
            try
            {
                stream = openRead();
                if (stream is null || !stream.CanRead)
                {
                    throw new IOException(
                        "The assembly opener did not return a readable stream.");
                }

                var peReader = new PEReader(
                    stream,
                    PEStreamOptions.LeaveOpen);
                return new CandidateImage(stream, peReader);
            }
            catch (Exception ex)
            {
                OwnedResourceCleanup.DisposeAfterFailure(
                    stream,
                    ex);
                throw;
            }
        }

        public void Dispose()
        {
            PeReader.Dispose();
            stream.Dispose();
        }
    }
}
