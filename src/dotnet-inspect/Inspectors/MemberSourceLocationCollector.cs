using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal static class MemberSourceLocationCollector
{
    public static async Task<string?> EnrichAsync(
        ApiType apiType,
        string assemblyPath,
        string? packageName,
        string? packageVersion,
        MemberOptions options,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        try
        {
            using var service = SourceLinkService.Open(assemblyPath, logger.Log);
            var context = service.Context;
            if (!context.HasMetadata)
                return null;

            if (context.NeedsPdb)
            {
                await SourceEnricher.AcquirePdbAsync(context, httpClient,
                    packageName, packageVersion,
                    isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly), logger.Log);
            }

            var pdbPath = context.PortablePdbPath;
            if (!service.HasPdb || !service.HasSourceLink)
                return pdbPath;

            var targetMembers = GetTargetMembers(apiType, options).ToArray();
            var subject = new FindingSubject(assemblyPath, Path.GetFileName(assemblyPath));
            var membersByToken = targetMembers
                .Where(static member => member.MetadataToken is not null)
                .GroupBy(static member => member.MetadataToken!.Value)
                .ToDictionary(static group => group.Key, static group => group.ToArray());
            if (membersByToken.Count == 0)
                return pdbPath;

            var sourceInspection = MetadataFindings.InspectMemberSources(
                service,
                subject,
                new MemberSourceQuery(membersByToken.Keys.ToHashSet()));
            if (sourceInspection.Value is FindingInspection<MemberSourceObservation>.Complete complete)
            {
                ApplySourceLocations(membersByToken, complete);
                return pdbPath;
            }

            if (sourceInspection.Value is FindingInspection<MemberSourceObservation>.Absent)
                return pdbPath;

            // A malformed method must not suppress source locations for healthy selected
            // members. Token queries are direct lookups, so this fallback remains O(selected).
            foreach (var (token, members) in membersByToken)
            {
                var tokenInspection = MetadataFindings.InspectMemberSources(
                    service,
                    subject,
                    new MemberSourceQuery(new HashSet<int> { token }));
                if (tokenInspection.Value is FindingInspection<MemberSourceObservation>.Failed failed)
                {
                    logger.Log(
                        $"Warning: Failed to resolve source location for {members[0].Name}: "
                        + failed.Error.Reason);
                    continue;
                }

                if (tokenInspection.Value is not FindingInspection<MemberSourceObservation>.Complete tokenComplete)
                    continue;

                ApplySourceLocations(
                    new Dictionary<int, ApiMember[]> { [token] = members },
                    tokenComplete);
            }

            return pdbPath;
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Failed to resolve member source locations for {apiType.FullName}: {ex.Message}");
            return null;
        }
    }

    private static void ApplySourceLocations(
        IReadOnlyDictionary<int, ApiMember[]> membersByToken,
        FindingInspection<MemberSourceObservation>.Complete inspection)
    {
        foreach (var mappings in inspection.Findings
            .Select(static finding => finding.Payload)
            .GroupBy(static mapping => mapping.MetadataToken))
        {
            if (!membersByToken.TryGetValue(mappings.Key, out var members))
                continue;

            // Preserve the legacy resolver's preference for MethodDebugInformation.Document.
            var mapping = mappings
                .OrderByDescending(static candidate => candidate.IsPrimaryDocument)
                .ThenBy(static candidate => candidate.DocumentRowId)
                .First();
            foreach (var member in members)
            {
                member.SourceFilePath = mapping.OriginalPath;
                member.SourceUrl = mapping.ResolvedUrl;
                member.SourceLineNumber = mapping.StartLine;
                member.SourceEndLineNumber = mapping.EndLine;
            }
        }
    }

    private static IEnumerable<ApiMember> GetTargetMembers(ApiType apiType, MemberOptions options)
    {
        var members = apiType.Members
            .Where(ApiMemberSectionDescriptors.IsMethodLike)
            .Where(m => !MemberFilters.IsCompilerGenerated(m.Name));

        if (options.MemberFilter.Count > 0)
            members = members.Where(m => TypeMatcher.MatchesMemberFilter(m.Name, options.MemberFilter));

        if (options.KindFilter.Count > 0)
            members = members.Where(m => options.KindFilter.Contains(m.Kind));

        if (options.UnsafeOnly)
            members = members.Where(m => m.IsUnsafe);

        return members;
    }

}
