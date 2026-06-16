using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

namespace TianShu.VSSDK.VSExtension;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("TianShu VSSDK", "通过 Sidecar 消费 TianShu 执行运行时的最小会话扩展", "0.1.3")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideToolWindow(typeof(TianShuConversationToolWindow))]
[Guid(PackageGuidString)]
[SuppressMessage("Style", "VSTHRD200:Use \"Async\" suffix for async methods", Justification = "VSSDK override signature")]
public sealed class TianShuVssdkPackage : AsyncPackage
{
    public const string PackageGuidString = "1b42b89a-be49-4936-92a2-10e127c6f642";
    private const string DebugRootSuffix = "TianShuExp";

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        Debug.WriteLine("[TianShu VSIX] Package InitializeAsync start.");
        await TianShuConversationToolWindowCommand.InitializeAsync(this).ConfigureAwait(false);
        Debug.WriteLine("[TianShu VSIX] Command registration completed.");

        if (!ShouldAutoShowToolWindow())
        {
            Debug.WriteLine("[TianShu VSIX] Auto show skipped for non-debug root suffix.");
            return;
        }

        _ = JoinableTaskFactory.RunAsync(async delegate
        {
            try
            {
                // 包初始化必须尽快返回，否则实验实例会卡在 Shell 启动阶段。
                await Task.Delay(TimeSpan.FromMilliseconds(800), DisposalToken).ConfigureAwait(false);
                await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                Debug.WriteLine("[TianShu VSIX] Auto show tool window scheduled.");

                var window = await ShowToolWindowAsync(typeof(TianShuConversationToolWindow), 0, true, DisposalToken).ConfigureAwait(true);
                if (window is null)
                {
                    throw new NotSupportedException("无法在调试实例中打开 TianShu 对话工具窗口。 ");
                }

                Debug.WriteLine("[TianShu VSIX] Tool window shown.");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[TianShu VSIX] Auto show canceled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TianShu VSIX] Auto show failed: {ex}");
            }
        });
    }

    private static bool ShouldAutoShowToolWindow()
    {
        var args = Environment.GetCommandLineArgs();
        for (var index = 0; index < args.Length; index++)
        {
            var current = args[index];
            if (string.Equals(current, "/RootSuffix", StringComparison.OrdinalIgnoreCase)
                || string.Equals(current, "-RootSuffix", StringComparison.OrdinalIgnoreCase)
                || string.Equals(current, "/rootsuffix", StringComparison.OrdinalIgnoreCase)
                || string.Equals(current, "-rootsuffix", StringComparison.OrdinalIgnoreCase))
            {
                return index + 1 < args.Length
                    && string.Equals(args[index + 1], DebugRootSuffix, StringComparison.OrdinalIgnoreCase);
            }

            if (current.StartsWith("/RootSuffix:", StringComparison.OrdinalIgnoreCase)
                || current.StartsWith("-RootSuffix:", StringComparison.OrdinalIgnoreCase)
                || current.StartsWith("/rootsuffix:", StringComparison.OrdinalIgnoreCase)
                || current.StartsWith("-rootsuffix:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = current.Split(new[] { ':' }, 2);
                return parts.Length == 2
                    && string.Equals(parts[1], DebugRootSuffix, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }
}
