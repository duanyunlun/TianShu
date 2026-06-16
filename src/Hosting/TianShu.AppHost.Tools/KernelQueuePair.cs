using System.Threading.Channels;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 参考 TianShu 内核的 SQ/EQ（Submission Queue / Event Queue）模式：通过两个有界队列解耦“提交请求”和“事件输出”。
/// </summary>
internal sealed class KernelQueuePair<TSubmission, TEvent>
{
    public const int DefaultCapacity = 128;

    private readonly Channel<TSubmission> submissions;
    private readonly Channel<TEvent> events;

    public KernelQueuePair(int capacity = DefaultCapacity)
    {
        capacity = Math.Clamp(capacity, 1, 1024);

        submissions = Channel.CreateBounded<TSubmission>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        events = Channel.CreateBounded<TEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    public ValueTask EnqueueSubmissionAsync(TSubmission submission, CancellationToken cancellationToken)
        => submissions.Writer.WriteAsync(submission, cancellationToken);

    public bool TryEnqueueSubmission(TSubmission submission)
        => submissions.Writer.TryWrite(submission);

    public IAsyncEnumerable<TSubmission> ReadSubmissionsAsync(CancellationToken cancellationToken)
        => submissions.Reader.ReadAllAsync(cancellationToken);

    public ValueTask PublishEventAsync(TEvent @event, CancellationToken cancellationToken)
        => events.Writer.WriteAsync(@event, cancellationToken);

    public bool TryPublishEvent(TEvent @event)
        => events.Writer.TryWrite(@event);

    public IAsyncEnumerable<TEvent> ReadEventsAsync(CancellationToken cancellationToken)
        => events.Reader.ReadAllAsync(cancellationToken);

    public void CompleteSubmissions(Exception? error = null)
        => submissions.Writer.TryComplete(error);

    public void CompleteEvents(Exception? error = null)
        => events.Writer.TryComplete(error);

    public void Complete(Exception? error = null)
    {
        CompleteSubmissions(error);
        CompleteEvents(error);
    }
}
