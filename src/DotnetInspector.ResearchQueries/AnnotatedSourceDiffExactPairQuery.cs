using ILInspector.Metadata;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>A producer-issued body selection with its side-local declaration anchor.</summary>
public sealed record AnnotatedSourceDiffBodyEndpoint(
    MetadataTypeDefinitionName Type,
    MemberAnchor Anchor,
    MemberTargetSelector Selector,
    int MethodToken,
    ResearchTargetRelationshipRole Role);

/// <summary>
/// Projects an already-corresponded exact pair. The caller owns the semantic
/// relation; Research validates and designates its physical endpoints.
/// </summary>
public static class AnnotatedSourceDiffExactPairQuery
{
    public static AnnotatedSourceDiffDocument Execute(
        AssemblyContextGroup beforeGroup, AssemblyContextParticipant beforeParticipant,
        AssemblyContextGroup afterGroup, AssemblyContextParticipant afterParticipant,
        ImplementationComparisonBinding beforeBinding, ImplementationComparisonBinding afterBinding,
        AnnotatedSourceDiffBodyEndpoint? before, AnnotatedSourceDiffBodyEndpoint after,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(after);
        AdmitPair(beforeGroup, beforeParticipant, afterGroup, afterParticipant,
                beforeBinding, afterBinding, before, after, cancellationToken);
        return AnnotatedSourceDiffDocument.Create(
            new(after.Type.ToEscapedFullName(), after.Selector.NormalizedSelector),
            before is null ? AnnotatedSourceDiffSide.Absent()
                : Project(beforeGroup, beforeParticipant, before, cancellationToken),
            Project(afterGroup, afterParticipant, after, cancellationToken), [], includeIl: true);
    }

    static void AdmitPair(AssemblyContextGroup beforeGroup, AssemblyContextParticipant beforeParticipant,
        AssemblyContextGroup afterGroup, AssemblyContextParticipant afterParticipant,
        ImplementationComparisonBinding beforeBinding, ImplementationComparisonBinding afterBinding,
        AnnotatedSourceDiffBodyEndpoint? before, AnnotatedSourceDiffBodyEndpoint after,
        CancellationToken cancellationToken)
    {
        var sealing = QueryComparisonPopulationSealer.Execute(
            new ImplementationComparisonPopulationRequest([beforeBinding], [afterBinding]));
        if (sealing is not QueryPopulationSealingOutcome.Sealed sealedPopulation)
            throw new InspectionQueryException("Exact Member population was rejected.");
        var population = (QueryComparisonPopulation<ImplementationComparisonBinding>)sealedPopulation.Population;
        if (QueryPopulationProjection.Execute(population)
            is not QueryPopulationProjectionOutcome.Projected projected)
            throw new InspectionQueryException("Exact Member population projection was rejected.");
        var receipt = projected.Population.Receipt;
        var admission = projected.Population.Admission;
        var question = receipt.Questions[population.Question];
        var oldInput = admission.Inputs.Single(input => input.Side == ResearchComparisonSide.Before);
        var newInput = admission.Inputs.Single(input => input.Side == ResearchComparisonSide.After);
        var selections = new List<ResearchMemberSelectionOccurrence>();
        ValidateEndpoint(after);
        if (before is not null)
        {
            ValidateEndpoint(before);
            selections.Add(new ResearchExactAddressMemberSelection(question, oldInput, before.Type, before.Selector,
                Address(beforeGroup, beforeParticipant, before.MethodToken), before.Role));
        }
        selections.Add(new ResearchExactAddressMemberSelection(question, newInput, after.Type, after.Selector,
            Address(afterGroup, afterParticipant, after.MethodToken), after.Role));
        var planning = ResearchTargetResolver.Resolve(new(
            admission,
            admission.Inputs.Select(input => new ResearchTargetInputRoleAssignment(input, ResearchTargetInputRole.Implementation)),
            selections), cancellationToken);
        if (planning is not ResearchTargetPlanningOutcome.Planned planned)
            throw new InspectionQueryException("Exact Member planning was rejected.");
        if (planned.Resolution.Attempts.Any(attempt => attempt.Outcome is not ResearchTargetOutcome.Resolved))
            throw new InspectionQueryException("Exact Member endpoint association is unavailable or rejected.");
        if (before is null) return;
        var oldAttempt = planned.Resolution.Attempts.Single(attempt => ReferenceEquals(attempt.Request.Input, oldInput.Id));
        var newAttempt = planned.Resolution.Attempts.Single(attempt => ReferenceEquals(attempt.Request.Input, newInput.Id));
        if (ResearchDesignatedPairAdmission.Admit(admission, planned.Resolution, oldAttempt, newAttempt)
            is not ResearchDesignatedPairOutcome.Admitted)
            throw new InspectionQueryException("Exact Member designation is unavailable or rejected.");
    }

    static void ValidateEndpoint(AnnotatedSourceDiffBodyEndpoint endpoint)
    {
        if (endpoint.Anchor.TypeFullName != endpoint.Type.ToEscapedFullName()
            || !(endpoint.Selector.NormalizedSelector == endpoint.Anchor.StableSelector
                || endpoint.Role != ResearchTargetRelationshipRole.Method
                    && endpoint.Selector.NormalizedSelector.StartsWith(endpoint.Anchor.StableSelector + ":", StringComparison.Ordinal)))
            throw new ArgumentException("The exact Member anchor and selection do not agree.", nameof(endpoint));
    }

    static MetadataMethodAddress Address(AssemblyContextGroup group, AssemblyContextParticipant participant, int token)
        => AssemblyContextMethodAddressQuery.ExecuteParticipant(group, participant, token) switch
        {
            AssemblyContextEntry<MetadataMethodAddress>.Available available => available.Value,
            _ => throw new InspectionQueryException("Exact Member address is unavailable."),
        };

    static AnnotatedSourceDocument WithDeclaration(AssemblyContextGroup group,
        AssemblyContextParticipant participant, AnnotatedSourceDiffBodyEndpoint selection,
        AnnotatedSourceDocument document)
    {
        var available = AssemblyContextApiSurfaceQuery.ExecuteParticipant(group, participant, ApiSurfaceScope.Public)
            as AssemblyContextEntry<AssemblyApiSurface>.Available
            ?? throw new InspectionQueryException("The exact Member declaration is unavailable.");
        var type = available.Value.Surface.Types.Single(type => type.DefinitionName == selection.Type);
        var member = type.Members.Single(member => ApiMemberIdentity.GetMemberAnchor(type, member) == selection.Anchor);
        string declaration = CSharpMemberDeclaration.Render(type, member);
        string csharp = declaration + "\n";
        string prefix = csharp;
        AnnotatedSourceSpan Shift(AnnotatedSourceSpan span) => new(span.Start + prefix.Length, span.Length);
        var nodes = document.Nodes.Select(node => new AnnotatedSourceNode(node.Id, node.Kind, node.Medium,
            node.Spans.Select(Shift).ToArray(), node.IlOffset, node.Provenance)).ToList();
        nodes.Add(new(nodes.Count, AnnotatedSourceNodeKinds.MemberDeclaration, SourceLineKind.CSharp, [new(0, csharp.Length - 1)]));
        return new(prefix + document.Text, nodes,
            document.Regions.Select(region => new AnnotatedSourceRegion(region.Role, region.Spans.Select(Shift).ToArray())).ToArray(),
            document.Facts, document.Targets, document.Source);
    }

    static AnnotatedSourceDiffSide Project(AssemblyContextGroup group,
        AssemblyContextParticipant participant, AnnotatedSourceDiffBodyEndpoint selection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var address = Address(group, participant, selection.MethodToken);
        var endpoint = new AnnotatedSourceDiffEndpoint(participant.Assembly.Identity.Name,
            address.ModuleVersionId, address.Token, selection.Role);
        var projected = AssemblyContextMemberProjectionQuery.ExecuteParticipant(group, participant,
            new(selection.Type.ToEscapedFullName(), selection.Selector.NormalizedSelector,
                MethodToken: address.Token, SourceDocument: true));
        cancellationToken.ThrowIfCancellationRequested();
        return projected switch
        {
            AssemblyContextEntry<AssemblyMemberProjection>.Available available
                when available.Value.Projection.SelectedMethodToken == address.Token
                    && available.Value.Projection.SourceDocument is { } document =>
                AnnotatedSourceDiffSide.Present(endpoint, WithDeclaration(group, participant, selection, document)),
            AssemblyContextEntry<AssemblyMemberProjection>.Available available
                when available.Value.Projection.SourceDocumentFailureKind == MemberProjectionSourceDocumentFailureKind.NoManagedBody =>
                AnnotatedSourceDiffSide.NotApplicable(endpoint, "No managed body."),
            AssemblyContextEntry<AssemblyMemberProjection>.Failed failed =>
                AnnotatedSourceDiffSide.Failed(endpoint, AnnotatedSourceDiffSideReason.ProjectionFailed, failed.Error.Message),
            _ => AnnotatedSourceDiffSide.Failed(endpoint, AnnotatedSourceDiffSideReason.ProjectionFailed,
                "The exact Member document could not be projected."),
        };
    }
}
