extern alias ingest;

using System.Text.Json;
using System.Xml;
using ingest::Maple2.File.Ingest.Mapper;
using ingest::Maple2.File.Parser.Enum;
using ingest::Maple2.File.Parser.Xml.AI;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

/// <summary>IG-01: the AI ingest keeps the battleEnd steps apart from battle and the op form of the slave count condition.</summary>
public class AiMapperTest {
    [Test]
    public void MapAi_BattleEndStepsStayInBattleEnd() {
        var data = new NpcAi {
            Battle = {new SkillNode {name = "skill", idx = 1}},
            BattleEnd = {new SkillNode {name = "skill", idx = 2}},
        };

        AiMetadata ai = AiMapper.MapAi("AI_Test.xml", data);

        Assert.That(ai.Battle, Has.Length.EqualTo(1));
        Assert.That(((AiMetadata.SkillNode) ai.Battle[0]).Idx, Is.EqualTo(1));
        Assert.That(ai.BattleEnd, Has.Length.EqualTo(1));
        Assert.That(((AiMetadata.SkillNode) ai.BattleEnd[0]).Idx, Is.EqualTo(2));
    }

    [Test]
    public void MapAi_SlaveCountOpFormBecomesSlaveCountOpCondition() {
        AiMetadata.Condition condition = MapBattleCondition(new SlaveCountCondition {
            name = "slaveCount",
            slaveCount = 0,
            slaveCountOp = ConditionOp.Greater,
        });

        var op = condition as AiMetadata.SlaveCountOpCondition;
        Assert.That(op, Is.Not.Null);
        Assert.That(op!.SlaveCount, Is.EqualTo(0));
        Assert.That(op.SlaveCountOp, Is.EqualTo(AiConditionOp.Greater));
    }

    [Test]
    public void MapAi_SlaveCountCountFormStaysSlaveCountCondition() {
        AiMetadata.Condition condition = MapBattleCondition(new SlaveCountCondition {
            name = "SlaveCount",
            count = 2,
            useSummonGroup = true,
            summonGroup = 201,
        });

        var count = condition as AiMetadata.SlaveCountCondition;
        Assert.That(count, Is.Not.Null);
        Assert.That(count!.Count, Is.EqualTo(2));
        Assert.That(count.UseSummonGroup, Is.True);
        Assert.That(count.SummonGroup, Is.EqualTo(201));
    }

    [Test]
    public void IsOnlyDead_ReadsTheBattleEndAttribute() {
        Assert.That(AiMapper.IsOnlyDead(Xml("<npcAi><battleEnd onlyDead=\"true\"><skill name=\"skill\" idx=\"1\"/></battleEnd></npcAi>")), Is.True);
        Assert.That(AiMapper.IsOnlyDead(Xml("<npcAi><battleEnd><skill name=\"skill\" idx=\"1\"/></battleEnd></npcAi>")), Is.False);
        Assert.That(AiMapper.IsOnlyDead(Xml("<npcAi><battle><skill name=\"skill\" idx=\"1\"/></battle></npcAi>")), Is.False);
    }

    [Test]
    public void MapAi_OnlyDeadMarkerComesFirstInBattleEnd() {
        var data = new NpcAi {BattleEnd = {new SkillNode {name = "skill", idx = 2}}};

        AiMetadata marked = AiMapper.MapAi("AI_Test.xml", data, onlyDead: true);

        Assert.That(marked.BattleEnd, Has.Length.EqualTo(2));
        Assert.That(marked.BattleEnd[0], Is.TypeOf<AiMetadata.OnlyDead>());
        Assert.That(((AiMetadata.SkillNode) marked.BattleEnd[1]).Idx, Is.EqualTo(2));
        Assert.That(AiMapper.MapAi("AI_Test.xml", data).BattleEnd, Has.Length.EqualTo(1), "without onlyDead no marker");
        Assert.That(AiMapper.MapAi("AI_Test.xml", new NpcAi(), onlyDead: true).BattleEnd, Is.Empty, "no steps, no marker");
    }

    [Test]
    public void OnlyDeadMarker_SurvivesTheJsonRoundTrip() {
        AiMetadata.Entry[] entries = [new AiMetadata.OnlyDead("onlyDead"), new AiMetadata.AiPreset("preset")];

        AiMetadata.Entry[]? read = JsonSerializer.Deserialize<AiMetadata.Entry[]>(JsonSerializer.Serialize(entries));

        Assert.That(read, Is.Not.Null);
        Assert.That(read![0], Is.TypeOf<AiMetadata.OnlyDead>());
        Assert.That(read[1], Is.TypeOf<AiMetadata.AiPreset>());
    }

    private static XmlDocument Xml(string text) {
        var document = new XmlDocument();
        document.LoadXml(text);
        return document;
    }

    private static AiMetadata.Condition MapBattleCondition(SlaveCountCondition condition) {
        var node = new ConditionsNode {name = "conditions"};
        node.Entries.Add(condition);
        var data = new NpcAi {Battle = {node}};

        AiMetadata ai = AiMapper.MapAi("AI_Test.xml", data);

        var conditions = (AiMetadata.ConditionsNode) ai.Battle[0];
        Assert.That(conditions.Conditions, Has.Length.EqualTo(1));
        return conditions.Conditions[0];
    }
}
