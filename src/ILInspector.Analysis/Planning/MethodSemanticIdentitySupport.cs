using System.Reflection.Metadata;

namespace ILInspector.Analysis.Planning;

/// <summary>
/// Receipted semantic-identity work of one Method source, owned by
/// <c>docs/design/method-query-source.md#semantic-identity</c>.
/// </summary>
/// <param name="Constructed">Whether the execution read the module's
/// assembly name and MVID, once, for the identities it decoded.</param>
/// <param name="Work">The decode work: once per physical MethodDef for the
/// physical source, and for each read for a request.</param>
/// <param name="Methods">The MethodDefs whose identity was decoded
/// (physical) or read (request).</param>
/// <param name="Decoded">Identities decoded completely.</param>
/// <param name="SignatureBoundExceeded">Identities whose signature exceeded
/// the safe decoding limits.</param>
/// <param name="Malformed">MethodDefs whose metadata could not be read.</param>
public sealed record MethodSemanticIdentityCoverage(
    bool Constructed,
    MethodSemanticIdentityWork Work,
    MethodDefinitionHandleCoverage Methods,
    int Decoded,
    int SignatureBoundExceeded,
    int Malformed)
{
    public static MethodSemanticIdentityCoverage Empty { get; } =
        new(
            Constructed: false,
            MethodSemanticIdentityWork.Empty,
            MethodDefinitionHandleCoverage.Empty,
            Decoded: 0,
            SignatureBoundExceeded: 0,
            Malformed: 0);
}

/// <summary>
/// Execution-scoped inputs of semantic-identity decoding: the module's
/// assembly name and MVID, read once. The decoding itself is
/// <see cref="MethodSemanticIdentityReader"/>'s.
/// </summary>
internal sealed class MethodSemanticIdentitySupport(
    MetadataReader reader,
    string sourceName)
{
    string? _assemblyName;
    Guid _mvid;

    /// <summary>
    /// Decodes one MethodDef's semantic identity. Unreadable metadata becomes
    /// <see cref="MethodSemanticIdentityResult.Malformed"/>, so a producer
    /// decides whether it fails the unit.
    /// </summary>
    public MethodSemanticIdentityResult Decode(
        TypeDefinitionHandle typeHandle,
        TypeDefinition type,
        MethodDefinitionHandle methodHandle,
        MethodDefinition method,
        out bool constructed,
        out MethodSemanticIdentityWork work)
    {
        constructed = false;
        var counter = new MethodSemanticIdentityReader.WorkCounter();
        MethodSemanticIdentityResult result;
        try
        {
            if (_assemblyName is null)
            {
                _mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
                _assemblyName = reader.IsAssembly
                    ? reader.GetString(reader.GetAssemblyDefinition().Name)
                    : Path.GetFileNameWithoutExtension(sourceName);
                constructed = true;
            }

            GenericScope scope = MethodSemanticIdentityReader.CreateScope(
                reader,
                type,
                method,
                counter);
            result = MethodSemanticIdentityReader.Read(
                reader,
                _assemblyName,
                _mvid,
                typeHandle,
                methodHandle,
                method,
                scope,
                counter);
        }
        catch (Exception ex)
            when (MethodSemanticIdentityReader.IsUnreadableMetadata(ex))
        {
            result = new MethodSemanticIdentityResult.Malformed(
                ex.GetType().Name);
        }

        work = counter.Work;
        return result;
    }
}
