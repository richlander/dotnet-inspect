using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests
{
    public sealed class AssemblyReaderTests
    {
        [Fact]
        public void FindUniquePublicType_matches_simple_public_type_name()
        {
            var match = AssemblyReader.FindUniquePublicType(
                typeof(PublicTypeLookupFixture).Assembly.Location,
                nameof(PublicTypeLookupFixture));

            Assert.Equal(typeof(PublicTypeLookupFixture).FullName, match);
        }

        [Fact]
        public void FindUniquePublicType_matches_nested_public_type_name()
        {
            var match = AssemblyReader.FindUniquePublicType(
                typeof(PublicTypeLookupFixture).Assembly.Location,
                $"{nameof(PublicTypeLookupFixture)}.{nameof(PublicTypeLookupFixture.Nested)}");

            Assert.Equal(typeof(PublicTypeLookupFixture.Nested).FullName!.Replace('+', '.'), match);
        }

        [Fact]
        public void FindUniquePublicType_returns_null_for_non_public_type()
        {
            var match = AssemblyReader.FindUniquePublicType(
                typeof(InternalTypeLookupFixture).Assembly.Location,
                nameof(InternalTypeLookupFixture));

            Assert.Null(match);
        }

        [Fact]
        public void FindUniquePublicType_returns_null_for_ambiguous_simple_name()
        {
            var match = AssemblyReader.FindUniquePublicType(
                typeof(PublicTypeLookupFixture).Assembly.Location,
                "AmbiguousTypeLookupFixture");

            Assert.Null(match);
        }
    }

    public sealed class PublicTypeLookupFixture
    {
        public sealed class Nested;
    }

    internal sealed class InternalTypeLookupFixture;
}

namespace ILInspector.Metadata.Tests.AmbiguousOne
{
    public sealed class AmbiguousTypeLookupFixture;
}

namespace ILInspector.Metadata.Tests.AmbiguousTwo
{
    public sealed class AmbiguousTypeLookupFixture;
}
