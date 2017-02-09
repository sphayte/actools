﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using JetBrains.Annotations;
using AcManager.Controls;
using AcManager.Controls.Dialogs;
using AcManager.Tools;
using AcManager.Tools.Helpers;
using AcManager.Tools.Helpers.AcLog;
using AcManager.Tools.Helpers.Api;
using AcManager.Tools.Lists;
using AcManager.Tools.Managers;
using AcManager.Tools.Objects;
using AcTools;
using AcTools.Processes;
using AcTools.Utils;
using AcTools.Utils.Helpers;
using FirstFloor.ModernUI;
using FirstFloor.ModernUI.Commands;
using FirstFloor.ModernUI.Dialogs;
using FirstFloor.ModernUI.Helpers;
using FirstFloor.ModernUI.Presentation;
using Newtonsoft.Json;
using Application = System.Windows.Application;
using WaitingDialog = FirstFloor.ModernUI.Dialogs.WaitingDialog;

namespace AcManager.Pages.Dialogs {
    public partial class CarUpdatePreviewsDialog : INotifyPropertyChanged, IProgress<Showroom.ShootingProgress>, IUserPresetable {
        #region Options

        private bool _disableSweetFx;

        public bool DisableSweetFx {
            get { return _disableSweetFx; }
            set {
                if (Equals(value, _disableSweetFx)) return;
                _disableSweetFx = value;
                OnPropertyChanged();
                SaveLater();
            }
        }

        private bool _disableWatermark;

        public bool DisableWatermark {
            get { return _disableWatermark; }
            set {
                if (Equals(value, _disableWatermark)) return;
                _disableWatermark = value;
                OnPropertyChanged();
                SaveLater();
            }
        }

        private bool _resizePreviews;

        public bool ResizePreviews {
            get { return _resizePreviews; }
            set {
                if (Equals(value, _resizePreviews)) return;
                _resizePreviews = value;
                OnPropertyChanged();
                SaveLater();
            }
        }

        private bool _maximizeVideoSettings;

        public bool MaximizeVideoSettings {
            get { return _maximizeVideoSettings; }
            set {
                if (Equals(value, _maximizeVideoSettings)) return;
                _maximizeVideoSettings = value;
                OnPropertyChanged();
            }
        }

        private bool _enableFxaa;

        public bool EnableFxaa {
            get { return _enableFxaa; }
            set {
                if (Equals(value, _enableFxaa)) return;
                _enableFxaa = value;
                OnPropertyChanged();
            }
        }

        private bool _useSpecialResolution;

        public bool UseSpecialResolution {
            get { return _useSpecialResolution; }
            set {
                if (Equals(value, _useSpecialResolution)) return;
                _useSpecialResolution = value;
                OnPropertyChanged();
            }
        }

        private string _cameraPosition;

        public string CameraPosition {
            get { return _cameraPosition; }
            set {
                if (Equals(value, _cameraPosition)) return;
                _cameraPosition = value;
                OnPropertyChanged();
                SaveLater();
            }
        }

        private string _cameraLookAt;

        public string CameraLookAt {
            get { return _cameraLookAt; }
            set {
                if (Equals(value, _cameraLookAt)) return;
                _cameraLookAt = value;
                OnPropertyChanged();
                SaveLater();
            }
        }

        private double _cameraFov;

        public double CameraFov {
            get { return _cameraFov; }
            set {
                if (Equals(value, _cameraFov)) return;
                _cameraFov = value;
                OnPropertyChanged();
                SaveLater();
            }
        }

        private double? _cameraExposure;

        public double? CameraExposure {
            get { return _cameraExposure; }
            set {
                if (Equals(value, _cameraExposure)) return;
                _cameraExposure = value;
                OnPropertyChanged();
                SaveLater();
            }
        }

        private ShowroomObject _selectedShowroom;

        public ShowroomObject SelectedShowroom {
            get { return _selectedShowroom; }
            set {
                if (Equals(value, _selectedShowroom)) return;
                var update = _selectedShowroom == null || value == null;
                _selectedShowroom = value;
                OnPropertyChanged();
                SaveLater();

                if (!update) return;
                foreach (var command in Buttons.Select(x => x.Command).OfType<CommandBase>().NonNull()) {
                    command.RaiseCanExecuteChanged();
                }
            }
        }

        public static SettingEntry[] Modes { get; } = {
            new SettingEntry("Default", "Default Mode (Using Original Showroom)"),
            new SettingEntry("GT5-like", "Style similar to Gran Turismo 5"),
        };

        public AcEnabledOnlyCollection<ShowroomObject> Showrooms => ShowroomsManager.Instance.EnabledOnlyCollection;

        private IWithId _selectedFilter;

        public IWithId SelectedFilter {
            get { return _selectedFilter; }
            set {
                if (Equals(value, _selectedFilter)) return;
                var update = _selectedFilter == null || value == null;
                _selectedFilter = value;
                OnPropertyChanged();
                SaveLater();

                if (!update) return;
                foreach (var command in Buttons.Select(x => x.Command).OfType<CommandBase>().NonNull()) {
                    command.RaiseCanExecuteChanged();
                }
            }
        }

        public BuiltInPpFilter DefaultPpFilter { get; } = new BuiltInPpFilter {
            Name = @"AT-Previews Special",
            Filename = @"AT-Previews Special.ini",
            Content = Properties.BinaryResources.PpFilterAtPreviewsSpecial
        };

        public class BuiltInPpFilter : NotifyPropertyChanged, IWithId {
            public string Filename;
            public byte[] Content;

            private string _id, _name;

            public string Id => _id ?? (_id = Filename.ToLower());

            public string Name {
                get {
                    return _name ?? (_name = Path.GetFileNameWithoutExtension(Filename));
                }
                set {
                    if (Equals(value, _name)) return;
                    _name = value;
                    OnPropertyChanged();
                }
            }

            public override string ToString() {
                return Name;
            }

            public void EnsureInstalled() {
                var destination = Path.Combine(FileUtils.GetPpFiltersDirectory(AcRootDirectory.Instance.Value), Filename);
                if (File.Exists(destination) && new FileInfo(destination).Length == Content.Length) return;

                // don’t ignore changes because why? list will be updated, but it’s not a bad thing
                File.WriteAllBytes(destination, Content);
            }
        }

        private IReadOnlyList<IWithId> _filters;

        public IReadOnlyList<IWithId> Filters {
            get {
                PpFiltersManager.Instance.EnsureLoaded();
                return _filters ?? (_filters = new ObservableCollection<IWithId>(
                        PpFiltersManager.Instance.EnabledOnly.Where(x => DefaultPpFilter.Id != x.Id)
                                        .Cast<IWithId>().Prepend(DefaultPpFilter)));
            }
        }
        #endregion

        #region Shooting
        private AsyncProgressEntry _seriesProgress;

        public AsyncProgressEntry SeriesProgress {
            get { return _seriesProgress; }
            set {
                if (Equals(value, _seriesProgress)) return;
                _seriesProgress = value;
                OnPropertyChanged();
            }
        }

        private AsyncProgressEntry _progress;

        public AsyncProgressEntry Progress {
            get { return _progress; }
            set {
                if (Equals(value, _progress)) return;
                _progress = value;
                OnPropertyChanged();
            }
        }

        private string _errorMessage;

        public string ErrorMessage {
            get { return _errorMessage; }
            set {
                if (Equals(value, _errorMessage)) return;
                _errorMessage = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<ResultPreviewComparison> _resultPreviewComparisons;

        public ObservableCollection<ResultPreviewComparison> ResultPreviewComparisons {
            get { return _resultPreviewComparisons; }
            set {
                if (Equals(value, _resultPreviewComparisons)) return;
                _resultPreviewComparisons = value;
                OnPropertyChanged();

                ResultPreviewComparisonsView = new CollectionView(value);
            }
        }

        private CollectionView _resultPreviewComparisonsView;

        public CollectionView ResultPreviewComparisonsView {
            get { return _resultPreviewComparisonsView; }
            set {
                if (Equals(value, _resultPreviewComparisonsView)) return;
                _resultPreviewComparisonsView = value;
                OnPropertyChanged();
            }
        }
        #endregion

        private class SaveableData {
            public string ShowroomId, FilterId;
            public string CameraPosition, CameraLookAt;
            public double CameraFov;
            public double? CameraExposure;

            [DefaultValue(true), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
            public bool DisableSweetFx;

            [DefaultValue(true), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
            public bool DisableWatermark;

            [DefaultValue(true), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
            public bool ResizePreviews;

            [DefaultValue(true), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
            public bool MaximizeVideoSettings;

            [DefaultValue(true), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
            public bool EnableFxaa;

            [DefaultValue(true), JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
            public bool UseSpecialResolution;
        }

        private void SaveLater() {
            _saveable.SaveLater();
            Changed?.Invoke(this, new EventArgs());
        }

        private readonly ISaveHelper _saveable;

        private void ShowroomMessage([Localizable(false)] string showroomName, [Localizable(false)] string showroomId, [Localizable(false)] string informationUrl) {
            if (ShowMessage(string.Format(AppStrings.CarPreviews_ShowroomIsMissing, informationUrl, showroomName),
                    AppStrings.CarPreviews_ShowroomIsMissing_Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            InstallShowroom(showroomName, showroomId).Forget();
        }

        private async Task InstallShowroom(string showroomName, string showroomId) {
            _mode = DialogMode.Options;
            await Task.Delay(100);

            using (var dialog = new WaitingDialog(showroomName)) {
                dialog.Report(ControlsStrings.Common_Downloading);

                var destination = FileUtils.GetShowroomsDirectory(AcRootDirectory.Instance.Value);
                var data = await CmApiProvider.GetDataAsync($"static/get/{showroomId}", dialog, dialog.CancellationToken);

                if (data == null) {
                    dialog.Close();
                    NonfatalError.Notify(string.Format(AppStrings.CarPreviews_CannotDownloadShowroom, showroomName), ToolsStrings.Common_CannotDownloadFile_Commentary);
                    return;
                }

                dialog.Content = ControlsStrings.Common_Installing;
                try {
                    await Task.Run(() => {
                        using (var stream = new MemoryStream(data, false))
                        using (var archive = new ZipArchive(stream)) {
                            archive.ExtractToDirectory(destination);
                        }
                    });
                } catch (Exception e) {
                    dialog.Close();
                    NonfatalError.Notify(string.Format(AppStrings.CarPreviews_CannotInstallShowroom, showroomName), e);
                    return;
                }

                await Task.Delay(1000);
                SelectedShowroom = ShowroomsManager.Instance.GetById(showroomId) ?? SelectedShowroom;
            }
        }

        public enum DialogMode {
            Options, Start, StartManual
        }

        private readonly string _loadPreset;

        [NotNull]
        private readonly List<ToUpdate> _toUpdate;

        public class ToUpdate {
            [NotNull]
            public CarObject Car { get; }

            [CanBeNull]
            public string[] SkinIds { get; }

            public ToUpdate([NotNull] CarObject car, [CanBeNull] string[] skinIds) {
                Car = car;
                SkinIds = skinIds;
            }
        }

        private bool _applyImmediately;

        public bool ApplyImmediately {
            get { return _applyImmediately; }
            set {
                if (Equals(value, _applyImmediately)) return;
                _applyImmediately = value;
                OnPropertyChanged();
            }
        }

        public CarUpdatePreviewsDialog(CarObject carObject, [CanBeNull] string[] skinIds, DialogMode mode, string loadPreset = null)
                : this(new List<ToUpdate> { new ToUpdate(carObject, skinIds) }, mode, loadPreset) {}

        public CarUpdatePreviewsDialog([NotNull] List<ToUpdate> toUpdate, DialogMode mode, string loadPreset = null, bool applyImmediately = false) {
            if (toUpdate == null) throw new ArgumentNullException(nameof(toUpdate));
            if (toUpdate.Count == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(toUpdate));

            ApplyImmediately = applyImmediately;

            _toUpdate = toUpdate;
            _mode = mode;
            _loadPreset = loadPreset;

            _saveable = new SaveHelper<SaveableData>("__AutoUpdatePreviews", () => IsVisible ? new SaveableData {
                ShowroomId = SelectedShowroom?.Id,
                FilterId = SelectedFilter?.Id,
                CameraPosition = CameraPosition,
                CameraLookAt = CameraLookAt,
                CameraFov = CameraFov,
                CameraExposure = CameraExposure,
                DisableSweetFx = DisableSweetFx,
                DisableWatermark = DisableWatermark,
                ResizePreviews = ResizePreviews,
                MaximizeVideoSettings = MaximizeVideoSettings,
                EnableFxaa = EnableFxaa,
                UseSpecialResolution = UseSpecialResolution
            } : null, o => {
                if (o.ShowroomId != null) {
                    var showroom = ShowroomsManager.Instance.GetById(o.ShowroomId);
                    SelectedShowroom = showroom ?? SelectedShowroom;
                    if (showroom == null) {
                        switch (o.ShowroomId) {
                            case "at_studio_black":
                                ShowroomMessage("Studio Black Showroom (AT Previews Special)", "at_studio_black",
                                        "http://www.racedepartment.com/downloads/studio-black-showroom.4353/");
                                break;

                            case "at_previews":
                                ShowroomMessage("Kunos Previews Showroom (AT Previews Special)", "at_previews",
                                        "http://www.assettocorsa.net/assetto-corsa-v1-5-dev-diary-part-33/");
                                break;
                        }
                    }
                }

                if (o.FilterId != null) {
                    SelectedFilter = Filters.GetByIdOrDefault(o.FilterId) ?? SelectedFilter;
                } else {
                    SelectedFilter = DefaultPpFilter;
                }

                CameraPosition = o.CameraPosition;
                CameraLookAt = o.CameraLookAt;
                CameraFov = o.CameraFov;
                CameraExposure = o.CameraExposure == 0 ? null : o.CameraExposure;
                DisableWatermark = o.DisableWatermark;
                DisableSweetFx = o.DisableSweetFx;
                ResizePreviews = o.ResizePreviews;
                MaximizeVideoSettings = o.MaximizeVideoSettings;
                EnableFxaa = o.EnableFxaa;
                UseSpecialResolution = o.UseSpecialResolution;
            }, () => {
                SelectedShowroom = null;
                SelectedFilter = (IWithId)PpFiltersManager.Instance.GetDefault() ?? DefaultPpFilter;
                CameraPosition = @"-3.867643, 1.423590, 4.70381";
                CameraLookAt = @"0.0, 0.7, 0.5";
                CameraFov = 30;
                CameraExposure = 94.5;
                DisableWatermark = true;
                DisableSweetFx = true;
                ResizePreviews = true;
                MaximizeVideoSettings = true;
                EnableFxaa = true;
                UseSpecialResolution = true;
            });

            InitializeComponent();
            DataContext = this;
        }

        public CarUpdatePreviewsDialog(CarObject carObject, DialogMode mode, string loadPreset = null)
            : this(carObject, null, mode, loadPreset) { }

        /*public static void Run(CarObject carObject, string[] skinIds, string presetFilename) {
            new CarUpdatePreviewsDialog(carObject, skinIds, DialogMode.Start).ShowDialog();
        }

        public static void RunPreset(CarObject carObject, string[] skinIds, string presetFilename) {
            new CarUpdatePreviewsDialog(carObject, skinIds, DialogMode.Start, presetFilename).ShowDialog();
        }*/

        private void OnLoaded(object sender, RoutedEventArgs e) {
            if (_loadPreset == null) {
                if (_saveable.HasSavedData || UserPresetsControl.CurrentUserPreset != null) {
                    _saveable.Initialize();
                } else {
                    _saveable.Reset();
                    UserPresetsControl.CurrentUserPreset =
                            UserPresetsControl.SavedPresets.FirstOrDefault(x => x.ToString() == @"Kunos");
                }
            } else {
                _saveable.Reset();
                UserPresetsControl.CurrentUserPreset =
                        UserPresetsControl.SavedPresets.FirstOrDefault(x => x.Filename == _loadPreset);
            }

            if (_mode == DialogMode.Options) {
                SelectPhase(Phase.Options);
            } else {
                RunShootingProcess(_mode == DialogMode.StartManual).Forget();
            }
        }

        private DialogMode _mode;
        private bool _cancelled;

        public new bool ShowDialog() {
            if (_cancelled) return false;
            base.ShowDialog();
            return CurrentPhase == Phase.Result;
        }

        public bool CanBeSaved => SelectedShowroom != null && SelectedFilter != null;

        public const string PresetableKeyValue = "Previews";

        public string PresetableKey => PresetableKeyValue;

        string IUserPresetable.PresetableCategory => PresetableKeyValue;

        string IUserPresetable.DefaultPreset => null;

        public string ExportToPresetData() {
            return _saveable.ToSerializedString();
        }

        public event EventHandler Changed;

        public void ImportFromPresetData(string data) {
            _saveable.FromSerializedString(data);
        }

        private void OnClosing(object sender, CancelEventArgs args) {
            if (CurrentPhase == Phase.Waiting) {
                _cancellationTokenSource.Cancel(false);
            } else if (CurrentPhase == Phase.Result && IsResultOk) {
                Apply().Forget();
            }
        }

        private async Task Apply() {
            if (_resultDirectory == null) return;

            using (var waiting = new WaitingDialog {
                Owner = Application.Current?.MainWindow
            }) {
                try {
                    var car = _toUpdate[0].Car;
                    await ImageUtils.ApplyPreviewsAsync(AcRootDirectory.Instance.RequireValue, car.Id, _resultDirectory, ResizePreviews,
                            new AcPreviewImageInformation {
                                Name = car.DisplayName,
                                Style = Path.GetFileNameWithoutExtension(UserPresetsControl.SelectedPresetFilename)
                            }, waiting, waiting.CancellationToken);
                } catch (Exception e) {
                    NonfatalError.Notify(AppStrings.CarPreviews_CannotSave, e);
                }
            }
        }

        public enum Phase {
            Options, Waiting, Result, ResultSummary, Error
        }

        private Phase _currentPhase;

        public Phase CurrentPhase {
            get { return _currentPhase; }
            set {
                if (Equals(value, _currentPhase)) return;
                _currentPhase = value;
                OnPropertyChanged();
            }
        }

        private const string KeySize = "_CarUpdatePreviewsDialog.Size";

        private void Resize(double? width, double? height, bool resizeable) {
            var oldWidth = Width;
            var oldHeight = Height;
            var area = Screen.PrimaryScreen.WorkingArea;

            if (width.HasValue && height.HasValue) {
                double w, h;

                if (resizeable) {
                    var p = ValuesStorage.GetPoint(KeySize, new Point(width.Value, height.Value));
                    w = Math.Min(p.X, area.Width);
                    h = Math.Min(p.Y, area.Height);
                } else {
                    w = Math.Min(width.Value, area.Width);
                    h = Math.Min(height.Value, area.Height);
                }

                Width = w;
                Height = h;
                MinWidth = Math.Min(width.Value, area.Width);
                MinHeight = Math.Min(height.Value, area.Height);
                SizeToContent = SizeToContent.Manual;
                ResizeMode = resizeable ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
            } else {
                MinWidth = 240d;
                MinHeight = 120d;
                SizeToContent = SizeToContent.WidthAndHeight;
                ResizeMode = ResizeMode.NoResize;
            }

            Left = Math.Max(area.Left, Left + (oldWidth - Width) / 2d);
            Top = Math.Max(area.Top, Top + (oldHeight - Height) / 2d);
        }

        protected override void OnSizeChanged(object sender, SizeChangedEventArgs e) {
            base.OnSizeChanged(sender, e);

            if (ResizeMode == ResizeMode.CanResizeWithGrip) {
                ValuesStorage.Set(KeySize, new Point(Width, Height));
            }
        }

        private void SelectPhase(Phase phase, string errorMessage = null, WhatsGoingOn whatsGoingOn = null) {
            CurrentPhase = phase;

            switch (phase) {
                case Phase.Options:
                    Resize(540d, 460d, false);
                    var manual = CreateExtraDialogButton(AppStrings.CarPreviews_Manual,
                            new AsyncCommand(() => RunShootingProcess(true), () => CanBeSaved).ListenOnWeak(this, nameof(CanBeSaved)));
                    manual.ToolTip = AppStrings.CarPreviews_Manual_Tooltip;
                    Buttons = new[] {
                        CreateExtraStyledDialogButton("Go.Button", AppStrings.Common_Go,
                                new AsyncCommand(() => RunShootingProcess(), () => CanBeSaved).ListenOnWeak(this, nameof(CanBeSaved))),
                        ApplyImmediately ? null : manual,
                        CloseButton
                    };
                    break;

                case Phase.Waiting:
                    Resize(540d, 400d, false);
                    Buttons = new[] { CancelButton };
                    break;

                case Phase.Result:
                    Resize(540d, 400d, true);
                    Buttons = new[] { OkButton, CancelButton };
                    break;

                case Phase.ResultSummary:
                    Resize(540d, Errors.Count > 0 ? 600d : 400d, true);
                    Buttons = new[] { OkButton };
                    break;

                case Phase.Error:
                    Resize(null, null, false);
                    ErrorMessage = (whatsGoingOn?.GetDescription() ?? errorMessage)?.ToSentence();
                    if (whatsGoingOn?.Fix != null) {
                        Buttons = new[] {
                            this.CreateFixItButton(whatsGoingOn.Solution),
                            OkButton
                        };
                    } else {
                        Buttons = new[] { OkButton };
                    }

                    break;
            }
        }

        public class ResultPreviewComparison : NotifyPropertyChanged {
            public string Name { get; set; }

            public string LiveryImage { get; set; }

            public string OriginalImage { get; set; }

            public string UpdatedImage { get; set; }
        }

        private CancellationTokenSource _cancellationTokenSource;

        [CanBeNull]
        private string _resultDirectory;

        public class ErrorDescription {
            public ErrorDescription(ToUpdate toUpdate, string message, WhatsGoingOn whatsGoingOn) {
                ToUpdate = toUpdate;
                Message = message;
                WhatsGoingOn = whatsGoingOn;
            }

            public ToUpdate ToUpdate { get; }

            public string Message { get; }

            public WhatsGoingOn WhatsGoingOn { get; }
        }

        public BetterObservableCollection<ErrorDescription> Errors { get; } = new BetterObservableCollection<ErrorDescription>();

        private async Task ShootCar([NotNull] ToUpdate toUpdate, string filterId, bool manualMode, bool applyImmediately, CancellationToken cancellation) {
            if (toUpdate == null) throw new ArgumentNullException(nameof(toUpdate));

            try {
                _currentCar = toUpdate.Car;
                _resultDirectory = await Showroom.ShotAsync(new Showroom.ShotProperties {
                    AcRoot = AcRootDirectory.Instance.Value,
                    CarId = toUpdate.Car.Id,
                    ShowroomId = SelectedShowroom.Id,
                    SkinIds = toUpdate.SkinIds,
                    Filter = filterId,
                    Fxaa = EnableFxaa,
                    SpecialResolution = UseSpecialResolution,
                    MaximizeVideoSettings = MaximizeVideoSettings,
                    Mode = manualMode ? Showroom.ShotMode.ClassicManual : Showroom.ShotMode.Fixed,
                    UseBmp = true,
                    DisableWatermark = DisableWatermark,
                    DisableSweetFx = DisableSweetFx,
                    ClassicCameraDx = 0.0,
                    ClassicCameraDy = 0.0,
                    ClassicCameraDistance = 5.5,
                    FixedCameraPosition = CameraPosition,
                    FixedCameraLookAt = CameraLookAt,
                    FixedCameraFov = CameraFov,
                    FixedCameraExposure = CameraExposure ?? 0d,
                }, this, cancellation);
                if (cancellation.IsCancellationRequested) return;
            } catch (ProcessExitedException e) when (applyImmediately) {
                Errors.Add(new ErrorDescription(toUpdate, e.Message, AcLogHelper.TryToDetermineWhatsGoingOn())); 
                return;
            }

            if (applyImmediately) {
                if (_resultDirectory == null) {
                    Errors.Add(new ErrorDescription(toUpdate, AppStrings.CarPreviews_SomethingWentWrong, AcLogHelper.TryToDetermineWhatsGoingOn()));
                } else {
                    Progress = AsyncProgressEntry.Indetermitate;
                    await ImageUtils.ApplyPreviewsAsync(AcRootDirectory.Instance.RequireValue, toUpdate.Car.Id, _resultDirectory, ResizePreviews,
                            new AcPreviewImageInformation {
                                Name = toUpdate.Car.DisplayName,
                                Style = Path.GetFileNameWithoutExtension(UserPresetsControl.SelectedPresetFilename)
                            }, new Progress<Tuple<string, double?>>(t => {
                                Progress = new AsyncProgressEntry($"Applying freshly made previews ({t.Item1})…", t.Item2);
                            }), cancellation);
                }
            } else {
                if (_resultDirectory == null) {
                    SelectPhase(Phase.Error, AppStrings.CarPreviews_SomethingWentWrong, AcLogHelper.TryToDetermineWhatsGoingOn());
                    return;
                }

                ResultPreviewComparisons = new ObservableCollection<ResultPreviewComparison>(
                        Directory.GetFiles(_resultDirectory, "*.*").Select(x => {
                            var id = (Path.GetFileNameWithoutExtension(x) ?? "").ToLower();
                            return new ResultPreviewComparison {
                                Name = toUpdate.Car.GetSkinById(id)?.DisplayName ?? id,

                                /* custom paths, because theoretically skin might no longer exist at this point */
                                LiveryImage = Path.Combine(toUpdate.Car.Location, @"skins", id, @"livery.png"),
                                OriginalImage = Path.Combine(toUpdate.Car.Location, @"skins", id, @"preview.jpg"),
                                UpdatedImage = x
                            };
                        }));
            }
        }

        private async Task RunShootingProcess(bool manualMode = false) {
            if (SelectedShowroom == null) {
                if (ShowMessage(AppStrings.CarPreviews_ShowroomIsMissingOptions, AppStrings.Common_OneMoreThing, MessageBoxButton.YesNo) ==
                    MessageBoxResult.Yes) {
                    SelectPhase(Phase.Options);
                } else {
                    _cancelled = true;
                    Top = -9999;
                    await Task.Delay(1);
                    Close();
                }
                return;
            }

            if (SelectedFilter == null) {
                if (ShowMessage(AppStrings.CarPreviews_FilterIsMissingOptions, AppStrings.Common_OneMoreThing, MessageBoxButton.YesNo) ==
                    MessageBoxResult.Yes) {
                    SelectPhase(Phase.Options);
                } else {
                    _cancelled = true;
                    Top = -9999;
                    await Task.Delay(1);
                    Close();
                }
                return;
            }

            if (_toUpdate.Any(u => !u.Car.Enabled ||
                    u.SkinIds?.Any(x => u.Car.SkinsManager.GetWrapperById(x)?.Value.Enabled == false) == true)) {
                SelectPhase(Phase.Error, AppStrings.CarPreviews_CannotUpdateForDisabled);
                return;
            }

            Progress = AsyncProgressEntry.FromStringIndetermitate(UiStrings.Common_PleaseWait);
            SelectPhase(Phase.Waiting);

            _cancellationTokenSource = new CancellationTokenSource();

            try {
                string filterId;

                var builtInPpFilter = SelectedFilter as BuiltInPpFilter;
                if (builtInPpFilter != null) {
                    builtInPpFilter.EnsureInstalled();
                    filterId = builtInPpFilter.Name;
                } else {
                    var filterObject = SelectedFilter as PpFilterObject;
                    filterId = filterObject?.Name;
                }

                var begin = DateTime.Now;

                if (_toUpdate.Count > 1 && !ApplyImmediately) {
                    throw new Exception("Can’t apply previews later if there are more than one car");
                }
                
                for (var i = 0; i < _toUpdate.Count; i++) {
                    var toUpdate = _toUpdate[i];
                    SeriesProgress = new AsyncProgressEntry(toUpdate.Car.DisplayName, i, _toUpdate.Count);

                    await ShootCar(toUpdate, filterId, manualMode, ApplyImmediately, _cancellationTokenSource.Token);
                    if (_cancellationTokenSource.IsCancellationRequested) {
                        SelectPhase(Phase.Error, AppStrings.CarPreviews_CancelledMessage);
                        return;
                    }
                }

                TakenTime = DateTime.Now - begin;
                SelectPhase(ApplyImmediately ? Phase.ResultSummary : Phase.Result);
            } catch (ShotingCancelledException e) {
                SelectPhase(Phase.Error, e.UserCancelled ? AppStrings.CarPreviews_CancelledMessage : e.Message);

                if (!e.UserCancelled) {
                    Logging.Warning("Cannot update previews: " + e);
                }
            } catch (ProcessExitedException e) {
                SelectPhase(Phase.Error, e.Message, AcLogHelper.TryToDetermineWhatsGoingOn());
                Logging.Warning("Cannot update previews: " + e);
            } catch (Exception e) {
                SelectPhase(Phase.Error, e.Message);
                Logging.Warning("Cannot update previews: " + e);
            }
        }

        public string DisplayTakenTime => TakenTime.ToReadableTime();

        private TimeSpan _takenTime;

        public TimeSpan TakenTime {
            get { return _takenTime; }
            set {
                if (Equals(value, _takenTime)) return;
                _takenTime = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayTakenTime));
            }
        }

        private CarObject _currentCar;

        public void Report(Showroom.ShootingProgress value) {
            Progress = new AsyncProgressEntry(
                string.Format(AppStrings.CarPreviews_Progress, _currentCar?.GetSkinById(value.SkinId)?.DisplayName ?? value.SkinId, value.SkinNumber + 1,
                    value.TotalSkins),
                value.SkinNumber, value.TotalSkins);
        }

        private void ImageViewer(bool showUpdated) {
            var current = (ResultPreviewComparison)ResultPreviewComparisonsView.CurrentItem;
            new ImageViewer(new[] { current.OriginalImage, current.UpdatedImage }, showUpdated ? 1 : 0, CommonAcConsts.PreviewWidth).ShowDialog();
        }

        private void OriginalPreview_OnMouseDown(object sender, MouseButtonEventArgs e) {
            ImageViewer(false);
        }

        private void UpdatedPreview_OnMouseDown(object sender, MouseButtonEventArgs e) {
            ImageViewer(true);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
