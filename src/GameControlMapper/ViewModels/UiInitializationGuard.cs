using Microsoft.Extensions.Logging;

namespace GameControlMapper.ViewModels;

public static class UiInitializationGuard
{
    public static async Task RunAsync(
        Func<Task> initialize,
        ILogger logger,
        Action<Exception> enterRecoverableState)
    {
        ArgumentNullException.ThrowIfNull(initialize);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(enterRecoverableState);

        try
        {
            await initialize().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UI initialization failed");
            enterRecoverableState(ex);
        }
    }
}
