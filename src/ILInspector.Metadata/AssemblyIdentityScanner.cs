using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

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
/// </summary>
public sealed record AssemblyIdentityNames(
    string Name,
    ImmutableArray<string> ReferenceNames,
    bool ReferencesComplete = true);

/// <summary>
/// Reads only the <c>Assembly</c> and <c>AssemblyRef</c> tables. This is the cheapest question that
/// can be asked of an image — no method bodies, no signatures, no public-key token derivation — so
/// it suits callers that need to decide whether a fuller, far more expensive analysis of the image
/// is worth starting at all.
/// </summary>
public static class AssemblyIdentityScanner
{
    public static AssemblyIdentityNames Scan(PEReader peReader)
    {
        var reader = peReader.GetMetadataReader();
        string name = reader.IsAssembly
            ? reader.GetString(reader.GetAssemblyDefinition().Name)
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
                references.Add(reader.GetString(reader.GetAssemblyReference(handle).Name));
            }
            catch (BadImageFormatException)
            {
                complete = false;
            }
        }

        return new AssemblyIdentityNames(name, references.ToImmutable(), complete);
    }
}
