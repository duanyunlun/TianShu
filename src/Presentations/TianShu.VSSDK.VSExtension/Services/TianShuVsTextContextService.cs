using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace TianShu.VSSDK.VSExtension.Services;

internal sealed class TianShuVsTextContextService
{
    public TianShuVsTextContextSnapshot? TryGetActiveTextContext(out string? errorMessage)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        errorMessage = null;
        var textManager = ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager)) as IVsTextManager;
        if (textManager is null)
        {
            errorMessage = "未能获取 VS 文本管理服务。";
            return null;
        }

        if (ErrorHandler.Failed(textManager.GetActiveView(fMustHaveFocus: 1, pBuffer: null, out var activeView)) || activeView is null)
        {
            errorMessage = "当前没有可读取的文本编辑器视图。";
            return null;
        }

        if (activeView is not IVsUserData userData)
        {
            errorMessage = "当前活动窗口不支持文本上下文读取。";
            return null;
        }

        var viewHostGuid = DefGuidList.guidIWpfTextViewHost;
        if (ErrorHandler.Failed(userData.GetData(ref viewHostGuid, out var holder)))
        {
            errorMessage = "当前活动窗口不是 WPF 文本编辑器。";
            return null;
        }

        if (holder is not IWpfTextViewHost viewHost)
        {
            errorMessage = "当前活动窗口不是 WPF 文本编辑器。";
            return null;
        }

        var textView = viewHost.TextView;
        if (!textView.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document))
        {
            errorMessage = "当前文本缓冲区没有关联到物理文件。";
            return null;
        }

        var snapshot = textView.TextSnapshot;
        var selectedText = textView.Selection.IsEmpty
            ? string.Empty
            : textView.Selection.StreamSelectionSpan.GetText();

        return new TianShuVsTextContextSnapshot(
            document.FilePath,
            snapshot.GetText(),
            selectedText);
    }
}

internal sealed class TianShuVsTextContextSnapshot
{
    public TianShuVsTextContextSnapshot(string filePath, string fileText, string selectionText)
    {
        FilePath = filePath;
        FileText = fileText;
        SelectionText = selectionText;
    }

    public string FilePath { get; }

    public string FileText { get; }

    public string SelectionText { get; }

    public bool HasSelection => !string.IsNullOrWhiteSpace(SelectionText);
}
