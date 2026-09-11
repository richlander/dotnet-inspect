using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>One normalized dependency subject adopted for PackageHouse processing.</summary>
public abstract record PackageHouseDependencySubject
{
    private PackageHouseDependencySubject()
    {
    }

    public abstract PackageAcquisitionCandidate Candidate { get; }

    public abstract PackageDependencyEvidenceAuthorship Authorship { get; }

    public sealed record Declaration : PackageHouseDependencySubject
    {
        internal Declaration(
            PackageDependencyEvidenceDeclaration evidence,
            PackageDependencyCandidateResult.Resolved resolution)
        {
            Evidence = evidence;
            Resolution = resolution;
        }

        public PackageDependencyEvidenceDeclaration Evidence { get; }

        public PackageDependencyCandidateResult.Resolved Resolution { get; }

        public override PackageAcquisitionCandidate Candidate =>
            Resolution.Candidate;

        public override PackageDependencyEvidenceAuthorship Authorship =>
            Evidence.Authorship;
    }

    public sealed record Relationship : PackageHouseDependencySubject
    {
        internal Relationship(
            PackageDependencyEvidenceRelationship evidence,
            PackageAcquisitionCandidate candidate)
        {
            Evidence = evidence;
            Candidate = candidate;
        }

        public PackageDependencyEvidenceRelationship Evidence { get; }

        public override PackageAcquisitionCandidate Candidate { get; }

        public override PackageDependencyEvidenceAuthorship Authorship =>
            Evidence.Authorship;
    }
}

/// <summary>
/// Resource-free PackageHouse input retaining its exact normalized dependency evidence.
/// </summary>
public sealed record PackageHouseDependencyInput
{
    internal PackageHouseDependencyInput(
        PackageDependencyEvidenceRoot root,
        PackageHouseDependencySubject subject,
        PackageHouseRequest request)
    {
        Root = root;
        Subject = subject;
        Request = request;
    }

    public PackageDependencyEvidenceRoot Root { get; }

    public PackageHouseDependencySubject Subject { get; }

    public PackageDependencyEvidenceProcessingResult Processing =>
        Root.Processing;

    public PackageHouseRequest Request { get; }

    public PackageHouseRequestAssociation Association =>
        Request.Association
        ?? throw new InvalidOperationException(
            "An adopted dependency input requires its PackageHouse association.");
}

/// <summary>
/// Adopts normalized dependency evidence into candidate-bound PackageHouse input.
/// </summary>
public static class PackageHouseDependencyInputAdapter
{
    public static PackageHouseDependencyInput Create(
        PackageDependencyEvidenceRoot root,
        PackageDependencyCandidateResult.Resolved resolution,
        PackageHouseOperation operation,
        PackageHouseTargetContext? targetContext = null,
        PackageHouseAssetSelectionKind? assetSelection = null,
        PackageHouseLibraryHandoffMode libraryHandoff =
            PackageHouseLibraryHandoffMode.PackageOnly)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(resolution.Declaration);
        ArgumentNullException.ThrowIfNull(resolution.Candidate);
        PackageDependencyEvidenceDeclaration declaration =
            RequireDeclaration(root, resolution.Declaration);
        PackageAcquisitionCandidate candidate = resolution.Candidate;
        if (!candidate.Coordinate.PackageId.Equals(
                declaration.CanonicalPackageId,
                StringComparison.Ordinal)
            || !PackageDependencyVersionRange.Satisfies(
                candidate.Coordinate.Version,
                declaration.CanonicalVersionConstraint))
        {
            throw new ArgumentException(
                "The resolved candidate must satisfy the normalized declaration.",
                nameof(resolution));
        }

        return Create(
            root,
            new PackageHouseDependencySubject.Declaration(
                declaration,
                resolution),
            candidate,
            operation,
            targetContext,
            assetSelection,
            libraryHandoff);
    }

    public static PackageHouseDependencyInput Create(
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceRelationship relationship,
        PackageAcquisitionCandidate candidate,
        PackageHouseOperation operation,
        PackageHouseTargetContext? targetContext = null,
        PackageHouseAssetSelectionKind? assetSelection = null,
        PackageHouseLibraryHandoffMode libraryHandoff =
            PackageHouseLibraryHandoffMode.PackageOnly)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentNullException.ThrowIfNull(candidate);
        PackageDependencyEvidenceRelationship retained =
            RequireRelationship(root, relationship);
        if (candidate.Coordinate != retained.ResolvedCoordinate)
        {
            throw new ArgumentException(
                "The candidate coordinate must equal the normalized relationship coordinate.",
                nameof(candidate));
        }

        return Create(
            root,
            new PackageHouseDependencySubject.Relationship(
                retained,
                candidate),
            candidate,
            operation,
            targetContext,
            assetSelection,
            libraryHandoff);
    }

    private static PackageHouseDependencyInput Create(
        PackageDependencyEvidenceRoot root,
        PackageHouseDependencySubject subject,
        PackageAcquisitionCandidate candidate,
        PackageHouseOperation operation,
        PackageHouseTargetContext? targetContext,
        PackageHouseAssetSelectionKind? assetSelection,
        PackageHouseLibraryHandoffMode libraryHandoff)
    {
        ArgumentNullException.ThrowIfNull(operation);
        PackageHouseRequestAssociation association =
            PackageHouseRequestAssociation.Create();
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Candidate(candidate),
            operation,
            targetContext,
            assetSelection,
            libraryHandoff,
            association);
        return new PackageHouseDependencyInput(
            root,
            subject,
            request);
    }

    private static PackageDependencyEvidenceDeclaration RequireDeclaration(
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceDeclaration declaration)
    {
        if (root.Declaration
            is not PackageDependencyEvidenceDeclarationResult.Available
                available)
        {
            throw new ArgumentException(
                "The normalized root does not carry available declarations.",
                nameof(root));
        }

        PackageDependencyEvidenceDeclaration[] matches =
        [
            .. available.Groups
                .SelectMany(group => group.Declarations)
                .Where(candidate =>
                    candidate.Identity == declaration.Identity),
        ];
        if (matches.Length != 1 || matches[0] != declaration)
        {
            throw new ArgumentException(
                "The declaration must be an exact member of the normalized root.",
                nameof(declaration));
        }

        return matches[0];
    }

    private static PackageDependencyEvidenceRelationship RequireRelationship(
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceRelationship relationship)
    {
        if (root.Relationships
            is not PackageDependencyEvidenceRelationshipResult.Available
                available)
        {
            throw new ArgumentException(
                "The normalized root does not carry available relationships.",
                nameof(root));
        }

        PackageDependencyEvidenceRelationship[] matches =
        [
            .. available.Relationships.Where(
                candidate =>
                    candidate.Identity == relationship.Identity),
        ];
        if (matches.Length != 1 || matches[0] != relationship)
        {
            throw new ArgumentException(
                "The relationship must be an exact member of the normalized root.",
                nameof(relationship));
        }

        return matches[0];
    }
}
