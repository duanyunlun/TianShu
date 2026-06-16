using TianShu.Contracts.Primitives;
using System.Text.Json;

namespace TianShu.Contracts.Primitives.Tests;

public sealed class PrimitivesContractTests
{
    [Fact]
    public void ThreadId_RejectsWhitespace()
    {
        Assert.Throws<ArgumentException>(() => new ThreadId(" "));
    }

    [Fact]
    public void InteractionEnvelopeId_RetainsOriginalValue()
    {
        var id = new InteractionEnvelopeId("interaction-001");

        Assert.Equal("interaction-001", id.Value);
        Assert.Equal("interaction-001", id.ToString());
    }

    [Fact]
    public void StructuredValue_RoundTripsPlainObjectGraph()
    {
        var value = StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["message"] = "hello",
            ["attempt"] = 2,
            ["flags"] = new object?[] { "plan", true },
        });

        var plainObject = Assert.IsType<Dictionary<string, object?>>(value.ToPlainObject());

        Assert.Equal("hello", plainObject["message"]);
        Assert.Equal(2L, plainObject["attempt"]);

        var flags = Assert.IsType<object?[]>(plainObject["flags"]);
        Assert.Equal("plan", flags[0]);
        Assert.Equal(true, flags[1]);
    }

    [Fact]
    public void StructuredValue_FromJsonElement_PreservesObjectShape_AndSupportsScalarAccessors()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "message": "hello",
              "attempt": 3,
              "enabled": true
            }
            """);

        var value = StructuredValue.FromJsonElement(document.RootElement);

        Assert.Equal("hello", value.GetProperty("message").GetString());
        Assert.Equal(3, value.GetProperty("attempt").GetInt32());
        Assert.True(value.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void StructuredValue_JsonSerializer_RoundTripsStructuredPayload()
    {
        var value = StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["mode"] = "plan",
            ["attempt"] = 3,
            ["flags"] = new object?[] { true, "typed" },
        });

        var json = JsonSerializer.Serialize(value);
        var roundTripped = JsonSerializer.Deserialize<StructuredValue>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal("plan", roundTripped!.GetProperty("mode").GetString());
        Assert.Equal(3, roundTripped.GetProperty("attempt").GetInt32());
        Assert.True(roundTripped.GetProperty("flags").Items[0].GetBoolean());
        Assert.Equal("typed", roundTripped.GetProperty("flags").Items[1].GetString());
    }

    [Fact]
    public void StructuredValue_JsonSerializer_PreservesNumericTokenShape()
    {
        var value = StructuredValue.FromNumber("1.25");

        var json = JsonSerializer.Serialize(value);

        Assert.Equal("1.25", json);
    }

    [Fact]
    public void LabelSet_DeduplicatesAndNormalizesValues()
    {
        var labels = LabelSet.Create(new[] { "design", "design", " contracts ", "", "tests" });

        Assert.Equal(new[] { "design", "contracts", "tests" }, labels.Values);
    }
}
