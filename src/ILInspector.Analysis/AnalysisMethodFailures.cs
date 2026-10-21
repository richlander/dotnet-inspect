using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Analysis;

/// <summary>
/// The recoverable per-method metadata failures Analysis reports as
/// diagnostics, and the label those diagnostics carry.
/// </summary>
internal static class AnalysisMethodFailures
{
    // A generated-expansion bound is recoverable only once its expansion has
    // settled, when a resolver cache can replay it to a later unit.
    internal static bool IsRecoverable(Exception ex) =>
        ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or ArgumentOutOfRangeException
            or IndexOutOfRangeException
            or Planning.MethodDefinitionGeneratedExpansionBoundException
            {
                IsSettled: true,
            };

    internal static string Label(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle)
    {
        try
        {
            var typeDefinition =
                reader.GetTypeDefinition(typeHandle);
            string ns =
                reader.GetString(typeDefinition.Namespace);
            string typeName =
                reader.GetString(typeDefinition.Name);
            string methodName =
                reader.GetString(
                    reader.GetMethodDefinition(
                        methodHandle).Name);
            string fullTypeName =
                ns.Length == 0
                    ? typeName
                    : $"{ns}.{typeName}";
            return $"{fullTypeName}::{methodName}";
        }
        catch (Exception ex)
            when (IsRecoverable(ex))
        {
            return
                $"0x{MetadataTokens.GetToken(methodHandle):X8}";
        }
    }
}
