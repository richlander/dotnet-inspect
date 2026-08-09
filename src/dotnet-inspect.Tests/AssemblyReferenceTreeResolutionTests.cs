using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Inspectors;
using DotnetInspector.Output;
using ILInspector.Metadata;
using AssemblyReference = ILInspector.Metadata.AssemblyReference;

namespace DotnetInspector.Tests;

public class AssemblyReferenceTreeResolutionTests
{
    [Fact]
    public void TraversingAssemblyRefName_IsIdentityAndCannotEscapeTheAssemblyDirectory()
    {
        string root = Directory.CreateTempSubdirectory(
            "dotnet-inspect-reference-tree-").FullName;
        try
        {
            string assemblyDirectory = Directory.CreateDirectory(
                Path.Combine(root, "app")).FullName;
            string ownerPath = Path.Combine(assemblyDirectory, "Owner.dll");
            string siblingPath = Path.Combine(assemblyDirectory, "Sibling.dll");
            string payloadPath = Path.Combine(root, "payload.dll");

            File.WriteAllBytes(
                ownerPath,
                BuildAssembly("Owner", "../payload", "Sibling"));
            File.WriteAllBytes(siblingPath, BuildAssembly("Sibling"));
            File.WriteAllBytes(payloadPath, BuildAssembly("Payload"));

            List<AssemblyReference> references =
                AssemblyInspector.ExtractReferences(ownerPath);
            List<AssemblyReferenceNode> nodes =
                LibraryMetadataService.BuildTransitiveReferences(
                    references,
                    ownerPath,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Owner"
                    },
                    new VerboseLogger(enabled: false),
                    deduplicate: true);

            AssemblyReferenceNode traversing =
                Assert.Single(nodes, node => node.Name == "../payload");
            Assert.Null(traversing.Path);
            Assert.Null(traversing.ResolvedFrom);

            AssemblyReferenceNode sibling =
                Assert.Single(nodes, node => node.Name == "Sibling");
            Assert.Equal(siblingPath, sibling.Path);
            Assert.Equal("local", sibling.ResolvedFrom);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] BuildAssembly(
        string assemblyName,
        params string[] references)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        foreach (string reference in references)
        {
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(reference),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        }

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }
}
