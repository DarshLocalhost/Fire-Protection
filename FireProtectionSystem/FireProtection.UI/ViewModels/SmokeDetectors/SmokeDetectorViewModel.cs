using System.Collections.Generic;
using FireProtection.UI.Models;
using FireProtection.UI.ViewModels.Devices;

namespace FireProtection.UI.ViewModels.SmokeDetectors
{
    /// <summary>
    /// Smoke-detector placement tab. Inherits the shared device placement workflow
    /// (<see cref="DevicePlacementViewModelBase"/>); adds NFPA-72 detector-specific parameters. The actual
    /// placement backend is deferred (UI-first slice), so placement remains disabled via the base seam.
    /// </summary>
    public class SmokeDetectorViewModel : DevicePlacementViewModelBase
    {
        private string _detectorType = "Smoke";
        private string _ceilingMount = "Ceiling";
        private string _ceilingSlope = "Flat";

        public SmokeDetectorViewModel()
            : this(null)
        {
        }

        public SmokeDetectorViewModel(FireProtectionUiData data)
            : base(data)
        {
        }

        public override string DeviceDisplayName => "SMOKE DETECTOR CONFIGURATION";

        public IReadOnlyList<string> DetectorTypeOptions => new[] { "Smoke", "Heat", "Smoke/Heat" };
        public IReadOnlyList<string> CeilingMountOptions => new[] { "Ceiling", "Wall" };
        public IReadOnlyList<string> CeilingSlopeOptions => new[] { "Flat", "Sloped" };

        public string DetectorType
        {
            get => _detectorType;
            set => SetProperty(ref _detectorType, value);
        }

        public string CeilingMount
        {
            get => _ceilingMount;
            set => SetProperty(ref _ceilingMount, value);
        }

        public string CeilingSlope
        {
            get => _ceilingSlope;
            set => SetProperty(ref _ceilingSlope, value);
        }
    }
}
