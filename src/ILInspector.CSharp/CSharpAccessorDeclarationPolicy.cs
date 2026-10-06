namespace ILInspector.CSharp;

internal enum CSharpContainingDeclarationKind
{
    Class,
    Struct,
    Interface,
}

internal readonly record struct CSharpDeclarationModifierShape(
    bool IsStatic,
    bool IsVirtual,
    bool IsAbstract,
    bool IsOverride,
    bool IsSealed);

internal static class CSharpAccessorDeclarationPolicy
{
    internal static bool PropertyAccessibilityIsRepresentable(
        string? propertyAccessibility,
        IReadOnlyList<string?> accessorAccessibilities)
    {
        string property = propertyAccessibility ?? "public";
        if (!IsCSharpAccessibility(property))
            return false;

        string[] modified =
        [
            .. accessorAccessibilities.Where(
                    static accessibility => accessibility is not null)
                .Select(static accessibility => accessibility!),
        ];
        if (modified.Length == 0)
            return true;
        if (accessorAccessibilities.Count != 2 || modified.Length != 1)
            return false;

        return IsStrictlyMoreRestrictive(modified[0], property);
    }

    internal static bool TrySelectPropertyAccessibility(
        IReadOnlyList<string> accessorAccessibilities,
        out string declarationAccessibility)
    {
        declarationAccessibility = "";
        if (accessorAccessibilities.Count is < 1 or > 2
            || accessorAccessibilities.Any(
                accessibility => !IsCSharpAccessibility(accessibility)))
        {
            return false;
        }

        declarationAccessibility = accessorAccessibilities[0];
        if (accessorAccessibilities.Count == 1
            || accessorAccessibilities[1] == declarationAccessibility)
        {
            return true;
        }

        string second = accessorAccessibilities[1];
        if (IsStrictlyMoreRestrictive(second, declarationAccessibility))
            return true;
        if (IsStrictlyMoreRestrictive(declarationAccessibility, second))
        {
            declarationAccessibility = second;
            return true;
        }

        declarationAccessibility = "";
        return false;
    }

    internal static bool DeclarationModifiersAreRepresentable(
        CSharpContainingDeclarationKind containingKind,
        bool containingIsStatic,
        bool containingIsAbstract,
        bool containingIsSealed,
        string? declarationAccessibility,
        CSharpDeclarationModifierShape modifiers)
    {
        if (declarationAccessibility == "private"
            && (modifiers.IsVirtual
                || modifiers.IsAbstract
                || modifiers.IsOverride))
        {
            return false;
        }

        if (modifiers.IsStatic)
        {
            if (modifiers.IsOverride || modifiers.IsSealed)
                return false;
            if (containingKind == CSharpContainingDeclarationKind.Interface)
            {
                return modifiers.IsVirtual && modifiers.IsAbstract;
            }
            return !modifiers.IsVirtual && !modifiers.IsAbstract;
        }

        if (modifiers.IsAbstract && !modifiers.IsVirtual
            || modifiers.IsOverride && !modifiers.IsVirtual
            || modifiers.IsSealed
                && (!modifiers.IsOverride || modifiers.IsAbstract))
        {
            return false;
        }

        return containingKind switch
        {
            CSharpContainingDeclarationKind.Struct =>
                !modifiers.IsVirtual
                && !modifiers.IsAbstract
                && !modifiers.IsOverride
                && !modifiers.IsSealed,
            CSharpContainingDeclarationKind.Interface =>
                modifiers.IsVirtual
                && modifiers.IsAbstract
                && !modifiers.IsOverride
                && !modifiers.IsSealed,
            _ =>
                !containingIsStatic
                && (!modifiers.IsAbstract || containingIsAbstract)
                && (!containingIsSealed
                    || !modifiers.IsVirtual
                    || modifiers.IsOverride),
        };
    }

    internal static bool IsAtLeastAsAccessibleAs(
        string candidate,
        string required) =>
        IsCSharpAccessibility(candidate)
        && IsCSharpAccessibility(required)
        && (candidate == required
            || IsStrictlyMoreRestrictive(required, candidate));

    internal static bool IsCSharpAccessibility(string accessibility) =>
        accessibility is
            "public"
            or "protected internal"
            or "protected"
            or "internal"
            or "private protected"
            or "private";

    internal static bool IsStrictlyMoreRestrictive(
        string accessor,
        string property) =>
        property switch
        {
            "public" => accessor is
                "protected internal"
                or "protected"
                or "internal"
                or "private protected"
                or "private",
            "protected internal" => accessor is
                "protected"
                or "internal"
                or "private protected"
                or "private",
            "protected" => accessor is "private protected" or "private",
            "internal" => accessor is "private protected" or "private",
            "private protected" => accessor is "private",
            _ => false,
        };
}
