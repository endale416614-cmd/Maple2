using Maple2.Database.Context;
using Maple2.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mail = Maple2.Model.Game.Mail;
using Item = Maple2.Model.Game.Item;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    // Days a mail stays in the mailbox when its sender set no expiry. The client hides mail whose expiry has passed, and an
    // expiry of 0 has passed, so every stored mail needs one. The game server sets this from the server constants.
    public static int MailExpiryDays { get; set; } = 30;

    // Offline edition (OFFLINE_MAIL_NEVER_EXPIRES=true, launcher.ini MailNeverExpires): mail stays until the player deletes
    // it. Its expiry is kept about five years ahead (inside the 32-bit time range) and moved forward when mail is loaded.
    public static bool MailNeverExpires { get; set; } =
        OfflineProfile.Flag("OFFLINE_MAIL_NEVER_EXPIRES", true);
    public const int NeverExpiresDays = 1825;

    // Mail ids start at 100000 and stay below the first account id, so an item owned by an id in this range is a mail attachment.
    public const long FirstMailId = 100000;
    public const long MailIdLimit = 10000000000;

    public partial class Request {
        public Mail? GetMail(long mailId, long characterId) {
            Model.Mail? model = Context.Mail.Find(characterId, mailId);
            if (model == null) {
                return null;
            }

            Mail mail = model;
            foreach (Item item in GetAllItems(mailId)) {
                mail.Items.Add(item);
            }

            return mail;
        }

        public ICollection<Mail> GetSentMail(long characterId) {
            Mail[] mails = Context.Mail.Where(mail => mail.SenderId == characterId)
                .AsEnumerable()
                .Select<Model.Mail, Mail>(mail => mail)
                .ToArray();

            foreach (Mail mail in mails) {
                foreach (Item item in GetAllItems(mail.Id)) {
                    mail.Items.Add(item);
                }
            }

            return mails;
        }

        // Binds all mails from an account to the first character that access them
        public void BindAccountMailsToCharacter(long accountId, long characterId) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            BeginTransaction();

            List<Model.Mail> mails = Context.Mail.Where(mail => mail.ReceiverId == accountId).ToList();
            if (mails.Count == 0) {
                Commit();
                return;
            }

            foreach (Model.Mail mail in mails) {
                Context.Mail.Remove(mail);
            }

            Context.SaveChanges();

            foreach (Model.Mail mail in mails) {
                mail.ReceiverId = characterId;
                Context.Mail.Add(mail);
            }

            Context.SaveChanges();

            if (!Commit()) {
                throw new Exception("Failed to bind account mails to character");
            }
        }

        public ICollection<Mail> GetAllMail(long characterId, long minId = 0) {
            RepairMailExpiry(characterId);
            Mail[] mails = Context.Mail.Where(mail => mail.ReceiverId == characterId)
                .Where(mail => mail.Id > minId)
                .AsEnumerable()
                .Select<Model.Mail, Mail>(mail => mail)
                .ToArray();

            foreach (Mail mail in mails) {
                foreach (Item item in GetAllItems(mail.Id)) {
                    mail.Items.Add(item);
                }
            }

            return mails;
        }

        private long NextSqliteMailId() {
            // Same numbering as the MySQL schema (mail ids start at 100000). Ids that still own items are skipped: a collected
            // item keeps the mail id as owner until the inventory is saved, and a new mail with that id would show it again.
            long maxMail = Context.Mail.Max(existing => (long?) existing.Id) ?? 0;
            long maxOwner = Context.Item.Where(item => item.OwnerId >= FirstMailId && item.OwnerId < MailIdLimit)
                .Max(item => (long?) item.OwnerId) ?? 0;
            return Math.Max(FirstMailId, Math.Max(maxMail, maxOwner) + 1);
        }

        // Mails stored without an expiry (every sender but one stopped setting it upstream) were invisible in the mailbox,
        // together with their items. They get a fresh expiry, counted from now since the player never saw them. With
        // MailNeverExpires every mail whose expiry is less than half the never-expires span away is moved forward again,
        // which also brings back mail that had already expired.
        private void RepairMailExpiry(long characterId) {
            DateTime cutoff = MailNeverExpires ? DateTime.Now.AddDays(NeverExpiresDays / 2) : DateTime.UnixEpoch.AddDays(1);
            DateTime expiry = NewExpiry().FromEpochSeconds();
            Context.Mail.Where(mail => mail.ReceiverId == characterId && mail.ExpiryTime < cutoff)
                .ExecuteUpdate(setters => setters.SetProperty(mail => mail.ExpiryTime, expiry));
        }

        private static long NewExpiry() {
            return DateTimeOffset.UtcNow.AddDays(MailNeverExpires ? NeverExpiresDays : MailExpiryDays).ToUnixTimeSeconds();
        }

        public Mail? CreateMail(Mail mail) {
            if (mail.ExpiryTime <= 0 || MailNeverExpires) {
                mail.ExpiryTime = Math.Max(mail.ExpiryTime, NewExpiry());
            }
            Model.Mail model = mail;
            // SQLite cannot auto-number a column inside a composite key (ReceiverId, Id): assign the next id here.
            model.Id = Maple2.Database.Context.DbProvider.IsSqlite ? NextSqliteMailId() : 0;
            if (mail.Items.Count == 0) {
                Context.Mail.Add(model);
                return SaveChanges() ? model : null;
            }

            BeginTransaction();
            Context.Mail.Add(model);
            if (!SaveChanges()) {
                return null;
            }

            if (!SaveItems(model.Id, mail.Items.ToArray())) {
                return null;
            }
            if (!Commit()) {
                return null;
            }

            Mail updatedMail = model;
            foreach (Item item in mail.Items) {
                updatedMail.Items.Add(item);
            }

            return updatedMail;
        }

        public Mail? MarkMailRead(long mailId, long characterId) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            Model.Mail? mail = Context.Mail.Find(characterId, mailId);
            if (mail == null || mail.ReadTime > DateTime.Now) {
                return null;
            }

            mail.ReadTime = DateTime.Now;
            Context.Mail.Update(mail);
            return SaveChanges() ? mail : null;
        }

        // Deletes the mail together with the attachments still on it. Only the given items go: a collected item stays stored
        // under the mail id until the inventory is saved, so deleting every item the mail owns would destroy it.
        public bool DeleteMail(long mailId, long characterId, ICollection<long> attachedItemUids) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            Model.Mail? mail = Context.Mail.Find(characterId, mailId);
            if (mail == null) {
                return false;
            }

            BeginTransaction();
            Context.Mail.Remove(mail);
            if (!SaveChanges()) {
                Rollback();
                return false;
            }
            if (attachedItemUids.Count > 0) {
                Context.Item.Where(item => item.OwnerId == mailId && attachedItemUids.Contains(item.Id)).ExecuteDelete();
            }
            return Commit();
        }

        // Attachments of mail that no longer exists (mail used to be deleted without its items). Run at world start, when no
        // player holds a collected item that is still stored under its mail id.
        public void DeleteOrphanedMailItems() {
            int deletedCount = Context.Item
                .Where(item => item.OwnerId >= FirstMailId && item.OwnerId < MailIdLimit && !Context.Mail.Any(mail => mail.Id == item.OwnerId))
                .ExecuteDelete();
            if (deletedCount == 0) {
                return;
            }

            Logger.LogInformation("Deleted {Count} items of deleted mail.", deletedCount);
        }

        public Mail? UpdateMail(Mail mail) {
            Model.Mail model = mail;
            Context.Mail.Update(model);
            return SaveChanges() ? model : null;
        }
    }
}
