using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using NAudio.Wave;
using System.Net.Sockets;
using System.Net;
using System.Threading.Tasks;
using FragLabs.Audio.Codecs;
using System.Threading;
using NLog.Config;
using NLog.Targets;
using NLog;
using System.ComponentModel;
using SharpDX.DirectInput;
using SharpDX.Multimedia;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        WaveIn _waveIn;
        WaveOut _waveOut;
        BufferedWaveProvider _playBuffer;
        OpusEncoder _encoder;
        OpusDecoder _decoder;
        int _segmentFrames;
        int _bytesPerSegment;
        volatile bool _stop = true;

        ClientSync client;

        String guid;
        UDPVoiceHandler voiceSender;

        InputDeviceManager inputManager;

        System.Collections.Concurrent.ConcurrentDictionary<string, Common.SRClient> clients = new System.Collections.Concurrent.ConcurrentDictionary<string, Common.SRClient>();

        public MainWindow()
        {
            InitializeComponent();

            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                mic.Items.Add(WaveIn.GetCapabilities(i).ProductName);
            }
            if (WaveIn.DeviceCount > 0)
                mic.SelectedIndex = 0;
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                speakers.Items.Add(WaveOut.GetCapabilities(i).ProductName);
            }
            if (WaveOut.DeviceCount > 0)
                speakers.SelectedIndex = 0;

            guid = Guid.NewGuid().ToString();
            SetupLogging();
           

            inputManager = new InputDeviceManager(this);
            LoadInputSettings();




        }

       

        private void SetupLogging()
        {

            // Step 1. Create configuration object 
            var config = new LoggingConfiguration();

            // Step 2. Create targets and add them to the configuration 
            var consoleTarget = new ColoredConsoleTarget();
            config.AddTarget("console", consoleTarget);

            var fileTarget = new FileTarget();
            config.AddTarget("file", fileTarget);

            // Step 3. Set target properties 
            consoleTarget.Layout = @"${date:format=HH\:mm\:ss} ${logger} ${message}";
            fileTarget.FileName = "${basedir}/clientlog.txt";
            fileTarget.Layout = @"${date:format=HH\:mm\:ss} ${logger} ${message}";

            // Step 4. Define rules
            var rule1 = new LoggingRule("*", LogLevel.Debug, consoleTarget);
            config.LoggingRules.Add(rule1);

            var rule2 = new LoggingRule("*", LogLevel.Debug, fileTarget);
            config.LoggingRules.Add(rule2);

            // Step 5. Activate the configuration
            LogManager.Configuration = config;
        }

        void LoadInputSettings()
        {
            //TODO load input settings

            if(inputManager.InputConfig.PTTCommon !=null)
            {
                pttCommonText.Text = inputManager.InputConfig.PTTCommon.Button.ToString();
                pttCommonDevice.Text = inputManager.InputConfig.PTTCommon.DeviceName;
            }
           
        }

        void StartEncoding()
        {
            IPAddress ipAddr;
            if (IPAddress.TryParse(this.serverIp.Text.Trim(), out ipAddr))
            {
                _stop = false;

                _segmentFrames = 960 / 2;
                _encoder = OpusEncoder.Create(24000, 1, FragLabs.Audio.Codecs.Opus.Application.Restricted_LowLatency);
                //    _encoder.Bitrate = 8192;
                _decoder = OpusDecoder.Create(24000, 1);
                _bytesPerSegment = _encoder.FrameByteCount(_segmentFrames);

                _waveIn = new WaveIn(WaveCallbackInfo.FunctionCallback());
                _waveIn.BufferMilliseconds = 50;
                _waveIn.DeviceNumber = mic.SelectedIndex;
                _waveIn.DataAvailable += _waveIn_DataAvailable;
                _waveIn.WaveFormat = new NAudio.Wave.WaveFormat(48000, 16, 1);

                _playBuffer = new BufferedWaveProvider(new NAudio.Wave.WaveFormat(48000, 16, 1));

                _waveOut = new WaveOut();
                _waveOut.DesiredLatency = 75; //50ms latency
                _waveOut.DeviceNumber = speakers.SelectedIndex;
                _waveOut.Init(_playBuffer);

                _waveOut.Play();

                voiceSender = new UDPVoiceHandler(clients, guid, ipAddr, _decoder, _playBuffer, inputManager);
                Thread voiceSenderThread = new Thread(voiceSender.Listen);

                voiceSenderThread.Start();

                _waveIn.StartRecording();
            }
            else
            {
                //invalid IP
            }



        }



        byte[] _notEncodedBuffer = new byte[0];


        void _waveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            byte[] soundBuffer = new byte[e.BytesRecorded + _notEncodedBuffer.Length];

            for (int i = 0; i < _notEncodedBuffer.Length; i++)
                soundBuffer[i] = _notEncodedBuffer[i];

            for (int i = 0; i < e.BytesRecorded; i++)
                soundBuffer[i + _notEncodedBuffer.Length] = e.Buffer[i];

            int byteCap = _bytesPerSegment;
            //      Console.WriteLine("{0} ByteCao", byteCap);
            int segmentCount = (int)Math.Floor((decimal)soundBuffer.Length / byteCap);
            int segmentsEnd = segmentCount * byteCap;
            int notEncodedCount = soundBuffer.Length - segmentsEnd;
            _notEncodedBuffer = new byte[notEncodedCount];
            for (int i = 0; i < notEncodedCount; i++)
            {
                _notEncodedBuffer[i] = soundBuffer[segmentsEnd + i];
            }

            for (int i = 0; i < segmentCount; i++)
            {
                byte[] segment = new byte[byteCap];
                for (int j = 0; j < segment.Length; j++)
                    segment[j] = soundBuffer[(i * byteCap) + j];

                int len;
                byte[] buff = _encoder.Encode(segment, segment.Length, out len);

                if (voiceSender != null)
                    voiceSender.Send(buff, len);
            }
        }

        void StopEncoding()
        {
            _waveIn.StopRecording();
            _waveIn.Dispose();
            _waveIn = null;
            _waveOut.Stop();
            _waveOut.Dispose();
            _waveOut = null;
            _playBuffer = null;
            _encoder.Dispose();
            _encoder = null;
            _decoder.Dispose();
            _decoder = null;


            _stop = true;
        }

        private void startStop_Click(object sender, RoutedEventArgs e)
        {
            if (!_stop)
            {
                stop();
            }
            else
            {
                IPAddress ipAddr;

                if (IPAddress.TryParse(this.serverIp.Text.Trim(), out ipAddr))
                {
                    client = new ClientSync(clients, guid);
                    client.TryConnect(new IPEndPoint(ipAddr, 5002), ConnectCallback);

                    startStop.Content = "Connecting...";

                }
                else
                {
                    //invalid ID

                }

            }
        }
        private void stop()
        {
            startStop.Content = "Start";
            try
            {
                StopEncoding();
            }
            catch (Exception ex) { }

            _stop = true;

            if (client != null)
            {
                client.Disconnect();
                client = null;
            }


            if (voiceSender != null)
            {
                voiceSender.RequestStop();
                voiceSender = null;
            }


        }

        private void ConnectCallback(bool result)
        {
            if (result)
            {
                if (_stop)
                {
                    startStop.Content = "Disconnect";
                    StartEncoding();
                    _stop = false;
                }

            }
            else
            {
                stop();
            }

        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            stop();

        }

        private void pttCommonButton_Click(object sender, RoutedEventArgs e)
        {
            pttCommonClear.IsEnabled = false;
            pttCommonButton.IsEnabled = false;


            inputManager.AssignButton((InputDevice device) =>
            {
                pttCommonClear.IsEnabled = true;
                pttCommonButton.IsEnabled = true;

                pttCommonDevice.Text = device.DeviceName;
                pttCommonText.Text = device.Button.ToString();

                inputManager.InputConfig.PTTCommon = device;
                inputManager.InputConfig.WriteInputRegistry("common", device);
               
            //    Console.WriteLine(device.Button + " " + device.Device);

                //inputManager.StartDetectPTT(device, (bool pressed) => {
                //    Console.WriteLine("PTT: "+pressed);

                //});

            });

        }
    }
}



