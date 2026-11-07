namespace Carwash.API.Services;

public sealed class ReceiptPrintWorker(IServiceScopeFactory scopes, ILogger<ReceiptPrintWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var recovered = false;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ImpresionComprobantesService>();
                if (!recovered) { await service.RecoverAsync(stoppingToken); recovered = true; }
                await service.ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("PRINT_WORKER_UNAVAILABLE; revisar estado antes de repetir."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}