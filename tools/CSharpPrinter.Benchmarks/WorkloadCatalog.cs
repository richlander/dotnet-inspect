using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
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
            typeof(CSharpPrinter).Assembly.Location,
            typeof(CSharpPrinter).FullName!,
            "AppendContainer"),
        new(
            "large",
            "large",
            typeof(CSharpPrinter).Assembly.Location,
            typeof(CSharpPrinter).FullName!,
            "AppendStatementCore"),
    ];

    public static IEnumerable<string> Ids => s_all.Select(workload => workload.Id);

    public static IReadOnlyList<WorkloadDefinition> All => s_all;

    public static WorkloadDefinition Get(string id)
        => s_all.Single(
            workload => StringComparer.Ordinal.Equals(workload.Id, id));
}

internal static class WorkloadValidator
{
    public static int Run(TextWriter output)
    {
        output.WriteLine(
            "workload\tassembly\ttype\tmethod\tmvid\tmethod_token\t"
            + "output_chars\toutput_sha256");

        foreach (WorkloadDefinition definition in WorkloadCatalog.All)
        {
            using var source = MetadataSource.Open(definition.AssemblyPath);
            IrFunction raisedFunction = Import(source, definition);
            string productPathOutput =
                CSharpPrinterAllocationBenchmarks.RequireOutput(
                    CSharpPrinter.PrintRaised(raisedFunction));
            string renderOnlyOutput =
                CSharpPrinterAllocationBenchmarks.RequireOutput(
                    CSharpPrinter.Print(raisedFunction));
            string repeatedProductPathOutput =
                CSharpPrinterAllocationBenchmarks.RequireOutput(
                    CSharpPrinter.PrintRaised(Import(source, definition)));

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
        WorkloadDefinition definition)
        => IrImporter.Import(
            source,
            definition.TypeName,
            definition.MethodName,
            publicOnly: false)
        ?? throw new InvalidOperationException(
            $"Could not import {definition.TypeName}.{definition.MethodName}.");
}
