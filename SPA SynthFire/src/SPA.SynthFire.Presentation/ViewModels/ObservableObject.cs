using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SPA.SynthFire.UI.ViewModels;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> base for view models. Kept WPF-free
/// so the presentation layer builds on plain net8.0 and stays unit-testable.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Assigns <paramref name="field"/> and raises change notification when the value differs.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
