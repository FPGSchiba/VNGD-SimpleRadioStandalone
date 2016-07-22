using System;
using Microsoft.Win32;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class AppConfiguration
    {
        public enum RegKeys
        {
            AUDIO_INPUT_DEVICE_ID,
            AUDIO_OUTPUT_DEVICE_ID,
            LAST_SERVER,
            MIC_BOOST,
            SPEAKER_BOOST,
        }

        private const string RegPath = "HKEY_CURRENT_USER\\SOFTWARE\\DCS-SimpleRadioStandalone";

        private int _audioInputDeviceId;
        private int _audioOutputDeviceId;
        private string _lastServer;
        private float _micBoost;
        private float _speakerBoost;

        public AppConfiguration()
        {
            try
            {
                AudioInputDeviceId = (int) Registry.GetValue(RegPath,
                    RegKeys.AUDIO_INPUT_DEVICE_ID.ToString(),
                    0);
            }
            catch (Exception ex)
            {
                AudioInputDeviceId = 0;
            }

            try
            {
                AudioOutputDeviceId = (int) Registry.GetValue(RegPath,
                    RegKeys.AUDIO_OUTPUT_DEVICE_ID.ToString(),
                    0);
            }
            catch (Exception ex)
            {
                AudioOutputDeviceId = 0;
            }

            try
            {
                LastServer = (string) Registry.GetValue(RegPath,
                    RegKeys.LAST_SERVER.ToString(),
                    "127.0.0.1");
            }
            catch (Exception ex)
            {
                LastServer = "127.0.0.1";
            }

            try
            {
                MicBoost = float.Parse((string) Registry.GetValue(RegPath,
                    RegKeys.MIC_BOOST.ToString(),
                    "1.0"));
            }
            catch (Exception ex)
            {
                MicBoost = 1.0f;
            }

            try
            {
                SpeakerBoost = float.Parse((string)Registry.GetValue(RegPath,
                    RegKeys.SPEAKER_BOOST.ToString(),
                    "1.0"));
            }
            catch (Exception ex)
            {
                SpeakerBoost = 1.0f;
            }
        }


        public int AudioInputDeviceId
        {
            get { return _audioInputDeviceId; }
            set
            {
                _audioInputDeviceId = value;

                Registry.SetValue(RegPath,
                    RegKeys.AUDIO_INPUT_DEVICE_ID.ToString(),
                    _audioInputDeviceId);
            }
        }

        public int AudioOutputDeviceId
        {
            get { return _audioOutputDeviceId; }
            set
            {
                _audioOutputDeviceId = value;

                Registry.SetValue(RegPath,
                    RegKeys.AUDIO_OUTPUT_DEVICE_ID.ToString(),
                    _audioOutputDeviceId);
            }
        }

        public string LastServer
        {
            get { return _lastServer; }
            set
            {
                _lastServer = value;

                Registry.SetValue(RegPath,
                    RegKeys.LAST_SERVER.ToString(),
                    _lastServer);
            }
        }

        public float MicBoost
        {
            get { return _micBoost; }
            set
            {
                _micBoost = value;

                Registry.SetValue(RegPath,
                    RegKeys.MIC_BOOST.ToString(),
                    _micBoost);
            }
        }



        public float SpeakerBoost
        {
            get { return _speakerBoost; }
            set
            {
                _speakerBoost = value;

                Registry.SetValue(RegPath,
                    RegKeys.SPEAKER_BOOST.ToString(),
                    _speakerBoost);
            }
        }
       
    }
}