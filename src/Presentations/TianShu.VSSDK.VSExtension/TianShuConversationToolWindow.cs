using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace TianShu.VSSDK.VSExtension;

[Guid("a9def99e-b792-4819-a14e-fcde7f24d32b")]
public sealed class TianShuConversationToolWindow : ToolWindowPane
{
    public TianShuConversationToolWindowControl ConversationControl { get; }

    public TianShuConversationToolWindow() : base(null)
    {
        Caption = "TianShu 对话";
        ConversationControl = new TianShuConversationToolWindowControl();
        Content = ConversationControl;
    }
}
