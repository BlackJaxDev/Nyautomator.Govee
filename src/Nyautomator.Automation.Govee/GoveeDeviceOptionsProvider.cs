using System;
using System.Collections.Generic;
using System.Linq;
using Nyautomator.Govee;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Supplies the list of configured Govee devices for automation parameter selection.
/// </summary>
public sealed class GoveeDeviceOptionsProvider : IStringOptionsProvider
{
    public IEnumerable<string> GetOptions()
    {
        try
        {
            var getDevices = GoveeAutomationHost.GetGoveeDevices;
            if (getDevices is null)
                return Array.Empty<string>();

            var devices = getDevices(false, CancellationToken.None).GetAwaiter().GetResult();
            if (devices is null || devices.Count == 0)
                return Array.Empty<string>();

            return devices
                .OrderBy(d => d.DeviceName ?? d.Sku ?? d.Device, StringComparer.OrdinalIgnoreCase)
                .Select(FormatOption)
                .ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Automation][Govee] Failed to enumerate devices: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static string FormatOption(GoveeDevice device)
    {
        var name = string.IsNullOrWhiteSpace(device.DeviceName) ? "(Unnamed)" : device.DeviceName!.Trim();
        return $"{device.Device}|{device.Sku}|{name}";
    }
}
