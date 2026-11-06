using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Nyautomator.Govee;

/// <summary>
/// Represents a physical Govee device returned by the Open API.
/// </summary>
public sealed class GoveeDevice
{
    public string Sku { get; set; } = string.Empty;
    public string Device { get; set; } = string.Empty;
    
    /// <summary>
    /// The type of device (e.g. devices.types.light, devices.types.socket).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    private string? _deviceName;

    public string? DeviceName
    {
        get => _deviceName;
        set => _deviceName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public List<GoveeCapability> Capabilities { get; set; } = [];

    /// <summary>
    /// Indicates whether the device reports support for LAN control / same-network control.
    /// </summary>
    public bool SupportsLanControl { get; set; }

    /// <summary>
    /// Most recently reported Wi-Fi SSID for the device (when available).
    /// </summary>
    public string? WifiSsid { get; set; }

    /// <summary>
    /// Reported Wi-Fi BSSID / MAC, when included in API responses.
    /// </summary>
    public string? WifiBssid { get; set; }

    /// <summary>
    /// Local IP address provided by Govee when the device is on the same network.
    /// </summary>
    public string? LocalIpAddress { get; set; }

    /// <summary>
    /// Optional Wi-Fi RSSI or signal strength indicator (higher is stronger), when reported.
    /// </summary>
    public int? WifiSignalStrength { get; set; }

    /// <summary>
    /// Optional firmware version or build identifier.
    /// </summary>
    public string? FirmwareVersion { get; set; }

    /// <summary>
    /// True when the device surfaced LAN metadata indicating it shares the current Wi-Fi network.
    /// </summary>
    public bool IsOnSameNetwork => !string.IsNullOrWhiteSpace(LocalIpAddress);
}

/// <summary>
/// Describes a supported capability on a Govee device (power, brightness, etc.).
/// </summary>
public sealed class GoveeCapability
{
    public string Type { get; set; } = string.Empty;
    public string Instance { get; set; } = string.Empty;
    public JsonElement Parameters { get; set; }
}

/// <summary>
/// Command payload sent to the Govee Open API.
/// </summary>
public sealed class GoveeCommand
{
    public required string Device { get; init; }
    public required string Sku { get; init; }
    public required GoveeCommandCapability Capability { get; init; }
}

/// <summary>
/// Capability portion of a Govee command.
/// </summary>
public sealed class GoveeCommandCapability
{
    public required string Type { get; init; }
    public required string Instance { get; init; }
    public object? Value { get; init; }
    public object? Parameters { get; init; }
}

/// <summary>
/// Real-time status payload for a specific Govee device.
/// </summary>
public sealed class GoveeDeviceStatus
{
    public string Device { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;

    private string? _deviceName;

    public string? DeviceName
    {
        get => _deviceName;
        set => _deviceName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public DateTimeOffset RetrievedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<GoveeCapabilityState> Capabilities { get; } = new();
}

/// <summary>
/// Represents the current state for a specific capability on a device.
/// </summary>
public sealed class GoveeCapabilityState
{
    public string Type { get; set; } = string.Empty;
    public string Instance { get; set; } = string.Empty;
    public JsonElement State { get; set; }
    public object? Value { get; set; }
}

/// <summary>
/// Real-time event payload emitted from the Govee Open API event stream.
/// </summary>
public sealed class GoveeDeviceEvent
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Device { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    private string? _deviceName;

    public string? DeviceName
    {
        get => _deviceName;
        set => _deviceName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<GoveeCapabilityState> Capabilities { get; } = new();
    public JsonElement Raw { get; set; } = default;
}

/// <summary>
/// Exception raised when Govee API operations fail.
/// </summary>
public sealed class GoveeApiException : Exception
{
    public GoveeApiException(string message)
        : base(message)
    {
    }

    public GoveeApiException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
