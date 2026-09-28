using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using InertText;

namespace ILInspector.Metadata;

/// <summary>
/// Resource-free forwarding evidence copied from one exact assembly image.
/// </summary>
public sealed record AssemblyTypeForwardingEvidence(
    ImmutableArray<ExportedTypeToken> Declarations,
    AssemblyReferenceIdentity Target);

/// <summary>
/// One detached declaration row enriched only with requested nested facts.
/// </summary>
public sealed record AssemblyTypeDeclarationRow(
    AssemblyTypeDeclaration Declaration,
    string DisplayName,
    string Namespace,
    AssemblyTypeForwardingEvidence? Forwarding,
    int? MemberCount);

public enum AssemblyTypeDeclarationRowsBound
{
    RetainedTextCharacters,
}

/// <summary>The typed result of enriching one declaration segment.</summary>
public abstract record AssemblyTypeDeclarationRowsOutcome
{
    private protected AssemblyTypeDeclarationRowsOutcome()
    {
    }

    public sealed record Read(
        ImmutableArray<AssemblyTypeDeclarationRow> Rows,
        long RetainedTextCharacters)
        : AssemblyTypeDeclarationRowsOutcome;

    public sealed record Incomplete(
        AssemblyTypeDeclarationRowsBound Bound,
        long MeasuredRetainedTextCharacters)
        : AssemblyTypeDeclarationRowsOutcome;

    public sealed record Rejected(CandidateOpenFailure Failure)
        : AssemblyTypeDeclarationRowsOutcome;
}

internal static class AssemblyTypeDeclarationRowsReader
{
    internal static AssemblyTypeDeclarationRowsOutcome Read(
        PEReader peReader,
        AssemblyTypeDeclarationInventory inventory,
        ImmutableArray<AssemblyTypeDeclaration> declarations,
        bool includeMemberCount,
        int maximumRetainedTextCharacters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumRetainedTextCharacters);

        try
        {
            MetadataReader reader =
                MetadataFormatAdmission.GetMetadataReader(peReader);
            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            if (!inventory.Identity.IsEquivalentTo(identity))
            {
                throw new ArgumentException(
                    "The declaration inventory belongs to a different assembly image.",
                    nameof(inventory));
            }

            var rows =
                ImmutableArray.CreateBuilder<AssemblyTypeDeclarationRow>(
                    declarations.Length);
            var referenceProjection =
                new AssemblyReferenceProjectionCache(reader);
            IReadOnlyDictionary<TypeDefinitionToken, int>? memberCounts =
                includeMemberCount
                    ? ApiSurfaceExtractor.CountSummaryMembers(
                        reader,
                        declarations)
                    : null;
            long retainedTextCharacters = 0;
            foreach (AssemblyTypeDeclaration declaration in declarations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string displayName = DisplayName(reader, declaration);
                long measured = checked(
                    retainedTextCharacters
                        + new InertString(
                            TextPolicy.Field,
                            displayName).Length
                        + new InertString(
                            TextPolicy.Field,
                            declaration.Name.Namespace).Length);
                AssemblyTypeForwardingEvidence? forwarding = null;
                int? memberCount = null;

                switch (declaration.Kind)
                {
                    case AssemblyTypeDeclarationKind.Definition:
                        if (includeMemberCount)
                        {
                            TypeDefinitionToken definitionToken =
                                declaration.DefinitionToken
                                ?? throw new InvalidOperationException(
                                    "A Type definition row omitted its locator.");
                            memberCount =
                                memberCounts!.TryGetValue(
                                    definitionToken,
                                    out int count)
                                    ? count
                                    : throw new InvalidOperationException(
                                        "A requested definition Member Count was not measured.");
                        }
                        break;
                    case AssemblyTypeDeclarationKind.Forwarder:
                        ExportedTypeToken token =
                            declaration.ExportedTypeToken
                                ?? throw new InvalidOperationException(
                                    "A forwarder row omitted its locator.");
                        EntityHandle entity =
                            MetadataTokens.EntityHandle(token.Value);
                        if (entity.Kind != HandleKind.ExportedType)
                        {
                            return Rejected(
                                "The selected forwarder locator is not an ExportedType token.");
                        }
                        if (!MetadataTypeDeclarationProbe
                                .TryReadExportedCandidate(
                                    reader,
                                    (ExportedTypeHandle)entity,
                                    referenceProjection,
                                    out TypeDeclarationCandidate? candidate,
                                    out MetadataTypeNameFailure? failure))
                        {
                            return Rejected(failure!.Detail);
                        }
                        if (candidate
                            is not TypeDeclarationCandidate.Forwarder
                                forwarder)
                        {
                            return Rejected(
                                "The selected forwarder locator no longer identifies a forwarder.");
                        }

                        forwarding = new(
                            forwarder.Declarations,
                            forwarder.Target);
                        measured = checked(
                            measured
                                + TextCharacters(forwarder.Target));
                        break;
                    case AssemblyTypeDeclarationKind.ModuleExport:
                        return Rejected(
                            "A module export cannot be projected as a definition or forwarder row.");
                    default:
                        throw new InvalidOperationException(
                            "Unknown Type declaration kind.");
                }

                if (measured > maximumRetainedTextCharacters)
                {
                    return new AssemblyTypeDeclarationRowsOutcome.Incomplete(
                        AssemblyTypeDeclarationRowsBound
                            .RetainedTextCharacters,
                        measured);
                }

                retainedTextCharacters = measured;
                rows.Add(
                    new(
                        declaration,
                        displayName,
                        declaration.Name.Namespace,
                        forwarding,
                        memberCount));
            }

            return new AssemblyTypeDeclarationRowsOutcome.Read(
                rows.MoveToImmutable(),
                retainedTextCharacters);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            return Rejected(
                "The selected declaration row contains malformed Metadata.");
        }
    }

    static long TextCharacters(AssemblyReferenceIdentity identity) =>
        checked(
            new InertString(TextPolicy.Field, identity.Name).Length
                + (identity.Culture is null
                    ? 0
                    : new InertString(
                        TextPolicy.Field,
                        identity.Culture).Length)
                + (identity.PublicKeyToken is null
                    ? 0
                    : new InertString(
                        TextPolicy.Field,
                        identity.PublicKeyToken).Length));

    static string DisplayName(
        MetadataReader reader,
        AssemblyTypeDeclaration declaration)
    {
        bool hasArityMarker =
            declaration.Name.Segments.Any(
                static segment =>
                    segment.Contains('`', StringComparison.Ordinal));
        if (declaration.Kind != AssemblyTypeDeclarationKind.Definition)
        {
            return hasArityMarker
                ? MetadataTypeNameFormatter.FormatFullName(declaration.Name)
                : declaration.Name.ToMetadataFullName();
        }

        TypeDefinitionToken token =
            declaration.DefinitionToken
            ?? throw new InvalidOperationException(
                "A Type definition row omitted its locator.");
        EntityHandle entity = MetadataTokens.EntityHandle(token.Value);
        if (entity.Kind != HandleKind.TypeDefinition)
        {
            throw new BadImageFormatException(
                "A Type definition locator is not a TypeDef token.");
        }

        var handle = (TypeDefinitionHandle)entity;
        TypeDefinition definition = reader.GetTypeDefinition(handle);
        GenericParameterHandleCollection parameters =
            definition.GetGenericParameters();
        if (parameters.Count == 0 && !hasArityMarker)
            return declaration.Name.ToMetadataFullName();

        GenericContext.ValidateParameterIndices(reader, parameters);
        string[] parameterNames =
        [
            .. parameters.Select(
                parameter =>
                        reader.GetString(
                            reader.GetGenericParameter(parameter).Name)),
        ];
        return MetadataTypeNameFormatter.FormatFullName(
            declaration.Name,
            parameterNames,
            MetadataDeclarationQuery.GetIntroducedTypeParameterCounts(
                reader,
                handle));
    }

    static AssemblyTypeDeclarationRowsOutcome.Rejected Rejected(
        string detail) =>
        new(
            new CandidateOpenFailure(
                CandidateOpenFailureKind.InvalidImage,
                detail));
}
