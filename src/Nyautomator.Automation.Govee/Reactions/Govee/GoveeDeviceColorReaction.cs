using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Nyautomator.Govee;

namespace NyautomatorUI.Server.Automation;

public sealed class GoveeDeviceColorReaction : GoveeReactionBase
{
    public override string Id => "Govee.Device.Color";
    public override string DisplayName => "Govee: Set RGB Color";
    public override string Description => "Sets the RGB color of a capable Govee device.";

    [Description("Optional hex color in the form #RRGGBB. Overrides individual component values when provided.")]
    public string? Hex { get; set; }

    [Description("Red channel (0-255).")]
    public int Red { get; set; } = 255;

    [Description("Green channel (0-255).")]
    public int Green { get; set; } = 255;

    [Description("Blue channel (0-255).")]
    public int Blue { get; set; } = 255;

    protected override Task<GoveeCommandCapability?> BuildCapabilityAsync(string deviceId, string sku, GoveeDevice? device, CancellationToken cancellationToken)
    {
        var capability = FindCapabilityByInstance(device, "colorRgb");
        var (type, instance) = ResolveCapabilityInfo(device, "devices.capabilities.color_setting", "colorRgb");

        var rgb = ResolveColor();
        var packedValue = PackRgb(rgb.r, rgb.g, rgb.b);

        return Task.FromResult<GoveeCommandCapability?>(new GoveeCommandCapability
        {
            Type = type,
            Instance = instance,
            Value = packedValue
        });
    }

    private (int r, int g, int b) ResolveColor()
    {
        if (TryParseHex(Hex, out var r, out var g, out var b))
            return (r, g, b);

        return (
            Math.Clamp(Red, 0, 255),
            Math.Clamp(Green, 0, 255),
            Math.Clamp(Blue, 0, 255));
    }

    private static int PackRgb(int r, int g, int b)
    {
        static int ClampByte(int value) => Math.Clamp(value, 0, 255);

        var red = ClampByte(r);
        var green = ClampByte(g);
        var blue = ClampByte(b);
        return (red << 16) | (green << 8) | blue;
    }

    private static bool TryParseHex(string? value, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (text.StartsWith('#'))
            text = text[1..];

        if (text.Length != 6)
            return false;

        if (int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
        {
            r = (packed >> 16) & 0xFF;
            g = (packed >> 8) & 0xFF;
            b = packed & 0xFF;
            return true;
        }

        return false;
    }
}
