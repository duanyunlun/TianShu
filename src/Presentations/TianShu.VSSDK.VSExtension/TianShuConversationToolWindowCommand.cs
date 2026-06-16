using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace TianShu.VSSDK.VSExtension;

internal sealed class TianShuConversationToolWindowCommand
{
    public const int ShowCommandId = 0x0101;
    public const int NewSessionCommandId = 0x0102;
    public const int AddCurrentFileCommandId = 0x0103;
    public const int AddSelectionCommandId = 0x0104;
    public const int AddSpecifiedFileCommandId = 0x0105;
    public const int ReconnectRuntimeCommandId = 0x0106;
    public const int SettingsCommandId = 0x0107;
    public static readonly Guid CommandSet = new Guid("d8c148b7-6cc7-49bc-9e5d-f23a215a8c9d");
    private readonly AsyncPackage package;

    private TianShuConversationToolWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        this.package = package ?? throw new ArgumentNullException(nameof(package));
        RegisterCommand(commandService, ShowCommandId, ExecuteShow);
        RegisterCommand(commandService, NewSessionCommandId, ExecuteNewSession);
        RegisterCommand(commandService, AddCurrentFileCommandId, ExecuteAddCurrentFile);
        RegisterCommand(commandService, AddSelectionCommandId, ExecuteAddSelection);
        RegisterCommand(commandService, AddSpecifiedFileCommandId, ExecuteAddSpecifiedFile);
        RegisterCommand(commandService, ReconnectRuntimeCommandId, ExecuteReconnectRuntime);
        RegisterCommand(commandService, SettingsCommandId, ExecuteShowSettings);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)).ConfigureAwait(true) as OleMenuCommandService;
        if (commandService is null)
        {
            throw new InvalidOperationException("无法获取 OleMenuCommandService。 ");
        }

        _ = new TianShuConversationToolWindowCommand(package, commandService);
    }

    private static void RegisterCommand(OleMenuCommandService commandService, int commandId, EventHandler handler)
    {
        var menuCommandId = new CommandID(CommandSet, commandId);
        var menuItem = new MenuCommand(handler, menuCommandId);
        commandService.AddCommand(menuItem);
    }

    private void ExecuteShow(object sender, EventArgs e)
    {
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            var control = await ShowToolWindowAndGetControlAsync().ConfigureAwait(true);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            control.FocusComposer();
        });
    }

    private void ExecuteNewSession(object sender, EventArgs e)
    {
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            var control = await ShowToolWindowAndGetControlAsync().ConfigureAwait(true);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            await control.StartNewSessionFromCommandAsync().ConfigureAwait(true);
        });
    }

    private void ExecuteAddCurrentFile(object sender, EventArgs e)
    {
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            var control = await ShowToolWindowAndGetControlAsync().ConfigureAwait(true);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            await control.AddCurrentFileContextFromCommandAsync().ConfigureAwait(true);
        });
    }

    private void ExecuteAddSelection(object sender, EventArgs e)
    {
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            var control = await ShowToolWindowAndGetControlAsync().ConfigureAwait(true);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            await control.AddSelectionContextFromCommandAsync().ConfigureAwait(true);
        });
    }

    private void ExecuteAddSpecifiedFile(object sender, EventArgs e)
    {
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            var control = await ShowToolWindowAndGetControlAsync().ConfigureAwait(true);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            await control.AddSpecifiedFileContextFromCommandAsync().ConfigureAwait(true);
        });
    }

    private void ExecuteReconnectRuntime(object sender, EventArgs e)
    {
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            var control = await ShowToolWindowAndGetControlAsync().ConfigureAwait(true);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            await control.ReconnectRuntimeFromCommandAsync().ConfigureAwait(true);
        });
    }

    private void ExecuteShowSettings(object sender, EventArgs e)
    {
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async delegate
        {
            var control = await ShowToolWindowAndGetControlAsync().ConfigureAwait(true);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            await control.ShowSettingsFromCommandAsync().ConfigureAwait(true);
        });
    }

    private async Task<TianShuConversationToolWindowControl> ShowToolWindowAndGetControlAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var window = await package.ShowToolWindowAsync(typeof(TianShuConversationToolWindow), 0, true, package.DisposalToken).ConfigureAwait(true);
        if (window?.Frame is not IVsWindowFrame frame)
        {
            throw new NotSupportedException("无法创建 TianShu 对话工具窗口。 ");
        }

        ErrorHandler.ThrowOnFailure(frame.Show());
        if (window is not TianShuConversationToolWindow conversationWindow)
        {
            throw new NotSupportedException("无法获取 TianShu 对话窗口实例。 ");
        }

        return conversationWindow.ConversationControl;
    }
}
