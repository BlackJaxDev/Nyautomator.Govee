using Microsoft.Extensions.DependencyInjection;
using Nyautomator.Module.Abstractions;
using Nyautomator.Runtime.Abstractions;
using Nyautomator.Runtime.Integrations.Govee;
using NyautomatorUI.Server.Automation;

namespace Nyautomator.Modules.Govee;

public sealed class GoveeModule : INyautomatorModule
{
    private static readonly object FallbackGate = new();
    private static IGoveeIntegration? _fallbackIntegration;

    public NyautomatorModuleDescriptor Descriptor { get; } = new(
        GoveeModuleApiHandler.ModuleIdValue,
        "Govee",
        "Govee Open API device control, realtime device events, and smart-light automation nodes.",
        "0.1.0");

    public void ConfigureServices(NyautomatorModuleServiceContext context, IServiceCollection services)
    {
        services.AddNyautomatorGoveeIntegration();
    }

    public void ConfigureRuntime(NyautomatorModuleRuntimeContext context)
    {
        var options = context.Services.GetService<IModuleOptionsProvider>();
        var integration = GetOrCreateIntegration(context.Services);
        var handler = new GoveeModuleApiHandler(options, integration);

        WireAutomationHost(handler, integration);
        context.ApiHandlers.Register(handler);
    }

    internal static IGoveeIntegration GetOrCreateIntegration(IServiceProvider services)
    {
        lock (FallbackGate)
        {
            _fallbackIntegration ??= services.GetService<IGoveeIntegration>() ?? new GoveeIntegration();
            return _fallbackIntegration;
        }
    }

    private static void WireAutomationHost(GoveeModuleApiHandler handler, IGoveeIntegration integration)
    {
        GoveeAutomationHost.IsGoveeConfigured = () =>
        {
            try
            {
                return handler.EnsureConfiguredAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                return false;
            }
        };
        GoveeAutomationHost.SendGoveeCommand = integration.SendCommandAsync;
        GoveeAutomationHost.GetGoveeDevices = integration.GetDevicesAsync;
        GoveeAutomationHost.GetGoveeDeviceStatus = integration.GetDeviceStatusAsync;

        GoveeAutomationIntegration.Register(
            subscribe: callback => integration.DeviceEventReceived += callback,
            unsubscribe: callback => integration.DeviceEventReceived -= callback);
    }
}
