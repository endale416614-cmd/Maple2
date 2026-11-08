using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Database.Storage;

/// <summary>
/// Offline edition: Change Attributes at half its materials, OFFLINE_REMAKE_SALE (on in Adapted, A34). Every Global login list we
/// hold (0xB4, 542 captures) names the same two SaleRemake events: 220019 for pets and 220021 for equipment, 5000 each. The servers
/// add them to their event list at start, so the client gets that list and ChangeAttributesHandler charges what it says.
/// </summary>
public static class OfflineRemakeSale {
    public const int Discount = 5000;

    public static readonly bool Enabled = OfflineProfile.Flag("OFFLINE_REMAKE_SALE", true);

    /// <summary>The captured events, in the captured order, when <paramref name="enabled"/>; none otherwise.</summary>
    public static GameEventMetadata[] Events(bool enabled) => enabled ? [Sale(220019, SaleRemake.Pets), Sale(220021, SaleRemake.Equipment)] : [];

    // Lasting, to the end of the data's own lasting sales (90000061).
    private static GameEventMetadata Sale(int id, byte kind) => new(id, GameEventType.SaleRemake, new DateTime(2000, 1, 1), new DateTime(2124, 1, 1),
        TimeSpan.Zero, TimeSpan.Zero, [], new SaleRemake(Discount, kind), $"{Discount}", $"{kind}", string.Empty, string.Empty);
}
