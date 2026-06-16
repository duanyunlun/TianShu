namespace TianShu.Execution.Runtime.Tests;

/// <summary>
/// 测试用线程安全 <see cref="System.IO.StringWriter"/>。
/// AppHostServer 可能在后台继续写入通知；未同步读取 <c>ToString()</c> 会导致并发访问 StringBuilder 出错。
/// </summary>
public sealed class StringWriter : System.IO.StringWriter
{
    private readonly object gate = new();

    public override void Write(char value)
    {
        lock (gate)
        {
            base.Write(value);
        }
    }

    public override void Write(string? value)
    {
        lock (gate)
        {
            base.Write(value);
        }
    }

    public override void Write(char[] buffer, int index, int count)
    {
        lock (gate)
        {
            base.Write(buffer, index, count);
        }
    }

    public override void WriteLine()
    {
        lock (gate)
        {
            base.WriteLine();
        }
    }

    public override void WriteLine(string? value)
    {
        lock (gate)
        {
            base.WriteLine(value);
        }
    }

    public override Task WriteAsync(char value)
    {
        lock (gate)
        {
            base.Write(value);
        }

        return Task.CompletedTask;
    }

    public override Task WriteAsync(string? value)
    {
        lock (gate)
        {
            base.Write(value);
        }

        return Task.CompletedTask;
    }

    public override Task WriteAsync(char[] buffer, int index, int count)
    {
        lock (gate)
        {
            base.Write(buffer, index, count);
        }

        return Task.CompletedTask;
    }

    public override Task WriteLineAsync(string? value)
    {
        lock (gate)
        {
            base.WriteLine(value);
        }

        return Task.CompletedTask;
    }

    public override Task FlushAsync()
    {
        lock (gate)
        {
            return Task.CompletedTask;
        }
    }

    public override string ToString()
    {
        lock (gate)
        {
            return base.ToString();
        }
    }
}

