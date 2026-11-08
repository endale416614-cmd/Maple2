using System;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>NB-20a: a splash region starts Delay after its add and, without ImmediateActive, first hits Delay + Interval after it; the add packet
/// carries that start, 0 for a map's or a trigger's region (BT-68, C3). Golden: the 227 GMS captures with RegionSkill packets, medians per skill.</summary>
[TestFixture]
public class SplashTimingTest {
    private const long AddTick = 1000;

    [TestCase(500, 1000, false, 2500, 1500)] // Vine Splash 40011002: first hit 1499 ms after the add, its tick +492 (68 adds)
    [TestCase(2000, 0, false, 3000, 3000)] // 70000023, Spring Beach's steam: 2002 ms, +1928 (272 adds)
    [TestCase(500, 300, false, 1800, 1500)] // 40328012: 794 ms, +464 (83 adds)
    [TestCase(0, 1500, false, 2500, 1000)] // 40500049: 1509 ms, -20 (68 adds)
    [TestCase(1200, 0, false, 2200, 2200)] // Lacerate: Burst 10200272, Sever's last part: 1229 ms, +1152
    [TestCase(1000, 0, true, 1000, 2000)] // 60125001, ImmediateActive: at the add, +989 (9 adds)
    [TestCase(int.MaxValue, 1000, false, 2000, 1000)] // 99999763: no wait (hits from 46 ms, 6 adds); the first hit stays at its Interval
    public void ASplashRegionStartsAfterItsDelay(int delay, int interval, bool immediate, long firstHit, long start) {
        FieldSkill region = Splash(new SkillEffectMetadataSplash(interval, delay, 0, false, immediate, false, false, false, false, null));
        Assert.That((region.NextTick, region.StartTick), Is.EqualTo((firstHit, start)));
    }

    [Test]
    public void ASensingRegionKeepsItsFirstCheck() {
        // 10300254: OnlySensingActive, Delay 2000, Interval 0, no sensor: it checks for targets once, at the add, as before (FS-01).
        FieldSkill region = Splash(new SkillEffectMetadataSplash(0, 2000, 0, false, false, false, true, false, false, null));
        Assert.That((region.NextTick, region.StartTick), Is.EqualTo((AddTick, 3000L)));
    }

    [Test]
    public void TheAddPacketCarriesTheStart() {
        FieldSkill splash = Splash(new SkillEffectMetadataSplash(1000, 500, 0, false, false, false, false, false, false, null));
        var trigger = new FieldSkill(Field(), 2, Bare<FieldActor>(), Skill(), 150, 1, Vector3.Zero);
        Assert.That(Tick(RegionSkillPacket.Add(splash)), Is.EqualTo(1500));
        Assert.That(Tick(RegionSkillPacket.Add(trigger)), Is.EqualTo(0), "2,063 of 2,063 trigger adds and 666 of 666 map region adds carry 0");
    }

    private static FieldSkill Splash(SkillEffectMetadataSplash splash) => new(Field(), 1, Bare<FieldActor>(), Skill(), 1, splash, Vector3.Zero);

    // After the 2-byte op code: the command, the region's object id and its owner's (RegionSkillPacket.Add).
    private static int Tick(ByteWriter packet) => BitConverter.ToInt32(packet.Buffer, 11);

    private static SkillMetadata Skill() => new(40011002, 1, null, null!, null!, new SkillMetadataLevel(null!, null, null, null!, null!, null, null!, [], []));

    private static FieldManager Field() {
        FieldManager field = Bare<FieldManager>();
        SetField(field, "<FieldTick>k__BackingField", AddTick);
        return field;
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static void SetField(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }
}
