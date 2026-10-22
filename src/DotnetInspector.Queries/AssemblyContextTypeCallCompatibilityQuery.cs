using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Analysis = ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public enum TypeCallCompatibilityTargetDisposition
{
    Available,
    Unavailable,
    Limited,
}

public enum TypeCallCompatibilityTopologyDisposition
{
    Complete,
    Partial,
    Unavailable,
}

/// <summary>
/// Compatibility attributes read directly from one admitted target method.
/// </summary>
public sealed record TypeCallCompatibilityTarget(
    AssemblyContextSubject Subject,
    Analysis.MethodIdentity Method,
    TypeCallCompatibilityTargetDisposition Disposition,
    ImmutableArray<CompatibilityAttributeEvidence> Attributes,
    CandidateOpenFailure? OpenFailure = null,
    string? Limitation = null);

/// <summary>
/// Exact outgoing call and compatibility evidence for one focused Type.
/// </summary>
public sealed record AssemblyContextTypeCallCompatibilityResult(
    AssemblyAcquisitionRegistration FocusAssembly,
    MetadataTypeDefinitionName FocusType,
    ImmutableArray<AssemblyContextCallCensusOccurrence> Occurrences,
    ImmutableArray<AssemblyContextCallCensusUnresolvedOccurrence>
        UnresolvedOccurrences,
    ImmutableArray<TypeCallCompatibilityTarget> Targets,
    TypeCallCompatibilityTopologyDisposition TopologyDisposition)
{
    public bool TopologyComplete =>
        TopologyDisposition
            == TypeCallCompatibilityTopologyDisposition.Complete;
}

/// <summary>
/// Projects one exact Type's outgoing call census and reads direct
/// compatibility attributes from its exact resolved targets.
/// </summary>
public static class AssemblyContextTypeCallCompatibilityQuery
{
    public static InspectionQuery<AssemblyContextTypeCallCompatibilityResult>
        Definition
    { get; } =
        new(
            "Assembly context Type call compatibility",
            InspectionCost.Unbounded);

    public static AssemblyContextTypeCallCompatibilityResult Execute(
        AssemblyContextGroup group,
        AssemblyContextCallCensusResult census,
        AssemblyAcquisitionRegistration focusAssembly,
        MetadataTypeDefinitionName focusType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(census);
        ArgumentNullException.ThrowIfNull(focusAssembly);
        ArgumentNullException.ThrowIfNull(focusType);

        AssemblyContextParticipant focusParticipant =
            RequireParticipant(group, focusAssembly);
        string focusNamespace = focusType.Namespace;
        string focusName = focusType.ToNestedMetadataName();

        ImmutableArray<AssemblyContextCallCensusOccurrence> occurrences =
        [
            .. census.Occurrences.Where(occurrence =>
                    ReferenceEquals(
                        occurrence.Source.Registration,
                        focusParticipant.Assembly.Registration)
                    && IsType(
                        occurrence.SourceMethod.DeclaringType,
                        focusNamespace,
                        focusName))
                .Where(occurrence =>
                    !ReferenceEquals(
                        occurrence.Target.Registration,
                        focusParticipant.Assembly.Registration)
                    || !IsType(
                        occurrence.TargetMethod.DeclaringType,
                        focusNamespace,
                        focusName)),
        ];
        ImmutableArray<AssemblyContextCallCensusUnresolvedOccurrence>
            unresolved =
        [
            .. census.UnresolvedOccurrences.Where(occurrence =>
                ReferenceEquals(
                    occurrence.Source.Registration,
                    focusParticipant.Assembly.Registration)
                && IsType(
                    occurrence.SourceMethod.DeclaringType,
                    focusNamespace,
                    focusName)),
        ];

        ImmutableArray<TypeCallCompatibilityTarget> targets =
            InspectTargets(group, occurrences, cancellationToken);
        TypeCallCompatibilityTopologyDisposition topology =
            census.Receipt is null
                ? TypeCallCompatibilityTopologyDisposition.Unavailable
                : census.IsComplete && unresolved.IsEmpty
                    ? TypeCallCompatibilityTopologyDisposition.Complete
                    : TypeCallCompatibilityTopologyDisposition.Partial;

        return new(
            focusAssembly,
            focusType,
            occurrences,
            unresolved,
            targets,
            topology);
    }

    static ImmutableArray<TypeCallCompatibilityTarget> InspectTargets(
        AssemblyContextGroup group,
        ImmutableArray<AssemblyContextCallCensusOccurrence> occurrences,
        CancellationToken cancellationToken)
    {
        var targets = occurrences
            .GroupBy(occurrence => (
                occurrence.Target.Registration,
                occurrence.TargetMethod))
            .Select(grouping => (
                Subject: grouping.First().Target,
                Method: grouping.Key.TargetMethod,
                Order: grouping
                    .OrderBy(occurrence =>
                        occurrence.TargetOrderingKey)
                    .First()
                    .TargetOrderingKey))
            .OrderBy(target => target.Order)
            .ToArray();
        var results =
            ImmutableArray.CreateBuilder<TypeCallCompatibilityTarget>(
                targets.Length);

        foreach (var assemblyTargets in targets.GroupBy(
            target => target.Subject.Registration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssemblyContextParticipant participant =
                RequireParticipant(group, assemblyTargets.Key);
            var targetSnapshot = assemblyTargets.ToArray();
            AssemblyImageAccessResult<
                ImmutableArray<TypeCallCompatibilityTarget>> access =
                    group.UseSnapshot(
                        participant,
                        cancellationToken,
                        snapshot => InspectTargetSnapshot(
                            snapshot,
                            targetSnapshot,
                            cancellationToken));
            switch (access)
            {
                case AssemblyImageAccessResult<
                    ImmutableArray<TypeCallCompatibilityTarget>>
                    .Available available:
                    results.AddRange(available.Value);
                    break;
                case AssemblyImageAccessResult<
                    ImmutableArray<TypeCallCompatibilityTarget>>
                    .Rejected rejected:
                    results.AddRange(targetSnapshot.Select(target =>
                        new TypeCallCompatibilityTarget(
                            target.Subject,
                            target.Method,
                            TypeCallCompatibilityTargetDisposition.Unavailable,
                            [],
                            rejected.Failure)));
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown assembly image access result.");
            }
        }

        return
        [
            .. results.OrderBy(result =>
                targets.First(target =>
                    ReferenceEquals(
                        target.Subject.Registration,
                        result.Subject.Registration)
                    && target.Method == result.Method).Order),
        ];
    }

    static ImmutableArray<TypeCallCompatibilityTarget> InspectTargetSnapshot(
        AssemblyImageSnapshot snapshot,
        (AssemblyContextSubject Subject, Analysis.MethodIdentity Method,
            Analysis.CatalogCallCensusMethodOrderingKey Order)[] targets,
        CancellationToken cancellationToken)
    {
        try
        {
            using var image = new PEReader(snapshot.Content);
            MetadataReader reader =
                MetadataFormatAdmission.GetMetadataReader(image);
            var results =
                ImmutableArray.CreateBuilder<TypeCallCompatibilityTarget>(
                    targets.Length);
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(
                    InspectTarget(reader, snapshot.ModuleVersionId, target));
            }
            return results.MoveToImmutable();
        }
        catch (BadImageFormatException error)
        {
            return
            [
                .. targets.Select(target =>
                    new TypeCallCompatibilityTarget(
                        target.Subject,
                        target.Method,
                        TypeCallCompatibilityTargetDisposition.Limited,
                        [],
                        Limitation: error.Message)),
            ];
        }
    }

    static TypeCallCompatibilityTarget InspectTarget(
        MetadataReader reader,
        Guid moduleVersionId,
        (AssemblyContextSubject Subject, Analysis.MethodIdentity Method,
            Analysis.CatalogCallCensusMethodOrderingKey Order) target)
    {
        if (target.Method.ModuleVersionId != moduleVersionId
            || (target.Method.MetadataToken & unchecked((int)0xff000000))
                != 0x06000000)
        {
            return Limited(target, "The target method identity did not match the image.");
        }

        int row = target.Method.MetadataToken & 0x00ffffff;
        if (row == 0 || row > reader.MethodDefinitions.Count)
            return Limited(target, "The target method token was outside the image.");

        MethodDefinition definition = reader.GetMethodDefinition(
            MetadataTokens.MethodDefinitionHandle(row));
        var attributes =
            ImmutableArray.CreateBuilder<CompatibilityAttributeEvidence>();
        bool unresolved = false;
        foreach (CustomAttributeHandle handle in
            definition.GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            string? name = AttributeReader.GetAttributeTypeName(
                reader,
                attribute.Constructor);
            if (name is null)
            {
                unresolved = true;
                continue;
            }

            if (name is
                KnownAttributeNames.RequiresUnreferencedCodeAttribute
                or KnownAttributeNames.RequiresDynamicCodeAttribute
                or KnownAttributeNames.RequiresAssemblyFilesAttribute)
            {
                attributes.Add(
                    new(
                        target.Method,
                        MetadataTokens.GetToken(handle),
                        name));
            }
        }

        return unresolved
            ? new(
                target.Subject,
                target.Method,
                TypeCallCompatibilityTargetDisposition.Limited,
                attributes.ToImmutable(),
                Limitation:
                    "At least one custom attribute type could not be resolved.")
            : new(
                target.Subject,
                target.Method,
                TypeCallCompatibilityTargetDisposition.Available,
                attributes.ToImmutable());
    }

    static TypeCallCompatibilityTarget Limited(
        (AssemblyContextSubject Subject, Analysis.MethodIdentity Method,
            Analysis.CatalogCallCensusMethodOrderingKey Order) target,
        string detail) =>
        new(
            target.Subject,
            target.Method,
            TypeCallCompatibilityTargetDisposition.Limited,
            [],
            Limitation: detail);

    static AssemblyContextParticipant RequireParticipant(
        AssemblyContextGroup group,
        AssemblyAcquisitionRegistration registration)
    {
        AssemblyContextParticipant? participant =
            group.Participants.SingleOrDefault(candidate =>
                ReferenceEquals(
                    candidate.Assembly.Registration,
                    registration));
        return participant
            ?? throw new ArgumentException(
                "The exact assembly registration is not a group participant.",
                nameof(registration));
    }

    static bool IsType(
        Analysis.TypeRef type,
        string @namespace,
        string name) =>
        StringComparer.Ordinal.Equals(type.Namespace, @namespace)
        && StringComparer.Ordinal.Equals(type.Name, name);
}
