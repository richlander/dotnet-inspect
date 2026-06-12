using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Metadata;

public record SwitchInfo(
    string Kind,
    string Switch,
    string Api);

public static class SwitchScanner
{
    private const string FeatureSwitchDefinitionAttributeName =
        "System.Diagnostics.CodeAnalysis.FeatureSwitchDefinitionAttribute";
    private const string RuntimeHostConfigurationOptionAttributeName =
        "System.Runtime.CompilerServices.RuntimeHostConfigurationOptionAttribute";

    public static List<SwitchInfo> Scan(PEReader peReader)
    {
        if (!peReader.HasMetadata)
            return [];

        var reader = peReader.GetMetadataReader();
        Dictionary<string, SwitchInfo> switches = new(StringComparer.Ordinal);
        AddRuntimeHostConfigurationOptions(reader, switches);
        AddFeatureSwitchDefinitions(reader, switches);
        AddAppContextSwitches(peReader, reader, switches);

        return switches.Values
            .OrderBy(s => s.Kind, StringComparer.Ordinal)
            .ThenBy(s => s.Switch, StringComparer.Ordinal)
            .ThenBy(s => s.Api, StringComparer.Ordinal)
            .ToList();
    }

    public static bool HasSupport(PEReader peReader)
        => peReader.HasMetadata && Scan(peReader).Count > 0;

    private static void AddRuntimeHostConfigurationOptions(
        MetadataReader reader,
        Dictionary<string, SwitchInfo> switches)
    {
        if (reader.IsAssembly)
            AddAttributes(reader, "Assembly", reader.GetAssemblyDefinition().GetCustomAttributes(), switches);
        AddAttributes(reader, "Module", reader.GetModuleDefinition().GetCustomAttributes(), switches);
    }

    private static void AddFeatureSwitchDefinitions(
        MetadataReader reader,
        Dictionary<string, SwitchInfo> switches)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            var typeName = reader.GetFullTypeName(type);
            foreach (var propertyHandle in type.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var propertyName = reader.GetString(property.Name);
                foreach (var attributeHandle in property.GetCustomAttributes())
                {
                    var attribute = reader.GetCustomAttribute(attributeHandle);
                    if (AttributeReader.GetAttributeTypeName(reader, attribute.Constructor) != FeatureSwitchDefinitionAttributeName)
                        continue;

                    var switchName = TryGetStringArguments(reader, attribute).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(switchName))
                        AddSwitch(switches, "Feature Switch", switchName, $"{TypeResolver.FormatDisplayName(typeName)}.{propertyName}");
                }
            }
        }
    }

    private static void AddAttributes(
        MetadataReader reader,
        string target,
        CustomAttributeHandleCollection attributes,
        Dictionary<string, SwitchInfo> switches)
    {
        foreach (var attributeHandle in attributes)
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (AttributeReader.GetAttributeTypeName(reader, attribute.Constructor) != RuntimeHostConfigurationOptionAttributeName)
                continue;

            var args = TryGetStringArguments(reader, attribute);
            if (args.Count > 0)
                AddSwitch(switches, "Runtime Host Configuration", args[0], target);
        }
    }

    private static void AddAppContextSwitches(
        PEReader peReader,
        MetadataReader reader,
        Dictionary<string, SwitchInfo> switches)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            var typeName = reader.GetFullTypeName(type);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var instructions = ILDisassembler.Disassemble(peReader, reader, method);
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

                    if (call.Contains("::TryGetSwitch(", StringComparison.Ordinal))
                        AddAppContextSwitch(switches, switchName, $"{TypeResolver.FormatDisplayName(typeName)}.{reader.GetString(method.Name)}(...)");
                    else if (call.Contains("::SetSwitch(", StringComparison.Ordinal))
                        AddAppContextSwitch(switches, switchName, $"{TypeResolver.FormatDisplayName(typeName)}.{reader.GetString(method.Name)}(...)");
                }
            }
        }
    }

    private static void AddAppContextSwitch(
        Dictionary<string, SwitchInfo> switches,
        string switchName,
        string api)
    {
        if (IsIgnoredAppContextSwitch(switchName))
            return;

        AddSwitch(switches, "AppContext", switchName, api);
    }

    private static bool IsIgnoredAppContextSwitch(string switchName)
        => switchName.StartsWith("System.Resources.UseSystemResourceKeys", StringComparison.Ordinal)
           || switchName.StartsWith("TestSwitch.", StringComparison.Ordinal)
           || switchName.StartsWith("Switch.", StringComparison.Ordinal);

    private static List<string> TryGetStringArguments(MetadataReader reader, CustomAttribute attribute)
    {
        try
        {
            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.Length < 2)
                return [];
            blob.ReadUInt16();

            List<string> values = [];
            while (blob.RemainingBytes > 0)
            {
                var value = blob.ReadSerializedString();
                if (value is null)
                    break;
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value);
            }

            return values;
        }
        catch
        {
            return [];
        }
    }

    private static string Unquote(string value)
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

    private static void AddSwitch(
        Dictionary<string, SwitchInfo> switches,
        string kind,
        string switchName,
        string api)
    {
        switches.TryAdd($"{kind}\0{switchName}\0{api}", new SwitchInfo(kind, switchName, api));
    }
}
