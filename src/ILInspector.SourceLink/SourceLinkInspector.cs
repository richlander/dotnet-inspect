namespace ILInspector.SourceLink;

/// <summary>Audits PE/PDB debug information with SourceLink decoration.</summary>
public static class SourceLinkInspector
{
    public static LibraryDebugInfo InspectDll(string dllPath)
    {
        using var source = SourceLinkService.Open(dllPath);
        return Inspect(source);
    }

    /// <summary>Audits an already-open assembly and its current PDB state.</summary>
    public static LibraryDebugInfo Inspect(SourceLinkService source)
    {
        var context = source.Context;

        if (!context.HasMetadata)
        {
            var native = context.CreateNativeInfo();
            return new LibraryDebugInfo
            {
                AssemblyInfo = native,
                IsNativeAot = native.IsNativeAot,
            };
        }

        SourceLinkDebugAudit debugAudit = InspectDebugInformation(source);
        var audit = new LibraryDebugInfo
        {
            HasReproducibleFlag = context.HasReproducibleFlag,
            HasEmbeddedPdb = context.HasEmbeddedPdb,
            HasNormalizedPaths = debugAudit.HasNormalizedPaths,
            PdbPath = context.CodeViewPdbPath,
            PdbFormat = context.PdbFormat,
            HasSourceLink = debugAudit.SourceLinkMap.IsPresent,
            SourceLinkJson = source.SourceLinkJson,
            SourceLinkMap = debugAudit.SourceLinkMap,
            RepositoryUrl = source.RepositoryUrl,
            NonNormalizedPaths = debugAudit.NonNormalizedPaths is null
                ? null
                : [.. debugAudit.NonNormalizedPaths],
            AssemblyInfo = context.ExtractFullAssemblyInfo(),
        };

        audit.IsDeterministic =
            audit.HasReproducibleFlag && audit.HasNormalizedPaths != false;
        return audit;
    }

    /// <summary>
    /// Combines PE/PDB path normalization with SourceLink map parse and entry facts.
    /// </summary>
    public static SourceLinkDebugAudit InspectDebugInformation(
        SourceLinkService source)
    {
        var context = source.Context;
        SourceLinkMapInspection map = source.SourceLinkMap;
        bool? hasNormalizedPaths = context.HasNormalizedPaths;
        List<string>? nonNormalizedPaths = context.NonNormalizedPaths is null
            ? null
            : [.. context.NonNormalizedPaths];

        if (map.Error is not null)
            hasNormalizedPaths = false;

        foreach (string key in map.DocumentKeys)
        {
            if (key.StartsWith("/_", StringComparison.Ordinal))
                continue;
            hasNormalizedPaths = false;
            nonNormalizedPaths ??= [];
            nonNormalizedPaths.Add($"SourceLink: {key}");
        }

        return new SourceLinkDebugAudit(
            map,
            hasNormalizedPaths,
            nonNormalizedPaths);
    }
}
