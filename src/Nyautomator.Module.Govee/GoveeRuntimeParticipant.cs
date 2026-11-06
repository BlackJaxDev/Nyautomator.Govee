using Nyautomator;
using Nyautomator.Runtime.Abstractions;
using Nyautomator.Runtime.Integrations.Govee;

namespace Nyautomator.Modules.Govee;

/// <summary>
/// Runtime participant contributed by the Govee module manifest. It applies the module-owned API key
/// and owns the realtime event stream lifecycle.
/// </summary>
public sealed class GoveeRuntimeParticipant : IRuntimeParticipant
{
    private readonly IGoveeIntegration _inner;
    private readonly IModuleOptionsProvider? _options;

    public GoveeRuntimeParticipant(IServiceProvider services)
    {
        _inner = GoveeModule.GetOrCreateIntegration(services);
        _options = services.GetService(typeof(IModuleOptionsProvider)) as IModuleOptionsProvider;
        if (_options is not null)
            _options.ModuleOptionsChanged += OnModuleOptionsChanged;
    }

    public string Name => _inner.Name;

    public Task ConfigureAsync(AppConfiguration config, CancellationToken cancellationToken)
        => ApplyOptionsAsync(cancellationToken);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ApplyOptionsAsync(cancellationToken).ConfigureAwait(false);
        await _inner.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => _inner.StopAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        if (_options is not null)
            _options.ModuleOptionsChanged -= OnModuleOptionsChanged;

        return ValueTask.CompletedTask;
    }

    private Task ApplyOptionsAsync(CancellationToken cancellationToken)
        => _inner.ConfigureAsync(GoveeModuleApiHandler.GetEffectiveOptions(_options).GetEffectiveApiKey(), cancellationToken);

    private void OnModuleOptionsChanged(ModuleOptionsChangedEventArgs args)
    {
        if (!string.Equals(args.ModuleId, GoveeModuleApiHandler.ModuleIdValue, StringComparison.OrdinalIgnoreCase))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await ApplyOptionsAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Govee] Failed to apply module options: {ex.Message}");
            }
        });
    }
}
