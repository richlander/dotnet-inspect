using ILInspector.Metadata;

namespace ILInspector.Analysis;

public abstract class TypeCorrespondenceFailure
{
    private protected TypeCorrespondenceFailure()
    {
    }

    public sealed class Resolution : TypeCorrespondenceFailure
    {
        internal Resolution(TypeResolutionOutcome nonSuccess) =>
            NonSuccess = nonSuccess;

        public TypeResolutionOutcome NonSuccess { get; }
    }

    public sealed class DuplicateArtifact : TypeCorrespondenceFailure
    {
        internal DuplicateArtifact(
            DefinitionCorrespondence.IndeterminateDuplicateArtifact evidence) =>
            Evidence = evidence;

        public DefinitionCorrespondence.IndeterminateDuplicateArtifact Evidence
            { get; }
    }

    public sealed class IncomparableCatalogs : TypeCorrespondenceFailure
    {
        internal IncomparableCatalogs(
            AssemblyCatalogId left,
            AssemblyCatalogId right)
        {
            Left = left;
            Right = right;
        }

        public AssemblyCatalogId Left { get; }
        public AssemblyCatalogId Right { get; }
    }

    public sealed class StaleGeneration : TypeCorrespondenceFailure
    {
        internal StaleGeneration(
            AssemblyCatalogGenerationId left,
            AssemblyCatalogGenerationId right)
        {
            Left = left;
            Right = right;
        }

        public AssemblyCatalogGenerationId Left { get; }
        public AssemblyCatalogGenerationId Right { get; }
    }

    public sealed class IncompleteMetadata : TypeCorrespondenceFailure
    {
        internal IncompleteMetadata()
        {
        }
    }
}

public abstract class CandidateTypeRelation
{
    private protected CandidateTypeRelation()
    {
    }

    public sealed class SameDefinition : CandidateTypeRelation
    {
        internal SameDefinition()
        {
        }
    }

    public sealed class DifferentDefinition : CandidateTypeRelation
    {
        internal DifferentDefinition()
        {
        }
    }

    public sealed class Indeterminate : CandidateTypeRelation
    {
        internal Indeterminate(TypeCorrespondenceFailure failure) =>
            Failure = failure;

        public TypeCorrespondenceFailure Failure { get; }
    }
}

/// <summary>
/// Frozen per-origin declaring-type relations reused by scope selection and
/// final call-site matching.
/// </summary>
public sealed class CallerResolutionPlan
{
    readonly MetadataTypeDefinitionName _target;
    readonly IReadOnlyDictionary<
        CandidateReferenceKey,
        CandidateTypeRelation> _relations;
    readonly IReadOnlySet<AssemblyAcquisitionRegistration> _incomplete;

    internal CallerResolutionPlan(
        MetadataTypeDefinitionName target,
        IReadOnlyDictionary<
            CandidateReferenceKey,
            CandidateTypeRelation> relations,
        IReadOnlySet<AssemblyAcquisitionRegistration> incomplete)
    {
        _target = target;
        _relations = relations;
        _incomplete = incomplete;
    }

    /// <summary>Gets the frozen relation for one decoded declaring type.</summary>
    public CandidateTypeRelation GetRelation(
        ResolvedAssemblyReference source,
        TypeRef declaringType)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(declaringType);

        ResolvableTypeReference? reference = declaringType.Resolution;
        if (reference is null)
        {
            return new CandidateTypeRelation.Indeterminate(
                new TypeCorrespondenceFailure.IncompleteMetadata());
        }

        if (!reference.Type.Equals(_target))
            return new CandidateTypeRelation.DifferentDefinition();

        if (_relations.TryGetValue(
                new CandidateReferenceKey(source.Registration, reference),
                out CandidateTypeRelation? relation))
        {
            return relation;
        }

        return _incomplete.Contains(source.Registration)
            ? new CandidateTypeRelation.Indeterminate(
                new TypeCorrespondenceFailure.IncompleteMetadata())
            : new CandidateTypeRelation.DifferentDefinition();
    }

    internal readonly record struct CandidateReferenceKey(
        AssemblyAcquisitionRegistration Source,
        ResolvableTypeReference Reference);
}
