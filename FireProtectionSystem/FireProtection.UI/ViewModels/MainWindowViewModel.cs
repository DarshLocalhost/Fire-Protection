using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using FireProtection.UI.Models;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.NotificationAppliances;
using FireProtection.UI.ViewModels.SmokeDetectors;
using FireProtection.UI.ViewModels.Sprinklers;

namespace FireProtection.UI.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private object _selectedTabViewModel;

        public MainWindowViewModel()
            : this((FireProtectionUiData)null, null, null)
        {
        }

        public MainWindowViewModel(string json)
            : this(DeserializeData(json), null, null)
        {
        }

        public MainWindowViewModel(string json, IPlacementExecutor placementExecutor)
            : this(DeserializeData(json), placementExecutor, null)
        {
        }

        public MainWindowViewModel(
            string json,
            IPlacementExecutor placementExecutor,
            ISprinklerFamilySource sprinklerFamilySource)
            : this(DeserializeData(json), placementExecutor, sprinklerFamilySource)
        {
        }

        public MainWindowViewModel(FireProtectionUiData data)
            : this(data, null, null)
        {
        }

        public MainWindowViewModel(FireProtectionUiData data, IPlacementExecutor placementExecutor)
            : this(data, placementExecutor, null)
        {
        }

        public MainWindowViewModel(
            FireProtectionUiData data,
            IPlacementExecutor placementExecutor,
            ISprinklerFamilySource sprinklerFamilySource)
        {
            Data = data;
            Sprinkler = new SprinklerViewModel(data, placementExecutor, sprinklerFamilySource);
            SmokeDetector = new SmokeDetectorViewModel(data);
            NotificationAppliance = new NotificationApplianceViewModel(data);
            _selectedTabViewModel = Sprinkler;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public FireProtectionUiData Data { get; }

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