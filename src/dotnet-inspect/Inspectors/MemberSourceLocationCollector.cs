using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
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

            foreach (var member in GetTargetMembers(apiType, options))
            {
                var typeName = string.IsNullOrWhiteSpace(member.DeclaringType)
                    ? apiType.FullName
                    : member.DeclaringType!;
                var overloadIndex = GetSourceOverloadIndex(apiType, member, options);
                if (overloadIndex < 0)
                    continue;

                var publicOnly = !options.IncludeAll
                                 && member.Kind != "explicit-interface-implementation";
                var info = service.ResolveMethodSource(typeName, member.Name, overloadIndex, publicOnly);
                if (info is null)
                    continue;

                member.SourceFilePath = info.FilePath;
                member.SourceUrl = info.SourceUrl;
                member.SourceLineNumber = info.StartLine;
                member.SourceEndLineNumber = info.EndLine;
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

    private static int GetSourceOverloadIndex(ApiType apiType, ApiMember member, MemberOptions options)
    {
        if (member.DeclaringOverloadIndex is { } declaringOverload)
            return declaringOverload - 1;

        if (options.OverloadIndex is { } selectedOverload && apiType.Members.Count == 1)
            return selectedOverload - 1;

        var sameName = apiType.Members
            .Where(m => string.Equals(m.Name, member.Name, StringComparison.Ordinal))
            .Where(ApiMemberSectionDescriptors.IsMethodLike)
            .ToList();

        return sameName.IndexOf(member);
    }
}
