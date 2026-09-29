namespace ILInspector.Metadata;

/// <summary>
/// Projects one fully extracted Type into the declaration rows of one Member
/// spelling and hidden-admission choice.
/// </summary>
public static class ApiTypeMemberPopulationProjection
{
    public static IReadOnlyList<ApiMember> Project(
        ApiType type,
        MetadataMemberSpelling spelling,
        bool includeHidden)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!Enum.IsDefined(spelling))
            throw new ArgumentOutOfRangeException(nameof(spelling));

        var projected = new List<ApiMember>();
        foreach (ApiMember member in type.Members)
        {
            if (spelling == MetadataMemberSpelling.CSharp)
            {
                Add(member);
                continue;
            }

            if (IsAttachedExtension(type, member))
                continue;

            if (member.Kind is not ("property" or "event"))
            {
                if (member.PhysicalMethodAccess is { } physicalAccess)
                {
                    member.Accessibility =
                        ApiSurfaceExtractor.GetAccessibility(physicalAccess);
                    member.Kind = "method";
                }
                Add(member);
                continue;
            }

            ApiMember[] accessors = [.. ApiMemberAccessors.Create(member, type)];
            member.Accessibility = OwnerAccessibility(member);
            Add(member);
            foreach (ApiMember accessor in accessors)
            {
                accessor.Kind = "method";
                Add(accessor);
            }
        }

        return projected;

        void Add(ApiMember member)
        {
            if (includeHidden || !member.IsHidden)
                projected.Add(member);
        }
    }

    static bool IsAttachedExtension(ApiType type, ApiMember member) =>
        member.IsExtension
        && member.Kind == "extension-method"
        && !string.IsNullOrEmpty(member.DeclaringType)
        && !string.Equals(
            member.DeclaringType,
            type.FullName,
            StringComparison.Ordinal);

    static string? OwnerAccessibility(ApiMember member)
    {
        IEnumerable<string?> accessibilities = member.Kind switch
        {
            "property" =>
                Present(member.GetterToken, member.GetterAccessibility)
                    .Concat(Present(
                        member.SetterToken,
                        member.SetterAccessibility)),
            "event" =>
                Present(member.AdderToken, member.AdderAccessibility)
                    .Concat(Present(
                        member.RemoverToken,
                        member.RemoverAccessibility)),
            _ => [],
        };
        using IEnumerator<string?> enumerator =
            accessibilities.GetEnumerator();
        if (!enumerator.MoveNext())
            return member.Accessibility;

        string? joined = enumerator.Current;
        while (enumerator.MoveNext())
            joined = Join(joined, enumerator.Current);
        return joined;
    }

    static IEnumerable<string?> Present(int? token, string? accessibility)
    {
        if (token is not null)
            yield return accessibility;
    }

    static string? Join(string? left, string? right)
    {
        Access a = Parse(left);
        Access b = Parse(right);
        if (a == b)
            return Format(a);
        if (a == Access.Public || b == Access.Public)
            return null;
        if (a == Access.ProtectedInternal
            || b == Access.ProtectedInternal)
        {
            return "protected internal";
        }
        if (a == Access.Private)
            return Format(b);
        if (b == Access.Private)
            return Format(a);
        if ((a == Access.Protected && b == Access.Internal)
            || (a == Access.Internal && b == Access.Protected))
        {
            return "protected internal";
        }
        if (a == Access.PrivateProtected)
            return Format(b);
        if (b == Access.PrivateProtected)
            return Format(a);
        throw new InvalidOperationException(
            $"Unsupported accessor accessibility join '{left}' with '{right}'.");
    }

    static Access Parse(string? accessibility) => accessibility switch
    {
        null or "" or "public" => Access.Public,
        "protected internal" => Access.ProtectedInternal,
        "protected" => Access.Protected,
        "internal" => Access.Internal,
        "private protected" => Access.PrivateProtected,
        "private" => Access.Private,
        _ => throw new InvalidOperationException(
            $"Unknown accessor accessibility '{accessibility}'."),
    };

    static string? Format(Access accessibility) => accessibility switch
    {
        Access.Public => null,
        Access.ProtectedInternal => "protected internal",
        Access.Protected => "protected",
        Access.Internal => "internal",
        Access.PrivateProtected => "private protected",
        Access.Private => "private",
        _ => throw new ArgumentOutOfRangeException(nameof(accessibility)),
    };

    enum Access
    {
        Private,
        PrivateProtected,
        Protected,
        Internal,
        ProtectedInternal,
        Public,
    }
}
