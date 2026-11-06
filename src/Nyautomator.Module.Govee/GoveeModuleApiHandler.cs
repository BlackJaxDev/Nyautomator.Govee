using System.Text.Json;
using Nyautomator.Govee;
using Nyautomator.Module.Abstractions;
using Nyautomator.Runtime.Abstractions;
using Nyautomator.Runtime.Integrations.Govee;

namespace Nyautomator.Modules.Govee;

/// <summary>
/// Serves <c>/modules/govee/api/*</c>: status, device listing, device status and device commands.
/// </summary>
public sealed class GoveeModuleApiHandler(IModuleOptionsProvider? options, IGoveeIntegration govee) : IModuleApiHandler
{
    public const string ModuleIdValue = "govee";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly IModuleOptionsProvider? _options = options;
    private readonly IGoveeIntegration _govee = govee;

    public string ModuleId => ModuleIdValue;

    public static GoveeModuleOptions GetEffectiveOptions(IModuleOptionsProvider? options)
        => options?.Get(ModuleIdValue, GoveeModuleOptions.CreateDefault()) ?? GoveeModuleOptions.CreateDefault();

    public async Task<ModuleApiResponse> HandleAsync(ModuleApiRequest request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        var token = timeout.Token;
        var path = NormalizePath(request.Path).ToLowerInvariant();

        try
        {
            return path switch
            {
                "status" when IsMethod(request, "GET") => await GetStatusAsync(token).ConfigureAwait(false),
                "devices" when IsMethod(request, "GET") => await GetDevicesAsync(ParseBool(request, "refresh"), token).ConfigureAwait(false),
                "refresh" when IsMethod(request, "POST") => await GetDevicesAsync(forceRefresh: true, token).ConfigureAwait(false),
                "devices/status" when IsMethod(request, "POST") => await GetDeviceStatusesAsync(await ReadBodyAsync<DeviceStatusRequest>(request.Body, token).ConfigureAwait(false), token).ConfigureAwait(false),
                "device/control" or "command" when IsMethod(request, "POST") => await ControlDeviceAsync(await ReadBodyAsync<ControlDeviceRequest>(request.Body, token).ConfigureAwait(false), token).ConfigureAwait(false),
                _ => Error($"Govee module API path '/{path}' is not available.", 404)
            };
        }
        catch (JsonException ex)
        {
            return Error($"Invalid request body: {ex.Message}", 400);
        }
        catch (OperationCanceledException)
        {
            return Error("Govee request cancelled or timed out.", 504);
        }
        catch (Exception ex)
        {
            return Error($"Govee request failed: {ex.Message}", 500);
        }
    }

    /// <summary>
    /// Applies the current module API key to the integration. Returns false when no key is configured.
    /// </summary>
    public async Task<bool> EnsureConfiguredAsync(CancellationToken cancellationToken)
    {
        var apiKey = GetEffectiveOptions(_options).GetEffectiveApiKey();
        await _govee.ConfigureAsync(apiKey, cancellationToken).ConfigureAwait(false);
        return apiKey is not null && _govee.IsConfigured;
    }

    private async Task<ModuleApiResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var hasApiKey = GetEffectiveOptions(_options).GetEffectiveApiKey() is not null;
        var configured = await EnsureConfiguredAsync(cancellationToken).ConfigureAwait(false);
        var cached = _govee.GetCachedDevices();

        return ModuleApiResponse.Json(new
        {
            success = true,
            connected = configured,
            configured,
            hasApiKey,
            cachedDevices = cached.Count,
            message = configured
                ? $"Govee API key configured. {cached.Count} cached device(s)."
                : hasApiKey
                    ? "Govee integration failed to initialize. Double-check the API key."
                    : "Add your Govee API key in Settings to enable Govee devices."
        });
    }

    private async Task<ModuleApiResponse> GetDevicesAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (await NotConfiguredAsync(cancellationToken).ConfigureAwait(false) is { } notConfigured)
            return notConfigured;

        var devices = await _govee.GetDevicesAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
        var normalized = devices
            .Where(static device => device is not null)
            .OrderBy(static device => device.DeviceName ?? device.Device)
            .Select(ToDto)
            .ToArray();

        return ModuleApiResponse.Json(new
        {
            success = true,
            total = normalized.Length,
            timestampUtc = DateTimeOffset.UtcNow,
            devices = normalized
        });
    }

    private async Task<ModuleApiResponse> ControlDeviceAsync(ControlDeviceRequest? request, CancellationToken cancellationToken)
    {
        if (await NotConfiguredAsync(cancellationToken).ConfigureAwait(false) is { } notConfigured)
            return notConfigured;

        if (request is null || string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.Sku))
            return Error("Device ID and SKU are required.", 400);

        var command = new GoveeCommand
        {
            Device = request.DeviceId.Trim(),
            Sku = request.Sku.Trim(),
            Capability = new GoveeCommandCapability
            {
                Type = string.IsNullOrWhiteSpace(request.CapabilityType) ? "devices.capabilities.on_off" : request.CapabilityType,
                Instance = string.IsNullOrWhiteSpace(request.Instance) ? "powerSwitch" : request.Instance,
                Value = request.Value,
                Parameters = request.Parameters
            }
        };

        var success = await _govee.SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
        return success
            ? ModuleApiResponse.Json(new { success = true })
            : ModuleApiResponse.Json(new { success = false, message = "Govee command was rejected. Check API key and device connectivity." });
    }

    private async Task<ModuleApiResponse> GetDeviceStatusesAsync(DeviceStatusRequest? request, CancellationToken cancellationToken)
    {
        if (await NotConfiguredAsync(cancellationToken).ConfigureAwait(false) is { } notConfigured)
            return notConfigured;

        if (request?.Devices is null || request.Devices.Length == 0)
            return Error("Provide at least one device to query.", 400);

        var statuses = new List<object>();
        var failures = new List<object>();

        foreach (var device in request.Devices)
        {
            if (device is null || string.IsNullOrWhiteSpace(device.DeviceId) || string.IsNullOrWhiteSpace(device.Sku))
            {
                failures.Add(new { message = "Device entry missing deviceId and sku." });
                continue;
            }

            try
            {
                var status = await _govee
                    .GetDeviceStatusAsync(device.DeviceId.Trim(), device.Sku.Trim(), cancellationToken)
                    .ConfigureAwait(false);

                if (status is not null)
                    statuses.Add(ToStatusDto(status));
                else
                    failures.Add(new { device.DeviceId, device.Sku, message = "Status unavailable." });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new { device.DeviceId, device.Sku, message = ex.Message });
            }
        }

        return ModuleApiResponse.Json(new
        {
            success = failures.Count == 0,
            timestampUtc = DateTimeOffset.UtcNow,
            total = statuses.Count,
            statuses,
            failures = failures.Count > 0 ? failures : null
        });
    }

    private async Task<ModuleApiResponse?> NotConfiguredAsync(CancellationToken cancellationToken)
    {
        if (GetEffectiveOptions(_options).GetEffectiveApiKey() is null)
            return ModuleApiResponse.Json(new { success = false, message = "Add your Govee API key in Settings to enable this view." });

        if (!await EnsureConfiguredAsync(cancellationToken).ConfigureAwait(false))
            return ModuleApiResponse.Json(new { success = false, message = "Govee integration failed to initialize. Double-check the API key." });

        return null;
    }

    internal static object ToDto(GoveeDevice device)
        => new
        {
            deviceId = device.Device,
            sku = device.Sku,
            name = string.IsNullOrWhiteSpace(device.DeviceName) ? device.Device : device.DeviceName,
            type = device.Type,
            capabilities = device.Capabilities.Select(capability => new
            {
                type = capability.Type,
                instance = capability.Instance,
                parameters = ConvertJsonElement(capability.Parameters)
            }).ToArray(),
            localIpAddress = device.LocalIpAddress,
            wifiSsid = device.WifiSsid,
            wifiBssid = device.WifiBssid,
            wifiSignalStrength = device.WifiSignalStrength,
            firmwareVersion = device.FirmwareVersion,
            supportsLanControl = device.SupportsLanControl,
            isOnSameNetwork = device.IsOnSameNetwork
        };

    private static object ToStatusDto(GoveeDeviceStatus status)
        => new
        {
            deviceId = status.Device,
            sku = status.Sku,
            name = string.IsNullOrWhiteSpace(status.DeviceName) ? status.Device : status.DeviceName,
            retrievedAtUtc = status.RetrievedAtUtc,
            capabilities = status.Capabilities.Select(capability => new
            {
                type = capability.Type,
                instance = capability.Instance,
                value = capability.Value,
                state = ConvertJsonElement(capability.State)
            }).ToArray()
        };

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
                return element.TryGetInt64(out var i64)
                    ? i64
                    : element.TryGetDouble(out var dbl) ? dbl : element.GetDecimal();
            case JsonValueKind.Array:
                return element.EnumerateArray().Select(ConvertJsonElement).ToList();
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

    private static ModuleApiResponse Error(string message, int statusCode)
        => ModuleApiResponse.Json(new { ok = false, success = false, error = message, message }, statusCode);

    private static string NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Trim('/');

    private static bool IsMethod(ModuleApiRequest request, string expected)
        => string.Equals(request.Method?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool ParseBool(ModuleApiRequest request, string name)
        => request.Query.TryGetValue(name, out var value) && bool.TryParse(value, out var parsed) && parsed;

    private static async Task<T?> ReadBodyAsync<T>(Stream? body, CancellationToken cancellationToken)
    {
        if (body is null || !body.CanRead)
            return default;

        if (body.CanSeek && body.Length == 0)
            return default;

        return await JsonSerializer.DeserializeAsync<T>(body, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    internal sealed class ControlDeviceRequest
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public string? CapabilityType { get; set; }
        public string? Instance { get; set; }
        public object? Value { get; set; }
        public object? Parameters { get; set; }
    }

    internal sealed class DeviceStatusRequest
    {
        public DeviceSelection[] Devices { get; set; } = [];
    }

    internal sealed class DeviceSelection
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
    }
}
