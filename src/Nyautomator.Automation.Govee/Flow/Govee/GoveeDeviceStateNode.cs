using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using Nyautomator.Govee;

namespace NyautomatorUI.Server.Automation.Flow;

public sealed class GoveeDeviceStateNode : FlowNodeType
{
    public override string Id => "Govee.Device.State";
    public override string DisplayName => "Govee: Fetch Device State";
    public override string Description => "Retrieves the latest state for a Govee device via the Open API and exposes the result as outputs.";

    [OptionsProvider(typeof(GoveeDeviceOptionsProvider))]
    [Description("Default Govee device selection. Stores the raw value in the format 'deviceId|sku|name'.")]
    public string Device { get; set; } = string.Empty;

    [Description("Overrides the target device identifier at runtime. Accepts 'deviceId' or 'deviceId|sku'.")]
    public string? DeviceId { get; set; }

    [Description("Optional SKU/model override when supplying a device identifier dynamically.")]
    public string? DeviceSku { get; set; }

    public override string? Execute()
    {
        Outputs.Clear();

        if (!(GoveeAutomationHost.IsGoveeConfigured?.Invoke() == true))
        {
            Log("Govee module has no API key configured; state retrieval skipped.");
            SetOutput("Status", null);
            return null;
        }

        var cancellationToken = CancellationToken.None;
        var target = GoveeAutomationDeviceHelper
            .ResolveDeviceAsync(Device, DeviceId, DeviceSku, cancellationToken, Log)
            .GetAwaiter()
            .GetResult();

        if (target is null)
        {
            SetOutput("Status", null);
            SetOutput("Values", new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
            SetOutput("TimestampUtc", DateTimeOffset.UtcNow);
            return null;
        }

        var (deviceId, sku, device) = target.Value;

        GoveeDeviceStatus? status = null;
        try
        {
            status = GoveeAutomationHost.GetGoveeDeviceStatus!(deviceId, sku, cancellationToken)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            Log($"Failed to retrieve state for '{deviceId}': {ex.Message}");
        }

        if (status is null)
        {
            SetOutput("DeviceId", deviceId);
            SetOutput("Sku", sku);
            SetOutput("DeviceName", device?.DeviceName ?? deviceId);
            SetOutput("Capabilities", Array.Empty<GoveeCapabilityState>());
            SetOutput("Values", new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
            SetOutput("Status", null);
            SetOutput("TimestampUtc", DateTimeOffset.UtcNow);
            return null;
        }

        if (string.IsNullOrWhiteSpace(status.Device))
            status.Device = deviceId;
        if (string.IsNullOrWhiteSpace(status.Sku))
            status.Sku = sku;
        if (string.IsNullOrWhiteSpace(status.DeviceName) && !string.IsNullOrWhiteSpace(device?.DeviceName))
            status.DeviceName = device!.DeviceName;

        var values = BuildStateMap(status);

        SetOutput("Status", status);
        SetOutput("DeviceId", status.Device);
        SetOutput("Sku", status.Sku);
        SetOutput("DeviceName", status.DeviceName ?? device?.DeviceName ?? status.Device);
        SetOutput("Capabilities", status.Capabilities);
        SetOutput("Values", values);
        SetOutput("TimestampUtc", status.RetrievedAtUtc);

        return null;
    }

    public override Type? GetOutputType(string? outputHandle)
    {
        var handle = AutomationValueHelper.NormalizeValueHandle(outputHandle);
        if (string.IsNullOrWhiteSpace(handle))
            return base.GetOutputType(outputHandle);

        return handle.ToLowerInvariant() switch
        {
            "status" => typeof(GoveeDeviceStatus),
            "capabilities" => typeof(List<GoveeCapabilityState>),
            "values" => typeof(Dictionary<string, object?>),
            "deviceid" => typeof(string),
            "sku" => typeof(string),
            "devicename" => typeof(string),
            "timestamputc" => typeof(DateTimeOffset),
            _ => base.GetOutputType(outputHandle)
        };
    }

    private static Dictionary<string, object?> BuildStateMap(GoveeDeviceStatus status)
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var capability in status.Capabilities)
        {
            var value = ResolveCapabilityValue(capability);

            if (!string.IsNullOrWhiteSpace(capability.Instance))
                map[capability.Instance] = value;

            if (!string.IsNullOrWhiteSpace(capability.Type))
            {
                if (!map.ContainsKey(capability.Type))
                    map[capability.Type] = value;

                if (!string.IsNullOrWhiteSpace(capability.Instance))
                    map[$"{capability.Type}.{capability.Instance}"] = value;
            }
        }

        return map;
    }

    private static object? ResolveCapabilityValue(GoveeCapabilityState capability)
    {
        if (capability.Value is not null)
            return capability.Value;

        if (capability.State.ValueKind == JsonValueKind.Undefined)
            return null;

        return ConvertJsonElement(capability.State);
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var i64))
                    return i64;
                if (element.TryGetUInt64(out var u64))
                    return u64;
                if (element.TryGetDouble(out var dbl))
                    return dbl;
                return element.GetDecimal();
            case JsonValueKind.Array:
            {
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                    list.Add(ConvertJsonElement(item));
                return list;
            }
            case JsonValueKind.Object:
            {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                    dict[property.Name] = ConvertJsonElement(property.Value);
                return dict;
            }
            default:
                return element.GetRawText();
        }
    }

    private static void Log(string message)
        => Console.WriteLine($"[Automation][Govee] {message}");
}
