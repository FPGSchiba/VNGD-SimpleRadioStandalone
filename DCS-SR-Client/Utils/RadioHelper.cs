using NLog;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Settings.RadioChannels;
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
            var manager = App.RadioStateManager;
            if (manager == null) return false;
            if (radioId < 1 || radioId > manager.CurrentState.Radios.Count) return false;

            double freqHz = inMHz ? frequency * 1_000_000.0 : frequency;
            double currentHz = manager.CurrentState.Radios[radioId - 1].FrequencyHz;
            double deltaHz = delta ? freqHz : freqHz - currentHz;
            manager.UpdateRadioFrequency(radioId, deltaHz);
            return true;
        }

        public static bool UpdateStandbyRadioFrequency(double standbyfrequency, int radioId, bool delta = true, bool inMHz = true)
        {
            return true;
        }

        public static bool SelectRadio(int radioId)
        {
            // radioId is 0-based (InputBinding value - 100)
            var manager = App.RadioStateManager;
            if (manager == null) return false;
            if (radioId < 0 || radioId >= manager.CurrentState.Radios.Count) return false;
            var radio = manager.CurrentState.Radios[radioId];
            if (!radio.Enabled) return false;
            manager.SelectedRadioIndex = radioId;
            return true;
        }

        public static RadioInformation GetRadio(int radio)
        {
            return App.RadioStateManager?.GetRadio(radio);
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
            var manager = App.RadioStateManager;
            if (manager == null) return;
            if (RadioId < 1 || RadioId > manager.CurrentState.Radios.Count) return;
            manager.SetRadioModulation(
                RadioId,
                modulation != RadioInformation.Modulation.DISABLED,
                modulation == RadioInformation.Modulation.INTERCOM);
        }
    }
}

