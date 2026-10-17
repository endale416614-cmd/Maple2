using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Server.Game.Commands;

namespace Maple2.Server.Tests.Offline;

/// <summary>Name and count parsing of /give.</summary>
public class ItemNameResolverTest {
    private static readonly JobCode[] None = [];

    private static readonly List<ItemNameResolver.Entry> Items = [
        new(20000001, "Red Potion", None),
        new(20000002, "Red Potion Box", None),
        new(30000001, "Onyx Crystal", None),
        new(30000002, "Onyx Crystal 2", None),
        new(11300001, "Absolute Earrings", [JobCode.Knight]),
        new(11300002, "Absolute Earrings", [JobCode.Wizard]),
        new(11300003, "Absolute Earrings", [JobCode.HeavyGunner]),
        new(40000001, "Bronze Sword", [JobCode.Knight]),
        new(40000002, "Bronze Sword", [JobCode.Knight]),
        new(50000001, "Crystal Blade", None),
        new(50000002, "Crystal Spear", None),
        new(60000001, "", None),
    ];

    private static IEnumerable<ItemNameResolver.Entry> Search(string name) =>
        Items.Where(e => e.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static ItemNameResolver.Result Resolve(string text, JobCode job = JobCode.Knight) =>
        ItemNameResolver.Resolve(text.Split(' '), job, Search);

    [Test]
    public void ExactBeatsSubstring() {
        ItemNameResolver.Result result = Resolve("red potion");
        Assert.That(result.Id, Is.EqualTo(20000001));
        Assert.That(result.Count, Is.EqualTo(1));
    }

    [Test]
    public void SingleSubstringMatchIsGiven() {
        Assert.That(Resolve("Box").Id, Is.EqualTo(20000002));
    }

    [Test]
    public void SameNameGearPrefersPlayerJob() {
        Assert.That(Resolve("Absolute Earrings", JobCode.Wizard).Id, Is.EqualTo(11300002));
        Assert.That(Resolve("Absolute Earrings", JobCode.Knight).Id, Is.EqualTo(11300001));
    }

    [Test]
    public void SameNameGearWithoutJobMatchIsAmbiguous() {
        ItemNameResolver.Result result = Resolve("Absolute Earrings", JobCode.Thief);
        Assert.That(result.Id, Is.EqualTo(0));
        Assert.That(result.Options.Select(e => e.Id), Is.EqualTo(new[] { 11300001, 11300002, 11300003 }));
    }

    [Test]
    public void SameNameSameJobIsAmbiguous() {
        Assert.That(Resolve("Bronze Sword").Options, Has.Count.EqualTo(2));
    }

    [Test]
    public void SeveralSubstringMatchesAreListed() {
        ItemNameResolver.Result result = Resolve("Crystal");
        Assert.That(result.Id, Is.EqualTo(0));
        Assert.That(result.Options.Select(e => e.Name), Is.EquivalentTo(new[] { "Onyx Crystal", "Onyx Crystal 2", "Crystal Blade", "Crystal Spear" }));
    }

    [Test]
    public void ListIsCappedAndEmptyNamesAreIgnored() {
        var many = Enumerable.Range(1, 25).Select(i => new ItemNameResolver.Entry(i, $"Gem {i}", None)).Append(new(99, "", None)).ToList();
        ItemNameResolver.Result result = ItemNameResolver.Resolve(["Gem"], JobCode.Knight, _ => many);
        Assert.That(result.Options, Has.Count.EqualTo(ItemNameResolver.MAX_LISTED));
        Assert.That(result.Options.All(e => e.Name.Length > 0));
    }

    [Test]
    public void NoMatch() {
        ItemNameResolver.Result result = Resolve("Nothing Here");
        Assert.That(result.Id, Is.EqualTo(0));
        Assert.That(result.Options, Is.Empty);
        Assert.That(result.Name, Is.EqualTo("Nothing Here"));
    }

    [Test]
    public void TrailingNumberIsTheCount() {
        ItemNameResolver.Result result = Resolve("Red Potion 5");
        Assert.That(result.Name, Is.EqualTo("Red Potion"));
        Assert.That(result.Count, Is.EqualTo(5));
        Assert.That(result.Id, Is.EqualTo(20000001));
        Assert.That(Resolve("Red Potion").Count, Is.EqualTo(1));
    }

    [Test]
    public void NameEndingInNumberWinsWhenItExists() {
        ItemNameResolver.Result result = Resolve("Onyx Crystal 2");
        Assert.That(result.Id, Is.EqualTo(30000002));
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(Resolve("Onyx Crystal 2 3").Id, Is.EqualTo(30000002));
        Assert.That(Resolve("Onyx Crystal 2 3").Count, Is.EqualTo(3));
    }
}
