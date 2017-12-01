﻿using AcManager.Tools.Miscellaneous;
using AcTools.DataFile;
using AcTools.Utils;

#pragma warning disable 649

namespace AcManager.Tools.Objects {
    public sealed partial class CarObject : ICupSupportedObject {
        private LazierThis<double?> _steerLock;
        public double? SteerLock => _steerLock.Get(() => AcdData?.GetIniFile("car.ini")["CONTROLS"].GetDoubleNullable("STEER_LOCK") * 2d);

        string ICupSupportedObject.InstalledVersion => Version;
        public CupContentType CupContentType => CupContentType.Car;
        public bool IsCupUpdateAvailable => CupClient.Instance.ContainsAnUpdate(CupContentType.Car, Id, Version);
        public CupClient.CupInformation CupUpdateInformation => CupClient.Instance.GetInformation(CupContentType.Car, Id);

        protected override void OnVersionChanged() {
            OnPropertyChanged(nameof(ICupSupportedObject.InstalledVersion));
            OnPropertyChanged(nameof(IsCupUpdateAvailable));
            OnPropertyChanged(nameof(CupUpdateInformation));
        }

        void ICupSupportedObject.OnCupUpdateAvailableChanged() {
            OnPropertyChanged(nameof(IsCupUpdateAvailable));
            OnPropertyChanged(nameof(CupUpdateInformation));
        }

        public void SetValues(string author, string informationUrl, string version) {
            Author = author;
            Url = informationUrl;
            Version = version;
            Save();
        }
    }
}