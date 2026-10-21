using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

internal class OfflineState {
    public required string Key { get; set; }
    public string Value { get; set; } = "";

    public static void Configure(EntityTypeBuilder<OfflineState> builder) {
        builder.ToTable("offline-state");
        builder.HasKey(state => state.Key);
    }
}