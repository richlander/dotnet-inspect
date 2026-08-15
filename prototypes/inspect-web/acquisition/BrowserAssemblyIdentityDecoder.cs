using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ILInspector.Metadata;

namespace InspectWeb.Acquisition;

/// <summary>Decodes only the assembly identity needed to mint a workspace participant.</summary>
[SupportedOSPlatform("browser")]
public static class BrowserAssemblyIdentityDecoder
{
    public static AssemblyReferenceIdentity? Decode(byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        try
        {
            using var peReader = new PEReader(
                ImmutableCollectionsMarshal.AsImmutableArray(image));
            if (!peReader.HasMetadata)
                return null;

            MetadataReader reader = peReader.GetMetadataReader();
            return reader.IsAssembly
                ? AssemblyReferenceIdentity.FromAssemblyDefinition(reader)
                : null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }
}
