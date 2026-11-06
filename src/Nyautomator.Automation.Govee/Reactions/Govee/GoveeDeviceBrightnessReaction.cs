using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Nyautomator.Govee;

namespace NyautomatorUI.Server.Automation;

public sealed class GoveeDeviceBrightnessReaction : GoveeReactionBase
{
    public override string Id => "Govee.Device.Brightness";
    public override string DisplayName => "Govee: Set Brightness";
    public override string Description => "Adjusts the brightness level of a Govee device (0-100).";

    [Description("Target brightness percentage (0-100). Values outside the supported range will be clamped.")]
    public int Brightness { get; set; } = 100;

    protected override Task<GoveeCommandCapability?> BuildCapabilityAsync(string deviceId, string sku, GoveeDevice? device, CancellationToken cancellationToken)
    {
        var capability = FindCapabilityByInstance(device, "brightness");
        var (type, instance) = ResolveCapabilityInfo(device, "devices.capabilities.range", "brightness");
        var (min, max, _) = ExtractRange(capability);

        var minValue = min.HasValue ? (int)Math.Round(min.Value) : 0;
        var maxValue = max.HasValue ? (int)Math.Round(max.Value) : 100;

        var target = Math.Clamp(Brightness, minValue, maxValue);

        return Task.FromResult<GoveeCommandCapability?>(new GoveeCommandCapability
        {
            Type = type,
            Instance = instance,
            Value = target
        });
    }
}
