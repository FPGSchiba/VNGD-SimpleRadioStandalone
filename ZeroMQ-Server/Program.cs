using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;

using NetMQ;

namespace ZeroMQ_Server
{
    class Program
    {
        static void Main(string[] args)
        {

            using (NetMQContext ctx = NetMQContext.Create())
            using (NetMQSocket snapshot = ctx.CreateSocket(ZmqSocketType.Router))
            using (NetMQSocket publisher = ctx.CreateSocket(ZmqSocketType.Pub))
            using (NetMQSocket collector = ctx.CreateSocket(ZmqSocketType.Pull))
            {
                
                snapshot.Bind("tcp://*:5556");

                
                publisher.Bind("tcp://*:5557");

                
                collector.Bind("tcp://*:5558");

                Poller poller = new Poller();

             //   poller.AddPollInSocket(collector);
             //   poller.AddPollInSocket(snapshot);
            }
        }
    }
}
