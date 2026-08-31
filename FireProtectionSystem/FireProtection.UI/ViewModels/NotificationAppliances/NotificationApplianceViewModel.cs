using System.Collections.Generic;
using FireProtection.UI.Models;
using FireProtection.UI.ViewModels.Devices;

namespace FireProtection.UI.ViewModels.NotificationAppliances
{
    /// <summary>
    /// Notification-appliance (strobe / speaker / speaker-strobe) placement tab. Inherits the shared device
    /// placement workflow (<see cref="DevicePlacementViewModelBase"/>); adds NFPA-72 appliance-specific
    /// parameters (candela, dBA). Placement backend is deferred (UI-first slice).
    /// </summary>
    public class NotificationApplianceViewModel : DevicePlacementViewModelBase
    {
        private string _applianceType = "Speaker-Strobe";
        private string _candela = "75";
        private string _notificationDba = "75";

        public NotificationApplianceViewModel()
            : this(null)
        {
        }

        public NotificationApplianceViewModel(FireProtectionUiData data)
            : base(data)
        {
        }

        public override string DeviceDisplayName => "NOTIFICATION APPLIANCE CONFIGURATION";

        public IReadOnlyList<string> ApplianceTypeOptions => new[] { "Strobe", "Speaker", "Speaker-Strobe" };
        public IReadOnlyList<string> CandelaOptions => new[] { "15", "30", "75", "110", "135", "177" };
        public IReadOnlyList<string> NotificationDbaOptions => new[] { "65", "70", "75", "80", "85", "90" };

        public string ApplianceType
        {
            get => _applianceType;
            set => SetProperty(ref _applianceType, value);
        }

        public string Candela
        {
            get => _candela;
            set => SetProperty(ref _candela, value);
        }

        public string NotificationDba
        {
            get => _notificationDba;
            set => SetProperty(ref _notificationDba, value);
        }
    }
}
