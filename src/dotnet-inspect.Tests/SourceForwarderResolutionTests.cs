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
    public void ResolutionSession_RejectsSourceChangedAfterInventory()
    {
        string directory = CreateDirectory();
        try
        {
            string rootPath = Path.Combine(
                directory,
                "Root.dll");
            File.WriteAllBytes(
                rootPath,
                BuildAssembly("Root"));
            byte[] inventoried =
                BuildAssembly(
                    "Changing",
                    definesType: true,
                    typeName: "First");
            byte[] changed =
                BuildAssembly(
                    "Changing",
                    definesType: true,
                    typeName: "Second");
            int opens = 0;
            var source = ResolvedAssemblyReference.Create(
                ReadIdentity(inventoried),
                path: null,
                openRead: () => new MemoryStream(
                    Interlocked.Increment(ref opens) == 1
                        ? inventoried
                        : changed,
                    writable: false),
                AssemblyResolutionProvenance.Local(
                    nameof(
                        ResolutionSession_RejectsSourceChangedAfterInventory)));

            using var resolution =
                new TypeDefinitionResolutionSession(
                    rootPath,
                    isPlatformAssembly: false);
            ApiSurface? surface =
                resolution.ExtractApiSurface(source);

            Assert.Null(surface);
            Assert.Equal(2, opens);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
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
    public void ApiServices_PropagatesForwardedTargetInspectionFailures()
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
                    null),
                    typeName: "Type`1"));
            File.WriteAllBytes(
                targetPath,
                BuildTargetWithMissingConstraint());
            ApiSurface api =
                AssemblyReader.ExtractApiSurface(facadePath)!;

            ApiServices.ResolveForwardedTypes(
                api,
                facadePath,
                new VerboseLogger(enabled: false),
                includeAll: false);

            Assert.True(Assert.Single(api.Types).IsForwarded);
            ApiSurfaceInspectionFailure failure =
                Assert.Single(api.InspectionFailures);
            Assert.Contains(
                "N.ForwardedBase",
                failure.Detail,
                StringComparison.Ordinal);
            Assert.Single(
                api.ConstraintResolutionFailuresBySubject);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ApiServices_ClassifiesConstraintsOnForwardedType()
    {
        string directory = CreateDirectory();
        try
        {
            string targetSource =
                typeof(CrossAssemblyConstraintRestatementFixture)
                    .Assembly.Location;
            string dependencySource =
                typeof(DotnetInspector.Fixtures.ExternalConstraintClass)
                    .Assembly.Location;
            string targetPath = Path.Combine(
                directory,
                Path.GetFileName(targetSource));
            string dependencyPath = Path.Combine(
                directory,
                Path.GetFileName(dependencySource));
            File.Copy(targetSource, targetPath);
            File.Copy(dependencySource, dependencyPath);

            AssemblyName targetName =
                typeof(CrossAssemblyConstraintRestatementFixture)
                    .Assembly.GetName();
            string facadePath = Path.Combine(directory, "Facade.dll");
            File.WriteAllBytes(
                facadePath,
                BuildAssembly(
                    "Facade",
                    new AssemblyReferenceIdentity(
                        targetName.Name!,
                        targetName.Version,
                        targetName.CultureName,
                        null),
                    typeNamespace:
                        typeof(CrossAssemblyConstraintRestatementFixture)
                            .Namespace!,
                    typeName:
                        nameof(
                            CrossAssemblyConstraintRestatementFixture)));
            ApiSurface api =
                AssemblyReader.ExtractApiSurface(facadePath)!;

            ApiServices.ResolveForwardedTypes(
                api,
                facadePath,
                new VerboseLogger(enabled: false),
                includeAll: false);

            ApiType type = Assert.Single(api.Types);
            Assert.True(type.IsForwarded);
            AssertKind(
                nameof(
                    CrossAssemblyConstraintRestatementFixture
                        .ClassConstraint),
                TypeParameterTypeKind.ReferenceType);
            AssertKind(
                nameof(
                    CrossAssemblyConstraintRestatementFixture
                        .InterfaceConstraint),
                TypeParameterTypeKind.NeitherReferenceNorValue);

            void AssertKind(
                string methodName,
                TypeParameterTypeKind expected)
            {
                ApiMember member = Assert.Single(
                    type.Members,
                    candidate => candidate.Name == methodName);
                Assert.Equal(
                    expected,
                    Assert.Single(
                        member.SignatureModel!.TypeParameters)
                        .TypeKind);
            }
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

    static AssemblyReferenceIdentity ReadIdentity(
        byte[] image)
    {
        using var pe =
            new PEReader(new MemoryStream(image));
        return AssemblyReferenceIdentity
            .FromAssemblyDefinition(
                pe.GetMetadataReader());
    }

    static byte[] BuildAssembly(
        string assemblyName,
        AssemblyReferenceIdentity? forwardTarget = null,
        bool definesType = false,
        string typeNamespace = "N",
        string typeName = "Type")
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
                metadata.GetOrAddString(typeNamespace),
                metadata.GetOrAddString(typeName),
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
                metadata.GetOrAddString(typeNamespace),
                metadata.GetOrAddString(typeName),
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

    static byte[] BuildTargetWithMissingConstraint()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Target.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Target"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        AssemblyReferenceHandle missingAssembly =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Missing"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        TypeReferenceHandle forwardedBase =
            metadata.AddTypeReference(
                missingAssembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("ForwardedBase"));
        TypeReferenceHandle unrelatedBase =
            metadata.AddTypeReference(
                missingAssembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("UnrelatedBase"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle forwarded =
            metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Type`1"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle unrelated =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Unrelated`1"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        AddMissingConstraint(
            forwarded,
            "T",
            forwardedBase);
        AddMissingConstraint(
            unrelated,
            "U",
            unrelatedBase);

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();

        void AddMissingConstraint(
            TypeDefinitionHandle type,
            string name,
            TypeReferenceHandle missingBase)
        {
            GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                type,
                GenericParameterAttributes.None,
                metadata.GetOrAddString(name),
                index: 0);
            metadata.AddGenericParameterConstraint(
                parameter,
                missingBase);
        }
    }
}
