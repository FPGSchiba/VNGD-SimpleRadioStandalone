using System;
using System.Linq;
using NLog;
using Vanguard.VCS.Client.Network.Models;
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
            var state = ClientStateSingleton.Instance.CurrentRadioState;
            if (radioId < 1 || radioId > state.Radios.Count) return false;

            var updated = state.Radios.ToList();
            var r = updated[radioId - 1];

            double freqHz = inMHz ? frequency * 1_000_000.0 : frequency;
            double newFreq = delta ? r.FrequencyHz + freqHz : freqHz;
            newFreq = Math.Max(1.0, Math.Min(9_999_999_999.0, newFreq));

            updated[radioId - 1] = r with { FrequencyHz = newFreq };
            ClientStateSingleton.Instance.CurrentRadioState =
                new ClientRadioState(updated.AsReadOnly());
            return true;
        }

        public static bool UpdateStandbyRadioFrequency(double standbyfrequency, int radioId, bool delta = true, bool inMHz = true)
        {
            return true;
        }

        public static bool SelectRadio(int radioId)
        {
            // radioId is 0-based (InputBinding value - 100)
            var state = ClientStateSingleton.Instance.CurrentRadioState;
            if (radioId < 0 || radioId >= state.Radios.Count) return false;
            var radio = state.Radios[radioId];
            if (!radio.Enabled) return false;
            ClientStateSingleton.Instance.SelectedRadioIndex = radioId;
            return true;
        }

        public static RadioInformation GetRadio(int radio)
        {
            var radios = ClientStateSingleton.Instance.CurrentRadioState.Radios;
            if (radio < 1 || radio > radios.Count)
                return null;

            var r = radios[radio - 1];
            return new RadioInformation
            {
                name = r.Name,
                freq = r.FrequencyHz,
                modulation = r.Enabled
                    ? (r.IsIntercom
                        ? RadioInformation.Modulation.INTERCOM
                        : RadioInformation.Modulation.AM)
                    : RadioInformation.Modulation.DISABLED,
                freqMax = 9999999999,
                freqMin = 1,
            };
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
            // RadioId is 1-based (UI convention)
            var state = ClientStateSingleton.Instance.CurrentRadioState;
            if (RadioId < 1 || RadioId > state.Radios.Count) return;

            var updated = state.Radios.ToList();
            var r = updated[RadioId - 1];
            updated[RadioId - 1] = r with
            {
                Enabled = modulation != RadioInformation.Modulation.DISABLED,
                IsIntercom = modulation == RadioInformation.Modulation.INTERCOM,
            };
            ClientStateSingleton.Instance.CurrentRadioState =
                new ClientRadioState(updated.AsReadOnly());
        }
    }
}
