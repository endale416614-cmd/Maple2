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
}
