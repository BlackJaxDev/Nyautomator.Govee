using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Nyautomator.Govee;

namespace NyautomatorUI.Server.Automation;

public sealed class GoveeDeviceColorTemperatureReaction : GoveeReactionBase
{
    public override string Id => "Govee.Device.ColorTemperature";
    public override string DisplayName => "Govee: Set Color Temperature";
    public override string Description => "Sets the color temperature of a Govee device in Kelvin.";

    [Description("Desired temperature in Kelvin. Values outside the supported range will be clamped.")]
    public int TemperatureKelvin { get; set; } = 3500;

    protected override Task<GoveeCommandCapability?> BuildCapabilityAsync(string deviceId, string sku, GoveeDevice? device, CancellationToken cancellationToken)
    {
        var capability = FindCapabilityByInstance(device, "colorTemperature");
        var (type, instance) = ResolveCapabilityInfo(device, "devices.capabilities.color_setting", "colorTemperature");
        var (min, max, step) = ExtractRange(capability);

        var minValue = min.HasValue ? (int)Math.Round(min.Value) : 2000;
        var maxValue = max.HasValue ? (int)Math.Round(max.Value) : 9000;
        var target = Math.Clamp(TemperatureKelvin, minValue, maxValue);

        if (step.HasValue && step.Value > 0)
        {
            var increment = (int)Math.Round(step.Value);
            if (increment > 0)
            {
                var relative = target - minValue;
                target = minValue + (int)Math.Round(relative / (double)increment) * increment;
                target = Math.Clamp(target, minValue, maxValue);
            }
        }

        return Task.FromResult<GoveeCommandCapability?>(new GoveeCommandCapability
        {
            Type = type,
            Instance = instance,
            Value = target
        });
    }
}
