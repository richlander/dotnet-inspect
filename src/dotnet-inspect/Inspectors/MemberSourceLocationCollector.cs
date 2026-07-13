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

            foreach (var member in targetMembers)
            {
                if (member.MetadataToken is not { } token)
                    continue;

                var sourceInspection = MetadataFindings.InspectMemberSources(
                    service,
                    subject,
                    new MemberSourceQuery(new HashSet<int> { token }));
                if (sourceInspection.Value is FindingInspection<MemberSourceObservation>.Failed failed)
                {
                    logger.Log(
                        $"Warning: Failed to resolve source location for {member.Name}: "
                        + failed.Error.Reason);
                    continue;
                }

                if (sourceInspection.Value is not FindingInspection<MemberSourceObservation>.Complete complete)
                    continue;

                var mapping = complete.Findings
                    .Select(static finding => finding.Payload)
                    .OrderBy(static candidate => candidate.DocumentRowId)
                    .FirstOrDefault();
                if (mapping is null)
                    continue;

                member.SourceFilePath = mapping.OriginalPath;
                member.SourceUrl = mapping.ResolvedUrl;
                member.SourceLineNumber = mapping.StartLine;
                member.SourceEndLineNumber = mapping.EndLine;
            }

            return pdbPath;
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Failed to resolve member source locations for {apiType.FullName}: {ex.Message}");
            return null;
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
