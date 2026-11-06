using Nyautomator;
using Nyautomator.Govee;
using Nyautomator.Runtime.Abstractions;

namespace Nyautomator.Runtime.Integrations.Govee;

public interface IGoveeIntegration : IRuntimeParticipant
{
    bool IsConfigured { get; }
    Task ConfigureAsync(string? apiKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<GoveeDevice>> GetDevicesAsync(bool forceRefresh, CancellationToken cancellationToken);
    Task<bool> SendCommandAsync(GoveeCommand command, CancellationToken cancellationToken);
    Task<GoveeDeviceStatus?> GetDeviceStatusAsync(string deviceId, string sku, CancellationToken cancellationToken);
    IReadOnlyList<GoveeDevice> GetCachedDevices();
    event EventHandler<GoveeDeviceEvent>? DeviceEventReceived;
}
