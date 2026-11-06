using System;
using System.ComponentModel;
using System.Linq;
using Nyautomator.Govee;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Automation action that fires when a realtime Govee device event is received.
/// </summary>
public sealed class GoveeDeviceEventAction : ActionType, IExternalPayloadAction
{
    public const string TypeId = "Govee.Device.Event";

    [OptionsProvider(typeof(GoveeDeviceOptionsProvider))]
    [Description("Default device selection captured from the device picker in the format 'deviceId|sku|name'.")]
    public string Device { get; set; } = string.Empty;

    [Description("Optional override for the device identifier. Accepts 'deviceId' or 'deviceId|sku'.")]
    public string? DeviceId { get; set; }

    [Description("Optional SKU/model override used when providing the device identifier dynamically.")]
    public string? DeviceSku { get; set; }

    [Description("Filter to a specific event type reported by the Govee API. Leave blank to accept all events.")]
    public string? EventType { get; set; }

    [Description("Filter events to a specific capability type (for example 'devices.capabilities.switch').")]
    public string? CapabilityType { get; set; }

    [Description("Filter events to a specific capability instance (for example 'brightness' or 'color').")]
    public string? CapabilityInstance { get; set; }

    public override string Id => TypeId;
    public override string DisplayName => "Govee: Device Event";
    public override string Description => "Triggered when the selected Govee device sends an event over the realtime API.";

    public override Type? GetOutputType(string? outputHandle)
        => typeof(GoveeDeviceEvent);

    public bool ShouldHandlePayload(object? payload)
        => payload is GoveeDeviceEvent goveeEvent && Matches(goveeEvent);

    public bool Matches(GoveeDeviceEvent payload)
    {
        if (payload is null)
            return false;

        if (!MatchesDevice(payload))
            return false;

        if (!string.IsNullOrWhiteSpace(EventType) &&
            !string.Equals(EventType.Trim(), payload.EventType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(CapabilityType) && string.IsNullOrWhiteSpace(CapabilityInstance))
            return true;

        if (payload.Capabilities.Count == 0)
            return false;

        return payload.Capabilities.Any(MatchesCapability);
    }

    private bool MatchesDevice(GoveeDeviceEvent payload)
    {
        var configured = GoveeAutomationDeviceHelper.NormalizeDeviceKey(Device);
        var configuredDeviceId = configured?.DeviceId;
        var configuredSku = configured?.Sku;

        var overrideId = GoveeAutomationDeviceHelper.Normalize(DeviceId);
        var overrideSku = GoveeAutomationDeviceHelper.Normalize(DeviceSku);

        var expectedDeviceId = overrideId ?? configuredDeviceId;
        var expectedSku = overrideSku ?? configuredSku;

        if (!string.IsNullOrWhiteSpace(expectedDeviceId) &&
            !string.Equals(expectedDeviceId, payload.Device, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(expectedSku) &&
            !string.Equals(expectedSku, payload.Sku, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private bool MatchesCapability(GoveeCapabilityState capability)
    {
        if (!string.IsNullOrWhiteSpace(CapabilityType) &&
            !string.Equals(CapabilityType, capability.Type, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(CapabilityInstance) &&
            !string.Equals(CapabilityInstance, capability.Instance, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
