using System.Collections.Generic;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Preferences
{
    public interface ISavedAddressStore
    {
        IEnumerable<AddressSetting> LoadFromStore();

        bool SaveToStore(IEnumerable<AddressSetting> savedAddresses);
    }
}