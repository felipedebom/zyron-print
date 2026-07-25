using System.Security;
using Zyron.Print.Configuration;
using Zyron.Print.Infrastructure;
using Zyron.Print.Models;
using Zyron.Print.Printing;

namespace Zyron.Print.Services;

public sealed class PrintQueueWorker : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly CredentialStore _credentials;
    private readonly SupabaseDeviceClient _api;
    private readonly RawPrinterService _printer;
    private readonly FileLogger _logger;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public PrintQueueWorker(
        SettingsStore settings,
        CredentialStore credentials,
        SupabaseDeviceClient api,
        RawPrinterService printer,
        FileLogger logger)
    {
        _settings = settings;
        _credentials = credentials;
        _api = api;
        _printer = printer;
        _logger = logger;
    }

    public event EventHandler<WorkerStatus>? StatusChanged;

    public void Start()
    {
        if (_loop is { IsCompleted: false }) return;
        _cancellation = new CancellationTokenSource();
        _loop = RunAsync(_cancellation.Token);
    }

    public async Task StopAsync()
    {
        if (_cancellation is null) return;
        _cancellation.Cancel();
        if (_loop is not null)
        {
            try { await _loop; } catch (OperationCanceledException) { }
        }
        _cancellation.Dispose();
        _cancellation = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var settings = _settings.Current;
            if (_credentials.Load() is null || !_api.IsConfigured)
            {
                Publish(ConnectionState.Disconnected, "Aguardando pareamento");
                await Delay(settings.PollIntervalSeconds, cancellationToken);
                continue;
            }
            if (string.IsNullOrWhiteSpace(settings.PrinterName))
            {
                Publish(ConnectionState.Error, "Selecione uma impressora");
                await Delay(settings.PollIntervalSeconds, cancellationToken);
                continue;
            }

            try
            {
                await _api.HeartbeatAsync(settings.PrinterName, "connected", cancellationToken);
                Publish(ConnectionState.Connected, "Conectado e aguardando pedidos");
                var job = await _api.ClaimNextAsync(cancellationToken);
                if (job is null)
                {
                    await Delay(settings.PollIntervalSeconds, cancellationToken);
                    continue;
                }
                await ProcessAsync(job, settings, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.Error("Falha ao consultar a fila.", exception);
                Publish(ConnectionState.Error, exception.Message);
                await Delay(Math.Max(10, settings.PollIntervalSeconds), cancellationToken);
            }
        }
    }

    private async Task ProcessAsync(PrintJob job, AppSettings settings, CancellationToken cancellationToken)
    {
        var credential = _credentials.Load() ?? throw new InvalidOperationException("Pareamento ausente.");
        if (job.RestaurantId != credential.RestaurantId)
        {
            _logger.Error($"Bloqueado trabalho {job.Id} de outra loja.");
            throw new SecurityException("O servidor retornou um trabalho de outra loja.");
        }

        try
        {
            Publish(ConnectionState.Printing, job.IsReprint ? "Reimprimindo comanda" : "Imprimindo comanda");
            var bytes = EscPosReceiptBuilder.BuildFromPayload(job.ReceiptPayload, job.PaperWidth, job.Cut && settings.CutPaper);
            for (var copy = 1; copy <= Math.Clamp(job.Copies, 1, 5); copy++)
            {
                _printer.Print(settings.PrinterName, bytes, $"ZYRON - {job.Id} - via {copy}");
            }
            await _api.CompleteAsync(job.Id, cancellationToken);
            Publish(ConnectionState.Connected, "Impressão concluída");
        }
        catch (Exception exception)
        {
            _logger.Error($"Falha no trabalho {job.Id}.", exception);
            try
            {
                await _api.FailAsync(job.Id, exception.Message, 30, cancellationToken);
            }
            catch (Exception reportException)
            {
                _logger.Error($"Não foi possível registrar a falha do trabalho {job.Id}.", reportException);
            }
            Publish(ConnectionState.Error, "Impressora indisponível; nova tentativa será feita");
        }
    }

    private void Publish(ConnectionState state, string message) =>
        StatusChanged?.Invoke(this, new WorkerStatus(state, message));

    private static Task Delay(int seconds, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 2, 60)), cancellationToken);

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
    }
}
