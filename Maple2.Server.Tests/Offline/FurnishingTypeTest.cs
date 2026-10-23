using Maple2.Model.Game;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class FurnishingTypeTest {
    [Test]
    public void TestFurnishingTypes() {
        // Lapentouched Portraits (Type 5) should be furnishing
        Assert.IsTrue(new ItemType(50500011).IsFurnishing);
        Assert.IsTrue(new ItemType(50500012).IsFurnishing);
        Assert.IsTrue(new ItemType(50500013).IsFurnishing);

        // Monster Boxes (Type 5) should be furnishing
        Assert.IsTrue(new ItemType(50500001).IsFurnishing);
        Assert.IsTrue(new ItemType(50500002).IsFurnishing);
        Assert.IsTrue(new ItemType(50500003).IsFurnishing);
        Assert.IsTrue(new ItemType(50500004).IsFurnishing);
        Assert.IsTrue(new ItemType(50500005).IsFurnishing);
        Assert.IsTrue(new ItemType(50500006).IsFurnishing);
        Assert.IsTrue(new ItemType(50500007).IsFurnishing);
        Assert.IsTrue(new ItemType(50500008).IsFurnishing);
        Assert.IsTrue(new ItemType(50500009).IsFurnishing);
        Assert.IsTrue(new ItemType(50500010).IsFurnishing);

        // Other types that should remain furnishing
        Assert.IsTrue(new ItemType(50100001).IsFurnishing); // Type 1
        Assert.IsTrue(new ItemType(50900001).IsFurnishing); // Type 9

        // Types that should NOT be furnishing
        Assert.IsFalse(new ItemType(50600001).IsFurnishing); // Type 6 (Ground Mount)
        Assert.IsFalse(new ItemType(30000001).IsFurnishing); // Group 3
    }
}
