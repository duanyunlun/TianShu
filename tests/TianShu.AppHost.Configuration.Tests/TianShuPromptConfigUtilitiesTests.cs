using System.Text;
using TianShu.Configuration;

namespace TianShu.AppHost.Configuration.Tests;

public sealed class TianShuPromptConfigUtilitiesTests
{
    [Fact]
    public void ApplyPromptConfigLayer_LoadsPromptPackSelectedProfile()
    {
        var directory = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(directory, "tianshu.toml");
            var packDirectory = Path.Combine(directory, "modules", "prompts", "team");
            Directory.CreateDirectory(packDirectory);
            File.WriteAllText(configPath, "model = \"gpt-5.5\"", Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(packDirectory, "prompt.toml"),
                """
                id = "team"
                enabled = true
                priority = 10
                profile = "strict"

                [base]
                text = "base default"

                [language_policy]
                mode = "append"
                text = "language default"

                [model_status]
                reasoning_probe_prompt = "probe default"

                [profiles.strict.base]
                text = "base strict"

                [profiles.strict.model_status]
                reasoning_probe_prompt = "probe strict"
                """,
                Encoding.UTF8);

            var effectiveConfig = new Dictionary<string, object?>(StringComparer.Ordinal);

            TianShuPromptConfigUtilities.ApplyPromptConfigLayer(
                effectiveConfig,
                [
                    new TianShuPromptConfigLayer(
                        configPath,
                        directory,
                        new Dictionary<string, object?>(StringComparer.Ordinal),
                        IsDisabled: false,
                        FileExists: true),
                ],
                cwd: null);

            var prompt = TianShuPromptConfigUtilities.FromConfig(effectiveConfig);
            Assert.Equal("base strict", prompt.Base?.Text);
            Assert.Equal(TianShuPromptMergeMode.Append, prompt.LanguagePolicy?.Mode);
            Assert.Equal("language default", prompt.LanguagePolicy?.Text);
            Assert.Equal("probe strict", prompt.ModelStatusReasoningProbePrompt);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ApplyPromptConfigLayer_IgnoresLegacyDefaultPromptToml()
    {
        var directory = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(directory, "tianshu.toml");
            Directory.CreateDirectory(Path.Combine(directory, "prompt"));
            File.WriteAllText(configPath, "model = \"gpt-5.5\"", Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(directory, "default_prompt.toml"),
                """
                [developer]
                text = "developer from root legacy"
                """,
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(directory, "prompt", "default_prompt.toml"),
                """
                [developer]
                text = "developer from prompt directory legacy"
                """,
                Encoding.UTF8);

            var effectiveConfig = new Dictionary<string, object?>(StringComparer.Ordinal);

            TianShuPromptConfigUtilities.ApplyPromptConfigLayer(
                effectiveConfig,
                [
                    new TianShuPromptConfigLayer(
                        configPath,
                        directory,
                        new Dictionary<string, object?>(StringComparer.Ordinal),
                        IsDisabled: false,
                        FileExists: true),
                ],
                cwd: null);

            var prompt = TianShuPromptConfigUtilities.FromConfig(effectiveConfig);
            Assert.Null(prompt.Developer);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ApplyPromptConfigLayer_WhenSectionUsesFile_ResolvesRelativeToPromptPackManifest()
    {
        var directory = CreateTempDirectory();
        try
        {
            var packDirectory = Path.Combine(directory, "modules", "prompts", "team");
            Directory.CreateDirectory(packDirectory);
            var configPath = Path.Combine(directory, "tianshu.toml");
            File.WriteAllText(configPath, "model = \"gpt-5.5\"", Encoding.UTF8);
            File.WriteAllText(Path.Combine(packDirectory, "base.md"), "base from file", Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(packDirectory, "prompt.toml"),
                """
                id = "team"
                enabled = true
                priority = 10

                [base]
                mode = "prepend"
                file = "base.md"

                [realtime]
                start_instructions_file = "base.md"
                """,
                Encoding.UTF8);

            var effectiveConfig = new Dictionary<string, object?>(StringComparer.Ordinal);

            TianShuPromptConfigUtilities.ApplyPromptConfigLayer(
                effectiveConfig,
                [
                    new TianShuPromptConfigLayer(
                        configPath,
                        directory,
                        new Dictionary<string, object?>(StringComparer.Ordinal),
                        IsDisabled: false,
                        FileExists: true),
                ],
                cwd: null);

            var prompt = TianShuPromptConfigUtilities.FromConfig(effectiveConfig);
            Assert.Equal(TianShuPromptMergeMode.Prepend, prompt.Base?.Mode);
            Assert.Equal("base from file", prompt.Base?.Text);
            Assert.Equal("base from file", prompt.RealtimeStartInstructions);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ApplyPromptConfigLayer_MergesPromptPacksByPriority()
    {
        var directory = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(directory, "tianshu.toml");
            var basePackDirectory = Path.Combine(directory, "modules", "prompts", "base");
            var teamPackDirectory = Path.Combine(directory, "modules", "prompts", "team");
            Directory.CreateDirectory(basePackDirectory);
            Directory.CreateDirectory(teamPackDirectory);
            File.WriteAllText(configPath, "model = \"gpt-5.5\"", Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(basePackDirectory, "prompt.toml"),
                """
                id = "base"
                enabled = true
                priority = 0

                [base]
                mode = "append"
                text = "base from base pack"

                [developer]
                text = "developer from base pack"
                """,
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(teamPackDirectory, "prompt.toml"),
                """
                id = "team"
                enabled = true
                priority = 10

                [base]
                mode = "prepend"
                text = "base from team pack"
                """,
                Encoding.UTF8);

            var effectiveConfig = new Dictionary<string, object?>(StringComparer.Ordinal);

            TianShuPromptConfigUtilities.ApplyPromptConfigLayer(
                effectiveConfig,
                [
                    new TianShuPromptConfigLayer(
                        configPath,
                        directory,
                        new Dictionary<string, object?>(StringComparer.Ordinal),
                        IsDisabled: false,
                        FileExists: true),
                ],
                cwd: null);

            var prompt = TianShuPromptConfigUtilities.FromConfig(effectiveConfig);
            Assert.Equal("base from team pack", prompt.Base?.Text);
            Assert.Equal(TianShuPromptMergeMode.Prepend, prompt.Base?.Mode);
            Assert.Equal("developer from base pack", prompt.Developer?.Text);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ApplySection_ShouldHonorMergeModesAndDisabledSections()
    {
        Assert.Equal(
            $"built-in{Environment.NewLine}{Environment.NewLine}custom",
            TianShuPromptConfigUtilities.ApplySection(
                new TianShuPromptSection(true, TianShuPromptMergeMode.Append, "custom"),
                "built-in"));
        Assert.Equal(
            $"custom{Environment.NewLine}{Environment.NewLine}built-in",
            TianShuPromptConfigUtilities.ApplySection(
                new TianShuPromptSection(true, TianShuPromptMergeMode.Prepend, "custom"),
                "built-in"));
        Assert.Equal(
            "custom",
            TianShuPromptConfigUtilities.ApplySection(
                new TianShuPromptSection(true, TianShuPromptMergeMode.Replace, "custom"),
                "built-in"));
        Assert.Null(TianShuPromptConfigUtilities.ApplySection(
            new TianShuPromptSection(false, TianShuPromptMergeMode.Replace, "custom"),
            "built-in"));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-prompt-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}

