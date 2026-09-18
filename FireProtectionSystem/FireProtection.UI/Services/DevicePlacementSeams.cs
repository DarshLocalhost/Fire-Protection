namespace FireProtection.UI.Services
{
    /// <summary>
    /// Bundles the Revit-aware device seams (family source + placement executor) for the non-sprinkler
    /// device tabs so they can be threaded from the Backend command through <see cref="UiLauncher"/>,
    /// the window and the root ViewModel without adding a new positional parameter at every layer.
    /// <para>
    /// Every slot is optional. A null executor leaves that tab's placement disabled (the base VM reports
    /// <c>IsBackendPending</c>) while room/level selection stays available — which is exactly the state for
    /// tests, the WPF designer and the standalone catalog host, where no Revit context exists to build them.
    /// </para>
    /// </summary>
    public sealed class DevicePlacementSeams
    {
        /// <summary>Smoke-detector family/type source (OST_FireAlarmDevices). Fallback for the family combo
        /// when the catalog lists none.</summary>
        public IDeviceFamilySource SmokeFamilySource { get; set; }

        /// <summary>Smoke-detector placement executor (NFPA 72 calc + Revit placement). Null =&gt; tab is
        /// selection-only.</summary>
        public IDevicePlacementExecutor SmokeExecutor { get; set; }

        /// <summary>Notification-appliance family/type source (OST_FireAlarmDevices).</summary>
        public IDeviceFamilySource NotificationFamilySource { get; set; }

        /// <summary>Notification-appliance placement executor. Null =&gt; tab is selection-only.</summary>
        public IDevicePlacementExecutor NotificationExecutor { get; set; }
    }
}
