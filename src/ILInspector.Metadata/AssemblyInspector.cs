using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace ILInspector.Metadata;

/// <summary>
/// Inspects .NET assemblies to extract PE header info, assembly metadata, and references.
/// </summary>
public static class AssemblyInspector
{
    /// <summary>
    /// Extracts assembly info from a PEReader, including PE headers, CorFlags, architecture,
    /// and optionally assembly references.
    /// </summary>
    public static AssemblyInfo ExtractAssemblyInfo(PEReader peReader, bool includeReferences = false)
    {
        var info = new AssemblyInfo();
        var peHeaders = peReader.PEHeaders;
        var coffHeader = peHeaders.CoffHeader;
        var corHeader = peHeaders.CorHeader;

        info.HasCorHeader = corHeader != null;
        info.HasManagedMetadata = peReader.HasMetadata;

        bool hasR2R = corHeader != null && corHeader.ManagedNativeHeaderDirectory.Size > 0;
        bool hasILCode = corHeader != null && peReader.HasMetadata;
        bool isILOnly = corHeader?.Flags.HasFlag(CorFlags.ILOnly) == true;

        info.HasILCode = hasILCode;
        info.IsReadyToRun = hasR2R;

        if (corHeader == null)
        {
            info.CompilationType = "Native";
        }
        else if (hasR2R)
        {
            info.CompilationType = "ReadyToRun";
        }
        else if (isILOnly || hasILCode)
        {
            info.CompilationType = "CoreCLR";
        }
        else
        {
            info.CompilationType = "Unknown";
        }

        info.Architecture = coffHeader.Machine switch
        {
            Machine.I386 =>
                corHeader?.Flags.HasFlag(CorFlags.Requires32Bit) == true ? "x86" :
                corHeader?.Flags.HasFlag(CorFlags.Prefers32Bit) == true ? "AnyCPU (32-bit preferred)" : "AnyCPU",
            Machine.Amd64 => "x64",
            Machine.Arm => "ARM",
            Machine.Arm64 => "ARM64",
            _ => null // Ref assemblies may have placeholder machine headers
        };

        info.IsAnyCpu = coffHeader.Machine == Machine.I386 &&
                        corHeader?.Flags.HasFlag(CorFlags.Requires32Bit) != true;
        info.Prefers32Bit = corHeader?.Flags.HasFlag(CorFlags.Prefers32Bit) == true;
        info.IsSigned = corHeader?.Flags.HasFlag(CorFlags.StrongNameSigned) == true;

        info.IsExecutable = peHeaders.IsExe;
        info.IsDll = peHeaders.IsDll;

        if (peReader.HasMetadata)
        {
            var metadataReader = peReader.GetMetadataReader();
            info.RuntimeVersion = metadataReader.MetadataVersion;

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
                    var publicKeyHash = SHA1.HashData(publicKey);
                    var publicKeyToken = publicKeyHash[^8..];
                    Array.Reverse(publicKeyToken);
                    info.PublicKeyToken = Convert.ToHexString(publicKeyToken).ToLowerInvariant();
                }
            }

            ExtractCustomAttributes(metadataReader, info);

            info.TypeDefinitionCount = metadataReader.GetTableRowCount(TableIndex.TypeDef);
            info.MethodDefinitionCount = metadataReader.GetTableRowCount(TableIndex.MethodDef);

            if (includeReferences)
            {
                info.References = ExtractReferences(metadataReader);
            }
        }

        return info;
    }

    /// <summary>
    /// Extracts full assembly info including MetadataVersion and HasUnsafeCode.
    /// Used by the package auditor which needs these additional fields.
    /// </summary>
    public static AssemblyInfo ExtractFullAssemblyInfo(PEReader peReader)
    {
        var info = ExtractAssemblyInfo(peReader);

        if (peReader.HasMetadata)
        {
            var metadataReader = peReader.GetMetadataReader();
            info.MetadataVersion = metadataReader.GetTableRowCount(TableIndex.Module);
            info.HasUnsafeCode = CheckForUnsafeCode(metadataReader);
        }

        return info;
    }

    /// <summary>
    /// Creates an AssemblyInfo for a native (non-managed) binary.
    /// </summary>
    public static AssemblyInfo CreateNativeInfo(PEReader peReader)
    {
        var peHeaders = peReader.PEHeaders;
        var coffHeader = peHeaders.CoffHeader;

        return new AssemblyInfo
        {
            HasCorHeader = false,
            HasManagedMetadata = false,
            HasILCode = false,
            IsExecutable = peHeaders.IsExe,
            IsDll = peHeaders.IsDll,
            Architecture = coffHeader.Machine switch
            {
                Machine.I386 => "x86",
                Machine.Amd64 => "x64",
                Machine.Arm => "ARM",
                Machine.Arm64 => "ARM64",
                _ => coffHeader.Machine.ToString()
            },
            CompilationType = "Native"
        };
    }

    /// <summary>
    /// Detects if a native binary is NativeAOT compiled.
    /// </summary>
    public static bool DetectNativeAot(PEReader peReader)
    {
        try
        {
            foreach (var entry in peReader.ReadDebugDirectory())
            {
                if (entry.Type == DebugDirectoryEntryType.Reproducible)
                {
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

    /// <summary>
    /// Checks if a PDB file is a Windows PDB (MSF format).
    /// </summary>
    public static bool IsWindowsPdb(string pdbPath)
    {
        try
        {
            using var stream = File.OpenRead(pdbPath);
            byte[] header = new byte[4];
            if (stream.Read(header, 0, 4) < 4)
                return false;

            return header[0] == 'M' && header[1] == 'i' && header[2] == 'c' && header[3] == 'r';
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a PDB file is a Portable PDB.
    /// </summary>
    public static bool IsPortablePdb(string pdbPath)
    {
        try
        {
            using var stream = File.OpenRead(pdbPath);
            byte[] header = new byte[4];
            if (stream.Read(header, 0, 4) < 4)
                return false;

            return header[0] == 'B' && header[1] == 'S' && header[2] == 'J' && header[3] == 'B';
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts assembly references from a file path.
    /// </summary>
    public static List<AssemblyReference> ExtractReferences(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata)
            return [];

        var metadataReader = peReader.GetMetadataReader();
        return ExtractReferences(metadataReader) ?? [];
    }

    /// <summary>
    /// Extracts assembly references and company name in a single pass.
    /// </summary>
    public static (List<AssemblyReference> References, string? Company) ExtractReferencesAndCompany(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata)
            return ([], null);

        var metadataReader = peReader.GetMetadataReader();
        var refs = ExtractReferences(metadataReader) ?? [];
        var company = ExtractCompanyAttribute(metadataReader);
        return (refs, company);
    }

    private static string? ExtractCompanyAttribute(MetadataReader metadataReader)
    {
        foreach (var attrHandle in metadataReader.CustomAttributes)
        {
            var attr = metadataReader.GetCustomAttribute(attrHandle);
            string? attrName = GetAttributeName(metadataReader, attr);
            if (attrName == "System.Reflection.AssemblyCompanyAttribute")
            {
                return GetAttributeStringValue(metadataReader, attr);
            }
        }
        return null;
    }

    private static List<AssemblyReference>? ExtractReferences(MetadataReader metadataReader)
    {
        List<AssemblyReference> references = [];
        foreach (var refHandle in metadataReader.AssemblyReferences)
        {
            var assemblyRef = metadataReader.GetAssemblyReference(refHandle);
            var name = metadataReader.GetString(assemblyRef.Name);
            var version = assemblyRef.Version.ToString();
            var culture = metadataReader.GetString(assemblyRef.Culture);
            if (string.IsNullOrEmpty(culture))
                culture = "neutral";

            string? publicKeyToken = null;
            var pkToken = metadataReader.GetBlobBytes(assemblyRef.PublicKeyOrToken);
            if (pkToken.Length > 0)
            {
                publicKeyToken = Convert.ToHexString(pkToken).ToLowerInvariant();
            }

            references.Add(new AssemblyReference(name, version, culture, publicKeyToken));
        }

        return references.Count > 0 ? references : null;
    }

    private static void ExtractCustomAttributes(MetadataReader metadataReader, AssemblyInfo info)
    {
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
            else if (attrName == "System.Reflection.AssemblyProductAttribute")
            {
                info.Product = GetAttributeStringValue(metadataReader, attr);
            }
            else if (attrName == "System.Reflection.AssemblyCompanyAttribute")
            {
                info.Company = GetAttributeStringValue(metadataReader, attr);
            }
            else if (attrName == "System.Reflection.AssemblyCopyrightAttribute")
            {
                info.Copyright = GetAttributeStringValue(metadataReader, attr);
            }
            else if (attrName == "System.Reflection.AssemblyDescriptionAttribute")
            {
                info.Description = GetAttributeStringValue(metadataReader, attr);
            }
        }
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

    internal static string? GetAttributeName(MetadataReader reader, CustomAttribute attr)
        => AttributeReader.GetAttributeTypeName(reader, attr.Constructor);

    internal static string? GetAttributeStringValue(MetadataReader reader, CustomAttribute attr)
    {
        try
        {
            var value = reader.GetBlobReader(attr.Value);
            value.ReadUInt16(); // Skip prolog
            return value.ReadSerializedString();
        }
        catch
        {
            return null;
        }
    }
}
