using TianShu.Contracts.Configuration;

namespace TianShu.Configuration.Tests;

public sealed class TianShuPromptTomlConfigurationTests
{
    [Fact]
    public void Load_ScansOnlyPromptPackManifests()
    {
        using var temp = TempTianShuHome.Create();
        File.WriteAllText(Path.Combine(temp.Root, "default_prompt.toml"), "[base]\ntext = \"root legacy\"\n");
        File.WriteAllText(Path.Combine(temp.Root, "invalid.toml"), "[base\ntext = \"broken\"\n");
        Directory.CreateDirectory(Path.Combine(temp.Root, "prompt"));
        File.WriteAllText(Path.Combine(temp.Root, "prompt", "default_prompt.toml"), "[base]\ntext = \"prompt legacy\"\n");
        Directory.CreateDirectory(Path.Combine(temp.Root, "modules", "prompts", "default"));
        Directory.CreateDirectory(Path.Combine(temp.Root, "modules", "prompts", "team"));
        Directory.CreateDirectory(Path.Combine(temp.Root, "modules", "prompts", "broken"));
        File.WriteAllText(Path.Combine(temp.Root, "modules", "prompts", "default", "prompt.toml"), "[base]\ntext = \"default pack\"\n");
        File.WriteAllText(Path.Combine(temp.Root, "modules", "prompts", "team", "prompt.toml"), "[developer]\ntext = \"team pack\"\n");
        File.WriteAllText(Path.Combine(temp.Root, "modules", "prompts", "broken", "prompt.toml"), "[base\ntext = \"broken\"\n");
        File.WriteAllText(Path.Combine(temp.Root, "tianshu.toml"), "model = \"gpt-test\"\n");

        var projection = new TianShuPromptTomlConfiguration().Load(temp.Root);

        Assert.Equal(
            [Path.Combine("modules", "prompts", "default", "prompt.toml"), Path.Combine("modules", "prompts", "team", "prompt.toml")],
            projection.Files.Select(static file => file.DisplayName).Order(StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.Equal(Path.Combine(temp.Root, "modules", "prompts", "default", "prompt.toml"), projection.SelectedFilePath);
        Assert.DoesNotContain(projection.Files, static file => file.DisplayName == "default_prompt.toml");
        Assert.DoesNotContain(projection.Files, static file => file.DisplayName == Path.Combine("prompt", "default_prompt.toml"));
        Assert.DoesNotContain(projection.Files, static file => file.DisplayName == "tianshu.toml");
    }

    [Fact]
    public void Load_UsesExplicitSelectedPromptPackWhenValid()
    {
        using var temp = TempTianShuHome.Create();
        Directory.CreateDirectory(Path.Combine(temp.Root, "modules", "prompts", "default"));
        Directory.CreateDirectory(Path.Combine(temp.Root, "modules", "prompts", "research"));
        File.WriteAllText(Path.Combine(temp.Root, "modules", "prompts", "default", "prompt.toml"), "[base]\ntext = \"default\"\n");
        File.WriteAllText(Path.Combine(temp.Root, "modules", "prompts", "research", "prompt.toml"), "[base]\ntext = \"research\"\n");

        var projection = new TianShuPromptTomlConfiguration().Load(temp.Root, Path.Combine("modules", "prompts", "research", "prompt.toml"));

        Assert.Equal(Path.Combine(temp.Root, "modules", "prompts", "research", "prompt.toml"), projection.SelectedFilePath);
        var baseValue = Assert.Single(projection.Values, static value => value.Key == "base");
        Assert.Equal("research", baseValue.Text);
        Assert.True(baseValue.IsConfigured);
    }

    [Fact]
    public void Load_ReadsPromptSectionValues()
    {
        using var temp = TempTianShuHome.Create();
        var promptPath = Path.Combine(temp.Root, "modules", "prompts", "default", "prompt.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(promptPath)!);
        File.WriteAllText(
            promptPath,
            """
            [base]
            enabled = false
            mode = "replace"
            text = "基础指令"

            [model_status]
            reasoning_probe_prompt = "探针"
            """);

        var projection = new TianShuPromptTomlConfiguration().Load(temp.Root);

        var baseValue = Assert.Single(projection.Values, static value => value.Key == "base");
        Assert.False(baseValue.Enabled);
        Assert.Equal(PromptConfigurationSectionMergeMode.Replace, baseValue.Mode);
        Assert.Equal("基础指令", baseValue.Text);

        var modelStatusValue = Assert.Single(projection.Values, static value => value.Key == "model_status.reasoning_probe_prompt");
        Assert.Equal("探针", modelStatusValue.Text);
        Assert.True(modelStatusValue.IsConfigured);
    }

    [Fact]
    public void Load_ReadsPromptPackMetadataAndReportsVersionIncompatibleIssue()
    {
        using var temp = TempTianShuHome.Create();
        var promptPath = Path.Combine(temp.Root, "modules", "prompts", "future", "prompt.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(promptPath)!);
        File.WriteAllText(
            promptPath,
            """
            id = "future"
            display_name = "Future Prompt"
            enabled = true
            type = "package"
            priority = 0
            version = "3.0.0"
            min_tianshu_version = "99.0.0"
            capabilities = ["prompt:base"]
            diagnostics = ["prompt:load"]

            [base]
            text = "future"
            """);

        var projection = new TianShuPromptTomlConfiguration().Load(temp.Root, promptPath);

        var file = Assert.Single(projection.Files);
        Assert.Equal("3.0.0", file.Version);
        Assert.Equal("99.0.0", file.MinTianShuVersion);
        Assert.Equal(["prompt:base"], file.Capabilities);
        Assert.Equal(["prompt:load"], file.Diagnostics);
        Assert.Equal("unavailable", file.LoadStatus);
        Assert.Contains(projection.Issues, static issue => issue.Code == "prompt.toml.version_incompatible");
    }

    [Fact]
    public void SaveSection_WritesSelectedPromptPackWithoutTouchingTianShuToml()
    {
        using var temp = TempTianShuHome.Create();
        var TianShuTomlPath = Path.Combine(temp.Root, "tianshu.toml");
        var promptPath = Path.Combine(temp.Root, "modules", "prompts", "custom", "prompt.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(promptPath)!);
        File.WriteAllText(TianShuTomlPath, "model = \"gpt-test\"\n");
        File.WriteAllText(promptPath, "[developer]\ntext = \"old\"\nfile = \"legacy.md\"\n");

        new TianShuPromptTomlConfiguration().SaveSection(promptPath, new PromptConfigurationSectionChange
        {
            SectionKey = "developer",
            Enabled = true,
            Mode = PromptConfigurationSectionMergeMode.Prepend,
            Text = "新的开发者指令",
        });

        var projection = new TianShuPromptTomlConfiguration().Load(temp.Root, promptPath);
        var value = Assert.Single(projection.Values, static value => value.Key == "developer");
        Assert.Equal("新的开发者指令", value.Text);
        Assert.Equal(PromptConfigurationSectionMergeMode.Prepend, value.Mode);
        Assert.True(value.Enabled);
        Assert.Null(value.File);
        Assert.Equal("model = \"gpt-test\"\n", File.ReadAllText(TianShuTomlPath));
    }

    [Fact]
    public void CreateCopyAndDeletePromptFiles_StayInsidePromptPackRoots()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuPromptTomlConfiguration();

        var createdPath = configuration.CreatePromptFile(temp.Root, "custom");
        Assert.Equal(Path.Combine(temp.Root, "modules", "prompts", "custom", "prompt.toml"), createdPath);
        Assert.True(File.Exists(createdPath));

        File.AppendAllText(createdPath, "[base]\ntext = \"custom\"\n");
        var copiedPath = configuration.CopyPromptFile(temp.Root, createdPath, "copied");
        Assert.Equal(Path.Combine(temp.Root, "modules", "prompts", "copied", "prompt.toml"), copiedPath);
        Assert.Equal(File.ReadAllText(createdPath), File.ReadAllText(copiedPath));

        configuration.DeletePromptFile(temp.Root, copiedPath);
        Assert.False(File.Exists(copiedPath));
    }

    [Fact]
    public void CreatePromptFile_RejectsPathsOutsidePromptPackRoots()
    {
        using var temp = TempTianShuHome.Create();
        var configuration = new TianShuPromptTomlConfiguration();

        Assert.Throws<InvalidOperationException>(() => configuration.CreatePromptFile(temp.Root, "..\\outside"));
        Assert.Throws<InvalidOperationException>(() => configuration.CreatePromptFile(temp.Root, "nested\\custom"));
        Assert.Throws<InvalidOperationException>(() => configuration.SaveSection(
            Path.Combine(temp.Root, "prompt", "custom_prompt.toml"),
            new PromptConfigurationSectionChange { SectionKey = "base", Text = "nope" }));
    }

    private sealed class TempTianShuHome : IDisposable
    {
        private TempTianShuHome(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TempTianShuHome Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"tianshu-prompt-config-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new TempTianShuHome(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

