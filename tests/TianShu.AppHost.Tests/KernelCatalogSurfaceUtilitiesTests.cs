using System.Text.Json;
using TianShu.AppHost.Catalog;

namespace TianShu.AppHost.Tests;

public sealed class KernelCatalogSurfaceUtilitiesTests
{
    [Fact]
    public void GetBuiltInModels_ShouldMarkExactlyOneDefaultModel()
    {
        var models = KernelCatalogSurfaceUtilities.GetBuiltInModels();

        Assert.NotEmpty(models);
        Assert.Single(models, static model => model.IsDefault);
    }

    [Fact]
    public void ToModelPayload_ShouldProjectModelCapabilities()
    {
        var model = KernelCatalogSurfaceUtilities.GetBuiltInModels()
            .First(static item => !item.Hidden);

        var payload = KernelCatalogSurfaceUtilities.ToModelPayload(model);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload));

        Assert.Equal(model.Model, json.RootElement.GetProperty("model").GetString());
        Assert.Equal(model.IsDefault, json.RootElement.GetProperty("isDefault").GetBoolean());
        Assert.True(json.RootElement.TryGetProperty("supportedReasoningEfforts", out _));
    }

    [Fact]
    public void TryGetBuiltInModel_ShouldResolveKnownModel()
    {
        var resolved = KernelCatalogSurfaceUtilities.TryGetBuiltInModel("gpt-5.4", out var model);

        Assert.True(resolved);
        Assert.NotNull(model);
        Assert.Equal("gpt-5.4", model!.Model);
        Assert.NotEmpty(model.SupportedReasoningEfforts);
    }

    [Fact]
    public void GetBuiltInModelNames_ShouldReturnBuiltInModelList()
    {
        var modelNames = KernelCatalogSurfaceUtilities.GetBuiltInModelNames();

        Assert.NotEmpty(modelNames);
        Assert.Contains("gpt-5", modelNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveFeatureEnabledState_ShouldHonorConfigOverride()
    {
        var descriptor = KernelCatalogSurfaceUtilities.GetExperimentalFeatureDescriptors()
            .First(static item => string.Equals(item.Name, "apps", StringComparison.Ordinal));

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["features.apps"] = "true",
        };

        var enabled = KernelCatalogSurfaceUtilities.ResolveFeatureEnabledState(descriptor, values);

        Assert.True(enabled);
    }

    [Fact]
    public void GetExperimentalFeatureDescriptors_ShouldUseTianShuBrandingInUserFacingText()
    {
        var descriptors = KernelCatalogSurfaceUtilities.GetExperimentalFeatureDescriptors();

        Assert.NotEmpty(descriptors);
        foreach (var descriptor in descriptors)
        {
            Assert.DoesNotContain("Codex", descriptor.DisplayName ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("Codex", descriptor.Description ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("Codex", descriptor.Announcement ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BuildCollaborationModeMasks_ShouldReturnPlanAndDefaultMasks()
    {
        var masks = KernelCatalogSurfaceUtilities.BuildCollaborationModeMasks();

        Assert.Collection(
            masks,
            first =>
            {
                Assert.Equal("Plan", first.Name);
                Assert.Equal("plan", first.Mode);
                Assert.Equal("medium", first.ReasoningEffort);
            },
            second =>
            {
                Assert.Equal("Default", second.Name);
                Assert.Equal("default", second.Mode);
                Assert.Null(second.Model);
                Assert.Null(second.ReasoningEffort);
            });
    }
}
