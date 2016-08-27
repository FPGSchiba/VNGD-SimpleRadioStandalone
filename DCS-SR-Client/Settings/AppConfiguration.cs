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
            RADIO_X,
            RADIO_Y,
            RADIO_SIZE,
            RADIO_OPACITY,
            RADIO_WIDTH,
            RADIO_HEIGHT
        }

        private const string RegPath = "HKEY_CURRENT_USER\\SOFTWARE\\DCS-SimpleRadioStandalone";

        private int _audioInputDeviceId;
        private int _audioOutputDeviceId;
        private string _lastServer;
        private float _micBoost;
        private float _speakerBoost;
        private double _radioX;
        private double _radioY;
        private float _radioSize;
        private double _radioOpacity;
        private double _radioWidth;
        private double _radioHeight;

        private static AppConfiguration _instance;

        public string[] UserSettings { get; }

        public static AppConfiguration Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new AppConfiguration();
                }
                return _instance;
            }
        }

        private AppConfiguration()
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

         
            try
            {
                RadioX = double.Parse((string)Registry.GetValue(RegPath,
                    RegKeys.RADIO_X.ToString(),
                    "300"));
            }
            catch (Exception ex)
            {
                RadioX = 300;
            }

            try
            {
                RadioY = double.Parse((string)Registry.GetValue(RegPath,
                    RegKeys.RADIO_Y.ToString(),
                    "300"));
            }
            catch (Exception ex)
            {
                RadioY = 300;
            }


            try
            {
                RadioWidth = double.Parse((string)Registry.GetValue(RegPath,
                    RegKeys.RADIO_WIDTH.ToString(),
                    "122"));
            }
            catch (Exception ex)
            {
                RadioWidth = 300;
            }

            try
            {
                RadioHeight = double.Parse((string)Registry.GetValue(RegPath,
                    RegKeys.RADIO_HEIGHT.ToString(),
                    "270"));
            }
            catch (Exception ex)
            {
                RadioHeight = 300;
            }


            try
            {
                RadioOpacity = double.Parse((string)Registry.GetValue(RegPath,
                    RegKeys.RADIO_OPACITY.ToString(),
                    "1.0"));
            }
            catch (Exception ex)
            {
                RadioOpacity = 1.0;
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

        public double RadioX
        {
            get { return _radioX; }
            set
            {
                _radioX = value;

                Registry.SetValue(RegPath,
                    RegKeys.RADIO_X.ToString(),
                    _radioX);
            }
        }

        public double RadioY
        {
            get { return _radioY; }
            set
            {
                _radioY = value;

                Registry.SetValue(RegPath,
                    RegKeys.RADIO_Y.ToString(),
                    _radioY);
            }
        }

        public double RadioHeight
        {
            get { return _radioHeight; }
            set
            {
                _radioHeight = value;

                Registry.SetValue(RegPath,
                    RegKeys.RADIO_HEIGHT.ToString(),
                    _radioHeight);
            }
        }

        public double RadioWidth
        {
            get { return _radioWidth; }
            set
            {
                _radioWidth = value;

                Registry.SetValue(RegPath,
                    RegKeys.RADIO_WIDTH.ToString(),
                    _radioWidth);
            }
        }

        public float RadioSize
        {
            get { return _radioSize; }
            set
            {
                _radioSize = value;

                Registry.SetValue(RegPath,
                    RegKeys.RADIO_SIZE.ToString(),
                    _radioSize);
            }
        }

        public double RadioOpacity
        {
            get { return _radioOpacity; }
            set
            {
                _radioOpacity = value;

                Registry.SetValue(RegPath,
                    RegKeys.RADIO_OPACITY.ToString(),
                    _radioOpacity);
            }
        }



    }
}