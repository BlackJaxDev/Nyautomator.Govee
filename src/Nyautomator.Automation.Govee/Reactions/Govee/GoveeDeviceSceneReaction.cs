using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nyautomator.Govee;

namespace NyautomatorUI.Server.Automation;

public sealed class GoveeDeviceSceneReaction : GoveeReactionBase
{
    public override string Id => "Govee.Device.Scene";
    public override string DisplayName => "Govee: Activate Scene";
    public override string Description => "Activates a predefined lighting scene on a Govee device.";

    [Description("Scene or mode identifier to activate. Accepts dynamic string inputs.")]
    public string Scene { get; set; } = string.Empty;

    protected override Task<GoveeCommandCapability?> BuildCapabilityAsync(string deviceId, string sku, GoveeDevice? device, CancellationToken cancellationToken)
    {
        var (type, instance) = ResolveCapabilityInfo(device, "devices.capabilities.mode", "scene");

        var sceneValue = (Scene ?? string.Empty).Trim();
        if (sceneValue.Length == 0)
        {
            Log("Scene value is empty; skipping command.");
            return Task.FromResult<GoveeCommandCapability?>(null);
        }

        var capability = FindCapabilityByInstance(device, instance);
        var allowed = ExtractEnumValues(capability, "modes", "scenes", "values");
        if (allowed.Count > 0 && !allowed.Any(value => string.Equals(value, sceneValue, StringComparison.OrdinalIgnoreCase)))
            Log($"Scene '{sceneValue}' is not present in the reported capability list; attempting anyway.");

        return Task.FromResult<GoveeCommandCapability?>(new GoveeCommandCapability
        {
            Type = type,
            Instance = instance,
            Value = sceneValue
        });
    }
}
