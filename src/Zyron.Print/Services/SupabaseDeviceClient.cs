using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zyron.Print.Configuration;
using Zyron.Print.Infrastructure;
using Zyron.Print.Models;

namespace Zyron.Print.Services;

public sealed class SupabaseDeviceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly SettingsStore _settings;
    private readonly CredentialStore _credentials;
    private readonly FileLogger _logger;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public SupabaseDeviceClient(SettingsStore settings, CredentialStore credentials, FileLogger logger)
    {
        _settings = settings;
        _credentials = credentials;
        _logger = logger;
    }

    public bool IsConfigured
    {
        get
        {
            var settings = _settings.Current;
            return Uri.TryCreate(settings.SupabaseUrl, UriKind.Absolute, out _) &&
                   !string.IsNullOrWhiteSpace(settings.SupabaseAnonKey);
        }
    }

    public async Task<PairingResult> PairAsync(string code, string deviceName, CancellationToken cancellationToken)
    {
        var settings = RequireSettings();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{settings.SupabaseUrl.TrimEnd('/')}/functions/v1/zyron-print-pair");
        request.Headers.Add("apikey", settings.SupabaseAnonKey);
        request.Content = JsonContent.Create(new
        {
            code = NormalizePairingCode(code),
            device_name = deviceName,
            platform = "windows",
            app_version = Application.ProductVersion
        });
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ReadError(body, "O código não pôde ser pareado."));
        var result = JsonSerializer.Deserialize<PairingResponse>(body, JsonOptions)
                     ?? throw new InvalidOperationException("Resposta de pareamento inválida.");
        return new PairingResult(result.DeviceId, result.RestaurantId, result.RestaurantName, result.AccessToken);
    }

    public async Task<PrintJob?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        var response = await CallRpcAsync("claim_print_job", new { }, cancellationToken);
        if (response.RootElement.ValueKind == JsonValueKind.Array)
        {
            var first = response.RootElement.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Undefined) return null;
            return ParseJob(first);
        }
        if (response.RootElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        return ParseJob(response.RootElement);
    }

    public async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken) =>
        _ = await CallRpcAsync("complete_print_job", new { p_job_id = jobId }, cancellationToken);

    public async Task FailAsync(Guid jobId, string error, int retrySeconds, CancellationToken cancellationToken) =>
        _ = await CallRpcAsync("fail_print_job", new
        {
            p_job_id = jobId,
            p_error = Limit(error, 500),
            p_retry_seconds = retrySeconds
        }, cancellationToken);

    public async Task HeartbeatAsync(string printerName, string status, CancellationToken cancellationToken) =>
        _ = await CallRpcAsync("heartbeat_print_device", new
        {
            p_printer_name = printerName,
            p_status = status,
            p_app_version = Application.ProductVersion
        }, cancellationToken);

    private async Task<JsonDocument> CallRpcAsync(string function, object payload, CancellationToken cancellationToken)
    {
        var settings = RequireSettings();
        var credential = _credentials.Load() ?? throw new InvalidOperationException("Este computador ainda não está pareado.");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{settings.SupabaseUrl.TrimEnd('/')}/rest/v1/rpc/{function}");
        request.Headers.Add("apikey", settings.SupabaseAnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Content = JsonContent.Create(payload);
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = ReadError(body, $"Falha ao chamar {function}.");
            _logger.Warning(message);
            throw new InvalidOperationException(message);
        }
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "null" : body);
    }

    private AppSettings RequireSettings()
    {
        var settings = _settings.Current;
        if (!IsConfigured)
            throw new InvalidOperationException("Informe a URL e a chave pública (anon) do Supabase.");
        return settings;
    }

    private static PrintJob ParseJob(JsonElement element)
    {
        var payload = element.TryGetProperty("receipt_payload", out var receipt)
            ? receipt.Clone()
            : JsonDocument.Parse("{}").RootElement.Clone();
        return new PrintJob
        {
            Id = element.GetProperty("id").GetGuid(),
            RestaurantId = element.GetProperty("restaurant_id").GetGuid(),
            PaperWidth = element.TryGetProperty("paper_width", out var width) ? width.GetInt32() : 58,
            Copies = element.TryGetProperty("copies", out var copies) ? copies.GetInt32() : 1,
            Cut = !element.TryGetProperty("cut", out var cut) || cut.GetBoolean(),
            IsReprint = element.TryGetProperty("is_reprint", out var reprint) && reprint.GetBoolean(),
            ReceiptPayload = payload
        };
    }

    public static string NormalizePairingCode(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

    private static string ReadError(string body, string fallback)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("message", out var message)) return message.GetString() ?? fallback;
            if (json.RootElement.TryGetProperty("error", out var error)) return error.GetString() ?? fallback;
        }
        catch
        {
            // A resposta pode não ser JSON.
        }
        return fallback;
    }

    private sealed class PairingResponse
    {
        [JsonPropertyName("device_id")] public Guid DeviceId { get; set; }
        [JsonPropertyName("restaurant_id")] public Guid RestaurantId { get; set; }
        [JsonPropertyName("restaurant_name")] public string RestaurantName { get; set; } = "";
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    }
}

