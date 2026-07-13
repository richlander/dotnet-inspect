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
            var metadataTokens = targetMembers
                .Select(static member => member.MetadataToken)
                .OfType<int>()
                .ToHashSet();
            var sourceInspection = MetadataFindings.InspectMemberSources(
                service,
                new FindingSubject(assemblyPath, Path.GetFileName(assemblyPath)),
                new MemberSourceQuery(metadataTokens));
            if (sourceInspection.Value is FindingInspection<MemberSourceObservation>.Failed failed)
            {
                logger.Log(
                    $"Warning: Failed to resolve member source locations for {apiType.FullName}: "
                    + failed.Error.Reason);
                return pdbPath;
            }

            if (sourceInspection.Value is not FindingInspection<MemberSourceObservation>.Complete complete)
                return pdbPath;

            var mappingsByToken = complete.Findings
                .Select(static finding => finding.Payload)
                .GroupBy(static mapping => mapping.MetadataToken)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.OrderBy(static mapping => mapping.DocumentRowId).First());

            foreach (var member in targetMembers)
            {
                if (member.MetadataToken is not { } token
                    || !mappingsByToken.TryGetValue(token, out var mapping))
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
