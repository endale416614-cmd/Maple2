using System;
using System.IO;
using System.Linq;
using System.Text;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Tools;

namespace Maple2.Server.Tests.Offline;

/// <summary>RR-03 (R-19): the SaleRemake events Offline Adapted Mode lists, their bytes and the sale price. The presses are AutoRerollRunTest's.</summary>
public class RemakeSaleTest {
    private static byte[] Write(GameEvent gameEvent) {
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        return writer.Buffer[..writer.Length];
    }

    [TestCase("Legacy", null, false, TestName = "Legacy: no sale")]
    [TestCase("Adapted", null, true, TestName = "Adapted: the sale")]
    [TestCase(null, null, true, TestName = "No mode: the sale")]
    public void TheFlag_FollowsTheMode(string? mode, string? flag, bool expected) {
        string source = File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "Maple2.Database", "Storage", "Metadata", "OfflineRemakeSale.cs"));
        Assert.That(source, Does.Contain("OfflineProfile.Flag(\"OFFLINE_REMAKE_SALE\", true)"));
        Assert.That(OfflineProfile.Resolve(mode, flag, adaptedDefault: true), Is.EqualTo(expected));
        Assert.That(OfflineRemakeSale.Events(false), Is.Empty);
    }

    [Test]
    public void TheEvents_WriteAsTheCapturedLoginList() {
        // Golden: the 0xB4 login list of all 542 Global captures that hold one (2020-03-22 to 2020-05-27; 1-20 MSB\1-Tria.msb, record 2221):
        // SaleRemake 220019, 5000, kind 3, and later 220021, 5000, kind 1.
        byte[] name = BitConverter.GetBytes((short) 10).Concat(Encoding.Unicode.GetBytes("SaleRemake")).ToArray();
        GameEventMetadata[] sales = OfflineRemakeSale.Events(true);

        Assert.That(sales.Select(sale => sale.Id), Is.EqualTo(new[] { 220019, 220021 }));
        Assert.That(Write(new GameEvent(sales[0])), Is.EqualTo(name.Concat(Convert.FromHexString("735B03008813000003")).ToArray()));
        Assert.That(Write(new GameEvent(sales[1])), Is.EqualTo(name.Concat(Convert.FromHexString("755B03008813000001")).ToArray()));
        Assert.That(sales.All(sale => new GameEvent(sale).IsActive()), Is.True);
    }

    // Golden: pet_change_talents.msb paid 5, 115 and 120 as 2, 57 and 60 under 5000, so the sale rounds down.
    [TestCase(5, 2)]
    [TestCase(115, 57)]
    [TestCase(114, 57)]
    [TestCase(120, 60)]
    [TestCase(165, 82)]
    [TestCase(1, 1)]
    [TestCase(0, 0)]
    public void OnSale_HalvesRoundingDown_AtLeastOne(int amount, int paid) {
        Assert.That(ChangeAttributesHandler.OnSale(amount, OfflineRemakeSale.Discount), Is.EqualTo(paid));
    }

    [Test]
    public void NoSale_KeepsThePrice() {
        Assert.That(ChangeAttributesHandler.OnSale(400, 0), Is.EqualTo(400));
    }
}
