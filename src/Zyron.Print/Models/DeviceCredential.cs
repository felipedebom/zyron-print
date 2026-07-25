namespace Zyron.Print.Models;

public sealed class DeviceCredential
{
    public Guid DeviceId { get; set; }
    public Guid RestaurantId { get; set; }
    public string RestaurantName { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public DateTimeOffset PairedAt { get; set; }
}

