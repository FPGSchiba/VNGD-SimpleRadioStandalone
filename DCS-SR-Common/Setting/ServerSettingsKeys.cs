using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Setting
{
    public enum ServerSettingsKeys
    {
        SERVER_PORT = 0,
        COALITION_AUDIO_SECURITY = 1,
        SPECTATORS_AUDIO_DISABLED = 2,
        CLIENT_EXPORT_ENABLED = 3,
        LOS_ENABLED = 4,
        DISTANCE_ENABLED = 5,
        IRL_RADIO_TX = 6,
        IRL_RADIO_RX_INTERFERENCE = 7,
        IRL_RADIO_STATIC = 8, // Not used
        RADIO_EXPANSION = 9,
        EXTERNAL_AWACS_MODE = 10,
        EXTERNAL_AWACS_MODE_BLUE_PASSWORD = 11,
        EXTERNAL_AWACS_MODE_RED_PASSWORD = 12
    }
}
