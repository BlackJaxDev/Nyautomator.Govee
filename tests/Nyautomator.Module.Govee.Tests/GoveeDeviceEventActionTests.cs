using Nyautomator.Govee;
using NyautomatorUI.Server.Automation;
using Xunit;

namespace Nyautomator.Module.Govee.Tests;

public class GoveeDeviceEventActionTests
{
    [Fact]
    public void Matches_ReturnsTrue_ForMatchingDeviceAndEvent()
    {
        var action = new GoveeDeviceEventAction
        {
            Device = "dev123|H123|Lamp",
            EventType = "device.status"
        };

        var evt = new GoveeDeviceEvent
        {
            Device = "dev123",
            Sku = "H123",
            EventType = "device.status"
        };

        Assert.True(action.Matches(evt));
    }

    [Fact]
    public void Matches_ReturnsFalse_WhenCapabilityFilterDoesNotMatch()
    {
        var action = new GoveeDeviceEventAction
        {
            Device = "dev123|H123|Lamp",
            CapabilityType = "devices.capabilities.switch",
            CapabilityInstance = "brightness"
        };

        var evt = new GoveeDeviceEvent
        {
            Device = "dev123",
            Sku = "H123",
            EventType = "device.status"
        };

        evt.Capabilities.Add(new GoveeCapabilityState
        {
            Type = "devices.capabilities.switch",
            Instance = "power"
        });

        Assert.False(action.Matches(evt));
    }

    [Fact]
    public void ShouldHandlePayload_IgnoresOtherPayloadTypes()
    {
        var action = new GoveeDeviceEventAction { Device = "dev123|H123|Lamp" };

        Assert.False(action.ShouldHandlePayload("not a govee event"));
        Assert.True(action.ShouldHandlePayload(new GoveeDeviceEvent { Device = "dev123", Sku = "H123" }));
    }
}
