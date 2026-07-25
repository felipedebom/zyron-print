using System.Text.Json;

namespace Zyron.Print.Models;

public sealed class PrintJob
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public int PaperWidth { get; set; } = 58;
    public int Copies { get; set; } = 1;
    public bool Cut { get; set; } = true;
    public bool IsReprint { get; set; }
    public JsonElement ReceiptPayload { get; set; }
}

public sealed record PairingResult(
    Guid DeviceId,
    Guid RestaurantId,
    string RestaurantName,
    string AccessToken,
    string RefreshToken,
    int ExpiresIn);

public enum ConnectionState
{
    Disconnected,
    Connected,
    Printing,
    Error
}

public sealed record WorkerStatus(ConnectionState State, string Message);
