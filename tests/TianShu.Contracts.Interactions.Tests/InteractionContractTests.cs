using TianShu.Contracts.Interactions;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Interactions.Tests;

public sealed class InteractionContractTests
{
    [Fact]
    public void InteractionEnvelope_RequiresAtLeastOneItem()
    {
        Assert.Throws<ArgumentException>(() => new InteractionEnvelope(
            new InteractionEnvelopeId("interaction-002"),
            new InteractionSource(InteractionSourceKind.Host, "cli"),
            Array.Empty<InteractionItem>()));
    }

    [Fact]
    public void TextInteractionItem_PreservesTextElements()
    {
        var item = new TextInteractionItem(
            "请分析 Contracts 设计",
            new[]
            {
                new TextInteractionElement(new InteractionByteRange(0, 2), "动作"),
            });

        Assert.Equal("text", item.Kind);
        Assert.Single(item.Elements);
        Assert.Equal("动作", item.Elements[0].Placeholder);
    }

    [Fact]
    public void StructuredInteractionItem_PreservesSemanticKindAndPayload()
    {
        var item = new StructuredInteractionItem(
            "approval_response",
            StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["decision"] = StructuredValue.FromString("approve"),
            }));

        Assert.Equal("approval_response", item.Kind);
        Assert.Equal("approval_response", item.SemanticKind);
        Assert.Equal("approve", item.Payload.Properties["decision"].StringValue);
    }

    [Fact]
    public void InteractionEnvelopeAccepted_UsesEnvelopeAndTarget()
    {
        var envelope = new InteractionEnvelope(
            new InteractionEnvelopeId("interaction-003"),
            new InteractionSource(InteractionSourceKind.Host, "vsix"),
            new InteractionItem[]
            {
                new TextInteractionItem("继续落地 Contracts"),
            },
            new InteractionTarget(new CollaborationSpaceId("space-003"), new ThreadId("thread-003")));

        var accepted = new InteractionEnvelopeAccepted(envelope.Id, envelope.Target);

        Assert.Equal(envelope.Id, accepted.EnvelopeId);
        Assert.Equal(envelope.Target, accepted.Target);
    }

    [Fact]
    public void InteractionEnvelopeRef_FromEnvelope_CopiesMinimalIdentity()
    {
        var createdAt = new DateTimeOffset(2026, 4, 8, 10, 30, 0, TimeSpan.Zero);
        var envelope = new InteractionEnvelope(
            new InteractionEnvelopeId("interaction-004"),
            new InteractionSource(InteractionSourceKind.Approval, "cli"),
            new InteractionItem[]
            {
                new TextInteractionItem("继续"),
            },
            createdAt: createdAt);

        var reference = InteractionEnvelopeRef.From(envelope);

        Assert.Equal(envelope.Id, reference.Id);
        Assert.Equal(envelope.Source.Kind, reference.SourceKind);
        Assert.Equal(envelope.Source.Surface, reference.Surface);
        Assert.Equal(createdAt, reference.CreatedAt);
    }
}
