using System.Collections.Immutable;
using System.Reflection.Metadata;

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
            if (match == MetadataTypeDefinitionNameMatch.Match)
            {
                candidates.Add(
                    new PendingValue(
                        new TypeDeclarationCandidate.Definition(
                            TypeDefinitionToken.FromHandle(reader, handle))));
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

            TypeDeclarationCandidate? candidate;
            if (!TryReadExportedCandidate(
                    reader,
                    handle,
                    out candidate,
                    out failure))
            {
                return new TypeDeclarationResult.Rejected(failure!);
            }

            AddCandidate(candidates, forwarders, candidate!);
        }

        return Complete(candidates);
    }

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
        List<PendingCandidate> pending)
    {
        if (pending.Count == 0)
            return new TypeDeclarationResult.Missing();

        ImmutableArray<TypeDeclarationCandidate> candidates =
            [.. pending.Select(candidate => candidate.Materialize())];
        if (candidates.Length > 1)
        {
            return new TypeDeclarationResult.Ambiguous(
                candidates);
        }

        return candidates[0] switch
        {
            TypeDeclarationCandidate.Definition definition =>
                new TypeDeclarationResult.Defined(definition.Token),
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
        internal abstract TypeDeclarationCandidate Materialize();
    }

    sealed class PendingValue(TypeDeclarationCandidate value) : PendingCandidate
    {
        internal override TypeDeclarationCandidate Materialize() => value;
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

        internal override TypeDeclarationCandidate Materialize() =>
            new TypeDeclarationCandidate.Forwarder(
                [.. declarations],
                Target);
    }
}
