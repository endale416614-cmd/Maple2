using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.Tests.Data;
using Grpc.Core;
using Maple2.Server.Game.Util.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Tests.Offline;

/// <summary>VR-01a: a field that ticks on real data. A FieldManager on a real map of maple-data.db whose Update() the test calls by
/// hand on a clock it sets, one bare player whose sends can be read, and helpers to spawn an NPC by id, advance game time, read the
/// player's hits and count the field's regions. The field has no thread and none of the map's own spawns, portals, triggers or
/// regions: only what the test puts in. No database of saves, no network.</summary>
internal sealed class FieldRig : IDisposable {
    /// <summary>Taken from the first map that loads: a wide map with a navigation mesh and a player spawn.</summary>
    public const int DefaultMapId = 2000001;
    public const int PlayerLevel = 60;

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>The time between two Update() calls; the real field sleeps 15 ms between them.</summary>
    public const int StepMs = 50;

    /// <summary>Every log line (warning and above) the field and the NPCs wrote; an exception the guard in Update caught is one.</summary>
    public readonly List<string> Errors = [];
    private sealed class Sink(List<string> lines) : ILogEventSink {
        public void Emit(LogEvent logEvent) {
            if (logEvent.Level >= LogEventLevel.Error) {
                lock (lines) {
                    lines.Add(logEvent.RenderMessage() + (logEvent.Exception != null ? " :: " + logEvent.Exception.GetType().Name + ": " + logEvent.Exception.Message + " at " + TopFrame(logEvent.Exception) : ""));
                }
            }
        }
    }

    /// <summary>The World server: every call returns an empty answer, so a player's updates to it go nowhere.</summary>
    private sealed class NoWorld : CallInvoker {
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => default!;
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }

    private static string TopFrame(Exception ex) => string.Join(" | ", (ex.StackTrace ?? "").Split((char) 10).Take(6).Select(frame => frame.Trim())); // VR-05: six frames, so a null deep in a callee is found from the report

    private readonly ILogger previousLogger;
    private readonly SqliteConnection connection;
    private readonly MetadataContext context;
    public readonly NpcMetadataStorage Npcs;
    public readonly SkillMetadataStorage Skills;
    public readonly TableMetadataStorage Tables;
    public readonly ServerTableMetadataStorage ServerTables;
    public readonly FieldManager Field;
    public readonly GameSession Session;
    public readonly FieldPlayer Player;
    private readonly BlockingCollection<(byte[] packet, int length)> sent;
    private long now;

    /// <summary>Every NPC of the data that has an AI path, by id.</summary>
    public IReadOnlyList<NpcMetadata> NpcsWithAi => context.NpcMetadata.ToList().Where(npc => !string.IsNullOrEmpty(npc.AiPath)).OrderBy(npc => npc.Id).ToList();

    /// <summary>The game time since the rig was made, in ms.</summary>
    public long Elapsed { get; private set; }

    /// <summary>The rig, or null when maple-data.db is missing (the caller skips).</summary>
    public static FieldRig? TryOpen(int mapId = DefaultMapId) {
        string? path = DataDb.Find();
        return path == null ? null : new FieldRig(path, mapId);
    }

    private FieldRig(string path, int mapId) {
        previousLogger = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Error().WriteTo.Sink(new Sink(Errors)).CreateLogger();

        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        var maps = new MapMetadataStorage(context);
        var entities = new MapEntityStorage(context);
        var mapData = new MapDataStorage(context);
        Npcs = new NpcMetadataStorage(context);
        Skills = new SkillMetadataStorage(context);
        Tables = new TableMetadataStorage(context);
        ServerTables = new ServerTableMetadataStorage(context);
        var items = new ItemMetadataStorage(context);
        var ai = new AiMetadataStorage(context);

        if (!maps.TryGet(mapId, out MapMetadata? map)) {
            throw new InvalidOperationException($"map {mapId} is not in maple-data.db");
        }
        MapEntityMetadata entity = entities.Get(map.XBlock) ?? throw new InvalidOperationException($"no entities for map {mapId}");

        now = Environment.TickCount64;
        // The constructor only creates the loop thread; Init would start it, and add the map's spawns, so it is not called.
        Field = new FieldManager(map, new UgcMapMetadata(mapId, new Dictionary<int, UgcMapGroup>()), entity, Npcs) {
            MapMetadata = maps,
            MapData = mapData,
            NpcMetadata = Npcs,
            ItemMetadata = items,
            ItemStatsCalc = new ItemStatsCalculator { TableMetadata = Tables }, // VR-05: an NPC that dies makes its drops, and CreateItem reads the stats
            SkillMetadata = Skills,
            RideMetadata = new RideMetadataStorage(context),
            AiMetadata = ai,
            TableMetadata = Tables,
            ServerTableMetadata = ServerTables,
            TickSource = () => now,
        };
        if (!mapData.TryGet(map.XBlock, out FieldAccelerationStructure? acceleration)) {
            throw new InvalidOperationException($"no acceleration structure for map {mapId}");
        }
        Set(Field, "<AccelerationStructure>k__BackingField", acceleration);
        Set(Field, "<FieldTick>k__BackingField", now);
        Set(Field, "initialized", true);
        typeof(FieldManager).GetField("Scheduler", Any)!.FieldType.GetMethod("Start")!.Invoke(typeof(FieldManager).GetField("Scheduler", Any)!.GetValue(Field), null);
        Set(Field, "lastBannerUpdate", DateTimeOffset.MaxValue); // the banner pass reads the save

        sent = new BlockingCollection<(byte[] packet, int length)>(new ConcurrentQueue<(byte[], int)>());
        Session = Bare<GameSession>();
        Set(Session, "sendQueue", sent);
        Set(Session, "lastSentPackets", new ConcurrentDictionary<SendOp, byte[]>());
        Set(Session, "Logger", Log.Logger);
        Set(Session, "<NpcMetadata>k__BackingField", Npcs);
        Set(Session, "<SkillMetadata>k__BackingField", Skills);
        Set(Session, "<TableMetadata>k__BackingField", Tables);
        Set(Session, "<ServerTableMetadata>k__BackingField", ServerTables);
        Set(Session, "<MapMetadata>k__BackingField", maps);
        Set(Session, "<ItemMetadata>k__BackingField", items);
        Session.Field = Field;
        Session.State = SessionState.Connected;
        Set(Session, "<PlayerInfo>k__BackingField", new PlayerInfoStorage(new WorldClient(new NoWorld())));
        Session.Animation = Bare<AnimationManager>();
        var events = Bare<GameEventManager>();
        Set(events, "nextUpdateTick", long.MaxValue); // its first Update would read the save
        Session.GameEvent = events;
        // Trophies and quests: none exist for the rig's player, and every ConditionUpdate passes through both.
        var achievements = new AchievementMetadataStorage(context);
        Set(achievements, "cachedTypes", new HashSet<ConditionType>(Enum.GetValues<ConditionType>()));
        Set(Session, "<AchievementMetadata>k__BackingField", achievements);
        Session.Achievement = Bare<AchievementManager>();
        Session.Ride = new RideManager(Session); // a buff can mount the player and its begin condition asks whether it rides a battle mount
        Session.Currency = new CurrencyManager(Session); // a buff's begin condition reads the mesos
        Session.Dungeon = Bare<DungeonManager>(); // a buff add updates its enter limits (none for the bare player)
        Set(Session.Dungeon, "session", Session);
        Set(Session.Achievement, "session", Session);
        Session.Quest = Bare<QuestManager>();
        Set(Session.Quest, "session", Session);
        Set(Session.Quest, "accountValues", new Dictionary<int, Quest>());
        Set(Session.Quest, "characterValues", new Dictionary<int, Quest>());

        var character = new Character {
            Id = 1, AccountId = 1, Name = "Rig", Level = PlayerLevel, Job = Job.Knight, Gender = Gender.Male,
            MapId = mapId, Mastery = new Mastery(),
        };
        var player = new Player(new Account { Username = "rig" }, character, FieldManager.NextGlobalId()) { Home = new Home(), Currency = new Currency(), Unlock = new Unlock() };
        Player = new FieldPlayer(Session, player);
        Player.Dispose(); // stops its minute schedule (stay_map, playtime), which runs on the system clock and needs the quest managers
        Set(Session, "<Player>k__BackingField", Player);
        Session.Animation = new AnimationManager(Session);
        Set(Player, "<Animation>k__BackingField", Session.Animation);
        Session.Stats = new StatsManager(Player, ServerTables.UserStatTable);
        Session.Buffs = new BuffManager(Player);
        // The stat rebuild needs the equips, the ride, the points and the dungeon managers of a save. A rebuild asked for on the thread that
        // is running one only sets a flag (StatsManager.RunRebuild), so marking this thread as running one turns every rebuild into no work:
        // the bare player's stats are the job's level stats whatever buffs it holds. Build and run the rig on one thread.
        Set(Session.Stats, "rebuildThread", Environment.CurrentManagedThreadId);
        Session.Stats.Values[BasicAttribute.Health].Current = Session.Stats.Values[BasicAttribute.Health].Total;

        SpawnPointPC? spawn = entity.PlayerSpawns.FirstOrDefault();
        if (spawn != null) {
            Player.Position = spawn.Position + new Vector3(0, 0, 25);
        }
        Field.Players[Player.ObjectId] = Player;
    }

    /// <summary>Advances game time by <paramref name="ms"/> in steps of <see cref="StepMs"/>, one Update() each.</summary>
    public void Advance(int ms) {
        for (int done = 0; done < ms; done += StepMs) {
            now += StepMs;
            Elapsed += StepMs;
            if (followRigTime) {
                MoveScheduledEvents();
            }
            Field.Update();
            lastSystemTick = Environment.TickCount64; // after the update: an event it scheduled is early by what the update took, never late
            while (sentAt.Count < sent.Count) {
                sentAt.Add(Elapsed);
            }
            if (Immortal) {
                Stat health = Session.Stats.Values[BasicAttribute.Health];
                health.Current = health.Total;
            }
        }
    }

    /// <summary>VR-02: the field's one-shot delayed events (a missile's hit, a delayed spawn) wait on the system clock, not on the field's tick,
    /// so a test that advances a minute in a second would never see them. With this on, each Advance step brings every one of them
    /// <see cref="StepMs"/> closer and takes back the system time that passed since the last step, so they come due on game time alone.
    /// Off by default: the sweeps ran without it. Repeated events (the field's own minute schedules) are left alone.</summary>
    public bool ScheduledEventsFollowRigTime {
        get => followRigTime;
        set {
            followRigTime = value;
            lastSystemTick = Environment.TickCount64;
        }
    }
    private bool followRigTime;
    private long lastSystemTick = Environment.TickCount64;

    private void MoveScheduledEvents() {
        object queue = typeof(FieldManager).GetField("Scheduler", Any)!.GetValue(Field)!;
        Type queueType = queue.GetType();
        long real = Environment.TickCount64;
        long drift = real - lastSystemTick;
        var events = (System.Collections.IList) queueType.GetField("timedEvents", Any)!.GetValue(queue)!;
        lock (queueType.GetField("mutex", Any)!.GetValue(queue)!) {
            foreach (object scheduled in events) {
                Type eventType = scheduled.GetType();
                var interval = (TimeSpan) eventType.GetField("interval", Any)!.GetValue(scheduled)!;
                if (interval >= TimeSpan.Zero) {
                    continue; // a repeated event
                }
                FieldInfo time = eventType.GetField("<ExecutionTime>k__BackingField", Any)!;
                time.SetValue(scheduled, (long) time.GetValue(scheduled)! + drift - StepMs);
            }
            queueType.GetField("nextTime", Any)!.SetValue(queue, 0L); // InvokeAll looks again
        }
    }

    /// <summary>Advances until <paramref name="done"/> is true or <paramref name="maxMs"/> have passed; says whether it came true.</summary>
    public bool AdvanceUntil(Func<bool> done, int maxMs) {
        for (int passed = 0; passed < maxMs && !done(); passed += StepMs) {
            Advance(StepMs);
        }
        return done();
    }

    /// <summary>Spawns the NPC with its AI at the player (or <paramref name="offset"/> from it). VR-02: <paramref name="ai"/> false spawns it
    /// with no AI, so only the casts a test gives it run; <paramref name="peaceRestore"/> flags its spawn as a map's spawn point does, so it
    /// heals to full when a battle ends.</summary>
    public FieldNpc SpawnNpc(int npcId, Vector3 offset = default, bool ai = true, bool peaceRestore = false) {
        if (!Npcs.TryGet(npcId, out NpcMetadata? metadata)) {
            throw new InvalidOperationException($"npc {npcId} is not in maple-data.db");
        }
        Vector3 position = Player.Position + offset;
        FieldNpc npc = Field.SpawnNpc(metadata, position, Player.Rotation, disableAi: !ai) ?? throw new InvalidOperationException($"npc {npcId} did not spawn");
        if (peaceRestore) {
            Set(npc, "<PeaceRestore>k__BackingField", true);
        }
        return npc;
    }

    /// <summary>VR-02: queues a cast of the NPC's skill at the player's position, as an AI skill node does.</summary>
    public MovementState.NpcSkillCastTask Cast(FieldNpc npc, int skillId, short level = 1) =>
        (MovementState.NpcSkillCastTask) npc.CastAiSkill(skillId, level, 1, Player.Position);

    public void Despawn(FieldNpc npc) {
        Field.Mobs.TryRemove(npc.ObjectId, out _);
        Field.Npcs.TryRemove(npc.ObjectId, out _);
    }

    /// <summary>The player takes hits and is back at full health after every Update, with health no boss kills in one hit: a fight can
    /// run its whole time with the player in it.</summary>
    public bool Immortal {
        get => immortal;
        set {
            immortal = value;
            if (value) {
                Stat health = Session.Stats.Values[BasicAttribute.Health];
                health.Total = health.Base = health.Current = 1_000_000_000_000;
            }
        }
    }
    private bool immortal;

    /// <summary>Back to an empty field: every NPC (and so its crowd agent) and region gone, the player alive at full health with no buffs.</summary>
    public void Clean() {
        foreach (FieldNpc npc in Field.EnumerateNpcs()) {
            Field.RemoveNpc(npc.ObjectId);
        }
        Advance(StepMs); // RemoveNpc is a scheduled event: one Update runs it
        foreach (FieldSkill region in Field.DebugFieldSkills.ToList()) {
            Field.RemoveSkill(region.ObjectId);
        }
        ((ConcurrentDictionary<int, FieldSkill>) typeof(FieldManager).GetField("cubeSkills", Any)!.GetValue(Field)!).Clear();
        Session.Buffs = new BuffManager(Player); // Clear would run each buff's end events, which name casters that are gone
        Session.Stats.Values[BasicAttribute.Health].Current = Session.Stats.Values[BasicAttribute.Health].Total;
    }

    /// <summary>What the player has lost of its health.</summary>
    public long HealthLost => Session.Stats.Values[BasicAttribute.Health].Total - Session.Stats.Values[BasicAttribute.Health].Current;

    /// <summary>The packets sent to the player so far, by opcode; <see cref="ClearSent"/> forgets them.</summary>
    public IReadOnlyList<SendOp> Sent => sent.Select(item => (SendOp) (item.packet[1] << 8 | item.packet[0])).ToList();

    public void ClearSent() {
        while (sent.TryTake(out _)) { }
        sentAt.Clear();
    }

    /// <summary>The rig time of each packet in the send queue, by position (the step of the Advance that sent it).</summary>
    private readonly List<long> sentAt = [];

    /// <summary>The hits the player took: its sends of the damage opcodes. A monster's hit is a skill damage packet.</summary>
    public int Hits => Sent.Count(op => op == SendOp.SkillDamage);

    /// <summary>VR-02: every entry of damage the player took, read from the damage, region and tile packets in its send queue: the kind, the
    /// amount and the skill id (0 in a region packet, which carries none). A hit of several counts is several entries.</summary>
    public IReadOnlyList<(DamageType Type, long Amount, int SkillId, long AtMs)> Damages {
        get {
            var list = new List<(DamageType, long, int, long)>();
            int index = -1;
            foreach ((byte[] packet, int length) in sent) {
                index++;
                if ((SendOp) (packet[1] << 8 | packet[0]) != SendOp.SkillDamage) {
                    continue;
                }
                var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(packet, 2, length - 2));
                byte command = reader.ReadByte();
                int skillId = 0;
                int vector3S = 6, vector3 = 12;
                switch (command) {
                    case 1: // Damage: skill uid, target uid, caster, skill, level, motion, attack, impact, direction
                        reader.ReadInt64(); reader.ReadInt64(); reader.ReadInt32();
                        skillId = reader.ReadInt32();
                        reader.ReadInt16(); reader.ReadByte(); reader.ReadByte();
                        reader.ReadBytes(vector3S * 2);
                        break;
                    case 5: // Region: skill uid, caster, owner, attack
                        reader.ReadInt64(); reader.ReadInt32(); reader.ReadInt32(); reader.ReadByte();
                        break;
                    case 6: // Tile: skill uid, skill, level
                        reader.ReadInt64();
                        skillId = reader.ReadInt32();
                        reader.ReadInt16();
                        break;
                    default:
                        continue;
                }
                int targets = reader.ReadByte();
                for (int i = 0; i < targets; i++) {
                    int objectId = reader.ReadInt32();
                    int count = reader.ReadByte();
                    if (command != 1) {
                        reader.ReadBytes(vector3S + vector3);
                    }
                    for (int j = 0; j < count; j++) {
                        var type = (DamageType) reader.ReadByte();
                        long amount = reader.ReadInt64();
                        if (objectId == Player.ObjectId) {
                            list.Add((type, amount, skillId, index < sentAt.Count ? sentAt[index] : Elapsed));
                        }
                    }
                }
            }
            return list;
        }
    }

    /// <summary>VR-02: the attack points that went out as target packets (command 0): the rig time, the skill and the attack point. A
    /// missile's hit comes later as a damage packet; this is when the attack point itself was reached.</summary>
    public IReadOnlyList<(long AtMs, int SkillId, byte AttackPoint)> AttackPoints {
        get {
            var list = new List<(long, int, byte)>();
            int index = -1;
            foreach ((byte[] packet, int length) in sent) {
                index++;
                if ((SendOp) (packet[1] << 8 | packet[0]) != SendOp.SkillDamage || packet[2] != 0 || length < 25) {
                    continue;
                }
                var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(packet, 3, length - 3));
                reader.ReadInt64(); reader.ReadInt32(); // cast uid, caster
                int skillId = reader.ReadInt32();
                reader.ReadInt16(); reader.ReadByte(); // level, motion
                list.Add((index < sentAt.Count ? sentAt[index] : Elapsed, skillId, reader.ReadByte()));
            }
            return list;
        }
    }

    /// <summary>The field's live regions, the map's own none, and the most of one caster.</summary>
    public int Regions => Field.DebugFieldSkills.Count() + Field.DebugCubeSkills.Count();
    public int MostRegionsOfOneCaster => Field.DebugFieldSkills.Concat(Field.DebugCubeSkills).GroupBy(region => region.Caster.ObjectId).Select(group => group.Count()).DefaultIfEmpty(0).Max();

    public void Dispose() {
        Log.Logger = previousLogger;
        sent.CompleteAdding();
        context.Dispose();
        connection.Dispose();
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static void Set(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }
}
