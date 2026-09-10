using System.Text;
using System.Xml;

namespace CSharpText.Tests;

public sealed class XmlDocumentationReaderTests
{
    const string GenericMethodId =
        "M:Samples.Container.Method``1(``0,System.Int32)";

    [Fact]
    public void ReaderAndCatalog_ReturnTheSameExactGenericMember()
    {
        const string xml = """
            <doc>
              <members>
                <member name="M:Samples.Container.Method">
                  <summary>Neighbor</summary>
                </member>
                <member name="M:Samples.Container.Method``1(``0,System.Int32)">
                  <summary> Uses <typeparamref name="T"/>. </summary>
                  <remarks> More <see cref="T:System.String"/>. </remarks>
                  <param name="value">first</param>
                  <param name="value">last</param>
                  <returns>Result</returns>
                  <exception cref="T:System.ArgumentException">Bad value.</exception>
                  <example>
                    <code source="samples\Example.cs" title="Example" region="Run"/>
                  </example>
                </member>
              </members>
            </doc>
            """;
        var identity = new XmlDocMemberIdentity(GenericMethodId);

        XmlDocumentationEntry? streamed = XmlDocumentationReader.ReadMember(
            Stream(xml),
            identity);
        XmlDocumentationEntry? cataloged =
            XmlDocumentationCatalog.Load(Stream(xml)).Find(identity);

        Assert.NotNull(streamed);
        Assert.NotNull(cataloged);
        Assert.Equal(streamed.Summary, cataloged.Summary);
        Assert.Equal(streamed.Parameters, cataloged.Parameters);
        Assert.Equal(streamed.Exceptions, cataloged.Exceptions);
        Assert.Equal(streamed.Samples, cataloged.Samples);
        Assert.Equal("Uses T.", streamed.Summary);
        Assert.Equal("More String.", streamed.Remarks);
        Assert.Equal("last", Assert.Single(streamed.Parameters).Value);
        Assert.Equal("Result", streamed.Returns);
        Assert.Equal(
            new XmlDocumentationException(
                "T:System.ArgumentException",
                "Bad value."),
            Assert.Single(streamed.Exceptions));
        Assert.Equal(
            new XmlDocumentationSampleReference(
                @"samples\Example.cs",
                "Example",
                "Run"),
            Assert.Single(streamed.Samples));

        IDictionary<string, string> parameters =
            Assert.IsAssignableFrom<IDictionary<string, string>>(
                streamed.Parameters);
        IList<XmlDocumentationException> exceptions =
            Assert.IsAssignableFrom<IList<XmlDocumentationException>>(
                streamed.Exceptions);
        IList<XmlDocumentationSampleReference> samples =
            Assert.IsAssignableFrom<IList<XmlDocumentationSampleReference>>(
                streamed.Samples);
        Assert.Throws<NotSupportedException>(
            () => parameters["value"] = "changed");
        Assert.Throws<NotSupportedException>(
            () => exceptions[0] = new(null, null));
        Assert.Throws<NotSupportedException>(
            () => samples[0] = new("changed", null, null));
    }

    [Fact]
    public void Reader_UsesTheLastDuplicateAndValidatesTrailingXml()
    {
        const string xml = """
            <doc>
              <members>
                <member name="M:Samples.M"><summary>first</summary></member>
                <member name="M:Samples.M"><summary>last</summary></member>
              </members>
            </doc>
            """;
        var identity = new XmlDocMemberIdentity("M:Samples.M");

        XmlDocumentationEntry? streamed =
            XmlDocumentationReader.ReadMember(Stream(xml), identity);
        XmlDocumentationEntry? cataloged =
            XmlDocumentationCatalog.Load(Stream(xml)).Find(identity);

        Assert.Equal("last", streamed?.Summary);
        Assert.Equal(streamed?.Summary, cataloged?.Summary);

        Assert.Throws<XmlException>(() =>
            XmlDocumentationReader.ReadMember(
                Stream(xml + "<broken>"),
                identity));
    }

    [Fact]
    public void Reader_DistinguishesMissingFromPresentEmptyEntry()
    {
        const string xml = """
            <doc>
              <members>
                <member name="M:Samples.Empty"/>
              </members>
            </doc>
            """;

        Assert.NotNull(
            XmlDocumentationReader.ReadMember(
                Stream(xml),
                new XmlDocMemberIdentity("M:Samples.Empty")));
        Assert.Null(
            XmlDocumentationReader.ReadMember(
                Stream(xml),
                new XmlDocMemberIdentity("M:Samples.Missing")));
    }

    [Fact]
    public void Reader_RejectsDtdAndExcessiveDepth()
    {
        const string dtd = """
            <!DOCTYPE doc [<!ENTITY x "expanded">]>
            <doc><members><member name="M:Samples.M"><summary>&x;</summary></member></members></doc>
            """;
        Assert.Throws<XmlException>(() =>
            XmlDocumentationReader.ReadMember(
                Stream(dtd),
                new XmlDocMemberIdentity("M:Samples.M")));

        var builder = new StringBuilder(
            "<doc><members><member name=\"M:Samples.M\"><summary>");
        for (int index = 0; index <= XmlDocText.MaxElementDepth; index++)
            builder.Append("<p>");
        builder.Append('x');
        for (int index = 0; index <= XmlDocText.MaxElementDepth; index++)
            builder.Append("</p>");
        builder.Append("</summary></member></members></doc>");

        Assert.Throws<XmlException>(() =>
            XmlDocumentationReader.ReadMember(
                Stream(builder.ToString()),
                new XmlDocMemberIdentity("M:Samples.M")));

        builder.Clear();
        builder.Append(
            "<doc><members><member name=\"M:Samples.Other\"><unknown>");
        for (int index = 0; index <= XmlDocText.MaxElementDepth; index++)
            builder.Append("<p>");
        for (int index = 0; index <= XmlDocText.MaxElementDepth; index++)
            builder.Append("</p>");
        builder.Append("</unknown></member></members></doc>");

        Assert.Throws<XmlException>(() =>
            XmlDocumentationReader.ReadMember(
                Stream(builder.ToString()),
                new XmlDocMemberIdentity("M:Samples.Missing")));
    }

    [Fact]
    public void Reader_AppliesEveryDeclaredResourceLimit()
    {
        var identity = new XmlDocMemberIdentity("M:Samples.M");
        XmlDocumentationReadLimits defaults =
            XmlDocumentationReadLimits.Default;

        AssertLimit(
            """
            <doc><members>
              <member name="M:Samples.Other"/>
              <member name="M:Samples.M"/>
            </members></doc>
            """,
            identity,
            defaults with { MaxMembers = 1 });
        AssertLimit(
            """
            <doc><members><member name="M:Samples.M"/></members></doc>
            """,
            identity,
            defaults with { MaxMemberIdCharacters = 4 });
        AssertLimit(
            """
            <doc><members><member name="M:Samples.M">
              <summary>text</summary>
            </member></members></doc>
            """,
            identity,
            defaults with { MaxRetainedTextCharacters = 14 });
        AssertLimit(
            """
            <doc><members><member name="M:Samples.M">
              <param name="a">a</param><param name="b">b</param>
            </member></members></doc>
            """,
            identity,
            defaults with { MaxParametersPerMember = 1 });
        AssertLimit(
            """
            <doc><members><member name="M:Samples.M">
              <exception/><exception/>
            </member></members></doc>
            """,
            identity,
            defaults with { MaxExceptionsPerMember = 1 });
        AssertLimit(
            """
            <doc><members><member name="M:Samples.M"><example>
              <code source="a"/><code source="b"/>
            </example></member></members></doc>
            """,
            identity,
            defaults with { MaxSamplesPerMember = 1 });
        AssertLimit(
            """
            <doc><members><member name="M:Samples.M"/></members></doc>
            """,
            identity,
            defaults with { MaxCharactersInDocument = 16 });
    }

    static void AssertLimit(
        string xml,
        XmlDocMemberIdentity identity,
        XmlDocumentationReadLimits limits) =>
        Assert.Throws<XmlException>(
            () => XmlDocumentationReader.ReadMember(
                Stream(xml),
                identity,
                limits));

    static MemoryStream Stream(string xml) =>
        new(Encoding.UTF8.GetBytes(xml), writable: false);
}
