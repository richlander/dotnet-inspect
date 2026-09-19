using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace ILInspector.Metadata;

/// <summary>
/// The simple assembly names an image publishes for itself and for every assembly it references.
/// Names only: no version, culture, or public-key token, because the consumers of this facet decide
/// reachability by simple name.
///
/// <paramref name="ReferencesComplete"/> is <see langword="false"/> when a row of the
/// <c>AssemblyRef</c> table could not be read. The names that were read are still returned, but a
/// consumer deciding reachability must treat the set as unknown rather than absent — a dropped row
/// could have been the one that mattered.
///
/// <see cref="AssemblyIdentityNames.HasAssemblyDefinition"/> distinguishes a metadata module from
/// an assembly. Images with an empty required Module or Assembly name are rejected rather than
/// represented as valid modules.
/// </summary>
public sealed record AssemblyIdentityNames(
    string Name,
    ImmutableArray<string> ReferenceNames,
    bool ReferencesComplete = true)
{
    public bool HasAssemblyDefinition { get; init; } = true;
}

/// <summary>
/// Reads only the <c>Assembly</c> and <c>AssemblyRef</c> tables. This is the cheapest question that
/// can be asked of an image — no method bodies, no signatures, no public-key token derivation — so
/// it suits callers that need to decide whether a fuller, far more expensive analysis of the image
/// is worth starting at all.
/// </summary>
public static class AssemblyIdentityScanner
{
    private static readonly MetadataStringDecoder s_strictUtf8Decoder = new(
        new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true));

    public static AssemblyIdentityNames Scan(string assemblyPath)
        => OwnedResourceCleanup.ReadPeImage(
            () => File.OpenRead(assemblyPath),
            Scan);

    public static AssemblyIdentityNames Scan(PEReader peReader)
    {
        var reader = MetadataFormatAdmission.GetMetadataReader(
            peReader,
            MetadataReaderOptions.None,
            s_strictUtf8Decoder);
        _ = ReadRequiredName(
            reader,
            reader.GetModuleDefinition().Name,
            "Module definition");

        bool hasAssemblyDefinition = reader.IsAssembly;
        string name = hasAssemblyDefinition
            ? ReadRequiredName(
                reader,
                reader.GetAssemblyDefinition().Name,
                "Assembly definition")
            : string.Empty;

        // A malformed row must not discard the identity that was read successfully. Reporting the
        // name with an incomplete reference set lets a consumer keep the assembly under
        // consideration on its own terms instead of losing it entirely.
        var references = ImmutableArray.CreateBuilder<string>(reader.AssemblyReferences.Count);
        bool complete = true;
        foreach (var handle in reader.AssemblyReferences)
        {
            try
            {
                references.Add(ReadRequiredName(
                    reader,
                    reader.GetAssemblyReference(handle).Name,
                    "AssemblyRef row"));
            }
            catch (BadImageFormatException)
            {
                complete = false;
            }
        }

        return new AssemblyIdentityNames(
            name,
            references.ToImmutable(),
            complete)
        {
            HasAssemblyDefinition = hasAssemblyDefinition,
        };
    }

    private static string ReadRequiredName(
        MetadataReader reader,
        StringHandle handle,
        string owner)
    {
        try
        {
            string value = reader.GetString(handle);
            if (value.Length == 0)
            {
                throw new BadImageFormatException(
                    $"The {owner} has an empty required name.");
            }

            return value;
        }
        catch (DecoderFallbackException exception)
        {
            throw new BadImageFormatException(
                $"The {owner} has a required name with invalid UTF-8.",
                exception);
        }
    }
}
