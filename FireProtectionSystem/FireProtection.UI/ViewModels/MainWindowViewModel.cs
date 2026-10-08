using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Newtonsoft.Json;
using FireProtection.UI.Models;
using FireProtection.UI.Services;
using FireProtection.UI.ViewModels.Catalog;
using FireProtection.UI.ViewModels.Common;
using FireProtection.UI.ViewModels.NotificationAppliances;
using FireProtection.UI.ViewModels.SmokeDetectors;
using FireProtection.UI.ViewModels.Sprinklers;

namespace FireProtection.UI.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        // Retained so the top-bar "Load families…" button can push a picked .rfa into the active
        // model. Document.LoadFamily is category-agnostic, so this one source loads sprinkler,
        // detector and notification-appliance families alike for all three device tabs.
        private readonly ISprinklerFamilySource _familySource;

        /// <summary>
        /// Retained only so <see cref="LoadFamiliesCore"/> can invalidate the eligibility cache after a
        /// successful load. Loading a family mutates the Document, which the cache key cannot observe
        /// (see ISprinklerPlacementService.ClearEligibilityCache).
        /// </summary>
        private readonly ISprinklerPlacementService _sprinklerPlacementService;

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
            : this(data, placementInputExporter, sprinklerFamilySource, sprinklerPlacementService, catalog, null)
        {
        }

        public MainWindowViewModel(
            FireProtectionUiData data,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource,
            ISprinklerPlacementService sprinklerPlacementService,
            CatalogViewModel catalog,
            DevicePlacementSeams deviceSeams)
        {
            Data = data;
            Catalog = catalog ?? new CatalogViewModel();
            Sprinkler = new SprinklerViewModel(data, placementInputExporter, sprinklerFamilySource, sprinklerPlacementService, Catalog);
            SmokeDetector = new SmokeDetectorViewModel(
                data, deviceSeams?.SmokeFamilySource, deviceSeams?.SmokeExecutor, Catalog);
            NotificationAppliance = new NotificationApplianceViewModel(
                data, deviceSeams?.NotificationFamilySource, deviceSeams?.NotificationExecutor, Catalog);

            _familySource = sprinklerFamilySource;
            LoadFamiliesCommand = new RelayCommand(_ => LoadFamilies());
        }

        public MainWindowViewModel(
            string json,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource,
            ISprinklerPlacementService sprinklerPlacementService,
            CatalogViewModel catalog)
            : this(DeserializeData(json), placementInputExporter, sprinklerFamilySource, sprinklerPlacementService, catalog, null)
        {
        }

        public MainWindowViewModel(
            string json,
            IPlacementInputExporter placementInputExporter,
            ISprinklerFamilySource sprinklerFamilySource,
            ISprinklerPlacementService sprinklerPlacementService,
            CatalogViewModel catalog,
            DevicePlacementSeams deviceSeams)
            : this(DeserializeData(json), placementInputExporter, sprinklerFamilySource, sprinklerPlacementService, catalog, deviceSeams)
        {
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

            _familySource = sprinklerFamilySource;
            _sprinklerPlacementService = sprinklerPlacementService;
            LoadFamiliesCommand = new RelayCommand(_ => LoadFamilies());
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public FireProtectionUiData Data { get; }

        public CatalogViewModel Catalog { get; }

        public SprinklerViewModel Sprinkler { get; }

        public SmokeDetectorViewModel SmokeDetector { get; }

        public NotificationApplianceViewModel NotificationAppliance { get; }

        /// <summary>Top-bar action: pick one or more .rfa files and load them into the active model.
        /// Shared by all three device tabs.</summary>
        public ICommand LoadFamiliesCommand { get; }

        // Document.LoadFamily is only legal in a Revit API context; a modeless window's WPF handlers are
        // not one, so the picker runs on the UI thread and only the load is marshalled onto Revit's idle
        // thread via RevitApi.Run. (In non-Revit hosts RevitApi.Run runs the work inline.)
        private void LoadFamilies()
        {
            if (_familySource == null)
            {
                Dialogs.Show("Loading families needs an active Revit document.",
                    "Load families", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            OpenFileDialog dlg = new OpenFileDialog
            {
                Title = "Select Revit families to load",
                Filter = "Revit families (*.rfa)|*.rfa|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;

            string[] files = dlg.FileNames;
RevitApi.Run(
                () =>
                {
                    string result = LoadFamiliesCore(files);
                    // WpfHost.RunOnUi instead of Application.Current.Dispatcher: in a Revit
                    // add-in Application.Current is null, so the direct call threw
                    // NullReferenceException.
                    WpfHost.RunOnUi(() => Dialogs.Show(
                        result, "Load families", MessageBoxButton.OK,
                        result.StartsWith("Could not be loaded:", StringComparison.Ordinal)
                            ? MessageBoxImage.Warning
                            : MessageBoxImage.Information));
                },
                ex => WpfHost.RunOnUi(() => Dialogs.Show(
                    "Loading families failed:\n\n" + (ex != null ? ex.Message : "unknown error"),
                    "Load families", MessageBoxButton.OK, MessageBoxImage.Error)));
        }

        private string LoadFamiliesCore(string[] files)
        {
            List<string> loaded = new List<string>();
            List<string> alreadyPresent = new List<string>();
            List<string> failures = new List<string>();

            foreach (string file in files)
            {
                string name = System.IO.Path.GetFileName(file);
                FamilyLoadOutcome outcome = _familySource.TryLoadFamily(file, out string error);
                switch (outcome)
                {
                    case FamilyLoadOutcome.Loaded: loaded.Add(name); break;
                    case FamilyLoadOutcome.AlreadyPresent: alreadyPresent.Add(name); break;
                    default: failures.Add(name + ": " + error); break;
                }

                // T1.3: the document just changed, so any cached eligibility result may now be wrong.
                // This is the one place the eligibility cache is explicitly invalidated, because
                // loading a family is exactly the kind of change the cache key cannot observe.
                if (outcome != FamilyLoadOutcome.Failed && _sprinklerPlacementService != null)
                {
                    _sprinklerPlacementService.ClearEligibilityCache();
                }
            }

            List<string> lines = new List<string>();
            if (loaded.Count > 0)
                lines.Add(loaded.Count + (loaded.Count == 1 ? " family" : " families") + " loaded into the model.");
            if (alreadyPresent.Count > 0)
                lines.Add((alreadyPresent.Count == 1 ? "This family is" : "These families are")
                    + " already in the model:\n  " + string.Join("\n  ", alreadyPresent));
            if (failures.Count > 0)
                lines.Add("Could not be loaded:\n  " + string.Join("\n  ", failures));
            if (lines.Count == 0)
                lines.Add("No families were selected.");

            return string.Join("\n\n", lines);
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
