using System.Collections.Immutable;
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

    /// <summary>
    /// The exact member exists and is one logical member, but it occupies no
    /// MethodDef. A property or event whose <c>MethodSemantics</c> rows are
    /// absent owns no method body, and a field owns none by construction, so
    /// either selects an empty body population rather than an arbitrary one.
    /// </summary>
    MemberHasNoMethodBody,
}

readonly record struct StructuralCloneTypeResolution(
    TypeDefinitionHandle Handle,
    StructuralCloneTypeResolutionStatus Status);

readonly record struct StructuralCloneMemberResolution(
    MethodDefinitionHandle Method,
    StructuralCloneMemberResolutionStatus Status);

/// <summary>
/// Every exact method body one selected member occupies, in MethodDef row
/// order.
/// </summary>
readonly record struct StructuralCloneMemberBodyResolution(
    ImmutableArray<MethodDefinitionHandle> Methods,
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
    /// <summary>
    /// Selects the one MethodDef carrying the exact member identity. Only a
    /// method anchor resolves; a logical member that occupies several bodies
    /// is not a single-body selection.
    /// </summary>
    internal static StructuralCloneMemberResolution ResolveMember(
        MetadataReader reader,
        MetadataTypeDefinitionName typeName,
        MemberAnchor member)
    {
        StructuralCloneMemberBodyResolution resolved =
            ResolveMemberCore(
                reader,
                typeName,
                member,
                expandLogicalMembers: false);
        return new StructuralCloneMemberResolution(
            resolved.Methods.IsDefaultOrEmpty
                ? default
                : resolved.Methods[0],
            resolved.Status);
    }

    /// <summary>
    /// Selects every exact method body the selected member occupies.
    /// </summary>
    /// <remarks>
    /// A method anchor selects its own single body, so an explicit accessor
    /// selection stays one body. A property or event anchor selects the bodies
    /// its <c>MethodSemantics</c> rows associate with it — getter, setter,
    /// adder, remover, raiser, and any other associated accessor — because
    /// those are the exact bodies that logical member occupies. A field is a
    /// supported Member subject that occupies no method body at all, so it is
    /// selected here to distinguish a field that exists from a member that
    /// does not. Ambiguity and malformed-metadata behavior are unchanged: a
    /// repeated exact identity is ambiguous, and an identity that cannot be
    /// decoded is a metadata failure rather than a confident selection.
    /// </remarks>
    internal static StructuralCloneMemberBodyResolution ResolveMemberBodies(
        MetadataReader reader,
        MetadataTypeDefinitionName typeName,
        MemberAnchor member)
        => ResolveMemberCore(
            reader,
            typeName,
            member,
            expandLogicalMembers: true);

    static StructuralCloneMemberBodyResolution ResolveMemberCore(
        MetadataReader reader,
        MetadataTypeDefinitionName typeName,
        MemberAnchor member,
        bool expandLogicalMembers)
    {
        StructuralCloneTypeResolution type =
            ResolveType(reader, typeName);
        switch (type.Status)
        {
            case StructuralCloneTypeResolutionStatus.NotFound:
                return new StructuralCloneMemberBodyResolution(
                    [],
                    StructuralCloneMemberResolutionStatus.TypeNotFound);
            case StructuralCloneTypeResolutionStatus.Ambiguous:
                return new StructuralCloneMemberBodyResolution(
                    [],
                    StructuralCloneMemberResolutionStatus.TypeAmbiguous);
        }

        var bodies =
            ImmutableArray.CreateBuilder<MethodDefinitionHandle>();
        int matches = 0;
        int inspectedRows = 0;
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
            ChargeMemberRow(ref inspectedRows);
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

                NoteIdentityDecodeFailure(
                    ex,
                    anchorWorkRemaining,
                    ref identityDecodeFailures,
                    ref rejected);
                continue;
            }

            if (anchor != member)
            {
                continue;
            }

            bodies.Add(methodHandle);
            matches++;
        }

        // A logical member occupies the bodies its MethodSemantics rows
        // associate with it. Physical accessor projection belongs to the
        // metadata owner, so the association is read through SRM's accessor
        // projection rather than re-derived from accessor name conventions.
        if (expandLogicalMembers)
        {
            int methodRowCount =
                reader.GetTableRowCount(TableIndex.MethodDef);
            foreach (PropertyDefinitionHandle propertyHandle
                in definition.GetProperties())
            {
                ChargeMemberRow(ref inspectedRows);
                PropertyDefinition property =
                    reader.GetPropertyDefinition(propertyHandle);
                MemberAnchor anchor;
                try
                {
                    anchor = ApiMemberIdentity.CreatePropertyAnchor(
                        reader,
                        type.Handle,
                        property,
                        ref anchorWorkRemaining);
                }
                catch (Exception ex) when (IsMalformedMetadata(ex))
                {
                    NoteIdentityDecodeFailure(
                        ex,
                        anchorWorkRemaining,
                        ref identityDecodeFailures,
                        ref rejected);
                    continue;
                }

                if (anchor != member)
                {
                    continue;
                }

                PropertyAccessors accessors = property.GetAccessors();
                AddAccessor(
                    reader,
                    accessors.Getter,
                    type.Handle,
                    methodRowCount,
                    bodies);
                AddAccessor(
                    reader,
                    accessors.Setter,
                    type.Handle,
                    methodRowCount,
                    bodies);
                foreach (MethodDefinitionHandle other in accessors.Others)
                {
                    AddAccessor(
                        reader,
                        other,
                        type.Handle,
                        methodRowCount,
                        bodies);
                }

                matches++;
            }

            foreach (EventDefinitionHandle eventHandle
                in definition.GetEvents())
            {
                ChargeMemberRow(ref inspectedRows);
                EventDefinition eventDefinition =
                    reader.GetEventDefinition(eventHandle);
                MemberAnchor anchor;
                try
                {
                    anchor = ApiMemberIdentity.CreateEventAnchor(
                        reader,
                        type.Handle,
                        eventDefinition,
                        ref anchorWorkRemaining);
                }
                catch (Exception ex) when (IsMalformedMetadata(ex))
                {
                    NoteIdentityDecodeFailure(
                        ex,
                        anchorWorkRemaining,
                        ref identityDecodeFailures,
                        ref rejected);
                    continue;
                }

                if (anchor != member)
                {
                    continue;
                }

                EventAccessors accessors = eventDefinition.GetAccessors();
                AddAccessor(
                    reader,
                    accessors.Adder,
                    type.Handle,
                    methodRowCount,
                    bodies);
                AddAccessor(
                    reader,
                    accessors.Remover,
                    type.Handle,
                    methodRowCount,
                    bodies);
                AddAccessor(
                    reader,
                    accessors.Raiser,
                    type.Handle,
                    methodRowCount,
                    bodies);
                foreach (MethodDefinitionHandle other in accessors.Others)
                {
                    AddAccessor(
                        reader,
                        other,
                        type.Handle,
                        methodRowCount,
                        bodies);
                }

                matches++;
            }

            // A field is a supported Member subject that occupies no method
            // body. Scanning fields is what makes a bodyless field the typed
            // "member exists, has no body" outcome instead of a member the
            // selected type does not declare.
            foreach (FieldDefinitionHandle fieldHandle
                in definition.GetFields())
            {
                ChargeMemberRow(ref inspectedRows);
                FieldDefinition field =
                    reader.GetFieldDefinition(fieldHandle);
                MemberAnchor anchor;
                try
                {
                    anchor = ApiMemberIdentity.CreateFieldAnchor(
                        reader,
                        type.Handle,
                        field,
                        ref anchorWorkRemaining);
                }
                catch (Exception ex) when (IsMalformedMetadata(ex))
                {
                    NoteIdentityDecodeFailure(
                        ex,
                        anchorWorkRemaining,
                        ref identityDecodeFailures,
                        ref rejected);
                    continue;
                }

                if (anchor != member)
                {
                    continue;
                }

                matches++;
            }
        }

        // A rejected sibling cannot be shown to decode to a different
        // anchor, so a single healthy match does not establish
        // uniqueness. Surface the metadata failure rather than return a
        // confident result that a successful decode might have made
        // ambiguous.
        if (rejected is not null)
        {
            throw new BadImageFormatException(
                "A member could not be inspected while resolving "
                    + "the exact seed member.",
                rejected);
        }

        if (matches == 0)
        {
            return new StructuralCloneMemberBodyResolution(
                [],
                StructuralCloneMemberResolutionStatus.MemberNotFound);
        }
        if (matches > 1)
        {
            return new StructuralCloneMemberBodyResolution(
                [],
                StructuralCloneMemberResolutionStatus.MemberAmbiguous);
        }

        ImmutableArray<MethodDefinitionHandle> methods =
        [
            .. bodies
                .Distinct()
                .OrderBy(static handle =>
                    MetadataTokens.GetRowNumber(handle)),
        ];
        return methods.IsEmpty
            ? new StructuralCloneMemberBodyResolution(
                [],
                StructuralCloneMemberResolutionStatus
                    .MemberHasNoMethodBody)
            : new StructuralCloneMemberBodyResolution(
                methods,
                StructuralCloneMemberResolutionStatus.Resolved);
    }

    static void ChargeMemberRow(ref int inspectedRows)
    {
        inspectedRows++;
        if (inspectedRows
            > MetadataSafetyPolicy.MaxCorrespondenceMethodRows)
        {
            throw new BadImageFormatException(
                "The exact seed member lookup exceeds the member "
                    + "row budget.");
        }
    }

    static void NoteIdentityDecodeFailure(
        Exception failure,
        int anchorWorkRemaining,
        ref int identityDecodeFailures,
        ref Exception? rejected)
    {
        if (anchorWorkRemaining <= 0)
        {
            throw new BadImageFormatException(
                "The exact seed member lookup exceeds the "
                    + "anchor-signature work budget.",
                failure);
        }

        identityDecodeFailures++;
        if (identityDecodeFailures
            >= MetadataSafetyPolicy
                .MaxClassificationIdentityDecodeFailures)
        {
            throw new BadImageFormatException(
                "The exact seed member lookup exceeds the "
                    + "member-identity decode failure budget.",
                failure);
        }

        rejected ??= failure;
    }

    /// <summary>
    /// Records one associated accessor body, rejecting an association that
    /// does not name a MethodDef the selected type declares.
    /// </summary>
    /// <remarks>
    /// SRM's accessor projection returns the raw <c>MethodSemantics</c>
    /// method, so an out-of-range row is malformed metadata rather than an
    /// absent accessor. Being in range is not sufficient: a member of the
    /// selected type cannot be implemented by a body another type owns, so an
    /// accessor associated across types is malformed metadata too, not a body
    /// of the selected member. Declaring-type ownership is read through SRM
    /// over an image whose TypeDef method ranges
    /// <see cref="StructuralCloneValidatedImage"/> already established as a
    /// partition of the MethodDef table, which is what makes that lookup
    /// answer for exactly one type.
    /// </remarks>
    static void AddAccessor(
        MetadataReader reader,
        MethodDefinitionHandle handle,
        TypeDefinitionHandle declaringType,
        int methodRowCount,
        ImmutableArray<MethodDefinitionHandle>.Builder bodies)
    {
        if (handle.IsNil)
        {
            return;
        }

        int row = MetadataTokens.GetRowNumber(handle);
        if (row < 1 || row > methodRowCount)
        {
            throw new BadImageFormatException(
                "A MethodSemantics accessor references a MethodDef that "
                    + "does not exist.");
        }

        if (reader.GetMethodDefinition(handle).GetDeclaringType()
            != declaringType)
        {
            throw new BadImageFormatException(
                "A MethodSemantics accessor references a MethodDef that "
                    + "another type declares.");
        }

        bodies.Add(handle);
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
