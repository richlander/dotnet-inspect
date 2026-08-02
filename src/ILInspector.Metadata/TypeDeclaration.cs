using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>A validated TypeDef metadata token in one assembly candidate.</summary>
public readonly record struct TypeDefinitionToken
{
    TypeDefinitionToken(int value) => Value = value;

    public int Value { get; }

    internal static TypeDefinitionToken FromHandle(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        if (handle.IsNil)
            throw new ArgumentException("A TypeDef token cannot be nil.", nameof(handle));
        reader.GetTypeDefinition(handle);
        return new TypeDefinitionToken(MetadataTokens.GetToken(handle));
    }
}

/// <summary>A validated ExportedType metadata token in one assembly candidate.</summary>
public readonly record struct ExportedTypeToken
{
    ExportedTypeToken(int value) => Value = value;

    public int Value { get; }

    internal static ExportedTypeToken FromHandle(
        MetadataReader reader,
        ExportedTypeHandle handle)
    {
        if (handle.IsNil)
            throw new ArgumentException("An ExportedType token cannot be nil.", nameof(handle));
        reader.GetExportedType(handle);
        return new ExportedTypeToken(MetadataTokens.GetToken(handle));
    }
}

/// <summary>Copied evidence for a File row that carries a module export.</summary>
public sealed class ModuleFileReference
{
    internal ModuleFileReference(
        string name,
        bool containsMetadata,
        ImmutableArray<byte> hash)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        ContainsMetadata = containsMetadata;
        Hash = hash;
    }

    public string Name { get; }
    public bool ContainsMetadata { get; }
    public ImmutableArray<byte> Hash { get; }
}

/// <summary>One declaration competing for an exact metadata type name.</summary>
public abstract class TypeDeclarationCandidate
{
    private protected TypeDeclarationCandidate()
    {
    }

    public sealed class Definition : TypeDeclarationCandidate
    {
        internal Definition(TypeDefinitionToken token) => Token = token;

        public TypeDefinitionToken Token { get; }
    }

    public sealed class Forwarder : TypeDeclarationCandidate
    {
        internal Forwarder(
            ImmutableArray<ExportedTypeToken> declarations,
            AssemblyReferenceIdentity target)
        {
            Declarations = declarations;
            Target = target;
        }

        public ImmutableArray<ExportedTypeToken> Declarations { get; }
        public AssemblyReferenceIdentity Target { get; }
    }

    public sealed class ModuleExport : TypeDeclarationCandidate
    {
        internal ModuleExport(
            ImmutableArray<ExportedTypeToken> declarations,
            ModuleFileReference module)
        {
            Declarations = declarations;
            Module = module;
        }

        public ImmutableArray<ExportedTypeToken> Declarations { get; }
        public ModuleFileReference Module { get; }
    }
}

/// <summary>The closed declaration answer for one exact name in one readable image.</summary>
public abstract class TypeDeclarationResult
{
    private protected TypeDeclarationResult()
    {
    }

    public sealed class Defined : TypeDeclarationResult
    {
        internal Defined(TypeDefinitionToken definition) => Definition = definition;

        public TypeDefinitionToken Definition { get; }
    }

    public sealed class Forwarded : TypeDeclarationResult
    {
        internal Forwarded(
            ImmutableArray<ExportedTypeToken> declarations,
            AssemblyReferenceIdentity target)
        {
            Declarations = declarations;
            Target = target;
        }

        public ImmutableArray<ExportedTypeToken> Declarations { get; }
        public AssemblyReferenceIdentity Target { get; }
    }

    public sealed class ExportedFromModule : TypeDeclarationResult
    {
        internal ExportedFromModule(
            ImmutableArray<ExportedTypeToken> declarations,
            ModuleFileReference module)
        {
            Declarations = declarations;
            Module = module;
        }

        public ImmutableArray<ExportedTypeToken> Declarations { get; }
        public ModuleFileReference Module { get; }
    }

    public sealed class Missing : TypeDeclarationResult
    {
        internal Missing()
        {
        }
    }

    public sealed class Ambiguous : TypeDeclarationResult
    {
        internal Ambiguous(ImmutableArray<TypeDeclarationCandidate> candidates) =>
            Candidates = candidates;

        public ImmutableArray<TypeDeclarationCandidate> Candidates { get; }
    }

    public sealed class Rejected : TypeDeclarationResult
    {
        internal Rejected(MetadataTypeNameFailure rejection) =>
            Rejection = rejection;

        public MetadataTypeNameFailure Rejection { get; }
    }
}
