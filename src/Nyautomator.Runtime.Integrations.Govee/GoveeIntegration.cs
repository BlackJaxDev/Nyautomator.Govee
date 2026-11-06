using Nyautomator;
using Nyautomator.Govee;
using Nyautomator.Runtime.Abstractions;

namespace Nyautomator.Runtime.Integrations.Govee;

public sealed class GoveeIntegration : IGoveeIntegration
{
    private static readonly TimeSpan DeviceCacheLifetime = TimeSpan.FromMinutes(2);

    private readonly object _sync = new();
    private readonly SemaphoreSlim _deviceCacheLock = new(1, 1);
    private GoveeClient? _client;
    private GoveeEventStream? _eventStream;
    private IReadOnlyList<GoveeDevice> _cachedDevices = [];
    private DateTimeOffset _cachedDevicesAtUtc = DateTimeOffset.MinValue;
    private bool _disposed;

    public string Name => "Govee";

    public bool IsConfigured
    {
        get
        {
            lock (_sync)
            {
                return _client is not null;
            }
        }
    }

    public event EventHandler<GoveeDeviceEvent>? DeviceEventReceived;

    /// <summary>
    /// Host-level configuration no longer carries Govee settings; the module runtime participant
    /// supplies the API key from module configuration through <see cref="ConfigureAsync(string?, CancellationToken)"/>.
    /// </summary>
    public Task ConfigureAsync(AppConfiguration config, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task ConfigureAsync(string? apiKey, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                DisposeClient();
                DisposeEventStream();
                ResetDeviceCache();
                return Task.CompletedTask;
            }

            apiKey = apiKey.Trim();
            if (_client?.HasSameKey(apiKey) == true)
                return Task.CompletedTask;

            DisposeClient();
            _client = new GoveeClient(apiKey);
            RestartEventStream(apiKey);
            ResetDeviceCache();
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            _eventStream?.Start();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        lock (_sync)
        {
            DisposeEventStream();
            DisposeClient();
            ResetDeviceCache();
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<GoveeDevice>> GetDevicesAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        await _deviceCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GoveeClient? client;
            lock (_sync)
            {
                client = _client;
            }

            if (client is null)
            {
                _cachedDevices = [];
                _cachedDevicesAtUtc = DateTimeOffset.UtcNow;
                return _cachedDevices;
            }

            var now = DateTimeOffset.UtcNow;
            if (!forceRefresh && now - _cachedDevicesAtUtc < DeviceCacheLifetime && _cachedDevices.Count > 0)
                return _cachedDevices;

            try
            {
                var devices = await client.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
                _cachedDevices = devices?.ToArray() ?? [];
                _cachedDevicesAtUtc = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Govee] Device refresh failed: {ex.Message}");
                if (_cachedDevices.Count == 0)
                    return [];
            }

            return _cachedDevices;
        }
        finally
        {
            _deviceCacheLock.Release();
        }
    }

    public async Task<bool> SendCommandAsync(GoveeCommand command, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        GoveeClient? client;
        lock (_sync)
        {
            client = _client;
        }

        if (client is null)
            return false;

        try
        {
            await client.SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Govee] Send command failed: {ex.Message}");
            return false;
        }
    }

    public async Task<GoveeDeviceStatus?> GetDeviceStatusAsync(string deviceId, string sku, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Device identifier must be provided.", nameof(deviceId));
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("Device SKU/model must be provided.", nameof(sku));

        GoveeClient? client;
        lock (_sync)
        {
            client = _client;
        }

        if (client is null)
            return null;

        try
        {
            return await client.GetDeviceStatusAsync(deviceId.Trim(), sku.Trim(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Govee] Device status request failed: {ex.Message}");
            return null;
        }
    }

    public IReadOnlyList<GoveeDevice> GetCachedDevices()
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            return _cachedDevices;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _deviceCacheLock.Dispose();
    }

    private void ResetDeviceCache()
    {
        _cachedDevices = [];
        _cachedDevicesAtUtc = DateTimeOffset.MinValue;
    }

    private void RestartEventStream(string apiKey)
    {
        DisposeEventStream();

        _eventStream = new GoveeEventStream(apiKey);
        _eventStream.DeviceEventReceived += OnDeviceEventReceived;
        _eventStream.Start();
    }

    private void OnDeviceEventReceived(object? sender, GoveeDeviceEvent evt)
    {
        if (evt is null)
            return;

        lock (_sync)
        {
            var device = _cachedDevices.FirstOrDefault(d => string.Equals(d.Device, evt.Device, StringComparison.OrdinalIgnoreCase));
            if (device is not null)
            {
                if (string.IsNullOrWhiteSpace(evt.Sku))
                    evt.Sku = device.Sku;
                if (string.IsNullOrWhiteSpace(evt.DeviceName) && !string.IsNullOrWhiteSpace(device.DeviceName))
                    evt.DeviceName = device.DeviceName;
            }
        }

        try
        {
            DeviceEventReceived?.Invoke(this, evt);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Govee] Event handler failure: {ex.Message}");
        }
    }

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
    }

    private void DisposeEventStream()
    {
        if (_eventStream is null)
            return;

        _eventStream.DeviceEventReceived -= OnDeviceEventReceived;
        try
        {
            _eventStream.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Govee] Failed to dispose event stream: {ex.Message}");
        }
        finally
        {
            _eventStream = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GoveeIntegration));
    }
}
