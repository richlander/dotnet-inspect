using System.Collections.Immutable;
using CSharpText;
using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed partial class CSharpTypePrinterTests
{
    readonly CSharpTypePrinter _outcomePrinter = new();
    readonly SuccessfulCSharpTypePrinter _printer = new();

    static ApiType CreateEmptyType(string? @namespace, string name)
        => new()
        {
            Namespace = @namespace,
            Name = name,
            Kind = "class"
        };

    static ApiType CreateExactType(
        string? @namespace,
        string[] segments,
        int[] introducedCounts,
        string[] typeParameterNames,
        string kind = "class")
    {
        string leaf = segments[^1];
        return new ApiType
        {
            Namespace = @namespace,
            Name = leaf,
            MetadataName = leaf,
            DefinitionName =
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        @namespace ?? "",
                        [.. segments])).Name,
            IntroducedTypeParameterCounts = [.. introducedCounts],
            TypeParameters =
                [.. typeParameterNames.Select(name => new TypeParameter { Name = name })],
            Kind = kind
        };
    }

    static CSharpTypePrintResult AssertPrinted(CSharpTypePrintOutcome outcome)
        => Assert.IsType<CSharpTypePrintOutcome.Printed>(outcome).Result;

    static CSharpTypePrintOutcome.NotRendered AssertNotRendered(
        CSharpTypePrintOutcome outcome)
        => Assert.IsType<CSharpTypePrintOutcome.NotRendered>(outcome);

    static void AssertArityNotRendered(ApiType type)
    {
        CSharpTypePrintOutcome.NotRendered notRendered = AssertNotRendered(
            new CSharpTypePrinter().Print(new CSharpTypePrintRequest(type)));
        Assert.IsType<CSharpDeclaredTypeSelfNameFailureReason.ArityMismatch>(
            Assert.Single(notRendered.SelfNameFailures).Reason);
    }

    static void AssertIdentifierFailure(
        CSharpDeclaredTypeSelfNameFailure failure,
        string[] expectedSegments,
        CSharpTypeDeclarationIdentifierRefusalReason expectedReason)
    {
        Assert.Equal(expectedSegments, failure.Identity.Segments);
        var reason =
            Assert.IsType<CSharpDeclaredTypeSelfNameFailureReason.IdentifierNotAdmitted>(
                failure.Reason);
        Assert.Equal(expectedReason, reason.Reason);
    }

    static ApiMember CreateMethod(string name)
        => new()
        {
            Name = name,
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "void",
                MemberName = name
            }
        };

    sealed class SuccessfulCSharpTypePrinter
    {
        readonly CSharpTypePrinter _printer = new();

        public CSharpTypePrintResult Print(
            CSharpTypePrintRequest request,
            CSharpTypePrintOptions? options = null)
            => Assert.IsType<CSharpTypePrintOutcome.Printed>(
                _printer.Print(request, options)).Result;

        public CSharpTypePrintResult PrintBatch(
            IEnumerable<CSharpTypePrintRequest> requests,
            CSharpTypePrintOptions? options = null)
            => Assert.IsType<CSharpTypePrintOutcome.Printed>(
                _printer.PrintBatch(requests, options)).Result;
    }

    sealed class DifferentEachEnumerationList<T>(T first, T later) : IReadOnlyList<T>
    {
        int _enumerationCount;

        public int Count => 1;

        public T this[int index] => index == 0 ? first : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<T> GetEnumerator()
        {
            yield return _enumerationCount++ == 0 ? first : later;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
