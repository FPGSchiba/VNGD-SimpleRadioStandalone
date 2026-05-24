using System.Collections.Generic;
using Vanguard.VCS.Client.Audio.Models;

namespace Vanguard.VCS.Client.Audio.Recording
{
    //this holds all transmissions received in a single tick ready for processing on another thread to ensure we dont slow the other
    //processing loop
    public class AudioRecordingSample
    { 
        public int RadioId { get; set;}
        public List<DeJitteredTransmission> MainRadioClientTransmissions { get; set; }
        public List<DeJitteredTransmission> SecondaryRadioClientTransmissions { get; set; }




    }
}
