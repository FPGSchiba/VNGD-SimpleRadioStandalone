using System.Collections.Generic;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Preferences
{
    public class MockSavedAddressStore : ISavedAddressStore
    {
        public IEnumerable<AddressSetting> LoadFromStore()
        {
            yield return new AddressSetting("test 1", "123.456", true);
            yield return new AddressSetting("test 2", "123.456", false);
            yield return new AddressSetting("test 3", "123.456", false);
        }

        public bool SaveToStore(IEnumerable<AddressSetting> savedAddresses)
        {
            return true;
        }
    }
}