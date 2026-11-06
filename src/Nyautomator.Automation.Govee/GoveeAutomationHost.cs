using Nyautomator.Govee;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Delegates the Govee module wires up so automation nodes can reach the Govee runtime
/// without referencing module or host services directly.
/// </summary>
public static class GoveeAutomationHost
{
    public static Func<bool>? IsGoveeConfigured { get; set; }
    public static Func<GoveeCommand, CancellationToken, Task<bool>>? SendGoveeCommand { get; set; }
    public static Func<bool, CancellationToken, Task<IReadOnlyList<GoveeDevice>>>? GetGoveeDevices { get; set; }
    public static Func<string, string, CancellationToken, Task<GoveeDeviceStatus?>>? GetGoveeDeviceStatus { get; set; }

    public static void Reset()
    {
        IsGoveeConfigured = null;
        SendGoveeCommand = null;
        GetGoveeDevices = null;
        GetGoveeDeviceStatus = null;
    }
}
