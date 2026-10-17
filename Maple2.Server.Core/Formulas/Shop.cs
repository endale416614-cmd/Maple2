using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Core.Formulas;

public static class Shop {
    // Hardcoded values used for items with level >= 57
    private static readonly int[] PRICES = [1541, 1926, 2465, 9256, 11339, 13653];

    /// <summary>Price of one item, 0 when the rarity has no price entry.</summary>
    public static long SellPrice(ItemMetadata metadata, ItemType type, int rarity) {
        return TryUnitPrice(metadata, type, rarity, out long price) ? price : 0;
    }

    /// <summary>Total for <paramref name="quantity"/> items. False for quantity &lt;= 0, a rarity without a price entry, or overflow.</summary>
    public static bool TrySellPrice(ItemMetadata metadata, ItemType type, int rarity, int quantity, out long price) {
        price = 0;
        if (quantity <= 0 || !TryUnitPrice(metadata, type, rarity, out long unit) || unit > long.MaxValue / quantity) {
            return false;
        }

        price = unit * quantity;
        return true;
    }

    private static bool TryUnitPrice(ItemMetadata metadata, ItemType type, int rarity, out long price) {
        int index = rarity - 1;
        price = 0;
        if (index < 0) {
            return false;
        }

        if (metadata.Limit.Level >= 57) {
            if (index >= PRICES.Length) {
                return false;
            }
            price = PRICES[index];
            return true;
        }

        long[] sellPrices = metadata.Property.SellPrices;
        long[] customPrices = metadata.Property.CustomSellPrices;
        if (index >= sellPrices.Length) {
            return false;
        }

        bool discounted = rarity < 4 && (type.IsArmor || type.IsWeapon);
        long basePrice = index < customPrices.Length && customPrices[index] > 0 ? customPrices[index] : sellPrices[index];
        price = discounted ? (long) Math.Floor(basePrice * 0.333) : basePrice;
        return true;
    }

    public static (ShopCurrencyType CurrencyType, int Cost) ExcessRestockCost(ShopCurrencyType excessCurrencyType, int restockCount) {
        return restockCount switch {
            > 0 and <= 5 => (Constant.InitialTierExcessRestockCurrency, 50000),
            6 => (excessCurrencyType, 10),
            7 => (excessCurrencyType, 20),
            8 => (excessCurrencyType, 40),
            9 => (excessCurrencyType, 60),
            10 => (excessCurrencyType, 80),
            > 10 and <= 15 => (excessCurrencyType, 100),
            _ => (excessCurrencyType, 150),
        };
    }
}
