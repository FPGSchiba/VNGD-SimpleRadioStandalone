using System;
using Newtonsoft.Json;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class DCSPlayerRadioInfo
    {
        //HOTAS or IN COCKPIT controls
        public enum RadioSwitchControls
        {
            HOTAS = 0,
            IN_COCKPIT = 1
        }

        public string name = "";
        public DcsPosition pos = new DcsPosition();
        public volatile bool ptt = false;

        public RadioInformation[] radios = new RadioInformation[11]; //10 + intercom
        public RadioSwitchControls control = RadioSwitchControls.HOTAS;
        public short selected = 0;
        public string unit = "";
        public uint unitId;

        public readonly static uint UnitIdOffset = 100000001
            ; // this is where non aircraft "Unit" Ids start from for satcom intercom

        public DCSPlayerRadioInfo()
        {
            for (var i = 0; i < 11; i++)
            {
                radios[i] = new RadioInformation();
            }
        }

        [JsonIgnore]
        public long LastUpdate { get; set; }

        // override object.Equals
        public override bool Equals(object compare)
        {
            if ((compare == null) || (GetType() != compare.GetType()))
            {
                return false;
            }

            var compareRadio = compare as DCSPlayerRadioInfo;

            if (control != compareRadio.control)
            {
                return false;
            }
            //if (side != compareRadio.side)
            //{
            //    return false;
            //}
            if (!name.Equals(compareRadio.name))
            {
                return false;
            }
            if (!unit.Equals(compareRadio.unit))
            {
                return false;
            }

            if (unitId != compareRadio.unitId)
            {
                return false;
            }

            for (var i = 0; i < radios.Length; i++)
            {
                var radio1 = radios[i];
                var radio2 = compareRadio.radios[i];

                if ((radio1 != null) && (radio2 != null))
                {
                    if (!radio1.Equals(radio2))
                    {
                        return false;
                    }
                }
            }

            return true;
        }


        /*
         * Was Radio updated in the last 10 Seconds
         */

        public bool IsCurrent()
        {
            return LastUpdate > Environment.TickCount - 10000;
        }

        public RadioInformation CanHearTransmission(double frequency, RadioInformation.Modulation modulation,
            uint sendingUnitId,
            out RadioReceivingState receivingState)
        {
            if (!IsCurrent())
            {
                receivingState = null;
                return null;
            }
            for (var i = 0; i < radios.Length; i++)
            {
                var receivingRadio = radios[i];

                if (receivingRadio != null)
                {
                    //handle INTERCOM Modulation is 2
                    if ((receivingRadio.modulation == RadioInformation.Modulation.INTERCOM) &&
                        (modulation == RadioInformation.Modulation.INTERCOM))
                    {
                        if ((unitId > 0) && (sendingUnitId > 0)
                            && (unitId == sendingUnitId))
                        {
                            receivingState = new RadioReceivingState
                            {
                                IsSecondary = false,
                                LastReceviedAt = Environment.TickCount,
                                ReceivedOn = i
                            };


                            return receivingRadio;
                        }
                        receivingState = null;
                        return null;
                    }

                    if (modulation == RadioInformation.Modulation.DISABLED
                        || receivingRadio.modulation == RadioInformation.Modulation.DISABLED)
                    {
                        continue;
                    }

                    if ((receivingRadio.freq == frequency)
                        && (receivingRadio.modulation == modulation)
                        && (receivingRadio.freq > 10000))
                    {
                        receivingState = new RadioReceivingState
                        {
                            IsSecondary = false,
                            LastReceviedAt = Environment.TickCount,
                            ReceivedOn = i
                        };

                        return receivingRadio;
                    }
                    if ((receivingRadio.secFreq == frequency)
                        && (receivingRadio.secFreq > 10000))
                    {
                        receivingState = new RadioReceivingState
                        {
                            IsSecondary = true,
                            LastReceviedAt = Environment.TickCount,
                            ReceivedOn = i
                        };

                        return receivingRadio;
                    }
                }
            }
            receivingState = null;
            return null;
        }
    }
}