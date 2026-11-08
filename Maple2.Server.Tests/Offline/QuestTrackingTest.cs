using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Offline;

/// <summary>QT-01 (BT-178): the tracker toggle is stored on the quest, saved with it and sent at the next login.
/// Golden: the LoadQuestStates row of started epic 50100780 with Track off, capture `Priest_-_Rank_2_-_Left-tree.msb`.</summary>
[NonParallelizable]
public class QuestTrackingTest {
    private const int QuestId = 50100780;
    private const long StartTime = 1560038763;
    private const long AccountId = 1;
    private const long CharacterId = 2;
    private const byte CommandTracking = 9;

    // id, state, completions, start, end, track, condition count, counter
    private static readonly byte[] TrackOffRow = Convert.FromHexString("2C7AFC02" + "01000000" + "00000000" + "6B4DFC5C00000000" + "0000000000000000" + "00" + "01000000" + "00000000");

    private static readonly QuestMetadata Metadata = new(QuestId, null, null!, null!, null!, null!, null!, null!, null!, null!, null, null, null, QuestEventMissionType.none,
        [new ConditionMetadata(ConditionType.map, 1, null, null)]);

    // The one quest a load looks up: no game data needed.
    private sealed class OneQuest : QuestMetadataStorage {
        public OneQuest() : base(null!) => Cache.AddReplace(QuestId, Metadata);
    }

    private SqliteConnection connection = null!;
    private GameStorage storage = null!;

    [SetUp]
    public void SetUp() {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        DbContextOptions options = new DbContextOptionsBuilder().UseSqlite(connection).Options;
        using (var context = new Ms2Context(options)) {
            context.Database.EnsureCreated();
        }
        storage = new GameStorage(options, null!, null!, null!, new OneQuest(), null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    [TearDown]
    public void TearDown() => connection.Dispose();

    // As QuestManager.Start creates it: tracked.
    private Quest Accepted() {
        var quest = new Quest(Metadata) { Track = true, State = QuestState.Started, StartTime = StartTime };
        quest.Conditions.Add(0, new Quest.Condition(Metadata.Conditions[0]));
        using GameStorage.Request db = storage.Context();
        return db.CreateQuest(CharacterId, quest)!;
    }

    // A session without a socket, holding the quest. A disposed session drops the echo.
    private static GameSession SessionWith(Quest quest) {
        var session = Bare<GameSession>();
        SetField(session, "disposed", true);
        SetField(session, "<AccountId>k__BackingField", AccountId);
        SetField(session, "<CharacterId>k__BackingField", CharacterId);
        var manager = Bare<QuestManager>();
        SetField(manager, "session", session);
        SetField(manager, "accountValues", new Dictionary<int, Quest>());
        SetField(manager, "characterValues", new Dictionary<int, Quest> { [quest.Id] = quest });
        session.Quest = manager;
        return session;
    }

    private static void Toggle(GameSession session, bool tracking) {
        var writer = new ByteWriter();
        writer.WriteByte(CommandTracking);
        writer.WriteInt(QuestId);
        writer.WriteBool(tracking);
        new QuestHandler { TableMetadata = null!, ServerTableMetadata = null! }.Handle(session, new ByteReader(writer.Buffer[..writer.Length]));
    }

    // What the next login's QuestManager reads after the session saved.
    private Quest SavedAndLoaded(GameSession session) {
        using (GameStorage.Request db = storage.Context()) {
            session.Quest.Save(db);
        }
        using GameStorage.Request read = storage.Context();
        return read.GetQuests(CharacterId)[QuestId];
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TheToggleIsStoredOnTheQuest(bool tracking) {
        Quest quest = Accepted();
        quest.Track = !tracking;

        Toggle(SessionWith(quest), tracking);

        Assert.That(quest.Track, Is.EqualTo(tracking));
    }

    [Test]
    public void ACompletedQuestKeepsItsFlag() {
        Quest quest = Accepted();
        quest.State = QuestState.Completed;

        Toggle(SessionWith(quest), false);

        Assert.That(quest.Track, Is.True);
    }

    [Test]
    public void TheToggleLoadsBackOffThenOn() {
        Quest quest = Accepted();
        GameSession session = SessionWith(quest);

        Toggle(session, false);
        Assert.That(SavedAndLoaded(session).Track, Is.False);

        Toggle(session, true);
        Assert.That(SavedAndLoaded(session).Track, Is.True);
    }

    [Test]
    public void TheNextLoginSendsTheCapturedRow() {
        GameSession session = SessionWith(Accepted());
        Toggle(session, false);

        var writer = new ByteWriter();
        SavedAndLoaded(session).WriteTo(writer);

        Assert.That(writer.Buffer[..writer.Length], Is.EqualTo(TrackOffRow));
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static void SetField(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }
}
