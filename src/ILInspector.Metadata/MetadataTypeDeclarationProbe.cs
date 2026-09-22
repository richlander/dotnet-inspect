using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata;

/// <summary>Answers how one readable metadata image declares one exact type name.</summary>
public static class MetadataTypeDeclarationProbe
{
    /// <summary>
    /// Finds one exact TypeDef in the current image without considering exports
    /// or forwarders. Its row and cumulative name-comparison budgets are gated
    /// by <c>ProbeDefinition_RejectsRowsBeforeScanning</c> and
    /// <c>ProbeDefinition_ReportsRepeatedLongLeafWorkAsBudgetExceeded</c>.
    /// </summary>
    public static TypeDeclarationResult ProbeDefinition(
        MetadataReader reader,
        MetadataTypeDefinitionName name) =>
        ProbeDefinition(
            reader,
            name,
            MetadataSafetyPolicy.MaxTypeDeclarationRows,
            MetadataSafetyPolicy.MaxTypeDeclarationNameWorkChars);

    internal static TypeDeclarationResult ProbeDefinition(
        MetadataReader reader,
        MetadataTypeDefinitionName name,
        int maxRows,
        long maxNameWork)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNameWork);

        if (reader.TypeDefinitions.Count > maxRows)
        {
            return new TypeDeclarationResult.BudgetExceeded(
                maxRows,
                "The exact TypeDef lookup exceeded its metadata-row budget.");
        }

        var candidates = new List<PendingCandidate>();
        var referenceProjection =
            new AssemblyReferenceProjectionCache(reader);
        bool canDeclareCoreLibraryRoot =
            reader.AssemblyReferences.Count == 0;
        int coreLibraryRootCandidateCount = 0;
        int leafUtf8Length =
            System.Text.Encoding.UTF8.GetByteCount(name.Segments[^1]);
        long comparisonWork = NameComparisonWork(name);

        long remainingWork = maxNameWork;
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            try
            {
                TypeDefinition definition =
                    reader.GetTypeDefinition(handle);
                if (canDeclareCoreLibraryRoot
                    && IsCoreLibraryRoot(reader, definition))
                {
                    coreLibraryRootCandidateCount++;
                }
                if (reader.GetBlobReader(definition.Name).Length
                    == leafUtf8Length)
                {
                    remainingWork -= comparisonWork;
                    if (remainingWork < 0)
                    {
                        return new TypeDeclarationResult.BudgetExceeded(
                            maxNameWork,
                            "The exact TypeDef lookup exceeded its "
                                + "structural-name work budget.");
                    }
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
                    reader,
                    handle,
                    name,
                    out MetadataTypeNameFailure? failure);
            if (match == MetadataTypeDefinitionNameMatch.Rejected)
                return new TypeDeclarationResult.Rejected(failure!);
            if (match == MetadataTypeDefinitionNameMatch.Match)
            {
                candidates.Add(
                    new PendingDefinition(
                        handle,
                        TypeDefinitionToken.FromHandle(
                            reader,
                            handle)));
            }
        }
        bool declaresCoreLibraryRoot =
            canDeclareCoreLibraryRoot
            && coreLibraryRootCandidateCount == 1;
        return Complete(
            reader,
            candidates,
            referenceProjection,
            declaresCoreLibraryRoot);
    }

    public static TypeDeclarationResult Probe(
        MetadataReader reader,
        MetadataTypeDefinitionName name) =>
        Probe(
            reader,
            name,
            MetadataSafetyPolicy.MaxTypeDeclarationRows,
            MetadataSafetyPolicy.MaxTypeDeclarationNameWorkChars);

    internal static TypeDeclarationResult Probe(
        MetadataReader reader,
        MetadataTypeDefinitionName name,
        int maxRows,
        long maxNameWork)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNameWork);

        long rowCount =
            (long)reader.TypeDefinitions.Count
            + reader.ExportedTypes.Count;
        if (rowCount > maxRows)
        {
            return new TypeDeclarationResult.BudgetExceeded(
                maxRows,
                "The type declaration scan exceeded its metadata-row budget.");
        }

        var candidates = new List<PendingCandidate>();
        var forwarders =
            new Dictionary<AssemblyReferenceIdentity, PendingForwarder>();
        var referenceProjection =
            new AssemblyReferenceProjectionCache(reader);
        bool canDeclareCoreLibraryRoot =
            reader.AssemblyReferences.Count == 0;
        int coreLibraryRootCandidateCount = 0;
        int leafUtf8Length =
            System.Text.Encoding.UTF8.GetByteCount(name.Segments[^1]);
        long comparisonWork = NameComparisonWork(name);
        long remainingWork = maxNameWork;

        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            try
            {
                TypeDefinition definition =
                    reader.GetTypeDefinition(handle);
                if (canDeclareCoreLibraryRoot
                    && IsCoreLibraryRoot(reader, definition))
                {
                    coreLibraryRootCandidateCount++;
                }
                if (reader.GetBlobReader(definition.Name).Length
                    == leafUtf8Length
                    && !TryCharge(
                        ref remainingWork,
                        comparisonWork))
                {
                    return new TypeDeclarationResult.BudgetExceeded(
                        maxNameWork,
                        "The type declaration scan exceeded its "
                            + "structural-name work budget.");
                }
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

            MetadataTypeDefinitionNameMatch match =
                MetadataTypeDefinitionNameReader.Matches(
                    reader,
                    handle,
                    name,
                    out MetadataTypeNameFailure? failure);
            if (match == MetadataTypeDefinitionNameMatch.Rejected)
                return new TypeDeclarationResult.Rejected(failure!);
            if (match == MetadataTypeDefinitionNameMatch.Match)
            {
                candidates.Add(
                    new PendingDefinition(
                        handle,
                        TypeDefinitionToken.FromHandle(reader, handle)));
            }
        }

        bool declaresCoreLibraryRoot =
            canDeclareCoreLibraryRoot
            && coreLibraryRootCandidateCount == 1;

        foreach (ExportedTypeHandle handle in reader.ExportedTypes)
        {
            try
            {
                ExportedType exported =
                    reader.GetExportedType(handle);
                if (reader.GetBlobReader(exported.Name).Length
                    == leafUtf8Length
                    && !TryCharge(
                        ref remainingWork,
                        comparisonWork))
                {
                    return new TypeDeclarationResult.BudgetExceeded(
                        maxNameWork,
                        "The type declaration scan exceeded its "
                            + "structural-name work budget.");
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
                    referenceProjection,
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
            referenceProjection,
            declaresCoreLibraryRoot);
    }

    internal static Index CreateIndex(MetadataReader reader) =>
        new(
            reader,
            MetadataSafetyPolicy.MaxTypeDeclarationRows,
            MetadataSafetyPolicy.MaxTypeDeclarationNameWorkChars);

    internal static Index CreateIndex(
        MetadataReader reader,
        int maxRows,
        long maxNameWork) =>
        new(reader, maxRows, maxNameWork);

    internal sealed class Index
    {
        readonly MetadataReader _reader;
        readonly DefinitionEntry[] _definitionsByHash = [];
        readonly ExportEntry[] _exportsByHash = [];
        readonly TypeDeclarationResult? _failure;
        readonly bool _declaresCoreLibraryRoot;
        readonly long _maxNameWork;
        readonly AssemblyReferenceProjectionCache
            _assemblyReferenceProjection;

        internal Index(
            MetadataReader reader,
            int maxRows,
            long maxNameWork)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNameWork);

            _reader = reader;
            _maxNameWork = maxNameWork;
            _assemblyReferenceProjection =
                new AssemblyReferenceProjectionCache(reader);
            long rowCount =
                (long)reader.TypeDefinitions.Count
                + reader.ExportedTypes.Count;
            if (rowCount > maxRows)
            {
                _failure =
                    new TypeDeclarationResult.BudgetExceeded(
                        maxRows,
                        "The type declaration index exceeded its "
                            + "metadata-row budget.");
                return;
            }

            bool canDeclareCoreLibraryRoot =
                reader.AssemblyReferences.Count == 0;
            int coreLibraryRootCandidateCount = 0;
            long remainingWork = maxNameWork;
            var definitions =
                new DefinitionEntry[reader.TypeDefinitions.Count];
            int definitionIndex = 0;
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                try
                {
                    TypeDefinition definition =
                        reader.GetTypeDefinition(handle);
                    if (canDeclareCoreLibraryRoot
                        && IsCoreLibraryRoot(
                            reader,
                            definition))
                    {
                        coreLibraryRootCandidateCount++;
                    }
                    if (!TryCharge(
                        ref remainingWork,
                        reader.GetBlobReader(definition.Name).Length))
                    {
                        _failure =
                            new TypeDeclarationResult.BudgetExceeded(
                                maxNameWork,
                                "The type declaration index exceeded its "
                                    + "stored-name work budget.");
                        return;
                    }
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
                        new TypeDeclarationResult.Rejected(
                            MetadataTypeNameFailure.Malformed(
                                handle,
                                ex.Message));
                    return;
                }
            }
            _declaresCoreLibraryRoot =
                canDeclareCoreLibraryRoot
                && coreLibraryRootCandidateCount == 1;
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
            var exports =
                new ExportEntry[reader.ExportedTypes.Count];
            int exportIndex = 0;
            foreach (ExportedTypeHandle handle in reader.ExportedTypes)
            {
                try
                {
                    ExportedType exported =
                        reader.GetExportedType(handle);
                    if (!TryCharge(
                        ref remainingWork,
                        reader.GetBlobReader(exported.Name).Length))
                    {
                        _failure =
                            new TypeDeclarationResult.BudgetExceeded(
                                maxNameWork,
                                "The type declaration index exceeded its "
                                    + "stored-name work budget.");
                        return;
                    }
                    exports[exportIndex++] =
                        new ExportEntry(
                            StringComparer.Ordinal.GetHashCode(
                                reader.GetString(exported.Name)),
                            handle);
                }
                catch (Exception ex) when (
                    ex is BadImageFormatException
                        or ArgumentOutOfRangeException)
                {
                    _failure =
                        new TypeDeclarationResult.Rejected(
                            MetadataTypeNameFailure.Malformed(
                                handle,
                                ex.Message));
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
            _definitionsByHash = definitions;
            _exportsByHash = exports;
        }

        internal TypeDeclarationResult Probe(
            MetadataTypeDefinitionName name)
        {
            if (_failure is not null)
                return _failure;

            var candidates = new List<PendingCandidate>();
            var forwarders =
                new Dictionary<AssemblyReferenceIdentity, PendingForwarder>();
            string leaf = name.Segments[^1];
            int leafUtf8Length =
                System.Text.Encoding.UTF8.GetByteCount(leaf);
            int leafHash = StringComparer.Ordinal.GetHashCode(leaf);
            long comparisonWork = NameComparisonWork(name);
            long remainingWork = _maxNameWork;
            for (int i = LowerBound(_definitionsByHash, leafHash);
                i < _definitionsByHash.Length
                    && _definitionsByHash[i].Hash == leafHash;
                i++)
            {
                TypeDefinitionHandle handle =
                    _definitionsByHash[i].Handle;
                try
                {
                    StringHandle candidateName =
                        _reader.GetTypeDefinition(handle).Name;
                    if (_reader.GetBlobReader(candidateName).Length
                            == leafUtf8Length
                        && !TryCharge(
                            ref remainingWork,
                            comparisonWork))
                    {
                        return new TypeDeclarationResult.BudgetExceeded(
                            _maxNameWork,
                            "The type declaration index probe exceeded its "
                                + "structural-name work budget.");
                    }
                    if (!_reader.StringComparer.Equals(candidateName, leaf))
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
                    StringHandle candidateName =
                        _reader.GetExportedType(handle).Name;
                    if (_reader.GetBlobReader(candidateName).Length
                            == leafUtf8Length
                        && !TryCharge(
                            ref remainingWork,
                            comparisonWork))
                    {
                        return new TypeDeclarationResult.BudgetExceeded(
                            _maxNameWork,
                            "The type declaration index probe exceeded its "
                                + "structural-name work budget.");
                    }
                    if (!_reader.StringComparer.Equals(candidateName, leaf))
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
                        _assemblyReferenceProjection,
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
                _assemblyReferenceProjection,
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

    static long NameComparisonWork(
        MetadataTypeDefinitionName name)
    {
        long work =
            System.Text.Encoding.UTF8.GetByteCount(name.Namespace);
        foreach (string segment in name.Segments)
        {
            work += System.Text.Encoding.UTF8.GetByteCount(segment);
        }
        return Math.Max(work, 1);
    }

    static bool TryCharge(
        ref long remainingWork,
        long work)
    {
        remainingWork -= work;
        return remainingWork >= 0;
    }

    static bool IsCoreLibraryRoot(
        MetadataReader reader,
        TypeDefinition definition) =>
        CoreLibraryRootAuthentication
            .IsValidTopLevelCoreLibraryRoot(
                reader,
                definition);

    internal static bool TryReadExportedCandidate(
        MetadataReader reader,
        ExportedTypeHandle handle,
        AssemblyReferenceProjectionCache referenceProjection,
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
                            (AssemblyReferenceHandle)chain.Terminal,
                            referenceProjection);
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
        AssemblyReferenceProjectionCache referenceProjection,
        bool declaringAssemblyDefinesCoreLibraryRoot)
    {
        if (pending.Count == 0)
            return new TypeDeclarationResult.Missing();

        ImmutableArray<TypeDeclarationCandidate> candidates =
            [.. pending.Select(
                candidate => candidate.Materialize(
                    reader,
                    referenceProjection,
                    declaringAssemblyDefinesCoreLibraryRoot))];
        if (candidates.Length > 1)
        {
            return new TypeDeclarationResult.Ambiguous(
                candidates);
        }

        return candidates[0] switch
        {
            TypeDeclarationCandidate.Definition
            { KindFailure: { } failure } definition =>
                new TypeDeclarationResult.DefinitionKindUnavailable(
                    definition.Token,
                    failure,
                    declaringAssemblyDefinesCoreLibraryRoot,
                    definition.GenericParameterCount),
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
            AssemblyReferenceProjectionCache referenceProjection,
            bool declaringAssemblyDefinesCoreLibraryRoot);
    }

    sealed class PendingValue(TypeDeclarationCandidate value) : PendingCandidate
    {
        internal override TypeDeclarationCandidate Materialize(
            MetadataReader reader,
            AssemblyReferenceProjectionCache referenceProjection,
            bool declaringAssemblyDefinesCoreLibraryRoot) =>
            value;
    }

    sealed class PendingDefinition(
        TypeDefinitionHandle handle,
        TypeDefinitionToken token) : PendingCandidate
    {
        internal override TypeDeclarationCandidate Materialize(
            MetadataReader reader,
            AssemblyReferenceProjectionCache referenceProjection,
            bool declaringAssemblyDefinesCoreLibraryRoot)
        {
            DefinitionKindClassification classification =
                ClassifyDefinitionKind(
                    reader,
                    handle,
                    declaringAssemblyDefinesCoreLibraryRoot,
                    referenceProjection);
            TryGetGenericParameterCount(
                reader,
                handle,
                out int genericParameterCount,
                out MetadataTypeDefinitionKindFailure? genericFailure);
            MetadataTypeDefinitionKindFailure? failure =
                classification.Failure ?? genericFailure;

            return new TypeDeclarationCandidate.Definition(
                token,
                failure is null
                    ? classification.Kind
                    : MetadataTypeDefinitionKind.Unknown,
                genericParameterCount,
                failure is null
                    ? classification.Dependency
                    : null,
                failure);
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
            AssemblyReferenceProjectionCache referenceProjection,
            bool declaringAssemblyDefinesCoreLibraryRoot) =>
            new TypeDeclarationCandidate.Forwarder(
                [.. declarations],
                Target);
    }

    internal static MetadataTypeDefinitionKind ClassifyDefinitionKind(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        bool declaringAssemblyDefinesCoreLibraryRoot)
        => ClassifyDefinitionKindCore(
            reader,
            handle,
            declaringAssemblyDefinesCoreLibraryRoot,
            referenceProjection: null).Kind;

    static DefinitionKindClassification ClassifyDefinitionKind(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        bool declaringAssemblyDefinesCoreLibraryRoot,
        AssemblyReferenceProjectionCache referenceProjection) =>
        ClassifyDefinitionKindCore(
            reader,
            handle,
            declaringAssemblyDefinesCoreLibraryRoot,
            referenceProjection);

    internal static MetadataTypeDefinitionKind ClassifyDefinitionKind(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        bool declaringAssemblyDefinesCoreLibraryRoot,
        AssemblyReferenceProjectionCache referenceProjection,
        out DefinitionKindDependency? dependency)
    {
        DefinitionKindClassification classification =
            ClassifyDefinitionKindCore(
                reader,
                handle,
                declaringAssemblyDefinesCoreLibraryRoot,
                referenceProjection);
        dependency = classification.Failure is null
            ? classification.Dependency
            : null;
        return classification.Kind;
    }

    static DefinitionKindClassification ClassifyDefinitionKindCore(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        bool declaringAssemblyDefinesCoreLibraryRoot,
        AssemblyReferenceProjectionCache? referenceProjection)
    {
        var visited = new HashSet<TypeDefinitionHandle>();
        TypeDefinitionHandle current = handle;
        bool requiresClass = false;
        while (true)
        {
            if (visited.Count
                    >= MetadataSafetyPolicy.MaxRelationshipNodes)
            {
                return DefinitionKindClassification.Failed(
                    new MetadataTypeDefinitionKindFailure.BudgetExceeded(
                        MetadataSafetyPolicy.MaxRelationshipNodes,
                        "TypeDef kind classification exceeded its "
                            + "relationship-node budget."));
            }
            if (!visited.Add(current))
            {
                return DefinitionKindClassification.Failed(
                    new MetadataTypeDefinitionKindFailure.Malformed(
                        MetadataTokens.GetToken(current),
                        "The TypeDef base chain contains a cycle."));
            }

            try
            {
                TypeDefinition definition =
                    reader.GetTypeDefinition(current);
                if ((definition.Attributes
                        & System.Reflection.TypeAttributes.Interface) != 0)
                {
                    return requiresClass
                        ? DefinitionKindClassification.Failed(
                            new MetadataTypeDefinitionKindFailure.Malformed(
                                MetadataTokens.GetToken(current),
                                "A TypeSpec marked CLASS resolves to an interface."))
                        : DefinitionKindClassification.Known(
                            MetadataTypeDefinitionKind.Interface);
                }

                if (declaringAssemblyDefinesCoreLibraryRoot
                    && MetadataTypeDefinitionNameReader.Read(
                        reader,
                        current)
                        is MetadataTypeDefinitionNameReadResult.Read ownName
                    && ownName.Name.ToMetadataFullName()
                        == "System.Enum")
                {
                    return DefinitionKindClassification.Known(
                        MetadataTypeDefinitionKind.Class);
                }

                if (definition.BaseType.IsNil)
                {
                    return DefinitionKindClassification.Known(
                        MetadataTypeDefinitionKind.Class);
                }

                if (definition.BaseType.Kind
                    == HandleKind.TypeSpecification)
                {
                    TypeSpecificationHandle specification =
                        (TypeSpecificationHandle)definition.BaseType;
                    TypeSpecificationRootReadResult rootResult =
                        TypeSpecificationRoot.Read(
                            reader,
                            specification);
                    if (rootResult
                        is not TypeSpecificationRootReadResult.Read rootRead)
                    {
                        return DefinitionKindClassification.Failed(
                            KindFailure(
                                specification,
                                rootResult));
                    }

                    TypeSpecificationRoot root = rootRead.Root;
                    if (root.Kind
                            != TypeSpecificationRootKind.NamedType
                        || root.RawTypeKind
                            != (byte)SignatureTypeKind.Class)
                    {
                        return DefinitionKindClassification.Failed(
                            new MetadataTypeDefinitionKindFailure.Unsupported(
                                MetadataTokens.GetToken(specification),
                                "A TypeDef base TypeSpec must be a named "
                                    + "CLASS shape."));
                    }

                    if (root.Type.Kind
                        == HandleKind.TypeReference)
                    {
                        if (referenceProjection is null)
                        {
                            return DefinitionKindClassification.Known(
                                MetadataTypeDefinitionKind.Unknown);
                        }

                        if (!TryReadDefinitionKindDependency(
                                reader,
                                (TypeReferenceHandle)root.Type,
                                root.GenericArgumentCount,
                                referenceProjection,
                                out DefinitionKindDependency? dependency,
                                out MetadataTypeDefinitionKindFailure? failure))
                        {
                            return DefinitionKindClassification.Failed(
                                failure!);
                        }

                        return DefinitionKindClassification.Known(
                            MetadataTypeDefinitionKind.Unknown,
                            dependency);
                    }

                    if (root.Type.Kind
                        != HandleKind.TypeDefinition)
                    {
                        return DefinitionKindClassification.Failed(
                            new MetadataTypeDefinitionKindFailure.Unsupported(
                                MetadataTokens.GetToken(specification),
                                "The TypeSpec root is not a TypeDef or TypeRef."));
                    }

                    if (!TryReadTypeSpecificationClassBase(
                            reader,
                            root,
                            declaringAssemblyDefinesCoreLibraryRoot,
                            out current,
                            out MetadataTypeDefinitionKindFailure?
                                classBaseFailure))
                    {
                        return DefinitionKindClassification.Failed(
                            classBaseFailure!);
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
                {
                    var rejected =
                        (MetadataTypeDefinitionNameReadResult.Rejected)read;
                    return DefinitionKindClassification.Failed(
                        KindFailure(rejected.Failure));
                }

                if (named.Name.ToMetadataFullName()
                    is not ("System.ValueType" or "System.Enum"))
                {
                    return DefinitionKindClassification.Known(
                        MetadataTypeDefinitionKind.Class);
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
                        ? DefinitionKindClassification.Failed(
                            new MetadataTypeDefinitionKindFailure.Malformed(
                                MetadataTokens.GetToken(current),
                                "A TypeSpec marked CLASS resolves to a value type."))
                        : DefinitionKindClassification.Known(kind);
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException
                    or ArgumentException
                    or IndexOutOfRangeException)
            {
                return DefinitionKindClassification.Failed(
                    new MetadataTypeDefinitionKindFailure.Malformed(
                        MetadataTokens.GetToken(current),
                        ex.Message));
            }
        }
    }

    static bool TryReadTypeSpecificationClassBase(
        MetadataReader reader,
        TypeSpecificationRoot root,
        bool declaringAssemblyDefinesCoreLibraryRoot,
        out TypeDefinitionHandle rootHandle,
        out MetadataTypeDefinitionKindFailure? failure)
    {
        rootHandle = default;
        failure = null;
        if (root.Type.Kind != HandleKind.TypeDefinition)
        {
            failure = new MetadataTypeDefinitionKindFailure.Unsupported(
                root.Type.IsNil
                    ? null
                    : MetadataTokens.GetToken(root.Type),
                "The TypeSpec CLASS root is not a TypeDef.");
            return false;
        }

        rootHandle = (TypeDefinitionHandle)root.Type;
        if (!TryGetGenericParameterCount(
                reader,
                rootHandle,
                out int genericParameterCount,
                out failure))
        {
            return false;
        }
        if (genericParameterCount != root.GenericArgumentCount)
        {
            failure = new MetadataTypeDefinitionKindFailure.Malformed(
                MetadataTokens.GetToken(rootHandle),
                "The constructed TypeSpec generic arity does not match "
                    + "its TypeDef root.");
            return false;
        }
        if (!declaringAssemblyDefinesCoreLibraryRoot)
            return true;

        MetadataTypeDefinitionNameReadResult name =
            MetadataTypeDefinitionNameReader.Read(
                    reader,
                    rootHandle);
        if (name is not MetadataTypeDefinitionNameReadResult.Read named)
        {
            failure = KindFailure(
                ((MetadataTypeDefinitionNameReadResult.Rejected)name)
                    .Failure);
            return false;
        }
        if (named.Name.ToMetadataFullName()
            is "System.ValueType" or "System.Enum")
        {
            failure = new MetadataTypeDefinitionKindFailure.Malformed(
                MetadataTokens.GetToken(rootHandle),
                "A TypeSpec marked CLASS resolves to a core value-type root.");
            return false;
        }

        return true;
    }

    internal static bool TryGetGenericParameterCount(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        out int count)
        => TryGetGenericParameterCount(
            reader,
            handle,
            out count,
            out _);

    static bool TryGetGenericParameterCount(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        out int count,
        out MetadataTypeDefinitionKindFailure? failure)
    {
        count = 0;
        failure = null;
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
                    failure =
                        new MetadataTypeDefinitionKindFailure.Malformed(
                            MetadataTokens.GetToken(parameter),
                            "A TypeDef generic-parameter sequence has "
                                + "non-contiguous numbering.");
                    return false;
                }

                count++;
            }

            return true;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or IndexOutOfRangeException)
        {
            count = -1;
            failure = new MetadataTypeDefinitionKindFailure.Malformed(
                MetadataTokens.GetToken(handle),
                ex.Message);
            return false;
        }
    }

    static bool TryReadDefinitionKindDependency(
        MetadataReader reader,
        TypeReferenceHandle handle,
        int genericArgumentCount,
        AssemblyReferenceProjectionCache referenceProjection,
        out DefinitionKindDependency? dependency,
        out MetadataTypeDefinitionKindFailure? failure)
    {
        dependency = null;
        failure = null;
        try
        {
            MetadataTypeDefinitionNameReadResult name =
                MetadataTypeDefinitionNameReader.Read(
                    reader,
                    handle);
            if (name
                is not MetadataTypeDefinitionNameReadResult.Read named)
            {
                var rejected =
                    (MetadataTypeDefinitionNameReadResult.Rejected)
                        name;
                failure = KindFailure(rejected.Failure);
                return false;
            }

            Span<TypeReferenceHandle> rootToLeaf =
                stackalloc TypeReferenceHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal
                    .TryWalkTypeReferenceResolutionScope(
                        reader,
                        handle,
                        rootToLeaf,
                        out _,
                        out EntityHandle terminal,
                        out RelationshipTraversalRejection? rejection))
            {
                if (rejection is not null
                        && rejection.Kind
                            is RelationshipTraversalRejectionKind.NodeBudget
                                or RelationshipTraversalRejectionKind.NameBudget)
                {
                    failure =
                        new MetadataTypeDefinitionKindFailure.BudgetExceeded(
                            rejection.Kind
                                == RelationshipTraversalRejectionKind.NameBudget
                                    ? MetadataSafetyPolicy
                                        .MaxTypeNameCharacters
                                    : MetadataSafetyPolicy
                                        .MaxRelationshipNodes,
                            rejection.Detail);
                }
                else
                {
                    failure =
                        new MetadataTypeDefinitionKindFailure.Malformed(
                            rejection is { Subject.IsNil: false }
                                ? MetadataTokens.GetToken(rejection.Subject)
                                : MetadataTokens.GetToken(handle),
                            rejection?.Detail
                                ?? "The TypeRef resolution-scope chain could "
                                    + "not be read.");
                }
                return false;
            }
            if (terminal.Kind != HandleKind.AssemblyReference)
            {
                failure = new MetadataTypeDefinitionKindFailure.Unsupported(
                        terminal.IsNil
                            ? MetadataTokens.GetToken(handle)
                            : MetadataTokens.GetToken(terminal),
                        "The TypeRef kind dependency does not terminate at an "
                            + "assembly reference.");
                return false;
            }

            AssemblyReferenceIdentity reference =
                AssemblyReferenceIdentity.From(
                    (AssemblyReferenceHandle)terminal,
                    referenceProjection);
            AssemblyResolutionScope scope =
                PlatformKeys.IsPlatform(reference.PublicKeyToken)
                    ? AssemblyResolutionScope.Platform
                    : AssemblyResolutionScope.Any;
            dependency = new DefinitionKindDependency(
                reference,
                scope,
                named.Name,
                genericArgumentCount);
            return true;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or ArgumentException
                or IndexOutOfRangeException)
        {
            failure = new MetadataTypeDefinitionKindFailure.Malformed(
                MetadataTokens.GetToken(handle),
                ex.Message);
            return false;
        }
    }

    static MetadataTypeDefinitionKindFailure KindFailure(
        MetadataTypeNameFailure failure)
    {
        if (failure.RelationshipKind
                is RelationshipTraversalRejectionKind.NodeBudget
                    or RelationshipTraversalRejectionKind.NameBudget)
        {
            long budget = failure.RelationshipKind
                == RelationshipTraversalRejectionKind.NameBudget
                    ? MetadataSafetyPolicy.MaxTypeNameCharacters
                    : MetadataSafetyPolicy.MaxRelationshipNodes;
            return new MetadataTypeDefinitionKindFailure.BudgetExceeded(
                budget,
                failure.Detail);
        }
        if (failure.SignatureKind is { } signatureKind
            && signatureKind
                != SignatureDecodeRejectionKind.MalformedMetadata)
        {
            long budget = signatureKind switch
            {
                SignatureDecodeRejectionKind.NameBudget =>
                    MetadataSafetyPolicy.MaxTypeNameCharacters,
                SignatureDecodeRejectionKind.TypeSpecificationBudget =>
                    TypeSpecGuard.MaxCumulativeBytes,
                _ => SignatureBlobGuard.DefaultMaxDepth,
            };
            return new MetadataTypeDefinitionKindFailure.BudgetExceeded(
                budget,
                failure.Detail);
        }

        return new MetadataTypeDefinitionKindFailure.Malformed(
            failure.SubjectToken,
            failure.Detail,
            failure);
    }

    static MetadataTypeDefinitionKindFailure KindFailure(
        TypeSpecificationHandle handle,
        TypeSpecificationRootReadResult result) =>
        result switch
        {
            TypeSpecificationRootReadResult.BudgetExceeded exceeded =>
                new MetadataTypeDefinitionKindFailure.BudgetExceeded(
                    exceeded.Budget,
                    exceeded.Detail),
            TypeSpecificationRootReadResult.Malformed malformed =>
                new MetadataTypeDefinitionKindFailure.Malformed(
                    MetadataTokens.GetToken(malformed.Subject),
                    malformed.Detail),
            TypeSpecificationRootReadResult.Cycle cycle =>
                new MetadataTypeDefinitionKindFailure.Malformed(
                    MetadataTokens.GetToken(cycle.Subject),
                    cycle.Detail),
            TypeSpecificationRootReadResult.Unsupported unsupported =>
                new MetadataTypeDefinitionKindFailure.Unsupported(
                    MetadataTokens.GetToken(unsupported.Subject),
                    unsupported.Detail),
            _ => throw new InvalidOperationException(
                "A successful TypeSpec root cannot be converted to a failure."),
        };

    readonly record struct DefinitionKindClassification(
        MetadataTypeDefinitionKind Kind,
        DefinitionKindDependency? Dependency,
        MetadataTypeDefinitionKindFailure? Failure)
    {
        internal static DefinitionKindClassification Known(
            MetadataTypeDefinitionKind kind,
            DefinitionKindDependency? dependency = null) =>
            new(kind, dependency, Failure: null);

        internal static DefinitionKindClassification Failed(
            MetadataTypeDefinitionKindFailure failure) =>
            new(
                MetadataTypeDefinitionKind.Unknown,
                Dependency: null,
                failure);
    }
}
