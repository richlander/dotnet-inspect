using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using InertText;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// Projects one classified method row's identity text: its declaring type
/// name, anchor and return type, signature text, and P/Invoke module. These
/// are the functions <see cref="MethodClassificationScanner"/> uses, exposed
/// with no Planning types so a method-row gate can use them unchanged. The
/// caller owns the identity budget.
/// </summary>
public static class MethodRowProjection
{
    public static string FormatDeclaringTypeName(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        if (MetadataTypeDefinitionNameReader.Read(reader, handle)
            is MetadataTypeDefinitionNameReadResult.Read read)
        {
            string displayName =
                TypeResolver.FormatDisplayName(read.Name.Segments);
            return read.Name.Namespace.Length == 0
                ? displayName
                : $"{read.Name.Namespace}.{displayName}";
        }

        return reader.GetFullTypeName(reader.GetTypeDefinition(handle));
    }

    public static string? GetPInvokeModuleName(MetadataReader reader, MethodDefinitionHandle methodHandle)
    {
        var import = reader.GetMethodDefinition(methodHandle).GetImport();
        if (import.Module.IsNil)
            return null;

        var moduleRef = reader.GetModuleReference(import.Module);
        return reader.GetString(moduleRef.Name);
    }

    public static MethodAnchorInfo? TryCreateMethodIdentity(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinition method,
        ref int identityDecodeFailures,
        ref int scanWorkRemaining)
    {
        try
        {
            return ApiMemberIdentity.CreateMethodAnchorInfo(
                reader,
                typeHandle,
                method,
                ref scanWorkRemaining);
        }
        catch (BadImageFormatException ex)
        {
            // One malformed anchor is skippable (null identity on that row). Many
            // hostile methods each paying the per-anchor reject cost — or many
            // near-limit successes drawing down the scan work budget — are not.
            // Gated by MaxClassificationIdentityDecodeFailures and
            // MaxClassificationScanWorkChars.
            // Exhausted scan-level work (including a single near-limit identity that
            // consumed the shared budget) must fail the scan, not soft-skip.
            if (scanWorkRemaining <= 0
                || ex.Message.Contains(
                    "classification scan work budget",
                    StringComparison.Ordinal))
            {
                throw;
            }

            NoteDecodeFailure(ref identityDecodeFailures, ex);
            return null;
        }
    }

    public static void NoteDecodeFailure(
        ref int identityDecodeFailures,
        BadImageFormatException ex)
    {
        identityDecodeFailures++;
        if (identityDecodeFailures
            >= MetadataSafetyPolicy.MaxClassificationIdentityDecodeFailures)
        {
            throw new BadImageFormatException(
                "The assembly exceeds the method-identity decode failure budget during classification scan.",
                ex);
        }
    }

    public static string FormatSignatureOrFallback(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method,
        string methodName,
        MethodAnchorInfo? identity)
    {
        // When identity decode already failed, the blob is hostile or malformed —
        // do not decode it again for display text.
        if (identity is null)
            return methodName + "(...)";

        return FormatSignature(reader, typeDef, method, methodName);
    }

    private static string FormatSignature(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method,
        string methodName)
    {
        try
        {
            var context = GenericContext.ForMethod(reader, typeDef, method);
            var sig = GuardedSignatureText.MethodText(reader, method, context)
                .GetValueOrThrow();
            return SignatureRenderer.RenderDecodedSignature(
                reader,
                method,
                methodName,
                sig,
                context);
        }
        catch
        {
            return methodName + "(...)";
        }
    }

    /// <summary>The longest part (namespace, type, or method name) a failure label shows.</summary>
    public const int MaxFailureLabelPartLength = 256;

    /// <summary>
    /// A presentation label for a failure recorded by <paramref name="methodToken"/>:
    /// <c>Namespace.Type::Method</c>, each part read with its string-heap length
    /// checked first and capped at <see cref="MaxFailureLabelPartLength"/>
    /// characters, with an ellipsis when truncated. It is one bounded decode,
    /// so a hostile name cannot inflate a diagnostic. An unreadable row
    /// falls back to the token.
    /// </summary>
    public static InertString FailureLabel(MetadataReader reader, int methodToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string token = $"0x{methodToken:X8}";
        try
        {
            if (MetadataTokens.EntityHandle(methodToken) is not { Kind: HandleKind.MethodDefinition } entity)
                return new InertString(TextPolicy.Field, token);

            var methodHandle = (MethodDefinitionHandle)entity;
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
            TypeDefinition type = reader.GetTypeDefinition(method.GetDeclaringType());
            string ns = CappedString(reader, type.Namespace);
            string typeName = CappedString(reader, type.Name);
            string methodName = CappedString(reader, method.Name);
            string fullTypeName = ns.Length == 0 ? typeName : $"{ns}.{typeName}";
            return new InertString(TextPolicy.Field, $"{fullTypeName}::{methodName}");
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException or InvalidOperationException)
        {
            return new InertString(TextPolicy.Field, token);
        }
    }

    /// <summary>
    /// Reads at most <see cref="MaxFailureLabelPartLength"/> characters of a
    /// string, checking its encoded length before decoding any of it.
    /// </summary>
    static string CappedString(MetadataReader reader, StringHandle handle)
    {
        if (handle.IsNil)
            return "";

        BlobReader blob = reader.GetBlobReader(handle);
        int maxBytes = MaxFailureLabelPartLength * 4;
        bool truncated = blob.Length > maxBytes;
        string text = blob.ReadUTF8(truncated ? maxBytes : blob.Length);
        if (text.Length > MaxFailureLabelPartLength)
        {
            text = text[..MaxFailureLabelPartLength];
            truncated = true;
        }

        return truncated ? text + "\u2026" : text;
    }
}
