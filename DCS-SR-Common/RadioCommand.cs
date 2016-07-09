namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class RadioCommand
    {
        public enum CmdType
        {
            FREQUENCY = 1,
            VOLUME = 2,
            SELECT = 3
        }

        public CmdType cmdType;

        public double freq = 1;
        public int radio;
        public float volume = 1.0f;
    }
}