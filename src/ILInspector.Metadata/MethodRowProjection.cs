using System.Reflection.Metadata;
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
}
