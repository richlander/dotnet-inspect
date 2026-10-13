using System.Globalization;
using System.Numerics;
using System.Text.Json;
using DotnetInspector.Sections;
using QuerySpace.Explanation;

namespace DotnetInspector.Sections.Tests;

public sealed class ResourceExplanationDataProjectionTests
{
    [Fact]
    public void DetachedResource_PreservesLosslessValuesAndUnavailableOutcomesWithoutLinks()
    {
        var owner = new ExplanationOwnerIdentity("projection-test");
        var schemaId = new ExplanationSchemaIdentity(owner, "values");
        var type = new ExplanationResourceTypeIdentity(schemaId, "resource");
        var text = new ExplanationDataShapeIdentity(schemaId, "text");
        var integer = new ExplanationDataShapeIdentity(schemaId, "integer");
        var octets = new ExplanationDataShapeIdentity(schemaId, "octets");
        var decimalShape = new ExplanationDataShapeIdentity(schemaId, "decimal");
        var version = new ExplanationSchemaVersion(1);
        var budget = new ExplanationValueBudget(4096, 4, 16);
        ExplanationFactIdentity huge = new(type, "huge");
        ExplanationFactIdentity bytes = new(type, "bytes");
        ExplanationFactIdentity fraction = new(type, "fraction");
        ExplanationFactIdentity unavailable = new(type, "unavailable");
        ExplanationFactIdentity failed = new(type, "failed");
        ExplanationFactIdentity empty = new(type, "empty");
        var schema = new ExplanationSchema(schemaId, version,
            [
                new ExplanationDataShapeDeclaration.Scalar(text, "Text", "Text.", budget,
                    ExplanationScalarKind.Text),
                new ExplanationDataShapeDeclaration.Scalar(integer, "Integer", "Integer.", budget,
                    ExplanationScalarKind.Integer),
                new ExplanationDataShapeDeclaration.Scalar(octets, "Octets", "Octets.", budget,
                    ExplanationScalarKind.Octets),
                new ExplanationDataShapeDeclaration.Scalar(decimalShape, "Decimal", "Decimal.", budget,
                    ExplanationScalarKind.Decimal),
            ],
            [new ExplanationResourceTypeDeclaration(type, "Resource", "One resource.", text,
                [
                    new(huge, "Huge", "Huge integer.", integer, ExplanationCardinality.RequiredOne,
                        ExplanationObservationStates.Available),
                    new(bytes, "Bytes", "Octets.", octets, ExplanationCardinality.RequiredOne,
                        ExplanationObservationStates.Available),
                    new(fraction, "Fraction", "Decimal.", decimalShape, ExplanationCardinality.RequiredOne,
                        ExplanationObservationStates.Available),
                    new(unavailable, "Unavailable", "Unavailable value.", text,
                        ExplanationCardinality.RequiredOne,
                        ExplanationObservationStates.Available | ExplanationObservationStates.Unavailable,
                        unavailableDataShape: text),
                    new(failed, "Failed", "Failed value.", text,
                        ExplanationCardinality.RequiredOne,
                        ExplanationObservationStates.Available | ExplanationObservationStates.Failed,
                        failureDataShape: text),
                    new(empty, "Empty", "Empty list.", text, ExplanationCardinality.OrderedMany,
                        ExplanationObservationStates.Available, maximumValueCount: 10),
                ])]);
        BigInteger number = BigInteger.Pow(10, 50) + 1;
        const decimal precise = 0.1234567890123456789012345678m;
        var key = new ExplanationResourceKey(owner, type, ExplanationValue.Text("detached"));
        ExplanationResourceSnapshot snapshot = ExplanationConformance.CreateSnapshot(
            [schema], key, version, ExplanationSnapshotScope.Detached, [],
            [
                new(huge, ExplanationObservationState.Available, [ExplanationValue.Integer(number)]),
                new(bytes, ExplanationObservationState.Available,
                    [new ExplanationValue.Scalar(ExplanationScalarValue.FromOctets([0, 128, 255]))]),
                new(fraction, ExplanationObservationState.Available,
                    [new ExplanationValue.Scalar(ExplanationScalarValue.FromDecimal(precise))]),
                new(unavailable, ExplanationObservationState.Unavailable,
                    outcomeData: ExplanationValue.Text("not provisioned")),
                new(failed, ExplanationObservationState.Failed,
                    outcomeData: ExplanationValue.Text("failed visibly")),
                new(empty, ExplanationObservationState.Available, []),
            ], []);
        // Detached snapshots are supplied directly, rather than acquiring a subject or minting a path.
        var resource = new ResourceExplanationResource(null, key, version,
            ExplanationSnapshotScope.Detached, snapshot.Addresses, snapshot.Facts);
        var document = new ResourceExplanationDocument(null, key, [schema], [resource], [],
            new ResourceExplanationTraversalReceipt(0, 1, 1, 1, 100, 0, 1, 0, 0,
                0, ResourceExplanationCompleteness.Complete, []));
        int bindings = 0;
        JsonElement data = ResourceExplanationDataProjection.Create(document, _ =>
        {
            bindings++;
            throw new InvalidOperationException("No address is available.");
        });
        Assert.Equal(0, bindings);
        Assert.Empty(data.GetProperty("_links").EnumerateObject());
        Assert.False(data.TryGetProperty("path", out _));
        JsonElement facts = data.GetProperty("facts");
        Assert.Equal(number.ToString(CultureInfo.InvariantCulture), facts.GetProperty("huge").GetString());
        Assert.Equal(precise.ToString(CultureInfo.InvariantCulture), facts.GetProperty("fraction").GetString());
        Assert.Equal(new byte[] { 0, 128, 255 }, facts.GetProperty("bytes").GetBytesFromBase64());
        Assert.Empty(facts.GetProperty("empty").EnumerateArray());
        Assert.False(facts.TryGetProperty("unavailable", out _));
        Assert.Equal("Unavailable", data.GetProperty("fact_states").GetProperty("unavailable")
            .GetProperty("state").GetString());
        Assert.Equal("failed visibly", data.GetProperty("fact_states").GetProperty("failed")
            .GetProperty("outcome").GetString());
    }
}
