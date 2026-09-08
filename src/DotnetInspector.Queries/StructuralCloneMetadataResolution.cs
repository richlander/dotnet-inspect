using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>Neutral outcome of one exact TypeDef selection.</summary>
enum StructuralCloneTypeResolutionStatus
{
    Resolved,
    NotFound,
    Ambiguous,
}

/// <summary>Neutral outcome of one exact member selection.</summary>
enum StructuralCloneMemberResolutionStatus
{
    Resolved,
    TypeNotFound,
    TypeAmbiguous,
    MemberNotFound,
    MemberAmbiguous,
}

readonly record struct StructuralCloneTypeResolution(
    TypeDefinitionHandle Handle,
    StructuralCloneTypeResolutionStatus Status);

readonly record struct StructuralCloneMemberResolution(
    MethodDefinitionHandle Method,
    StructuralCloneMemberResolutionStatus Status);

/// <summary>
/// A metadata reader whose TypeDef method ranges are known to partition the
/// MethodDef table.
/// </summary>
/// <remarks>
/// Seed, population, and candidate enumeration accept only this type, so the
/// projection guarantee is established once at image entry rather than
/// re-derived at each projection site. Three review rounds found holes of
/// exactly that second shape.
/// </remarks>
readonly struct StructuralCloneValidatedImage
{
    StructuralCloneValidatedImage(MetadataReader reader) => Reader = reader;

    public MetadataReader Reader { get; }

    public static StructuralCloneValidatedImage Create(PEReader image)
    {
        MetadataReader reader = image.GetMetadataReader();
        StructuralCloneMetadataResolution.ValidateMethodOwnership(reader);
        return new StructuralCloneValidatedImage(reader);
    }
}

/// <summary>
/// Shared exact TypeDef and member selection over one validated image.
/// </summary>
/// <remarks>
/// Both the single-pair retrieval query and the Workspace clone search bind
/// owner-issued exact identities to the same retained content, so the bounded
/// resolution and image-validation rules live here once instead of being
/// duplicated per query. Callers map the neutral outcomes onto their own
/// typed failure vocabularies.
/// </remarks>
static class StructuralCloneMetadataResolution
{
    internal static StructuralCloneMemberResolution ResolveMember(
        MetadataReader reader,
        MetadataTypeDefinitionName typeName,
        MemberAnchor member)
    {
        StructuralCloneTypeResolution type =
            ResolveType(reader, typeName);
        switch (type.Status)
        {
            case StructuralCloneTypeResolutionStatus.NotFound:
                return new StructuralCloneMemberResolution(
                    default,
                    StructuralCloneMemberResolutionStatus.TypeNotFound);
            case StructuralCloneTypeResolutionStatus.Ambiguous:
                return new StructuralCloneMemberResolution(
                    default,
                    StructuralCloneMemberResolutionStatus.TypeAmbiguous);
        }

        MethodDefinitionHandle match = default;
        int matches = 0;
        int inspectedMethods = 0;
        int identityDecodeFailures = 0;
        int anchorWorkRemaining =
            MetadataSafetyPolicy.MaxClassificationScanWorkChars;
        Exception? rejected = null;
        TypeDefinition definition =
            reader.GetTypeDefinition(type.Handle);
        var attributeBudget = new AttributeInspectionBudget();
        bool isExtensionContainer =
            definition.Attributes.HasFlag(TypeAttributes.Abstract)
            && definition.Attributes.HasFlag(TypeAttributes.Sealed)
            && HasExtensionAttribute(
                reader,
                definition.GetCustomAttributes(),
                attributeBudget);
        foreach (MethodDefinitionHandle methodHandle
            in definition.GetMethods())
        {
            inspectedMethods++;
            if (inspectedMethods
                > MetadataSafetyPolicy.MaxCorrespondenceMethodRows)
            {
                throw new BadImageFormatException(
                    "The exact seed member lookup exceeds the MethodDef "
                        + "row budget.");
            }

            MethodDefinition method =
                reader.GetMethodDefinition(methodHandle);
            MemberAnchor anchor;
            try
            {
                bool isExtensionMethod =
                    isExtensionContainer
                    && method.Attributes.HasFlag(
                        MethodAttributes.Static)
                    && HasExtensionAttribute(
                        reader,
                        method.GetCustomAttributes(),
                        attributeBudget);
                anchor = ApiMemberIdentity.CreateMethodAnchorInfo(
                        reader,
                        type.Handle,
                        method,
                        ref anchorWorkRemaining,
                        isExtensionMethod)
                    .Anchor;
            }
            catch (Exception ex) when (IsMalformedMetadata(ex))
            {
                if (ex is AttributeInspectionBudgetException)
                {
                    throw;
                }
                if (anchorWorkRemaining <= 0)
                {
                    throw new BadImageFormatException(
                        "The exact seed member lookup exceeds the "
                            + "anchor-signature work budget.",
                        ex);
                }
                identityDecodeFailures++;
                if (identityDecodeFailures
                    >= MetadataSafetyPolicy
                        .MaxClassificationIdentityDecodeFailures)
                {
                    throw new BadImageFormatException(
                        "The exact seed member lookup exceeds the "
                            + "method-identity decode failure budget.",
                        ex);
                }

                rejected ??= ex;
                continue;
            }

            if (anchor != member)
            {
                continue;
            }

            match = methodHandle;
            matches++;
        }

        // A rejected sibling cannot be shown to decode to a different
        // anchor, so a single healthy match does not establish
        // uniqueness. Surface the metadata failure rather than return a
        // confident result that a successful decode might have made
        // ambiguous.
        if (rejected is not null)
        {
            throw new BadImageFormatException(
                "A MethodDef could not be inspected while resolving "
                    + "the exact seed member.",
                rejected);
        }

        return matches switch
        {
            0 => new StructuralCloneMemberResolution(
                default,
                StructuralCloneMemberResolutionStatus.MemberNotFound),
            1 => new StructuralCloneMemberResolution(
                match,
                StructuralCloneMemberResolutionStatus.Resolved),
            _ => new StructuralCloneMemberResolution(
                default,
                StructuralCloneMemberResolutionStatus.MemberAmbiguous),
        };
    }

    internal static StructuralCloneTypeResolution ResolveType(
        MetadataReader reader,
        MetadataTypeDefinitionName name)
    {
        TypeDefinitionHandle match = default;
        int matches = 0;
        long comparisonWork = Encoding.UTF8.GetByteCount(
            name.Namespace);
        foreach (string segment in name.Segments)
        {
            comparisonWork +=
                Encoding.UTF8.GetByteCount(segment);
        }
        comparisonWork = Math.Max(comparisonWork, 1);
        if (comparisonWork
            > MetadataSafetyPolicy.MaxStructuralSignatureWorkChars)
        {
            throw new BadImageFormatException(
                "The exact TypeDef name exceeds the structural-name "
                    + "work budget.");
        }
        long remainingComparisonWork =
            MetadataSafetyPolicy.MaxStructuralSignatureWorkChars;
        int leafUtf8Length = Encoding.UTF8.GetByteCount(
            name.Segments[^1]);
        int typeNameDecodeFailures = 0;
        MetadataTypeNameFailure? rejected = null;
        foreach (TypeDefinitionHandle candidate
            in reader.TypeDefinitions)
        {
            remainingComparisonWork--;
            if (remainingComparisonWork < 0)
            {
                throw new BadImageFormatException(
                    "The exact TypeDef lookup exceeded its "
                        + "structural-name work budget.");
            }

            int candidateLeafUtf8Length;
            try
            {
                TypeDefinition definition =
                    reader.GetTypeDefinition(candidate);
                candidateLeafUtf8Length =
                    reader.GetBlobReader(definition.Name).Length;
            }
            catch (Exception ex) when (IsMalformedMetadata(ex))
            {
                NoteTypeNameDecodeFailure(
                    ref typeNameDecodeFailures,
                    ex);
                rejected ??=
                    MetadataTypeNameFailure.Malformed(
                        candidate,
                        ex.Message);
                continue;
            }

            remainingComparisonWork -=
                Math.Max(candidateLeafUtf8Length - 1, 0);
            if (remainingComparisonWork < 0)
            {
                throw new BadImageFormatException(
                    "The exact TypeDef lookup exceeded its "
                        + "structural-name work budget.");
            }
            if (candidateLeafUtf8Length != leafUtf8Length)
            {
                continue;
            }

            // Matching a leaf walks the whole declaring chain and scans
            // the walked prefix for cycles, so the comparison this is
            // about to perform costs far more than the names involved.
            // Charge that traversal at its ceiling: the leaf length
            // alone would let a deep shared chain amplify bounded
            // budget into unbounded work.
            remainingComparisonWork -=
                comparisonWork
                + MetadataSafetyPolicy.MaxRelationshipNodes;
            if (remainingComparisonWork < 0)
            {
                throw new BadImageFormatException(
                    "The exact TypeDef lookup exceeded its "
                        + "structural-name work budget.");
            }

            MetadataTypeDefinitionNameMatchResult result =
                MetadataTypeDefinitionName.Matches(
                    reader,
                    candidate,
                    name,
                    out MetadataTypeNameFailure? failure);
            if (result
                == MetadataTypeDefinitionNameMatchResult.Rejected)
            {
                NoteTypeNameDecodeFailure(
                    ref typeNameDecodeFailures);
                rejected ??= failure;
                continue;
            }
            if (result
                != MetadataTypeDefinitionNameMatchResult.Match)
            {
                continue;
            }

            match = candidate;
            matches++;
        }

        if (matches == 0 && rejected is not null)
        {
            throw new BadImageFormatException(
                rejected.Detail);
        }
        return matches switch
        {
            0 => new StructuralCloneTypeResolution(
                default,
                StructuralCloneTypeResolutionStatus.NotFound),
            1 => new StructuralCloneTypeResolution(
                match,
                StructuralCloneTypeResolutionStatus.Resolved),
            _ => new StructuralCloneTypeResolution(
                default,
                StructuralCloneTypeResolutionStatus.Ambiguous),
        };
    }

    static void NoteTypeNameDecodeFailure(
        ref int typeNameDecodeFailures,
        Exception? inner = null)
    {
        typeNameDecodeFailures++;
        if (typeNameDecodeFailures
            >= MetadataSafetyPolicy
                .MaxClassificationIdentityDecodeFailures)
        {
            throw new BadImageFormatException(
                "The exact TypeDef lookup exceeds the "
                    + "type-name decode failure budget.",
                inner);
        }
    }

    /// <summary>
    /// Verifies that the image's TypeDef method ranges partition the
    /// MethodDef table exactly once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-projection checks alone are not sufficient. A corrupt
    /// <c>MethodList</c> start yields an empty range rather than an
    /// error, which would turn a malformed image into a success-shaped
    /// empty population, and a corrupt <c>MethodPtr</c> table can alias
    /// one MethodDef row into two different types without repeating
    /// within either type's own projection.
    /// </para>
    /// <para>
    /// SRM exposes no raw <c>MethodList</c> column, so the partition is
    /// checked by construction: no range may report a negative length,
    /// every projected row must be in range and claimed exactly once,
    /// and the claimed rows must cover the table. This holds for
    /// optimized and unoptimized images alike, because a valid
    /// <c>MethodPtr</c> table is itself a permutation of the MethodDef
    /// rows.
    /// </para>
    /// <para>
    /// Those requirements bound the raw column jointly, and no one of
    /// them does it alone. A descending start is not silent: SRM reports
    /// that range with a negative <c>Count</c> while enumerating
    /// nothing, so rejecting a negative length is what makes the starts
    /// non-decreasing. Coverage then supplies the rest, because with the
    /// starts rising the enumerated total is the projected row count
    /// less the first non-null start plus one; requiring distinct
    /// in-range rows that total the MethodDef row count forces that
    /// first non-null start to row 1 and holds every later start within
    /// <c>projectionRows + 1</c>. A null start is not part of that
    /// chain: ECMA-335 II.22.37 permits it and SRM reports its range as
    /// length zero rather than as the difference to the next start, so
    /// leading nulls neither rise nor break the ordering. Only a
    /// *leading* null is expressible, though. Because each run is
    /// delimited by the following TypeDef's start, a null after a
    /// populated run would end the preceding run before it began, and
    /// the negative length lands on that preceding row rather than on
    /// the null itself. Such a column is malformed and is rejected.
    /// Neither check
    /// is redundant: a descending column passes coverage, and a column
    /// starting past row 1 passes the length check.
    /// </para>
    /// <para>
    /// The projection alone does not prove that permutation, because a
    /// <c>MethodPtr</c> row that no TypeDef range covers is never
    /// projected and so is never checked. An unreachable row still
    /// changes what SRM reports for a reachable method, because
    /// declaring-type lookup scans <c>MethodPtr</c> for the first row
    /// naming a MethodDef and can land on the uncovered row. Requiring
    /// equal row counts closes that gap: with every projected row
    /// distinct, in range, and covering the MethodDef table, equal
    /// counts leave no <c>MethodPtr</c> row uncovered.
    /// </para>
    /// </remarks>
    internal static void ValidateMethodOwnership(MetadataReader reader)
    {
        if (reader.GetTableRowCount(TableIndex.TypeDef) == 0)
        {
            throw new BadImageFormatException(
                "The image declares no TypeDef rows, so it lacks the "
                    + "module pseudo-type that owns module-wide "
                    + "methods.");
        }

        int methodRows = reader.GetTableRowCount(TableIndex.MethodDef);
        int methodPtrRows =
            reader.GetTableRowCount(TableIndex.MethodPtr);
        if (methodPtrRows != 0 && methodPtrRows != methodRows)
        {
            throw new BadImageFormatException(
                "The MethodPtr table is not a permutation of the "
                    + "MethodDef table.");
        }

        var owned = new HashSet<MethodDefinitionHandle>();
        foreach (TypeDefinitionHandle type in reader.TypeDefinitions)
        {
            MethodDefinitionHandleCollection methods =
                reader.GetTypeDefinition(type).GetMethods();

            // Checked before enumerating, because a negative range
            // yields no elements rather than an error.
            if (methods.Count < 0)
            {
                throw new BadImageFormatException(
                    "The TypeDef MethodList column is not a "
                        + "non-decreasing range in the projected "
                        + "method table.");
            }

            foreach (MethodDefinitionHandle method in methods)
            {
                ValidateProjectedMethod(method, methodRows, owned);
            }
        }

        if (owned.Count != methodRows)
        {
            throw new BadImageFormatException(
                "The TypeDef method ranges do not cover the MethodDef "
                    + "table exactly once.");
        }
    }

    /// <summary>
    /// Rejects a projected MethodDef row that is out of range or already
    /// seen, so a malformed projection cannot reach Analysis as an
    /// untyped argument error.
    /// </summary>
    static void ValidateProjectedMethod(
        MethodDefinitionHandle method,
        int methodRows,
        HashSet<MethodDefinitionHandle> projected)
    {
        int row = MetadataTokens.GetRowNumber(method);
        if (row == 0 || row > methodRows)
        {
            throw new BadImageFormatException(
                "A projected MethodDef row falls outside the MethodDef "
                    + "table.");
        }

        if (!projected.Add(method))
        {
            throw new BadImageFormatException(
                "The selected method projection repeats a MethodDef "
                    + "row.");
        }
    }

    static bool HasExtensionAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        AttributeInspectionBudget budget)
    {
        budget.Admit(attributes);
        try
        {
            foreach (CustomAttributeHandle attributeHandle
                in attributes)
            {
                CustomAttribute attribute =
                    reader.GetCustomAttribute(attributeHandle);
                string? attributeTypeName =
                    AttributeReader.GetAttributeTypeName(
                        reader,
                        attribute.Constructor,
                        budget.ObserveMaterialization);
                if (attributeTypeName is null)
                {
                    attributeTypeName =
                        ResolveRejectedAttributeType(
                            reader,
                            attribute.Constructor,
                            budget);
                }

                if (attributeTypeName
                    == KnownAttributeNames.ExtensionAttribute)
                {
                    return true;
                }
            }

            return false;
        }
        catch (AttributeInspectionBudgetSignalException ex)
        {
            // Signature decoding converts BadImageFormatException to a
            // rejection, so the callback uses a private signal until it leaves
            // that guarded boundary.
            throw new AttributeInspectionBudgetException(
                ex.Message,
                ex);
        }
    }

    static string? ResolveRejectedAttributeType(
        MetadataReader reader,
        EntityHandle constructor,
        AttributeInspectionBudget budget)
    {
        EntityHandle type = constructor.Kind switch
        {
            HandleKind.MemberReference =>
                reader.GetMemberReference(
                    (MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition =>
                reader.GetMethodDefinition(
                    (MethodDefinitionHandle)constructor)
                    .GetDeclaringType(),
            _ => default,
        };
        if (type.IsNil)
        {
            return null;
        }

        return TypeResolver.ResolveTypeName(reader, type) switch
        {
            MetadataTypeNameResult.Resolved resolved =>
                ChargeResolvedAttributeType(resolved.Value, budget),
            MetadataTypeNameResult.Absent => null,
            MetadataTypeNameResult.Rejected rejected =>
                throw new BadImageFormatException(
                    rejected.Failure.Detail),
            _ => throw new InvalidOperationException(
                "Unknown metadata type-name result."),
        };
    }

    static string ChargeResolvedAttributeType(
        string value,
        AttributeInspectionBudget budget)
    {
        budget.ObserveMaterialization(
            Encoding.UTF8.GetByteCount(value));
        return value;
    }

    internal static bool IsMalformedMetadata(Exception exception)
        => exception is BadImageFormatException
            or ArgumentOutOfRangeException
            or OverflowException;

    sealed class AttributeInspectionBudget
    {
        const int MinimumRowCharge = 64;
        const string ExceededMessage =
            "The exact seed member lookup exceeds the custom "
                + "attribute work budget.";
        int remaining =
            MetadataSafetyPolicy.MaxStructuralSignatureWorkChars;

        internal void Admit(
            CustomAttributeHandleCollection attributes)
            => Charge(
                (long)attributes.Count * MinimumRowCharge);

        internal void ObserveMaterialization(int work)
        {
            long charge = Math.Max(work, 1);
            if (charge > remaining)
            {
                throw new AttributeInspectionBudgetSignalException(
                    ExceededMessage);
            }

            remaining -= (int)charge;
        }

        void Charge(long work)
        {
            if (work > remaining)
            {
                throw new AttributeInspectionBudgetException(
                    ExceededMessage);
            }

            remaining -= (int)work;
        }
    }

    sealed class AttributeInspectionBudgetSignalException(string message)
        : Exception(message);

    sealed class AttributeInspectionBudgetException(
        string message,
        Exception? innerException = null)
        : BadImageFormatException(message, innerException);
}
