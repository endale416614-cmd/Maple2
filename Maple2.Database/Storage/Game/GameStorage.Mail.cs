using Maple2.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using Mail = Maple2.Model.Game.Mail;
using Item = Maple2.Model.Game.Item;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    // Days a mail stays in the mailbox when its sender set no expiry. The client hides mail whose expiry has passed, and an
    // expiry of 0 has passed, so every stored mail needs one. The game server sets this from the server constants.
    public static int MailExpiryDays { get; set; } = 30;

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
            // Same numbering as the MySQL schema (mail ids start at 100000).
            return Math.Max(100000, (Context.Mail.Max(existing => (long?) existing.Id) ?? 0) + 1);
        }

        // Mails stored without an expiry (every sender but one stopped setting it upstream) were invisible in the mailbox,
        // together with their items. They get a fresh expiry, counted from now since the player never saw them.
        private void RepairMailExpiry(long characterId) {
            DateTime missing = DateTime.UnixEpoch.AddDays(1);
            DateTime expiry = DateTimeOffset.UtcNow.AddDays(MailExpiryDays).ToUnixTimeSeconds().FromEpochSeconds();
            Context.Mail.Where(mail => mail.ReceiverId == characterId && mail.ExpiryTime < missing)
                .ExecuteUpdate(setters => setters.SetProperty(mail => mail.ExpiryTime, expiry));
        }

        public Mail? CreateMail(Mail mail) {
            if (mail.ExpiryTime <= 0) {
                mail.ExpiryTime = DateTimeOffset.UtcNow.AddDays(MailExpiryDays).ToUnixTimeSeconds();
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

        public bool DeleteMail(long mailId, long characterId) {
            Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;

            Model.Mail? mail = Context.Mail.Find(characterId, mailId);
            if (mail == null) {
                return false;
            }

            Context.Mail.Remove(mail);
            return SaveChanges();
        }

        public Mail? UpdateMail(Mail mail) {
            Model.Mail model = mail;
            Context.Mail.Update(model);
            return SaveChanges() ? model : null;
        }
    }
}
