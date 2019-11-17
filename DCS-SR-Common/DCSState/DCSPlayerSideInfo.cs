using Ciribob.DCS.SimpleRadio.Standalone.Common.DCSState;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class DCSPlayerSideInfo
    {
        public string name = "";
        public int side = 0;
        public DcsPosition Position { get; set; } = new DcsPosition();
        public DCSLatLngPosition LngLngPosition { get; set; } = new DCSLatLngPosition();

        public void Reset()
        {
            name = "";
            side = 0;
            Position = new DcsPosition();
            LngLngPosition = new DCSLatLngPosition();
        }
    }
}