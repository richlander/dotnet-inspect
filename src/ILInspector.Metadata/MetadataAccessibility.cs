using System.Reflection;

namespace ILInspector.Metadata;

internal static class MetadataAccessibility
{
    public static MethodAttributes Join(
        MethodAttributes left,
        MethodAttributes right)
    {
        if (!TryGet(left, out _))
            return left;
        if (!TryGet(right, out _))
            return right;

        left = Normalize(left);
        right = Normalize(right);
        if (left == right)
            return left;
        if (left == MethodAttributes.Public
            || right == MethodAttributes.Public)
        {
            return MethodAttributes.Public;
        }
        if (left == MethodAttributes.FamORAssem
            || right == MethodAttributes.FamORAssem
            || left is MethodAttributes.Assembly
                && right is MethodAttributes.Family
            || left is MethodAttributes.Family
                && right is MethodAttributes.Assembly)
        {
            return MethodAttributes.FamORAssem;
        }
        if (left == MethodAttributes.Assembly
            || right == MethodAttributes.Assembly)
        {
            return MethodAttributes.Assembly;
        }
        if (left == MethodAttributes.Family
            || right == MethodAttributes.Family)
        {
            return MethodAttributes.Family;
        }
        if (left == MethodAttributes.FamANDAssem
            || right == MethodAttributes.FamANDAssem)
        {
            return MethodAttributes.FamANDAssem;
        }
        return MethodAttributes.Private;
    }

    public static bool Equivalent(
        MethodAttributes left,
        MethodAttributes right) =>
        TryGet(left, out string? leftValue)
        && TryGet(right, out string? rightValue)
        && string.Equals(leftValue, rightValue, StringComparison.Ordinal);

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

    private static MethodAttributes Normalize(MethodAttributes access)
        => access == MethodAttributes.PrivateScope
            ? MethodAttributes.Private
            : access;
}
