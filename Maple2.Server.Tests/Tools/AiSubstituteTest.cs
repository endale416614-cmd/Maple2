using System.Linq;
using Maple2.Database.Storage;

namespace Maple2.Server.Tests.Tools;

/// <summary>NB-05: an NPC whose AI path is missing gets the one AI of the same file name, or the name without "NA".</summary>
public class AiSubstituteTest {
    [Test]
    public void Substitute_SameFileElsewhereOrWithoutNa() {
        string[] names = ["AI_Default.xml", "BossDungeon/SnowKing_Normal/AI_Bjorn.xml", "Raid/AI_PapulatusChaos.xml", "A/AI_Twin.xml", "B/AI_Twin.xml"];

        Assert.That(AiMetadataStorage.Substitute("BossDungeon/SnowKing/AI_Bjorn.xml", names), Is.EqualTo("BossDungeon/SnowKing_Normal/AI_Bjorn.xml"));
        Assert.That(AiMetadataStorage.Substitute("AI_PapulatusChaosNA.xml", names), Is.EqualTo("Raid/AI_PapulatusChaos.xml"));
        Assert.That(AiMetadataStorage.Substitute("C/AI_Twin.xml", names), Is.Null, "two candidates");
        Assert.That(AiMetadataStorage.Substitute("AI_Survival_Missing.xml", names), Is.Null);
    }

    /// <summary>NB-31 (BT-244): AI_Default_Summon_&lt;id&gt; runs the base file; the _Normal sibling folder's file wins among same-named files.</summary>
    [Test]
    public void Substitute_SummonIdAndNormalSibling() {
        string[] names = ["AI_Default.xml", "AI_Default_Summon.xml", "BossDungeon/Pinkbean/AI_StatueHuman01.xml", "BossDungeon/Pinkbean_Normal/Chaos/AI_StatueHuman01.xml"];

        Assert.That(AiMetadataStorage.Substitute("AI_Default_Summon_24003228.xml", names), Is.EqualTo("AI_Default_Summon.xml"));
        Assert.That(AiMetadataStorage.Substitute("AI_Default_Summon_x.xml", names), Is.Null, "the id is a number");
        Assert.That(AiMetadataStorage.Substitute("BossDungeon/Pinkbean/Chaos/AI_StatueHuman01.xml", names), Is.EqualTo("BossDungeon/Pinkbean_Normal/Chaos/AI_StatueHuman01.xml"),
            "two files of that name; the one in the _Normal sibling wins");
        Assert.That(AiMetadataStorage.Substitute("BossDungeon/Pinkbean/Chaos/AI_StatueHuman01.xml", names.Where(name => !name.Contains("_Normal")).ToArray()),
            Is.EqualTo("BossDungeon/Pinkbean/AI_StatueHuman01.xml"), "no _Normal file: the one other file of the name, as before");
    }
}
