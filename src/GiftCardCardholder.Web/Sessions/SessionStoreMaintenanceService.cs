namespace GiftCardCardholder.Web.Sessions;

/// <summary>
/// Creates the session tables on startup and periodically removes rows that can
/// no longer be used. Expired sessions hold encrypted tokens the backend has
/// already invalidated, so deleting them removes useless material rather than
/// business history.
/// </summary>
internal sealed partial class SessionStoreMaintenanceService(
    ICardholderSessionStore store,
    TimeProvider timeProvider,
    ILogger<SessionStoreMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(30);

    [LoggerMessage(
        EventId = 3100,
        Level = LogLevel.Debug,
        Message = "Removed {Count} expired cardholder session or activation rows.")]
    private static partial void LogSwept(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3101,
        Level = LogLevel.Warning,
        Message = "Expired-row sweep failed; it will run again on the next interval.")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await store.InitializeAsync(stoppingToken);

        using var timer = new PeriodicTimer(SweepInterval);
        do
        {
            try
            {
                var removed = await store.DeleteExpiredAsync(
                    timeProvider.GetUtcNow(),
                    stoppingToken);
                LogSwept(logger, removed);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogSweepFailed(logger, exception);
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(
        PeriodicTimer timer,
        CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
