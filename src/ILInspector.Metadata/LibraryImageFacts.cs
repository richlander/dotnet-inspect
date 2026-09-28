using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json.Serialization;

namespace ILInspector.Metadata;

/// <summary>How the managed image was compiled.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LibraryCompilationForm>))]
public enum LibraryCompilationForm
{
    [JsonStringEnumMemberName("il")]
    IL,

    [JsonStringEnumMemberName("ready-to-run")]
    ReadyToRun,
}

/// <summary>The PE machine the image targets, when the header names a known one.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LibraryArchitecture>))]
public enum LibraryArchitecture
{
    [JsonStringEnumMemberName("any-cpu")]
    AnyCpu,

    [JsonStringEnumMemberName("any-cpu-prefers-32-bit")]
    AnyCpuPrefers32Bit,

    [JsonStringEnumMemberName("x86")]
    X86,

    [JsonStringEnumMemberName("x64")]
    X64,

    [JsonStringEnumMemberName("arm")]
    Arm,

    [JsonStringEnumMemberName("arm64")]
    Arm64,
}

/// <summary>The PE debug-directory reproducible flag, or why it cannot be read.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LibraryReproducibility>))]
public enum LibraryReproducibility
{
    [JsonStringEnumMemberName("reproducible")]
    Reproducible,

    [JsonStringEnumMemberName("not-reproducible")]
    NotReproducible,

    [JsonStringEnumMemberName("unavailable")]
    Unavailable,
}

public enum AssemblyAttributeTextState
{
    Present,
    Undecodable,
    Conflicting,
}

/// <summary>
/// One assembly-level string attribute as the image carries it. An attribute
/// the image does not carry has no observation.
/// </summary>
public readonly record struct AssemblyAttributeText(
    AssemblyAttributeTextState State,
    string? Value);

/// <summary>
/// Image and Description facts read from one managed image
/// (<c>docs/design/library-inspection-document.md#library-facts</c>).
/// </summary>
public sealed record AssemblyLibraryFactsObservation(
    LibraryCompilationForm Compilation,
    LibraryArchitecture? Architecture,
    bool StrongNameSigned,
    LibraryReproducibility Reproducibility,
    AssemblyAttributeText? TargetFramework,
    AssemblyAttributeText? InformationalVersion,
    AssemblyAttributeText? Company,
    AssemblyAttributeText? Product,
    AssemblyAttributeText? Copyright)
{
    private const string TargetFrameworkAttribute = "System.Runtime.Versioning.TargetFrameworkAttribute";
    private const string InformationalVersionAttribute = "System.Reflection.AssemblyInformationalVersionAttribute";
    private const string CompanyAttribute = "System.Reflection.AssemblyCompanyAttribute";
    private const string ProductAttribute = "System.Reflection.AssemblyProductAttribute";
    private const string CopyrightAttribute = "System.Reflection.AssemblyCopyrightAttribute";

    internal static AssemblyLibraryFactsObservation Read(PEReader peReader)
    {
        PEHeaders headers = peReader.PEHeaders;
        CorHeader? corHeader = headers.CorHeader;
        MetadataReader reader = MetadataFormatAdmission.GetMetadataReader(peReader);

        var texts = new Dictionary<string, AssemblyAttributeText>(StringComparer.Ordinal);
        bool undecodableName = false;
        if (reader.IsAssembly)
        {
            foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                CustomAttribute attribute = reader.GetCustomAttribute(handle);
                string? name;
                try
                {
                    name = AttributeReader.GetAttributeTypeName(reader, attribute.Constructor);
                }
                catch (BadImageFormatException)
                {
                    // An unnameable attribute might be any of the text facts.
                    undecodableName = true;
                    continue;
                }

                if (name is not (TargetFrameworkAttribute or InformationalVersionAttribute
                    or CompanyAttribute or ProductAttribute or CopyrightAttribute))
                {
                    continue;
                }

                AssemblyAttributeText observed = ReadText(reader, attribute);
                texts[name] = texts.TryGetValue(name, out AssemblyAttributeText prior)
                    ? Combine(prior, observed)
                    : observed;
            }
        }

        AssemblyAttributeText? Text(string name) =>
            texts.TryGetValue(name, out AssemblyAttributeText text)
                ? text
                : undecodableName
                    ? new AssemblyAttributeText(AssemblyAttributeTextState.Undecodable, null)
                    : null;

        return new(
            corHeader is not null && corHeader.ManagedNativeHeaderDirectory.Size > 0
                ? LibraryCompilationForm.ReadyToRun
                : LibraryCompilationForm.IL,
            ReadArchitecture(headers.CoffHeader.Machine, corHeader),
            corHeader?.Flags.HasFlag(CorFlags.StrongNameSigned) == true,
            ReadReproducibility(peReader),
            Text(TargetFrameworkAttribute),
            Text(InformationalVersionAttribute),
            Text(CompanyAttribute),
            Text(ProductAttribute),
            Text(CopyrightAttribute));
    }

    private static AssemblyAttributeText ReadText(MetadataReader reader, CustomAttribute attribute)
    {
        try
        {
            BlobReader blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 0x0001)
                return new(AssemblyAttributeTextState.Undecodable, null);
            string? value = blob.ReadSerializedString();
            return value is null
                ? new(AssemblyAttributeTextState.Undecodable, null)
                : new(AssemblyAttributeTextState.Present, value);
        }
        catch (BadImageFormatException)
        {
            return new(AssemblyAttributeTextState.Undecodable, null);
        }
    }

    private static AssemblyAttributeText Combine(AssemblyAttributeText prior, AssemblyAttributeText observed) =>
        (prior.State, observed.State) switch
        {
            (AssemblyAttributeTextState.Undecodable, _) or (_, AssemblyAttributeTextState.Undecodable) =>
                new(AssemblyAttributeTextState.Undecodable, null),
            (AssemblyAttributeTextState.Present, AssemblyAttributeTextState.Present)
                when string.Equals(prior.Value, observed.Value, StringComparison.Ordinal) => prior,
            _ => new(AssemblyAttributeTextState.Conflicting, null),
        };

    private static LibraryArchitecture? ReadArchitecture(Machine machine, CorHeader? corHeader) =>
        machine switch
        {
            // Compilers mark 32-bit-preferred AnyCPU with both flags; x86 has
            // Requires32Bit alone.
            Machine.I386 when corHeader?.Flags.HasFlag(CorFlags.Prefers32Bit) == true =>
                LibraryArchitecture.AnyCpuPrefers32Bit,
            Machine.I386 when corHeader?.Flags.HasFlag(CorFlags.Requires32Bit) == true =>
                LibraryArchitecture.X86,
            Machine.I386 => LibraryArchitecture.AnyCpu,
            Machine.Amd64 => LibraryArchitecture.X64,
            Machine.Arm => LibraryArchitecture.Arm,
            Machine.Arm64 => LibraryArchitecture.Arm64,
            // Reference assemblies and OS-specific ReadyToRun images may carry
            // machine values this vocabulary does not name.
            _ => null,
        };

    private static LibraryReproducibility ReadReproducibility(PEReader peReader)
    {
        try
        {
            foreach (DebugDirectoryEntry entry in peReader.ReadDebugDirectory())
            {
                if (entry.Type == DebugDirectoryEntryType.Reproducible)
                    return LibraryReproducibility.Reproducible;
            }

            return LibraryReproducibility.NotReproducible;
        }
        catch (BadImageFormatException)
        {
            return LibraryReproducibility.Unavailable;
        }
    }
}
