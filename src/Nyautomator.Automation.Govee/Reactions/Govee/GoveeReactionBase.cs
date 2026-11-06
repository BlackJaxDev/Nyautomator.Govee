using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nyautomator.Govee;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Shared helpers for Govee automation reactions.
/// </summary>
public abstract class GoveeReactionBase : ReactionType, IAsyncReaction
{
    [OptionsProvider(typeof(GoveeDeviceOptionsProvider))]
    [Description("Default Govee device selection. Stores the raw value in the format 'deviceId|sku|name'.")]
    public string Device { get; set; } = string.Empty;

    [Description("Overrides the target device identifier at runtime. Accepts 'deviceId' or 'deviceId|sku'.")]
    public string? DeviceId { get; set; }

    [Description("Optional SKU/model override when supplying a device identifier dynamically.")]
    public string? DeviceSku { get; set; }

    public sealed override void Execute()
        => ExecuteAsync().GetAwaiter().GetResult();

    public async Task ExecuteAsync()
    {
        if (!(GoveeAutomationHost.IsGoveeConfigured?.Invoke() == true))
        {
            Log("Govee module has no API key configured; skipping reaction.");
            return;
        }

        var cancellationToken = CancellationToken.None;

        var target = await ResolveDeviceAsync(cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            Log("No target device resolved; command not sent.");
            return;
        }

        var (deviceId, sku, device) = target.Value;
        var capability = await BuildCapabilityAsync(deviceId, sku, device, cancellationToken).ConfigureAwait(false);
        if (capability is null)
        {
            Log("Capability builder returned null; command not sent.");
            return;
        }

        var command = new GoveeCommand
        {
            Device = deviceId,
            Sku = sku,
            Capability = capability
        };

        var dispatched = await GoveeAutomationHost.SendGoveeCommand!(command, cancellationToken).ConfigureAwait(false);
        if (!dispatched)
            Log("Failed to dispatch command to the Govee API.");
    }

    protected abstract Task<GoveeCommandCapability?> BuildCapabilityAsync(string deviceId, string sku, GoveeDevice? device, CancellationToken cancellationToken);

    protected Task<(string DeviceId, string Sku, GoveeDevice? Device)?> ResolveDeviceAsync(CancellationToken cancellationToken)
        => GoveeAutomationDeviceHelper.ResolveDeviceAsync(Device, DeviceId, DeviceSku, cancellationToken, Log);

    protected static (string Type, string Instance) ResolveCapabilityInfo(GoveeDevice? device, string defaultType, string defaultInstance)
    {
        var capability = FindCapabilityByInstance(device, defaultInstance)
            ?? device?.Capabilities.FirstOrDefault(c => string.Equals(c.Type, defaultType, StringComparison.OrdinalIgnoreCase));

        if (capability is null)
            return (defaultType, defaultInstance);

        return (capability.Type, capability.Instance);
    }

    protected static GoveeCapability? FindCapabilityByInstance(GoveeDevice? device, string instance)
        => device?.Capabilities.FirstOrDefault(c => string.Equals(c.Instance, instance, StringComparison.OrdinalIgnoreCase));

    protected static (double? Min, double? Max, double? Step) ExtractRange(GoveeCapability? capability)
    {
        var parameters = capability?.Parameters ?? default;
        if (parameters.ValueKind != JsonValueKind.Object)
            return (null, null, null);

        if (!parameters.TryGetProperty("range", out var rangeElement) || rangeElement.ValueKind != JsonValueKind.Object)
            rangeElement = parameters;

        double? min = TryReadNumber(rangeElement, "min");
        double? max = TryReadNumber(rangeElement, "max");
        double? step = TryReadNumber(rangeElement, "step");

        return (min, max, step);
    }

    protected static IReadOnlyList<string> ExtractEnumValues(GoveeCapability? capability, params string[] propertyNames)
    {
        var parameters = capability?.Parameters ?? default;
        if (parameters.ValueKind != JsonValueKind.Object)
            return Array.Empty<string>();

        foreach (var property in propertyNames)
        {
            if (!parameters.TryGetProperty(property, out var valueElement))
                continue;

            if (valueElement.ValueKind != JsonValueKind.Array)
                continue;

            var results = new List<string>();
            foreach (var item in valueElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        results.Add(text.Trim());
                }
                else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("value", out var inner) && inner.ValueKind == JsonValueKind.String)
                {
                    var text = inner.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        results.Add(text.Trim());
                }
            }

            if (results.Count > 0)
                return results;
        }

        return Array.Empty<string>();
    }

    protected static void Log(string message)
        => Console.WriteLine($"[Automation][Govee] {message}");

    private static double? TryReadNumber(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number))
            return number;

        if (property.ValueKind == JsonValueKind.String && double.TryParse(property.GetString(), out var parsed))
            return parsed;

        return null;
    }
}
