using System.Threading.Channels;

namespace AuditTrailReview.Engine.BackgroundTasks
{
    /// <summary>
    /// In-process, in-memory implementation of <see cref="IBackgroundTaskQueue"/> backed by
    /// an unbounded <see cref="Channel{T}"/>. Registered as a singleton; queued items are lost
    /// on process shutdown/restart, which is an acceptable trade-off here since a queued item
    /// is always a best-effort supplementary step (AI analysis) attached to a scan that is
    /// already fully and durably saved -- it is never the record of truth itself.
    /// </summary>
    public class BackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _queue =
            Channel.CreateUnbounded<Func<IServiceProvider, CancellationToken, Task>>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        public void QueueBackgroundWorkItem(Func<IServiceProvider, CancellationToken, Task> workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);
            _queue.Writer.TryWrite(workItem);
        }

        public async Task<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
            => await _queue.Reader.ReadAsync(cancellationToken);
    }
}
