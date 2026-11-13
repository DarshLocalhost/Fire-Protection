using System;
using System.Windows.Input;

namespace SPA.SynthFire.UI.ViewModels;

/// <summary>
/// A basic <see cref="ICommand"/> that delegates to an <see cref="Action"/>.
/// WPF-free (no CommandManager) so it lives in the net8.0 presentation layer;
/// callers that gate on <see cref="CanExecute"/> should raise
/// <see cref="RaiseCanExecuteChanged"/> when their gating state changes.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
