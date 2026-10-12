using Maple2.Model.Game;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        public IList<SystemBanner> GetBanners() {
            DateTime now = DateTime.Now;
            return Context.SystemBanner
                .Where(banner => banner.EndTime > now)
                .Select<Model.SystemBanner, SystemBanner>(banner => banner)
                .ToList();
        }
    }
}
