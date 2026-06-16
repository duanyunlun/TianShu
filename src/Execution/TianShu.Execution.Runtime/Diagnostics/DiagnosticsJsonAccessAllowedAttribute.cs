namespace TianShu.Execution.Runtime.Diagnostics;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor, Inherited = false)]
public sealed class DiagnosticsJsonAccessAllowedAttribute : Attribute
{
}
