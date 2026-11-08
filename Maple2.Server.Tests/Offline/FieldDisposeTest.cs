using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.DebugGraphics;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Serilog;

namespace Maple2.Server.Tests.Offline;

/// <summary>A field handed to a player who is loading into it is not disposed as empty (FD-01, BT-130); the dispose loop judges and removes a field under its map's lock and disposes it after the lock (FD-02).</summary>
public class FieldDisposeTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int SylvanWoodsTrail = 2000017;
    private const long Owner = 1;
    private const int RoomA = 1;
    private const int RoomB = 2;
    private static readonly TimeSpan Blocked = TimeSpan.FromMilliseconds(300); // long enough to see a thread that does not wait for a lock
    private static readonly TimeSpan Stuck = TimeSpan.FromSeconds(30); // only a deadlock takes this long

    private readonly ConcurrentQueue<Exception> threadFailures = new();
#pragma warning disable NUnit1032 // The bare factory's Dispose needs a loop thread, and a failed test can leave a thread waiting on a lock.
    private FieldManager.Factory factory = null!;
    private SemaphoreSlim mapLock = null!;
    private SemaphoreSlim homeLock = null!;
#pragma warning restore NUnit1032
    private ConcurrentDictionary<int, FieldManager> rooms = null!;
    private ConcurrentDictionary<long, HomeFieldManager> homes = null!;

    private static DateTime LongEmpty => DateTime.UtcNow - TimeSpan.FromSeconds(610); // 10 min 10 s, report 9e897098

    [SetUp]
    public void NewFactory() {
        mapLock = new SemaphoreSlim(1, 1);
        homeLock = new SemaphoreSlim(1, 1);
        rooms = new ConcurrentDictionary<int, FieldManager>();
        homes = new ConcurrentDictionary<long, HomeFieldManager>();
        var tables = Bare<ServerTableMetadataStorage>();
        SetField(tables, "instanceFieldTable", new Lazy<InstanceFieldTable>(new InstanceFieldTable(new Dictionary<int, InstanceFieldMetadata>())));
        factory = Bare<FieldManager.Factory>(); // no dispose loop thread
        SetField(factory, "<ServerTableMetadata>k__BackingField", tables);
        SetField(factory, "logger", Log.Logger);
        SetField(factory, "cancel", new CancellationTokenSource());
        SetField(factory, "mapLocks", new ConcurrentDictionary<int, SemaphoreSlim> { [SylvanWoodsTrail] = mapLock, [Constant.DefaultHomeMapId] = homeLock });
        SetField(factory, "fields", new ConcurrentDictionary<int, ConcurrentDictionary<int, FieldManager>> { [SylvanWoodsTrail] = rooms });
        SetField(factory, "homes", homes);
        SetField(factory, "dungeons", new ConcurrentDictionary<int, DungeonFieldManager>());
        SetField(factory, "subDungeonFields", new ConcurrentDictionary<int, DungeonFieldManager>());
    }

    [TearDown]
    public void NoThreadFailed() {
        Assert.That(threadFailures, Is.Empty);
    }

    [Test]
    public void Get_RestartsTheEmptyTimer() {
        // The player left the map 10 min 10 s ago and comes back: the dispose loop's next pass would dispose the field during the load.
        var field = Bare<FieldManager>();
        SetField(field, "fieldEmptySince", DateTime.UtcNow - TimeSpan.FromSeconds(610));
        var tables = Bare<ServerTableMetadataStorage>();
        SetField(tables, "instanceFieldTable", new Lazy<InstanceFieldTable>(new InstanceFieldTable(new Dictionary<int, InstanceFieldMetadata>())));
        var factory = Bare<FieldManager.Factory>(); // no dispose loop thread
        SetField(factory, "<ServerTableMetadata>k__BackingField", tables);
        SetField(factory, "mapLocks", new ConcurrentDictionary<int, SemaphoreSlim>());
        SetField(factory, "fields", new ConcurrentDictionary<int, ConcurrentDictionary<int, FieldManager>> {
            [SylvanWoodsTrail] = new ConcurrentDictionary<int, FieldManager> { [field.RoomId] = field },
        });

        Assert.That(factory.Get(SylvanWoodsTrail), Is.SameAs(field));
        Assert.That(GetField(field, "fieldEmptySince"), Is.Null, "the field still counts as empty for 10 minutes while the player loads");
    }

    [Test]
    public void AGetInProgressKeepsTheField() {
        FieldManager field = AddRoom(RoomA, LongEmpty);

        // Get holds the map's lock and restarts the timer while the pass is on its way to this field.
        mapLock.Wait();
        Thread pass = Start(RunPass);
        Assert.That(pass.Join(Blocked), Is.False, "the pass did not wait for the map's lock");
        SetField(field, "fieldEmptySince", null);
        mapLock.Release();

        Assert.That(pass.Join(Stuck), Is.True);
        Assert.That(field.Disposed, Is.False);
        Assert.That(rooms.ContainsKey(RoomA), Is.True);
        Assert.That(GetField(field, "fieldEmptySince"), Is.Not.Null, "the pass starts a new timer");
    }

    [Test]
    public void AGetInProgressKeepsTheHome() {
        HomeFieldManager home = AddHome(LongEmpty);

        homeLock.Wait();
        Thread pass = Start(RunPass);
        Assert.That(pass.Join(Blocked), Is.False, "the pass did not wait for the home map's lock");
        SetField(home, "fieldEmptySince", null);
        homeLock.Release();

        Assert.That(pass.Join(Stuck), Is.True);
        Assert.That(home.Disposed, Is.False);
        Assert.That(homes.ContainsKey(Owner), Is.True);
    }

    [Test]
    public void GetDuringDisposeDoesNotReturnTheField() {
        // The field's thread holds Dispose open, so Get falls in the time of one Dispose that FD-01 left.
        var release = new ManualResetEventSlim();
        var disposing = new ManualResetEventSlim();
        FieldManager going = AddRoom(RoomA, LongEmpty, Start(release.Wait), disposing.Set);
        FieldManager staying = AddRoom(RoomB, null);

        Thread pass = Start(RunPass);
        Assert.That(disposing.Wait(Stuck), Is.True);

        FieldManager? got = null;
        Assert.That(Start(() => got = factory.Get(SylvanWoodsTrail)).Join(Stuck), Is.True, "Dispose runs under the map's lock");
        Assert.That(got, Is.SameAs(staying));
        Assert.That(going.Disposed, Is.False);

        release.Set();
        Assert.That(pass.Join(Stuck), Is.True);
        Assert.That(going.Disposed, Is.True);
    }

    [Test]
    public void AFieldThreadInGetDoesNotBlockItsOwnDispose() {
        // A queued handler (QuestHandler, TaxiHandler) runs on the field's thread and calls Get. Dispose joins that thread, so under the lock each would wait for the other.
        var disposing = new ManualResetEventSlim();
        FieldManager? got = null;
        Thread fieldThread = Start(() => {
            disposing.Wait();
            got = factory.Get(SylvanWoodsTrail);
        });
        FieldManager going = AddRoom(RoomA, LongEmpty, fieldThread, disposing.Set);
        FieldManager staying = AddRoom(RoomB, null);

        Thread pass = Start(RunPass);

        Assert.That(pass.Join(Stuck), Is.True, "the pass and the field's thread wait for each other");
        Assert.That(got, Is.SameAs(staying));
        Assert.That(going.Disposed, Is.True);
    }

    [Test]
    public void AHomeIsOffItsListWithTheLockFreeWhenItsDisposeStarts() {
        bool listed = true;
        int free = 0;
        HomeFieldManager home = AddHome(LongEmpty, () => {
            listed = homes.ContainsKey(Owner);
            free = homeLock.CurrentCount;
        });

        RunPass();

        Assert.That(listed, Is.False);
        Assert.That(free, Is.EqualTo(1));
        Assert.That(home.Disposed, Is.True);
    }

    [Test]
    public void AnExpiredRoomIsOffItsListWithTheLockFreeWhenItsDisposeStarts() {
        bool listed = true;
        int free = 0;
        FieldManager room = AddRoom(RoomA, null, onDispose: () => {
            listed = rooms.ContainsKey(RoomA);
            free = mapLock.CurrentCount;
        });
        SetField(room, "<RoomTimer>k__BackingField", new RoomTimer(room, RoomTimerType.Clock, -1));

        RunPass();

        Assert.That(listed, Is.False);
        Assert.That(free, Is.EqualTo(1));
        Assert.That(room.Disposed, Is.True);
    }

    [TestCase(590, false)]
    [TestCase(610, true)]
    public void TheEmptyTimeIsTenMinutes(int emptySeconds, bool disposed) {
        DateTime since = DateTime.UtcNow - TimeSpan.FromSeconds(emptySeconds);
        FieldManager field = AddRoom(RoomA, since);
        HomeFieldManager home = AddHome(since);

        RunPass();

        Assert.That(field.Disposed, Is.EqualTo(disposed));
        Assert.That(rooms.ContainsKey(RoomA), Is.Not.EqualTo(disposed));
        Assert.That(home.Disposed, Is.EqualTo(disposed));
        Assert.That(homes.ContainsKey(Owner), Is.Not.EqualTo(disposed));
    }

    [Test]
    public void PlayersClearTheTimer() {
        FieldManager field = AddRoom(RoomA, LongEmpty);
        HomeFieldManager home = AddHome(LongEmpty);
        field.Players[1] = Bare<FieldPlayer>();
        home.Players[1] = Bare<FieldPlayer>();

        RunPass();

        Assert.That(field.Disposed, Is.False);
        Assert.That(GetField(field, "fieldEmptySince"), Is.Null);
        Assert.That(home.Disposed, Is.False);
        Assert.That(GetField(home, "fieldEmptySince"), Is.Null);
    }

    [Test]
    public void ShutdownLetsTheLoopEndBeforeTheLocksGo() {
        // The loop's thread has no catch: a lock disposed under a pass would end the process.
        var release = new ManualResetEventSlim();
        var disposing = new ManualResetEventSlim();
        FieldManager going = AddRoom(RoomA, LongEmpty, Start(release.Wait), disposing.Set);
        FieldManager staying = AddRoom(RoomB, null);
        SetField(factory, "thread", Start(() => Invoke("DisposeLoop")));
        Assert.That(disposing.Wait(Stuck), Is.True);

        Thread stop = Start(factory.Dispose);
        Assert.That(stop.Join(Blocked), Is.False, "the factory did not wait for its loop");
        Assert.That(mapLock.Wait(0), Is.True, "the pass holds the lock during Dispose"); // throws ObjectDisposedException if the locks went first
        mapLock.Release();

        release.Set();
        Assert.That(stop.Join(Stuck), Is.True);
        Assert.That(going.Disposed, Is.True);
        Assert.That(staying.Disposed, Is.True);
    }

    private void RunPass() => Invoke("DisposeExpiredFields");

    private void Invoke(string method) {
        try {
            typeof(FieldManager.Factory).GetMethod(method, Any)!.Invoke(factory, null);
        } catch (TargetInvocationException e) {
            throw e.InnerException!;
        }
    }

    // Background, so a deadlock fails the test instead of holding the test host open.
    private Thread Start(Action action) {
        var thread = new Thread(() => {
            try {
                action();
            } catch (Exception e) {
                threadFailures.Enqueue(e);
            }
        }) { IsBackground = true };
        thread.Start();
        return thread;
    }

    private FieldManager AddRoom(int roomId, DateTime? emptySince, Thread? thread = null, Action? onDispose = null) {
        var field = NewField<FieldManager>(SylvanWoodsTrail, emptySince, thread, onDispose);
        rooms[roomId] = field;
        return field;
    }

    private HomeFieldManager AddHome(DateTime? emptySince, Action? onDispose = null) {
        var home = NewField<HomeFieldManager>(Constant.DefaultHomeMapId, emptySince, null, onDispose);
        SetField(home, "<Home>k__BackingField", new Home());
        homes[Owner] = home;
        return home;
    }

    private T NewField<T>(int mapId, DateTime? emptySince, Thread? thread, Action? onDispose) where T : FieldManager {
        var field = Bare<T>();
        SetField(field, "<MapId>k__BackingField", mapId);
        SetField(field, "<Players>k__BackingField", new ConcurrentDictionary<int, FieldPlayer>());
        SetField(field, "fieldAdBalloons", new ConcurrentDictionary<string, FieldInteract>());
        SetField(field, "fieldEmptySince", emptySince);
        SetField(field, "logger", Log.Logger);
        SetField(field, "cancel", new CancellationTokenSource());
        SetField(field, "thread", thread ?? Start(() => { }));
        SetField(field, "<Navigation>k__BackingField", Bare<Navigation>());
        SetField(field, "<DebugGraphicsContext>k__BackingField", new DisposeWatch(onDispose));
        return field;
    }

    /// <summary>A field's Dispose calls FieldRemoved before it stops the field's thread.</summary>
    private sealed class DisposeWatch(Action? onDispose) : IGraphicsContext {
        public void Initialize() { }
        public void CleanUp() { }
        public IFieldRenderer? FieldAdded(FieldManager field) => null;
        public void FieldRemoved(FieldManager field) => onDispose?.Invoke();
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static FieldInfo FindField(object target, string name) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) {
                return field;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static void SetField(object target, string name, object? value) => FindField(target, name).SetValue(target, value);

    private static object? GetField(object target, string name) => FindField(target, name).GetValue(target);
}
