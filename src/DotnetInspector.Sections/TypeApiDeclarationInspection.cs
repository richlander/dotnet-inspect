using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Queries;
using ILInspector.CSharp;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

[JsonConverter(typeof(JsonStringEnumConverter<TypeApiDeclarationScope>))]
public enum TypeApiDeclarationScope
{
    ApiVisible,
    All,
}

[JsonConverter(typeof(JsonStringEnumConverter<TypeApiDeclarationOutcome>))]
public enum TypeApiDeclarationOutcome
{
    Available,
    NotFound,
    Unavailable,
}

[JsonConverter(typeof(JsonStringEnumConverter<TypeApiDeclarationFailureKind>))]
public enum TypeApiDeclarationFailureKind
{
    ProjectionTruncated,
    ParticipantRejected,
    ParticipantFailed,
    InspectionIncomplete,
    AccessorMetadataUnavailable,
    PrinterNotRendered,
}

public sealed record TypeApiDeclarationFailure(
    TypeApiDeclarationFailureKind Kind,
    string Detail,
    string? Operation = null,
    int? SubjectToken = null);

public sealed record TypeApiDeclarationResult
{
    public TypeApiDeclarationResult(
        TypeApiDeclarationOutcome outcome,
        ExactTypeDefinitionIdentity typeIdentity,
        TypeApiDeclarationScope scope,
        string? text,
        ImmutableArray<TypeApiDeclarationFailure> failures)
    {
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));
        ArgumentNullException.ThrowIfNull(typeIdentity);
        if (failures.IsDefault)
        {
            throw new ArgumentException(
                "Type API declaration failures must be initialized.",
                nameof(failures));
        }
        if (outcome == TypeApiDeclarationOutcome.Available
            && (text is null || failures.Length > 0))
        {
            throw new ArgumentException(
                "An available Type API declaration requires text and no failures.");
        }
        if (outcome != TypeApiDeclarationOutcome.Available && text is not null)
        {
            throw new ArgumentException(
                "A non-available Type API declaration cannot carry text.");
        }
        if (outcome == TypeApiDeclarationOutcome.Unavailable
            && failures.Length == 0)
        {
            throw new ArgumentException(
                "An unavailable Type API declaration requires failure evidence.");
        }
        if (outcome == TypeApiDeclarationOutcome.NotFound
            && failures.Length > 0)
        {
            throw new ArgumentException(
                "A conclusive missing Type cannot carry unavailable failure evidence.");
        }

        Outcome = outcome;
        TypeIdentity = typeIdentity;
        Scope = scope;
        Text = text;
        Failures = failures;
    }

    public TypeApiDeclarationOutcome Outcome { get; }

    public ExactTypeDefinitionIdentity TypeIdentity { get; }

    public TypeApiDeclarationScope Scope { get; }

    public string? Text { get; }

    public ImmutableArray<TypeApiDeclarationFailure> Failures { get; }
}

public static class TypeApiDeclarationInspection
{
    public static InspectionEnvelope<TypeApiDeclarationResult> Execute(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        MetadataTypeDefinitionName type,
        TypeApiDeclarationScope scope,
        ApiSurfaceProjectionLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(limits);
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        cancellationToken.ThrowIfCancellationRequested();
        AssemblyContextApiSurfaceResult projection =
            AssemblyContextApiSurfaceQuery.ExecuteBoundedResolved(
                group,
                ApiSurfaceScope.IncludeAll,
                limits,
                [participant]);
        cancellationToken.ThrowIfCancellationRequested();

        ExactTypeDefinitionIdentity identity =
            ExactTypeDefinitionIdentity.From(type);
        if (projection.Truncation is { } truncation)
        {
            return Unavailable(
                identity,
                scope,
                new TypeApiDeclarationFailure(
                    TypeApiDeclarationFailureKind.ProjectionTruncated,
                    $"API extraction exceeded the {truncation.Limit} bound "
                        + $"of {truncation.Bound}."));
        }

        AssemblyContextEntry<AssemblyApiSurface> entry =
            projection.Assemblies.Assemblies.Single();
        if (entry
            is AssemblyContextEntry<AssemblyApiSurface>.Rejected rejected)
        {
            return Unavailable(
                identity,
                scope,
                new TypeApiDeclarationFailure(
                    TypeApiDeclarationFailureKind.ParticipantRejected,
                    rejected.Failure.Detail,
                    rejected.Failure.Kind.ToString()));
        }
        if (entry
            is AssemblyContextEntry<AssemblyApiSurface>.Failed failed)
        {
            return Unavailable(
                identity,
                scope,
                new TypeApiDeclarationFailure(
                    TypeApiDeclarationFailureKind.ParticipantFailed,
                    failed.Error.Message,
                    failed.Error.GetType().FullName));
        }

        ApiSurface surface =
            ((AssemblyContextEntry<AssemblyApiSurface>.Available)entry)
            .Value.Surface;
        ApiType? root = surface.Types.SingleOrDefault(
            candidate => candidate.DefinitionName == type);
        if (root is null)
        {
            ImmutableArray<TypeApiDeclarationFailure>
                incompleteIdentityFailures =
                ProjectInspectionFailures(
                    surface.InspectionFailures.Where(failure =>
                        FailureCouldHideRequestedType(
                            failure,
                            type)));
            if (incompleteIdentityFailures.Length > 0)
            {
                return Unavailable(
                    identity,
                    scope,
                    incompleteIdentityFailures);
            }

            return Envelope(
                new TypeApiDeclarationResult(
                    TypeApiDeclarationOutcome.NotFound,
                    identity,
                    scope,
                    text: null,
                    []));
        }

        DeclarationSelection selection =
            SelectDeclaration(surface, root, type, scope);
        if (selection.Failures.Length > 0)
        {
            return Unavailable(
                identity,
                scope,
                selection.Failures);
        }

        HashSet<int> selectedTokens = SelectedTokens(selection.Request);
        surface.ReprojectConstraintResolutionFailures(
            subject => selectedTokens.Contains(subject.SubjectToken));
        ImmutableArray<TypeApiDeclarationFailure> inspectionFailures =
            ProjectInspectionFailures(
                surface.InspectionFailures.Where(failure =>
                    failure.SubjectToken == 0
                    || selectedTokens.Contains(failure.SubjectToken)));
        if (inspectionFailures.Length > 0)
            return Unavailable(identity, scope, inspectionFailures);

        cancellationToken.ThrowIfCancellationRequested();
        CSharpTypePrintOutcome printed =
            new CSharpTypePrinter().Print(
                selection.Request,
                new CSharpTypePrintOptions
                {
                    IncludeCustomAttributes = true,
                });
        if (printed is CSharpTypePrintOutcome.NotRendered notRendered)
        {
            ImmutableArray<TypeApiDeclarationFailure> failures =
            [
                .. notRendered.SelfNameFailures.Select(failure =>
                    new TypeApiDeclarationFailure(
                        TypeApiDeclarationFailureKind.PrinterNotRendered,
                        failure.ToString())),
                .. notRendered.MemorySafetyFailures.Select(failure =>
                    new TypeApiDeclarationFailure(
                        TypeApiDeclarationFailureKind.PrinterNotRendered,
                        $"{failure.TypeName}: {failure.Message}")),
            ];
            return Unavailable(identity, scope, failures);
        }

        CSharpTypePrintResult result =
            ((CSharpTypePrintOutcome.Printed)printed).Result;
        string text = result.Source;
        cancellationToken.ThrowIfCancellationRequested();
        return new InspectionEnvelope<TypeApiDeclarationResult>(
            new TypeApiDeclarationResult(
                TypeApiDeclarationOutcome.Available,
                identity,
                scope,
                text,
                []),
            Share(),
            result.Diagnostics.Select(diagnostic =>
                new InspectionDiagnostic(
                    "type-api-declaration.printer-diagnostic",
                    InspectionDiagnosticSeverity.Warning,
                    diagnostic.Message,
                    diagnostic.TypeName)));
    }

    static DeclarationSelection SelectDeclaration(
        ApiSurface surface,
        ApiType root,
        MetadataTypeDefinitionName requested,
        TypeApiDeclarationScope scope)
    {
        var nodes = new Dictionary<
            MetadataTypeDefinitionName,
            DeclarationNode>();
        foreach (ApiType type in surface.Types)
        {
            if (type.DefinitionName is not { } definition
                || !string.Equals(
                    definition.Namespace,
                    requested.Namespace,
                    StringComparison.Ordinal))
            {
                continue;
            }

            bool ancestor = IsPrefix(
                definition.Segments,
                requested.Segments);
            bool descendant = IsPrefix(
                requested.Segments,
                definition.Segments);
            if (!ancestor && !descendant)
                continue;

            nodes.Add(
                definition,
                new DeclarationNode(
                    type,
                    definition,
                    IsContextShell:
                        definition.Segments.Length
                            < requested.Segments.Length));
        }

        if (!nodes.ContainsKey(requested))
        {
            throw new InvalidOperationException(
                $"The selected declaration root '{root.FullName}' was not retained.");
        }

        var included = new HashSet<MetadataTypeDefinitionName>();
        foreach (DeclarationNode shell in nodes.Values.Where(
            node => node.IsContextShell))
        {
            included.Add(shell.Identity);
        }
        included.Add(requested);
        foreach (DeclarationNode descendant in nodes.Values
            .Where(node =>
                node.Identity.Segments.Length
                    > requested.Segments.Length)
            .OrderBy(node => node.Identity.Segments.Length))
        {
            MetadataTypeDefinitionName parent =
                Parent(descendant.Identity);
            if (!included.Contains(parent))
                continue;
            if (scope == TypeApiDeclarationScope.All
                || IsApiVisible(descendant.Type.Accessibility))
            {
                included.Add(descendant.Identity);
            }
        }

        var failures =
            ImmutableArray.CreateBuilder<TypeApiDeclarationFailure>();
        var requests =
            new Dictionary<
                MetadataTypeDefinitionName,
                CSharpTypePrintRequest>();
        int topLevel = included.Min(identity => identity.Segments.Length);
        foreach (DeclarationNode node in nodes.Values
            .Where(node => included.Contains(node.Identity))
            .OrderByDescending(node => node.Identity.Segments.Length))
        {
            if (node.Identity.Segments.Length > topLevel)
                RetainIntroducedTypeParameters(node.Type);
            IReadOnlyList<ApiMember> members =
                node.IsContextShell
                    ? []
                    : SelectMembers(
                        node.Type,
                        scope,
                        failures);
            CSharpTypePrintRequest[] nested =
            [
                .. requests
                    .Where(pair =>
                        pair.Key.Segments.Length
                            == node.Identity.Segments.Length + 1
                        && Parent(pair.Key) == node.Identity)
                    .OrderBy(pair => pair.Key.ToNestedMetadataName(),
                        StringComparer.Ordinal)
                    .Select(pair => pair.Value),
            ];
            requests.Add(
                node.Identity,
                new CSharpTypePrintRequest(
                    node.Type,
                    CSharpBodyPolicy.Skeleton,
                    members,
                    nestedTypes: nested));
        }

        MetadataTypeDefinitionName top =
            included.OrderBy(identity => identity.Segments.Length).First();
        return new(
            requests[top],
            failures.DrainToImmutable());
    }

    static IReadOnlyList<ApiMember> SelectMembers(
        ApiType type,
        TypeApiDeclarationScope scope,
        ImmutableArray<TypeApiDeclarationFailure>.Builder failures)
    {
        if (scope == TypeApiDeclarationScope.All)
            return type.Members;

        var selected = new List<ApiMember>();
        foreach (ApiMember member in type.Members)
        {
            if (!IsApiVisible(member.Accessibility))
                continue;

            if (member.Kind == "property"
                && member.SignatureModel is not { } structuredSignature)
            {
                failures.Add(
                    new TypeApiDeclarationFailure(
                        TypeApiDeclarationFailureKind
                            .AccessorMetadataUnavailable,
                        $"Property '{type.FullName}.{member.Name}' has no "
                            + "structured accessor metadata.",
                        SubjectToken: member.DeclarationMetadataToken));
                continue;
            }
            if (member.Kind == "property")
            {
                ApiSignature propertySignature = member.SignatureModel!;
                if (propertySignature.Accessors.Any(accessor =>
                    accessor.AccessibilityIsRepresentable == false))
                {
                    failures.Add(
                        new TypeApiDeclarationFailure(
                            TypeApiDeclarationFailureKind
                                .AccessorMetadataUnavailable,
                            $"Property '{type.FullName}.{member.Name}' has "
                                + "an accessor with no exact C# accessibility.",
                            SubjectToken:
                                member.DeclarationMetadataToken));
                    continue;
                }

                propertySignature.Accessors =
                [
                    .. propertySignature.Accessors.Where(accessor =>
                        IsApiVisible(accessor.Accessibility)),
                ];
                if (propertySignature.Accessors.Count == 0)
                    continue;
            }

            selected.Add(member);
        }

        return selected;
    }

    static void RetainIntroducedTypeParameters(ApiType type)
    {
        int introduced =
            type.IntroducedTypeParameterCounts is { Count: > 0 } counts
                ? counts[^1]
                : 0;
        if (introduced == 0)
        {
            type.TypeParameters = [];
            return;
        }
        if (type.TypeParameters.Count < introduced)
        {
            throw new InvalidOperationException(
                $"Nested Type '{type.FullName}' declares {introduced} "
                    + "introduced generic parameters but retained fewer.");
        }

        type.TypeParameters =
        [
            .. type.TypeParameters.TakeLast(introduced),
        ];
    }

    static HashSet<int> SelectedTokens(CSharpTypePrintRequest root)
    {
        var tokens = new HashSet<int>();
        Visit(root);
        return tokens;

        void Visit(CSharpTypePrintRequest request)
        {
            Add(request.Type.MetadataToken);
            foreach (ApiMember member in request.Members)
            {
                Add(member.MetadataToken);
                Add(member.DeclarationMetadataToken);
                Add(member.GetterToken);
                Add(member.SetterToken);
                Add(member.AdderToken);
                Add(member.RemoverToken);
            }
            foreach (CSharpTypePrintRequest nested in request.NestedTypes)
                Visit(nested);
        }

        void Add(int? token)
        {
            if (token is { } value)
                tokens.Add(value);
        }
    }

    static bool IsApiVisible(string? accessibility) =>
        accessibility is null or ""
            or "public"
            or "protected"
            or "protected internal";

    static bool FailureCouldHideRequestedType(
        ApiSurfaceInspectionFailure failure,
        MetadataTypeDefinitionName requested) =>
        failure.OwningTypeDefinition == requested
        || failure.AffectedTypeDefinitions.Contains(requested)
        || (failure.OwningTypeDefinition is null
            && failure.Operation is
                "type identity"
                or "type row"
                or "type summary row");

    static ImmutableArray<TypeApiDeclarationFailure>
        ProjectInspectionFailures(
        IEnumerable<ApiSurfaceInspectionFailure> failures) =>
        [
            .. failures.Select(failure =>
                new TypeApiDeclarationFailure(
                    TypeApiDeclarationFailureKind.InspectionIncomplete,
                    $"{failure.Mechanism}/{failure.Kind}: {failure.Detail}",
                    failure.Operation,
                    failure.SubjectToken)),
        ];

    static bool IsPrefix(
        ImmutableArray<string> prefix,
        ImmutableArray<string> value) =>
        prefix.Length <= value.Length
        && prefix.SequenceEqual(value.Take(prefix.Length));

    static MetadataTypeDefinitionName Parent(
        MetadataTypeDefinitionName type) =>
        MetadataTypeDefinitionName.Create(
            type.Namespace,
            type.Segments.RemoveAt(type.Segments.Length - 1))
        is MetadataTypeDefinitionNameResult.Valid valid
            ? valid.Name
            : throw new InvalidOperationException(
                "A nested metadata Type requires a valid parent identity.");

    static InspectionEnvelope<TypeApiDeclarationResult> Unavailable(
        ExactTypeDefinitionIdentity identity,
        TypeApiDeclarationScope scope,
        params TypeApiDeclarationFailure[] failures) =>
        Unavailable(identity, scope, failures.ToImmutableArray());

    static InspectionEnvelope<TypeApiDeclarationResult> Unavailable(
        ExactTypeDefinitionIdentity identity,
        TypeApiDeclarationScope scope,
        ImmutableArray<TypeApiDeclarationFailure> failures) =>
        Envelope(
            new TypeApiDeclarationResult(
                TypeApiDeclarationOutcome.Unavailable,
                identity,
                scope,
                text: null,
                failures));

    static InspectionEnvelope<TypeApiDeclarationResult> Envelope(
        TypeApiDeclarationResult result) =>
        new(result, Share(), Diagnostics(result));

    static InspectionShare Share() =>
        new InspectionShare.NonProjectable(
            "type-api-declarations/share",
            "Type API Declarations do not yet have a portable Workspace "
                + "Share representation.");

    static IEnumerable<InspectionDiagnostic> Diagnostics(
        TypeApiDeclarationResult result)
    {
        foreach (TypeApiDeclarationFailure failure in result.Failures)
        {
            yield return new InspectionDiagnostic(
                DiagnosticCode(failure.Kind),
                InspectionDiagnosticSeverity.Error,
                failure.Detail,
                failure.Operation);
        }
        if (result.Outcome == TypeApiDeclarationOutcome.NotFound)
        {
            yield return new InspectionDiagnostic(
                "type-api-declaration.not-found",
                InspectionDiagnosticSeverity.Error,
                $"Type '{string.Join(".", result.TypeIdentity.Segments)}' "
                    + "was not found.");
        }
    }

    static string DiagnosticCode(
        TypeApiDeclarationFailureKind kind) =>
        kind switch
        {
            TypeApiDeclarationFailureKind.ProjectionTruncated =>
                "type-api-declaration.projection-truncated",
            TypeApiDeclarationFailureKind.ParticipantRejected =>
                "type-api-declaration.participant-rejected",
            TypeApiDeclarationFailureKind.ParticipantFailed =>
                "type-api-declaration.participant-failed",
            TypeApiDeclarationFailureKind.InspectionIncomplete =>
                "type-api-declaration.inspection-incomplete",
            TypeApiDeclarationFailureKind.AccessorMetadataUnavailable =>
                "type-api-declaration.accessor-metadata-unavailable",
            TypeApiDeclarationFailureKind.PrinterNotRendered =>
                "type-api-declaration.printer-not-rendered",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown Type API declaration failure kind."),
        };

    sealed record DeclarationNode(
        ApiType Type,
        MetadataTypeDefinitionName Identity,
        bool IsContextShell);

    sealed record DeclarationSelection(
        CSharpTypePrintRequest Request,
        ImmutableArray<TypeApiDeclarationFailure> Failures);
}
