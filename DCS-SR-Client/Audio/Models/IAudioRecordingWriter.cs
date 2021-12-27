using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Models
{
    public interface IAudioRecordingWriter
    {
        void ProcessAudio(ConcurrentQueue<ClientAudio>[] queues);
        void Stop();
        void Start();
    }
}
