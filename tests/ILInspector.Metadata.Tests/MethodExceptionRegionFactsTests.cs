using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public class MethodExceptionRegionFactsTests
{
    static string SelfPath => typeof(MethodExceptionRegionFactsTests).Assembly.Location;

    [Fact]
    public void Read_DistinguishesKnownEmptyNoBodyAndUnavailable()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        MethodBodySource source = session.MethodBodies;

        var empty = Assert.IsType<MethodBodyReadResult.Available>(
            source.Read(TokenOf(nameof(MethodExceptionRegionFactsSamples.NoRegions))));
        Assert.Empty(empty.Body.ExceptionRegionCatalog.Clauses);
        Assert.False(empty.Body.ExceptionRegionCatalog.HasExceptionRegions);

        int bodylessToken = typeof(MethodExceptionRegionFactsSamples.Shape)
            .GetMethod(nameof(MethodExceptionRegionFactsSamples.Shape.NoBody))!
            .MetadataToken;
        var noBody = Assert.IsType<MethodBodyReadResult.NoBody>(
            source.Read(bodylessToken));
        Assert.Equal(bodylessToken, noBody.Method.Token);

        var wrongKind = Assert.IsType<MethodBodyReadResult.Unavailable>(
            source.Read(typeof(MethodExceptionRegionFactsSamples).MetadataToken));
        Assert.IsType<MethodBodyUnavailableReason.NotMethodDefinitionToken>(
            wrongKind.Reason);
        Assert.Null(wrongKind.Method);

        int missingToken = MetadataTokens.GetToken(
            MetadataTokens.MethodDefinitionHandle(source.MethodDefinitionCount + 1));
        var missing = Assert.IsType<MethodBodyReadResult.Unavailable>(
            source.Read(missingToken));
        Assert.IsType<MethodBodyUnavailableReason.RowOutOfRange>(missing.Reason);
        Assert.Null(missing.Method);
    }

    [Fact]
    public void Read_IssuesDetachedBodyAndClauseIdentity()
    {
        MethodBodyData body;
        MethodBodyEvidenceId secondEvidence;
        int token = TokenOf(nameof(MethodExceptionRegionFactsSamples.Catch));

        using (var session = AssemblyInspectionSession.Open(SelfPath))
        {
            body = Assert.IsType<MethodBodyReadResult.Available>(
                session.MethodBodies.Read(token)).Body;
            secondEvidence = Assert.IsType<MethodBodyReadResult.Available>(
                session.MethodBodies.Read(token)).Body.EvidenceId;

            Assert.Equal(session.ModuleVersionId(), body.EvidenceId.Method.ModuleVersionId);
        }

        Assert.Equal(token, body.EvidenceId.Method.Token);
        Assert.NotEqual(Guid.Empty, body.EvidenceId.ObservationId);
        Assert.NotEqual(body.EvidenceId, secondEvidence);
        Assert.NotEmpty(body.IL);
        Assert.All(
            body.ExceptionRegionCatalog.Clauses,
            clause => Assert.Equal(body.EvidenceId, clause.Id.Body));
        Assert.Equal(
            Enumerable.Range(0, body.ExceptionRegionCatalog.Clauses.Length),
            body.ExceptionRegionCatalog.Clauses.Select(clause => clause.Id.Ordinal));
        using var stream = File.OpenRead(SelfPath);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinition method = reader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(token));
        ImmutableArray<ExceptionRegion> rawRegions =
            pe.GetMethodBody(method.RelativeVirtualAddress)
                .ExceptionRegions;
        Assert.Equal(rawRegions.Length, body.ExceptionRegionCatalog.Clauses.Length);
        for (int ordinal = 0; ordinal < rawRegions.Length; ordinal++)
        {
            ExceptionRegion raw = rawRegions[ordinal];
            MethodExceptionClause clause = body.ExceptionRegionCatalog.Clauses[ordinal];
            Assert.Equal(raw.Kind, clause.Kind);
            Assert.Equal(raw.TryOffset, clause.ProtectedExtent.Start);
            Assert.Equal(raw.TryOffset + raw.TryLength, clause.ProtectedExtent.End);
            Assert.Equal(raw.HandlerOffset, clause.HandlerExtent.Start);
            Assert.Equal(raw.HandlerOffset + raw.HandlerLength, clause.HandlerExtent.End);
            Assert.Equal(
                raw.Kind == ExceptionRegionKind.Filter ? raw.FilterOffset : null,
                clause.FilterExtent?.Start);
            Assert.Equal(
                raw.Kind == ExceptionRegionKind.Filter ? raw.HandlerOffset : null,
                clause.FilterExtent?.End);
        }
    }

    [Fact]
    public void Read_PreservesClauseKindsRangesCatchTypesAndContexts()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        MethodBodySource source = session.MethodBodies;

        MethodBodyData catchBody = Assert.IsType<MethodBodyReadResult.Available>(
            source.Read(TokenOf(nameof(MethodExceptionRegionFactsSamples.Catch)))).Body;
        MethodExceptionClause catchClause = Assert.Single(
            catchBody.ExceptionRegionCatalog.Clauses);
        Assert.Equal(ExceptionRegionKind.Catch, catchClause.Kind);
        Assert.NotNull(catchClause.CatchType);
        Assert.NotEqual(0, catchClause.CatchType.MetadataToken);
        var catchName = Assert.IsType<MetadataTypeNameResult.Resolved>(
            catchClause.CatchType.Name);
        Assert.Equal("System.DivideByZeroException", catchName.Value);

        MethodBodyData sharedCatch = Assert.IsType<MethodBodyReadResult.Available>(
            source.Read(TokenOf(nameof(MethodExceptionRegionFactsSamples.SharedCatchExtent)))).Body;
        Assert.Equal(2, sharedCatch.ExceptionRegionCatalog.Clauses.Length);
        Assert.Equal(
            sharedCatch.ExceptionRegionCatalog.Clauses[0].ProtectedExtent,
            sharedCatch.ExceptionRegionCatalog.Clauses[1].ProtectedExtent);
        Assert.NotEqual(
            sharedCatch.ExceptionRegionCatalog.Clauses[0].Id,
            sharedCatch.ExceptionRegionCatalog.Clauses[1].Id);

        MethodBodyData filtered = Assert.IsType<MethodBodyReadResult.Available>(
            source.Read(TokenOf(nameof(MethodExceptionRegionFactsSamples.FilterAndFinally)))).Body;
        Assert.Contains(
            filtered.ExceptionRegionCatalog.Clauses,
            clause => clause.Kind == ExceptionRegionKind.Filter);
        Assert.Contains(
            filtered.ExceptionRegionCatalog.Clauses,
            clause => clause.Kind == ExceptionRegionKind.Finally);

        foreach (MethodExceptionClause clause in filtered.ExceptionRegionCatalog.Clauses)
        {
            Assert.Contains(
                filtered.ExceptionRegionCatalog.ContextsAt(clause.ProtectedExtent.Start),
                context => context.Clause.Id == clause.Id
                    && context.Role == MethodExceptionRegionRole.Protected);
            Assert.DoesNotContain(
                filtered.ExceptionRegionCatalog.ContextsAt(clause.ProtectedExtent.End),
                context => context.Clause.Id == clause.Id
                    && context.Role == MethodExceptionRegionRole.Protected);
            Assert.Contains(
                filtered.ExceptionRegionCatalog.ContextsAt(clause.HandlerExtent.Start),
                context => context.Clause.Id == clause.Id
                    && context.Role == MethodExceptionRegionRole.Handler);
            Assert.DoesNotContain(
                filtered.ExceptionRegionCatalog.ContextsAt(clause.HandlerExtent.End),
                context => context.Clause.Id == clause.Id
                    && context.Role == MethodExceptionRegionRole.Handler);

            if (clause.FilterExtent is { } filter)
            {
                Assert.Contains(
                    filtered.ExceptionRegionCatalog.ContextsAt(filter.Start),
                    context => context.Clause.Id == clause.Id
                        && context.Role == MethodExceptionRegionRole.Filter);
                Assert.DoesNotContain(
                    filtered.ExceptionRegionCatalog.ContextsAt(filter.End),
                    context => context.Clause.Id == clause.Id
                        && context.Role == MethodExceptionRegionRole.Filter);
            }
        }

        Assert.Empty(filtered.ExceptionRegionCatalog.ContextsAt(filtered.IL.Length));
    }

    [Fact]
    public void Read_RefusesWholeBodyAboveTheByteLimit()
    {
        using var session = AssemblyInspectionSession.Open(SelfPath);
        MethodBodySource source = session.MethodBodies;
        int token = TokenOf(nameof(MethodExceptionRegionFactsSamples.Catch));
        MethodBodyData body = Assert.IsType<MethodBodyReadResult.Available>(
            source.Read(token)).Body;

        var unavailable = Assert.IsType<MethodBodyReadResult.Unavailable>(
            source.Read(token, body.IL.Length - 1));
        var exceeded = Assert.IsType<MethodBodyUnavailableReason.ILByteLimitExceeded>(
            unavailable.Reason);
        Assert.Equal(token, unavailable.Method!.Value.Token);
        Assert.Equal(body.IL.Length, exceeded.ILByteCount);
        Assert.Equal(body.IL.Length - 1, exceeded.MaxILBytes);

        Assert.IsType<MethodBodyReadResult.Available>(
            source.Read(token, body.IL.Length));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.Read(token, -1));
    }

    [Fact]
    public void Read_ReportsMalformedBodyWithoutPartialEvidence()
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(MethodExceptionRegionFactsSamples.Catch));
        using (var pe = new PEReader(new MemoryStream(image, writable: false)))
        {
            MetadataReader reader = pe.GetMetadataReader();
            MethodDefinition method = reader.GetMethodDefinition(
                (MethodDefinitionHandle)MetadataTokens.EntityHandle(token));
            image[RvaToFileOffset(pe.PEHeaders, method.RelativeVirtualAddress)] = 0;
        }

        using var session = AssemblyInspectionSession.OpenPrefetched(
            new MemoryStream(image, writable: false));
        var unavailable = Assert.IsType<MethodBodyReadResult.Unavailable>(
            session.MethodBodies.Read(token));
        Assert.IsType<MethodBodyUnavailableReason.MalformedBody>(
            unavailable.Reason);
        Assert.Equal(token, unavailable.Method!.Value.Token);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(29)]
    public void Read_RejectsMalformedExceptionSectionSize(int dataSize)
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(MethodExceptionRegionFactsSamples.SharedCatchExtent));
        int sectionOffset = ExceptionSectionOffset(image, token, out bool fat);
        Assert.False(fat);
        image[sectionOffset + 1] = checked((byte)dataSize);

        using var session = AssemblyInspectionSession.OpenPrefetched(
            new MemoryStream(image, writable: false));
        var unavailable = Assert.IsType<MethodBodyReadResult.Unavailable>(
            session.MethodBodies.Read(token));
        Assert.IsType<MethodBodyUnavailableReason.MalformedBody>(
            unavailable.Reason);
    }

    [Fact]
    public void Read_RejectsChainedExceptionSectionsWhenSrmOmitsAClause()
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(MethodExceptionRegionFactsSamples.ThreeCatchExtent));
        int sectionOffset = ExceptionSectionOffset(image, token, out bool fat);
        Assert.False(fat);

        byte[] secondClause = image.AsSpan(sectionOffset + 16, 12).ToArray();
        image[sectionOffset] |= 0x80;
        image[sectionOffset + 1] = 16;
        int secondSectionOffset = sectionOffset + 16;
        image[secondSectionOffset] = 0x01;
        image[secondSectionOffset + 1] = 16;
        image[secondSectionOffset + 2] = 0;
        image[secondSectionOffset + 3] = 0;
        secondClause.CopyTo(image, secondSectionOffset + 4);

        using var session = AssemblyInspectionSession.OpenPrefetched(
            new MemoryStream(image, writable: false));
        var unavailable = Assert.IsType<MethodBodyReadResult.Unavailable>(
            session.MethodBodies.Read(token));
        Assert.IsType<MethodBodyUnavailableReason.MalformedBody>(
            unavailable.Reason);
    }

    [Fact]
    public void Read_RejectsMalformedClauseWithoutPublishingValidNeighbor()
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(MethodExceptionRegionFactsSamples.SharedCatchExtent));
        int clauseOffset = FirstExceptionClauseOffset(image, token, out bool fat);
        if (fat)
            BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(clauseOffset + 16), int.MaxValue);
        else
            image[clauseOffset + 7] = byte.MaxValue;

        using var session = AssemblyInspectionSession.OpenPrefetched(
            new MemoryStream(image, writable: false));
        var unavailable = Assert.IsType<MethodBodyReadResult.Unavailable>(
            session.MethodBodies.Read(token));
        Assert.IsType<MethodBodyUnavailableReason.MalformedBody>(
            unavailable.Reason);
        Assert.Equal(token, unavailable.Method!.Value.Token);
    }

    [Theory]
    [InlineData(0, typeof(MetadataTypeNameResult.Absent))]
    [InlineData(0x02FFFFFF, typeof(MetadataTypeNameResult.Rejected))]
    public void Read_PreservesUnavailableCatchTypeEvidence(
        int catchTypeToken,
        Type expectedEvidenceType)
    {
        byte[] image = File.ReadAllBytes(SelfPath);
        int token = TokenOf(nameof(MethodExceptionRegionFactsSamples.Catch));
        int clauseOffset = FirstExceptionClauseOffset(image, token, out bool fat);
        int catchTypeOffset = clauseOffset + (fat ? 20 : 8);
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(catchTypeOffset),
            catchTypeToken);

        using var session = AssemblyInspectionSession.OpenPrefetched(
            new MemoryStream(image, writable: false));
        MethodBodyData body = Assert.IsType<MethodBodyReadResult.Available>(
            session.MethodBodies.Read(token)).Body;
        MethodExceptionClause clause = Assert.Single(
            body.ExceptionRegionCatalog.Clauses);
        Assert.IsType(expectedEvidenceType, clause.CatchType!.Name);
        Assert.Equal(catchTypeToken, clause.CatchType.MetadataToken);

        using var context = PdbContext.OpenMetadataOnly(
            ResolvedAssemblyReference.Create(
                ReadIdentity(image),
                path: null,
                openRead: () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local("test")));
        IReadOnlyList<MethodExceptionRegionInfo> projectedRegions =
            context.ResolveExceptionRegions(token, out string? regionError);
        IReadOnlyList<ILOffsetExceptionContextInfo> projectedContexts =
            context.ResolveExceptionContext(
                token,
                clause.HandlerExtent.Start,
                out string? contextError);
        if (expectedEvidenceType == typeof(MetadataTypeNameResult.Rejected))
        {
            Assert.Empty(projectedRegions);
            Assert.NotNull(regionError);
            Assert.Empty(projectedContexts);
            Assert.NotNull(contextError);
        }
        else
        {
            Assert.Single(projectedRegions);
            Assert.Null(regionError);
            Assert.Single(projectedContexts);
            Assert.Null(contextError);
        }
    }

    [Fact]
    public void Read_PreservesRealPlatformFaultClause()
    {
        string regexPath = typeof(System.Text.RegularExpressions.Regex).Assembly.Location;
        using var session = AssemblyInspectionSession.Open(regexPath);
        MethodBodyMember witness = Assert.Single(
            session.MethodBodies.EnumerateMethods(),
            method => method.Name == "MoveNext"
                && method.DeclaringType.Contains(
                    "<EnumerateAlternationBranches>d__",
                    StringComparison.Ordinal));

        MethodBodyData body = Assert.IsType<MethodBodyReadResult.Available>(
            session.MethodBodies.Read(witness.MetadataToken)).Body;
        Assert.Contains(
            body.ExceptionRegionCatalog.Clauses,
            clause => clause.Kind == ExceptionRegionKind.Fault);
    }

    [Fact]
    public void PdbContext_ProjectsOwnerIssuedCatalogAndOffsetContext()
    {
        int token = TokenOf(nameof(MethodExceptionRegionFactsSamples.Catch));
        using var context = PdbContext.Open(SelfPath);
        MethodBodyData body = Assert.IsType<MethodBodyReadResult.Available>(
            context.MethodBodies.Read(token)).Body;
        MethodExceptionClause clause = Assert.Single(
            body.ExceptionRegionCatalog.Clauses);

        IReadOnlyList<MethodExceptionRegionInfo> rows =
            context.ResolveExceptionRegions(token, out string? regionError);
        Assert.Null(regionError);
        MethodExceptionRegionInfo row = Assert.Single(rows);
        Assert.Equal(clause.Id.Ordinal + 1, row.Region);
        Assert.Equal(clause.ProtectedExtent.Start, row.TryStart);
        Assert.Equal(clause.ProtectedExtent.End, row.TryEnd);
        Assert.Equal(clause.HandlerExtent.Start, row.HandlerStart);
        Assert.Equal(clause.HandlerExtent.End, row.HandlerEnd);
        Assert.Equal(clause.CatchType!.DisplayName, row.CaughtType);

        IReadOnlyList<ILOffsetExceptionContextInfo> contexts =
            context.ResolveExceptionContext(
                token,
                clause.HandlerExtent.Start,
                out string? contextError);
        Assert.Null(contextError);
        ILOffsetExceptionContextInfo handler = Assert.Single(
            contexts,
            item => item.Region == clause.Id.Ordinal + 1);
        Assert.Equal("catch handler", handler.Context);
    }

    static int TokenOf(string methodName) =>
        typeof(MethodExceptionRegionFactsSamples)
            .GetMethod(methodName)!
            .MetadataToken;

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            pe.GetMetadataReader());
    }

    static int RvaToFileOffset(PEHeaders headers, int rva)
    {
        int sectionIndex = headers.GetContainingSectionIndex(rva);
        Assert.True(sectionIndex >= 0);
        SectionHeader section = headers.SectionHeaders[sectionIndex];
        return checked(rva - section.VirtualAddress + section.PointerToRawData);
    }

    static int FirstExceptionClauseOffset(
        byte[] image,
        int methodToken,
        out bool fat)
        => ExceptionSectionOffset(image, methodToken, out fat) + 4;

    static int ExceptionSectionOffset(
        byte[] image,
        int methodToken,
        out bool fat)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinition method = reader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(methodToken));
        int bodyOffset = RvaToFileOffset(pe.PEHeaders, method.RelativeVirtualAddress);
        ushort flagsAndSize = BinaryPrimitives.ReadUInt16LittleEndian(
            image.AsSpan(bodyOffset, 2));
        Assert.Equal(0x03, flagsAndSize & 0x03);
        int headerSize = (flagsAndSize >> 12) * 4;
        int codeSize = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(bodyOffset + 4, 4));
        int sectionOffset = (bodyOffset + headerSize + codeSize + 3) & ~3;
        byte sectionKind = image[sectionOffset];
        Assert.Equal(0x01, sectionKind & 0x3F);
        fat = (sectionKind & 0x40) != 0;
        return sectionOffset;
    }
}
