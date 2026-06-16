using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Artifacts.Tests;

public sealed class ArtifactContentContractTests
{
    [Fact]
    public void ArtifactTextContent_PreservesEncodingAndMetadata()
    {
        var metadata = new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["language"] = StructuredValue.FromString("markdown"),
        });

        var content = new ArtifactTextContent(
            "# Summary",
            mediaType: "text/markdown",
            encoding: "utf-8",
            metadata: metadata);

        Assert.Equal(ArtifactContentKind.Text, content.Kind);
        Assert.Equal("# Summary", content.Text);
        Assert.Equal("text/markdown", content.MediaType);
        Assert.Equal("utf-8", content.Encoding);
        Assert.Equal("markdown", content.Metadata.Entries["language"].GetString());
    }

    [Fact]
    public void ArtifactStructuredContent_PreservesStructuredValueAndSchema()
    {
        var content = new ArtifactStructuredContent(
            StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["status"] = StructuredValue.FromString("published"),
                ["count"] = StructuredValue.FromNumber("2"),
            }),
            mediaType: "application/json",
            schema: "tianshu.artifact.summary.v1");

        Assert.Equal(ArtifactContentKind.Structured, content.Kind);
        Assert.Equal("application/json", content.MediaType);
        Assert.Equal("tianshu.artifact.summary.v1", content.Schema);
        Assert.Equal("published", content.Value.GetProperty("status").GetString());
        Assert.Equal(2, content.Value.GetProperty("count").GetInt32());
    }

    [Fact]
    public void ArtifactBinaryContentReference_RejectsBlankReference()
    {
        Assert.Throws<ArgumentException>(() => new ArtifactBinaryContentReference(
            " ",
            mediaType: "application/pdf",
            sizeInBytes: 42,
            digest: "sha256:abc"));
    }
}
