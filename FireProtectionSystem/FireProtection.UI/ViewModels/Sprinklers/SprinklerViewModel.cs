using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FireProtection.UI.Models;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Sprinklers.BruteForce;
using FireProtection.UI.ViewModels.Sprinklers.Collision;

namespace FireProtection.UI.ViewModels.Sprinklers
{
    public class SprinklerViewModel : INotifyPropertyChanged
    {
        private object _selectedSubTabViewModel;

        public SprinklerViewModel()
            : this(null, null, null)
        {
        }

        public SprinklerViewModel(FireProtectionUiData data)
            : this(data, null, null)
        {
        }

        public SprinklerViewModel(FireProtectionUiData data, IPlacementExecutor placementExecutor)
            : this(data, placementExecutor, null)
        {
        }

        public SprinklerViewModel(
            FireProtectionUiData data,
            IPlacementExecutor placementExecutor,
            ISprinklerFamilySource sprinklerFamilySource)
        {
            Data = data;

            Collision = new SprinklerCollisionViewModel(data);
            BruteForce = new SprinklerBruteForceViewModel(data, placementExecutor, sprinklerFamilySource);

            SubTabViewModels = new ObservableCollection<object>
            {
                Collision,
                BruteForce
            };

            _selectedSubTabViewModel = BruteForce;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public FireProtectionUiData Data { get; }
        public SprinklerCollisionViewModel Collision { get; }
        public SprinklerBruteForceViewModel BruteForce { get; }
        public ObservableCollection<object> SubTabViewModels { get; }

        public object SelectedSubTabViewModel
        {
            get => _selectedSubTabViewModel;
            set => SetProperty(ref _selectedSubTabViewModel, value);
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