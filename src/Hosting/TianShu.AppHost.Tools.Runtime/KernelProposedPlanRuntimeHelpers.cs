using System.Text;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelProposedPlanSegment(bool IsPlan, string Text);

internal sealed record KernelProposedPlanExtraction(
    string VisibleText,
    string PlanText,
    bool HasPlan);

/// <summary>
/// `<proposed_plan>` 流式解析器，负责把可见文本和计划正文拆成两条增量流。
/// Streaming `<proposed_plan>` parser that splits visible assistant text from plan body deltas.
/// </summary>
internal sealed class KernelProposedPlanStreamParser
{
    private const string StartTag = "<proposed_plan>";
    private const string EndTag = "</proposed_plan>";

    private readonly StringBuilder buffer = new();
    private readonly StringBuilder visibleText = new();
    private readonly StringBuilder planText = new();
    private bool insidePlan;

    public bool HasPlan { get; private set; }

    public IReadOnlyList<KernelProposedPlanSegment> Append(string? delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return Array.Empty<KernelProposedPlanSegment>();
        }

        buffer.Append(delta);
        return Process(flushAll: false);
    }

    public IReadOnlyList<KernelProposedPlanSegment> Flush()
        => Process(flushAll: true);

    public KernelProposedPlanExtraction Complete()
    {
        _ = Flush();
        return new KernelProposedPlanExtraction(
            visibleText.ToString(),
            planText.ToString(),
            HasPlan);
    }

    private IReadOnlyList<KernelProposedPlanSegment> Process(bool flushAll)
    {
        List<KernelProposedPlanSegment>? segments = null;

        while (buffer.Length > 0)
        {
            var marker = insidePlan ? EndTag : StartTag;
            var markerIndex = buffer.ToString().IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                if (markerIndex > 0)
                {
                    AppendSegment(buffer.ToString(0, markerIndex), insidePlan, ref segments);
                }

                buffer.Remove(0, markerIndex + marker.Length);
                if (!insidePlan)
                {
                    HasPlan = true;
                }

                insidePlan = !insidePlan;
                continue;
            }

            var retainLength = flushAll ? 0 : marker.Length - 1;
            if (buffer.Length <= retainLength)
            {
                break;
            }

            var emitLength = buffer.Length - retainLength;
            AppendSegment(buffer.ToString(0, emitLength), insidePlan, ref segments);
            buffer.Remove(0, emitLength);
        }

        if (segments is not null)
        {
            return segments;
        }

        return Array.Empty<KernelProposedPlanSegment>();
    }

    private void AppendSegment(string text, bool isPlan, ref List<KernelProposedPlanSegment>? segments)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (isPlan)
        {
            planText.Append(text);
        }
        else
        {
            visibleText.Append(text);
        }

        segments ??= new List<KernelProposedPlanSegment>();
        segments.Add(new KernelProposedPlanSegment(isPlan, text));
    }
}
