using System.Text.Json;
using Windows.Agent.Interface;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Windows.Agent.Cli;
using Windows.Agent.Tools.Desktop;
using Windows.Agent.Tools.FileSystem;
using Windows.Agent.Tools.OCR;
using Windows.Agent.Tools.SystemControl;
using Windows.Agent.Tools.Contracts;
using Windows.Agent.Tools.Diagnostics;
using Windows.Agent.Uia;

namespace Windows.Agent.Cli.Test;

public class CliDispatcherTests
{
    [Fact]
    public async Task DesktopClick_ShouldInvokeDesktopService()
    {
        var desktop = new Mock<IDesktopService>(MockBehavior.Strict);
        desktop
            .Setup(s => s.ClickAsync(1, 2, "left", 1))
            .ReturnsAsync("ok");

        var sp = BuildServiceProvider(services =>
        {
            services.AddSingleton(desktop.Object);
            services.AddTransient<ClickTool>();
        });

        var output = new StringWriter();
        var exit = await CliDispatcher.RunAsync(
            new[] { "desktop", "click", "--x", "1", "--y", "2", "--button", "left", "--clicks", "1", "--dangerous", "--pretty" },
            sp,
            output);

        Assert.Equal(0, exit);
        desktop.VerifyAll();

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.Equal("1.0", doc.RootElement.GetProperty("schemaVersion").GetString());
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("OK", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal("Windows.Agent.Tools.Desktop.ClickTool.ClickAsync", doc.RootElement.GetProperty("tool").GetString());
        Assert.Equal("ok", doc.RootElement.GetProperty("result").GetProperty("raw").GetString());
    }

    [Fact]
    public async Task FileSystemRead_ShouldInvokeFileSystemService_AndExposeParsedJson()
    {
        var fs = new Mock<IFileSystemService>(MockBehavior.Strict);
        fs
            .Setup(s => s.ReadFileAsync(@"C:\temp\a.txt"))
            .ReturnsAsync(("hello", 0));

        var sp = BuildServiceProvider(services =>
        {
            services.AddSingleton(fs.Object);
            services.AddTransient<ReadFileTool>();
        });

        var output = new StringWriter();
        var exit = await CliDispatcher.RunAsync(
            new[] { "fs", "read", "--path", @"C:\temp\a.txt", "--pretty" },
            sp,
            output);

        Assert.Equal(0, exit);
        fs.VerifyAll();

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.Equal("1.0", doc.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("Windows.Agent.Tools.FileSystem.ReadFileTool.ReadFileAsync", doc.RootElement.GetProperty("tool").GetString());

        var parsed = doc.RootElement.GetProperty("result").GetProperty("parsed");
        Assert.True(parsed.GetProperty("success").GetBoolean());
        Assert.Equal("hello", parsed.GetProperty("content").GetString());
    }

    [Fact]
    public async Task OcrScreen_ShouldInvokeOcrService()
    {
        var ocr = new Mock<IOcrService>(MockBehavior.Strict);
        ocr
            .Setup(s => s.ExtractTextFromScreenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(("abc", 0));

        var sp = BuildServiceProvider(services =>
        {
            services.AddSingleton(ocr.Object);
            services.AddTransient<ExtractTextFromScreenTool>();
        });

        var output = new StringWriter();
        var exit = await CliDispatcher.RunAsync(
            new[] { "ocr", "screen", "--pretty" },
            sp,
            output);

        Assert.Equal(0, exit);
        ocr.VerifyAll();

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.Equal("1.0", doc.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(
            "Windows.Agent.Tools.OCR.ExtractTextFromScreenTool.ExtractTextFromScreenAsync",
            doc.RootElement.GetProperty("tool").GetString());

        var parsed = doc.RootElement.GetProperty("result").GetProperty("parsed");
        Assert.True(parsed.GetProperty("success").GetBoolean());
        Assert.Equal("abc", parsed.GetProperty("text").GetString());
    }

    [Fact]
    public async Task SystemVolumePercent_ShouldInvokeSystemControlService()
    {
        var sys = new Mock<ISystemControlService>(MockBehavior.Strict);
        sys
            .Setup(s => s.SetVolumePercentAsync(30))
            .ReturnsAsync("done");

        var sp = BuildServiceProvider(services =>
        {
            services.AddSingleton(sys.Object);
            services.AddTransient<VolumeTool>();
        });

        var output = new StringWriter();
        var exit = await CliDispatcher.RunAsync(
            new[] { "sys", "volume", "--percent", "30", "--dangerous", "--pretty" },
            sp,
            output);

        Assert.Equal(0, exit);
        sys.VerifyAll();

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.Equal("1.0", doc.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("Windows.Agent.Tools.SystemControl.VolumeTool.SetVolumePercentAsync", doc.RootElement.GetProperty("tool").GetString());
        Assert.Equal("done", doc.RootElement.GetProperty("result").GetProperty("raw").GetString());
    }

    [Fact]
    public async Task DesktopShortcut_ShouldSplitKeysAndInvokeService()
    {
        var desktop = new Mock<IDesktopService>(MockBehavior.Strict);
        desktop
            .Setup(s => s.ShortcutAsync(It.Is<string[]>(k => k.SequenceEqual(new[] { "ctrl", "shift", "esc" }))))
            .ReturnsAsync("ok");

        var sp = BuildServiceProvider(services =>
        {
            services.AddSingleton(desktop.Object);
            services.AddTransient<ShortcutTool>();
        });

        var output = new StringWriter();
        var exit = await CliDispatcher.RunAsync(
            new[] { "desktop", "shortcut", "--keys", "ctrl+shift+esc", "--dangerous", "--pretty" },
            sp,
            output);

        Assert.Equal(0, exit);
        desktop.VerifyAll();

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.Equal("1.0", doc.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("Windows.Agent.Tools.Desktop.ShortcutTool.ShortcutAsync", doc.RootElement.GetProperty("tool").GetString());
        Assert.Equal("ok", doc.RootElement.GetProperty("result").GetProperty("raw").GetString());
    }

    [Fact]
    public async Task DesktopClick_WithoutDangerous_ShouldFailBeforeResolvingTool()
    {
        var sp = BuildServiceProvider(_ => { });

        var output = new StringWriter();
        var exit = await CliDispatcher.RunAsync(
            new[] { "desktop", "click", "--x", "1", "--y", "2", "--pretty" },
            sp,
            output);

        Assert.Equal(1, exit);

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("POLICY_DENIED", doc.RootElement.GetProperty("code").GetString());
        Assert.Contains("--dangerous", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task DesktopUiaTree_ShouldInvokeUiaService()
    {
        var uia = new Mock<IUiaService>(MockBehavior.Strict);
        uia
            .Setup(s => s.GetTreeAsync("AppA", 2, "uia3"))
            .ReturnsAsync("{\"success\":true,\"windowTitleRegex\":\"AppA\",\"depth\":2}");

        var sp = BuildServiceProvider(services =>
        {
            services.AddSingleton(uia.Object);
            services.AddTransient<UiaTool>();
        });

        var output = new StringWriter();
        var exit = await CliDispatcher.RunAsync(
            new[] { "desktop", "uia-tree", "--window", "AppA", "--depth", "2", "--pretty" },
            sp,
            output);

        Assert.Equal(0, exit);
        uia.VerifyAll();

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.Equal("1.0", doc.RootElement.GetProperty("schemaVersion").GetString());
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Windows.Agent.Tools.Desktop.UiaTool.GetTreeAsync", doc.RootElement.GetProperty("tool").GetString());
    }

    [Fact]
    public async Task DesktopUiaInvoke_WithoutDangerous_ShouldFailBeforeResolvingTool()
    {
        var sp = BuildServiceProvider(_ => { });

        var output = new StringWriter();
        var exit = await CliDispatcher.RunAsync(
            new[] { "desktop", "uia-invoke", "--window", "AppA", "--selector", "name=OK", "--pretty" },
            sp,
            output);

        Assert.Equal(1, exit);

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("POLICY_DENIED", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task DesktopUiaFind_WithBadSelector_ShouldFailWithToolFailed()
    {
        var uia = new Mock<IUiaService>(MockBehavior.Strict);

        var sp = BuildServiceProvider(services =>
        {
            services.AddSingleton(uia.Object);
            services.AddTransient<UiaTool>();
        });

        var output = new StringWriter();
        var exit = await CliDispatcher.RunAsync(
            new[] { "desktop", "uia-find", "--window", "AppA", "--selector", "bad", "--pretty" },
            sp,
            output);

        Assert.Equal(1, exit);
        uia.VerifyAll();

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("TOOL_FAILED", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task DesktopUiaFind_WithSelector_ShouldParseAndInvokeUiaService()
    {
        var uia = new Mock<IUiaService>(MockBehavior.Strict);
        uia
            .Setup(s => s.FindAsync(
                "AppA",
                It.Is<UiaSelector>(sel => sel.AutomationId == "btnSendHttp" && sel.ControlType == "Button"),
                7,
                "uia3"))
            .ReturnsAsync("{\"success\":true,\"matchCount\":1}");

        var sp = BuildServiceProvider(services =>
        {
            services.AddSingleton(uia.Object);
            services.AddTransient<UiaTool>();
        });

        var output = new StringWriter();
        var exit = await CliDispatcher.RunAsync(
            new[]
            {
                "desktop",
                "uia-find",
                "--window",
                "AppA",
                "--selector",
                "automationId=btnSendHttp;controlType=Button",
                "--limit",
                "7",
                "--pretty"
            },
            sp,
            output);

        Assert.Equal(0, exit);
        uia.VerifyAll();

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Windows.Agent.Tools.Desktop.UiaTool.FindAsync", doc.RootElement.GetProperty("tool").GetString());
    }

    [Fact]
    public async Task DesktopUiaTree_WithBackendUia2_ShouldPassBackendToService()
    {
        var uia = new Mock<IUiaService>(MockBehavior.Strict);
        uia
            .Setup(s => s.GetTreeAsync("AppA", 1, "uia2"))
            .ReturnsAsync("{\"success\":true}");

        var sp = BuildServiceProvider(services =>
        {
            services.AddSingleton(uia.Object);
            services.AddTransient<UiaTool>();
        });

        var output = new StringWriter();
        var exit = await CliDispatcher.RunAsync(
            new[] { "desktop", "uia-tree", "--window", "AppA", "--depth", "1", "--backend", "uia2", "--pretty" },
            sp,
            output);

        Assert.Equal(0, exit);
        uia.VerifyAll();
    }

    [Fact]
    public async Task SystemVolumePercent_WithoutDangerous_ShouldFailBeforeResolvingTool()
    {
        var sp = BuildServiceProvider(_ => { });

        var output = new StringWriter();
        var exit = await CliDispatcher.RunAsync(
            new[] { "sys", "volume", "--percent", "30", "--pretty" },
            sp,
            output);

        Assert.Equal(1, exit);

        using var doc = JsonDocument.Parse(output.ToString());
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("POLICY_DENIED", doc.RootElement.GetProperty("code").GetString());
        Assert.Contains("--dangerous", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task ContractValidate_WithValidYaml_ShouldExposeParsedJson()
    {
        var yaml = """
                   contractVersion: "1.0.0"
                   app:
                     name: "AppA"
                     processNames: ["AppA.exe"]
                   windows:
                     main:
                       titleContains: "AppA"
                   controls:
                     btn_send_http:
                       windowId: "main"
                       uia:
                         automationId: "btnSendHttp"
                         controlType: "Button"
                       ocr:
                         text: "发送"
                         occurrence: 1
                       fallbackCoords:
                         offsetX: 420
                         offsetY: 180
                   assertions:
                     send_http_success:
                       ocrText: "请求成功"
                   """;

        var path = Path.Combine(Path.GetTempPath(), $"ui-contract-{Guid.NewGuid():N}.yml");
        await File.WriteAllTextAsync(path, yaml);

        try
        {
            var sp = BuildServiceProvider(services =>
            {
                services.AddTransient<ContractTool>();
            });

            var output = new StringWriter();
            var exit = await CliDispatcher.RunAsync(
                new[] { "contract", "validate", "--path", path, "--pretty" },
                sp,
                output);

            Assert.Equal(0, exit);

            using var doc = JsonDocument.Parse(output.ToString());
            Assert.Equal("1.0", doc.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal("Windows.Agent.Tools.Contracts.ContractTool.ValidateAsync", doc.RootElement.GetProperty("tool").GetString());

            var parsed = doc.RootElement.GetProperty("result").GetProperty("parsed");
            Assert.True(parsed.GetProperty("success").GetBoolean());
            Assert.Equal("1.0.0", parsed.GetProperty("contractVersion").GetString());
            Assert.Equal("AppA", parsed.GetProperty("app").GetProperty("name").GetString());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task ContractValidate_WithMissingFields_ShouldExposeErrors()
    {
        var yaml = """
                   contractVersion: ""
                   app:
                     name: ""
                   windows: {}
                   controls: {}
                   """;

        var path = Path.Combine(Path.GetTempPath(), $"ui-contract-{Guid.NewGuid():N}.yml");
        await File.WriteAllTextAsync(path, yaml);

        try
        {
            var sp = BuildServiceProvider(services =>
            {
                services.AddTransient<ContractTool>();
            });

            var output = new StringWriter();
            var exit = await CliDispatcher.RunAsync(
                new[] { "contract", "validate", "--path", path, "--pretty" },
                sp,
                output);

            Assert.Equal(1, exit);

            using var doc = JsonDocument.Parse(output.ToString());
            var parsed = doc.RootElement.GetProperty("result").GetProperty("parsed");
            Assert.False(parsed.GetProperty("success").GetBoolean());
            Assert.True(parsed.GetProperty("errors").GetArrayLength() > 0);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task SnapshotOnError_WhenToolFails_ShouldEmitSessionAndArtifacts()
    {
        var yaml = """
                   contractVersion: ""
                   app:
                     name: ""
                   windows: {}
                   controls: {}
                   """;

        var path = Path.Combine(Path.GetTempPath(), $"ui-contract-{Guid.NewGuid():N}.yml");
        await File.WriteAllTextAsync(path, yaml);

        try
        {
            var sp = BuildServiceProvider(services =>
            {
                services.AddTransient<ContractTool>();
            });

            var output = new StringWriter();
            var exit = await CliDispatcher.RunAsync(
                new[] { "contract", "validate", "--path", path, "--snapshot-on-error", "--pretty" },
                sp,
                output);

            Assert.Equal(1, exit);

            using var doc = JsonDocument.Parse(output.ToString());
            Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("session").GetString()));
            Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("artifacts").ValueKind);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task DiagTailLog_ShouldReturnLastLines()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wa-tail-{Guid.NewGuid():N}.log");
        await File.WriteAllLinesAsync(path, Enumerable.Range(1, 10).Select(i => $"line-{i}"));

        try
        {
            var sp = BuildServiceProvider(services =>
            {
                services.AddTransient<TailLogTool>();
            });

            var output = new StringWriter();
            var exit = await CliDispatcher.RunAsync(
                new[] { "diag", "tail-log", "--path", path, "--lines", "3", "--pretty" },
                sp,
                output);

            Assert.Equal(0, exit);

            using var doc = JsonDocument.Parse(output.ToString());
            Assert.Equal("1.0", doc.RootElement.GetProperty("schemaVersion").GetString());
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("Windows.Agent.Tools.Diagnostics.TailLogTool.TailAsync", doc.RootElement.GetProperty("tool").GetString());

            var parsed = doc.RootElement.GetProperty("result").GetProperty("parsed");
            Assert.True(parsed.GetProperty("success").GetBoolean());
            Assert.Equal(3, parsed.GetProperty("lineCount").GetInt32());
            Assert.Equal(new[] { "line-8", "line-9", "line-10" }, parsed.GetProperty("lines").EnumerateArray().Select(e => e.GetString()).ToArray());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static ServiceProvider BuildServiceProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services);
        return services.BuildServiceProvider();
    }
}
