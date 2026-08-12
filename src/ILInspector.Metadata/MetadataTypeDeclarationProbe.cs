using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>Answers how one readable metadata image declares one exact type name.</summary>
public static class MetadataTypeDeclarationProbe
{
    public static TypeDeclarationResult Probe(
        MetadataReader reader,
        MetadataTypeDefinitionName name)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(name);

        var candidates = new List<PendingCandidate>();
        var forwarders =
            new Dictionary<AssemblyReferenceIdentity, PendingForwarder>();
        bool declaresCoreLibraryRoot = false;
        bool canDeclareCoreLibraryRoot =
            reader.AssemblyReferences.Count == 0;

        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            MetadataTypeDefinitionNameMatch match =
                MetadataTypeDefinitionNameReader.Matches(
                    reader,
                    handle,
                    name,
                    out MetadataTypeNameFailure? failure);
            if (match == MetadataTypeDefinitionNameMatch.Rejected)
                return new TypeDeclarationResult.Rejected(failure!);

            try
            {
                TypeDefinition definition =
                    reader.GetTypeDefinition(handle);
                declaresCoreLibraryRoot |=
                    canDeclareCoreLibraryRoot
                    && IsCoreLibraryRoot(reader, definition);
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException
                    or IndexOutOfRangeException)
            {
                return new TypeDeclarationResult.Rejected(
                    MetadataTypeNameFailure.Malformed(
                        handle,
                        ex.Message));
            }
            if (match == MetadataTypeDefinitionNameMatch.Match)
            {
                candidates.Add(
                    new PendingDefinition(
                        handle,
                        TypeDefinitionToken.FromHandle(reader, handle)));
            }
        }

        foreach (ExportedTypeHandle handle in reader.ExportedTypes)
        {
            MetadataTypeDefinitionNameMatch match =
                MetadataTypeDefinitionNameReader.Matches(
                    reader,
                    handle,
                    name,
                    out MetadataTypeNameFailure? failure);
            if (match == MetadataTypeDefinitionNameMatch.Rejected)
                return new TypeDeclarationResult.Rejected(failure!);
            if (match == MetadataTypeDefinitionNameMatch.NoMatch)
                continue;

            if (!TryReadExportedCandidate(
                    reader,
                    handle,
                    out TypeDeclarationCandidate? candidate,
                    out failure))
            {
                return new TypeDeclarationResult.Rejected(failure!);
            }

            AddCandidate(candidates, forwarders, candidate!);
        }

        return Complete(
            reader,
            candidates,
            declaresCoreLibraryRoot);
    }

    internal static Index CreateIndex(MetadataReader reader) =>
        new(reader);

    internal sealed class Index
    {
        readonly MetadataReader _reader;
        readonly DefinitionEntry[] _definitionsByHash = [];
        readonly ExportEntry[] _exportsByHash = [];
        readonly MetadataTypeNameFailure? _failure;
        readonly bool _declaresCoreLibraryRoot;

        internal Index(MetadataReader reader)
        {
            _reader = reader;
            bool canDeclareCoreLibraryRoot =
                reader.AssemblyReferences.Count == 0;
            var definitions =
                new DefinitionEntry[reader.TypeDefinitions.Count];
            int definitionIndex = 0;
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                try
                {
                    TypeDefinition definition =
                        reader.GetTypeDefinition(handle);
                    _declaresCoreLibraryRoot |=
                        canDeclareCoreLibraryRoot
                        && IsCoreLibraryRoot(
                            reader,
                            definition);
                    definitions[definitionIndex++] =
                        new DefinitionEntry(
                            StringComparer.Ordinal.GetHashCode(
                                reader.GetString(definition.Name)),
                            handle);
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException
                        or ArgumentOutOfRangeException
                        or IndexOutOfRangeException)
                {
                    _failure =
                        MetadataTypeNameFailure.Malformed(
                            handle,
                            ex.Message);
                    return;
                }
            }
            Array.Sort(
                definitions,
                static (left, right) =>
                {
                    int hashOrder =
                        left.Hash.CompareTo(right.Hash);
                    return hashOrder != 0
                        ? hashOrder
                        : MetadataTokens.GetRowNumber(left.Handle)
                            .CompareTo(
                                MetadataTokens.GetRowNumber(
                                    right.Handle));
                });
            _definitionsByHash = definitions;

            var exports =
                new ExportEntry[reader.ExportedTypes.Count];
            int exportIndex = 0;
            foreach (ExportedTypeHandle handle in reader.ExportedTypes)
            {
                try
                {
                    exports[exportIndex++] =
                        new ExportEntry(
                            StringComparer.Ordinal.GetHashCode(
                                reader.GetString(
                                    reader.GetExportedType(handle).Name)),
                            handle);
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException
                        or ArgumentOutOfRangeException)
                {
                    _failure =
                        MetadataTypeNameFailure.Malformed(
                            handle,
                            ex.Message);
                    return;
                }
            }
            Array.Sort(
                exports,
                static (left, right) =>
                {
                    int hashOrder =
                        left.Hash.CompareTo(right.Hash);
                    return hashOrder != 0
                        ? hashOrder
                        : MetadataTokens.GetRowNumber(left.Handle)
                            .CompareTo(
                                MetadataTokens.GetRowNumber(
                                    right.Handle));
                });
            _exportsByHash = exports;
        }

        internal TypeDeclarationResult Probe(
            MetadataTypeDefinitionName name)
        {
            if (_failure is not null)
                return new TypeDeclarationResult.Rejected(_failure);

            var candidates = new List<PendingCandidate>();
            var forwarders =
                new Dictionary<AssemblyReferenceIdentity, PendingForwarder>();
            string leaf = name.Segments[^1];
            int leafHash = StringComparer.Ordinal.GetHashCode(leaf);
            for (int i = LowerBound(_definitionsByHash, leafHash);
                i < _definitionsByHash.Length
                    && _definitionsByHash[i].Hash == leafHash;
                i++)
            {
                TypeDefinitionHandle handle =
                    _definitionsByHash[i].Handle;
                try
                {
                    if (!_reader.StringComparer.Equals(
                            _reader.GetTypeDefinition(handle).Name,
                            leaf))
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException
                        or ArgumentOutOfRangeException)
                {
                    return new TypeDeclarationResult.Rejected(
                        MetadataTypeNameFailure.Malformed(
                            handle,
                            ex.Message));
                }

                MetadataTypeDefinitionNameMatch match =
                    MetadataTypeDefinitionNameReader.Matches(
                        _reader,
                        handle,
                        name,
                        out MetadataTypeNameFailure? failure);
                if (match == MetadataTypeDefinitionNameMatch.Rejected)
                {
                    return new TypeDeclarationResult.Rejected(
                        failure!);
                }

                if (match == MetadataTypeDefinitionNameMatch.Match)
                {
                    candidates.Add(
                        new PendingDefinition(
                            handle,
                            TypeDefinitionToken.FromHandle(
                                _reader,
                                handle)));
                }
            }

            for (int i = LowerBound(_exportsByHash, leafHash);
                i < _exportsByHash.Length
                    && _exportsByHash[i].Hash == leafHash;
                i++)
            {
                ExportedTypeHandle handle =
                    _exportsByHash[i].Handle;
                try
                {
                    if (!_reader.StringComparer.Equals(
                            _reader.GetExportedType(handle).Name,
                            leaf))
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException
                        or ArgumentOutOfRangeException)
                {
                    return new TypeDeclarationResult.Rejected(
                        MetadataTypeNameFailure.Malformed(
                            handle,
                            ex.Message));
                }

                MetadataTypeDefinitionNameMatch match =
                    MetadataTypeDefinitionNameReader.Matches(
                        _reader,
                        handle,
                        name,
                        out MetadataTypeNameFailure? failure);
                if (match == MetadataTypeDefinitionNameMatch.Rejected)
                {
                    return new TypeDeclarationResult.Rejected(
                        failure!);
                }
                if (match == MetadataTypeDefinitionNameMatch.NoMatch)
                    continue;

                if (!TryReadExportedCandidate(
                        _reader,
                        handle,
                        out TypeDeclarationCandidate? candidate,
                        out failure))
                {
                    return new TypeDeclarationResult.Rejected(
                        failure!);
                }

                AddCandidate(
                    candidates,
                    forwarders,
                    candidate!);
            }

            return Complete(
                _reader,
                candidates,
                _declaresCoreLibraryRoot);
        }

        static int LowerBound(
            DefinitionEntry[] entries,
            int hash)
        {
            int low = 0;
            int high = entries.Length;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (entries[middle].Hash < hash)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        static int LowerBound(
            ExportEntry[] entries,
            int hash)
        {
            int low = 0;
            int high = entries.Length;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (entries[middle].Hash < hash)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        readonly record struct DefinitionEntry(
            int Hash,
            TypeDefinitionHandle Handle);

        readonly record struct ExportEntry(
            int Hash,
            ExportedTypeHandle Handle);
    }

    static bool IsCoreLibraryRoot(
        MetadataReader reader,
        TypeDefinition definition) =>
        definition.BaseType.IsNil
        && (definition.Attributes
            & System.Reflection.TypeAttributes.Interface) == 0
        && reader.StringComparer.Equals(
            definition.Namespace,
            "System")
        && reader.StringComparer.Equals(
            definition.Name,
            "Object");

    static bool TryReadExportedCandidate(
        MetadataReader reader,
        ExportedTypeHandle handle,
        out TypeDeclarationCandidate? candidate,
        out MetadataTypeNameFailure? failure)
    {
        candidate = null;
        failure = null;

        var traversal =
            MetadataRelationshipTraversal.WalkExportedTypeImplementationChain(
                reader,
                handle);
        if (traversal is
            RelationshipTraversalResult<RelationshipChain<ExportedTypeHandle>>.Rejected rejected)
        {
            failure = MetadataTypeNameFailure.From(rejected.Rejection);
            return false;
        }

        RelationshipChain<ExportedTypeHandle> chain =
            ((RelationshipTraversalResult<RelationshipChain<ExportedTypeHandle>>.Completed)
                traversal).Value;
        ImmutableArray<ExportedTypeToken> declarations =
            [.. chain.Handles.Select(handle => ExportedTypeToken.FromHandle(reader, handle))];

        try
        {
            switch (chain.Terminal.Kind)
            {
                case HandleKind.AssemblyReference:
                    ExportedType root = reader.GetExportedType(chain.Handles[0]);
                    if (!root.IsForwarder)
                    {
                        failure = MetadataTypeNameFailure.Malformed(
                            chain.Handles[0],
                            "An AssemblyRef-terminated ExportedType chain must "
                            + "be marked as a forwarder.");
                        return false;
                    }

                    AssemblyReferenceIdentity target =
                        AssemblyReferenceIdentity.From(
                            reader,
                            (AssemblyReferenceHandle)chain.Terminal);
                    if (string.IsNullOrEmpty(target.Name))
                    {
                        failure = MetadataTypeNameFailure.Malformed(
                            chain.Terminal,
                            "An assembly-reference target must have a name.");
                        return false;
                    }

                    candidate = new TypeDeclarationCandidate.Forwarder(
                        declarations,
                        target);
                    return true;

                case HandleKind.AssemblyFile:
                    AssemblyFile file =
                        reader.GetAssemblyFile((AssemblyFileHandle)chain.Terminal);
                    string moduleName = reader.GetString(file.Name);
                    if (string.IsNullOrEmpty(moduleName))
                    {
                        failure = MetadataTypeNameFailure.Malformed(
                            chain.Terminal,
                            "A module file reference must have a name.");
                        return false;
                    }

                    candidate = new TypeDeclarationCandidate.ModuleExport(
                        declarations,
                        new ModuleFileReference(
                            moduleName,
                            file.ContainsMetadata,
                            ImmutableArray.Create(reader.GetBlobBytes(file.HashValue))));
                    return true;

                default:
                    failure = MetadataTypeNameFailure.Malformed(
                        handle,
                        $"ExportedType implementation terminates at unsupported "
                        + $"{chain.Terminal.Kind} metadata.");
                    return false;
            }
        }
        catch (BadImageFormatException ex)
        {
            failure = MetadataTypeNameFailure.Malformed(handle, ex.Message);
            return false;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            failure = MetadataTypeNameFailure.Malformed(handle, ex.Message);
            return false;
        }
    }

    static void AddCandidate(
        List<PendingCandidate> candidates,
        Dictionary<AssemblyReferenceIdentity, PendingForwarder> forwarders,
        TypeDeclarationCandidate candidate)
    {
        if (candidate is not TypeDeclarationCandidate.Forwarder forwarder)
        {
            candidates.Add(new PendingValue(candidate));
            return;
        }

        if (forwarders.TryGetValue(forwarder.Target, out PendingForwarder? existing))
        {
            foreach (ExportedTypeToken declaration in forwarder.Declarations)
                existing.Add(declaration);
            return;
        }

        var pending = new PendingForwarder(
            forwarder.Target,
            forwarder.Declarations);
        forwarders.Add(forwarder.Target, pending);
        candidates.Add(pending);
    }

    static TypeDeclarationResult Complete(
        MetadataReader reader,
        List<PendingCandidate> pending,
        bool declaringAssemblyDefinesCoreLibraryRoot)
    {
        if (pending.Count == 0)
            return new TypeDeclarationResult.Missing();

        ImmutableArray<TypeDeclarationCandidate> candidates =
            [.. pending.Select(
                candidate => candidate.Materialize(
                    reader,
                    declaringAssemblyDefinesCoreLibraryRoot))];
        if (candidates.Length > 1)
        {
            return new TypeDeclarationResult.Ambiguous(
                candidates);
        }

        return candidates[0] switch
        {
            TypeDeclarationCandidate.Definition definition =>
                new TypeDeclarationResult.Defined(
                    definition.Token,
                    definition.Kind,
                    declaringAssemblyDefinesCoreLibraryRoot,
                    definition.GenericParameterCount,
                    definition.KindDependency),
            TypeDeclarationCandidate.Forwarder forwarder =>
                new TypeDeclarationResult.Forwarded(
                    forwarder.Declarations,
                    forwarder.Target),
            TypeDeclarationCandidate.ModuleExport module =>
                new TypeDeclarationResult.ExportedFromModule(
                    module.Declarations,
                    module.Module),
            _ => throw new InvalidOperationException(
                "Unknown type declaration candidate."),
        };
    }

    abstract class PendingCandidate
    {
        internal abstract TypeDeclarationCandidate Materialize(
            MetadataReader reader,
            bool declaringAssemblyDefinesCoreLibraryRoot);
    }

    sealed class PendingValue(TypeDeclarationCandidate value) : PendingCandidate
    {
        internal override TypeDeclarationCandidate Materialize(
            MetadataReader reader,
            bool declaringAssemblyDefinesCoreLibraryRoot) =>
            value;
    }

    sealed class PendingDefinition(
        TypeDefinitionHandle handle,
        TypeDefinitionToken token) : PendingCandidate
    {
        internal override TypeDeclarationCandidate Materialize(
            MetadataReader reader,
            bool declaringAssemblyDefinesCoreLibraryRoot)
        {
            MetadataTypeDefinitionKind kind =
                ClassifyDefinitionKind(
                    reader,
                    handle,
                    declaringAssemblyDefinesCoreLibraryRoot);
            if (!TryGetGenericParameterCount(
                    reader,
                    handle,
                    out int genericParameterCount))
            {
                kind = MetadataTypeDefinitionKind.Unknown;
            }

            return new TypeDeclarationCandidate.Definition(
                token,
                kind,
                genericParameterCount,
                kind == MetadataTypeDefinitionKind.Unknown
                    ? ReadDefinitionKindDependency(reader, handle)
                    : null);
        }
    }

    sealed class PendingForwarder : PendingCandidate
    {
        readonly List<ExportedTypeToken> declarations;
        readonly HashSet<ExportedTypeToken> declarationSet;

        internal PendingForwarder(
            AssemblyReferenceIdentity target,
            ImmutableArray<ExportedTypeToken> declarations)
        {
            Target = target;
            this.declarations = [.. declarations];
            declarationSet = [.. declarations];
        }

        internal AssemblyReferenceIdentity Target { get; }

        internal void Add(ExportedTypeToken declaration)
        {
            if (declarationSet.Add(declaration))
                declarations.Add(declaration);
        }

        internal override TypeDeclarationCandidate Materialize(
            MetadataReader reader,
            bool declaringAssemblyDefinesCoreLibraryRoot) =>
            new TypeDeclarationCandidate.Forwarder(
                [.. declarations],
                Target);
    }

    internal static MetadataTypeDefinitionKind ClassifyDefinitionKind(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        bool declaringAssemblyDefinesCoreLibraryRoot)
    {
        var visited = new HashSet<TypeDefinitionHandle>();
        TypeDefinitionHandle current = handle;
        bool requiresClass = false;
        while (true)
        {
            if (visited.Count
                    >= MetadataSafetyPolicy.MaxRelationshipNodes
                || !visited.Add(current))
            {
                return MetadataTypeDefinitionKind.Unknown;
            }

            try
            {
                TypeDefinition definition =
                    reader.GetTypeDefinition(current);
                if ((definition.Attributes
                        & System.Reflection.TypeAttributes.Interface) != 0)
                {
                    return requiresClass
                        ? MetadataTypeDefinitionKind.Unknown
                        : MetadataTypeDefinitionKind.Interface;
                }

                if (declaringAssemblyDefinesCoreLibraryRoot
                    && MetadataTypeDefinitionNameReader.Read(
                        reader,
                        current)
                        is MetadataTypeDefinitionNameReadResult.Read ownName
                    && ownName.Name.ToMetadataFullName()
                        == "System.Enum")
                {
                    return MetadataTypeDefinitionKind.Class;
                }

                if (definition.BaseType.IsNil)
                    return MetadataTypeDefinitionKind.Class;

                if (definition.BaseType.Kind
                    == HandleKind.TypeSpecification)
                {
                    if (!TryReadTypeSpecificationClassBase(
                            reader,
                            (TypeSpecificationHandle)definition.BaseType,
                            declaringAssemblyDefinesCoreLibraryRoot,
                            out current))
                    {
                        return MetadataTypeDefinitionKind.Unknown;
                    }

                    requiresClass = true;
                    continue;
                }

                MetadataTypeDefinitionNameReadResult read =
                    definition.BaseType.Kind switch
                    {
                        HandleKind.TypeDefinition =>
                            MetadataTypeDefinitionNameReader.Read(
                                reader,
                                (TypeDefinitionHandle)definition.BaseType),
                        HandleKind.TypeReference =>
                            MetadataTypeDefinitionNameReader.Read(
                                reader,
                                (TypeReferenceHandle)definition.BaseType),
                        _ => new MetadataTypeDefinitionNameReadResult.Rejected(
                            MetadataTypeNameFailure.Malformed(
                                definition.BaseType,
                                "A type definition has an unsupported base-type handle.")),
                    };
                if (read is not MetadataTypeDefinitionNameReadResult.Read named)
                    return MetadataTypeDefinitionKind.Unknown;

                if (named.Name.ToMetadataFullName()
                    is not ("System.ValueType" or "System.Enum"))
                {
                    return MetadataTypeDefinitionKind.Class;
                }

                bool authenticCoreType =
                    definition.BaseType.Kind switch
                    {
                        HandleKind.TypeDefinition =>
                            declaringAssemblyDefinesCoreLibraryRoot,
                        HandleKind.TypeReference =>
                            ApiSurfaceExtractor.ResolvesThroughCoreLibrary(
                                reader,
                                reader.GetTypeReference(
                                    (TypeReferenceHandle)definition.BaseType)
                                    .ResolutionScope),
                        _ => false,
                    };
                MetadataTypeDefinitionKind kind = authenticCoreType
                    ? MetadataTypeDefinitionKind.ValueType
                    : MetadataTypeDefinitionKind.Class;
                return requiresClass
                    && kind != MetadataTypeDefinitionKind.Class
                        ? MetadataTypeDefinitionKind.Unknown
                        : kind;
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException
                    or ArgumentException)
            {
                return MetadataTypeDefinitionKind.Unknown;
            }
        }
    }

    static bool TryReadTypeSpecificationClassBase(
        MetadataReader reader,
        TypeSpecificationHandle handle,
        bool declaringAssemblyDefinesCoreLibraryRoot,
        out TypeDefinitionHandle rootHandle)
    {
        rootHandle = default;
        if (!TypeSpecificationRoot.TryRead(
                reader,
                handle,
                out TypeSpecificationRoot root)
            || root.Kind
                != TypeSpecificationRootKind.NamedType
            || root.RawTypeKind
                != (byte)SignatureTypeKind.Class)
        {
            return false;
        }

        if (root.Type.Kind != HandleKind.TypeDefinition)
            return false;

        rootHandle = (TypeDefinitionHandle)root.Type;
        return TryGetGenericParameterCount(
                reader,
                rootHandle,
                out int genericParameterCount)
            && genericParameterCount
                == root.GenericArgumentCount
            && (!declaringAssemblyDefinesCoreLibraryRoot
                || MetadataTypeDefinitionNameReader.Read(
                    reader,
                    rootHandle)
                    is not MetadataTypeDefinitionNameReadResult.Read named
                || named.Name.ToMetadataFullName()
                    is not ("System.ValueType" or "System.Enum"));
    }

    internal static bool TryGetGenericParameterCount(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        out int count)
    {
        count = 0;
        try
        {
            GenericParameterHandleCollection parameters =
                reader.GetTypeDefinition(handle).GetGenericParameters();
            foreach (GenericParameterHandle parameter in parameters)
            {
                if (reader.GetGenericParameter(parameter).Index
                    != count)
                {
                    count = -1;
                    return false;
                }

                count++;
            }

            return true;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
        {
            count = -1;
            return false;
        }
    }

    internal static DefinitionKindDependency? ReadDefinitionKindDependency(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        try
        {
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            if (definition.BaseType.Kind != HandleKind.TypeSpecification
                || !TypeSpecificationRoot.TryRead(
                    reader,
                    (TypeSpecificationHandle)definition.BaseType,
                    out TypeSpecificationRoot root)
                || root.Kind != TypeSpecificationRootKind.NamedType
                || root.RawTypeKind != (byte)SignatureTypeKind.Class
                || root.Type.Kind != HandleKind.TypeReference
                || MetadataTypeDefinitionNameReader.Read(
                    reader,
                    (TypeReferenceHandle)root.Type)
                    is not MetadataTypeDefinitionNameReadResult.Read named)
            {
                return null;
            }

            Span<TypeReferenceHandle> rootToLeaf =
                stackalloc TypeReferenceHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal
                    .TryWalkTypeReferenceResolutionScope(
                        reader,
                        (TypeReferenceHandle)root.Type,
                        rootToLeaf,
                        out _,
                        out EntityHandle terminal,
                        out _)
                || terminal.Kind != HandleKind.AssemblyReference)
            {
                return null;
            }

            AssemblyReferenceIdentity reference =
                AssemblyReferenceIdentity.From(
                    reader,
                    (AssemblyReferenceHandle)terminal);
            AssemblyResolutionScope scope =
                PlatformKeys.IsPlatform(reference.PublicKeyToken)
                    ? AssemblyResolutionScope.Platform
                    : AssemblyResolutionScope.Any;
            return new DefinitionKindDependency(
                reference,
                scope,
                named.Name,
                root.GenericArgumentCount);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or ArgumentException)
        {
            return null;
        }
    }
}
