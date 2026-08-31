using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis;

/// <summary>
/// Portable identity for the physical module a structural clone comparison's
/// method belongs to. <see cref="MetadataMethodAddress"/> alone is
/// reader-scoped: its module version id is not a cryptographic identity, and a
/// serialized document has no live reader to bound that risk the way
/// <see cref="MetadataMethodAddress.BelongsTo"/> does in-process. Pairing the
/// module version id with a file name and content hash gives a portable
/// consumer enough evidence to detect a mismatched or substituted module
/// without re-deriving it. This mirrors the harness-only
/// <c>StructuralCloneCoreLibArtifact</c> identity shape
/// (<c>tools/AnalysisHarness/StructuralCloneCoreLibCorpus.cs</c>), promoted
/// into the product because a portable document needs it too.
/// </summary>
public sealed record StructuralCloneModuleIdentity
{
    public StructuralCloneModuleIdentity(
        string FileName,
        string Sha256,
        Guid ModuleVersionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(Sha256);
        if (Sha256.Length != 64 || !IsLowerHex(Sha256))
        {
            throw new ArgumentException(
                "A module identity's content hash must be a lowercase 64-character SHA-256 hex string.",
                nameof(Sha256));
        }
        if (ModuleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A module identity requires a non-empty module version id.",
                nameof(ModuleVersionId));
        }

        this.FileName = FileName;
        this.Sha256 = Sha256;
        this.ModuleVersionId = ModuleVersionId;
    }

    /// <summary>The module's file name, retained for display; not a path.</summary>
    public string FileName { get; }

    /// <summary>Lowercase hex SHA-256 over the module's entire image bytes.</summary>
    public string Sha256 { get; }

    /// <summary>The module version id read from the module's own metadata.</summary>
    public Guid ModuleVersionId { get; }

    /// <summary>Computes a module identity from a retained managed PE image.</summary>
    public static StructuralCloneModuleIdentity Create(
        string fileName,
        PEReader image,
        MetadataReader reader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(reader);
        MetadataReader admittedReader =
            MetadataFormatAdmission.GetMetadataReader(image);

        byte[] hash = SHA256.HashData(image.GetEntireImage().GetContent().AsSpan());
        Guid moduleVersionId = admittedReader.GetGuid(
            admittedReader.GetModuleDefinition().Mvid);
        return new(fileName, Convert.ToHexStringLower(hash), moduleVersionId);
    }

    static bool IsLowerHex(string value)
    {
        foreach (char c in value)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                return false;
        }
        return true;
    }
}

/// <summary>
/// Portable, product-issued pairwise detail over one <see cref="StructuralCloneComparison"/>.
/// </summary>
/// <remarks>
/// <para>
/// Construction reissues the embedded fields through the same
/// <see cref="StructuralCloneComparison.Completed"/>/
/// <see cref="StructuralCloneComparison.NotCompleted"/> factories live
/// construction uses, so a tampered or internally inconsistent field
/// combination fails the identical invariant checks a fresh comparison would.
/// This validates internal consistency only. It does not re-verify that the
/// embedded correspondence is actually graph-correct against the original IL:
/// that requires a live <see cref="PEReader"/> and
/// <see cref="StructuralCloneAnalysis.Compare(PEReader, MethodDefinitionHandle, MethodDefinitionHandle, StructuralCloneComparisonLimits?)"/>,
/// which is what <see cref="Create"/> performs at authoring time. Full replay
/// from serialized normalized-graph facts alone is not implemented by this
/// slice.
/// </para>
/// <para>
/// <see cref="StructuralCloneAnalysis.Compare(PEReader, MethodDefinitionHandle, MethodDefinitionHandle, StructuralCloneComparisonLimits?)"/>
/// is deliberately A-vs-A: both method handles come from one retained image.
/// This document enforces the same boundary by requiring <see cref="Left"/>
/// and <see cref="Right"/> to share one module version id; cross-module
/// (A-vs-B) identity is a separate, not-yet-built capability.
/// </para>
/// </remarks>
public sealed record StructuralCloneComparisonDocument
{
    /// <summary>Current JSON shape version.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Current comparison methodology version.</summary>
    public const int CurrentMethodologyVersion = 1;

    /// <summary>Creates and validates one portable pairwise clone comparison.</summary>
    public StructuralCloneComparisonDocument(
        int SchemaVersion,
        int MethodologyVersion,
        StructuralCloneModuleIdentity Left,
        StructuralCloneModuleIdentity Right,
        int LeftToken,
        int RightToken,
        StructuralCloneDisposition Disposition,
        StructuralCloneRelation? Relation,
        StructuralCloneCorrespondence? Correspondence,
        StructuralCloneAlignment? Alignment,
        ImmutableArray<StructuralCloneBlocker> Blockers,
        StructuralCloneVerificationReceipt Receipt,
        StructuralCloneAlignmentReceipt? AlignmentReceipt)
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SchemaVersion),
                "Structural clone comparison document schema version is unsupported.");
        }
        if (MethodologyVersion != CurrentMethodologyVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MethodologyVersion),
                "Structural clone comparison document methodology version is unsupported.");
        }
        ArgumentNullException.ThrowIfNull(Left);
        ArgumentNullException.ThrowIfNull(Right);
        // A module version id alone is not sufficient: MVIDs are not guaranteed globally
        // unique (see MetadataMethodAddress.cs), so two byte-distinct modules could otherwise
        // slip past this A-vs-A boundary while carrying the very content hashes meant to
        // catch a substituted module. Require the full identity, hash included, to match.
        if (Left != Right)
        {
            throw new ArgumentException(
                "A structural clone comparison document requires both sides to originate from the same module (matching file name, content hash, and module version id); cross-module comparison is not yet supported.",
                nameof(Right));
        }
        if (Blockers.IsDefault)
        {
            throw new ArgumentException(
                "Structural clone comparison document blockers must be initialized.",
                nameof(Blockers));
        }
        ArgumentNullException.ThrowIfNull(Receipt);
        if (!IsMethodDefinitionToken(LeftToken))
        {
            throw new ArgumentException(
                "Structural clone comparison document tokens must be MethodDef tokens.",
                nameof(LeftToken));
        }
        if (!IsMethodDefinitionToken(RightToken))
        {
            throw new ArgumentException(
                "Structural clone comparison document tokens must be MethodDef tokens.",
                nameof(RightToken));
        }

        Comparison = Disposition == StructuralCloneDisposition.Completed
            ? StructuralCloneComparison.Completed(
                MakeAddress(Left, LeftToken),
                MakeAddress(Right, RightToken),
                Relation
                    ?? throw new ArgumentException(
                        "A completed structural clone comparison document requires a relation.",
                        nameof(Relation)),
                Correspondence,
                Receipt,
                Alignment,
                AlignmentReceipt)
            : StructuralCloneComparison.NotCompleted(
                MakeAddress(Left, LeftToken),
                MakeAddress(Right, RightToken),
                Disposition,
                Blockers,
                Receipt,
                AlignmentReceipt);

        this.SchemaVersion = SchemaVersion;
        this.MethodologyVersion = MethodologyVersion;
        this.Left = Left;
        this.Right = Right;
        this.LeftToken = LeftToken;
        this.RightToken = RightToken;
    }

    /// <summary>JSON shape version.</summary>
    public int SchemaVersion { get; }

    /// <summary>Comparison methodology version.</summary>
    public int MethodologyVersion { get; }

    /// <summary>Portable identity of the left method's module.</summary>
    public StructuralCloneModuleIdentity Left { get; }

    /// <summary>Portable identity of the right method's module.</summary>
    public StructuralCloneModuleIdentity Right { get; }

    /// <summary>The left method's MethodDef metadata token.</summary>
    public int LeftToken { get; }

    /// <summary>The right method's MethodDef metadata token.</summary>
    public int RightToken { get; }

    /// <summary>The execution disposition of this comparison.</summary>
    public StructuralCloneDisposition Disposition => Comparison.Disposition;

    /// <summary>The structural relationship, present only when completed.</summary>
    public StructuralCloneRelation? Relation => Comparison.Relation;

    /// <summary>Block/local correspondence, present only when exact.</summary>
    public StructuralCloneCorrespondence? Correspondence => Comparison.Correspondence;

    /// <summary>One-edit alignment, present only when near.</summary>
    public StructuralCloneAlignment? Alignment => Comparison.Alignment;

    /// <summary>Visible unsupported, limit, or failure receipts.</summary>
    public ImmutableArray<StructuralCloneBlocker> Blockers => Comparison.Blockers;

    /// <summary>Bounded-work receipt for exact verification.</summary>
    public StructuralCloneVerificationReceipt Receipt => Comparison.Receipt;

    /// <summary>Bounded-work receipt for near alignment, when attempted.</summary>
    public StructuralCloneAlignmentReceipt? AlignmentReceipt => Comparison.AlignmentReceipt;

    /// <summary>
    /// The reissued product comparison. Internal: it exists to prove the
    /// document's fields reconstruct a valid <see cref="StructuralCloneComparison"/>,
    /// not as additional wire shape.
    /// </summary>
    internal StructuralCloneComparison Comparison { get; }

    /// <summary>Issues a portable document from a product-owned comparison and its module identities.</summary>
    public static StructuralCloneComparisonDocument Create(
        StructuralCloneComparison comparison,
        StructuralCloneModuleIdentity left,
        StructuralCloneModuleIdentity right)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (comparison.Left.ModuleVersionId != left.ModuleVersionId)
        {
            throw new ArgumentException(
                "Left module identity does not match the comparison's left method module.",
                nameof(left));
        }
        if (comparison.Right.ModuleVersionId != right.ModuleVersionId)
        {
            throw new ArgumentException(
                "Right module identity does not match the comparison's right method module.",
                nameof(right));
        }

        return new(
            CurrentSchemaVersion,
            CurrentMethodologyVersion,
            left,
            right,
            comparison.Left.Token,
            comparison.Right.Token,
            comparison.Disposition,
            comparison.Relation,
            comparison.Correspondence,
            comparison.Alignment,
            comparison.Blockers,
            comparison.Receipt,
            comparison.AlignmentReceipt);
    }

    static MetadataMethodAddress MakeAddress(
        StructuralCloneModuleIdentity identity,
        int token)
        => new(
            identity.ModuleVersionId,
            MetadataTokens.MethodDefinitionHandle(token));

    /// <summary>
    /// <see cref="MetadataTokens.MethodDefinitionHandle(int)"/> masks off a token's table
    /// bits and keeps only the row number, so a non-MethodDef token (or a nil token) would
    /// otherwise silently round-trip into a plausible-looking MethodDef handle while the
    /// document's own <see cref="LeftToken"/>/<see cref="RightToken"/> retained the original,
    /// differently-tabled value. Reject anything but a non-nil MethodDef token up front so the
    /// document's stored token and its reissued handle can never disagree.
    /// </summary>
    static bool IsMethodDefinitionToken(int token)
        => unchecked((uint)token & 0xFF000000) == 0x06000000
            && ((uint)token & 0x00FFFFFF) != 0;
}
