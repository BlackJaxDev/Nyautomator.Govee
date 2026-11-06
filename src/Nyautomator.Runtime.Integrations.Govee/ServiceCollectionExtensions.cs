using Microsoft.Extensions.DependencyInjection;

namespace Nyautomator.Runtime.Integrations.Govee;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Govee integration service. The runtime participant is contributed by the
    /// Govee module manifest, so this does not register an <c>IRuntimeParticipant</c>.
    /// </summary>
    public static IServiceCollection AddNyautomatorGoveeIntegration(this IServiceCollection services)
    {
        services.AddSingleton<IGoveeIntegration, GoveeIntegration>();
        return services;
    }
}
