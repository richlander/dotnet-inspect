using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.ResearchSections;

public sealed record MemberBodyDiffMember(
    ResearchSubjectKey Subject,
    AnnotatedSourceDiffBodyEndpoint? Before,
    AnnotatedSourceDiffBodyEndpoint? After,
    LibraryApiMemberRelation? ApiRelation,
    IReadOnlyList<ImplementationDiffDocumentMember> Implementation,
    string Outcome,
    string? IdentityFailure);

public sealed record MemberBodyDiffInventory(
    ImplementationDiffDocument Implementation,
    LibraryApiDiffDocument Api,
    IReadOnlyList<MemberBodyDiffMember> Members,
    IReadOnlyList<MemberBodyDiffMember> Destinations);

public sealed record MemberBodyDiffMemberDocument(
    LibraryApiMemberRelation? ApiRelation,
    ResearchSubjectKey Subject,
    AnnotatedSourceDiffDocument Document);

/// <summary>Shared public-population implementation comparison and exact Member handoff.</summary>
public static class MemberBodyDiffInspection
{
    public static InspectionEnvelope<MemberBodyDiffInventory> Execute(
        AssemblyContextGroup beforeGroup, AssemblyContextParticipant before,
        AssemblyContextGroup afterGroup, AssemblyContextParticipant after,
        ApiSurfaceProjectionLimits limits,
        CancellationToken cancellationToken = default,
        AssemblyContextApiComparisonResult? surfaceComparison = null)
    {
        AssemblyContextApiComparisonResult api = surfaceComparison ?? AssemblyContextApiComparisonQuery.Execute(
            beforeGroup, before, afterGroup, after, ApiSurfaceScope.Public, limits);
        if (LibraryApiDiffPresentationAdapter.Create(api) is not LibraryApiDiffOutcome.Available available
            || api.Before.Surface is not { } oldSurface || api.After.Surface is not { } newSurface)
            throw new InspectionQueryException("The public API endpoints are unavailable or incomplete.");
        var oldBodies = Bodies(surfaceComparison is null ? oldSurface : ImplementationSurface(beforeGroup, before, limits));
        var newBodies = Bodies(surfaceComparison is null ? newSurface : ImplementationSurface(afterGroup, after, limits));
        ComparisonMemberSelection[] selections = [.. oldBodies.Concat(newBodies)
            .Select(body => new ComparisonMemberSelection(body.Type, body.Selector))
            .DistinctBy(selection => (selection.DeclaringType.ToEscapedFullName(), selection.Selector.NormalizedSelector))];
        return AssemblyContextImplementationDiffQuery.Execute(beforeGroup, before, afterGroup, after,
            oldBodies.Select(body => body.MethodToken).ToHashSet(),
            newBodies.Select(body => body.MethodToken).ToHashSet(), (oldBinding, newBinding) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = ImplementationDiffDocumentQuery.Execute(new(
                    [new(oldBinding.Assembly, oldBinding.Resolver, oldBinding.MethodPopulation)],
                    [new(newBinding.Assembly, newBinding.Resolver, newBinding.MethodPopulation)],
                    null, new ImplementationComparisonPopulation.Selected(selections,
                        [.. ApiBodyPairs(available.Document, oldBodies, newBodies)
                            .Where(pair => pair.Before is not null && pair.After is not null)
                            .Select(pair => new ComparisonMemberPairSelection(
                                new(pair.Before!.Type, pair.Before.Selector), new(pair.After!.Type, pair.After.Selector)))]),
                    ImplementationDiffMechanism.CSharp | ImplementationDiffMechanism.IlBody));
                var oldIndex = Index(oldBodies, oldBinding);
                var newIndex = Index(newBodies, newBinding);
                var rows = new Dictionary<string, MemberBodyDiffMember>(StringComparer.Ordinal);
                foreach (var member in document.Members)
                {
                    oldIndex.TryGetValue(member.Subject.Id, out var oldBody);
                    newIndex.TryGetValue(member.Subject.Id, out var newBody);
                    rows.Add(member.Subject.Id, new(member.Subject, oldBody, newBody, null, [member],
                        oldBody is null && newBody is not null ? "Added"
                            : newBody is null && oldBody is not null ? "Removed" : "Changed",
                        oldBody is null && newBody is null ? "Exact body identity unavailable." : null));
                }
                foreach (var pair in ApiBodyPairs(available.Document, oldBodies, newBodies))
                {
                    var relation = pair.Relation;
                    var oldBody = pair.Before;
                    var newBody = pair.After;
                    if (oldBody is null && newBody is null) continue;
                    var binding = newBody is not null ? newBinding : oldBinding;
                    var body = newBody ?? oldBody!;
                    var subject = Subject(body, binding);
                    var matched = rows.Values.Where(row =>
                        oldBody is not null && row.Before == oldBody || newBody is not null && row.After == newBody).ToArray();
                    var evidence = matched.SelectMany(row => row.Implementation).Distinct().ToList();
                    foreach (var row in matched) rows.Remove(row.Subject.Id);
                    rows[subject.Id] = new(subject, oldBody, newBody, relation, evidence,
                        relation.PairKind.ToString(), null);
                }
                cancellationToken.ThrowIfCancellationRequested();
                var destinations = new Dictionary<string, MemberBodyDiffMember>(rows, StringComparer.Ordinal);
                foreach (var body in newBodies)
                {
                    var subject = Subject(body, newBinding);
                    if (destinations.ContainsKey(subject.Id)) continue;
                    oldIndex.TryGetValue(subject.Id, out var priorBody);
                    if (priorBody is not null)
                        destinations.Add(subject.Id, new(subject, priorBody, body, null, [], "Identical", null));
                }
                return new InspectionEnvelope<MemberBodyDiffInventory>(
                    new(document, available.Document, [.. rows.Values], [.. destinations.Values]),
                    new InspectionShare.NonProjectable("comparison/endpoints",
                        "Workspace Share does not represent an ordered Member Body comparison."));
            }, cancellationToken);
    }

    public static InspectionEnvelope<MemberBodyDiffMemberDocument> ExecuteMember(
        AssemblyContextGroup beforeGroup, AssemblyContextParticipant before,
        AssemblyContextGroup afterGroup, AssemblyContextParticipant after,
        MemberBodyDiffMember member,
        CancellationToken cancellationToken = default)
    {
        if (member.After is not { } current)
            throw new ArgumentException("A deleted Member has no current destination.", nameof(member));
        if (member.Before is { } priorEndpoint && priorEndpoint.Role != current.Role)
            throw new ArgumentException("The exact body endpoints must have the same API member role.", nameof(member));
        if (member.ApiRelation is { } relation
            && (relation.Before?.Anchor != member.Before?.Anchor
                || relation.After?.Anchor != current.Anchor
                || relation.Before?.DeclaringType.DefinitionName.ToEscapedFullName() != member.Before?.Type.ToEscapedFullName()
                || relation.After?.DeclaringType.DefinitionName.ToEscapedFullName() != current.Type.ToEscapedFullName()))
            throw new ArgumentException("The API relation does not own these exact body endpoints.", nameof(member));
        return AssemblyContextImplementationDiffQuery.Execute(beforeGroup, before, afterGroup, after,
            member.Before is { } prior ? new HashSet<int> { prior.MethodToken } : new HashSet<int>(),
            new HashSet<int> { current.MethodToken }, (oldBinding, newBinding) => new InspectionEnvelope<MemberBodyDiffMemberDocument>(
                new(member.ApiRelation, member.Subject,
                    AnnotatedSourceDiffExactPairQuery.Execute(beforeGroup, before, afterGroup, after,
                        oldBinding, newBinding, member.Before, current, cancellationToken)),
                new InspectionShare.NonProjectable("comparison/endpoints",
                    "Workspace Share does not represent an exact Member Body comparison.")), cancellationToken);
    }

    static ApiSurface ImplementationSurface(AssemblyContextGroup group, AssemblyContextParticipant root,
        ApiSurfaceProjectionLimits limits)
    {
        var projection = AssemblyContextApiSurfaceQuery.ExecuteBounded(group, ApiSurfaceScope.Public, limits, [root]);
        if (projection.Truncation is not null
            || projection.Assemblies.Assemblies.Single() is not AssemblyContextEntry<AssemblyApiSurface>.Available available
            || !available.Value.InspectionFailures.IsEmpty)
            throw new InspectionQueryException("The public implementation endpoint is incomplete or unavailable.");
        return available.Value.Surface;
    }

    static AnnotatedSourceDiffBodyEndpoint[] Bodies(ApiSurface surface)
    {
        var result = new List<AnnotatedSourceDiffBodyEndpoint>();
        foreach (var type in surface.Types)
        foreach (var member in type.Members)
        {
            if (member.Accessibility is not null and not "public" || type.DefinitionName is null) continue;
            var anchor = ApiMemberIdentity.GetMemberAnchor(type, member);
            foreach (var body in CallGraphMemberResolver.CreateBodySelectors(type, member))
            {
                if (body.BodyToken == member.GetterToken && member.GetterAccessibility is not null and not "public"
                    || body.BodyToken == member.SetterToken && member.SetterAccessibility is not null and not "public"
                    || body.BodyToken == member.AdderToken && member.AdderAccessibility is not null and not "public"
                    || body.BodyToken == member.RemoverToken && member.RemoverAccessibility is not null and not "public") continue;
                bool? hasBody = body.BodyToken == member.GetterToken ? member.GetterHasMethodBody
                    : body.BodyToken == member.SetterToken ? member.SetterHasMethodBody
                    : body.BodyToken == member.AdderToken ? member.AdderHasMethodBody
                    : body.BodyToken == member.RemoverToken ? member.RemoverHasMethodBody
                    : member.HasMethodBody;
                if (hasBody != true) continue;
                var role = body.BodyToken == member.GetterToken ? ResearchTargetRelationshipRole.Getter
                    : body.BodyToken == member.SetterToken ? ResearchTargetRelationshipRole.Setter
                    : body.BodyToken == member.AdderToken ? ResearchTargetRelationshipRole.Adder
                    : body.BodyToken == member.RemoverToken ? ResearchTargetRelationshipRole.Remover
                    : ResearchTargetRelationshipRole.Method;
                string selector = anchor.StableSelector;
                if (role != ResearchTargetRelationshipRole.Method)
                {
                    int?[] tokens = member.Kind == "property"
                        ? [member.GetterToken, member.SetterToken] : [member.AdderToken, member.RemoverToken];
                    selector += $":{Array.IndexOf(tokens.Where(token => token is not null).ToArray(), body.BodyToken) + 1}";
                }
                result.Add(new(type.DefinitionName, anchor, MemberTargetSelector.Parse(selector), body.BodyToken, role));
            }
        }
        return [.. result];
    }

    static AnnotatedSourceDiffBodyEndpoint? Find(IEnumerable<AnnotatedSourceDiffBodyEndpoint> bodies,
        LibraryApiMemberIdentity? member, ResearchTargetRelationshipRole role)
        => member is null ? null
            : bodies.SingleOrDefault(body => body.Type.Equals(member.DeclaringType.DefinitionName)
                && body.Anchor == member.Anchor && body.Role == role);

    static IEnumerable<(LibraryApiMemberRelation Relation, AnnotatedSourceDiffBodyEndpoint? Before,
        AnnotatedSourceDiffBodyEndpoint? After)> ApiBodyPairs(LibraryApiDiffDocument document,
        AnnotatedSourceDiffBodyEndpoint[] before, AnnotatedSourceDiffBodyEndpoint[] after)
    {
        foreach (var relation in document.Comparison.Subjects.SelectMany(type => type.Comparison.Members)
            .Select(member => member.Relation).DistinctBy(relation => relation.Identifier))
        foreach (var role in new[] { ResearchTargetRelationshipRole.Method, ResearchTargetRelationshipRole.Getter,
            ResearchTargetRelationshipRole.Setter, ResearchTargetRelationshipRole.Adder, ResearchTargetRelationshipRole.Remover })
        {
            var oldBody = Find(before, relation.Before, role);
            var newBody = Find(after, relation.After, role);
            if (oldBody is not null || newBody is not null) yield return (relation, oldBody, newBody);
        }
    }

    static ResearchSubjectKey Subject(AnnotatedSourceDiffBodyEndpoint body, ImplementationComparisonBinding binding)
    {
        var method = binding.MethodPopulation.DeclaredMethods.SingleOrDefault(method => method.MetadataToken == body.MethodToken);
        return method is null ? ResearchMemberIdentity.SubjectFromAnchor(body.Anchor, body.Anchor.CanonicalSignature)
            : ResearchMemberIdentity.SubjectFromMethod(method);
    }

    static Dictionary<string, AnnotatedSourceDiffBodyEndpoint> Index(
        IEnumerable<AnnotatedSourceDiffBodyEndpoint> bodies, ImplementationComparisonBinding binding)
        => bodies.SelectMany(body =>
            {
                var method = binding.MethodPopulation.DeclaredMethods.SingleOrDefault(method => method.MetadataToken == body.MethodToken);
                return new[] { Subject(body, binding).Id, method is null ? Subject(body, binding).Id
                    : ResearchMemberIdentity.SubjectFromMethod(method, includeReturnType: true).Id }
                    .Distinct().Select(id => (Id: id, Body: body));
            }).GroupBy(item => item.Id, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single().Body, StringComparer.Ordinal);
}
