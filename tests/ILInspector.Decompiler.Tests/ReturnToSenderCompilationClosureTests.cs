using ILInspector.DecompilerHarness;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "RoundTrip")]
public sealed class ReturnToSenderCompilationClosureTests
{
    [Fact]
    public void HostApplicationDependencyDoesNotBecomePlatformAuthority()
    {
        string hostPath = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Single(path => Path.GetFileName(path) == "Markout.dll");
        AssemblyReferenceIdentity hostIdentity = Identity(hostPath);
        string directory = TempDirectory();
        try
        {
            string dependencyPath = Compile(
                $$"""
                [assembly: System.Reflection.AssemblyVersion("{{hostIdentity.Version}}")]
                namespace RtsHostDependency;
                public sealed class Marker;
                """,
                directory,
                hostIdentity.Name,
                excludedReferences: [hostIdentity.Name]);
            string targetPath = Compile(
                """
                public sealed class Fixture
                {
                    public RtsHostDependency.Marker Value => new();
                }
                """,
                directory,
                "Fixture",
                [MetadataReference.CreateFromFile(dependencyPath)],
                [hostIdentity.Name]);

            using ReturnToSender.CompilationClosure closure =
                ReturnToSender.CreateCompilationClosure(targetPath);

            Assert.True(closure.Use(context =>
                context.CompilerReferences.Any(reference =>
                    string.Equals(
                        reference.FilePath,
                        dependencyPath,
                        StringComparison.Ordinal))));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PlatformSelectionsContributeTheirReferenceClosure()
    {
        string directory = TempDirectory();
        try
        {
            string targetPath = Compile(
                """
                public sealed class UsesConsole
                {
                    public void Write() => System.Console.WriteLine("hello");
                }
                """,
                directory,
                "UsesConsole");

            using ReturnToSender.CompilationClosure closure =
                ReturnToSender.CreateCompilationClosure(targetPath);

            Assert.True(closure.Use(context =>
            {
                string[] selectedNames =
                    [.. context.CompilerReferences.Select(reference =>
                        AssemblyReferenceIdentity.FromAssemblyDefinition(
                            Assert.Single(
                                Assert.IsType<AssemblyMetadata>(
                                    reference.GetMetadata()).GetModules())
                                .GetMetadataReader()).Name)];
                Assert.Contains("System.Console", selectedNames);
                Assert.Contains("System.Runtime", selectedNames);
                return true;
            }));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UncheckedPlatformIdentityCollisionRemainsAmbiguous()
    {
        string directory = TempDirectory();
        try
        {
            string platformPath = typeof(System.Text.Json.JsonSerializer).Assembly.Location;
            File.Copy(
                platformPath,
                Path.Combine(directory, "RenamedPlatformCopy.dll"));
            string targetPath = Compile(
                """
                public sealed class UsesJson
                {
                    public string Serialize() =>
                        System.Text.Json.JsonSerializer.Serialize(1);
                }
                """,
                directory,
                "UsesJson");

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => ReturnToSender.CreateCompilationClosure(targetPath));

            Assert.Contains(
                nameof(CompileReferenceFailureKind.ReferenceSelectionAmbiguous),
                error.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DifferentVersionPlatformFamilyCandidateRemainsExact()
    {
        string directory = TempDirectory();
        try
        {
            string platformPath = typeof(System.Text.Json.JsonSerializer).Assembly.Location;
            AssemblyReferenceIdentity platformIdentity = Identity(platformPath);
            Assert.NotNull(platformIdentity.Version);
            Assert.True(platformIdentity.Version.Major > 0);
            AssemblyReferenceIdentity olderIdentity = platformIdentity with
            {
                Version = new Version(platformIdentity.Version.Major - 1, 0, 0, 0),
            };
            File.WriteAllBytes(
                Path.Combine(directory, "RenamedOlderPlatform.dll"),
                BuildIdentityOnlyAssemblyImage(platformPath, olderIdentity.Version));
            string targetPath = Compile(
                """
                public sealed class UsesJson
                {
                    public string Serialize() =>
                        System.Text.Json.JsonSerializer.Serialize(1);
                }
                """,
                directory,
                "UsesJson");

            using ReturnToSender.CompilationClosure closure =
                ReturnToSender.CreateCompilationClosure(targetPath);

            Assert.True(closure.Use(context =>
                context.CompilerReferences.Any(reference =>
                    olderIdentity.IsEquivalentTo(
                        AssemblyReferenceIdentity.FromAssemblyDefinition(
                            Assert.Single(
                                Assert.IsType<AssemblyMetadata>(
                                    reference.GetMetadata()).GetModules())
                                .GetMetadataReader())))));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MatchingDependencyManifestSuppressesDuplicateSiblingDiscovery()
    {
        string directory = TempDirectory();
        try
        {
            string dependencyPath = Compile(
                "public sealed class RtsManifestDependency;",
                directory,
                "RtsManifestDependency");
            string targetPath = Compile(
                """
                public sealed class Fixture
                {
                    public RtsManifestDependency Value => new();
                }
                """,
                directory,
                "RtsManifestApp",
                [MetadataReference.CreateFromFile(dependencyPath)]);
            File.WriteAllText(
                Path.ChangeExtension(targetPath, ".deps.json"),
                """
                {
                  "targets": {
                    "net11.0": {
                      "RtsManifestDependency/1.0.0": {
                        "runtime": {
                          "RtsManifestDependency.dll": {
                            "localPath": "RtsManifestDependency.dll"
                          }
                        }
                      }
                    }
                  },
                  "libraries": {
                    "RtsManifestDependency/1.0.0": {}
                  }
                }
                """);

            using ReturnToSender.CompilationClosure closure =
                ReturnToSender.CreateCompilationClosure(targetPath);

            Assert.Equal(1, closure.Use(context =>
                context.CompilerReferences.Count(reference =>
                    string.Equals(
                        Path.GetFileName(reference.FilePath),
                        "RtsManifestDependency.dll",
                        StringComparison.Ordinal))));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static string Compile(
        string source,
        string directory,
        string assemblyName,
        IReadOnlyList<MetadataReference>? additionalReferences = null,
        string[]? excludedReferences = null)
    {
        string path = Path.Combine(directory, $"{assemblyName}.dll");
        IEnumerable<MetadataReference> references =
            RoslynTestReferences.TrustedPlatform
                .Where(reference =>
                    reference is not PortableExecutableReference portable
                    || portable.FilePath is null
                    || excludedReferences?.Contains(
                        Path.GetFileNameWithoutExtension(portable.FilePath)) != true)
                .Concat(additionalReferences ?? []);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Disable));
        var emit = compilation.Emit(path);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return path;
    }

    static AssemblyReferenceIdentity Identity(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            pe.GetMetadataReader());
    }

    static byte[] BuildIdentityOnlyAssemblyImage(
        string assemblyPath,
        Version version)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var reader = new PEReader(stream);
        MetadataReader source = reader.GetMetadataReader();
        AssemblyDefinition assembly = source.GetAssemblyDefinition();
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString(
                source.GetString(assembly.Name) + ".dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(source.GetString(assembly.Name)),
            version,
            assembly.Culture.IsNil
                ? default
                : metadata.GetOrAddString(source.GetString(assembly.Culture)),
            assembly.PublicKey.IsNil
                ? default
                : metadata.GetOrAddBlob(source.GetBlobBytes(assembly.PublicKey)),
            assembly.Flags,
            assembly.HashAlgorithm);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static string TempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"return-to-sender-closure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
