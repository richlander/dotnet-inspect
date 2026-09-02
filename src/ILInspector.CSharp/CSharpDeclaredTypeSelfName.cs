using CSharpText;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

/// <summary>Why an exact declared-type self-name cannot be represented.</summary>
public abstract record CSharpDeclaredTypeSelfNameFailureReason
{
    private CSharpDeclaredTypeSelfNameFailureReason()
    {
    }

    /// <summary>Exact arity evidence disagrees with the declared type shape.</summary>
    public sealed record ArityMismatch : CSharpDeclaredTypeSelfNameFailureReason
    {
        internal ArityMismatch()
        {
        }
    }

    /// <summary>The arity-free leaf is not an exact C# declaration identifier.</summary>
    public sealed record IdentifierNotAdmitted : CSharpDeclaredTypeSelfNameFailureReason
    {
        internal IdentifierNotAdmitted(
            CSharpTypeDeclarationIdentifierRefusalReason reason)
            => Reason = reason;

        public CSharpTypeDeclarationIdentifierRefusalReason Reason { get; }
    }
}

/// <summary>One exact declared-type identity and its self-name refusal.</summary>
public sealed record CSharpDeclaredTypeSelfNameFailure
{
    internal CSharpDeclaredTypeSelfNameFailure(
        MetadataTypeDefinitionName identity,
        CSharpDeclaredTypeSelfNameFailureReason reason)
    {
        Identity = identity;
        Reason = reason;
    }

    public MetadataTypeDefinitionName Identity { get; }

    public CSharpDeclaredTypeSelfNameFailureReason Reason { get; }
}

internal abstract record CSharpDeclaredTypeSelfNameAdmission
{
    private CSharpDeclaredTypeSelfNameAdmission()
    {
    }

    internal sealed record Admitted : CSharpDeclaredTypeSelfNameAdmission
    {
        internal Admitted(
            MetadataTypeDefinitionName identity,
            string identifier)
        {
            Identity = identity;
            Identifier = identifier;
        }

        internal MetadataTypeDefinitionName Identity { get; }

        internal string Identifier { get; }
    }

    internal sealed record Unrepresentable : CSharpDeclaredTypeSelfNameAdmission
    {
        internal Unrepresentable(CSharpDeclaredTypeSelfNameFailure failure)
            => Failure = failure;

        internal CSharpDeclaredTypeSelfNameFailure Failure { get; }
    }
}

internal static class CSharpDeclaredTypeSelfName
{
    internal static CSharpDeclaredTypeSelfNameAdmission Admit(
        MetadataTypeDefinitionName identity,
        IReadOnlyList<int> introducedTypeParameterCounts,
        IReadOnlyList<TypeParameter> leafTypeParameters)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(introducedTypeParameterCounts);
        ArgumentNullException.ThrowIfNull(leafTypeParameters);

        if (introducedTypeParameterCounts.Count != identity.Segments.Length
            || introducedTypeParameterCounts.Any(static count => count < 0))
        {
            return ArityMismatch(identity);
        }

        string leaf = identity.Segments[^1];
        int introducedCount = introducedTypeParameterCounts[^1];
        bool hasCanonicalArity = MetadataNameArity.TryReadSuffix(
            leaf,
            out int canonicalArity,
            out int simpleNameLength);
        if ((introducedCount == 0
                ? hasCanonicalArity
                : !hasCanonicalArity || canonicalArity != introducedCount)
            || leafTypeParameters.Count != introducedCount)
        {
            return ArityMismatch(identity);
        }

        string simpleName = hasCanonicalArity
            ? leaf[..simpleNameLength]
            : leaf;
        return CSharpIdentifier.AdmitTypeDeclaration(simpleName) switch
        {
            CSharpTypeDeclarationIdentifierAdmission.Admitted admitted =>
                new CSharpDeclaredTypeSelfNameAdmission.Admitted(
                    identity,
                    admitted.Spelling),
            CSharpTypeDeclarationIdentifierAdmission.Refused refused =>
                new CSharpDeclaredTypeSelfNameAdmission.Unrepresentable(
                    new CSharpDeclaredTypeSelfNameFailure(
                        identity,
                        new CSharpDeclaredTypeSelfNameFailureReason.IdentifierNotAdmitted(
                            refused.Reason))),
            _ => throw new InvalidOperationException()
        };
    }

    static CSharpDeclaredTypeSelfNameAdmission.Unrepresentable ArityMismatch(
        MetadataTypeDefinitionName identity)
        => new(
            new CSharpDeclaredTypeSelfNameFailure(
                identity,
                new CSharpDeclaredTypeSelfNameFailureReason.ArityMismatch()));
}
