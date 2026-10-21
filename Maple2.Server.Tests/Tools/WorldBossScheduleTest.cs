using System;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using NUnit.Framework;

namespace Maple2.Server.Tests.Tools;

[TestFixture]
public class WorldBossScheduleTest {
    [Test]
    public void ComputeNextSpawnTimestamp_FirstSpawnAfterOverride() {
        var metadata = new WorldBossMetadata(
            Id: 1,
            Probability: 100,
            StartTime: new DateTime(2014, 1, 1, 0, 0, 0, DateTimeKind.Local),
            EndTime: new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Local),
            CycleTime: TimeSpan.FromHours(1),
            RandomTime: TimeSpan.Zero,
            LifeTime: TimeSpan.Zero,
            TargetMapIds: [],
            SpawnPointIds: [],
            NpcIds: [1000],
            Tag: 0,
            Unique: false,
            IndividualChannelSpawn: false,
            VariableCountByChannel: 0f,
            ScreenNotice: false,
            ChatNotice: false
        );

        using (GameClock.Override(() => new DateTimeOffset(2030, 1, 1, 10, 20, 0, TimeSpan.Zero))) {
            long result = WorldBossUtil.ComputeNextSpawnTimestamp(metadata);

            // The first whole local hour after the overridden time (20 past: no time zone puts that on a whole hour)
            DateTime now = GameClock.Now;
            long expected = new DateTimeOffset(now.Date.AddHours(now.Hour + 1)).ToUnixTimeSeconds();
            Assert.That(result, Is.EqualTo(expected));
        }
    }

    [Test]
    public void ComputeNextSpawnTimestamp_EndedBeforeOverride() {
        var metadata = new WorldBossMetadata(
            Id: 1,
            Probability: 100,
            StartTime: new DateTime(2014, 1, 1, 0, 0, 0, DateTimeKind.Local),
            EndTime: new DateTime(2029, 1, 1, 0, 0, 0, DateTimeKind.Local),
            CycleTime: TimeSpan.FromHours(1),
            RandomTime: TimeSpan.Zero,
            LifeTime: TimeSpan.Zero,
            TargetMapIds: [],
            SpawnPointIds: [],
            NpcIds: [1000],
            Tag: 0,
            Unique: false,
            IndividualChannelSpawn: false,
            VariableCountByChannel: 0f,
            ScreenNotice: false,
            ChatNotice: false
        );

        using (GameClock.Override(() => new DateTimeOffset(2030, 1, 1, 10, 20, 0, TimeSpan.Zero))) {
            long result = WorldBossUtil.ComputeNextSpawnTimestamp(metadata);

            // Should return 0 since the event ended before override time
            Assert.That(result, Is.EqualTo(0));
        }
    }
}
