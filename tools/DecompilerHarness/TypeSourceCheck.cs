using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// The whole-type source oracle (issue #1112). Where <see cref="FidelityCheck"/>
/// closes the loop on a single method body's opcodes, this validates the file
/// and type-level artifacts the product <see cref="TypeSourceComposer"/> emits:
/// namespace, the type kind and modifiers, and the complete member surface.
///
/// <para>
/// The metadata inventory (<see cref="ApiSurfaceExtractor"/>) is ground truth;
/// the composed whole-type listing is the product output under test. Roslyn
/// parses the listing with error recovery and the comparison is purely
/// syntactic — it never binds or recompiles. That is deliberate: a method body
/// the decompiler emits can fail to parse or bind for reasons that have nothing
/// to do with the type/file artifacts, and those method-codegen defects must not
/// mask (nor be reported as) a dropped namespace, modifier, or member. The
/// product path stays SRM-only and Roslyn-free; Roslyn lives only here in the
/// developer/CI oracle.
/// </para>
/// </summary>
static class TypeSourceCheck
{
    /// <summary>One type-level artifact discrepancy between metadata and the composed source.</summary>
    public sealed record TypeArtifactDelta(string FullType, string Kind, string Detail);

    /// <summary>Delta kinds, kept stable so reports and tests can bucket on them.</summary>
    public static class Deltas
    {
        public const string TypeDeclMissing = "type-decl-missing";
        public const string Namespace = "namespace";
        public const string TypeKind = "type-kind";
        public const string ModifierDropped = "modifier-dropped";
        public const string ModifierExtra = "modifier-extra";
        public const string GenericConstraintDropped = "generic-constraint-dropped";
        public const string GenericConstraintExtra = "generic-constraint-extra";
        public const string MemberMissing = "member-missing";
    }

    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples)
    {
        int typesChecked = 0, assembliesScanned = 0, withDeltas = 0;
        var byKind = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var examples = new List<string>();

        foreach (var path in assemblies)
        {
            if (typesChecked >= cap)
                break;
            IReadOnlyList<TypeArtifactDelta> deltas;
            int before = typesChecked;
            try
            {
                deltas = Evaluate(path, cap, ref typesChecked);
            }
            catch
            {
                continue;
            }
            if (typesChecked > before)
                assembliesScanned++;

            foreach (var group in deltas.GroupBy(d => d.FullType))
                withDeltas++;

            foreach (var delta in deltas)
            {
                byKind[delta.Kind] = byKind.GetValueOrDefault(delta.Kind) + 1;
                if (examples.Count < maxExamples)
                    examples.Add($"{delta.FullType} — {delta.Kind} — {delta.Detail}");
            }
        }

        Console.WriteLine($"TYPE-SOURCE over {typesChecked} composed types ({assembliesScanned} assemblies)");
        Console.WriteLine();
        Console.WriteLine($"  types with deltas : {withDeltas} ({Pct(withDeltas, typesChecked)})");
        Console.WriteLine();
        if (byKind.Count == 0)
        {
            Console.WriteLine("  no type-level artifact deltas.");
            return 0;
        }
        Console.WriteLine("  deltas by kind:");
        foreach (var (kind, n) in byKind.OrderByDescending(p => p.Value))
            Console.WriteLine($"    {kind}: {n}");
        if (examples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  examples:");
            foreach (var e in examples)
                Console.WriteLine($"    {e}");
        }
        return 0;
    }

    static string Pct(int n, int total) => total == 0 ? "0%" : $"{100.0 * n / total:0.0}%";

    /// <summary>
    /// Composes every public top-level type in <paramref name="assemblyPath"/>
    /// and compares its artifacts against metadata. Testable entry point.
    /// </summary>
    public static IReadOnlyList<TypeArtifactDelta> Evaluate(string assemblyPath, int cap = int.MaxValue)
    {
        int checkedCount = 0;
        return Evaluate(assemblyPath, cap, ref checkedCount);
    }

    static IReadOnlyList<TypeArtifactDelta> Evaluate(string assemblyPath, int cap, ref int checkedCount)
    {
        var deltas = new List<TypeArtifactDelta>();
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
            return deltas;

        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: false);
        foreach (var type in surface.Types)
        {
            if (checkedCount >= cap)
                break;
            // Delegates compose to no whole-type listing (a one-line declaration
            // is rendered elsewhere), so the oracle has nothing to inventory.
            if (type.Kind == "delegate")
                continue;

            var source = TypeSourceComposer.Compose(type, assemblyPath, pdbPath: null);
            if (source is null)
                continue;

            checkedCount++;
            deltas.AddRange(CompareType(type, source));
        }
        return deltas;
    }

    /// <summary>
    /// The pure oracle: compare the metadata truth in <paramref name="type"/>
    /// against the artifacts declared in <paramref name="composedSource"/>.
    /// Parses with Roslyn error recovery and never binds, so a method body that
    /// fails to parse or bind produces no delta — only namespace, type-kind,
    /// modifier, and member-surface discrepancies do.
    /// </summary>
    public static IReadOnlyList<TypeArtifactDelta> CompareType(ApiType type, string composedSource)
    {
        var deltas = new List<TypeArtifactDelta>();
        string fullType = type.FullName;
        string simpleName = SimpleName(type.Name);

        // Stub member bodies before parsing. A method body the decompiler emits
        // can contain constructs Roslyn cannot parse (synthesized names like
        // <Clone>$d__2); left in place, that defect derails error recovery and
        // can drop a *sibling* declaration, masking the real artifact under test
        // as a phantom missing member. Stubbing keeps the file/type/member
        // skeleton intact while making the parse immune to body codegen.
        var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(StubMemberBodies(composedSource)).GetRoot();

        var namespaceDecl = root.Members.OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        var typeHost = namespaceDecl is not null ? namespaceDecl.Members : root.Members;
        string? declaredNamespace = namespaceDecl?.Name.ToString();

        var typeDecl = typeHost.FirstOrDefault(m => IdentifierOf(m) == simpleName);
        if (typeDecl is null)
        {
            // No matching declaration: Compose returned an error comment or
            // malformed output. The body comparison cannot proceed.
            deltas.Add(new TypeArtifactDelta(fullType, Deltas.TypeDeclMissing,
                $"no '{simpleName}' declaration in composed source"));
            return deltas;
        }

        // Namespace.
        string expectedNamespace = type.Namespace ?? "";
        if ((declaredNamespace ?? "") != expectedNamespace)
            deltas.Add(new TypeArtifactDelta(fullType, Deltas.Namespace,
                $"expected '{expectedNamespace}', declared '{declaredNamespace ?? ""}'"));

        // Type kind keyword.
        string expectedKeyword = type.Kind == "enum" ? "enum" : type.Kind;
        string? declaredKeyword = KeywordOf(typeDecl);
        if (declaredKeyword != expectedKeyword)
            deltas.Add(new TypeArtifactDelta(fullType, Deltas.TypeKind,
                $"expected '{expectedKeyword}', declared '{declaredKeyword ?? "?"}'"));

        // Modifiers, scoped to the ones meaningful for the kind so unrelated
        // keywords never read as noise.
        var declaredModifiers = (typeDecl as TypeDeclarationSyntax)?.Modifiers
            .Select(m => m.Text).ToHashSet() ?? [];
        foreach (var (modifier, expected) in ExpectedModifiers(type))
        {
            bool declared = declaredModifiers.Contains(modifier);
            if (expected && !declared)
                deltas.Add(new TypeArtifactDelta(fullType, Deltas.ModifierDropped, modifier));
            else if (!expected && declared)
                deltas.Add(new TypeArtifactDelta(fullType, Deltas.ModifierExtra, modifier));
        }

        CompareGenericConstraints(type, typeDecl, fullType, deltas);

        // Member surface: every metadata member must have a matching declaration.
        // Extra syntax declarations (e.g. non-public context fields the composer
        // adds) are not flagged — this is a missing-member oracle.
        CompareMembers(type, typeDecl, fullType, deltas);

        return deltas;
    }

    static void CompareGenericConstraints(ApiType type, MemberDeclarationSyntax typeDecl, string fullType,
        List<TypeArtifactDelta> deltas)
    {
        if (typeDecl is not TypeDeclarationSyntax td)
            return;

        var declared = td.ConstraintClauses.ToDictionary(
            clause => clause.Name.Identifier.Text,
            clause => clause.Constraints.Select(c => c.ToString()).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (var typeParameter in type.TypeParameters)
        {
            var expected = typeParameter.Constraints.ToHashSet(StringComparer.Ordinal);
            declared.TryGetValue(typeParameter.Name, out var actual);
            actual ??= [];

            foreach (var constraint in expected)
            {
                if (!actual.Contains(constraint))
                    deltas.Add(new TypeArtifactDelta(fullType, Deltas.GenericConstraintDropped,
                        $"{typeParameter.Name} : {constraint}"));
            }

            foreach (var constraint in actual)
            {
                if (!expected.Contains(constraint))
                    deltas.Add(new TypeArtifactDelta(fullType, Deltas.GenericConstraintExtra,
                        $"{typeParameter.Name} : {constraint}"));
            }
        }

        var knownParameters = type.TypeParameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var (name, constraints) in declared)
        {
            if (knownParameters.Contains(name))
                continue;
            foreach (var constraint in constraints)
                deltas.Add(new TypeArtifactDelta(fullType, Deltas.GenericConstraintExtra,
                    $"{name} : {constraint}"));
        }
    }

    static void CompareMembers(ApiType type, MemberDeclarationSyntax typeDecl, string fullType,
        List<TypeArtifactDelta> deltas)
    {
        var declaredNames = new HashSet<string>(StringComparer.Ordinal);
        int declaredCtors = 0, declaredOperators = 0;

        void IndexMember(MemberDeclarationSyntax m)
        {
            switch (m)
            {
                case MethodDeclarationSyntax method:
                    declaredNames.Add(method.Identifier.Text);
                    // The composer renders operators with their raw metadata
                    // name (op_Equality), so they parse as methods, not operator
                    // syntax — count them as operators either way.
                    if (method.Identifier.Text.StartsWith("op_", StringComparison.Ordinal))
                        declaredOperators++;
                    break;
                case PropertyDeclarationSyntax prop:
                    declaredNames.Add(prop.Identifier.Text);
                    break;
                case IndexerDeclarationSyntax:
                    declaredNames.Add("this[]");
                    break;
                case EventDeclarationSyntax evt:
                    declaredNames.Add(evt.Identifier.Text);
                    break;
                case EventFieldDeclarationSyntax evtField:
                    foreach (var v in evtField.Declaration.Variables)
                        declaredNames.Add(v.Identifier.Text);
                    break;
                case FieldDeclarationSyntax field:
                    foreach (var v in field.Declaration.Variables)
                        declaredNames.Add(v.Identifier.Text);
                    break;
                case ConstructorDeclarationSyntax:
                    declaredCtors++;
                    break;
                case OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax:
                    declaredOperators++;
                    break;
                case EnumMemberDeclarationSyntax enumMember:
                    declaredNames.Add(enumMember.Identifier.Text);
                    break;
                case BaseTypeDeclarationSyntax nested:
                    declaredNames.Add(nested.Identifier.Text);
                    break;
                case DelegateDeclarationSyntax nestedDelegate:
                    declaredNames.Add(nestedDelegate.Identifier.Text);
                    break;
            }
        }

        if (typeDecl is TypeDeclarationSyntax td)
            foreach (var m in td.Members)
                IndexMember(m);
        else if (typeDecl is EnumDeclarationSyntax ed)
            foreach (var m in ed.Members)
                IndexMember(m);

        int expectedCtors = 0, expectedOperators = 0;
        foreach (var member in type.Members)
        {
            switch (member.Kind)
            {
                case "constructor":
                    expectedCtors++;
                    break;
                case "operator":
                    expectedOperators++;
                    break;
                // Extension methods are a metadata projection of a real static
                // method already counted under "method"; they are not a distinct
                // declaration the composer emits.
                case "extension-method":
                    break;
                case "explicit-interface-implementation":
                    // The syntax identifier drops the interface prefix; an
                    // explicit property/event implementation is rendered by name
                    // (Iface.get_E -> property E), so the accessor prefix is
                    // stripped too. Indexer accessors keep their method form, so
                    // the unstripped segment is accepted as well.
                    string segment = LastSegment(member.Name);
                    if (!declaredNames.Contains(segment)
                        && !declaredNames.Contains(StripAccessorPrefix(segment)))
                        deltas.Add(new TypeArtifactDelta(fullType, Deltas.MemberMissing,
                            $"{member.Kind} {member.Name}"));
                    break;
                default:
                    // Enum constants carry an EnumValue; the synthetic value__
                    // backing field does not and is not part of the source
                    // surface, so it is excluded for enum types.
                    bool isEnumConstant = member.EnumValue is not null;
                    bool isNamedMember = type.Kind != "enum"
                        && member.Kind is "field" or "property" or "event" or "method";
                    if (isEnumConstant || isNamedMember)
                    {
                        // An indexer is named Item/Chars/etc. in metadata but
                        // rendered as this[...]; match it on the indexer marker.
                        bool isIndexer = member.Kind == "property"
                            && member.Signature?.Contains("this[", StringComparison.Ordinal) == true;
                        string expectedName = isIndexer ? "this[]" : member.Name;
                        if (!declaredNames.Contains(expectedName))
                            deltas.Add(new TypeArtifactDelta(fullType, Deltas.MemberMissing,
                                $"{member.Kind} {member.Name}"));
                    }
                    break;
            }
        }

        if (expectedCtors > declaredCtors)
            deltas.Add(new TypeArtifactDelta(fullType, Deltas.MemberMissing,
                $"constructor (expected {expectedCtors}, declared {declaredCtors})"));
        if (expectedOperators > declaredOperators)
            deltas.Add(new TypeArtifactDelta(fullType, Deltas.MemberMissing,
                $"operator (expected {expectedOperators}, declared {declaredOperators})"));
    }

    /// <summary>The modifiers the metadata implies, scoped to the type kind.</summary>
    static IEnumerable<(string Modifier, bool Expected)> ExpectedModifiers(ApiType type)
    {
        if (type.Kind == "class")
        {
            yield return ("static", type.IsStatic);
            yield return ("abstract", type.IsAbstract && !type.IsStatic);
            yield return ("sealed", type.IsSealed && !type.IsStatic && !type.IsAbstract);
        }
        else if (type.Kind == "struct")
        {
            yield return ("ref", type.IsByRefLike);
            yield return ("readonly", type.IsReadOnly);
        }
    }

    static string? IdentifierOf(MemberDeclarationSyntax m) => m switch
    {
        TypeDeclarationSyntax t => t.Identifier.Text,
        EnumDeclarationSyntax e => e.Identifier.Text,
        DelegateDeclarationSyntax d => d.Identifier.Text,
        _ => null,
    };

    static string? KeywordOf(MemberDeclarationSyntax m) => m switch
    {
        // RecordDeclarationSyntax derives from TypeDeclarationSyntax; its Keyword
        // is "record", so it is handled by the TypeDeclarationSyntax arm only if
        // not matched here first. The composer never emits records.
        EnumDeclarationSyntax => "enum",
        TypeDeclarationSyntax t => t.Keyword.Text,
        _ => null,
    };

    /// <summary>
    /// Replaces every member body — block bodies <c>{ … }</c> and expression
    /// bodies <c>=> … ;</c> nested inside the type — with an empty stub, using
    /// Roslyn's resilient lexer so strings, comments, and interpolation are
    /// tracked correctly. The type/file skeleton (namespace, usings, type
    /// headers, member signatures, nested-type headers) is preserved verbatim,
    /// so the artifact inventory is unaffected while any body-level parse defect
    /// is erased. The composer always emits a file-scoped namespace, so the
    /// type body opens at brace depth 1.
    /// </summary>
    static string StubMemberBodies(string source)
    {
        var sb = new System.Text.StringBuilder(source.Length);
        int depth = 0;            // brace depth before the current token
        int blockSkipDepth = -1;  // skipping a brace body; resume when depth returns here
        int arrowDepth = -1;      // skipping an expression body; resume at ';' here

        foreach (var token in SyntaxFactory.ParseTokens(source))
        {
            var kind = token.Kind();
            if (kind == SyntaxKind.EndOfFileToken)
                break;

            if (arrowDepth >= 0)
            {
                if (kind == SyntaxKind.OpenBraceToken) depth++;
                else if (kind == SyntaxKind.CloseBraceToken) depth--;
                else if (kind == SyntaxKind.SemicolonToken && depth == arrowDepth)
                {
                    arrowDepth = -1;
                    sb.Append(";\n");
                }
                continue;
            }
            if (blockSkipDepth >= 0)
            {
                if (kind == SyntaxKind.OpenBraceToken) depth++;
                else if (kind == SyntaxKind.CloseBraceToken)
                {
                    depth--;
                    if (depth == blockSkipDepth)
                    {
                        blockSkipDepth = -1;
                        sb.Append(" { }\n");
                    }
                }
                continue;
            }

            switch (kind)
            {
                case SyntaxKind.OpenBraceToken when depth >= 1:
                    // A member body, accessor list, nested-type body, or
                    // initializer: drop its contents, resume at this depth.
                    blockSkipDepth = depth;
                    depth++;
                    break;
                case SyntaxKind.OpenBraceToken:
                    depth++;
                    sb.Append(token.ToFullString());
                    break;
                case SyntaxKind.CloseBraceToken:
                    depth--;
                    sb.Append(token.ToFullString());
                    break;
                case SyntaxKind.EqualsGreaterThanToken when depth >= 1:
                    arrowDepth = depth;
                    break;
                default:
                    sb.Append(token.ToFullString());
                    break;
            }
        }
        return sb.ToString();
    }

    static string SimpleName(string name)
    {
        int tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }

    static string LastSegment(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    static string StripAccessorPrefix(string name)
    {
        foreach (var prefix in new[] { "get_", "set_", "add_", "remove_" })
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return name[prefix.Length..];
        return name;
    }
}
