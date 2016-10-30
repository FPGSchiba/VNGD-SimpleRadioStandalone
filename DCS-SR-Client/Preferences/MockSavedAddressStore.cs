using System.Collections.Generic;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Preferences
{
    public class MockSavedAddressStore : ISavedAddressStore
    {
        public IEnumerable<SavedAddress> LoadFromStore()
        {
            yield return new SavedAddress("test 1", "123.456", true);
            yield return new SavedAddress("test 2", "123.456", false);
            yield return new SavedAddress("test 3", "123.456", false);
        }

        public void SaveToStore(IEnumerable<SavedAddress> savedAddresses)
        {
            // nothing :D
        }
    }
}