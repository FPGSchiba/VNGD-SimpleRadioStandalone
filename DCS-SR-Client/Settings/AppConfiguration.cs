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
            RADIO_HEIGHT,
            CLIENT_X,
            CLIENT_Y,
            AWACS_X,
            AWACS_Y,
        }

        private const string RegPath = "HKEY_CURRENT_USER\\SOFTWARE\\DCS-SimpleRadioStandalone";

        private static AppConfiguration _instance;

        private int _audioInputDeviceId;
        private String _audioOutputDeviceId;
        private string _lastServer;
        private float _micBoost;
        private double _radioHeight;
        private double _radioOpacity;
        private float _radioSize;
        private double _radioWidth;
        private double _radioX;
        private double _radioY;
        private float _speakerBoost;

        private double _clientX;
        private double _clientY;

        private double _awacsX;
        private double _awacsY;

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
                AudioOutputDeviceId = (String) Registry.GetValue(RegPath,
                    RegKeys.AUDIO_OUTPUT_DEVICE_ID.ToString(),
                    "");
            }
            catch (Exception ex)
            {
                AudioOutputDeviceId = "";
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
                SpeakerBoost = float.Parse((string) Registry.GetValue(RegPath,
                    RegKeys.SPEAKER_BOOST.ToString(),
                    "1.0"));
            }
            catch (Exception ex)
            {
                SpeakerBoost = 1.0f;
            }


            try
            {
                RadioX = double.Parse((string) Registry.GetValue(RegPath,
                    RegKeys.RADIO_X.ToString(),
                    "300"));
            }
            catch (Exception ex)
            {
                RadioX = 300;
            }

            try
            {
                RadioY = double.Parse((string) Registry.GetValue(RegPath,
                    RegKeys.RADIO_Y.ToString(),
                    "300"));
            }
            catch (Exception ex)
            {
                RadioY = 300;
            }

            try
            {
                 AwacsX = double.Parse((string)Registry.GetValue(RegPath,
                    RegKeys.AWACS_X.ToString(),
                    "300"));
            }
            catch (Exception ex)
            {
                AwacsX = 300;
            }

            try
            {
                AwacsY = double.Parse((string)Registry.GetValue(RegPath,
                    RegKeys.AWACS_Y.ToString(),
                    "300"));
            }
            catch (Exception ex)
            {
                AwacsY = 300;
            }

            try
            {
                ClientX = double.Parse((string)Registry.GetValue(RegPath,
                   RegKeys.CLIENT_X.ToString(),
                   "300"));
            }
            catch (Exception ex)
            {
                ClientX = 300;
            }

            try
            {
                ClientY = double.Parse((string)Registry.GetValue(RegPath,
                    RegKeys.CLIENT_Y.ToString(),
                    "300"));
            }
            catch (Exception ex)
            {
                ClientY = 300;
            }


            try
            {
                RadioWidth = double.Parse((string) Registry.GetValue(RegPath,
                    RegKeys.RADIO_WIDTH.ToString(),
                    "122"));
            }
            catch (Exception ex)
            {
                RadioWidth = 300;
            }

            try
            {
                RadioHeight = double.Parse((string) Registry.GetValue(RegPath,
                    RegKeys.RADIO_HEIGHT.ToString(),
                    "270"));
            }
            catch (Exception ex)
            {
                RadioHeight = 300;
            }


            try
            {
                RadioOpacity = double.Parse((string) Registry.GetValue(RegPath,
                    RegKeys.RADIO_OPACITY.ToString(),
                    "1.0"));
            }
            catch (Exception ex)
            {
                RadioOpacity = 1.0;
            }
        }

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

        public String AudioOutputDeviceId
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



        public double AwacsX
        {
            get { return _awacsX; }
            set
            {
                _awacsX = value;

                Registry.SetValue(RegPath,
                    RegKeys.AWACS_X.ToString(),
                    _awacsX);
            }
        }

        public double AwacsY
        {
            get { return _awacsY; }
            set
            {
                _awacsY = value;

                Registry.SetValue(RegPath,
                    RegKeys.AWACS_Y.ToString(),
                    _awacsY);
            }
        }


        public double ClientX
        {
            get { return _clientX; }
            set
            {
                _clientX = value;

                Registry.SetValue(RegPath,
                    RegKeys.CLIENT_X.ToString(),
                    _clientX);
            }
        }

        public double ClientY
        {
            get { return _clientY; }
            set
            {
                _clientY = value;

                Registry.SetValue(RegPath,
                    RegKeys.CLIENT_Y.ToString(),
                    _clientY);
            }
        }
    }
}