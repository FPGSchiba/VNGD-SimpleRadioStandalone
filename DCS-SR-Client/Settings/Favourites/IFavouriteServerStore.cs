using System.Collections.Generic;

namespace Vanguard.VCS.Client.Settings.Favourites
{
    public interface IFavouriteServerStore
    {
        IEnumerable<ServerAddress> LoadFromStore();

        bool SaveToStore(IEnumerable<ServerAddress> addresses);
    }
}