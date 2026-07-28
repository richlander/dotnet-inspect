using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.CSharp.Tests;

/// <summary>
/// Gates the containment properties the identifier spellings assert (issue #3319).
/// Three related but distinct properties are in play and these tests keep them
/// apart:
/// <list type="bullet">
/// <item><see cref="CSharpIdentifier.ContainIdentifier"/> and
/// <see cref="CSharpIdentifier.ContainIdentifierForDeclaration"/> guarantee only
/// that a name carries no line terminator — the property that closes #3319 —
/// while preserving every other spelling.</item>
/// <item><see cref="CSharpIdentifier.Sanitize"/> additionally guarantees a legal
/// C# identifier, and keeps its existing callers' behavior.</item>
/// <item><see cref="CSharpIdentifierCore.ContainComposedName"/> folds line
/// terminators in names that are not identifiers at all (<c>.ctor</c>, an
/// explicit interface implementation).</item>
/// </list>
/// </summary>
public sealed class CSharpIdentifierSanitizationTests
{
    /// <summary>
    /// Every line terminator <c>ReplaceLineEndings</c> recognizes, which is the set
    /// that can break a name out of a code fence, a Markdown table row, or the
    /// <c>type</c> tree gutter.
    /// </summary>
    public static TheoryData<string> LineTerminators => new()
    {
        "\n", "\r", "\r\n", "\f", "\u0085", "\u2028", "\u2029",
    };

    [Theory]
    [MemberData(nameof(LineTerminators))]
    public void ContainIdentifier_FoldsEveryLineTerminator(string terminator)
    {
        string hostile = $"p{terminator}    public int Injected() => 42; //";

        AssertNoLineTerminator(CSharpIdentifier.ContainIdentifier(hostile));
        AssertNoLineTerminator(CSharpIdentifier.ContainIdentifierForDeclaration(hostile));
    }

    /// <summary>
    /// Containment must leave a spelling C# can still parse as an identifier;
    /// otherwise the emitted source trades an injection for a syntax error.
    /// </summary>
    [Theory]
    [MemberData(nameof(LineTerminators))]
    public void ContainIdentifier_ProducesALegalIdentifier(string terminator)
    {
        string hostile = $"p{terminator}    public int Injected() => 42; //";

        Assert.True(CSharpIdentifier.IsEscapable(CSharpIdentifier.ContainIdentifier(hostile)));
        Assert.True(CSharpIdentifier.IsEscapable(CSharpIdentifier.ContainIdentifierForDeclaration(hostile)));
    }

    [Theory]
    [MemberData(nameof(LineTerminators))]
    public void ContainComposedName_FoldsEveryLineTerminator(string terminator)
        => AssertNoLineTerminator(
            CSharpIdentifierCore.ContainComposedName($"Meth{terminator}    public int Injected() => 42; //"));

    /// <summary>
    /// A member name is not always a simple identifier. Sanitizing these into one
    /// would rewrite <c>.ctor</c> to <c>__ctor</c> and an explicit interface
    /// implementation to <c>System_IConvertible_ToBoolean</c> — a regression the
    /// CLI suite caught while this change was being written.
    /// </summary>
    [Theory]
    [InlineData(".ctor")]
    [InlineData(".cctor")]
    [InlineData("System.IConvertible.ToBoolean")]
    [InlineData("System.Collections.Generic.IEnumerable<System.Char>.GetEnumerator")]
    public void ContainComposedName_PreservesStructuralPunctuation(string name)
        => Assert.Equal(name, CSharpIdentifierCore.ContainComposedName(name));

    /// <summary>
    /// The decompiler's contract for an unspellable-but-harmless name is to keep
    /// identity visible and report the problem through the fidelity marker, not to
    /// rewrite the name — see
    /// <c>KeywordIdentifierTests.RaisedNullConditionalUnspellableProperty_PreservesIdentity</c>.
    /// Containment must therefore stop at line terminators, and this test is what
    /// stops it being widened into <see cref="CSharpIdentifier.Sanitize"/> later.
    /// </summary>
    [Theory]
    [InlineData("bad-name")]
    [InlineData("<>c__DisplayClass0_0")]
    [InlineData("<Prop>k__BackingField")]
    [InlineData("<M>g__Local|0_0")]
    public void ContainIdentifier_PreservesUnspellableNamesThatCannotBreakOutput(string name)
    {
        Assert.Equal(name, CSharpIdentifier.ContainIdentifier(name));
        Assert.Equal(name, CSharpIdentifier.ContainIdentifierForDeclaration(name));

        // Non-vacuity: the wider sanitizer really would have rewritten these.
        Assert.NotEqual(name, CSharpIdentifier.Sanitize(name));
    }

    /// <summary>
    /// The change this file gates swaps ~40 call sites from keyword escaping to
    /// containment. This drives the "identical for every name a compiler can emit"
    /// claim from a real assembly's metadata rather than a hand-written list, so
    /// byte-neutrality is enforced rather than asserted.
    /// </summary>
    [Fact]
    public void ContainIdentifier_IsByteNeutral_AcrossRealAssemblyMetadata()
    {
        var names = ReadMetadataNames(typeof(object).Assembly.Location);

        // Non-vacuity: a real framework assembly carries tens of thousands of names.
        Assert.True(names.Count > 10_000, $"only {names.Count} names read");

        foreach (var name in names)
        {
            Assert.Equal(CSharpIdentifier.Escape(name), CSharpIdentifier.ContainIdentifier(name));
            Assert.Equal(name, CSharpIdentifierCore.ContainComposedName(name));
        }
    }

    /// <summary>
    /// The two containing spellings must keep their distinct keyword sets: a
    /// declaration-only contextual keyword is escaped in declaration position and
    /// left bare in body position. Pointing a declaration site at the body spelling
    /// would silently narrow its escaping, which is the trap this split prevents.
    /// </summary>
    [Theory]
    [InlineData("record")]
    [InlineData("required")]
    [InlineData("init")]
    [InlineData("file")]
    [InlineData("scoped")]
    public void ContainIdentifierForDeclaration_EscapesDeclarationContextualKeywords(string keyword)
    {
        Assert.Equal("@" + keyword, CSharpIdentifier.ContainIdentifierForDeclaration(keyword));
        Assert.Equal(keyword, CSharpIdentifier.ContainIdentifier(keyword));
    }

    [Theory]
    [InlineData("delegate")]
    [InlineData("int")]
    [InlineData("await")]
    public void ContainIdentifier_EscapesBodyKeywords(string keyword)
        => Assert.Equal("@" + keyword, CSharpIdentifier.ContainIdentifier(keyword));

    /// <summary>
    /// <c>CSharpPrinter</c>'s shadow-name normalization feeds a set that mixes raw
    /// and already-escaped spellings through this, then matches rendered names
    /// against it. Without idempotence <c>@foo</c> would be rewritten and stop
    /// matching, silently breaking shadow qualification.
    /// </summary>
    [Theory]
    [InlineData("@foo")]
    [InlineData("@record")]
    [InlineData("@int")]
    public void ContainIdentifier_IsIdempotentOnEscapedSpellings(string escaped)
    {
        Assert.Equal(escaped, CSharpIdentifier.ContainIdentifier(escaped));
        Assert.Equal(escaped, CSharpIdentifier.ContainIdentifierForDeclaration(escaped));
    }

    /// <summary>
    /// An <c>@</c> is not a licence to skip containment: a hostile name behind one
    /// must still be folded.
    /// </summary>
    [Theory]
    [MemberData(nameof(LineTerminators))]
    public void ContainIdentifier_DoesNotPassThroughHostileNamesBehindAnAtSign(string terminator)
        => AssertNoLineTerminator(CSharpIdentifier.ContainIdentifier($"@p{terminator}injected"));

    static void AssertNoLineTerminator(string spelled)
    {
        Assert.Equal(spelled, spelled.ReplaceLineEndings(" "));
        foreach (char c in spelled)
            Assert.False(c is '\r' or '\n' or '\f' or '\u0085' or '\u2028' or '\u2029');
    }

    static List<string> ReadMetadataNames(string assemblyPath)
    {
        var names = new List<string>();
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            names.Add(reader.GetString(type.Name));
            foreach (var fieldHandle in type.GetFields())
                names.Add(reader.GetString(reader.GetFieldDefinition(fieldHandle).Name));
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                names.Add(reader.GetString(method.Name));
                foreach (var parameterHandle in method.GetParameters())
                    names.Add(reader.GetString(reader.GetParameter(parameterHandle).Name));
            }
            foreach (var propertyHandle in type.GetProperties())
                names.Add(reader.GetString(reader.GetPropertyDefinition(propertyHandle).Name));
        }
        return names;
    }
}
