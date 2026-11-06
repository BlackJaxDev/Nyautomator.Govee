using Nyautomator;
using Nyautomator.Govee;
using Nyautomator.Modules.Govee;
using Nyautomator.Runtime.Integrations.Govee;
using Xunit;
using static Nyautomator.Module.Govee.Tests.TestSupport;

namespace Nyautomator.Module.Govee.Tests;

public sealed class GoveeModuleApiHandlerTests
{
    [Fact]
    public async Task Status_ReportsMissingApiKey()
    {
        var handler = new GoveeModuleApiHandler(new FakeOptionsProvider(new GoveeModuleOptions()), new FakeGoveeIntegration());

        var response = await handler.HandleAsync(Request("govee", "GET", "/status"), CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        var json = ToJson(response.Body);
        Assert.False(json.GetProperty("configured").GetBoolean());
        Assert.False(json.GetProperty("hasApiKey").GetBoolean());
    }

    [Fact]
    public async Task Devices_AppliesModuleApiKeyAndReturnsDevices()
    {
        var govee = new FakeGoveeIntegration
        {
            Devices = [new GoveeDevice { Device = "dev1", Sku = "H6001", DeviceName = "Desk" }]
        };
        var handler = new GoveeModuleApiHandler(new FakeOptionsProvider(new GoveeModuleOptions { ApiKey = " key " }), govee);

        var response = await handler.HandleAsync(Request("govee", "GET", "/devices"), CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("key", govee.ConfiguredApiKey);
        var json = ToJson(response.Body);
        Assert.Equal(1, json.GetProperty("total").GetInt32());
        Assert.Equal("Desk", json.GetProperty("devices")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Devices_WithoutApiKey_ReturnsUnsuccessfulPayload()
    {
        var handler = new GoveeModuleApiHandler(new FakeOptionsProvider(new GoveeModuleOptions()), new FakeGoveeIntegration());

        var response = await handler.HandleAsync(Request("govee", "GET", "/devices"), CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.False(ToJson(response.Body).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task DeviceControl_SendsCommandWithDefaults()
    {
        var govee = new FakeGoveeIntegration();
        var handler = new GoveeModuleApiHandler(new FakeOptionsProvider(new GoveeModuleOptions { ApiKey = "key" }), govee);

        var response = await handler.HandleAsync(
            Request("govee", "POST", "/device/control", """{"deviceId":"dev1","sku":"H6001","value":1}"""),
            CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.True(ToJson(response.Body).GetProperty("success").GetBoolean());
        var command = Assert.Single(govee.Commands);
        Assert.Equal("dev1", command.Device);
        Assert.Equal("devices.capabilities.on_off", command.Capability.Type);
        Assert.Equal("powerSwitch", command.Capability.Instance);
    }

    [Fact]
    public async Task DeviceControl_RequiresDeviceAndSku()
    {
        var handler = new GoveeModuleApiHandler(new FakeOptionsProvider(new GoveeModuleOptions { ApiKey = "key" }), new FakeGoveeIntegration());

        var response = await handler.HandleAsync(Request("govee", "POST", "/device/control", """{"deviceId":"dev1"}"""), CancellationToken.None);

        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task UnknownPath_Returns404()
    {
        var handler = new GoveeModuleApiHandler(new FakeOptionsProvider(null), new FakeGoveeIntegration());

        var response = await handler.HandleAsync(Request("govee", "GET", "/nope"), CancellationToken.None);

        Assert.Equal(404, response.StatusCode);
    }

    private sealed class FakeGoveeIntegration : IGoveeIntegration
    {
        public string? ConfiguredApiKey { get; private set; }
        public IReadOnlyList<GoveeDevice> Devices { get; set; } = [];
        public List<GoveeCommand> Commands { get; } = [];

        public string Name => "Govee";
        public bool IsConfigured => ConfiguredApiKey is not null;
        public event EventHandler<GoveeDeviceEvent>? DeviceEventReceived;

        public Task ConfigureAsync(string? apiKey, CancellationToken cancellationToken)
        {
            ConfiguredApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
            return Task.CompletedTask;
        }

        public Task ConfigureAsync(AppConfiguration config, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<IReadOnlyList<GoveeDevice>> GetDevicesAsync(bool forceRefresh, CancellationToken cancellationToken)
            => Task.FromResult(Devices);

        public Task<bool> SendCommandAsync(GoveeCommand command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(true);
        }

        public Task<GoveeDeviceStatus?> GetDeviceStatusAsync(string deviceId, string sku, CancellationToken cancellationToken)
            => Task.FromResult<GoveeDeviceStatus?>(new GoveeDeviceStatus { Device = deviceId, Sku = sku });

        public IReadOnlyList<GoveeDevice> GetCachedDevices() => Devices;

        public void Raise(GoveeDeviceEvent evt) => DeviceEventReceived?.Invoke(this, evt);
    }
}
