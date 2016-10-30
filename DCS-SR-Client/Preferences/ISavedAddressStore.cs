using System.Collections.Generic;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Preferences
{
    public interface ISavedAddressStore
    {
        IEnumerable<SavedAddress> LoadFromStore();

        void SaveToStore(IEnumerable<SavedAddress> savedAddresses);
    }
}