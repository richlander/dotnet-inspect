using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using InertText;
using NuGet.Versioning;

namespace DotnetInspector.Queries;

/// <summary>Exact-content provenance over caller-supplied authored project bytes.</summary>
public sealed record AuthoredProjectContentProvenance
{
    public AuthoredProjectContentProvenance(string sha256)
    {
        if (sha256 is not { Length: 64 }
            || !RestoredProjectIdentityText.IsLowerHex(sha256))
        {
            throw new ArgumentException(
                "A content provenance digest must be a lowercase 64-character SHA-256 hex string.",
                nameof(sha256));
        }

        Sha256 = sha256;
    }

    public string Sha256 { get; }

    internal static AuthoredProjectContentProvenance FromBytes(
        ReadOnlyMemory<byte> bytes) =>
        new(Convert.ToHexStringLower(SHA256.HashData(bytes.Span)));
}

/// <summary>Stable semantic identity over canonical authored-project syntax facts.</summary>
public sealed record AuthoredProjectIdentity
{
    public AuthoredProjectIdentity(string factsDigest)
    {
        if (factsDigest is not { Length: 64 }
            || !RestoredProjectIdentityText.IsLowerHex(factsDigest))
        {
            throw new ArgumentException(
                "An authored project identity must be a lowercase 64-character SHA-256 hex string.",
                nameof(factsDigest));
        }

        FactsDigest = factsDigest;
    }

    public string FactsDigest { get; }
}

/// <summary>The interpretation state of one authored target-framework spelling.</summary>
public enum AuthoredProjectTargetFrameworkKind
{
    Exact,
    Unrecognized,
    Unresolved,
}

/// <summary>Stable identity for one literal or unresolved authored target observation.</summary>
public sealed record AuthoredProjectTargetFrameworkIdentity
{
    private AuthoredProjectTargetFrameworkIdentity(
        AuthoredProjectTargetFrameworkKind kind,
        string? canonicalFramework,
        string comparisonIdentity)
    {
        Kind = kind;
        CanonicalFramework = canonicalFramework;
        ComparisonIdentity = comparisonIdentity;
    }

    public AuthoredProjectTargetFrameworkKind Kind { get; }

    public string? CanonicalFramework { get; }

    internal string ComparisonIdentity { get; }

    internal static AuthoredProjectTargetFrameworkIdentity Exact(
        string canonicalFramework) =>
        new(
            AuthoredProjectTargetFrameworkKind.Exact,
            canonicalFramework,
            canonicalFramework);

    internal static AuthoredProjectTargetFrameworkIdentity Unrecognized(
        string source) =>
        new(
            AuthoredProjectTargetFrameworkKind.Unrecognized,
            canonicalFramework: null,
            RestoredProjectIdentityText.Opaque(source));

    internal static AuthoredProjectTargetFrameworkIdentity Unresolved(
        string source) =>
        new(
            AuthoredProjectTargetFrameworkKind.Unresolved,
            canonicalFramework: null,
            RestoredProjectIdentityText.Opaque(source));
}

/// <summary>One target-framework spelling observed directly in project syntax.</summary>
public sealed record AuthoredProjectTargetFramework(
    AuthoredProjectTargetFrameworkIdentity Identity,
    InertString SourceSpelling,
    string SyntaxContextIdentity);

/// <summary>Condition association established for one authored package declaration.</summary>
public abstract record AuthoredProjectDependencyCondition
{
    private AuthoredProjectDependencyCondition()
    {
    }

    public sealed record Unconditional : AuthoredProjectDependencyCondition;

    public sealed record TargetFramework(
        AuthoredProjectTargetFrameworkIdentity Framework,
        InertString SourceSpelling) : AuthoredProjectDependencyCondition;

    public sealed record Unresolved(
        string OpaqueIdentity,
        ImmutableArray<InertString> SourceSpellings) :
        AuthoredProjectDependencyCondition;
}

/// <summary>Stable identity for one canonical authored declaration.</summary>
public sealed record AuthoredProjectPackageDeclarationIdentity
{
    public AuthoredProjectPackageDeclarationIdentity(string factsDigest)
    {
        if (factsDigest is not { Length: 64 }
            || !RestoredProjectIdentityText.IsLowerHex(factsDigest))
        {
            throw new ArgumentException(
                "An authored declaration identity must be a lowercase 64-character SHA-256 hex string.",
                nameof(factsDigest));
        }

        FactsDigest = factsDigest;
    }

    public string FactsDigest { get; }
}

/// <summary>
/// One canonical authored package declaration, including unresolved identity or
/// version syntax that remains useful as incomplete evidence.
/// </summary>
public sealed record AuthoredProjectPackageDeclaration(
    AuthoredProjectPackageDeclarationIdentity Identity,
    string? CanonicalPackageId,
    string? CanonicalVersionConstraint,
    InertString? SourcePackageIdSpelling,
    InertString? SourceVersionConstraintSpelling,
    AuthoredProjectDependencyCondition Condition,
    int SourceOccurrenceCount)
{
    public int SourceOccurrenceCount { get; } = SourceOccurrenceCount >= 1
        ? SourceOccurrenceCount
        : throw new ArgumentOutOfRangeException(
            nameof(SourceOccurrenceCount),
            SourceOccurrenceCount,
            "A source occurrence count must be at least one.");
}

/// <summary>A typed reason an authored syntax projection is incomplete.</summary>
public enum AuthoredProjectDependencyLimitationReason
{
    ExplicitImport,
    PropertyIndirection,
    ItemOrMetadataExpression,
    CentralPackageManagement,
    UnsupportedTargetDeclaration,
    ConflictingTargetDeclarations,
    UnsupportedCondition,
    UnsupportedPackageReferenceShape,
    PackageItemOperation,
    MissingVersionConstraint,
    InvalidPackageId,
    InvalidVersionConstraint,
    ConflictingVersionForms,
    ConflictingPackageDeclaration,
}

/// <summary>One canonical limitation reason and its exact source occurrence count.</summary>
public sealed record AuthoredProjectDependencyLimitation(
    AuthoredProjectDependencyLimitationReason Reason,
    int Count)
{
    public int Count { get; } = Count >= 1
        ? Count
        : throw new ArgumentOutOfRangeException(
            nameof(Count),
            Count,
            "A limitation count must be at least one.");
}

/// <summary>Unsupported dependency syntax retained without inventing a declaration.</summary>
public enum AuthoredProjectUnresolvedDependencySyntaxKind
{
    PackageReferenceWithoutInclude,
}

/// <summary>Opaque identity for one unsupported dependency-syntax occurrence.</summary>
public sealed record AuthoredProjectUnresolvedDependencySyntax(
    AuthoredProjectUnresolvedDependencySyntaxKind Kind,
    string OpaqueIdentity);

/// <summary>Immutable facts projected from one exact authored project document.</summary>
public sealed record AuthoredProjectDependencyFacts(
    AuthoredProjectIdentity Identity,
    AuthoredProjectContentProvenance ContentProvenance,
    ImmutableArray<AuthoredProjectTargetFramework> TargetFrameworks,
    ImmutableArray<AuthoredProjectPackageDeclaration> PackageDeclarations,
    ImmutableArray<AuthoredProjectUnresolvedDependencySyntax>
        UnresolvedDependencySyntax);

/// <summary>The stable reason one authored project document could not be projected.</summary>
public enum AuthoredProjectDependencyFactsFailureReason
{
    MalformedXml,
    UnsupportedDocumentShape,
    ConfiguredLimitExceeded,
}

/// <summary>A content-free authored-project projection failure.</summary>
public sealed record AuthoredProjectDependencyFactsFailure
{
    public AuthoredProjectDependencyFactsFailure(
        AuthoredProjectDependencyFactsFailureReason reason,
        int lineNumber = 0,
        int linePosition = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lineNumber);
        ArgumentOutOfRangeException.ThrowIfNegative(linePosition);
        Reason = reason;
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    public AuthoredProjectDependencyFactsFailureReason Reason { get; }

    public int LineNumber { get; }

    public int LinePosition { get; }

    public string Message => Reason switch
    {
        AuthoredProjectDependencyFactsFailureReason.MalformedXml
            when LineNumber > 0 && LinePosition > 0 =>
            $"The authored project is not well-formed XML at line {LineNumber}, position {LinePosition}.",
        AuthoredProjectDependencyFactsFailureReason.MalformedXml =>
            "The authored project is not well-formed XML.",
        AuthoredProjectDependencyFactsFailureReason.UnsupportedDocumentShape =>
            "The authored project has an unsupported document shape or namespace.",
        AuthoredProjectDependencyFactsFailureReason.ConfiguredLimitExceeded =>
            "The authored project exceeds a configured resource limit.",
        _ => "The authored project could not be projected.",
    };
}

/// <summary>The typed outcome of projecting facts from one exact authored project.</summary>
public abstract record AuthoredProjectDependencyFactsResult
{
    private AuthoredProjectDependencyFactsResult()
    {
    }

    public sealed record Available(
        AuthoredProjectDependencyFacts Value) :
        AuthoredProjectDependencyFactsResult;

    public sealed record Incomplete(
        AuthoredProjectDependencyFacts Value,
        ImmutableArray<AuthoredProjectDependencyLimitation> Limitations) :
        AuthoredProjectDependencyFactsResult;

    public sealed record Failed(
        AuthoredProjectDependencyFactsFailure Failure) :
        AuthoredProjectDependencyFactsResult;
}

/// <summary>
/// Projects bounded authored package-dependency facts from exact project XML
/// bytes without evaluating MSBuild.
/// </summary>
public static class AuthoredProjectDependencyFactsQuery
{
    public const int MaxProjectBytes = 1024 * 1024;
    public const int MaxProjectCharacters = 512 * 1024;
    public const int MaxScalarCharacters = 32 * 1024;
    public const int MaxTargetFrameworkOccurrences = 256;
    public const int MaxPackageReferenceOccurrences = 4096;
    public const int MaxLimitationOccurrences = 4096;
    public const int MaxXmlElementDepth = 256;

    private const string LegacyMsbuildNamespace =
        "http://schemas.microsoft.com/developer/msbuild/2003";
    private const string TargetFrameworkProperty = "$(TargetFramework)";

    public static InspectionQuery<AuthoredProjectDependencyFactsResult>
        Definition { get; } =
        new("Authored project dependency facts", InspectionCost.NetworkFree);

    public static AuthoredProjectDependencyFactsResult Execute(
        ReadOnlyMemory<byte> projectBytes)
    {
        if (projectBytes.Length > MaxProjectBytes)
        {
            return Failed(
                AuthoredProjectDependencyFactsFailureReason
                    .ConfiguredLimitExceeded);
        }

        try
        {
            AuthoredProjectContentProvenance contentProvenance =
                AuthoredProjectContentProvenance.FromBytes(projectBytes);
            using var stream = new MemoryStream(
                projectBytes.ToArray(),
                writable: false);
            XDocument document = HardenedXml.LoadXDocument(
                stream,
                MaxProjectCharacters);
            XElement root = ValidateRoot(document);
            ValidateElementDepth(root);
            XNamespace projectNamespace = root.Name.Namespace;
            var limitations = new LimitationAccumulator();
            ObserveImportsAndCentralManagement(
                root,
                projectNamespace,
                limitations);
            ImmutableArray<AuthoredProjectTargetFramework> targets =
                ProjectTargetFrameworks(
                    root,
                    projectNamespace,
                    limitations);
            PackageProjection packageProjection =
                ProjectPackageDeclarations(
                    root,
                    projectNamespace,
                    limitations);
            ImmutableArray<AuthoredProjectDependencyLimitation>
                projectedLimitations = limitations.ToImmutable();
            var identity = new AuthoredProjectIdentity(
                ComputeFactsDigest(
                    targets,
                    packageProjection.Declarations,
                    packageProjection.UnresolvedSyntax,
                    projectedLimitations));
            var facts = new AuthoredProjectDependencyFacts(
                identity,
                contentProvenance,
                targets,
                packageProjection.Declarations,
                packageProjection.UnresolvedSyntax);
            return projectedLimitations.IsEmpty
                ? new AuthoredProjectDependencyFactsResult.Available(facts)
                : new AuthoredProjectDependencyFactsResult.Incomplete(
                    facts,
                    projectedLimitations);
        }
        catch (ProjectionException exception)
        {
            return Failed(exception.Reason);
        }
        catch (XmlException exception)
        {
            return ClassifyXmlFailure(projectBytes, exception);
        }
    }

    private static AuthoredProjectDependencyFactsResult.Failed
        ClassifyXmlFailure(
            ReadOnlyMemory<byte> projectBytes,
            XmlException original)
    {
        try
        {
            using var stream = new MemoryStream(
                projectBytes.ToArray(),
                writable: false);
            _ = HardenedXml.LoadXDocument(
                stream,
                MaxProjectBytes + 1L);
            return Failed(
                AuthoredProjectDependencyFactsFailureReason
                    .ConfiguredLimitExceeded);
        }
        catch (XmlException)
        {
            return Failed(
                AuthoredProjectDependencyFactsFailureReason.MalformedXml,
                original.LineNumber,
                original.LinePosition);
        }
    }

    private static XElement ValidateRoot(XDocument document)
    {
        XElement? root = document.Root;
        if (root is null
            || !root.Name.LocalName.Equals(
                "Project",
                StringComparison.OrdinalIgnoreCase)
            || (root.Name.NamespaceName.Length != 0
                && !root.Name.NamespaceName.Equals(
                    LegacyMsbuildNamespace,
                    StringComparison.Ordinal)))
        {
            throw Failure(
                AuthoredProjectDependencyFactsFailureReason
                    .UnsupportedDocumentShape);
        }

        return root;
    }

    private static void ValidateElementDepth(XElement root)
    {
        var pending = new Stack<(XElement Element, int Depth)>();
        pending.Push((root, 1));
        while (pending.TryPop(out (XElement Element, int Depth) current))
        {
            if (current.Depth > MaxXmlElementDepth)
            {
                throw Failure(
                    AuthoredProjectDependencyFactsFailureReason
                        .ConfiguredLimitExceeded);
            }

            foreach (XElement child in current.Element.Elements())
                pending.Push((child, current.Depth + 1));
        }
    }

    private static void ObserveImportsAndCentralManagement(
        XElement root,
        XNamespace projectNamespace,
        LimitationAccumulator limitations)
    {
        foreach (XElement element in root.Descendants())
        {
            if (NameEquals(element, projectNamespace, "Import")
                || NameEquals(element, projectNamespace, "Sdk"))
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason.ExplicitImport);
            }

            if (NameEquals(element, projectNamespace, "PackageVersion"))
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .CentralPackageManagement);
            }

            if (NameEquals(
                element,
                projectNamespace,
                "GlobalPackageReference"))
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .CentralPackageManagement);
            }

            if (!NameEquals(
                element,
                projectNamespace,
                "ManagePackageVersionsCentrally"))
                continue;

            limitations.Add(
                AuthoredProjectDependencyLimitationReason
                    .CentralPackageManagement);
            if (element.HasElements)
                continue;

            string value = Scalar(element.Value);
            ObserveExpressionLimitations(value, limitations);
        }
    }

    private static ImmutableArray<AuthoredProjectTargetFramework>
        ProjectTargetFrameworks(
            XElement root,
            XNamespace projectNamespace,
            LimitationAccumulator limitations)
    {
        var observations =
            new Dictionary<string, AuthoredProjectTargetFramework>(
                StringComparer.Ordinal);
        var occurrenceSets = new List<string>();
        bool sawTargetFramework = false;
        bool sawTargetFrameworks = false;
        int occurrenceCount = 0;

        foreach (XElement property in root.Descendants())
        {
            bool isSingle = NameEquals(
                property,
                projectNamespace,
                "TargetFramework");
            bool isMultiple = NameEquals(
                property,
                projectNamespace,
                "TargetFrameworks");
            if (!isSingle && !isMultiple)
                continue;

            sawTargetFramework |= isSingle;
            sawTargetFrameworks |= isMultiple;
            XElement? propertyGroup = property.Parent;
            bool supportedPlacement = propertyGroup is not null
                && NameEquals(
                    propertyGroup,
                    projectNamespace,
                    "PropertyGroup")
                && propertyGroup.Parent == root;
            XElement[] contextElements =
            [
                .. property.AncestorsAndSelf()
                    .TakeWhile(element => element != root),
            ];
            ConditionCollection conditions =
                CollectConditions(contextElements);
            string syntaxContextIdentity = supportedPlacement
                && conditions.Occurrences.IsEmpty
                && conditions.AmbiguousElements.IsEmpty
                    ? "unconditional"
                    : OpaqueDigest(
                        "apdf-target-context/1",
                        [
                            supportedPlacement
                                ? "direct-property-group"
                                : "unsupported-placement",
                            conditions.AmbiguousElements.IsEmpty
                                ? "unambiguous"
                                : "ambiguous-attributes",
                            .. conditions.Occurrences
                                .Select(condition => condition.Value)
                                .Order(StringComparer.Ordinal),
                        ]);
            if (!supportedPlacement
                || !conditions.Occurrences.IsEmpty
                || !conditions.AmbiguousElements.IsEmpty)
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .UnsupportedTargetDeclaration);
                foreach (ConditionOccurrence condition
                    in conditions.Occurrences)
                {
                    ObserveExpressionLimitations(
                        condition.Value,
                        limitations,
                        condition.Attribute);
                }
            }

            if (property.HasElements)
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .UnsupportedTargetDeclaration);
                occurrenceSets.Add("");
                continue;
            }

            string rawValue = Scalar(property.Value);
            if (ContainsMsbuildExpression(rawValue))
            {
                occurrenceCount++;
                if (occurrenceCount > MaxTargetFrameworkOccurrences)
                {
                    throw Failure(
                        AuthoredProjectDependencyFactsFailureReason
                            .ConfiguredLimitExceeded);
                }

                string unresolvedSource = rawValue.Trim();
                ObserveExpressionLimitations(
                    unresolvedSource,
                    limitations);
                if (unresolvedSource.Length == 0)
                {
                    limitations.Add(
                        AuthoredProjectDependencyLimitationReason
                            .UnsupportedTargetDeclaration);
                    occurrenceSets.Add("");
                    continue;
                }

                AuthoredProjectTargetFrameworkIdentity unresolved =
                    AuthoredProjectTargetFrameworkIdentity.Unresolved(
                        unresolvedSource);
                string observationIdentity = OpaqueDigest(
                    "apdf-target-observation/1",
                    [
                        unresolved.ComparisonIdentity,
                        syntaxContextIdentity,
                    ]);
                observations.TryAdd(
                    observationIdentity,
                    new AuthoredProjectTargetFramework(
                        unresolved,
                        Inert(unresolvedSource),
                        syntaxContextIdentity));
                occurrenceSets.Add(
                    OpaqueDigest(
                        "apdf-target-set/1",
                        [unresolved.ComparisonIdentity]));
                continue;
            }

            string[] parts = isMultiple
                ? rawValue.Split(';')
                : [rawValue];
            var occurrenceIdentities = new List<string>(parts.Length);
            foreach (string rawPart in parts)
            {
                occurrenceCount++;
                if (occurrenceCount > MaxTargetFrameworkOccurrences)
                {
                    throw Failure(
                        AuthoredProjectDependencyFactsFailureReason
                            .ConfiguredLimitExceeded);
                }

                string part = rawPart.Trim();
                if (part.Length == 0)
                {
                    limitations.Add(
                        AuthoredProjectDependencyLimitationReason
                            .UnsupportedTargetDeclaration);
                    continue;
                }

                AuthoredProjectTargetFrameworkIdentity identity;
                if (NuGetTargetFrameworkIdentity.TryNormalize(
                    part,
                    out string canonical))
                {
                    identity =
                        AuthoredProjectTargetFrameworkIdentity.Exact(
                            canonical);
                }
                else
                {
                    identity =
                        AuthoredProjectTargetFrameworkIdentity.Unrecognized(
                            part);
                }

                occurrenceIdentities.Add(identity.ComparisonIdentity);
                string observationIdentity = OpaqueDigest(
                    "apdf-target-observation/1",
                    [
                        identity.ComparisonIdentity,
                        syntaxContextIdentity,
                    ]);
                observations.TryAdd(
                    observationIdentity,
                    new AuthoredProjectTargetFramework(
                        identity,
                        Inert(part),
                        syntaxContextIdentity));
            }

            occurrenceSets.Add(
                OpaqueDigest(
                    "apdf-target-set/1",
                    [
                        .. occurrenceIdentities
                            .Distinct(StringComparer.Ordinal)
                            .Order(StringComparer.Ordinal),
                    ]));
        }

        if ((sawTargetFramework && sawTargetFrameworks)
            || occurrenceSets.Distinct(StringComparer.Ordinal).Skip(1).Any())
        {
            limitations.Add(
                AuthoredProjectDependencyLimitationReason
                    .ConflictingTargetDeclarations);
        }

        return
        [
            .. observations.Values.OrderBy(
                target => target.Identity.ComparisonIdentity,
                StringComparer.Ordinal)
                .ThenBy(
                    target => target.SyntaxContextIdentity,
                    StringComparer.Ordinal),
        ];
    }

    private static PackageProjection
        ProjectPackageDeclarations(
            XElement root,
            XNamespace projectNamespace,
            LimitationAccumulator limitations)
    {
        var candidates = new List<DeclarationCandidate>();
        var unresolved =
            ImmutableArray.CreateBuilder<
                AuthoredProjectUnresolvedDependencySyntax>();
        int occurrenceCount = 0;

        foreach (XElement item in root.Descendants()
            .Where(element =>
                NameEquals(element, projectNamespace, "PackageReference")))
        {
            occurrenceCount++;
            if (occurrenceCount > MaxPackageReferenceOccurrences)
            {
                throw Failure(
                    AuthoredProjectDependencyFactsFailureReason
                        .ConfiguredLimitExceeded);
            }

            XElement? itemGroup = item.Parent;
            bool supportedShape = itemGroup is not null
                && NameEquals(itemGroup, projectNamespace, "ItemGroup")
                && itemGroup.Parent == root;
            var syntaxFields = new List<string>();
            if (!supportedShape)
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .UnsupportedPackageReferenceShape);
                syntaxFields.Add(
                    UnsupportedAncestryIdentity(item, root));
            }

            string[] includes = AttributeValues(item, "Include");
            if (includes.Length == 0)
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .UnsupportedPackageReferenceShape);
                syntaxFields.Add("missing-include");
            }
            if (includes.Length > 1)
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .UnsupportedPackageReferenceShape);
                syntaxFields.Add(
                    OpaqueDigest(
                        "apdf-include-alternatives/1",
                        [
                            .. includes.Order(StringComparer.Ordinal),
                        ]));
            }

            foreach (string operation in new[]
            {
                "Update",
                "Remove",
                "Exclude",
            })
            {
                string[] operationValues =
                    AttributeValues(item, operation);
                foreach (string operationValue in operationValues)
                {
                    limitations.Add(
                        AuthoredProjectDependencyLimitationReason
                            .PackageItemOperation);
                    syntaxFields.Add(
                        OpaqueDigest(
                            "apdf-item-operation/1",
                            [
                                operation.ToLowerInvariant(),
                                operationValue,
                            ]));
                    ObserveExpressionLimitations(
                        operationValue,
                        limitations);
                }
            }

            string? sourcePackageId = includes.Length == 1
                ? includes[0]
                : null;
            string? canonicalId = null;
            foreach (string include in includes)
            {
                if (ContainsMsbuildExpression(include))
                {
                    ObserveExpressionLimitations(include, limitations);
                }
                else if (includes.Length == 1
                    && PackageCoordinateResolver.IsCanonicalPackageId(include))
                {
                    canonicalId = include.ToLowerInvariant();
                }
                else if (!PackageCoordinateResolver.IsCanonicalPackageId(
                    include))
                {
                    limitations.Add(
                        AuthoredProjectDependencyLimitationReason
                            .InvalidPackageId);
                }
            }
            VersionProjection version = ProjectVersion(
                item,
                projectNamespace,
                limitations);
            XElement[] conditionElements =
            [
                .. item.AncestorsAndSelf()
                    .TakeWhile(element => element != root),
            ];
            ConditionCollection conditions =
                CollectConditions(conditionElements);
            string? unresolvedConditionContext = (
                supportedShape,
                conditions.AmbiguousElements.IsEmpty) switch
            {
                (true, true) => null,
                (false, true) => "unsupported-ancestry",
                (true, false) => "ambiguous-condition-attributes",
                (false, false) =>
                    "unsupported-ancestry-and-ambiguous-condition-attributes",
            };
            AuthoredProjectDependencyCondition condition = ProjectCondition(
                conditions,
                unresolvedConditionContext,
                limitations);
            string syntaxIdentity = syntaxFields.Count == 0
                ? "supported"
                : OpaqueDigest(
                    "apdf-declaration-syntax/1",
                    [.. syntaxFields]);
            if (includes.Length == 0)
            {
                unresolved.Add(
                    new AuthoredProjectUnresolvedDependencySyntax(
                        AuthoredProjectUnresolvedDependencySyntaxKind
                            .PackageReferenceWithoutInclude,
                        OpaqueDigest(
                            "apdf-unresolved-package-syntax/1",
                            [
                                version.Identity,
                                ConditionIdentity(condition),
                                syntaxIdentity,
                            ])));
                continue;
            }

            string packageIdentity = canonicalId
                ?? OpaqueDigest(
                    "apdf-package-identity/1",
                    [.. includes.Order(StringComparer.Ordinal)]);
            candidates.Add(
                new DeclarationCandidate(
                    packageIdentity,
                    canonicalId,
                    version.CanonicalVersionConstraint,
                    sourcePackageId,
                    version.SourceVersionConstraint,
                    version.Identity,
                    condition,
                    syntaxIdentity));
        }

        ObserveConflictingDeclarations(candidates, limitations);
        return new PackageProjection(
            AggregateDeclarations(candidates),
            unresolved
                .OrderBy(
                    syntax => syntax.OpaqueIdentity,
                    StringComparer.Ordinal)
                .ToImmutableArray());
    }

    private static VersionProjection ProjectVersion(
        XElement item,
        XNamespace projectNamespace,
        LimitationAccumulator limitations)
    {
        var versions = new List<VersionForm>();
        foreach (string attributeVersion in
            AttributeValues(item, "Version")
                .Order(StringComparer.Ordinal))
        {
            versions.Add(
                new VersionForm(
                    "version",
                    attributeVersion,
                    Condition: null,
                    ShapeIdentity: null));
        }

        foreach (XElement element in item.Elements()
            .Where(element =>
                NameEquals(element, projectNamespace, "Version")))
        {
            ConditionCollection conditions = CollectConditions([element]);
            foreach (ConditionOccurrence condition in conditions.Occurrences)
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .UnsupportedCondition,
                    condition.Attribute);
                ObserveExpressionLimitations(
                    condition.Value,
                    limitations,
                    condition.Attribute);
            }
            foreach (XElement ambiguous in conditions.AmbiguousElements)
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .UnsupportedCondition,
                    ambiguous);
            }
            string? conditionIdentity = conditions.Occurrences.IsEmpty
                && conditions.AmbiguousElements.IsEmpty
                    ? null
                    : OpaqueDigest(
                        "apdf-version-condition/1",
                        [
                            conditions.AmbiguousElements.IsEmpty
                                ? "single"
                                : "ambiguous",
                            .. conditions.Occurrences
                                .Select(condition => condition.Value)
                                .Order(StringComparer.Ordinal),
                        ]);

            if (element.HasElements)
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .UnsupportedPackageReferenceShape);
                versions.Add(
                    new VersionForm(
                        "version",
                        Value: null,
                        conditionIdentity,
                        CanonicalElementShapeIdentity(
                            "apdf-version-shape/1",
                            element)));
            }
            else
            {
                versions.Add(
                    new VersionForm(
                        "version",
                        Scalar(element.Value).Trim(),
                        conditionIdentity,
                        ShapeIdentity: null));
            }
        }

        var overrides = new List<VersionForm>();
        foreach (string overrideAttribute in
            AttributeValues(item, "VersionOverride")
                .Order(StringComparer.Ordinal))
        {
            ObserveExpressionLimitations(
                overrideAttribute,
                limitations);

            overrides.Add(
                new VersionForm(
                    "override",
                    overrideAttribute,
                    Condition: null,
                    ShapeIdentity: null));
        }

        foreach (XElement element in item.Elements()
            .Where(element =>
                NameEquals(element, projectNamespace, "VersionOverride")))
        {
            ConditionCollection conditions = CollectConditions([element]);
            foreach (ConditionOccurrence condition in conditions.Occurrences)
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .UnsupportedCondition,
                    condition.Attribute);
                ObserveExpressionLimitations(
                    condition.Value,
                    limitations,
                    condition.Attribute);
            }
            foreach (XElement ambiguous in conditions.AmbiguousElements)
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .UnsupportedCondition,
                    ambiguous);
            }
            if (element.HasElements)
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .UnsupportedPackageReferenceShape);
            }

            string? value = element.HasElements
                ? null
                : Scalar(element.Value).Trim();
            if (value is not null)
                ObserveExpressionLimitations(value, limitations);
            overrides.Add(
                new VersionForm(
                    "override",
                    value,
                    conditions.Occurrences.IsEmpty
                        && conditions.AmbiguousElements.IsEmpty
                            ? null
                            : OpaqueDigest(
                                "apdf-version-override-condition/1",
                                [
                                    conditions.AmbiguousElements.IsEmpty
                                        ? "single"
                                        : "ambiguous",
                                    .. conditions.Occurrences
                                        .Select(condition => condition.Value)
                                        .Order(StringComparer.Ordinal),
                                ]),
                    element.HasElements
                        ? CanonicalElementShapeIdentity(
                            "apdf-version-override-shape/1",
                            element)
                        : null));
        }

        if (overrides.Count > 0)
        {
            limitations.Add(
                AuthoredProjectDependencyLimitationReason
                    .CentralPackageManagement);
        }

        if (versions.Count == 0)
        {
            limitations.Add(
                AuthoredProjectDependencyLimitationReason
                    .MissingVersionConstraint);
        }
        else if (versions.Count > 1)
        {
            limitations.Add(
                AuthoredProjectDependencyLimitationReason
                    .ConflictingVersionForms);
        }

        foreach (VersionForm version in versions)
        {
            if (version.Value is not { } value)
                continue;
            if (value.Length == 0)
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .InvalidVersionConstraint);
            }
            else if (ContainsMsbuildExpression(value))
            {
                ObserveExpressionLimitations(value, limitations);
            }
            else if (!VersionRange.TryParse(value, out _))
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .InvalidVersionConstraint);
            }
        }

        string? sourceVersion = versions.Count == 1
            && overrides.Count == 0
            ? versions[0].Value
            : null;
        string? canonicalVersion = null;
        if (versions is
            [
                {
                    Value: { Length: > 0 } projectedValue,
                    Condition: null,
                    ShapeIdentity: null,
                },
            ]
            && overrides.Count == 0
            && !ContainsMsbuildExpression(projectedValue)
            && VersionRange.TryParse(
                projectedValue,
                out VersionRange? range))
        {
            canonicalVersion =
                range.ToNormalizedString().ToLowerInvariant();
        }

        VersionForm[] identityForms =
        [
            .. versions,
            .. overrides,
        ];
        string identity = canonicalVersion
            ?? OpaqueDigest(
                "apdf-version/1",
                identityForms.Length == 0
                    ? ["none"]
                    :
                    [
                        .. identityForms
                            .Select(VersionFormIdentity)
                            .Order(StringComparer.Ordinal),
                    ]);
        return new VersionProjection(
            canonicalVersion,
            sourceVersion,
            identity);
    }

    private static AuthoredProjectDependencyCondition ProjectCondition(
        ConditionCollection sourceConditions,
        string? unresolvedContext,
        LimitationAccumulator limitations)
    {
        string[] conditions =
        [
            .. sourceConditions.Occurrences.Select(
                condition => condition.Value),
        ];
        foreach (XElement ambiguous in sourceConditions.AmbiguousElements)
        {
            limitations.Add(
                AuthoredProjectDependencyLimitationReason
                    .UnsupportedCondition,
                ambiguous);
        }

        if (conditions.Length == 0)
        {
            return unresolvedContext is not null
                ? UnresolvedCondition(
                    conditions,
                    unresolvedContext)
                : new AuthoredProjectDependencyCondition.Unconditional();
        }

        var exact = new List<(
            AuthoredProjectTargetFrameworkIdentity Identity,
            string Source)>();
        bool hasUnsupportedCondition = false;
        foreach (ConditionOccurrence condition in
            sourceConditions.Occurrences)
        {
            if (!TryParseTargetFrameworkCondition(
                condition.Value,
                out AuthoredProjectTargetFrameworkIdentity identity))
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .UnsupportedCondition,
                    condition.Attribute);
                ObserveExpressionLimitations(
                    condition.Value,
                    limitations,
                    condition.Attribute);
                hasUnsupportedCondition = true;
                continue;
            }

            exact.Add((identity, condition.Value));
        }

        if (unresolvedContext is not null)
        {
            return UnresolvedCondition(
                conditions,
                unresolvedContext);
        }

        if (hasUnsupportedCondition)
            return UnresolvedCondition(conditions);

        if (exact.Select(item => item.Identity.ComparisonIdentity)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any())
        {
            limitations.Add(
                AuthoredProjectDependencyLimitationReason.UnsupportedCondition);
            return UnresolvedCondition(conditions);
        }

        (
            AuthoredProjectTargetFrameworkIdentity target,
            string source) = exact
            .OrderBy(item => item.Source, StringComparer.Ordinal)
            .Select(item => (item.Identity, item.Source))
            .First();
        return new AuthoredProjectDependencyCondition.TargetFramework(
            target,
            Inert(source));
    }

    private static AuthoredProjectDependencyCondition.Unresolved
        UnresolvedCondition(
            IEnumerable<string> conditions,
            string context = "unsupported-condition")
    {
        string[] ordered =
        [
            .. conditions
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        string identity = OpaqueDigest(
            "apdf-condition/1",
            [context, .. ordered]);
        return new AuthoredProjectDependencyCondition.Unresolved(
            identity,
            [.. ordered.Select(Inert)]);
    }

    private static bool TryParseTargetFrameworkCondition(
        string condition,
        out AuthoredProjectTargetFrameworkIdentity identity)
    {
        identity = null!;
        int operatorIndex = condition.IndexOf("==", StringComparison.Ordinal);
        if (operatorIndex < 0
            || condition.IndexOf(
                "==",
                operatorIndex + 2,
                StringComparison.Ordinal) >= 0)
        {
            return false;
        }

        string left = condition[..operatorIndex].Trim();
        string right = condition[(operatorIndex + 2)..].Trim();
        if (!TryUnquote(left, out string leftValue)
            || !TryUnquote(right, out string rightValue))
        {
            return false;
        }

        string framework;
        if (leftValue.Equals(
            TargetFrameworkProperty,
            StringComparison.OrdinalIgnoreCase))
        {
            framework = rightValue;
        }
        else if (rightValue.Equals(
            TargetFrameworkProperty,
            StringComparison.OrdinalIgnoreCase))
        {
            framework = leftValue;
        }
        else
        {
            return false;
        }

        if (ContainsMsbuildExpression(framework))
            return false;

        identity = NuGetTargetFrameworkIdentity.TryNormalize(
            framework,
            out string canonical)
            ? AuthoredProjectTargetFrameworkIdentity.Exact(canonical)
            : AuthoredProjectTargetFrameworkIdentity.Unrecognized(framework);
        return true;
    }

    private static bool TryUnquote(string value, out string unquoted)
    {
        unquoted = "";
        if (value.Length < 2
            || value[0] is not ('\'' or '"')
            || value[^1] != value[0])
        {
            return false;
        }

        unquoted = value[1..^1];
        return unquoted.Length > 0
            && !unquoted.Contains(value[0], StringComparison.Ordinal);
    }

    private static void ObserveConflictingDeclarations(
        IEnumerable<DeclarationCandidate> candidates,
        LimitationAccumulator limitations)
    {
        foreach (IGrouping<string, DeclarationCandidate> group in candidates
            .Where(candidate => candidate.CanonicalPackageId is not null)
            .GroupBy(
                candidate => OpaqueDigest(
                    "apdf-conflict-group/1",
                    [
                        candidate.CanonicalPackageId!,
                        ConditionIdentity(candidate.Condition),
                    ]),
                StringComparer.Ordinal))
        {
            string[] versions =
            [
                .. group.Select(candidate => candidate.VersionIdentity)
                    .Distinct(StringComparer.Ordinal),
            ];
            if (versions.Length > 1)
            {
                limitations.Add(
                    AuthoredProjectDependencyLimitationReason
                        .ConflictingPackageDeclaration);
            }
        }
    }

    private static ImmutableArray<AuthoredProjectPackageDeclaration>
        AggregateDeclarations(IEnumerable<DeclarationCandidate> candidates)
    {
        return
        [
            .. candidates
                .GroupBy(
                    candidate => new DeclarationKey(
                            candidate.PackageIdentity,
                            candidate.VersionIdentity,
                        ConditionIdentity(candidate.Condition),
                        candidate.SyntaxIdentity))
                .OrderBy(group => group.Key.PackageIdentity, StringComparer.Ordinal)
                .ThenBy(group => group.Key.VersionIdentity, StringComparer.Ordinal)
                .ThenBy(group => group.Key.ConditionIdentity, StringComparer.Ordinal)
                .ThenBy(group => group.Key.SyntaxIdentity, StringComparer.Ordinal)
                .Select(group =>
                {
                    DeclarationCandidate selected = group
                        .OrderBy(
                            candidate => candidate.SourcePackageId ?? "",
                            StringComparer.Ordinal)
                        .ThenBy(
                            candidate => candidate.SourceVersionConstraint,
                            StringComparer.Ordinal)
                        .First();
                    string digest = Digest(
                        "apdf-declaration/1",
                        group.Key.PackageIdentity,
                        group.Key.VersionIdentity,
                        group.Key.ConditionIdentity,
                        group.Key.SyntaxIdentity);
                    return new AuthoredProjectPackageDeclaration(
                        new AuthoredProjectPackageDeclarationIdentity(digest),
                        selected.CanonicalPackageId,
                        selected.CanonicalVersionConstraint,
                        selected.SourcePackageId is null
                            ? null
                            : Inert(selected.SourcePackageId),
                        selected.SourceVersionConstraint is null
                            ? null
                            : Inert(selected.SourceVersionConstraint),
                        selected.Condition,
                        group.Count());
                }),
        ];
    }

    private static string ComputeFactsDigest(
        ImmutableArray<AuthoredProjectTargetFramework> targets,
        ImmutableArray<AuthoredProjectPackageDeclaration> declarations,
        ImmutableArray<AuthoredProjectUnresolvedDependencySyntax>
            unresolvedSyntax,
        ImmutableArray<AuthoredProjectDependencyLimitation> limitations)
    {
        var text = new StringBuilder();
        Field(text, "apdf/1");
        Count(text, targets.Length);
        foreach (AuthoredProjectTargetFramework target in targets)
        {
            Count(text, (int)target.Identity.Kind);
            Field(text, target.Identity.ComparisonIdentity);
            Field(text, target.SyntaxContextIdentity);
        }

        Count(text, declarations.Length);
        foreach (AuthoredProjectPackageDeclaration declaration in declarations)
        {
            Field(text, declaration.Identity.FactsDigest);
            Count(text, declaration.SourceOccurrenceCount);
        }

        Count(text, unresolvedSyntax.Length);
        foreach (AuthoredProjectUnresolvedDependencySyntax syntax
            in unresolvedSyntax)
        {
            Count(text, (int)syntax.Kind);
            Field(text, syntax.OpaqueIdentity);
        }

        Count(text, limitations.Length);
        foreach (AuthoredProjectDependencyLimitation limitation in limitations)
        {
            Count(text, (int)limitation.Reason);
            Count(text, limitation.Count);
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static string ConditionIdentity(
        AuthoredProjectDependencyCondition condition) =>
        condition switch
        {
            AuthoredProjectDependencyCondition.Unconditional =>
                "unconditional",
            AuthoredProjectDependencyCondition.TargetFramework target =>
                $"target:{target.Framework.ComparisonIdentity}",
            AuthoredProjectDependencyCondition.Unresolved unresolved =>
                $"unresolved:{unresolved.OpaqueIdentity}",
            _ => throw new InvalidOperationException(
                "The authored project condition kind is not supported."),
        };

    private static string Digest(string version, params string[] values)
    {
        var text = new StringBuilder();
        Field(text, version);
        Count(text, values.Length);
        foreach (string value in values)
            Field(text, value);
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static string OpaqueDigest(
        string version,
        params string[] values) =>
        RestoredProjectIdentityText.OpaquePrefix + Digest(version, values);

    private static string UnsupportedAncestryIdentity(
        XElement item,
        XElement root)
    {
        string[] ancestry =
        [
            .. item.Ancestors()
                .TakeWhile(element => element != root)
                .Reverse()
                .Select(element =>
                    OpaqueDigest(
                        "apdf-ancestor/1",
                        [
                            element.Name.NamespaceName,
                            element.Name.LocalName,
                        ])),
        ];
        return OpaqueDigest(
            "apdf-unsupported-ancestry/1",
            ancestry.Length == 0 ? ["direct"] : ancestry);
    }

    private static string VersionFormIdentity(VersionForm form) =>
        OpaqueDigest(
            "apdf-version-form/1",
            [
                form.Kind,
                form.Value ?? "",
                form.Condition ?? "",
                form.ShapeIdentity ?? "",
            ]);

    private static void Field(StringBuilder text, string value) =>
        text.Append(value.Length).Append(':').Append(value).Append(';');

    private static void Count(StringBuilder text, int value) =>
        text.Append('#').Append(value).Append(';');

    private static bool NameEquals(
        XElement element,
        XNamespace projectNamespace,
        string localName) =>
        element.Name.Namespace == projectNamespace
        && element.Name.LocalName.Equals(
            localName,
            StringComparison.OrdinalIgnoreCase);

    private static string[] AttributeValues(
        XElement element,
        string localName) =>
    [
        .. MatchingAttributes(element, localName)
            .Select(attribute => Scalar(attribute.Value).Trim())
            .Order(StringComparer.Ordinal),
    ];

    private static bool ContainsPropertyExpansion(string? value) =>
        value?.Contains("$(", StringComparison.Ordinal) == true;

    private static bool ContainsItemOrMetadataExpression(string? value) =>
        value?.Contains("@(", StringComparison.Ordinal) == true
        || value?.Contains("%(", StringComparison.Ordinal) == true;

    private static bool ContainsMsbuildExpression(string? value) =>
        ContainsPropertyExpansion(value)
        || ContainsItemOrMetadataExpression(value);

    private static void ObserveExpressionLimitations(
        string value,
        LimitationAccumulator limitations,
        object? source = null)
    {
        if (ContainsPropertyExpansion(value))
        {
            AddLimitation(
                limitations,
                AuthoredProjectDependencyLimitationReason
                    .PropertyIndirection,
                source);
        }

        if (ContainsItemOrMetadataExpression(value))
        {
            AddLimitation(
                limitations,
                AuthoredProjectDependencyLimitationReason
                    .ItemOrMetadataExpression,
                source);
        }
    }

    private static void AddLimitation(
        LimitationAccumulator limitations,
        AuthoredProjectDependencyLimitationReason reason,
        object? source)
    {
        if (source is null)
            limitations.Add(reason);
        else
            limitations.Add(reason, source);
    }

    private static ConditionCollection CollectConditions(
        IEnumerable<XElement> elements)
    {
        var conditions =
            ImmutableArray.CreateBuilder<ConditionOccurrence>();
        var ambiguous =
            ImmutableArray.CreateBuilder<XElement>();
        foreach (XElement element in elements)
        {
            XAttribute[] attributes =
            [
                .. MatchingAttributes(element, "Condition"),
            ];
            if (attributes.Length > 1)
                ambiguous.Add(element);
            foreach (XAttribute attribute in attributes)
            {
                string value = Scalar(attribute.Value).Trim();
                if (value.Length > 0)
                {
                    conditions.Add(
                        new ConditionOccurrence(attribute, value));
                }
            }
        }

        return new ConditionCollection(
            conditions.ToImmutable(),
            ambiguous.ToImmutable());
    }

    private static string CanonicalElementShapeIdentity(
        string version,
        XElement element)
    {
        var text = new StringBuilder();
        Field(text, version);
        AppendCanonicalElement(text, element);
        return RestoredProjectIdentityText.OpaquePrefix
            + Convert.ToHexStringLower(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void AppendCanonicalElement(
        StringBuilder text,
        XElement element)
    {
        Field(text, "element");
        Field(text, element.Name.NamespaceName);
        Field(text, element.Name.LocalName);
        XAttribute[] attributes =
        [
            .. element.Attributes()
                .OrderBy(
                    attribute => attribute.Name.NamespaceName,
                    StringComparer.Ordinal)
                .ThenBy(
                    attribute => attribute.Name.LocalName,
                    StringComparer.Ordinal)
                .ThenBy(
                    attribute => attribute.Value,
                    StringComparer.Ordinal),
        ];
        Count(text, attributes.Length);
        foreach (XAttribute attribute in attributes)
        {
            Field(text, attribute.Name.NamespaceName);
            Field(text, attribute.Name.LocalName);
            Field(text, attribute.Value);
        }

        var nodes = new List<string>();
        var pendingText = new StringBuilder();
        foreach (XNode node in element.Nodes())
        {
            if (node is XComment)
                continue;
            if (node is XText value)
            {
                pendingText.Append(value.Value);
                continue;
            }

            FlushCanonicalText(nodes, pendingText);
            switch (node)
            {
                case XElement child:
                    var childText = new StringBuilder();
                    AppendCanonicalElement(childText, child);
                    nodes.Add(childText.ToString());
                    break;
                case XProcessingInstruction instruction:
                    var instructionText = new StringBuilder();
                    Field(instructionText, "processing-instruction");
                    Field(instructionText, instruction.Target);
                    Field(instructionText, instruction.Data);
                    nodes.Add(instructionText.ToString());
                    break;
                default:
                    var nodeText = new StringBuilder();
                    Field(nodeText, node.NodeType.ToString());
                    Field(
                        nodeText,
                        node.ToString(SaveOptions.DisableFormatting));
                    nodes.Add(nodeText.ToString());
                    break;
            }
        }

        FlushCanonicalText(nodes, pendingText);
        Count(text, nodes.Count);
        foreach (string node in nodes)
            text.Append(node);
    }

    private static void FlushCanonicalText(
        List<string> nodes,
        StringBuilder pendingText)
    {
        string value = pendingText.ToString().Trim();
        pendingText.Clear();
        if (value.Length == 0)
            return;

        var text = new StringBuilder();
        Field(text, "text");
        Field(text, value);
        nodes.Add(text.ToString());
    }

    private static IEnumerable<XAttribute> MatchingAttributes(
        XElement element,
        string localName) =>
        element.Attributes()
            .Where(attribute =>
                attribute.Name.NamespaceName.Length == 0
                && attribute.Name.LocalName.Equals(
                    localName,
                    StringComparison.OrdinalIgnoreCase));

    private static string Scalar(string value)
    {
        if (value.Length > MaxScalarCharacters)
        {
            throw Failure(
                AuthoredProjectDependencyFactsFailureReason
                    .ConfiguredLimitExceeded);
        }

        return value;
    }

    private static InertString Inert(string value) =>
        new(TextPolicy.Field, Scalar(value), MaxScalarCharacters);

    private static ProjectionException Failure(
        AuthoredProjectDependencyFactsFailureReason reason) =>
        new(reason);

    private static AuthoredProjectDependencyFactsResult.Failed Failed(
        AuthoredProjectDependencyFactsFailureReason reason,
        int lineNumber = 0,
        int linePosition = 0) =>
        new(
            new AuthoredProjectDependencyFactsFailure(
                reason,
                lineNumber,
                linePosition));

    private sealed class LimitationAccumulator
    {
        private readonly Dictionary<
            AuthoredProjectDependencyLimitationReason,
            int> _counts = [];
        private readonly Dictionary<
            AuthoredProjectDependencyLimitationReason,
            HashSet<object>> _sources = [];
        private int _total;

        public void Add(AuthoredProjectDependencyLimitationReason reason)
        {
            _total++;
            if (_total > MaxLimitationOccurrences)
            {
                throw Failure(
                    AuthoredProjectDependencyFactsFailureReason
                        .ConfiguredLimitExceeded);
            }

            _counts[reason] = _counts.GetValueOrDefault(reason) + 1;
        }

        public void Add(
            AuthoredProjectDependencyLimitationReason reason,
            object source)
        {
            if (!_sources.TryGetValue(reason, out HashSet<object>? sources))
            {
                sources = new HashSet<object>(
                    ReferenceEqualityComparer.Instance);
                _sources.Add(reason, sources);
            }

            if (sources.Add(source))
                Add(reason);
        }

        public ImmutableArray<AuthoredProjectDependencyLimitation> ToImmutable() =>
        [
            .. _counts
                .OrderBy(pair => pair.Key)
                .Select(pair =>
                    new AuthoredProjectDependencyLimitation(
                        pair.Key,
                        pair.Value)),
        ];
    }

    private sealed record DeclarationCandidate(
        string PackageIdentity,
        string? CanonicalPackageId,
        string? CanonicalVersionConstraint,
        string? SourcePackageId,
        string? SourceVersionConstraint,
        string VersionIdentity,
        AuthoredProjectDependencyCondition Condition,
        string SyntaxIdentity);

    private readonly record struct DeclarationKey(
        string PackageIdentity,
        string VersionIdentity,
        string ConditionIdentity,
        string SyntaxIdentity);

    private sealed record VersionProjection(
        string? CanonicalVersionConstraint,
        string? SourceVersionConstraint,
        string Identity);

    private sealed record PackageProjection(
        ImmutableArray<AuthoredProjectPackageDeclaration> Declarations,
        ImmutableArray<AuthoredProjectUnresolvedDependencySyntax>
            UnresolvedSyntax);

    private sealed record VersionForm(
        string Kind,
        string? Value,
        string? Condition,
        string? ShapeIdentity);

    private sealed record ConditionOccurrence(
        XAttribute Attribute,
        string Value);

    private sealed record ConditionCollection(
        ImmutableArray<ConditionOccurrence> Occurrences,
        ImmutableArray<XElement> AmbiguousElements);

    private sealed class ProjectionException(
        AuthoredProjectDependencyFactsFailureReason reason) : Exception
    {
        public AuthoredProjectDependencyFactsFailureReason Reason { get; } =
            reason;
    }
}
