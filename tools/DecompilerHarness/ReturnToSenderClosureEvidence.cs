using ILInspector.Decompiler;

namespace ILInspector.DecompilerHarness;

internal sealed record ReturnToSenderClosureEvidence(
    int RequiredTypes,
    int RequiredMembers,
    int RoslynRecoveredTypes,
    int RoslynRecoveredMemberSurfaces,
    IReadOnlyList<ReturnToSenderClosureRequirement> Requirements);

internal sealed record ReturnToSenderClosureRequirement(
    string Type,
    int RequiredMembers,
    bool RoslynRecovered,
    bool RoslynRecoveredMemberSurface,
    IReadOnlyList<string> Facts);

internal static class ReturnToSenderClosureEvidenceBuilder
{
    public static ReturnToSenderClosureEvidence FromPlan(CompileBackReconstructionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var requirements = plan.TypeRequirements.Select(Requirement).ToArray();
        return new ReturnToSenderClosureEvidence(
            plan.TypeRequirements.Count,
            plan.TypeRequirements.Sum(requirement => requirement.RequiredMembers.Count),
            requirements.Count(requirement => requirement.RoslynRecovered),
            requirements.Count(requirement => requirement.RoslynRecoveredMemberSurface),
            requirements);
    }

    static ReturnToSenderClosureRequirement Requirement(CompileBackTypeRequirement requirement)
    {
        var facts = requirement.SourceFacts
            .Select(fact => $"{fact.Producer}/{fact.Id}: {fact.Detail}")
            .ToArray();
        return new ReturnToSenderClosureRequirement(
            requirement.Type.FullName,
            requirement.RequiredMembers.Count,
            requirement.SourceFacts.Any(fact => fact.Producer == "roslyn" && fact.Id == "closure-root"),
            requirement.SourceFacts.Any(fact => fact.Producer == "roslyn" && fact.Id == "closure-member"),
            facts);
    }
}
