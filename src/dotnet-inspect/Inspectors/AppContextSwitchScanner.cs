using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

internal sealed record AppContextSwitchOccurrence(
    string Switch,
    string Api);

internal static class AppContextSwitchScanner
{
    public static List<AppContextSwitchOccurrence> Scan(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
            return [];

        return Scan(peReader, peReader.GetMetadataReader());
    }

    internal static List<AppContextSwitchOccurrence> Scan(
        PEReader peReader,
        MetadataReader reader)
    {
        List<AppContextSwitchOccurrence> switches = [];

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            var typeName = reader.GetFullTypeName(type);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var instructions = DecodeInstructions(peReader, method);
                if (instructions is null)
                    continue;

                string? lastString = null;
                foreach (var instruction in instructions.Instructions)
                {
                    if (instruction.OpCode == ILOpCode.Ldstr
                        && instruction.Operand == OperandKind.InlineString)
                    {
                        lastString = ResolveUserString(reader, (int)instruction.OperandValue);
                        continue;
                    }

                    if (instruction.OpCode is not (ILOpCode.Call or ILOpCode.Callvirt)
                        || instruction.Operand != OperandKind.InlineMethod
                        || lastString is not { Length: > 0 } switchName
                        || !IsAppContextSwitchMethod(reader, (int)instruction.OperandValue))
                    {
                        continue;
                    }

                    switches.Add(new AppContextSwitchOccurrence(
                        switchName,
                        $"{TypeResolver.FormatDisplayName(typeName)}.{reader.GetString(method.Name)}(...)"));
                }
            }
        }

        return switches;
    }

    static MethodInstructions? DecodeInstructions(
        PEReader peReader,
        MethodDefinition method)
    {
        if (method.RelativeVirtualAddress == 0)
            return null;

        MethodBodyBlock body;
        try
        {
            body = peReader.GetMethodBody(method.RelativeVirtualAddress);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return null;
        }

        var instructions = MethodInstructions.Decode(body);
        if (!instructions.IsComplete)
            throw new BadImageFormatException(instructions.Blocks.IncompleteReason ?? "IL body decode failed.");
        return instructions;
    }

    static string? ResolveUserString(MetadataReader reader, int token)
    {
        try
        {
            return reader.GetUserString(MetadataTokens.UserStringHandle(token));
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException)
        {
            return null;
        }
    }

    static bool IsAppContextSwitchMethod(MetadataReader reader, int token)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind == HandleKind.MethodSpecification)
                handle = reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method;

            string methodName;
            EntityHandle declaringType;
            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                {
                    var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                    methodName = reader.GetString(method.Name);
                    declaringType = method.GetDeclaringType();
                    break;
                }
                case HandleKind.MemberReference:
                {
                    var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                    if (member.GetKind() != MemberReferenceKind.Method)
                        return false;
                    methodName = reader.GetString(member.Name);
                    declaringType = member.Parent;
                    break;
                }
                default:
                    return false;
            }

            return methodName is "TryGetSwitch" or "SetSwitch"
                && IsSystemAppContext(reader, declaringType);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    static bool IsSystemAppContext(MetadataReader reader, EntityHandle handle)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition
                => reader.GetFullTypeName(reader.GetTypeDefinition((TypeDefinitionHandle)handle)) == "System.AppContext",
            HandleKind.TypeReference
                => reader.GetFullTypeName(reader.GetTypeReference((TypeReferenceHandle)handle)) == "System.AppContext",
            _ => false
        };
}
