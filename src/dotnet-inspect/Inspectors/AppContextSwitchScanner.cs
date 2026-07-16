using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal static class AppContextSwitchScanner
{
    public static List<SwitchInfo> Scan(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
            return [];

        var reader = peReader.GetMetadataReader();
        return Scan(peReader, reader);
    }

    internal static List<SwitchInfo> Scan(PEReader peReader, MetadataReader reader)
    {
        Dictionary<string, SwitchInfo> switches = new(StringComparer.Ordinal);
        var resolver = new MetadataOperandNameResolver(reader);

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            var typeName = reader.GetFullTypeName(type);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var instructions = InstructionProducer.Disassemble(peReader, method, resolver);
                if (instructions is not { Count: > 0 })
                    continue;

                string? lastString = null;
                foreach (var instruction in instructions)
                {
                    if (instruction.OpCodeName == "ldstr" && instruction.Operand is { } operand)
                    {
                        lastString = Unquote(operand);
                        continue;
                    }

                    if (instruction.OpCodeName is not ("call" or "callvirt")
                        || instruction.Operand is not { } call
                        || !call.Contains("System.AppContext::", StringComparison.Ordinal)
                        || lastString is not { Length: > 0 } switchName)
                        continue;

                    if (call.Contains("::TryGetSwitch(", StringComparison.Ordinal)
                        || call.Contains("::SetSwitch(", StringComparison.Ordinal))
                    {
                        AddSwitch(
                            switches,
                            switchName,
                            $"{TypeResolver.FormatDisplayName(typeName)}.{reader.GetString(method.Name)}(...)");
                    }
                }
            }
        }

        return switches.Values
            .OrderBy(s => s.Kind, StringComparer.Ordinal)
            .ThenBy(s => s.Switch, StringComparer.Ordinal)
            .ThenBy(s => s.Api, StringComparer.Ordinal)
            .ToList();
    }

    static void AddSwitch(
        Dictionary<string, SwitchInfo> switches,
        string switchName,
        string api)
    {
        if (switchName.StartsWith("System.Resources.UseSystemResourceKeys", StringComparison.Ordinal)
            || switchName.StartsWith("TestSwitch.", StringComparison.Ordinal)
            || switchName.StartsWith("Switch.", StringComparison.Ordinal))
        {
            return;
        }

        const string kind = "AppContext";
        switches.TryAdd(
            $"{kind}\0{switchName}\0{api}",
            new SwitchInfo(kind, switchName, api));
    }

    static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];
        return value
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal);
    }
}
