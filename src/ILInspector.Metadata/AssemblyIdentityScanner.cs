using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>
/// The simple assembly names an image publishes for itself and for every assembly it references.
/// Names only: no version, culture, or public-key token, because the consumers of this facet decide
/// reachability by simple name.
/// </summary>
public sealed record AssemblyIdentityNames(string Name, ImmutableArray<string> ReferenceNames);

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

        var references = ImmutableArray.CreateBuilder<string>(reader.AssemblyReferences.Count);
        foreach (var handle in reader.AssemblyReferences)
            references.Add(reader.GetString(reader.GetAssemblyReference(handle).Name));

        return new AssemblyIdentityNames(name, references.ToImmutable());
    }
}
