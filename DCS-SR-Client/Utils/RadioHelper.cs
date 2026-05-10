using System;
using System.Linq;
using NLog;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Settings.RadioChannels;
using Vanguard.VCS.Client.Singletons;
using Vanguard.VCS.Common.DCSState;

namespace Vanguard.VCS.Client.Utils
{
    public static class RadioHelper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static void ToggleGuard(int radioId)
        {
        }

        public static void SetGuard(int radioId, bool enabled)
        {
        }

        public static bool UpdateRadioFrequency(double frequency, int radioId, bool delta = true, bool inMHz = true)
        {
            return true;
        }

        public static bool UpdateStandbyRadioFrequency(double standbyfrequency, int radioId, bool delta = true, bool inMHz = true)
        {
            return true;
        }

        public static bool SelectRadio(int radioId)
        {
            return false;
        }

        public static RadioInformation GetRadio(int radio)
        {
            return null;
        }

        public static void ToggleEncryption(int radioId)
        {
        }

        public static void SetEncryptionKey(int radioId, int encKey)
        {
        }

        public static void SelectNextRadio()
        {
        }

        public static void SelectPreviousRadio()
        {
        }

        public static void IncreaseEncryptionKey(int radioId)
        {
        }

        public static void DecreaseEncryptionKey(int radioId)
        {
        }

        public static void SelectRadioChannel(PresetChannel selectedPresetChannel, int radioId)
        {
        }

        public static void SelectStandbyRadioChannel(PresetChannel selectedPresetChannel, int radioId)
        {
        }

        public static void RadioChannelUp(int radioId)
        {
        }

        public static void RadioChannelDown(int radioId)
        {
        }

        public static void SetRadioVolume(float volume, int radioId)
        {
        }

        public static void ToggleRetransmit(int radioId)
        {
        }

        public static void RadioVolumeUp(short radioId)
        {
        }

        public static void RadioVolumeDown(short radioId)
        {
        }

        public static void SetRadioModulation(int RadioId, RadioInformation.Modulation modulation)
        {
        }
    }
}
