using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nyautomator.Govee;

namespace NyautomatorUI.Server.Automation;

internal static class GoveeAutomationDeviceHelper
{
    public static async Task<(string DeviceId, string Sku, GoveeDevice? Device)?> ResolveDeviceAsync(
        string? configuredDevice,
        string? overrideDeviceId,
        string? overrideSku,
        CancellationToken cancellationToken,
        Action<string>? logger = null,
        bool forceRefresh = false)
    {
        var primaryKey = NormalizeDeviceKey(overrideDeviceId) ?? NormalizeDeviceKey(configuredDevice);
        if (primaryKey is null)
            return null;

        var (deviceId, parsedSku) = primaryKey.Value;
        var sku = Normalize(overrideSku) ?? parsedSku;
        GoveeDevice? device = null;

        try
        {
            var getDevices = GoveeAutomationHost.GetGoveeDevices
                ?? throw new InvalidOperationException("Govee module is not loaded.");
            var devices = await getDevices(forceRefresh || sku is null, cancellationToken)
                .ConfigureAwait(false);

            device = devices.FirstOrDefault(d => string.Equals(d.Device, deviceId, StringComparison.OrdinalIgnoreCase));
            if (device is not null)
                sku ??= Normalize(device.Sku);
        }
        catch (Exception ex)
        {
            logger?.Invoke($"Failed to refresh device list: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(sku))
        {
            logger?.Invoke("Unable to determine SKU/model for the selected device.");
            return null;
        }

        return (deviceId, sku!, device);
    }

    public static (string DeviceId, string? Sku)? NormalizeDeviceKey(string? raw)
    {
        var value = Normalize(raw);
        if (value is null)
            return null;

        var parts = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;

        var deviceId = parts[0];
        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        string? sku = null;
        if (parts.Length > 1)
        {
            sku = parts[1];
            if (string.IsNullOrWhiteSpace(sku))
                sku = null;
        }

        return (deviceId.Trim(), sku?.Trim());
    }

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim();
    }
}