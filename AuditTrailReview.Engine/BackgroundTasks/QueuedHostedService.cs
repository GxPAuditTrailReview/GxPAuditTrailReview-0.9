using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuditTrailReview.Engine.BackgroundTasks
{
    /// <summary>
    /// Drains <see cref="IBackgroundTaskQueue"/> one item at a time for the lifetime of the
    /// application. Each item runs on its own DI scope, created here rather than reused from
    /// wherever the item was queued from -- by the time a queued item runs, the HTTP request
    /// (and its scoped ApplicationDbContext) that queued it is long gone.
    ///
    /// A failing work item is logged and never allowed to take down this service or block
    /// the next item in the queue -- see the try/catch below.
    /// </summary>
    public class QueuedHostedService : BackgroundService
    {
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<QueuedHostedService> _logger;

        public QueuedHostedService(
            IBackgroundTaskQueue taskQueue,
            IServiceProvider serviceProvider,
            ILogger<QueuedHostedService> logger)
        {
            _taskQueue = taskQueue;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Func<IServiceProvider, CancellationToken, Task> workItem;
                try
                {
                    workItem = await _taskQueue.DequeueAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break; // application is shutting down
                }

                try
                {
                    await using var scope = _serviceProvider.CreateAsyncScope();
                    await workItem(scope.ServiceProvider, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception executing a queued background work item.");
                }
            }
        }
    }
}
