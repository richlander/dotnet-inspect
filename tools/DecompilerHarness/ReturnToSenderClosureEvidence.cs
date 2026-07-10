namespace ILInspector.DecompilerHarness;

internal sealed record ReturnToSenderClosureEvidence(
    int RequiredTypes,
    int RequiredMembers,
    int RoslynRecoveredTypes,
    int RoslynRecoveredMemberSurfaces,
    IReadOnlyList<ReturnToSenderRoslynFallback> RoslynFallbacks,
    IReadOnlyList<ReturnToSenderClosureRequirement> Requirements);

internal sealed record ReturnToSenderRoslynFallback(string Diagnostic, string Recovery, int Count);

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
        var fallbacks = requirements
            .SelectMany(requirement => requirement.Facts)
            .Select(ParseRoslynFallback)
            .OfType<ReturnToSenderRoslynFallback>()
            .GroupBy(fallback => (fallback.Diagnostic, fallback.Recovery))
            .Select(group => new ReturnToSenderRoslynFallback(group.Key.Diagnostic, group.Key.Recovery, group.Sum(fallback => fallback.Count)))
            .OrderBy(fallback => fallback.Diagnostic, StringComparer.Ordinal)
            .ThenBy(fallback => fallback.Recovery, StringComparer.Ordinal)
            .ToArray();
        return new ReturnToSenderClosureEvidence(
            plan.TypeRequirements.Count,
            plan.TypeRequirements.Sum(requirement => requirement.RequiredMembers.Count),
            requirements.Count(requirement => requirement.RoslynRecovered),
            requirements.Count(requirement => requirement.RoslynRecoveredMemberSurface),
            fallbacks,
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

    static ReturnToSenderRoslynFallback? ParseRoslynFallback(string fact)
    {
        const string prefix = "roslyn/";
        if (!fact.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        int kindEnd = fact.IndexOf(':', prefix.Length);
        if (kindEnd < 0)
            return null;
        string recovery = fact[prefix.Length..kindEnd];
        string detail = fact[(kindEnd + 1)..].TrimStart();
        int codeEnd = detail.IndexOf(':');
        string diagnostic = codeEnd > 0 ? detail[..codeEnd] : detail.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (!diagnostic.StartsWith("CS", StringComparison.Ordinal))
            return null;
        return new ReturnToSenderRoslynFallback(diagnostic, recovery, 1);
    }
}
