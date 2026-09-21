using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.CSharpPrinterBenchmarks;

internal sealed record WorkloadDefinition(
    string Id,
    string Scale,
    string AssemblyPath,
    string TypeName,
    string MethodName);

internal static class WorkloadCatalog
{
    static readonly WorkloadDefinition[] s_all =
    [
        new(
            "small",
            "small",
            typeof(List<>).Assembly.Location,
            "System.Collections.Generic.List`1",
            "get_Count"),
        new(
            "representative",
            "representative",
            typeof(JsonSerializer).Assembly.Location,
            "System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver",
            "PopulatePolymorphismMetadata"),
        new(
            "large",
            "large",
            typeof(JsonSerializer).Assembly.Location,
            "System.Text.Json.Schema.JsonSchemaExporter",
            "MapJsonSchemaCore"),
    ];

    public static IEnumerable<string> Ids => s_all.Select(workload => workload.Id);

    public static IReadOnlyList<WorkloadDefinition> All => s_all;

    public static WorkloadDefinition Get(string id)
        => s_all.Single(
            workload => StringComparer.Ordinal.Equals(workload.Id, id));

    public static int ResolveUniqueMethodToken(WorkloadDefinition definition)
    {
        using FileStream stream = File.OpenRead(definition.AssemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle[] matchingTypes = reader.TypeDefinitions
            .Where(handle => HasFullName(reader, handle, definition.TypeName))
            .ToArray();

        if (matchingTypes is not [var typeHandle])
        {
            throw new InvalidOperationException(
                $"Expected one type named {definition.TypeName}; "
                + $"found {matchingTypes.Length}.");
        }

        MethodDefinitionHandle[] matchingMethods = reader
            .GetTypeDefinition(typeHandle)
            .GetMethods()
            .Where(handle => StringComparer.Ordinal.Equals(
                reader.GetString(reader.GetMethodDefinition(handle).Name),
                definition.MethodName))
            .ToArray();

        if (matchingMethods is not [var methodHandle])
        {
            throw new InvalidOperationException(
                $"Expected one method named "
                + $"{definition.TypeName}.{definition.MethodName}; "
                + $"found {matchingMethods.Length}.");
        }

        return MetadataTokens.GetToken(methodHandle);
    }

    static bool HasFullName(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        string expectedFullName)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        string @namespace = reader.GetString(type.Namespace);
        return StringComparer.Ordinal.Equals(
            @namespace.Length == 0 ? name : $"{@namespace}.{name}",
            expectedFullName);
    }
}

internal static class WorkloadValidator
{
    public static int Run(TextWriter output)
    {
        CSharpPrinterAllocationBenchmarks.ValidateOutputGuard();
        output.WriteLine(
            "workload\tassembly\ttype\tmethod\tmvid\tmethod_token\t"
            + "output_chars\toutput_sha256");

        foreach (WorkloadDefinition definition in WorkloadCatalog.All)
        {
            int methodToken = WorkloadCatalog.ResolveUniqueMethodToken(definition);
            using var source = MetadataSource.Open(definition.AssemblyPath);
            IrFunction raisedFunction = Import(source, definition, methodToken);
            string productPathOutput =
                CSharpPrinterAllocationBenchmarks.RequireOutput(
                    CSharpPrinter.PrintRaised(raisedFunction));
            string renderOnlyOutput =
                CSharpPrinterAllocationBenchmarks.RequireOutput(
                    CSharpPrinter.Print(raisedFunction));
            string repeatedProductPathOutput =
                CSharpPrinterAllocationBenchmarks.RequireOutput(
                    CSharpPrinter.PrintRaised(
                        Import(source, definition, methodToken)));

            if (!StringComparer.Ordinal.Equals(productPathOutput, renderOnlyOutput)
                || !StringComparer.Ordinal.Equals(
                    productPathOutput,
                    repeatedProductPathOutput))
            {
                throw new InvalidOperationException(
                    $"Output validation failed for {definition.Id}.");
            }

            Guid mvid = ReadMvid(definition.AssemblyPath);
            string hash = System.Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(productPathOutput)))
                .ToLowerInvariant();
            output.WriteLine(
                $"{definition.Scale}\t{Path.GetFileName(definition.AssemblyPath)}\t"
                + $"{definition.TypeName}\t{definition.MethodName}\t{mvid:D}\t"
                + $"0x{raisedFunction.MetadataToken:X8}\t"
                + $"{productPathOutput.Length}\t{hash}");
        }

        return 0;
    }

    static Guid ReadMvid(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid);
    }

    static IrFunction Import(
        MetadataSource source,
        WorkloadDefinition definition,
        int methodToken)
    {
        IrFunction function = IrImporter.Import(
            source,
            definition.TypeName,
            definition.MethodName,
            publicOnly: false)
        ?? throw new InvalidOperationException(
            $"Could not import {definition.TypeName}.{definition.MethodName}.");

        return function.MetadataToken == methodToken
            ? function
            : throw new InvalidOperationException(
                $"Imported {definition.TypeName}.{definition.MethodName} "
                + $"as 0x{function.MetadataToken:X8}; "
                + $"expected 0x{methodToken:X8}.");
    }
}
