using DotnetInspector.Metadata;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Audits assemblies for SourceLink, deterministic builds, and assembly information.
/// Delegates PE/metadata inspection to AssemblyInspector in the Metadata library.
/// </summary>
public static class AssemblyAuditor
{
    public static void AuditAssemblies(string extractPath, InspectionResult result, bool includeApi = false)
    {
        // Find all DLL files
        string[] dllFiles = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);

        // Find standalone PDB files
        string[] pdbFiles = Directory.GetFiles(extractPath, "*.pdb", SearchOption.AllDirectories);

        foreach (string dllFile in dllFiles)
        {
            try
            {
                var audit = AuditDll(dllFile, extractPath, includeApi);
                if (audit != null)
                {
                    result.AssemblyAudits ??= [];
                    result.AssemblyAudits.Add(audit);
                }
            }
            catch
            {
                // Skip files that can't be read
            }
        }

        // Audit standalone PDBs
        foreach (string pdbFile in pdbFiles)
        {
            try
            {
                var audit = AuditStandalonePdb(pdbFile, extractPath);
                if (audit != null)
                {
                    result.AssemblyAudits ??= [];
                    result.AssemblyAudits.Add(audit);
                }
            }
            catch
            {
                // Skip files that can't be read
            }
        }

        // Calculate overall audit summary
        if (result.AssemblyAudits is { Count: > 0 })
        {
            int total = result.AssemblyAudits.Count;
            int deterministic = result.AssemblyAudits.Count(a => a.IsDeterministic);
            int hasSourceLink = result.AssemblyAudits.Count(a => a.HasSourceLink);
            int hasEmbeddedPdb = result.AssemblyAudits.Count(a => a.HasEmbeddedPdb);

            result.AuditSummary = new AuditSummary
            {
                TotalAssemblies = total,
                DeterministicCount = deterministic,
                SourceLinkCount = hasSourceLink,
                EmbeddedPdbCount = hasEmbeddedPdb,
                AllDeterministic = deterministic == total,
                AllHaveSourceLink = hasSourceLink == total
            };
        }
    }

    private static AssemblyAudit? AuditDll(string dllPath, string extractPath, bool includeApi)
    {
        string relativePath = Path.GetRelativePath(extractPath, dllPath);

        var auditInfo = AssemblyInspector.AuditDll(dllPath);

        // Determine if this is a native binary
        bool isNative = auditInfo.AssemblyInfo?.HasManagedMetadata == false;

        var audit = new AssemblyAudit
        {
            FileName = relativePath,
            FileType = isNative ? "native" : "dll",
            HasReproducibleFlag = auditInfo.HasReproducibleFlag,
            HasEmbeddedPdb = auditInfo.HasEmbeddedPdb,
            HasNormalizedPaths = auditInfo.HasNormalizedPaths,
            PdbPath = auditInfo.PdbPath,
            HasSourceLink = auditInfo.HasSourceLink,
            SourceLinkJson = auditInfo.SourceLinkJson,
            RepositoryUrl = auditInfo.RepositoryUrl,
            NonNormalizedPaths = auditInfo.NonNormalizedPaths,
            IsDeterministic = auditInfo.IsDeterministic,
            AssemblyInfo = auditInfo.AssemblyInfo
        };

        if (auditInfo.PdbFormat != null)
            audit.PdbFormat = auditInfo.PdbFormat;

        // Determine reason for missing SourceLink
        if (!isNative && !audit.HasSourceLink && !audit.HasEmbeddedPdb && audit.PdbPath != null)
        {
            audit.SourceLinkUnavailableReason = "PDB not in package";
        }

        // Extract API surface only if requested
        if (includeApi && !isNative)
        {
            audit.ApiSurface = AssemblyReader.ExtractApiSurface(dllPath);
        }

        return audit;
    }

    private static AssemblyAudit? AuditStandalonePdb(string pdbPath, string extractPath)
    {
        using FileStream stream = File.OpenRead(pdbPath);

        var auditInfo = AssemblyInspector.AuditStandalonePdb(stream);
        if (auditInfo == null)
            return null;

        string relativePath = Path.GetRelativePath(extractPath, pdbPath);

        return new AssemblyAudit
        {
            FileName = relativePath,
            FileType = "pdb",
            PdbFormat = auditInfo.PdbFormat,
            HasSourceLink = auditInfo.HasSourceLink,
            SourceLinkJson = auditInfo.SourceLinkJson,
            HasNormalizedPaths = auditInfo.HasNormalizedPaths,
            NonNormalizedPaths = auditInfo.NonNormalizedPaths,
            RepositoryUrl = auditInfo.RepositoryUrl,
            IsDeterministic = auditInfo.IsDeterministic
        };
    }
}
