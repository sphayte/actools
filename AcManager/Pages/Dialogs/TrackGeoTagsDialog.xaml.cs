﻿using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Windows.Input;
using AcManager.Controls.UserControls.Web;
using AcManager.Tools;
using AcManager.Tools.Helpers.Api;
using AcManager.Tools.Objects;
using AcTools.Utils.Helpers;
using FirstFloor.ModernUI.Commands;
using FirstFloor.ModernUI.Presentation;
using JetBrains.Annotations;

namespace AcManager.Pages.Dialogs {
    public partial class TrackGeoTagsDialog {
        private ViewModel Model => (ViewModel)DataContext;

        public TrackGeoTagsDialog(TrackObjectBase track) {
            DataContext = new ViewModel(track);
            InitializeComponent();

            Buttons = new[] {
                CreateExtraDialogButton(ToolsStrings.TrackGeoTags_FindIt, new DelegateCommand(() => {
                    MapWebBrowser.MainTab?.Execute(@"moveTo", GetQuery(Model.Track));
                })),
                CreateExtraDialogButton(FirstFloor.ModernUI.UiStrings.Ok, new CombinedCommand(Model.SaveCommand, CloseCommand)),
                CancelButton
            };

            MapWebBrowser.SetJsBridge<JsBridge>(x => x.Model = Model);
            MapWebBrowser.StartPage = GetMapAddress(track);

            Model.PropertyChanged += Model_PropertyChanged;
        }

        private static bool _skipNext;

        private void Model_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (_skipNext) return;
            switch (e.PropertyName) {
                case nameof(Model.Latitude):
                case nameof(Model.Longitude):
                    var pair = new GeoTagsEntry(Model.Latitude, Model.Longitude);
                    if (!pair.IsEmptyOrInvalid) {
                        MapWebBrowser.MainTab?.Execute(@"moveTo", $@"{pair.LatitudeValue};{pair.LongitudeValue}");
                    }
                    break;
            }
        }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust"), ComVisible(true)]
        public class JsBridge : JsBridgeCSharp {
            [CanBeNull]
            internal ViewModel Model;

            [UsedImplicitly]
            public void Update(double lat, double lng) {
                Sync(() => {
                    _skipNext = true;
                    if (Model != null) {
                        Model.Latitude = GeoTagsEntry.ToLat(lat);
                        Model.Longitude = GeoTagsEntry.ToLng(lng);
                    }
                    _skipNext = false;
                });
            }
        }

        public class ViewModel : NotifyPropertyChanged {
            private string _latitude;

            public string Latitude {
                get => _latitude;
                set {
                    if (value == _latitude) return;
                    _latitude = value;
                    OnPropertyChanged();
                    _saveCommand?.RaiseCanExecuteChanged();
                }
            }
            private string _longitude;

            public string Longitude {
                get => _longitude;
                set {
                    if (value == _longitude) return;
                    _longitude = value;
                    OnPropertyChanged();
                    _saveCommand?.RaiseCanExecuteChanged();
                }
            }

            public ViewModel(TrackObjectBase track) {
                Track = track;

                if (track.GeoTags != null) {
                    Latitude = track.GeoTags.DisplayLatitude.Replace(@"lat", "");
                    Longitude = track.GeoTags.DisplayLongitude.Replace(@"lon", "");
                } else {
                    Latitude = null;
                    Longitude = null;
                }
            }

            public TrackObjectBase Track { get; }

            private CommandBase _saveCommand;

            public ICommand SaveCommand => _saveCommand ?? (_saveCommand = new DelegateCommand(() => {
                Track.GeoTags = new GeoTagsEntry(Latitude, Longitude);
            }, () => Latitude != null && Longitude != null));
        }

        private static string GetQuery(TrackObjectBase track) {
            return string.IsNullOrEmpty(track.City) && string.IsNullOrEmpty(track.Country) ? track.Name :
                    new[] { track.City, track.Country }.Where(x => x != null).JoinToString(@", ");
        }

        private static string GetMapAddress(TrackObjectBase track) {
            var tags = track.GeoTags;
            return CmHelpersProvider.GetAddress("map") + @"?ms#" +
                    (tags?.IsEmptyOrInvalid == false ? $"{tags.LatitudeValue};{tags.LongitudeValue}" : GetQuery(track));
        }
    }
}
