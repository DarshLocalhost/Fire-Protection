using System;
using System.IO;
using System.Reflection;

namespace SPA.SynthFire.UI.Helpers;

/// <summary>
/// Resolves third-party assemblies (such as HelixToolkit.Wpf) from the add-in output
/// directory when WPF XAML parsing executes inside Revit's AppDomain.
/// </summary>
public static class AssemblyResolver
{
    private static bool _isRegistered;
    private static readonly object _lock = new();

    public static void EnsureRegistered()
    {
        if (_isRegistered) return;
        lock (_lock)
        {
            if (_isRegistered) return;
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            _isRegistered = true;
        }
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        try
        {
            var assemblyName = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(assemblyName)) return null;

            // 1. Probe the directory where SPA.SynthFire.UI.dll is installed
            string uiLocation = typeof(AssemblyResolver).Assembly.Location;
            string? uiDir = Path.GetDirectoryName(uiLocation);

            if (!string.IsNullOrEmpty(uiDir))
            {
                string targetDll = Path.Combine(uiDir, $"{assemblyName}.dll");
                if (File.Exists(targetDll))
                {
                    return Assembly.LoadFrom(targetDll);
                }
            }

            // 2. Probe AppDomain BaseDirectory
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string fallbackDll = Path.Combine(baseDir, $"{assemblyName}.dll");
            if (File.Exists(fallbackDll))
            {
                return Assembly.LoadFrom(fallbackDll);
            }
        }
        catch
        {
            // Fall back to default resolution mechanism on failure
        }
        return null;
    }
}