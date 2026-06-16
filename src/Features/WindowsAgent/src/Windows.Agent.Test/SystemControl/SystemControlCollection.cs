using Xunit;

namespace Windows.Agent.Test.SystemControl;

/// <summary>
/// SystemControl 类测试会修改系统状态（音量/亮度/分辨率），禁止并行执行，避免互相干扰。
/// </summary>
[CollectionDefinition("SystemControl", DisableParallelization = true)]
public sealed class SystemControlCollection;

