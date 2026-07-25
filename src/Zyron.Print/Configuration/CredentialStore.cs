using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zyron.Print.Infrastructure;
using Zyron.Print.Models;

namespace Zyron.Print.Configuration;

public sealed class CredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ZYRON.Print.Device.v1");
    private readonly AppPaths _paths;
    private readonly FileLogger _logger;

    public CredentialStore(AppPaths paths, FileLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public DeviceCredential? Load()
    {
        try
        {
            if (!File.Exists(_paths.CredentialFile))
            {
                return null;
            }
            var encrypted = File.ReadAllBytes(_paths.CredentialFile);
            var clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<DeviceCredential>(clear);
        }
        catch (Exception exception)
        {
            _logger.Error("A credencial local não pôde ser lida.", exception);
            return null;
        }
    }

    public void Save(DeviceCredential credential)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(credential);
        var encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_paths.CredentialFile, encrypted);
        _logger.Info($"Dispositivo {credential.DeviceId} pareado.");
    }

    public void Clear()
    {
        if (File.Exists(_paths.CredentialFile))
        {
            File.Delete(_paths.CredentialFile);
        }
        _logger.Info("Pareamento local removido.");
    }
}
