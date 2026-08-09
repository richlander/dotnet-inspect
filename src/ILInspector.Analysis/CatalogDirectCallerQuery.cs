using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// One cross-assembly direct call whose complete open member signature
/// corresponds to the selected target in one catalog generation.
/// </summary>
public sealed record CatalogDirectCaller(
    CatalogCallGraphParticipant Participant,
    DirectCall Call);

/// <summary>
/// Query-directed direct-caller correspondence over one target and the
/// same-name call sites in a fixed assembly group.
/// </summary>
public static class CatalogDirectCallerQuery
{
    public static ImmutableArray<CatalogDirectCaller> Find(
        IAssemblyBindingPolicy bindingPolicy,
        CatalogCallGraphParticipant target,
        int targetMethodToken,
        IEnumerable<CatalogCallGraphParticipant> sources,
        TypeResolutionContextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sources);

        MethodIdentity? targetMethod =
            target.Index.DeclaredMethods.FirstOrDefault(
                method => method.MetadataToken == targetMethodToken);
        if (targetMethod is null)
            return [];

        CatalogCallGraphParticipant[] sourceArray = sources.ToArray();
        var targetPlan = CatalogMemberCorrespondencePlan.Create(
            target.Assembly,
            targetMethod);
        var candidates =
            new List<(
                CatalogCallGraphParticipant Participant,
                DirectCall Call,
                CatalogMemberCorrespondencePlan Plan)>();
        foreach (CatalogCallGraphParticipant source in sourceArray)
        {
            ArgumentNullException.ThrowIfNull(source);
            foreach (DirectCall call in source.Index.DirectCalls)
            {
                if (!string.Equals(
                        call.Callee.Name,
                        targetMethod.Name,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                candidates.Add(
                    (
                        source,
                        call,
                        CatalogMemberCorrespondencePlan.Create(
                            source.Assembly,
                            call.Callee)));
            }
        }

        TypeResolutionRequest[] requests =
            targetPlan.Requests
                .Concat(candidates.SelectMany(
                    candidate => candidate.Plan.Requests))
                .Distinct(TypeResolutionRequestComparer.Instance)
                .ToArray();
        using TypeResolutionContext context =
            TypeResolutionContext.Create(
                bindingPolicy,
                sourceArray
                    .Select(source => source.Assembly)
                    .Prepend(target.Assembly),
                requests,
                options);
        if (targetPlan.Project(context)
            is not CatalogMemberJoinProjection.Issued targetProjection)
        {
            return [];
        }

        var matches = ImmutableArray.CreateBuilder<CatalogDirectCaller>();
        foreach (var candidate in candidates)
        {
            if (candidate.Plan.Project(context)
                    is CatalogMemberJoinProjection.Issued projection
                && projection.Key.Equals(targetProjection.Key))
            {
                matches.Add(
                    new CatalogDirectCaller(
                        candidate.Participant,
                        candidate.Call));
            }
        }

        return matches.ToImmutable();
    }
}
