using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using FireProtection.UI.Models;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Catalog;
using FireProtection.UI.ViewModels.NotificationAppliances;
using FireProtection.UI.ViewModels.SmokeDetectors;
using FireProtection.UI.ViewModels.Sprinklers;

namespace FireProtection.UI.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private object _selectedTabViewModel;

        public MainWindowViewModel()
            : this((FireProtectionUiData)null, null, null, null)
        {
        }

        public MainWindowViewModel(string json)
            : this(DeserializeData(json), null, null, null)
        {
        }

        public MainWindowViewModel(
            string json,
            ISprinklerFamilySource sprinklerFamilySource)
            : this(DeserializeData(json), null, sprinklerFamilySource, null)
        {
        }

        public MainWindowViewModel(
            string json,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource)
            : this(DeserializeData(json), placementInputExporter, sprinklerFamilySource, null)
        {
        }

        public MainWindowViewModel(FireProtectionUiData data)
            : this(data, null, null, null)
        {
        }

        public MainWindowViewModel(
            FireProtectionUiData data,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource)
            : this(data, placementInputExporter, sprinklerFamilySource, null)
        {
        }

        public MainWindowViewModel(
            FireProtectionUiData data,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource,
            ISprinklerPlacementService sprinklerPlacementService)
            : this(data, placementInputExporter, sprinklerFamilySource, sprinklerPlacementService, null)
        {
        }

        public MainWindowViewModel(
            FireProtectionUiData data,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource,
            ISprinklerPlacementService sprinklerPlacementService,
            CatalogViewModel catalog)
        {
            Data = data;
            Catalog = catalog ?? new CatalogViewModel();
            Sprinkler = new SprinklerViewModel(data, placementInputExporter, sprinklerFamilySource, sprinklerPlacementService, Catalog);
            SmokeDetector = new SmokeDetectorViewModel(data, Catalog);
            NotificationAppliance = new NotificationApplianceViewModel(data, Catalog);
            _selectedTabViewModel = Sprinkler;
        }

        public MainWindowViewModel(
            string json,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource,
            ISprinklerPlacementService sprinklerPlacementService,
            CatalogViewModel catalog)
        {
            FireProtectionUiData data = DeserializeData(json);
            Data = data;
            Catalog = catalog ?? new CatalogViewModel();
            Sprinkler = new SprinklerViewModel(data, placementInputExporter, sprinklerFamilySource, sprinklerPlacementService, Catalog);
            SmokeDetector = new SmokeDetectorViewModel(data, Catalog);
            NotificationAppliance = new NotificationApplianceViewModel(data, Catalog);
            _selectedTabViewModel = Sprinkler;
        }

        public MainWindowViewModel(
            string json,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource,
            ISprinklerPlacementService sprinklerPlacementService)
        {
            FireProtectionUiData data = DeserializeData(json);
            Data = data;
            Catalog = new CatalogViewModel();
            Sprinkler = new SprinklerViewModel(data, placementInputExporter, sprinklerFamilySource, sprinklerPlacementService, Catalog);
            SmokeDetector = new SmokeDetectorViewModel(data, Catalog);
            NotificationAppliance = new NotificationApplianceViewModel(data, Catalog);
            _selectedTabViewModel = Sprinkler;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public FireProtectionUiData Data { get; }

        public CatalogViewModel Catalog { get; }

        public SprinklerViewModel Sprinkler { get; }

        public SmokeDetectorViewModel SmokeDetector { get; }

        public NotificationApplianceViewModel NotificationAppliance { get; }

        public object SelectedTabViewModel
        {
            get => _selectedTabViewModel;
            set => SetProperty(ref _selectedTabViewModel, value);
        }

        private static FireProtectionUiData DeserializeData(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonConvert.DeserializeObject<FireProtectionUiData>(json);
        }

        protected bool SetProperty<T>(
            ref T storage,
            T value,
            [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}