using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Grpc.Core;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Tools.Scheduler;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Tests.Offline;

/// <summary>Save path: save before cleanup on session end, vault in the atomic save, autosave, tolerant lock and packet handling.</summary>
[NonParallelizable]
public class SavePathTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private string? statusFile;

    [TearDown]
    public void TearDown() {
        Environment.SetEnvironmentVariable("OFFLINE_SAVE_STATUS_FILE", null);
        if (statusFile != null && Directory.Exists(Path.GetDirectoryName(statusFile)!)) {
            Directory.Delete(Path.GetDirectoryName(statusFile)!, recursive: true);
        }
    }

    // A session object without a server, socket or database: only what each test sets.
    private static GameSession BareSession(ILogger? logger = null) {
        var session = (GameSession) RuntimeHelpers.GetUninitializedObject(typeof(GameSession));
        GC.SuppressFinalize(session);
        SetField(session, "Logger", logger ?? Log.Logger);
        return session;
    }

    private static void SetField(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static void SetProperty(object target, string name, object? value) {
        target.GetType().GetProperty(name, Any)!.SetValue(target, value);
    }

    private static object? Call(object target, string name, params object[] args) {
        MethodInfo? method = target.GetType().GetMethod(name, Any);
        Assert.That(method, Is.Not.Null, $"{name} is missing");
        return method!.Invoke(target, args);
    }

    private sealed class Collect : ILogEventSink {
        public readonly List<LogEvent> Events = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    [Test]
    public void Dispose_CleanupThrows_LeaveSaveIsStillCommitted() {
        statusFile = Path.Combine(Path.GetTempPath(), $"offline-save-status-{Guid.NewGuid():N}", "save-status.json");
        Environment.SetEnvironmentVariable("OFFLINE_SAVE_STATUS_FILE", statusFile);

        GameSession session = BareSession();
        var player = (FieldPlayer) RuntimeHelpers.GetUninitializedObject(typeof(FieldPlayer));
        GC.SuppressFinalize(player);
        SetField(player, "<Value>k__BackingField", new Player(new Account { Username = "saver" }, new Character { Name = "Saver", Mastery = null! }, 0) {
            Home = null!,
            Currency = null!,
            Unlock = null!,
        });
        SetProperty(session, "Player", player);
        session.GroupChats = new ConcurrentDictionary<int, GroupChatManager>();
        session.Clubs = new ConcurrentDictionary<long, ClubManager>();
        SetField(session, "activeStatusWritten", true);
        // The save itself succeeds (SaveSafetyTest covers its content); the cleanup throws at once: no scheduler, no server.
        SetField(session, "preMigrationSaved", true);

        session.Dispose();

        string result = JsonDocument.Parse(File.ReadAllText(statusFile)).RootElement.GetProperty("result").GetString()!;
        Assert.That(result, Is.EqualTo("committed"), "a cleanup failure skipped the leave save");
    }

    [Test]
    public void Storage_IsPartOfTheAtomicSave() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        DbContextOptions options = new DbContextOptionsBuilder().UseSqlite(connection).Options;
        using (var context = new Ms2Context(options)) {
            context.Database.EnsureCreated();
        }
        var storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
        long accountId;
        using (GameStorage.Request db = storage.Context()) {
            accountId = db.CreateAccount(new Account { Username = "vault", MachineId = Guid.NewGuid() }, "password").Id;
        }

        GameSession session = BareSession();
        SetProperty(session, "GameStorage", storage);
        SetProperty(session, "AccountId", accountId);
        var vault = new StorageManager(session);
        SetField(vault, "mesos", 500L);

        using (GameStorage.Request db = storage.Context()) {
            bool saved = db.SaveAtomic([
                ("storage", request => vault.Save(request)),
                ("later component", _ => throw new InvalidOperationException("boom")),
            ], out _, out _);
            Assert.That(saved, Is.False);
        }
        Assert.That(Mesos(), Is.Zero, "the vault was written outside the failed snapshot");

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveAtomic([("storage", request => vault.Save(request))], out _, out _), Is.True);
        }
        Assert.That(Mesos(), Is.EqualTo(500));

        long Mesos() {
            using GameStorage.Request db = storage.Context();
            return db.GetStorageInfo(accountId).Mesos;
        }
    }

    [Test]
    public void Autosave_IsScheduledOncePerSession() {
        GameSession session = BareSession();
        SetField(session, "Scheduler", new EventQueue(Log.Logger));

        Call(session, "ScheduleAutosave");
        Call(session, "ScheduleAutosave"); // second map entry

        Assert.That(session.Scheduler.Count, Is.EqualTo(1));
    }

    private sealed class WorldDown : CallInvoker {
        private static RpcException Down() => new(new Status(StatusCode.Unavailable, "world server down"));
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw Down();
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw Down();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw Down();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw Down();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw Down();
    }

    [Test]
    public void AcquireLock_WorldUnreachable_ContinuesWithoutTheLock() {
        GameSession session = BareSession();
        SetProperty(session, "World", new WorldClient(new WorldDown()));

        Assert.DoesNotThrow(() => Call(session, "AcquireLock", 1L, 5));
    }

    private sealed class RouterSession() : Session(null!) {
        protected override PatchType Type => PatchType.Ignore;
    }

    private sealed class BrokenHandler : PacketHandler<RouterSession> {
        public override RecvOp OpCode => RecvOp.ResponseVersion;
        public override void Handle(RouterSession session, IByteReader packet) => throw new InvalidOperationException("handler bug");
    }

    [Test]
    public void PacketRouter_HandlerThrows_SessionThreadKeepsRunning() {
        var session = (RouterSession) RuntimeHelpers.GetUninitializedObject(typeof(RouterSession));
        GC.SuppressFinalize(session);
        var router = new PacketRouter<RouterSession>([new BrokenHandler()]);

        Assert.DoesNotThrow(() => router.OnPacket(session, new ByteReader(BitConverter.GetBytes((ushort) RecvOp.ResponseVersion))));
    }

    [Test]
    public void GearRepair_RunsOncePerCharacter_NotOnEveryMigration() {
        var sink = new Collect();
        ILogger logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        long characterId = Random.Shared.NextInt64(1, long.MaxValue);

        // No database: each attempt logs a warning, so the warnings count the attempts.
        foreach (GameSession session in new[] { BareSession(logger), BareSession(logger) }) {
            SetProperty(session, "CharacterId", characterId);
            Call(session, "RepairGearWithoutStats");
        }

        Assert.That(sink.Events.Count(e => e.Level == LogEventLevel.Warning), Is.EqualTo(1));
    }
}
