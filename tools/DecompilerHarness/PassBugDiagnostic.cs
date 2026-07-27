using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

static class PassBugDiagnostic
{
    public static string Format(
        Exception exception,
        string assemblyPath,
        string typeName,
        string methodName,
        MethodSignature signature,
        int metadataToken)
    {
        string identity =
            $"{assemblyPath}!{typeName}::{methodName}{CorpusMethodIdentity.SignatureText(signature)} " +
            $"[token 0x{metadataToken:X8}]";
        return $"PASS BUG: {exception.GetType().Name}: {exception.Message} ({identity})";
    }
}
