using System;

namespace NGuava
{
    //thats just for test.
    public class Listener
    {
        [Subscribe]
        public void Send(string s)
        {
            Console.WriteLine(s);
        }

        public void Send2(string s)
        {
            //doing nothing.
        }

        private void Send3(string s)
        {
        }

        public virtual void Send4(string s)
        {
        }
    }
}