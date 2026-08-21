using System.ComponentModel;
using System.Runtime.CompilerServices;
using FireProtection.UI.Models;

namespace FireProtection.UI.ViewModels.NotificationAppliances
{
    public class NotificationApplianceViewModel : INotifyPropertyChanged
    {
        public NotificationApplianceViewModel()
            : this(null)
        {
        }

        public NotificationApplianceViewModel(FireProtectionUiData data)
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