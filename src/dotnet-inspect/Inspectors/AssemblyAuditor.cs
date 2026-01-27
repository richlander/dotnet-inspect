using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Audits assemblies for SourceLink, deterministic builds, and assembly information.
/// </summary>
public static class AssemblyAuditor
{
    // SourceLink GUID: CC110556-A091-4D38-9FEC-25AB9A351A6A
    private static readonly Guid SourceLinkGuid = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

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
        using FileStream stream = File.OpenRead(dllPath);
        using PEReader peReader = new(stream);

        string relativePath = Path.GetRelativePath(extractPath, dllPath);

        // Handle native binaries (no managed metadata)
        if (!peReader.HasMetadata)
        {
            return AuditNativeBinary(peReader, relativePath);
        }

        var audit = new AssemblyAudit
        {
            FileName = relativePath,
            FileType = "dll"
        };

        // Check debug directory entries
        foreach (var entry in peReader.ReadDebugDirectory())
        {
            if (entry.Type == DebugDirectoryEntryType.Reproducible)
            {
                audit.HasReproducibleFlag = true;
            }

            if (entry.Type == DebugDirectoryEntryType.CodeView)
            {
                var cvData = peReader.ReadCodeViewDebugDirectoryData(entry);
                audit.PdbPath = cvData.Path;

                // Check for normalized paths (deterministic builds use /_/ prefix or just filename)
                if (!cvData.Path.StartsWith("/_/", StringComparison.Ordinal) &&
                    Path.GetDirectoryName(cvData.Path) is string dir && !string.IsNullOrEmpty(dir))
                {
                    audit.HasNormalizedPaths = false;
                    audit.NonNormalizedPaths ??= [];
                    audit.NonNormalizedPaths.Add($"PDB Path: {cvData.Path}");
                }
                else
                {
                    audit.HasNormalizedPaths = true;
                }
            }

            if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                audit.HasEmbeddedPdb = true;
                using MetadataReaderProvider provider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
                MetadataReader reader = provider.GetMetadataReader();

                string? sourceLink = ExtractSourceLinkFromReader(reader);
                if (sourceLink != null)
                {
                    audit.HasSourceLink = true;
                    audit.SourceLinkJson = sourceLink;

                    var (pathsNormalized, nonNormalizedPaths) = CheckSourceLinkPaths(sourceLink);
                    if (!pathsNormalized)
                    {
                        audit.HasNormalizedPaths = false;
                        audit.NonNormalizedPaths ??= [];
                        foreach (var path in nonNormalizedPaths)
                        {
                            audit.NonNormalizedPaths.Add($"SourceLink: {path}");
                        }
                    }

                    // Extract repository URL from SourceLink
                    audit.RepositoryUrl = ExtractRepositoryUrl(sourceLink);
                }
            }
        }

        // Determine overall deterministic status
        audit.IsDeterministic = audit.HasReproducibleFlag && audit.HasNormalizedPaths != false;

        // Extract assembly info
        audit.AssemblyInfo = ExtractAssemblyInfo(peReader);

        // Extract API surface only if requested
        if (includeApi)
        {
            audit.ApiSurface = ApiSurfaceExtractor.Extract(peReader);
        }

        return audit;
    }

    private static AssemblyAudit AuditNativeBinary(PEReader peReader, string relativePath)
    {
        var audit = new AssemblyAudit
        {
            FileName = relativePath,
            FileType = "native"
        };

        var peHeaders = peReader.PEHeaders;
        var coffHeader = peHeaders.CoffHeader;

        // Create AssemblyInfo for native binaries
        var info = new AssemblyInfo
        {
            HasCorHeader = false,
            HasManagedMetadata = false,
            HasILCode = false,
            IsExecutable = peHeaders.IsExe,
            IsDll = peHeaders.IsDll
        };

        // Determine architecture
        info.Architecture = coffHeader.Machine switch
        {
            Machine.I386 => "x86",
            Machine.Amd64 => "x64",
            Machine.Arm => "ARM",
            Machine.Arm64 => "ARM64",
            _ => coffHeader.Machine.ToString()
        };

        // Detect if this is a NativeAOT binary
        bool isNativeAot = DetectNativeAot(peReader);

        info.IsNativeAot = isNativeAot;
        info.CompilationType = isNativeAot ? "NativeAOT" : "Native";

        audit.AssemblyInfo = info;
        return audit;
    }

    private static bool DetectNativeAot(PEReader peReader)
    {
        try
        {
            // Check for debug directory entries that might indicate NativeAOT
            foreach (var entry in peReader.ReadDebugDirectory())
            {
                // NativeAOT binaries may have reproducible/deterministic markers
                if (entry.Type == DebugDirectoryEntryType.Reproducible)
                {
                    // Having reproducible flag without metadata suggests NativeAOT
                    return true;
                }
            }
        }
        catch
        {
            // If we can't analyze, default to unknown
        }

        return false;
    }

    private static AssemblyAudit? AuditStandalonePdb(string pdbPath, string extractPath)
    {
        using FileStream stream = File.OpenRead(pdbPath);

        byte[] header = new byte[4];
        stream.ReadExactly(header, 0, 4);
        stream.Position = 0;

        // Only handle Portable PDBs (BSJB header)
        if (header[0] != 'B' || header[1] != 'S' || header[2] != 'J' || header[3] != 'B')
        {
            return new AssemblyAudit
            {
                FileName = Path.GetRelativePath(extractPath, pdbPath),
                FileType = "pdb",
                PdbFormat = "Windows PDB (legacy)",
                HasSourceLink = false,
                IsDeterministic = false
            };
        }

        using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        MetadataReader reader = provider.GetMetadataReader();

        string relativePath = Path.GetRelativePath(extractPath, pdbPath);
        var audit = new AssemblyAudit
        {
            FileName = relativePath,
            FileType = "pdb",
            PdbFormat = "Portable PDB"
        };

        string? sourceLink = ExtractSourceLinkFromReader(reader);
        if (sourceLink != null)
        {
            audit.HasSourceLink = true;
            audit.SourceLinkJson = sourceLink;

            var (pathsNormalized, nonNormalizedPaths) = CheckSourceLinkPaths(sourceLink);
            audit.HasNormalizedPaths = pathsNormalized;
            if (!pathsNormalized)
            {
                audit.NonNormalizedPaths = nonNormalizedPaths;
            }

            audit.RepositoryUrl = ExtractRepositoryUrl(sourceLink);
            audit.IsDeterministic = pathsNormalized;
        }

        return audit;
    }

    private static string? ExtractSourceLinkFromReader(MetadataReader reader)
    {
        foreach (CustomDebugInformationHandle handle in reader.CustomDebugInformation)
        {
            CustomDebugInformation info = reader.GetCustomDebugInformation(handle);
            Guid kind = reader.GetGuid(info.Kind);

            if (kind == SourceLinkGuid)
            {
                byte[] bytes = reader.GetBlobBytes(info.Value);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
        }

        return null;
    }

    private static (bool isNormalized, List<string> nonNormalizedPaths) CheckSourceLinkPaths(string sourceLink)
    {
        var nonNormalizedPaths = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(sourceLink);
            if (doc.RootElement.TryGetProperty("documents", out var documents))
            {
                foreach (var prop in documents.EnumerateObject())
                {
                    // Deterministic builds should have paths starting with /_
                    if (!prop.Name.StartsWith("/_", StringComparison.Ordinal))
                    {
                        nonNormalizedPaths.Add(prop.Name);
                    }
                }
            }
            return (nonNormalizedPaths.Count == 0, nonNormalizedPaths);
        }
        catch
        {
            return (false, nonNormalizedPaths);
        }
    }

    private static string? ExtractRepositoryUrl(string sourceLink)
    {
        try
        {
            using var doc = JsonDocument.Parse(sourceLink);
            if (doc.RootElement.TryGetProperty("documents", out var documents))
            {
                foreach (var prop in documents.EnumerateObject())
                {
                    string url = prop.Value.GetString() ?? "";
                    // Extract base URL from SourceLink URL pattern
                    if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = Regex.Match(url,
                            @"https://raw\.githubusercontent\.com/([^/]+)/([^/]+)/([^/]+)/");
                        if (match.Success)
                        {
                            return $"https://github.com/{match.Groups[1].Value}/{match.Groups[2].Value}";
                        }
                    }
                    else if (url.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
                             url.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase))
                    {
                        return url.Split('_')[0].TrimEnd('/');
                    }
                    break; // Just use the first document URL
                }
            }
        }
        catch
        {
            // Ignore parse errors
        }
        return null;
    }

    private static AssemblyInfo ExtractAssemblyInfo(PEReader peReader)
    {
        var info = new AssemblyInfo();

        var peHeaders = peReader.PEHeaders;
        var coffHeader = peHeaders.CoffHeader;
        var corHeader = peHeaders.CorHeader;

        info.HasCorHeader = corHeader != null;
        info.HasManagedMetadata = peReader.HasMetadata;

        // Check for ReadyToRun (R2R) compilation
        bool hasR2R = false;
        if (corHeader != null)
        {
            var managedNativeHeader = corHeader.ManagedNativeHeaderDirectory;
            hasR2R = managedNativeHeader.Size > 0;
        }

        bool hasILCode = corHeader != null && peReader.HasMetadata;
        bool isILOnly = corHeader?.Flags.HasFlag(CorFlags.ILOnly) == true;

        info.HasILCode = hasILCode;
        info.IsReadyToRun = hasR2R;

        // Determine compilation type
        if (corHeader == null)
        {
            info.CompilationType = "Native";
            info.IsNativeAot = false;
        }
        else if (hasR2R)
        {
            info.CompilationType = "ReadyToRun";
            info.IsNativeAot = false;
        }
        else if (isILOnly)
        {
            info.CompilationType = "CoreCLR";
            info.IsNativeAot = false;
        }
        else if (hasILCode)
        {
            info.CompilationType = "CoreCLR";
            info.IsNativeAot = false;
        }
        else
        {
            info.CompilationType = "Unknown";
            info.IsNativeAot = false;
        }

        // Determine architecture
        info.Architecture = coffHeader.Machine switch
        {
            Machine.I386 => corHeader?.Flags.HasFlag(CorFlags.Requires32Bit) == true ? "x86" :
                            corHeader?.Flags.HasFlag(CorFlags.Prefers32Bit) == true ? "AnyCPU (32-bit preferred)" : "AnyCPU",
            Machine.Amd64 => "x64",
            Machine.Arm => "ARM",
            Machine.Arm64 => "ARM64",
            _ => coffHeader.Machine.ToString()
        };

        info.IsAnyCpu = coffHeader.Machine == Machine.I386 &&
                        corHeader?.Flags.HasFlag(CorFlags.Requires32Bit) != true;
        info.Prefers32Bit = corHeader?.Flags.HasFlag(CorFlags.Prefers32Bit) == true;
        info.IsSigned = corHeader?.Flags.HasFlag(CorFlags.StrongNameSigned) == true;

        info.IsExecutable = peHeaders.IsExe;
        info.IsDll = peHeaders.IsDll;

        var metadataReader = peReader.GetMetadataReader();

        info.RuntimeVersion = metadataReader.MetadataVersion;
        info.MetadataVersion = metadataReader.GetTableRowCount(TableIndex.Module);

        info.HasUnsafeCode = CheckForUnsafeCode(metadataReader);

        if (metadataReader.IsAssembly)
        {
            var assemblyDef = metadataReader.GetAssemblyDefinition();
            info.AssemblyName = metadataReader.GetString(assemblyDef.Name);
            info.AssemblyVersion = assemblyDef.Version.ToString();
            info.Culture = metadataReader.GetString(assemblyDef.Culture);
            if (string.IsNullOrEmpty(info.Culture))
                info.Culture = "neutral";

            var publicKey = metadataReader.GetBlobBytes(assemblyDef.PublicKey);
            if (publicKey.Length > 0)
            {
                info.PublicKeyToken = Convert.ToHexString(publicKey.TakeLast(8).ToArray()).ToLowerInvariant();
            }
        }

        // Get custom attributes for additional info
        foreach (var attrHandle in metadataReader.CustomAttributes)
        {
            var attr = metadataReader.GetCustomAttribute(attrHandle);
            string? attrName = GetAttributeName(metadataReader, attr);

            if (attrName == "System.Runtime.Versioning.TargetFrameworkAttribute")
            {
                info.TargetFramework = GetAttributeStringValue(metadataReader, attr);
            }
            else if (attrName == "System.Reflection.AssemblyFileVersionAttribute")
            {
                info.FileVersion = GetAttributeStringValue(metadataReader, attr);
            }
            else if (attrName == "System.Reflection.AssemblyInformationalVersionAttribute")
            {
                info.InformationalVersion = GetAttributeStringValue(metadataReader, attr);
            }
        }

        return info;
    }

    private static bool CheckForUnsafeCode(MetadataReader reader)
    {
        foreach (var attrHandle in reader.CustomAttributes)
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            string? attrName = GetAttributeName(reader, attr);
            if (attrName == "System.Security.UnverifiableCodeAttribute")
            {
                return true;
            }
        }
        return false;
    }

    private static string? GetAttributeName(MetadataReader reader, CustomAttribute attr)
    {
        if (attr.Constructor.Kind == HandleKind.MemberReference)
        {
            var memberRef = reader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
            if (memberRef.Parent.Kind == HandleKind.TypeReference)
            {
                var typeRef = reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                string ns = reader.GetString(typeRef.Namespace);
                string name = reader.GetString(typeRef.Name);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }
        }
        else if (attr.Constructor.Kind == HandleKind.MethodDefinition)
        {
            var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor);
            var typeDef = reader.GetTypeDefinition(methodDef.GetDeclaringType());
            string ns = reader.GetString(typeDef.Namespace);
            string name = reader.GetString(typeDef.Name);
            return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
        }
        return null;
    }

    private static string? GetAttributeStringValue(MetadataReader reader, CustomAttribute attr)
    {
        try
        {
            var value = reader.GetBlobReader(attr.Value);
            // Skip prolog (2 bytes)
            value.ReadUInt16();
            // Read the string value
            return value.ReadSerializedString();
        }
        catch
        {
            return null;
        }
    }
}
