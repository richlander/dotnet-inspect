using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Analysis.ClassicAsyncFixtures;
using ILInspector.Analysis.MalformedOwnershipFixtures;
using ILInspector.Analysis.UnoptimizedAsyncFixtures;
using ILInspector.CallGraph;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public partial class LibraryBodyIndexTests
{

    static TypeReferenceHandle FirstExternalTypeReference(MetadataReader reader)
    {
        foreach (var handle in reader.TypeReferences)
        {
            if (reader.GetTypeReference(handle).ResolutionScope.Kind == HandleKind.AssemblyReference)
                return handle;
        }

        throw new InvalidOperationException("Expected at least one external TypeRef.");
    }

    static bool AssemblyForwardsType(
        string path,
        string @namespace,
        string name)
    {
        MetadataTypeDefinitionName structuredName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [name]))
            .Name;
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        return peReader.HasMetadata
            && MetadataTypeDeclarationProbe.Probe(
                peReader.GetMetadataReader(),
                structuredName)
            is TypeDeclarationResult.Forwarded;
    }

    static byte[] EmitStandaloneModule(
        string name,
        Guid moduleVersionId)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString(name),
            metadata.GetOrAddGuid(moduleVersionId),
            default,
            default);
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

    static MethodIdentity SyntheticMethod(
        string assemblyName,
        Guid moduleVersionId) =>
        new(
            assemblyName,
            moduleVersionId,
            TypeRef.Definition(
                assemblyName,
                "Fixtures",
                "SyntheticType"),
            "SyntheticMethod",
            [],
            TypeRef.CoreLib("System", "Void"),
            0x06000001,
            IsStatic: true);

    static TypeReferenceHandle FindExternalTypeReference(MetadataReader reader, string ns, string name)
    {
        foreach (var handle in reader.TypeReferences)
        {
            var typeReference = reader.GetTypeReference(handle);
            if (typeReference.ResolutionScope.Kind != HandleKind.AssemblyReference)
                continue;
            if (reader.StringComparer.Equals(typeReference.Namespace, ns)
                && reader.StringComparer.Equals(typeReference.Name, name))
                return handle;
        }

        return default;
    }

    /// <summary>Resolves an identity to the same-named assembly in a directory, with no redirect.</summary>
    sealed class FrameworkDirectoryResolver(string directory) : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
        {
            string candidate = Path.Combine(directory, identity.Name + ".dll");
            return File.Exists(candidate)
                ? ResolvedAssemblyReference.CreateFromPath(
                    candidate,
                    AssemblyResolutionProvenance.Local("test"))
                : null;
        }
    }

    /// <summary>Answers every identity with one assembly, so any forwarder chain cycles.</summary>
    sealed class ConstantResolver(string path) : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
            => ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("test"));
    }

    /// <summary>Records every identity and scope the builder asks for.</summary>
    sealed class RecordingResolver(IAssemblyReferenceResolver inner) : IAssemblyReferenceResolver
    {
        public List<(string Name, AssemblyResolutionScope Scope)> Requests { get; } = [];

        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
        {
            Requests.Add((identity.Name, scope));
            return inner.Resolve(identity, scope);
        }
    }

    sealed class CountingResolver : IAssemblyReferenceResolver
    {
        public int ResolveCalls { get; private set; }

        public ResolvedAssemblyReference? Resolve(AssemblyReferenceIdentity identity, AssemblyResolutionScope scope)
        {
            ResolveCalls++;
            return null;
        }
    }

    sealed class ThirdOpenFailsResolver(
        IAssemblyReferenceResolver inner)
        : IAssemblyReferenceResolver
    {
        readonly Dictionary<
            (AssemblyReferenceIdentity Identity,
                AssemblyResolutionScope Scope),
            ResolvedAssemblyReference?> _cache = [];
        readonly List<Func<int>> _openCounts = [];

        public IEnumerable<int> OpenCounts =>
            _openCounts.Select(read => read());

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
        {
            var key = (identity, scope);
            if (_cache.TryGetValue(
                    key,
                    out ResolvedAssemblyReference? cached))
            {
                return cached;
            }

            ResolvedAssemblyReference? selected =
                inner.Resolve(identity, scope);
            if (selected is null)
            {
                _cache.Add(key, null);
                return null;
            }

            int opens = 0;
            ResolvedAssemblyReference retained =
                ResolvedAssemblyReference.Create(
                    selected.Identity,
                    selected.Path,
                    () =>
                    {
                        if (Interlocked.Increment(ref opens) > 2)
                        {
                            throw new IOException(
                                "The mutable source was reopened.");
                        }
                        return selected.OpenRead();
                    },
                    selected.Provenance,
                    selected.LastWriteTimeUtc);
            _openCounts.Add(() => opens);
            _cache.Add(key, retained);
            return retained;
        }
    }

}
