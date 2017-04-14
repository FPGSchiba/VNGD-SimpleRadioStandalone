using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Common;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Utils
{
    public static class RadioHelper
    {

        public static void ToggleGuard(int radioId)
        {
            var radio = GetRadio(radioId);

            if (radio != null)
            {
                if (radio.freqMode == RadioInformation.FreqMode.OVERLAY)
                {
                    if (radio.secFreq > 0)
                    {
                        radio.secFreq = 0; // 0 indicates we want it overridden + disabled
                    }
                    else
                    {
                        radio.secFreq = 1; //indicates we want it back
                    }

                    //make radio data stale to force resysnc
                    RadioDCSSyncServer.LastSent = 0;
                }
            }
        }

        public static void UpdateRadioFrequency(double frequency, int radioId, bool delta = true)
        {
            const double MHz = 1000000;

            frequency = frequency * MHz;

            var radio = GetRadio(radioId);

            if (radio != null)
            {
                if (radio.modulation != RadioInformation.Modulation.DISABLED
                    && radio.modulation != RadioInformation.Modulation.INTERCOM
                    && radio.freqMode == RadioInformation.FreqMode.OVERLAY)
                {

                    if(delta)
                        radio.freq += frequency;
                    else
                    {
                        radio.freq = frequency;
                    }

                    //make sure we're not over or under a limit
                    if (radio.freq > radio.freqMax)
                    {
                        radio.freq = radio.freqMax;
                    }
                    else if (radio.freq < radio.freqMin)
                    {
                        radio.freq = radio.freqMin;
                    }

                    //make radio data stale to force resysnc
                    RadioDCSSyncServer.LastSent = 0;
                }
            }
        }

        public static bool SelectRadio(int radioId)
        {
            var radio = GetRadio(radioId);

            if (radio !=null)
            {
                if (radio.modulation != RadioInformation.Modulation.DISABLED
                    && RadioDCSSyncServer.DcsPlayerRadioInfo.control ==
                    DCSPlayerRadioInfo.RadioSwitchControls.HOTAS)
                {
                    RadioDCSSyncServer.DcsPlayerRadioInfo.selected = (short)radioId;
                    return true;
                }
            }

            return false;
        }

        public static RadioInformation GetRadio(int radio)
        {
            var dcsPlayerRadioInfo = RadioDCSSyncServer.DcsPlayerRadioInfo;

            if ((dcsPlayerRadioInfo != null) && dcsPlayerRadioInfo.IsCurrent() &&
                radio < dcsPlayerRadioInfo.radios.Length && (radio >= 0))
            {
                return dcsPlayerRadioInfo.radios[radio];
            }

            return null;
        }

        public static void ToggleEncryption(int radioId)
        {
            var radio = GetRadio(radioId);

            if (radio != null)
            {
                
                if (radio.modulation != RadioInformation.Modulation.DISABLED) // disabled
                {
                    //update stuff
                    if (radio.encMode == RadioInformation.EncryptionMode.ENCRYPTION_JUST_OVERLAY)
                    {
                        if (radio.enc)
                        {
                            radio.enc = false;
                        }
                        else
                        {
                            radio.enc = true;
                        }

                        //make radio data stale to force resysnc
                        RadioDCSSyncServer.LastSent = 0;
                    }
                }
            }
        }

        public static void SetEncryptionKey(int radioId,int encKey)
        {
            var currentRadio = RadioHelper.GetRadio(radioId);

            if (currentRadio != null &&
                currentRadio.modulation != RadioInformation.Modulation.DISABLED) // disabled
            {

                if (currentRadio.modulation != RadioInformation.Modulation.DISABLED) // disabled
                {
                    //update stuff
                    if ((currentRadio.encMode == RadioInformation.EncryptionMode.ENCRYPTION_COCKPIT_TOGGLE_OVERLAY_CODE) ||
                        (currentRadio.encMode == RadioInformation.EncryptionMode.ENCRYPTION_JUST_OVERLAY))
                    {
                        if (encKey > 252)
                            encKey = 252;
                        else if (encKey < 1)
                            encKey = 1;

                        currentRadio.encKey = (byte) encKey;
                        //make radio data stale to force resysnc
                        RadioDCSSyncServer.LastSent = 0;
                    }
                }
            }
        }

        public static void SelectNextRadio()
        {

            var dcsPlayerRadioInfo = RadioDCSSyncServer.DcsPlayerRadioInfo;

            if ((dcsPlayerRadioInfo != null) && dcsPlayerRadioInfo.IsCurrent() &&
                dcsPlayerRadioInfo.control == DCSPlayerRadioInfo.RadioSwitchControls.HOTAS)
            {
                if (dcsPlayerRadioInfo.selected < 0
                    || dcsPlayerRadioInfo.selected > dcsPlayerRadioInfo.radios.Length
                    || dcsPlayerRadioInfo.selected + 1 > dcsPlayerRadioInfo.radios.Length)
                {
                    SelectRadio(1);
                  
                    return;
                }
                else
                {
                    int currentRadio = dcsPlayerRadioInfo.selected;

                    //find next radio
                    for (int i = currentRadio + 1; i < dcsPlayerRadioInfo.radios.Length; i++)
                    {
                        if (SelectRadio(i))
                        {
                            return;
                        }
                    }

                    //search up to current radio
                    for (int i = 1; i < currentRadio; i++)
                    {
                        if (SelectRadio(i))
                        {
                            return;
                        }
                    }

                }
            }
        }

        public static void SelectPreviousRadio()
        {

            var dcsPlayerRadioInfo = RadioDCSSyncServer.DcsPlayerRadioInfo;

            if ((dcsPlayerRadioInfo != null) && dcsPlayerRadioInfo.IsCurrent() && dcsPlayerRadioInfo.control == DCSPlayerRadioInfo.RadioSwitchControls.HOTAS)
            {
                if (dcsPlayerRadioInfo.selected < 0
                    || dcsPlayerRadioInfo.selected > dcsPlayerRadioInfo.radios.Length)
                {
                    dcsPlayerRadioInfo.selected = 1;
                    return;
                }
                else
                {
                    int currentRadio = dcsPlayerRadioInfo.selected;

                    //find previous radio
                    for (int i = currentRadio - 1; i > 0; i--)
                    {
                        if (SelectRadio(i))
                        {
                            return;
                        }
                    }

                    //search down to current radio
                    for (int i = dcsPlayerRadioInfo.radios.Length; i < currentRadio; i--)
                    {
                        if (SelectRadio(i))
                        {
                            return;
                        }
                    }

                }
            }


        }

        public static void IncreaseEncryptionKey(int radioId)
        {
            var currentRadio = RadioHelper.GetRadio(radioId);

            if (currentRadio != null)
            {
                SetEncryptionKey(radioId,currentRadio.encKey + 1);
            }

        }

        public static void DecreaseEncryptionKey(int radioId)
        {
            var currentRadio = RadioHelper.GetRadio(radioId);

            if (currentRadio != null)
            {
                SetEncryptionKey(radioId, currentRadio.encKey - 1);
            }

        }
    }
}
