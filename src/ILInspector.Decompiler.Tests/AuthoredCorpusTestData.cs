using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;

using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

static class AuthoredCorpusTestData
{
    public static string CorrelatedRow(string assemblyPath)
        => Correlate(ReadFixture("one-row-authored-corpus.jsonl"), assemblyPath);

    public static string CorrelatedRows(string assemblyPath)
        => string.Join(
            Environment.NewLine,
            File.ReadLines(FixturePath("two-row-authored-corpus.jsonl"))
                .Select(row => Correlate(row, assemblyPath)))
            + Environment.NewLine;

    public static string CorrelateRow(string rowJson, string assemblyPath)
        => Correlate(rowJson, assemblyPath);

    public static string WriteCorrelatedCorpus(string assemblyPath)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"correlated-corpus-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, CorrelatedRow(assemblyPath) + Environment.NewLine);
        return path;
    }

    static string Correlate(string rowJson, string assemblyPath)
    {
        var row = JsonNode.Parse(rowJson)?.AsObject()
            ?? throw new InvalidDataException("The authored-corpus test row is not a JSON object.");
        var identity = AuthoredSourceHarvest.ReadAssemblyIdentity(assemblyPath);
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var reader = pe.GetMetadataReader();
        row["assembly"] = identity.Name;
        row["assemblyVersion"] = identity.Version;
        row["moduleVersionId"] = reader.GetGuid(reader.GetModuleDefinition().Mvid);
        row["metadataToken"] = MetadataTokens.GetToken(
            FindMethodDefinition(reader, row));
        return row.ToJsonString();
    }

    static MethodDefinitionHandle FindMethodDefinition(
        MetadataReader reader,
        JsonObject row)
    {
        string typeName = RequiredString(row, "type");
        string methodName = RequiredString(row, "method");
        int overload = RequiredInt32(row, "overload");
        string signature = RequiredString(row, "signature");
        MethodDefinitionHandle? match = null;

        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (!string.Equals(
                    reader.GetFullTypeName(type),
                    typeName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(methodHandle);
                if (!string.Equals(
                        reader.GetString(method.Name),
                        methodName,
                        StringComparison.Ordinal)
                    || ReturnToSenderSourceProbe.OverloadIndex(
                        reader,
                        type,
                        methodHandle,
                        methodName) != overload
                    || !string.Equals(
                        ReturnToSenderSourceProbe.UniqueTargetSignature(
                            reader,
                            type,
                            methodName,
                            methodHandle),
                        signature,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (match is not null)
                {
                    throw new InvalidDataException(
                        $"The authored-corpus test identity {typeName}::{methodName}#{overload} "
                            + "matches more than one MethodDef.");
                }

                match = methodHandle;
            }
        }

        return match
            ?? throw new InvalidDataException(
                $"The authored-corpus test identity {typeName}::{methodName}#{overload} "
                    + "does not match the copied assembly.");
    }

    static string RequiredString(JsonObject row, string propertyName)
        => row[propertyName]?.GetValue<string>() is { Length: > 0 } value
            ? value
            : throw new InvalidDataException(
                $"The authored-corpus test row requires a non-empty '{propertyName}'.");

    static int RequiredInt32(JsonObject row, string propertyName)
        => row[propertyName]?.GetValue<int>()
            ?? throw new InvalidDataException(
                $"The authored-corpus test row requires an integer '{propertyName}'.");

    static string ReadFixture(string fileName)
        => File.ReadAllText(FixturePath(fileName)).TrimEnd();

    static string FixturePath(string fileName)
        => Path.Combine(
            AuthoredCorpusRatchetTests.FindRepositoryRoot(),
            "tools",
            "DecompilerHarness",
            "corpus",
            fileName);
}
