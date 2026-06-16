using Xunit;

namespace Windows.Agent.Test.Desktop;

/// <summary>
/// Desktop 类测试会真实注入鼠标/键盘事件并抢夺焦点，禁止并行执行，避免互相干扰。
/// </summary>
[CollectionDefinition("Desktop", DisableParallelization = true)]
public sealed class DesktopCollection;

