using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Windows.Agent.Interface;
using Windows.Agent.Services;
using Windows.Agent.Uia;

namespace Windows.Agent.Test.Desktop
{
    /// <summary>
    /// UIA 工具链路测试（需要真实桌面环境，默认不建议全量跑）。
    /// </summary>
    [Trait("Category", "Desktop")]
    [Collection("Desktop")]
    public class UiaToolTest
    {
        [Fact]
        public async Task Uia_SetValue_And_Invoke_ShouldWriteExpectedOutput()
        {
            var outPath = Path.Combine(Path.GetTempPath(), $"wa-uia-{Guid.NewGuid():N}.txt");
            var windowTitle = "Windows.Agent UIA Test App";
            var windowTitleRegex = Regex.Escape(windowTitle);

            Process? app = null;
            try
            {
                var appPath = ResolveTestAppPath();
                app = Process.Start(new ProcessStartInfo
                {
                    FileName = appPath,
                    Arguments = $"--out \"{outPath}\"",
                    UseShellExecute = true
                });

                Assert.NotNull(app);
                await WaitForFileAsync(() => app!.MainWindowHandle != IntPtr.Zero, TimeSpan.FromSeconds(10));

                var services = new ServiceCollection();
                services.AddLogging(builder => builder.AddConsole());
                services.AddSingleton<IUiaService, UiaService>();
                var sp = services.BuildServiceProvider();
                var uia = sp.GetRequiredService<IUiaService>();

                var setValueRaw = await uia.SetValueAsync(
                    windowTitleRegex,
                    new UiaSelector(AutomationId: "txtInput", ControlType: "Edit"),
                    "hello-uia");
                AssertSuccess(setValueRaw);

                var invokeRaw = await uia.InvokeAsync(
                    windowTitleRegex,
                    new UiaSelector(AutomationId: "btnInvoke", ControlType: "Button"));
                AssertSuccess(invokeRaw);

                await WaitForFileAsync(() => File.Exists(outPath), TimeSpan.FromSeconds(5));
                var content = await File.ReadAllTextAsync(outPath);
                Assert.Equal("hello-uia", content);
            }
            finally
            {
                if (app != null)
                {
                    try
                    {
                        if (!app.HasExited)
                        {
                            app.Kill();
                            app.WaitForExit(3000);
                        }
                    }
                    catch
                    {
                        // best effort
                    }
                }

                if (File.Exists(outPath))
                {
                    File.Delete(outPath);
                }
            }
        }

        [Fact]
        public async Task Uia_SetValue_And_Invoke_WithBackendUia2_ShouldWriteExpectedOutput()
        {
            var outPath = Path.Combine(Path.GetTempPath(), $"wa-uia2-{Guid.NewGuid():N}.txt");
            var windowTitle = "Windows.Agent UIA Test App";
            var windowTitleRegex = Regex.Escape(windowTitle);

            Process? app = null;
            try
            {
                var appPath = ResolveTestAppPath();
                app = Process.Start(new ProcessStartInfo
                {
                    FileName = appPath,
                    Arguments = $"--out \"{outPath}\"",
                    UseShellExecute = true
                });

                Assert.NotNull(app);
                await WaitForFileAsync(() => app!.MainWindowHandle != IntPtr.Zero, TimeSpan.FromSeconds(10));

                var services = new ServiceCollection();
                services.AddLogging(builder => builder.AddConsole());
                services.AddSingleton<IUiaService, UiaService>();
                var sp = services.BuildServiceProvider();
                var uia = sp.GetRequiredService<IUiaService>();

                var backend = "uia2";
                var setValueRaw = await uia.SetValueAsync(
                    windowTitleRegex,
                    new UiaSelector(AutomationId: "txtInput", ControlType: "Edit"),
                    "hello-uia2",
                    backend);
                AssertSuccess(setValueRaw);

                var invokeRaw = await uia.InvokeAsync(
                    windowTitleRegex,
                    new UiaSelector(AutomationId: "btnInvoke", ControlType: "Button"),
                    backend);
                AssertSuccess(invokeRaw);

                await WaitForFileAsync(() => File.Exists(outPath), TimeSpan.FromSeconds(5));
                var content = await File.ReadAllTextAsync(outPath);
                Assert.Equal("hello-uia2", content);
            }
            finally
            {
                if (app != null)
                {
                    try
                    {
                        if (!app.HasExited)
                        {
                            app.Kill();
                            app.WaitForExit(3000);
                        }
                    }
                    catch
                    {
                        // best effort
                    }
                }

                if (File.Exists(outPath))
                {
                    File.Delete(outPath);
                }
            }
        }

        private static void AssertSuccess(string raw)
        {
            using var doc = JsonDocument.Parse(raw);
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        }

        private static string ResolveTestAppPath()
        {
            var baseDir = AppContext.BaseDirectory;
            var config = baseDir.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                ? "Debug"
                : "Release";

            var dir = new DirectoryInfo(baseDir);
            while (dir != null)
            {
                var sln = Path.Combine(dir.FullName, "TianShu.sln");
                if (File.Exists(sln))
                {
                    var candidate = Path.Combine(
                        dir.FullName,
                        "src",
                        "Features",
                        "WindowsAgent",
                        "src",
                        "Windows.Agent.UiaTestApp",
                        "bin",
                        config,
                        "net10.0-windows",
                        "Windows.Agent.UiaTestApp.exe");

                    if (!File.Exists(candidate))
                    {
                        throw new FileNotFoundException($"Windows.Agent.UiaTestApp.exe not found: {candidate}");
                    }

                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException("TianShu.sln not found above test output directory");
        }

        private static async Task WaitForFileAsync(Func<bool> condition, TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(200);
            }

            throw new TimeoutException("Timed out waiting for condition");
        }
    }
}

