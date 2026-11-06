using Nyautomator.Govee;

namespace NyautomatorUI.Server.Automation;

/// <summary>
/// Registers realtime Govee device events as an automation event source.
/// </summary>
public static class GoveeAutomationIntegration
{
    public const string SourceName = "Govee";

    private static readonly object Gate = new();
    private static Action<EventHandler<GoveeDeviceEvent>>? _subscribe;
    private static Action<EventHandler<GoveeDeviceEvent>>? _unsubscribe;
    private static bool _wired;

    /// <summary>
    /// Registers the event source. <paramref name="subscribe"/> and <paramref name="unsubscribe"/>
    /// attach and detach a handler on the Govee integration's device event stream.
    /// </summary>
    public static void Register(
        Action<EventHandler<GoveeDeviceEvent>> subscribe,
        Action<EventHandler<GoveeDeviceEvent>> unsubscribe)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);

        lock (Gate)
        {
            if (_wired)
                Unlink();

            _subscribe = subscribe;
            _unsubscribe = unsubscribe;
        }

        AutomationFlowEngine.RegisterExternalEventSource(SourceName, Link, Unlink);
    }

    public static void Unregister()
    {
        AutomationFlowEngine.UnregisterExternalEventSource(SourceName);
        lock (Gate)
        {
            Unlink();
            _subscribe = null;
            _unsubscribe = null;
        }
    }

    private static void Link()
    {
        lock (Gate)
        {
            if (_wired || _subscribe is null)
                return;

            _subscribe(OnDeviceEventReceived);
            _wired = true;

            if (GoveeAutomationHost.IsGoveeConfigured?.Invoke() != true)
                Console.WriteLine("[Automation] Govee event actions are available but the Govee module has no API key configured.");
        }
    }

    private static void Unlink()
    {
        lock (Gate)
        {
            if (!_wired)
                return;

            _unsubscribe?.Invoke(OnDeviceEventReceived);
            _wired = false;
        }
    }

    private static void OnDeviceEventReceived(object? sender, GoveeDeviceEvent evt)
    {
        if (evt is null)
            return;

        AutomationFlowEngine.DispatchExternalAction(SourceName, GoveeDeviceEventAction.TypeId, evt);
    }
}
