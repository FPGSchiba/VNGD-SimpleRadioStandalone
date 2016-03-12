using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using FragLabs.Audio.Codecs;
using NAudio.Wave;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    class UDPVoiceHandler
    {

        private static Logger logger = LogManager.GetCurrentClassLogger();
        UdpClient listener;

        private volatile bool stop = false;
        private ConcurrentDictionary<String, SRClient> clientsList;
        private byte[] guidAsciiBytes;
        private IPAddress address;
        private OpusDecoder _decoder;
        private BufferedWaveProvider _playBuffer;
        private string guid;
        private InputDeviceManager inputManager;

        private volatile bool ptt = false;

        public UDPVoiceHandler(ConcurrentDictionary<string, SRClient> clientsList, string guid, IPAddress address, OpusDecoder _decoder, BufferedWaveProvider _playBuffer, InputDeviceManager inputManager)
        {
            this._decoder = _decoder;
            this._playBuffer = _playBuffer;

            this.clientsList = clientsList;
            guidAsciiBytes = Encoding.ASCII.GetBytes(guid);

            this.guid = guid;
            this.address = address;

            this.inputManager = inputManager;
        }

      

        public void Listen()
        {
            listener = new UdpClient();
            listener.AllowNatTraversal(true);

            //open ports by sending
            try
            {
                IPEndPoint ip = new IPEndPoint(this.address, 5010);

                byte[] bytes = new byte[5];
              
                listener.Send(bytes, 5, ip);
            }
            catch (Exception ex) { }
            

            this.inputManager.StartDetectPTT((bool pressed) =>
            {
                ptt = pressed;
            });

            while (!stop)
            {
                try
                {
                    IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, 5010);
                    //   listener.Client.ReceiveTimeout = 3000;

                    byte[] bytes = listener.Receive(ref groupEP);

                    if(bytes!=null && bytes.Length > 0)
                    {

                        HandleAudio(bytes);
                    }
                
                }
                catch (Exception e)
                {
                    logger.Error(e, "error listening for UDP Voip");
                  
                }
            }

            try
            {
                listener.Close();
            }
            catch (Exception e) { }
        }
        public void RequestStop()
        {
            stop = true;
            try
            {
                listener.Close();
            }
            catch (Exception e) { }

            inputManager.StopPTT();
        }

        private SRClient IsClientMetaDataValid(string clientGuid)
        {
            if (clientsList.ContainsKey(clientGuid))
            {
                SRClient client = clientsList[guid];

                if (client != null && client.ClientRadios != null && client.ClientRadios.isCurrent())
                {
                    return client;
                }
            }
            return null;
        }

        private void HandleAudio(byte[] audioBytes)
        {
            //check if we should play audio

            SRClient myClient = IsClientMetaDataValid(guid);

            if (myClient != null)
            {
                //last 36 bytes are guid!
                String recievingGuid = Encoding.ASCII.GetString(
                audioBytes, audioBytes.Length - 36, 36);

                SRClient receivingClient = IsClientMetaDataValid(recievingGuid);

                if (receivingClient != null)
                {
                    RadioInformation receivingRadio = CanHear(myClient.ClientRadios, receivingClient.ClientRadios);
                    if (receivingRadio != null)
                    {
                        //now check that the radios match
                        int len;
                        //- 36 so we ignore the UUID
                        byte[] decoded = _decoder.Decode(audioBytes, audioBytes.Length - 36, out len);

                        float volume = receivingRadio.volume;

                        //convert to Shorts for volume
                        short[] sampleBuffer = new short[len / 2];
                        Buffer.BlockCopy(decoded, 0, sampleBuffer, 0, len);

                        //volume!
                        //for (int i=0; i< sampleBuffer.Length; i++)
                        //{

                        //    sampleBuffer[i] = (short)( volume * sampleBuffer[i] );
                        //}
                        //convert back to bytes

                        Buffer.BlockCopy(sampleBuffer, 0, decoded, 0, sampleBuffer.Length);

                        _playBuffer.AddSamples(decoded, 0, len);
                    }
                }
            }
        }

        private RadioInformation CanHear(DCSRadios myClient, DCSRadios transmittingClient)
        {

            if (transmittingClient.selected >= 0 && transmittingClient.selected < 3)
            {
                RadioInformation transmittingRadio = transmittingClient.radios[transmittingClient.selected];

                if (transmittingRadio != null)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        RadioInformation receivingRadio = myClient.radios[i];

                        if (receivingRadio != null)
                        {

                            //handle INTERCOM Modulation is 2
                            if (receivingRadio.modulation == 2 && transmittingRadio.modulation == 2
                                && myClient.unitId > 0 && transmittingClient.unitId > 0
                                && myClient.unitId == transmittingClient.unitId)
                            {
                                SendUpdateToGUI(i, false);
                                return receivingRadio;

                            }
                            else if (receivingRadio.frequency == transmittingRadio.frequency
                                && receivingRadio.modulation == transmittingRadio.modulation
                                && receivingRadio.frequency > 1)
                            {
                                SendUpdateToGUI(i, false);
                                return receivingRadio;
                            }
                            else if (receivingRadio.secondaryFrequency == transmittingRadio.secondaryFrequency
                                && receivingRadio.secondaryFrequency > 100)
                            {
                                SendUpdateToGUI(i, true);
                                return receivingRadio;

                            }
                        }

                    }
                }
            }


            return null;

        }


        public void Send(byte[] bytes, int len)
        {
            //TODO only send when transmit is pressed
            //TODO only send when a radio is actually selected!

            if (ptt)
            {
                SRClient myClient = IsClientMetaDataValid(guid);

                if (myClient != null && !stop)
                {

                    try
                    {

                        //append guid

                        byte[] combinedBytes = new byte[len + 36];
                        System.Buffer.BlockCopy(bytes, 0, combinedBytes, 0, len);
                        System.Buffer.BlockCopy(guidAsciiBytes, 0, combinedBytes, len, 36);



                        //   UdpClient myClient = new UdpClient();
                        //   listener.AllowNatTraversal(true);
                        IPEndPoint ip = new IPEndPoint(this.address, 5010);

                        listener.Send(combinedBytes, combinedBytes.Length, ip);
                        //    myClient.Close();
                    }
                    catch (Exception e)
                    {

                        Console.WriteLine("Exception Handling Message " + e.Message);
                    }
                }
            }
        }

        private void SendUpdateToGUI(int radio, bool secondary)
        {
            return; //TODO fix the string format?!
            //string str = String.Format("{\"radio\": {0} , \"secondary\": {1} }\r\n", radio, secondary ? "true" : "false");
            //byte[] bytes = Encoding.ASCII.GetBytes(str);
            ////multicast
            //try
            //{

            //    UdpClient client = new UdpClient();
            //    IPEndPoint ip = new IPEndPoint(IPAddress.Parse("239.255.50.10"), 35025);

            //    client.Send(bytes, bytes.Length, ip);
            //    client.Close();
            //}
            //catch (Exception e) { }

        }

    }
}
