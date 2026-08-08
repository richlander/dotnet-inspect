using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public class SourceForwarderResolutionTests
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

    [Fact]
    public void ResolutionSession_FollowsLegitimateAcquiredSibling()
    {
        string directory = CreateDirectory();
        try
        {
            string facadePath = Path.Combine(directory, "Facade.dll");
            string targetPath = Path.Combine(directory, "Target.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly("Facade", new AssemblyReferenceIdentity(
                    "Target",
                    new Version(1, 0, 0, 0),
                    null,
                    null)));
            File.WriteAllBytes(targetPath, BuildAssembly("Target"));

            using var resolution = new TypeDefinitionResolutionSession(
                facadePath,
                isPlatformAssembly: false);
            TypeResolutionOutcome outcome = resolution.Resolve(TypeName());

            var resolved = Assert.IsType<TypeResolutionOutcome.Resolved>(outcome);
            Assert.Single(resolved.Hops);
            Assert.Equal(
                Path.GetFullPath(targetPath),
                resolved.Definition.Assembly.Assembly.Path);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ResolutionSession_DoesNotTurnHostileAssemblyNameIntoPath()
    {
        string parent = CreateDirectory();
        string directory = Path.Combine(parent, "input");
        Directory.CreateDirectory(directory);
        try
        {
            string facadePath = Path.Combine(directory, "Facade.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly("Facade", new AssemblyReferenceIdentity(
                    "../payload",
                    new Version(1, 0, 0, 0),
                    null,
                    null)));
            File.WriteAllBytes(
                Path.Combine(parent, "payload.dll"),
                BuildAssembly("../payload", definesType: true));

            using var resolution = new TypeDefinitionResolutionSession(
                facadePath,
                isPlatformAssembly: false);
            TypeResolutionOutcome outcome = resolution.Resolve(TypeName());

            Assert.IsType<TypeResolutionOutcome.UnboundBinding>(outcome);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void ApiServices_OpensResolvedDescriptorForForwardedType()
    {
        string directory = CreateDirectory();
        try
        {
            string facadePath = Path.Combine(directory, "Facade.dll");
            string targetPath = Path.Combine(directory, "Target.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly("Facade", new AssemblyReferenceIdentity(
                    "Target",
                    new Version(1, 0, 0, 0),
                    null,
                    null)));
            File.WriteAllBytes(targetPath, BuildAssembly("Target"));
            ApiSurface api =
                AssemblyReader.ExtractApiSurface(facadePath)!;

            ApiServices.ResolveForwardedTypes(
                api,
                facadePath,
                new VerboseLogger(enabled: false),
                includeAll: false);

            ApiType type = Assert.Single(api.Types);
            Assert.True(type.IsForwarded);
            Assert.Equal(Path.GetFullPath(targetPath), type.SourceAssemblyPath);
            Assert.NotNull(type.DefinitionName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ApiServices_DoesNotOpenTraversalTarget()
    {
        string parent = CreateDirectory();
        string directory = Path.Combine(parent, "input");
        Directory.CreateDirectory(directory);
        try
        {
            string facadePath = Path.Combine(directory, "Facade.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly("Facade", new AssemblyReferenceIdentity(
                    "../payload",
                    new Version(1, 0, 0, 0),
                    null,
                    null)));
            File.WriteAllBytes(
                Path.Combine(parent, "payload.dll"),
                BuildAssembly("../payload", definesType: true));
            ApiSurface api =
                AssemblyReader.ExtractApiSurface(facadePath)!;

            ApiServices.ResolveForwardedTypes(
                api,
                facadePath,
                new VerboseLogger(enabled: false),
                includeAll: false);

            Assert.Empty(api.Types);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    static MetadataTypeDefinitionName TypeName() =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", ["Type"])).Name;

    static string CreateDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-source-forwarder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    static byte[] BuildAssembly(
        string assemblyName,
        AssemblyReferenceIdentity? forwardTarget = null,
        bool definesType = false)
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

        if (definesType || forwardTarget is null)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Type"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        }

        if (forwardTarget is not null)
        {
            AssemblyReferenceHandle target = metadata.AddAssemblyReference(
                metadata.GetOrAddString(forwardTarget.Name),
                forwardTarget.Version!,
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
            metadata.AddExportedType(
                TypeAttributes.Public | Forwarder,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Type"),
                target,
                typeDefinitionId: 0);
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
