using System.Buffers;
using System.Reflection;

namespace InertText.Tests;

public sealed class InertUtf8StringTests
{
    [Fact]
    public void OpaqueRanges_AreExactBoundedValueSnapshots()
    {
        OpaqueByteRange population =
            OpaqueByteRange.Copy("alpha-beta"u8);
        OpaqueByteRange alpha = population.Slice(0, 5);
        OpaqueByteRange same =
            OpaqueByteRange.Copy("alpha"u8);
        OpaqueByteRange beta = population.Slice(6, 4);

        Assert.Equal(5, alpha.Length);
        Assert.Equal(same, alpha);
        Assert.Equal(same.GetHashCode(), alpha.GetHashCode());
        Assert.True(alpha.CompareTo(beta) < 0);
        Assert.Equal(default, population.Slice(0, 0));
        Assert.Equal(
            OpaqueByteRange.Copy([]),
            population.Slice(population.Length, 0));
        Assert.Same(Backing(alpha), Backing(beta));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => population.Slice(8, 4));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => population.Slice(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => population.Slice(0, -1));

        static object Backing(OpaqueByteRange range) =>
            typeof(OpaqueByteRange)
                .GetField(
                    "_backing",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(range)!;
    }

    [Fact]
    public void Reader_MatchesPreEncodedTextWithoutContainment()
    {
        OpaqueByteRange source =
            OpaqueByteRange.Copy("Résumé"u8);
        var reader = new Utf8RangeReader(source);

        Assert.True(reader.ValueTextEquals("Résumé"u8));
        Assert.False(reader.ValueTextEquals("Resume"u8));
    }

    [Theory]
    [InlineData("ordinary", "ordinary")]
    [InlineData("a\nb", "a\\^Jb")]
    [InlineData(@"c:\tmp", @"c:\tmp")]
    [InlineData(@"literal\u202E", @"literal\\u202E")]
    [InlineData("a\u202Eb", "a\\u202Eb")]
    public void Containment_MatchesUtf16SpellingAndEvidence(
        string source,
        string expected)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(source);
        var reader =
            new Utf8RangeReader(OpaqueByteRange.Copy(bytes));

        var utf8 =
            new InertUtf8String(TextPolicy.Field, reader);
        var utf16 =
            new InertString(TextPolicy.Field, source);
        var destination = new ArrayBufferWriter<byte>();
        utf8.WriteTo(destination);

        Assert.Equal(expected, utf8.ToString());
        Assert.Equal(
            System.Text.Encoding.UTF8.GetBytes(expected),
            destination.WrittenSpan.ToArray());
        Assert.Equal(utf16.ToString(), utf8.ToInertString().ToString());
        Assert.Equal(utf16.Forms, utf8.Forms);
        Assert.Equal(utf16.Concerns, utf8.Concerns);
    }

    [Fact]
    public void IllFormedBytes_ReceiveDistinctReversibleSpellings()
    {
        InertUtf8String first = Contain([0x80]);
        InertUtf8String second = Contain([0x81]);

        Assert.Equal("\\uDC80", first.ToString());
        Assert.Equal("\\uDC81", second.ToString());
        Assert.NotEqual(first, second);
        Assert.Equal(VisualForm.BmpHex, first.Forms);
        Assert.Equal(TextConcern.Surrogate, first.Concerns);
    }

    [Fact]
    public void PublicRangeSurface_DoesNotLendRetainedBytes()
    {
        Type[] forbidden =
        [
            typeof(byte[]),
            typeof(Span<byte>),
            typeof(ReadOnlySpan<byte>),
            typeof(ReadOnlyMemory<byte>),
            typeof(Memory<byte>),
            typeof(Stream),
            typeof(string),
        ];
        MethodInfo[] disclosures =
        [
            .. typeof(OpaqueByteRange)
                .GetMethods(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.DeclaredOnly)
                .Where(method =>
                    forbidden.Contains(method.ReturnType)),
        ];

        Assert.Empty(disclosures);
        Assert.DoesNotContain(
            typeof(OpaqueByteRange)
                .GetProperties(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.DeclaredOnly),
            property =>
                forbidden.Contains(property.PropertyType));
        Assert.DoesNotContain(
            typeof(OpaqueByteRange)
                .GetMethods(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetParameters()),
            parameter =>
                typeof(Delegate).IsAssignableFrom(
                    parameter.ParameterType));
    }

    [Fact]
    public void ContainingType_CannotBeConstructedFromAnOpaqueRange()
    {
        ConstructorInfo[] constructors =
            typeof(InertUtf8String).GetConstructors();

        Assert.Single(constructors);
        Assert.Equal(
            [typeof(TextPolicy), typeof(Utf8RangeReader)],
            constructors[0]
                .GetParameters()
                .Select(parameter => parameter.ParameterType));
        Assert.Empty(
            typeof(Utf8RangeReader).GetConstructors());
    }

    private static InertUtf8String Contain(byte[] bytes) =>
        new(
            TextPolicy.Field,
            new Utf8RangeReader(OpaqueByteRange.Copy(bytes)));
}
