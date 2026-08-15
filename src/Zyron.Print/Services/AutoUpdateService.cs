using Velopack;
using Velopack.Sources;
using Zyron.Print.Infrastructure;

namespace Zyron.Print.Services;

public sealed class AutoUpdateService
{
    private const string ReleasesUrl = "https://github.com/felipedebom/zyron-print";
    private readonly FileLogger _logger;

    public AutoUpdateService(FileLogger logger)
    {
        _logger = logger;
    }

    public async Task<bool> InstallAvailableUpdateAsync()
    {
        try
        {
            var source = new GithubSource(ReleasesUrl, accessToken: null, prerelease: false);
            var manager = new UpdateManager(source);
            if (!manager.IsInstalled)
            {
                _logger.Info("Atualizacao automatica ignorada: aplicativo executado fora do instalador Velopack.");
                return false;
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                return false;
            }

            _logger.Info($"Nova versao encontrada: {update.TargetFullRelease.Version}. Iniciando download.");
            await manager.DownloadUpdatesAsync(update);
            _logger.Info("Atualizacao baixada. Reiniciando o ZYRON Print para concluir a instalacao.");
            manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error("Nao foi possivel verificar ou instalar atualizacoes. O aplicativo continuara normalmente.", exception);
            return false;
        }
    }
}
