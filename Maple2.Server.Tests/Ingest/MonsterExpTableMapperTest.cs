extern alias ingest;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml;
using ingest::Maple2.File.Ingest.Mapper;
using ingest::Maple2.File.IO;
using ingest::Maple2.File.IO.Crypto;
using ingest::Maple2.File.IO.Crypto.Common;
using ingest::Maple2.File.IO.Crypto.Keys;
using ingest::Maple2.File.IO.Crypto.Stream;
using Maple2.Model.Common;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

/// <summary>FT-03: a server table the parser has no method for, read from its raw XML. Golden: table/Server/monsterExpTable.xml of Server.m2d.</summary>
public class MonsterExpTableMapperTest {
    // Three rows of the real file, with its attribute order.
    private const string Rows = """
        <?xml version="1.0" encoding="utf-8"?>
        <ms2>
            <exp level="1" common="3" dungeonElite="45" elite="13" boss="154" dungeonBoss="216" />
            <exp level="40" common="6060" dungeonElite="45450" elite="13786" boss="155829" dungeonBoss="218160" />
            <exp level="99" common="120744" dungeonElite="1177262" elite="274694" boss="3104868" dungeonBoss="4346815" />
        </ms2>
        """;

    private static readonly MonsterExpTable.Entry Level1 = new(Common: 3, Elite: 13, DungeonElite: 45, Boss: 154, DungeonBoss: 216);
    private static readonly MonsterExpTable.Entry Level40 = new(Common: 6060, Elite: 13786, DungeonElite: 45450, Boss: 155829, DungeonBoss: 218160);
    private static readonly MonsterExpTable.Entry Level99 = new(Common: 120744, Elite: 274694, DungeonElite: 1177262, Boss: 3104868, DungeonBoss: 4346815);

    [Test]
    public void MapMonsterExp_ReadsEachColumnOfEachLevel() {
        MonsterExpTable table = ServerTableMapper.MapMonsterExp(Xml(Rows));

        Assert.That(table.Entries.Keys, Is.EqualTo(new[] {1, 40, 99}));
        Assert.That(table.Entries[1], Is.EqualTo(Level1));
        Assert.That(table.Entries[40], Is.EqualTo(Level40));
        Assert.That(table.Entries[99], Is.EqualTo(Level99));
    }

    [Test]
    public void MapMonsterExp_ABrokenRowStopsTheIngest() {
        Assert.That(() => ServerTableMapper.MapMonsterExp(Xml("<ms2><exp level=\"1\" common=\"3\"/></ms2>")), Throws.TypeOf<FormatException>(), "a missing column is not 0");

        const string row = "<exp level=\"1\" common=\"3\" dungeonElite=\"45\" elite=\"13\" boss=\"154\" dungeonBoss=\"216\"/>";
        Assert.That(() => ServerTableMapper.MapMonsterExp(Xml($"<ms2>{row}{row}</ms2>")), Throws.ArgumentException, "a level twice");
    }

    [Test]
    public void MonsterExpTable_SurvivesTheJsonRoundTrip() {
        ServerTable table = ServerTableMapper.MapMonsterExp(Xml(Rows));

        ServerTable? read = JsonSerializer.Deserialize<ServerTable>(JsonSerializer.Serialize(table));

        Assert.That(read, Is.TypeOf<MonsterExpTable>());
        Assert.That(((MonsterExpTable) read!).Entries[99], Is.EqualTo(Level99));
    }

    /// <summary>
    /// The reader's entry name and its rows: with MS2_DATA_FOLDER, as the ingest names it, on the real Server.m2d and its 99 rows.
    /// Without it, on a one-entry archive written here, so that the read runs in every suite.
    /// </summary>
    [Test]
    public void RawTable_ReadsTheArchive() {
        string? folder = Environment.GetEnvironmentVariable("MS2_DATA_FOLDER");
        if (!string.IsNullOrEmpty(folder)) {
            AssertReads(Path.Combine(folder, "Server.m2d"), Enumerable.Range(1, 99));
            return;
        }

        DirectoryInfo scratch = Directory.CreateTempSubdirectory();
        try {
            string path = Path.Combine(scratch.FullName, "Server.m2d");
            WriteArchive(path, $"table/Server/{ServerTableNames.MONSTER_EXP}", Encoding.UTF8.GetBytes(Rows));
            AssertReads(path, new[] {1, 40, 99});
        } finally {
            scratch.Delete(true);
        }
    }

    private static void AssertReads(string path, IEnumerable<int> levels) {
        using var reader = new M2dReader(path);
        MonsterExpTable table = ServerTableMapper.MapMonsterExp(ServerTableMapper.RawTable(reader, ServerTableNames.MONSTER_EXP));

        Assert.That(table.Entries.Keys, Is.EqualTo(levels));
        Assert.That(table.Entries[1], Is.EqualTo(Level1));
        Assert.That(table.Entries[40], Is.EqualTo(Level40));
        Assert.That(table.Entries[99], Is.EqualTo(Level99));
        Assert.That(() => ServerTableMapper.RawTable(reader, "monsterexptable.xml"), Throws.InvalidOperationException, "the name matches exactly, and a miss stops the ingest");
    }

    /// <summary>A one-entry MS2F archive, as tools/M2dPack writes one: the entry's block in the .m2d, its name and its header in the .m2h.</summary>
    internal static void WriteArchive(string path, string name, byte[] content) {
        File.WriteAllBytes(path, Encode(content, out uint size, out uint compressed, out uint encoded));
        IPackFileHeader header = PackFileHeaderVer1.CreateHeader(1, Encryption.Aes | Encryption.Zlib, 0, (byte[]) content.Clone());
        (header.FileSize, header.CompressedFileSize, header.EncodedFileSize) = (size, compressed, encoded);

        using var table = new MemoryStream();
        using (var writer = new BinaryWriter(table, Encoding.UTF8, true)) {
            header.Encode(writer);
        }
        var entry = new PackFileEntry { Index = 1, Hash = "", Name = name, FileHeader = header };
        byte[] names = Encode(Encoding.UTF8.GetBytes(entry.ToString()), out uint namesSize, out uint namesCompressed, out uint namesEncoded);
        byte[] headers = Encode(table.ToArray(), out uint tableSize, out uint tableCompressed, out uint tableEncoded);

        using var m2h = new BinaryWriter(File.Create(Path.ChangeExtension(path, ".m2h")));
        m2h.Write((uint) PackVersion.MS2F);
        m2h.Write(0u);
        foreach (ulong field in new ulong[] {tableCompressed, tableEncoded, namesSize, namesCompressed, namesEncoded, 1, tableSize}) {
            m2h.Write(field);
        }
        m2h.Write(names);
        m2h.Write(headers);
    }

    // AES over the whole compressed buffer: CryptoManager.Encrypt leaves the tail of a tiny file unencrypted, and the name list is one.
    private static byte[] Encode(byte[] content, out uint size, out uint compressed, out uint encoded) {
        byte[] buffer = CryptoManager.Encrypt(PackVersion.MS2F, (byte[]) content.Clone(), Encryption.Zlib, out size, out compressed, out _);
        CipherKeys.GetKeyAndIV(PackVersion.MS2F, compressed, out byte[] key, out byte[] iv);
        new AESCipher(key, iv).TransformBlock(buffer, 0, (uint) buffer.Length, buffer, 0);
        byte[] base64 = Encoding.UTF8.GetBytes(Convert.ToBase64String(buffer));
        encoded = (uint) base64.Length;
        return base64;
    }

    private static XmlDocument Xml(string text) {
        var document = new XmlDocument();
        document.LoadXml(text);
        return document;
    }
}
