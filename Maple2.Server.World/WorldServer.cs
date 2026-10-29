using System.Collections.Concurrent;
using Grpc.Core;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Channel.Service;
using Maple2.Server.World.Containers;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using Maple2.Tools.Scheduler;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;
using LoginClient = Maple2.Server.Login.Service.Login.LoginClient;


namespace Maple2.Server.World;

public class WorldServer {
    #region Autofac Autowired
    private readonly GameStorage gameStorage;
    private readonly ChannelClientLookup channelClients;
    private readonly ServerTableMetadataStorage serverTableMetadata;
    private readonly ItemMetadataStorage itemMetadata;
    private readonly TableMetadataStorage tableMetadata;
    private readonly GlobalPortalLookup globalPortalLookup;
    private readonly WorldBossLookup worldBossLookup;
    private readonly PlayerInfoLookup playerInfoLookup;
    private readonly GuildLookup guildLookup;
    private ConstantsTable Constants => serverTableMetadata.ConstantsTable;
    #endregion

    private readonly Thread thread;
    private readonly Thread heartbeatThread;
    private readonly EventQueue scheduler;
    private readonly CancellationTokenSource tokenSource = new();
    private readonly ConcurrentDictionary<int, string> memoryStringBoards;
    private static int _globalIdCounter;

    private readonly ILogger logger = Log.ForContext<WorldServer>();

    private readonly LoginClient login;

    private long clockTicks; // in-game ticks when World started (T-02)
    private long clockStartedAt; // Unix ms

    public WorldServer(GameStorage gameStorage, ChannelClientLookup channelClients, ServerTableMetadataStorage serverTableMetadata, GlobalPortalLookup globalPortalLookup, WorldBossLookup worldBossLookup, PlayerInfoLookup playerInfoLookup, LoginClient login, ItemMetadataStorage itemMetadata, TableMetadataStorage tableMetadata, GuildLookup guildLookup) {
        this.gameStorage = gameStorage;
        this.tableMetadata = tableMetadata;
        this.channelClients = channelClients;
        this.serverTableMetadata = serverTableMetadata;
        this.globalPortalLookup = globalPortalLookup;
        this.worldBossLookup = worldBossLookup;
        this.playerInfoLookup = playerInfoLookup;
        this.guildLookup = guildLookup;
        this.login = login;
        this.itemMetadata = itemMetadata;
        scheduler = new EventQueue(logger);
        scheduler.Start();
        memoryStringBoards = [];

        // World initialization: set all characters offline and cleanup unowned items
        using GameStorage.Request db = gameStorage.Context();
        db.SetAllCharacterToOffline();
        db.DeleteUnownedItems();
        db.DeleteOrphanedMailItems();
        SeedBlackMarket(db);
        StartClock(db);

        StartDailyReset();
        StartWeeklyReset();
        StartMonthlyReset();
        StartWorldEvents();
        StartWorldBossEvents();
        ScheduleGameEvents();
        FieldPlotExpiryCheck();
        thread = new Thread(Loop);
        thread.Start();

        heartbeatThread = new Thread(Heartbeat);
        heartbeatThread.Start();
    }

    // Offline Black Market (R12-22): a new set of synthetic listings at every World start, before the market is first
    // read; none under Legacy or without offline-market.json (the old set is removed either way).
    private void SeedBlackMarket(GameStorage.Request db) {
        try { // a bad file must not stop World (Play) from starting
            OfflineMarket.Config? market = OfflineMarket.Load();
            var offers = new List<(Item Item, long Price)>();
            long minPrice = Math.Max(1, Constants.BlackMarketSellMinPrice);
            long maxPrice = Constants.BlackMarketSellMaxPrice > 0 ? Constants.BlackMarketSellMaxPrice : long.MaxValue;
            double multiplier = market?.PriceMultiplier > 1 ? market.PriceMultiplier : 1;

            // A Seed lists the same set at every World start (tests, balancing); without one every start differs.
            Random random = market?.Seed is { } seed ? new Random(seed) : Random.Shared;

            IEnumerable<OfflineMarket.Offer> pool = (market?.Items ?? []).Where(offer => offer != null && itemMetadata.TryGet(offer.Id, out _));
            // BM-04: a share of the gear near the level of the character played last, the rest from other levels.
            int playerLevel = db.LastPlayedLevel();
            OfflineMarket.LevelRange? levels = market?.Levels;
            List<OfflineMarket.Offer> shuffled = pool.OrderBy(_ => random.Next()).ToList();
            OfflineMarket.Fill(shuffled, offer => levels != null && OfflineMarket.IsNear(GearLevel(offer.Id), playerLevel, levels),
                levels?.Share ?? 0, Math.Max(0, market?.Count ?? 0), ListGear);

            bool ListGear(OfflineMarket.Offer offer) {
                itemMetadata.TryGet(offer.Id, out ItemMetadata? metadata);
                int[]? weights = market?.Pricing?.RarityWeights;
                int rarity = offer.Rarity > 0 ? offer.Rarity : GearRarity(metadata!, weights, random);
                // Maintainer 2026-10-22: no Normal (white) gear; with RarityWeights a rarity without weight is never listed.
                if (weights != null && (rarity > weights.Length || weights[rarity - 1] <= 0)) {
                    return false;
                }
                long sellPrice = Core.Formulas.Shop.SellPrice(metadata!, new ItemType(offer.Id), rarity);
                long listPrice = offer.Price > 0 ? offer.Price
                    : market?.Pricing is { } pricing ? OfflineMarket.GearPrice(pricing, metadata!.Limit.Level, rarity) : (long) (sellPrice * multiplier);
                // Never below what an NPC shop pays for it: buying here and selling there would make meso every Play.
                long price = Math.Max(listPrice, sellPrice);
                offers.Add((new Item(metadata!, rarity, Math.Max(1, offer.Amount)), Math.Clamp(price, minPrice, maxPrice)));
                return true;
            }

            // Essentials (upgrade materials, gemstones, potions) are listed every time, in their own amount and price.
            foreach (OfflineMarket.Offer offer in (market?.Essentials ?? []).Where(offer => offer != null && itemMetadata.TryGet(offer.Id, out _))) {
                itemMetadata.TryGet(offer.Id, out ItemMetadata? metadata);
                int rarity = offer.Rarity > 0 ? offer.Rarity : ItemOptionRarity.Default(metadata!.Option, tableMetadata.ItemOptionConstantTable,
                    tableMetadata.ItemOptionStaticTable, tableMetadata.ItemOptionRandomTable);
                long sellPrice = Core.Formulas.Shop.SellPrice(metadata!, new ItemType(offer.Id), rarity);
                long price = Math.Max(offer.Price > 0 ? offer.Price : (long) (sellPrice * multiplier), sellPrice);
                int amount = Math.Clamp(offer.Amount, 1, Math.Max(1, metadata!.Property.SlotMax));
                offers.Add((new Item(metadata!, rarity, amount), Math.Clamp(price, minPrice, maxPrice)));
            }

            long expiry = DateTimeOffset.UtcNow.AddDays(Math.Max(1, Constants.BlackMarketSellEndDay)).ToUnixTimeSeconds();
            int listed = db.ReplaceSyntheticListings(offers, expiry);
            logger.Information("Offline Black Market: {Count} synthetic listings", listed);
        } catch (Exception ex) {
            logger.Error(ex, "Offline Black Market: listings not replaced");
        }
    }

    // Gear takes the lowest rarity its option tables define, as drops do (at rarity 1 some gear has no stats), unless the
    // market's RarityWeights pick one of the rarities it defines.
    private int GearRarity(ItemMetadata metadata, int[]? weights, Random random) {
        ItemOptionConstantTable constants = tableMetadata.ItemOptionConstantTable;
        ItemOptionStaticTable statics = tableMetadata.ItemOptionStaticTable;
        ItemOptionRandomTable randoms = tableMetadata.ItemOptionRandomTable;
        int? picked = weights == null ? null : OfflineMarket.PickRarity(ItemOptionRarity.Defined(metadata.Option, constants, statics, randoms), weights, random);
        return picked ?? ItemOptionRarity.Default(metadata.Option, constants, statics, randoms);
    }

    private int GearLevel(int itemId) => itemMetadata.TryGet(itemId, out ItemMetadata? metadata) ? metadata!.Limit.Level : 0;

    private void Heartbeat() {
        while (!tokenSource.Token.IsCancellationRequested) {
            try {
                Task.Delay(TimeSpan.FromSeconds(30), tokenSource.Token).Wait(tokenSource.Token);

                SaveClock();

                login.Heartbeat(new HeartbeatRequest(), cancellationToken: tokenSource.Token);

                foreach (PlayerInfo playerInfo in playerInfoLookup.GetOnlinePlayerInfos()) {
                    if (playerInfo.CharacterId == 0) {
                        logger.Information("Player {CharacterId} is online without a character id, setting to offline", playerInfo.CharacterId);
                        playerInfo.Channel = -1;
                        continue;
                    }
                    if (!channelClients.TryGetClient(playerInfo.Channel, out ChannelClient? channel)) {
                        // Player is online and without a channel, set them to offline.
                        logger.Information("Player {CharacterId} is online without a channel, setting to offline", playerInfo.CharacterId);
                        SetOffline(playerInfo);
                        continue;
                    }

                    try {
                        HeartbeatResponse? response = channel.Heartbeat(new HeartbeatRequest {
                            CharacterId = playerInfo.CharacterId,
                        }, cancellationToken: tokenSource.Token);
                        if (response is { Success: false }) {
                            SetOffline(playerInfo);
                            continue;
                        }
                        playerInfo.RetryHeartbeat = 3;
                    } catch (RpcException) {
                        if (playerInfo.RetryHeartbeat >= 0) {
                            playerInfo.RetryHeartbeat--;
                            continue;
                        }
                        SetOffline(playerInfo);
                    }

                }
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                logger.Warning(ex, "Heartbeat loop error");
            }
        }
    }

    public void SetOffline(PlayerInfo playerInfo) {
        playerInfoLookup.Update(new PlayerUpdateRequest {
            AccountId = playerInfo.AccountId,
            CharacterId = playerInfo.CharacterId,
            LastOnlineTime = GameClock.UtcNow.ToUnixTimeSeconds(),
            Channel = -1,
            Async = true,
        });
    }

    private void Loop() {
        while (!tokenSource.Token.IsCancellationRequested) {
            try {
                scheduler.InvokeAll();
            } catch (Exception e) {
                logger.Error(e, "Error in world server loop");
            }
            try {
                Task.Delay(TimeSpan.FromMinutes(1), tokenSource.Token).Wait();
            } catch { /* do nothing */
            }
        }
    }

    // In-game clock (T-02, maintainer 2026-10-20): it runs at 1x only while World runs; the time the game was closed never counts.
    private void StartClock(GameStorage.Request db) {
        clockTicks = db.GetWorldTicks().Ticks;
        clockStartedAt = GameClock.UtcNow.ToUnixTimeMilliseconds();
        db.SaveWorldTicks(clockTicks, clockStartedAt);
        logger.Information("In-game time: {Date}", OfflineCalendar.ToDate(clockTicks));
    }

    private void SaveClock() {
        try {
            long now = GameClock.UtcNow.ToUnixTimeMilliseconds();
            using GameStorage.Request db = gameStorage.Context();
            db.SaveWorldTicks(clockTicks + OfflineCalendar.TicksFromRealMilliseconds(now - clockStartedAt), now);
        } catch (Exception ex) {
            logger.Warning(ex, "In-game time not saved");
        }
    }

    public void Stop() {
        SaveClock();
        tokenSource.Cancel();
        thread.Join();
        heartbeatThread.Join();
        scheduler.Stop();
    }

    #region Daily Reset
    private void StartDailyReset() {
        // Daily reset
        using GameStorage.Request db = gameStorage.Context();
        DateTime lastReset = db.GetLastDailyReset();

        // Get last midnight.
        DateTime now = GameClock.Now;
        var lastMidnight = new DateTime(now.Year, now.Month, now.Day);
        if (lastReset < lastMidnight) {
            db.DailyReset();
        }

        DateTime nextMidnight = lastMidnight.AddDays(1);
        TimeSpan timeUntilMidnight = nextMidnight - now;
        scheduler.Schedule(ScheduleDailyReset, timeUntilMidnight);
    }

    private void ScheduleDailyReset() {
        DailyReset();
        // Schedule it to repeat every once a day.
        scheduler.ScheduleRepeated(DailyReset, TimeSpan.FromDays(1), strict: true);
    }

    private void DailyReset() {
        using GameStorage.Request db = gameStorage.Context();
        db.DailyReset();
        foreach ((int channelId, ChannelClient channelClient) in channelClients) {
            channelClient.GameReset(new GameResetRequest {
                Daily = new GameResetRequest.Types.Daily(),
            });
        }
    }
    #endregion

    #region Weekly Reset
    private void StartWeeklyReset() {
        using GameStorage.Request db = gameStorage.Context();
        DateTime lastReset = db.GetLastWeeklyReset();

        DateTime now = GameClock.Now;
        int daysSinceReset = ((int) now.DayOfWeek - (int) Constant.ResetDay + 7) % 7;
        DateTime lastResetMidnight = now.Date.AddDays(-daysSinceReset);

        if (lastReset < lastResetMidnight) {
            db.WeeklyReset();
        }

        DateTime nextReset = now.NextDayOfWeek(Constant.ResetDay).Date;
        TimeSpan timeUntilReset = nextReset - now;
        scheduler.Schedule(ScheduleWeeklyReset, timeUntilReset);
    }

    private void ScheduleWeeklyReset() {
        WeeklyReset();
        scheduler.ScheduleRepeated(WeeklyReset, TimeSpan.FromDays(7), strict: true);
    }

    private void WeeklyReset() {
        using GameStorage.Request db = gameStorage.Context();
        db.WeeklyReset();
        guildLookup.ResetWeeklyContribution(); // the cached guilds save on every change, so they must not keep last week's contribution (BT-80)
        foreach ((int channelId, ChannelClient channelClient) in channelClients) {
            channelClient.GameReset(new GameResetRequest {
                Weekly = new GameResetRequest.Types.Weekly(),
            });
        }
    }
    #endregion

    #region Monthly Reset
    private void StartMonthlyReset() {
        using GameStorage.Request db = gameStorage.Context();
        DateTime lastReset = db.GetLastMonthlyReset();

        DateTime now = GameClock.Now;
        DateTime firstOfMonth = new DateTime(now.Year, now.Month, 1);

        if (lastReset < firstOfMonth) {
            db.MonthlyReset();
        }

        DateTime nextMonth = firstOfMonth.AddMonths(1);
        TimeSpan timeUntilReset = nextMonth - now;
        scheduler.Schedule(ScheduleMonthlyReset, timeUntilReset);
    }

    private void ScheduleMonthlyReset() {
        MonthlyReset();
        DateTime now = GameClock.Now;
        DateTime nextMonth = new DateTime(now.Year, now.Month, 1).AddMonths(1);
        scheduler.Schedule(ScheduleMonthlyReset, nextMonth - now);
    }

    private void MonthlyReset() {
        using GameStorage.Request db = gameStorage.Context();
        db.MonthlyReset();
        foreach ((int channelId, ChannelClient channelClient) in channelClients) {
            channelClient.GameReset(new GameResetRequest {
                Monthly = new GameResetRequest.Types.Monthly(),
            });
        }
    }
    #endregion

    private void StartWorldEvents() {
        // Global Portal
        IReadOnlyDictionary<int, GlobalPortalMetadata> globalEvents = serverTableMetadata.TimeEventTable.GlobalPortal;
        foreach ((int eventId, GlobalPortalMetadata eventData) in globalEvents) {
            DateTime now = GameClock.Now;
            if (eventData.FirstRun(now) is not { } startTime) {
                continue;
            }
            scheduler.Schedule(() => GlobalPortal(eventData, startTime), startTime - now);
        }
    }

    private void GlobalPortal(GlobalPortalMetadata data, DateTime startTime) {
        // check probability
        bool run = !(data.Probability < 100 && Random.Shared.Next(100) > data.Probability);

        if (run) {
            DateTime now = GameClock.Now;
            globalPortalLookup.Create(data, (long) (now.ToEpochSeconds() + data.LifeTime.TotalMilliseconds), out int eventId);
            if (!globalPortalLookup.TryGet(out GlobalPortalManager? manager)) {
                logger.Error("Failed to create global portal");
                return;
            }

            manager.CreateFields();

            Task.Factory.StartNew(() => {
                Thread.Sleep(data.LifeTime);
                if (globalPortalLookup.TryGet(out GlobalPortalManager? globalPortalManager) && globalPortalManager.Portal.Id == eventId) {
                    globalPortalLookup.Dispose();
                }
            });
        }

        DateTime nextRunTime = startTime + data.CycleTime;
        if (data.RandomTime > TimeSpan.Zero) {
            nextRunTime += TimeSpan.FromMilliseconds(Random.Shared.Next((int) data.RandomTime.TotalMilliseconds));
        }

        if (data.EndTime < nextRunTime) {
            return;
        }

        scheduler.Schedule(() => GlobalPortal(data, nextRunTime), nextRunTime - GameClock.Now);
    }

    private void StartWorldBossEvents() {
        int scheduled = 0;
        foreach ((int _, WorldBossMetadata boss) in serverTableMetadata.TimeEventTable.WorldBoss) {
            if (boss.EndTime < GameClock.Now) {
                continue;
            }
            if (boss.CycleTime == TimeSpan.Zero) {
                continue;
            }

            DateTime startTime = boss.StartTime;
            if (GameClock.Now > startTime) {
                // Catch up to the next scheduled spawn after now
                while (startTime < GameClock.Now) {
                    startTime += boss.CycleTime;
                }
                if (startTime > boss.EndTime) {
                    continue;
                }
            }

            TimeSpan delay = startTime - GameClock.Now;
            scheduler.Schedule(() => SpawnWorldBoss(boss, startTime), delay);
            scheduled++;
        }
        logger.Information("WorldBoss scheduler started — {Count} bosses scheduled", scheduled);
    }

    private void SpawnWorldBoss(WorldBossMetadata metadata, DateTime spawnTime) {
        bool shouldSpawn = !(metadata.Probability < 100 && Random.Shared.Next(100) >= metadata.Probability);

        // Compute next spawn time before Create() so it can be broadcast with the announce
        DateTime nextSpawn = spawnTime + metadata.CycleTime;
        if (metadata.RandomTime > TimeSpan.Zero) {
            nextSpawn += TimeSpan.FromMilliseconds(Random.Shared.Next((int) metadata.RandomTime.TotalMilliseconds));
        }
        long nextSpawnTimestamp = nextSpawn <= metadata.EndTime ? new DateTimeOffset(nextSpawn).ToUnixTimeSeconds() : 0;

        if (!shouldSpawn) {
            logger.Debug("WorldBoss {Id} roll failed (probability {Probability}%) — skipping spawn, next at {NextSpawn:HH:mm:ss} UTC",
                metadata.Id, metadata.Probability, nextSpawn.ToUniversalTime());
        } else if (worldBossLookup.TryGet(metadata.Id, out _)) {
            logger.Debug("WorldBoss {Id} still active from previous cycle — skipping spawn", metadata.Id);
        } else {
            long spawnTimestamp = GameClock.UtcNow.ToUnixTimeSeconds();
            long endTick = GameClock.UtcNow.ToUnixTimeMilliseconds() + (long) metadata.LifeTime.TotalMilliseconds;
            if (worldBossLookup.Create(metadata, endTick, nextSpawnTimestamp, out int eventId)) {
                if (worldBossLookup.TryGet(metadata.Id, out WorldBossManager? manager)) {
                    manager.Boss.SpawnTimestamp = spawnTimestamp;
                    manager.Announce();
                }
                logger.Information("WorldBoss {Id} (NpcId: {NpcId}) spawned — eventId: {EventId}, alive for {LifeTime:mm\\:ss}, next spawn at {NextSpawn:HH:mm:ss} UTC",
                    metadata.Id, metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0, eventId, metadata.LifeTime, nextSpawn.ToUniversalTime());

                TimeSpan warnDelay = metadata.LifeTime - TimeSpan.FromMinutes(1);
                _ = MonitorWorldBossLifetimeAsync(metadata.Id, warnDelay, metadata.LifeTime, tokenSource.Token);
            }
        }

        if (nextSpawn > metadata.EndTime) {
            logger.Information("WorldBoss {Id} will not respawn — next spawn {NextSpawn} is past EndTime {EndTime}", metadata.Id, nextSpawn, metadata.EndTime);
            return;
        }

        scheduler.Schedule(() => SpawnWorldBoss(metadata, nextSpawn), nextSpawn - GameClock.Now);
    }

    private async Task MonitorWorldBossLifetimeAsync(int metadataId, TimeSpan warnDelay, TimeSpan lifeTime, CancellationToken token) {
        try {
            if (warnDelay > TimeSpan.Zero) {
                await Task.Delay(warnDelay, token);
                if (worldBossLookup.TryGet(metadataId, out WorldBossManager? warnManager)) {
                    warnManager.WarnChannels();
                }
                await Task.Delay(TimeSpan.FromMinutes(1), token);
            } else {
                await Task.Delay(lifeTime, token);
            }
            worldBossLookup.Dispose(metadataId);
            logger.Information("WorldBoss {Id} lifetime expired — despawning", metadataId);
        } catch (OperationCanceledException) {
            // Server shutting down — do not despawn or log as error
        } catch (Exception ex) {
            logger.Error(ex, "Error monitoring field boss lifetime for metadata {MetadataId}", metadataId);
        }
    }

    private void ScheduleGameEvents() {
        IEnumerable<GameEvent> events = serverTableMetadata.GetGameEvents().ToList();
        // Add Events
        // Get only events that havent been started. Started events already get loaded on game/login servers on start up
        foreach (GameEvent data in events.Where(gameEvent => gameEvent.StartTime > GameClock.UtcNow.ToUnixTimeSeconds())) {
            scheduler.Schedule(() => AddGameEvent(data.Id), TimeSpan.FromSeconds(data.StartTime - GameClock.UtcNow.ToUnixTimeSeconds()));
        }

        // Remove Events
        foreach (GameEvent data in events.Where(gameEvent => gameEvent.EndTime > GameClock.UtcNow.ToUnixTimeSeconds())) {
            scheduler.Schedule(() => RemoveGameEvent(data.Id), TimeSpan.FromSeconds(data.EndTime - GameClock.UtcNow.ToUnixTimeSeconds()));
        }
    }

    public void FieldPlotExpiryCheck() {
        using GameStorage.Request db = gameStorage.Context();
        // Get all plots that have expired but are not yet pending
        List<PlotInfo> expiredPlots = db.GetPlotsToExpire();
        if (expiredPlots.Count > 0) {
            foreach (PlotInfo plot in expiredPlots) {
                bool forfeit = false;
                try {
                    if (plot.OwnerId > 0 && plot.ExpiryTime < GameClock.UtcNow.ToUnixTimeSeconds()) {
                        SetPlotAsPending(db, plot);
                        forfeit = true;
                        // mark as open when 3 days has passed since the expiry time
                    } else if (plot.OwnerId == 0 && plot.ExpiryTime + Constants.UgcHomeSaleWaitingTime.TotalSeconds < GameClock.UtcNow.ToUnixTimeSeconds()) {
                        logger.Information("Marking plot {PlotId} as open (no owner)", plot.Id);
                        db.SetPlotOpen(plot.Id); // Mark as open
                    } else {
                        continue; // Still valid, skip
                    }
                } catch (Exception e) {
                    logger.Error(e, "Error processing plot {PlotId} for expiry check", plot.Id);
                    continue; // Skip this plot if there's an error
                }

                // Notify channels about the expired plots
                foreach ((int _, ChannelClient channelClient) in channelClients) {
                    logger.Information("Notifying channel about expired plot {PlotId}", plot.Id);
                    channelClient.UpdateFieldPlot(new FieldPlotRequest {
                        MapId = plot.MapId,
                        PlotNumber = plot.Number,
                        UpdatePlot = new FieldPlotRequest.Types.UpdatePlot() {
                            AccountId = plot.OwnerId,
                            Forfeit = forfeit,
                        },
                    });
                }
            }
        }

        // Schedule next check for the next soonest expiry
        PlotInfo? nextPlot = db.GetSoonestPlotFromExpire(Constants.UgcHomeSaleWaitingTime);
        TimeSpan delay;
        if (nextPlot is not null) {
            DateTimeOffset nextExpiry = DateTimeOffset.FromUnixTimeSeconds(nextPlot.ExpiryTime);
            delay = nextExpiry - GameClock.UtcNow;
            if (delay < TimeSpan.Zero) {
                delay = TimeSpan.Zero;
            }
        } else {
            delay = TimeSpan.FromDays(1); // Default to 1 day if no plots are found
        }
        scheduler.Schedule(FieldPlotExpiryCheck, delay);
    }

    // Marks a plot as pending; its cubes go to the owner's furnishing storage first, in one transaction (M5-02).
    private void SetPlotAsPending(GameStorage.Request db, PlotInfo plot) {
        logger.Information("Marking plot {PlotId} as pending (owner: {OwnerId})", plot.Id, plot.OwnerId);
        db.ExpireOutdoorPlot(plot);
    }

    private void AddGameEvent(int eventId) {
        foreach ((int channelId, ChannelClient channelClient) in channelClients) {
            channelClient.GameEvent(new GameEventRequest {
                Add = new GameEventRequest.Types.Add {
                    EventId = eventId,
                },
            });
        }
    }

    private void RemoveGameEvent(int eventId) {
        foreach ((int channelId, ChannelClient channelClient) in channelClients) {
            channelClient.GameEvent(new GameEventRequest {
                Remove = new GameEventRequest.Types.Remove {
                    EventId = eventId,
                },
            });
        }
    }

    private static int NextGlobalId() => Interlocked.Increment(ref _globalIdCounter);

    public int AddCustomStringBoard(string message) {
        if (string.IsNullOrEmpty(message)) {
            return -1;
        }

        int id = NextGlobalId();
        memoryStringBoards.TryAdd(id, message);
        return id;
    }

    public bool RemoveCustomStringBoard(int id) {
        return memoryStringBoards.TryRemove(id, out _);
    }

    public IReadOnlyDictionary<int, string> GetCustomStringBoards() {
        return memoryStringBoards;
    }
}
