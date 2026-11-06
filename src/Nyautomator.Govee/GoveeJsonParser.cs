using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Nyautomator.Govee;

internal static class GoveeJsonParser
{
    public static void PopulateCapabilities(ICollection<GoveeCapabilityState> target, JsonElement container)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));

        if (container.ValueKind != JsonValueKind.Array)
            return;

        foreach (var element in container.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                continue;

            var type = TryGetString(element, "type") ?? string.Empty;
            var instance = TryGetString(element, "instance") ?? TryGetString(element, "name") ?? string.Empty;
            var stateElement = ResolveStateElement(element);

            var capability = new GoveeCapabilityState
            {
                Type = type,
                Instance = instance,
                State = stateElement.ValueKind == JsonValueKind.Undefined ? default : stateElement.Clone(),
                Value = ExtractStateValue(stateElement)
            };

            target.Add(capability);
        }
    }

    public static JsonElement ResolveStateElement(JsonElement element)
    {
        if (element.TryGetProperty("state", out var stateElement))
            return stateElement;

        if (element.TryGetProperty("value", out var valueElement))
            return valueElement;

        if (element.TryGetProperty("properties", out var propertiesElement))
            return propertiesElement;

        if (element.TryGetProperty("values", out var valuesElement))
            return valuesElement;

        return default;
    }

    public static object? ExtractStateValue(JsonElement stateElement)
    {
        if (stateElement.ValueKind == JsonValueKind.Undefined || stateElement.ValueKind == JsonValueKind.Null)
            return null;

        if (stateElement.ValueKind == JsonValueKind.Object)
        {
            if (stateElement.TryGetProperty("value", out var valueProperty))
                return ConvertJsonElement(valueProperty);

            if (stateElement.TryGetProperty("status", out var statusProperty))
                return ConvertJsonElement(statusProperty);

            if (stateElement.TryGetProperty("powerState", out var powerProperty))
                return ConvertJsonElement(powerProperty);

            if (stateElement.TryGetProperty("state", out var innerState))
                return ConvertJsonElement(innerState);
        }

        return ConvertJsonElement(stateElement);
    }

    public static object? ConvertJsonElement(JsonElement element)
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

    public static GoveeDeviceEvent? ParseDeviceEvent(JsonElement root)
    {
        var payload = ResolvePayload(root);

        var evt = new GoveeDeviceEvent
        {
            EventId = TryGetString(root, "eventId") ?? TryGetString(root, "id") ?? Guid.NewGuid().ToString("N"),
            EventType = TryGetString(root, "event") ?? TryGetString(root, "eventType") ?? TryGetString(payload, "event") ?? "device_event",
            Device = TryGetString(payload, "device") ?? TryGetString(root, "device") ?? string.Empty,
            Sku = TryGetString(payload, "model") ?? TryGetString(payload, "sku") ?? TryGetString(root, "model") ?? TryGetString(root, "sku") ?? string.Empty,
            DeviceName = TryGetString(payload, "deviceName") ?? TryGetString(root, "deviceName"),
            Raw = root.Clone()
        };

        var timestampElement = payload.ValueKind != JsonValueKind.Undefined && payload.TryGetProperty("timestamp", out var ts)
            ? ts
            : root.TryGetProperty("timestamp", out var tsRoot) ? tsRoot : default;

        if (timestampElement.ValueKind == JsonValueKind.Number && timestampElement.TryGetInt64(out var tsNumber))
        {
            // Assume milliseconds if length > 10 digits
            evt.TimestampUtc = tsNumber > 3_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(tsNumber) : DateTimeOffset.FromUnixTimeSeconds(tsNumber);
        }
        else if (timestampElement.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(timestampElement.GetString(), out var parsedTs))
        {
            evt.TimestampUtc = parsedTs.ToUniversalTime();
        }
        else
        {
            evt.TimestampUtc = DateTimeOffset.UtcNow;
        }

        if (!string.IsNullOrWhiteSpace(evt.DeviceName))
            evt.DeviceName = evt.DeviceName?.Trim();

        if (payload.TryGetProperty("capabilities", out var capabilities) && capabilities.ValueKind == JsonValueKind.Array)
        {
            GoveeJsonParser.PopulateCapabilities(evt.Capabilities, capabilities);
        }
        else if (payload.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Array)
        {
            GoveeJsonParser.PopulateCapabilities(evt.Capabilities, properties);
        }
        else if (payload.TryGetProperty("state", out var stateArr) && stateArr.ValueKind == JsonValueKind.Array)
        {
            GoveeJsonParser.PopulateCapabilities(evt.Capabilities, stateArr);
        }

        return evt;
    }

    private static JsonElement ResolvePayload(JsonElement root)
    {
        if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object)
            return dataElement;

        if (root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.Object)
            return messageElement;

        if (root.TryGetProperty("payload", out var payloadElement) && payloadElement.ValueKind == JsonValueKind.Object)
            return payloadElement;

        return root;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out var i64) ? i64.ToString(System.Globalization.CultureInfo.InvariantCulture) : value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}
