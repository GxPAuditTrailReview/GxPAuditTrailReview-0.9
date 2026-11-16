namespace AuditTrailReview.Engine.BackgroundTasks
{
    /// <summary>
    /// A queue for work that should run after an HTTP request has already returned a response
    /// to the user, rather than blocking the request itself.
    ///
    /// This exists specifically to decouple the manual-upload AI analysis call from the
    /// core scan-save request (see ADR-0006, NFR-014): a missing/invalid OpenAI key or a
    /// slow OpenAI response must never make an otherwise-successful rule-based scan look
    /// like a failed upload. A queued item runs later, on its own DI scope (see
    /// <see cref="QueuedHostedService"/>), completely isolated from the request that queued it.
    /// </summary>
    public interface IBackgroundTaskQueue
    {
        /// <summary>
        /// Queues a unit of work to run on the background hosted service. The delegate
        /// receives a fresh, work-item-scoped <see cref="IServiceProvider"/> (safe to resolve
        /// scoped services such as ApplicationDbContext from) and a cancellation token tied
        /// to application shutdown.
        /// </summary>
        void QueueBackgroundWorkItem(Func<IServiceProvider, CancellationToken, Task> workItem);

        Task<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);
    }
}
