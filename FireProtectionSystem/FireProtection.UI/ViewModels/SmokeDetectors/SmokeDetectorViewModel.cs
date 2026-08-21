using System.ComponentModel;
using System.Runtime.CompilerServices;
using FireProtection.UI.Models;

namespace FireProtection.UI.ViewModels.SmokeDetectors
{
    public class SmokeDetectorViewModel : INotifyPropertyChanged
    {
        public SmokeDetectorViewModel()
            : this(null)
        {
        }

        public SmokeDetectorViewModel(FireProtectionUiData data)
        {
            Data = data;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public FireProtectionUiData Data { get; }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}