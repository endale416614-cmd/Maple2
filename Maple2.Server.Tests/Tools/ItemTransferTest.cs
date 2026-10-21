using Maple2.Model.Enum;
using Maple2.Model.Game;
using NUnit.Framework;

namespace Maple2.Server.Tests.Tools;

[TestFixture]
public class ItemTransferTest {
    [Test]
    public void CountTrade_UsesOneOfTheRemainingTradesDownToZero() {
        var transfer = new ItemTransfer(TransferFlag.LimitTrade, 2);

        transfer.CountTrade();
        Assert.That(transfer.RemainTrades, Is.EqualTo(1));
        transfer.CountTrade();
        Assert.That(transfer.RemainTrades, Is.EqualTo(0));
        transfer.CountTrade();
        Assert.That(transfer.RemainTrades, Is.EqualTo(0));
    }

    [Test]
    public void CountTrade_LeavesAnItemWithoutTradeLimitAlone() {
        var transfer = new ItemTransfer(TransferFlag.Bind, 2);

        transfer.CountTrade();
        Assert.That(transfer.RemainTrades, Is.EqualTo(2));
    }
}
