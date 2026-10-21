using System.Collections.Immutable;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class GameEventManager {
    private const int BATCH_SIZE = 10;
    private readonly GameSession session;
    private readonly Dictionary<int, Dictionary<GameEventUserValueType, GameEventUserValue>> eventValues;
    // Values of events that were not active at login; Get picks one up when its event turns active.
    private readonly Dictionary<(int EventId, GameEventUserValueType Type), GameEventUserValue> kept = new();
    private long nextUpdateTick;

    private readonly ILogger logger = Log.Logger.ForContext<GameEventManager>();


    public GameEventManager(GameSession session) {
        this.session = session;
        eventValues = new Dictionary<int, Dictionary<GameEventUserValueType, GameEventUserValue>>();
        GetUserValues();
    }

    public void Update(long tickCount) {
        if (nextUpdateTick > tickCount) {
            return;
        }
        nextUpdateTick = tickCount + 1000;

        UpdateDtReward();
    }

    private void GetUserValues() {
        IList<GameEvent> events = session.Events.ToList();

        using GameStorage.Request db = session.GameStorage.Context();
        IList<GameEventUserValue> values = db.GetEventUserValues(session.CharacterId);
        long now = GameClock.Now.ToEpochSeconds();
        foreach (GameEventUserValue userValue in values) {
            // Remove if the value has expired. A value of an event that is not active now stays in the save, unloaded:
            // part-time and offline yearly events (R12-15) come back, and their progress with them.
            if (Expired(userValue, now)) {
                db.RemoveGameEventUserValue(userValue, session.CharacterId);
                continue;
            }
            if (events.All(gameEvent => gameEvent.Id != userValue.EventId)) {
                kept[(userValue.EventId, userValue.Type)] = userValue;
                continue;
            }

            if (!eventValues.TryGetValue(userValue.EventId, out Dictionary<GameEventUserValueType, GameEventUserValue>? valueDict)) {
                eventValues.Add(userValue.EventId, new Dictionary<GameEventUserValueType, GameEventUserValue> {
                    { userValue.Type, userValue },
                });
            } else {
                valueDict.TryAdd(userValue.Type, userValue);
            }
        }
    }

    private void UpdateDtReward() {
        if (session.Field is null) return;

        IList<GameEvent> events = session.FindEvent(GameEventType.DTReward);
        if (events.Count == 0) {
            return;
        }

        foreach (GameEvent gameEvent in events) {
            if (gameEvent.Metadata.Data is not DTReward dtReward) {
                continue;
            }
            DateTime now = GameClock.Now;

            GameEventUserValue rewardIndexValue = Get(GameEventUserValueType.DTRewardRewardIndex, gameEvent.Id, now.AddDays(1).ToEpochSeconds());

            if (dtReward.Entries.Length <= rewardIndexValue.Int()) {
                continue;
            }

            GameEventUserValue userValue = Get(GameEventUserValueType.DTRewardStartTime, gameEvent.Id, now.AddDays(1).ToEpochSeconds());
            Set(gameEvent.Id, GameEventUserValueType.DTRewardStartTime, GameClock.Now.ToEpochSeconds());
            userValue = Get(GameEventUserValueType.DTRewardCurrentTime, gameEvent.Id, now.AddDays(1).ToEpochSeconds());
            Set(gameEvent.Id, GameEventUserValueType.DTRewardCurrentTime, userValue.Long() + 1);

            // Give reward
            if (userValue.Long() >= dtReward.Entries[rewardIndexValue.Int()].EndDuration) {
                DTReward.Entry entry = dtReward.Entries[rewardIndexValue.Int()];
                Item? item = session.Field?.ItemDrop.CreateItem(entry.Item.ItemId, entry.Item.Rarity, entry.Item.Amount);
                if (item == null) {
                    continue;
                }

                using GameStorage.Request db = session.GameStorage.Context();
                var mail = new Mail {
                    Type = MailType.System,
                    ReceiverId = session.CharacterId,
                    SenderId = session.CharacterId,
                    Content = entry.MailContentId.ToString(),
                };

                mail = db.CreateMail(mail);
                if (mail == null) {
                    logger.Error("Failed to create mail for DTReward. Event ID: {EventId}, Reward Index: {RewardIndex}", gameEvent.Id, rewardIndexValue.Int());
                    return;
                }

                item = db.CreateItem(mail.Id, item);
                if (item == null) {
                    logger.Error("Failed to create item for DTReward. Event ID: {EventId}, Reward Index: {RewardIndex}", gameEvent.Id, rewardIndexValue.Int());
                    return;
                }

                mail.Items.Add(item);
                session.Mail.Notify(true);

                Set(gameEvent.Id, GameEventUserValueType.DTRewardRewardIndex, rewardIndexValue.Int() + 1);
                Set(gameEvent.Id, GameEventUserValueType.DTRewardCurrentTime, entry.StartDuration);
            }

            userValue = Get(GameEventUserValueType.DTRewardTotalTime, gameEvent.Id, now.AddDays(1).ToEpochSeconds());
            Set(gameEvent.Id, GameEventUserValueType.DTRewardTotalTime, userValue.Long() + 1);
        }
    }

    public void Set(int gameEventId, GameEventUserValueType type, object value) {
        if (!eventValues.TryGetValue(gameEventId, out Dictionary<GameEventUserValueType, GameEventUserValue>? eventDictionary)) {
            throw new ArgumentOutOfRangeException(nameof(gameEventId), gameEventId, "Invalid game event id.");
        }
        if (!eventDictionary.TryGetValue(type, out GameEventUserValue? gameEventUserValue)) {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Invalid game event type.");
        }

        string newValue = value.ToString() ?? throw new ArgumentException("Invalid value type.");
        gameEventUserValue.SetValue(newValue);
        session.Send(GameEventUserValuePacket.Update(gameEventUserValue));
    }

    public void Load() {
        foreach (ImmutableList<GameEventUserValue> batch in eventValues.Values.SelectMany(dict => dict.Values).Batch(BATCH_SIZE)) {
            session.Send(GameEventUserValuePacket.Load(batch));
        }
    }

    public GameEventUserValue Get(GameEventUserValueType type, int eventId, long expirationTime) {
        if (!eventValues.TryGetValue(eventId, out Dictionary<GameEventUserValueType, GameEventUserValue>? valueDict)) {
            eventValues.Add(eventId, valueDict = new Dictionary<GameEventUserValueType, GameEventUserValue>());
        }
        if (!valueDict.ContainsKey(type)) {
            if (kept.Remove((eventId, type), out GameEventUserValue? stored) && !Expired(stored, GameClock.Now.ToEpochSeconds())) {
                valueDict.Add(type, stored);
                session.Send(GameEventUserValuePacket.Update(stored)); // Load ran at login, before this event was active
            } else {
                valueDict.Add(type, new GameEventUserValue(type, expirationTime, eventId));
            }
        }

        if (Expired(eventValues[eventId][type], GameClock.Now.ToEpochSeconds())) {
            eventValues[eventId][type] = new GameEventUserValue(type, expirationTime, eventId);
        }

        return eventValues[eventId][type];
    }

    public void Save(GameStorage.Request db) {
        // Update certain values upon log off
        // TODO: Maybe update this to handle a list of other types that need to be updated upon logoff?
        IEnumerable<GameEventUserValue> accumulatedTimeValues =
            eventValues.Values.SelectMany(dict => dict.Values.Where(value => value.Type == GameEventUserValueType.AttendanceAccumulatedTime));
        foreach (GameEventUserValue userValue in accumulatedTimeValues) {
            userValue.SetValue((GameClock.Now.AddSeconds(userValue.Long()) - session.Player.Value.Character.LastModified).Seconds.ToString());
        }
        db.Ensure(db.SaveGameEventUserValues(session.CharacterId, eventValues.Values.SelectMany(value => value.Values).ToList()), "game event values");
    }

    public static bool Expired(GameEventUserValue value, long now) => value.ExpirationTime < now;
}
