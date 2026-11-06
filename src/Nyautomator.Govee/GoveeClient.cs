using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nyautomator.Govee;

/// <summary>
/// Lightweight wrapper around the Govee Open API.
/// </summary>
public sealed class GoveeClient : IDisposable
{
    private const string BaseUrl = "https://openapi.api.govee.com";
    private const string ApiKeyHeader = "Govee-API-Key";
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>
    /// Creates a new client instance configured for the provided API key.
    /// </summary>
    public GoveeClient(string apiKey, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key must be provided.", nameof(apiKey));

        ApiKey = apiKey.Trim();
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _httpClient.BaseAddress = new Uri(BaseUrl, UriKind.Absolute);
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Current API key assigned to the client.
    /// </summary>
    public string ApiKey { get; }

    /// <summary>
    /// Returns true when the provided API key matches the active key.
    /// </summary>
    public bool HasSameKey(string apiKey)
        => string.Equals(ApiKey, apiKey?.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// Retrieves the list of devices visible to the configured API key.
    /// </summary>
    public async Task<IReadOnlyList<GoveeDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "/router/api/v1/user/devices");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new GoveeApiException($"Govee devices request failed ({(int)response.StatusCode}). Response: {payload}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;
        var code = root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number
            ? codeElement.GetInt32()
            : 0;

        if (code != 200 && code != 0)
        {
            var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString()
                : "Unknown error";
            throw new GoveeApiException($"Govee API returned error code {code}: {message}");
        }

        if (!root.TryGetProperty("data", out var dataElement))
            return Array.Empty<GoveeDevice>();

        JsonElement devicesElement;
        if (dataElement.ValueKind == JsonValueKind.Array)
        {
            devicesElement = dataElement;
        }
        else if (dataElement.ValueKind == JsonValueKind.Object && dataElement.TryGetProperty("devices", out var innerDevices) && innerDevices.ValueKind == JsonValueKind.Array)
        {
            devicesElement = innerDevices;
        }
        else
        {
            return Array.Empty<GoveeDevice>();
        }

        var devices = new List<GoveeDevice>();

        foreach (var deviceElement in devicesElement.EnumerateArray())
        {
            var device = new GoveeDevice
            {
                Device = deviceElement.TryGetProperty("device", out var deviceId) && deviceId.ValueKind == JsonValueKind.String
                    ? deviceId.GetString() ?? string.Empty
                    : string.Empty,
                Sku = deviceElement.TryGetProperty("sku", out var skuElement) && skuElement.ValueKind == JsonValueKind.String
                    ? skuElement.GetString() ?? string.Empty
                    : deviceElement.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String
                        ? modelElement.GetString() ?? string.Empty
                        : string.Empty,
                Type = deviceElement.TryGetProperty("type", out var deviceTypeElement) && deviceTypeElement.ValueKind == JsonValueKind.String
                    ? deviceTypeElement.GetString() ?? string.Empty
                    : string.Empty
            };

            if (deviceElement.TryGetProperty("deviceName", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                device.DeviceName = nameElement.GetString();
            }

            device.SupportsLanControl = ReadBoolean(deviceElement, "lanControlSupport")
                || ReadBoolean(deviceElement, "lanControl")
                || ReadBoolean(deviceElement, "supportLanControl")
                || ReadBoolean(deviceElement, "supportLocal")
                || ReadBoolean(deviceElement, "supportLocalControl")
                || ReadBoolean(deviceElement, "lanControlEnable");

            if (deviceElement.TryGetProperty("lanControl", out var lanControlElement) && lanControlElement.ValueKind == JsonValueKind.Object)
            {
                device.SupportsLanControl = true;
                device.LocalIpAddress ??= NormalizeString(ReadString(lanControlElement, "ip") ?? ReadString(lanControlElement, "address"));
                device.WifiSsid ??= NormalizeString(ReadString(lanControlElement, "ssid"));
            }

            if (deviceElement.TryGetProperty("lan", out var lanElement) && lanElement.ValueKind == JsonValueKind.Object)
            {
                device.SupportsLanControl = true;
                device.LocalIpAddress ??= NormalizeString(ReadString(lanElement, "ip"));
                device.WifiSsid ??= NormalizeString(ReadString(lanElement, "ssid"));
            }

            if (deviceElement.TryGetProperty("wifi", out var wifiElement) && wifiElement.ValueKind == JsonValueKind.Object)
            {
                device.WifiSsid ??= NormalizeString(ReadString(wifiElement, "ssid") ?? ReadString(wifiElement, "name"));
                device.WifiBssid ??= NormalizeString(ReadString(wifiElement, "bssid"));
                device.LocalIpAddress ??= NormalizeString(ReadString(wifiElement, "local_ip") ?? ReadString(wifiElement, "localIp") ?? ReadString(wifiElement, "ip"));
                if (wifiElement.TryGetProperty("signal", out var signalElement) && signalElement.ValueKind == JsonValueKind.Number && signalElement.TryGetInt32(out var wifiSignalFromWifiObject))
                    device.WifiSignalStrength = wifiSignalFromWifiObject;
            }

            if (deviceElement.TryGetProperty("wifiSignal", out var wifiSignalElement) && wifiSignalElement.ValueKind == JsonValueKind.Number && wifiSignalElement.TryGetInt32(out var wifiSignal))
                device.WifiSignalStrength = wifiSignal;

            if (deviceElement.TryGetProperty("firmwareVersion", out var firmwareElement) && firmwareElement.ValueKind == JsonValueKind.String)
                device.FirmwareVersion = NormalizeString(firmwareElement.GetString());
            else if (deviceElement.TryGetProperty("hardwareVersion", out var hardwareElement) && hardwareElement.ValueKind == JsonValueKind.String)
                device.FirmwareVersion = NormalizeString(hardwareElement.GetString());

            if (!string.IsNullOrWhiteSpace(device.LocalIpAddress))
                device.SupportsLanControl = true;

            if (deviceElement.TryGetProperty("capabilities", out var capsElement) && capsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var capElement in capsElement.EnumerateArray())
                {
                    var capability = new GoveeCapability
                    {
                        Type = capElement.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                            ? typeElement.GetString() ?? string.Empty
                            : string.Empty,
                        Instance = capElement.TryGetProperty("instance", out var instanceElement) && instanceElement.ValueKind == JsonValueKind.String
                            ? instanceElement.GetString() ?? string.Empty
                            : string.Empty
                    };

                    if (capElement.TryGetProperty("parameters", out var parametersElement))
                    {
                        capability.Parameters = parametersElement.Clone();
                    }

                    device.Capabilities.Add(capability);
                }
            }

            devices.Add(device);
        }

        return devices;
    }

    /// <summary>
    /// Retrieves the current state for a specific Govee device.
    /// </summary>
    public async Task<GoveeDeviceStatus?> GetDeviceStatusAsync(string deviceId, string sku, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Device identifier must be provided.", nameof(deviceId));
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("Device SKU/model must be provided.", nameof(sku));

        var query = $"/router/api/v1/device/state?device={Uri.EscapeDataString(deviceId.Trim())}&model={Uri.EscapeDataString(sku.Trim())}";

        using var request = CreateRequest(HttpMethod.Get, query);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new GoveeApiException($"Govee device status request failed ({(int)response.StatusCode}). Response: {payload}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;
        var code = root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number
            ? codeElement.GetInt32()
            : 0;

        if (code != 200 && code != 0)
        {
            var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString()
                : "Unknown error";
            throw new GoveeApiException($"Govee API returned error code {code}: {message}");
        }

        if (!root.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Object)
            return null;

        var status = new GoveeDeviceStatus
        {
            Device = dataElement.TryGetProperty("device", out var deviceElement) && deviceElement.ValueKind == JsonValueKind.String
                ? deviceElement.GetString() ?? deviceId
                : deviceId,
            Sku = dataElement.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String
                ? modelElement.GetString() ?? sku
                : sku,
            RetrievedAtUtc = DateTimeOffset.UtcNow
        };

        if (dataElement.TryGetProperty("deviceName", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            status.DeviceName = nameElement.GetString();

        if (dataElement.TryGetProperty("capabilities", out var capabilitiesElement) && capabilitiesElement.ValueKind == JsonValueKind.Array)
            GoveeJsonParser.PopulateCapabilities(status.Capabilities, capabilitiesElement);
        else if (dataElement.TryGetProperty("properties", out var propertiesElement) && propertiesElement.ValueKind == JsonValueKind.Array)
            GoveeJsonParser.PopulateCapabilities(status.Capabilities, propertiesElement);
        else if (dataElement.TryGetProperty("state", out var stateElement) && stateElement.ValueKind == JsonValueKind.Array)
            GoveeJsonParser.PopulateCapabilities(status.Capabilities, stateElement);

        return status;
    }

    /// <summary>
    /// Issues a command to a Govee device using the Open API.
    /// </summary>
    public async Task SendCommandAsync(GoveeCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        if (command.Capability is null)
            throw new ArgumentException("Command capability must be provided.", nameof(command));

        var envelope = new
        {
            requestId = Guid.NewGuid().ToString("N"),
            payload = new
            {
                sku = command.Sku,
                device = command.Device,
                capability = new
                {
                    type = command.Capability.Type,
                    instance = command.Capability.Instance,
                    value = command.Capability.Value,
                    parameters = command.Capability.Parameters
                }
            }
        };

        using var request = CreateRequest(
            HttpMethod.Post,
            "/router/api/v1/device/control",
            JsonContent.Create(envelope, options: _serializerOptions));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new GoveeApiException($"Govee command failed ({(int)response.StatusCode}). Response: {payload}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;
        if (root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number)
        {
            var code = codeElement.GetInt32();
            if (code != 200)
            {
                var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                    ? messageElement.GetString()
                    : "Unknown error";
                throw new GoveeApiException($"Govee API command error {code}: {message}");
            }
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUri, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, relativeUri)
        {
            Content = content
        };

        request.Headers.TryAddWithoutValidation(ApiKeyHeader, ApiKey);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (request.Content is null)
        {
            // Force Content-Type header by providing empty content
            request.Content = new ByteArrayContent(Array.Empty<byte>());
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        return request;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static string? NormalizeString(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
