namespace ILInspector.SourceLink;

/// <summary>Audits PE/PDB debug information with SourceLink decoration.</summary>
public static class SourceLinkInspector
{
    public static LibraryDebugInfo InspectDll(string dllPath)
    {
        using var source = SourceLinkService.Open(dllPath);
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

        var audit = new LibraryDebugInfo
        {
            HasReproducibleFlag = context.HasReproducibleFlag,
            HasEmbeddedPdb = context.HasEmbeddedPdb,
            HasNormalizedPaths = context.HasNormalizedPaths,
            PdbPath = context.CodeViewPdbPath,
            PdbFormat = context.HasEmbeddedPdb ? "Portable" : null,
            HasSourceLink = context.HasEmbeddedPdb && source.HasSourceLink,
            SourceLinkJson = context.HasEmbeddedPdb ? source.SourceLinkJson : null,
            RepositoryUrl = context.HasEmbeddedPdb ? source.RepositoryUrl : null,
            NonNormalizedPaths = context.NonNormalizedPaths is null
                ? null
                : [.. context.NonNormalizedPaths],
            AssemblyInfo = context.ExtractFullAssemblyInfo(),
        };

        if (audit.SourceLinkJson is not null)
        {
            var map = SourceLinkFetch.SourceLinkResolver.Parse(audit.SourceLinkJson);
            if (map.ParseError is not null)
            {
                audit.HasNormalizedPaths = false;
            }
            else
            {
                foreach (string key in map.DocumentKeys)
                {
                    if (key.StartsWith("/_", StringComparison.Ordinal))
                        continue;
                    audit.HasNormalizedPaths = false;
                    audit.NonNormalizedPaths ??= [];
                    audit.NonNormalizedPaths.Add($"SourceLink: {key}");
                }
            }
        }

        audit.IsDeterministic =
            audit.HasReproducibleFlag && audit.HasNormalizedPaths != false;
        return audit;
    }
}
