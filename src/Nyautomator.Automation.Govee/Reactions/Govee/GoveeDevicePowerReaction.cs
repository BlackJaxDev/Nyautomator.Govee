using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Nyautomator.Govee;

namespace NyautomatorUI.Server.Automation;

public sealed class GoveeDevicePowerReaction : GoveeReactionBase
{
    public override string Id => "Govee.Device.Power";
    public override string DisplayName => "Govee: Power";
    public override string Description => "Turns a Govee device on or off.";

    [Description("True to power the device on, false to turn it off. Accepts dynamic boolean inputs.")]
    public bool TurnOn { get; set; } = true;

    protected override Task<GoveeCommandCapability?> BuildCapabilityAsync(string deviceId, string sku, GoveeDevice? device, CancellationToken cancellationToken)
    {
        var (type, instance) = ResolveCapabilityInfo(device, "devices.capabilities.on_off", "power");
        return Task.FromResult<GoveeCommandCapability?>(new GoveeCommandCapability
        {
            Type = type,
            Instance = instance,
            Value = TurnOn
        });
    }
}
