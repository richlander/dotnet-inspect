using System.Reflection;

namespace ILInspector.Metadata;

internal static class MetadataAccessibility
{
    public static bool TryGet(MethodAttributes access, out string? value)
    {
        switch (access)
        {
            case MethodAttributes.PrivateScope:
            case MethodAttributes.Private:
                value = "private";
                return true;
            case MethodAttributes.FamANDAssem:
                value = "private protected";
                return true;
            case MethodAttributes.Assembly:
                value = "internal";
                return true;
            case MethodAttributes.Family:
                value = "protected";
                return true;
            case MethodAttributes.FamORAssem:
                value = "protected internal";
                return true;
            case MethodAttributes.Public:
                value = null;
                return true;
            default:
                value = null;
                return false;
        }
    }

    public static bool TryGet(FieldAttributes access, out string? value)
    {
        switch (access)
        {
            case FieldAttributes.PrivateScope:
            case FieldAttributes.Private:
                value = "private";
                return true;
            case FieldAttributes.FamANDAssem:
                value = "private protected";
                return true;
            case FieldAttributes.Assembly:
                value = "internal";
                return true;
            case FieldAttributes.Family:
                value = "protected";
                return true;
            case FieldAttributes.FamORAssem:
                value = "protected internal";
                return true;
            case FieldAttributes.Public:
                value = null;
                return true;
            default:
                value = null;
                return false;
        }
    }

    public static string? Get(MethodAttributes access)
        => TryGet(access, out string? value)
            ? value
            : throw new BadImageFormatException(
                $"Unknown method accessibility value 0x{(int)access:X}.");

    public static string? Get(FieldAttributes access)
        => TryGet(access, out string? value)
            ? value
            : throw new BadImageFormatException(
                $"Unknown field accessibility value 0x{(int)access:X}.");

    public static string Keyword(MethodAttributes access)
        => Get(access) ?? "public";

    public static string Keyword(FieldAttributes access)
        => Get(access) ?? "public";
}
