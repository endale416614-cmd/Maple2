using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Serilog;
using static Maple2.Model.Metadata.AiMetadata;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public class AiState {
    protected readonly ILogger Logger = Log.ForContext<AiState>();
    private readonly FieldNpc actor;
    public AiMetadata? AiMetadata { get; private set; }
    private Node? battle;
    private Node? battleEnd;
    private bool scriptedEvent;

    private List<StackEntry> aiStack = new List<StackEntry>();
    private NpcTask? currentTask = null;
    private long currentLimitTick = 0;

    private AiMetadata? lastEvaluated;
    private DecisionTreeType currentTree;

    private Dictionary<string, long> coolTimes = new();
    private List<Node> reservedNodes = new List<Node>();

    private enum DecisionTreeType {
        None,
        Battle,
        BattleEnd,
    }

    /// <summary>distanceOver: the target is farther than <paramref name="value"/>.</summary>
    public static bool IsFarther(float distanceSquared, int value) => distanceSquared > (float) value * value;

    /// <summary>distanceLess: the target is closer than <paramref name="value"/>.</summary>
    public static bool IsCloser(float distanceSquared, int value) => distanceSquared < (float) value * value;

    /// <summary>triggerModifyUserValue adds its value to the map's: the data counts +1/-1 up to what the map script waits for.</summary>
    public static void AddUserValue(Dictionary<string, int> values, string key, int delta) {
        values.TryGetValue(key, out int old);
        values[key] = old + delta;
    }

    /// <summary>setValue and setValueRangeTarget: the value, or with isModify the value added to the one already set.</summary>
    public static void SetAiValue(Dictionary<string, int> data, string key, int value, bool modify) {
        data[key] = modify && data.TryGetValue(key, out int old) ? old + value : value;
    }

    /// <summary>setSlaveValue targets: every live slave, or with isRandom one of them at random (none without slaves).</summary>
    public static IReadOnlyList<T> SlaveTargets<T>(IReadOnlyList<T> slaves, bool isRandom, Random random) =>
        isRandom && slaves.Count > 1 ? [slaves[random.Next(slaves.Count)]] : slaves;

    /// <summary>A select's weights: with useNpcProb the NPC's own skill probabilities (entry i is skill i + 1) when it has one
    /// per skill and they are not all 0; otherwise the node's (AI_Default: Prob [100] over the skill 1, 2 and 3 entries).</summary>
    public static int[] SelectWeights(int[] nodeProb, bool useNpcProb, int[]? npcProbs, int skillCount) =>
        useNpcProb && npcProbs is { Length: > 0 } && npcProbs.Length == skillCount && npcProbs.Sum() > 0 ? npcProbs : nodeProb;

    /// <summary>combatTime: the battle time (ms), taken modulo loop when there is one, is from begin up to but not including end
    /// (no end: the end of the loop, or no end at all without a loop).</summary>
    public static bool InCombatWindow(long battleMs, long begin, long loop, long end) {
        if (battleMs < 0) {
            return false;
        }
        long time = loop > 0 ? battleMs % loop : battleMs;
        long until = end > 0 ? end : loop > 0 ? loop : long.MaxValue;
        return time >= begin && time < until;
    }

    public AiState(FieldNpc actor, string aiPath) {
        this.actor = actor;
        if (string.IsNullOrEmpty(aiPath)) {
            return;
        }

        if (aiPath == "AI_DefaultNew.xml") {
            aiPath = Constant.DefaultAiPath;
        }

        if (!actor.Field.AiMetadata.TryGetOrSubstitute(aiPath, out AiMetadata? metadata)) {
            Logger.Error("{AiPath} could not be found for {ValueId}. setting as default AI", aiPath, actor.Value.Id);
            if (!actor.Field.AiMetadata.TryGet(Constant.DefaultAiPath, out metadata)) {
                throw new KeyNotFoundException($"Could not find default AI {Constant.DefaultAiPath} for {actor.Value.Id}");
            }
        } else if (metadata.Name != aiPath) {
            // A substitute was used
            Logger.Information("{AiPath} is not in the data; {Npc} uses {Substitute}", aiPath, actor.Value.Id, metadata.Name);
        }

        AiMetadata = metadata;

        if (AiMetadata.Battle.Length != 0) {
            battle = new Node("battle", AiMetadata.Battle);
            scriptedEvent = IsScriptedEvent(AiMetadata.Battle);
        }

        if (AiMetadata.BattleEnd.Length != 0) {
            battleEnd = new Node("battle", AiMetadata.BattleEnd);
        }

        if (AiMetadata.Reserved.Length != 0) {
            reservedNodes = AiMetadata.Reserved.Cast<Node>().ToList();
        }
    }

    /// <summary>
    /// A cutscene actor: its battle tree drives a trigger (sets user values) and never looks for a target.
    /// It has to run once spawned; waiting for something to come into sight left e.g. the Tria Battlefield Devlin
    /// (AI_DevilHugeRed_Quest_event) idle far from the frozen player, so SetSkillA never came and the quest stuck.
    /// </summary>
    private static bool IsScriptedEvent(Entry[] entries) {
        bool setsUserValue = false;
        bool needsTarget = false;
        void Walk(Entry entry) {
            switch (entry) {
                case TriggerSetUserValueNode:
                    setsUserValue = true;
                    break;
                case TargetNode or TraceNode or ConditionsNode:
                    needsTarget = true;
                    break;
            }
            if (entry is Node node) {
                foreach (Entry child in node.Entries) {
                    Walk(child);
                }
            }
        }
        foreach (Entry entry in entries) {
            Walk(entry);
        }
        return setsUserValue && !needsTarget;
    }

    public bool IsProcessingNodes() {
        return aiStack.Count > 0;
    }

    public bool IsWaitingOnTask(long tickCount) {
        if (currentTask is null) {
            return false;
        }

        if (currentLimitTick != 0 && tickCount >= currentLimitTick) {
            return false;
        }

        return !currentTask.IsDone;
    }

    public void Update(long tickCount) {
        if (IsWaitingOnTask(tickCount)) {
            return;
        }

        currentTask?.Cancel();

        currentTask = null;
        currentLimitTick = 0;

        if (actor.IsDead) {
            return;
        }

        if (AiMetadata is null) {
            if (actor.Value.Metadata.AiPath != "") {
                actor.AppendDebugMessage("Missing AI\n");
                actor.AppendDebugMessage(actor.Value.Metadata.AiPath + "\n");
            }

            aiStack.Clear();
            currentTree = DecisionTreeType.None;

            return;
        }

        if (scriptedEvent) {
            actor.BattleState.KeepBattle = true;
        }

        bool isInBattle = actor.BattleState.InBattle;

        if (!isInBattle) {
            if (currentTree == DecisionTreeType.Battle) {
                aiStack.Clear();

                currentTree = battleEnd is not null ? DecisionTreeType.BattleEnd : DecisionTreeType.None;
            } else if (currentTree == DecisionTreeType.BattleEnd && aiStack.Count == 0) {
                currentTree = DecisionTreeType.None;
            }

            return;
        } else if (currentTree == DecisionTreeType.BattleEnd) {
            aiStack.Clear();

            currentTree = DecisionTreeType.None;
        }

        if (isInBattle && battle is not null) {
            currentTree = DecisionTreeType.Battle;
        }

        if (lastEvaluated != AiMetadata) {
            aiStack.Clear();
        }

        lastEvaluated = AiMetadata;

        foreach (var node in reservedNodes.ToList()) {
            if (ProcessCondition((dynamic) node)) {
                aiStack.Clear();
                reservedNodes.Remove(node);
                Push(node);
            }
        }

        if (aiStack.Count == 0) {
            if (currentTree == DecisionTreeType.Battle) {
                Push(battle!);
            } else if (currentTree == DecisionTreeType.BattleEnd) {
                Push(battleEnd!);
            }
        }

        while (aiStack.Count > 0) {
            StackEntry entry = aiStack.Last();

            int index = entry.Index;
            int last = aiStack.Count - 1;

            if (index >= entry.Node.Entries.Length) {
                aiStack.RemoveAt(last);

                continue;
            }

            int nextIndex = entry.LockIndex ? entry.Node.Entries.Length : index + 1;

            aiStack[last] = new StackEntry() {
                Node = entry.Node,
                Index = nextIndex,
                LockIndex = entry.LockIndex,
            };

            Process(entry.Node.Entries[index]);

            if (IsWaitingOnTask(tickCount)) {
                break;
            }
        }
    }

    private void SetNodeTask(NpcTask? task, long limit = 0) {
        currentTask = task;
        currentLimitTick = limit == 0 ? 0 : actor.Field.FieldTick + limit;
    }

    private struct StackEntry {
        public Node Node;
        public int Index;
        public bool LockIndex;
    }

    private void Push(Node node) {
        aiStack.Add(new StackEntry() {
            Node = node,
        });
    }

    private void Push(AiPreset aiPreset) {
        actor.AppendDebugMessage($"> Preset: '{aiPreset.Name}'\n");

        AiPresetDefinition? definition = AiMetadata?.AiPresets.First(value => value.Name == aiPreset.Name);

        if (definition is null) {
            return;
        }

        Push(definition);
    }

    private SkillMetadata? TryGetSkill(int idx, short levelOverride = 0) {
        // idx starts at 1
        if (idx < 1 || idx > actor.Skills.Length) {
            actor.AppendDebugMessage($"{actor.Value.Metadata.Name}[{actor.Value.Id}]\n");
            actor.AppendDebugMessage($"{AiMetadata!.Name}\n");
            actor.AppendDebugMessage($"Invalid Skill Idx {idx}\n");

            // Original data: a shared AI file names a skill the NPC lacks (Masked Swordsman 21000102 has one; 41 NPCs in 8 files).
            Logger.Debug($"Skill index {idx} beyond the {actor.Skills.Length} skills of {actor.Value.Metadata.Name}[{actor.Value.Id}] in '{AiMetadata!.Name}'; the step is skipped");

            return null;
        }

        SkillMetadata? skill = actor.Skills[idx - 1];

        if (skill is not null && levelOverride != 0) {
            actor.Field.SkillMetadata.TryGet(skill.Id, levelOverride, out skill);
        }

        if (skill is null) {
            actor.AppendDebugMessage($"{actor.Value.Metadata.Name}[{actor.Value.Id}]\n");
            actor.AppendDebugMessage($"{AiMetadata!.Name}\n");
            actor.AppendDebugMessage($"Missing Skill Idx {idx}\n");

            Logger.Warning($"Missing skillIdx {idx} in {actor.Value.Metadata.Name}[{actor.Value.Id}] script '{AiMetadata!.Name}'. Xml.m2d/m2h might be out of date. Check the MS2 Hub resources!");
        }

        return skill;
    }

    private void Push(Entry entry) {
        Push((dynamic) entry);
    }

    private bool CanEvaluateNode(Entry entry) {
        if (entry is not Node node) {
            return true;
        }

        bool hasTicked = coolTimes.TryGetValue(GetNodeKey(entry), out long lastTick);

        Type parent = entry.GetType();
        long? initialCooltime = parent.GetProperty("InitialCooltime")?.GetValue(entry, null) as long?;
        if (initialCooltime.HasValue && !hasTicked) {
            if (initialCooltime.Value > 0) {
                coolTimes[GetNodeKey(entry)] = actor.Field.FieldTick + initialCooltime.Value;
                return false;
            }
        }

        if (!hasTicked) {
            coolTimes[GetNodeKey(entry)] = 0;
            return true;
        }


        long? cooltime = parent.GetProperty("Cooltime")?.GetValue(entry, null) as long?;

        if (cooltime.HasValue) {
            return lastTick + cooltime.Value < actor.Field.FieldTick;
        }

        return lastTick < actor.Field.FieldTick;
    }

    private bool PerformOperation(AiConditionOp op, int value, int currentValue) {
        switch (op) {
            case AiConditionOp.Equal:
                return value == currentValue;
            case AiConditionOp.Greater:
                return value < currentValue;
            case AiConditionOp.Less:
                return value > currentValue;
            case AiConditionOp.GreaterEqual:
                return value <= currentValue;
            case AiConditionOp.LessEqual:
                return value >= currentValue;
            default:
                throw new ArgumentOutOfRangeException(nameof(op), op, null);
        }
    }
    private string GetNodeKey(Entry entry) {
        return $"{entry.Name}_{entry.GetHashCode()}";
    }

    private void Process(Entry entry) {
        if (entry is Node node) {
            actor.AppendDebugMessage($"> Node: {entry.Name}\n");

            ProcessNode((dynamic) entry);

            if (node.Entries.Length > 0 && aiStack.Last().Node is not SelectNode && aiStack.Last().Node != node && CanEvaluateNode(node.Entries[0])) {
                Push(node);
            }

            coolTimes[GetNodeKey(entry)] = actor.Field.FieldTick;
            return;
        }

        if (entry is AiPreset) {
            actor.AppendDebugMessage($"> AiPreset: {entry.Name}\n");
            Push(entry);
        }
    }

    private void ProcessNode(TraceNode node) {
        if (!actor.Field.TryGetActor(actor.BattleState.TargetId, out IActor? _)) {
            return;
        }

        float distance = 0;

        if (node.SkillIdx != 0) {
            SkillMetadata? skill = TryGetSkill(node.SkillIdx);

            if (skill is null) {
                return;
            }

            distance = skill.Data.Detect.Distance; // naive impl, might need to revisit
        }

        float speed = 1;

        if (node.Speed != 0) {
            speed = node.Speed;
        }

        NpcTask task = actor.MovementState.TryMoveTargetDistance(actor.BattleState.Target!, distance, true, node.Animation, speed);

        SetNodeTask(task, node.Limit);
    }

    private void ProcessNode(SkillNode node) {
        SkillMetadata? skill = TryGetSkill(node.Idx);

        if (skill is null) {
            return;
        }

        NpcTask task = actor.CastAiSkill(skill.Id, skill.Level, node.FaceTarget, node.FacePos);

        if (node.IsKeepBattle) {
            actor.BattleState.KeepBattle = true;
        }

        SetNodeTask(task, node.Limit);
    }

    private void ProcessNode(TeleportNode node) {
        actor.Position = node.Pos;

        if (node.FacePos == new Vector3(0, 0, 0)) {
            return;
        }

        Vector3 offset = node.FacePos - actor.Position;
        float squareDistance = offset.LengthSquared();

        if (MathF.Abs(squareDistance) < 1e-5f) {
            return;
        }

        actor.Transform.LookTo((1 / MathF.Sqrt(squareDistance)) * offset);
    }

    private void ProcessNode(StandbyNode node) {
        NpcTask task = actor.MovementState.TryStandby(null, false);

        if (node.IsKeepBattle) {
            actor.BattleState.KeepBattle = true;
        }

        SetNodeTask(task, node.Limit);
    }

    private void ProcessNode(SetDataNode node) {
        actor.AiExtraData[node.Key] = node.Value;
    }

    private void ProcessNode(TargetNode node) {
        actor.BattleState.ApplyTargetNode(node);
    }

    private void ProcessNode(SayNode node) {
        actor.Field.Broadcast(CinematicPacket.BalloonTalk(true, actor.ObjectId, node.Message, node.DurationTick, node.DelayTick));

        actor.AppendDebugMessage($"Say '{node.Message}'\n");
    }

    private void ProcessNode(SetValueNode node) {
        // Assumed that we increment the current by the value if isModify is true
        SetAiValue(actor.AiExtraData, node.Key, node.Value, node.IsModify);
    }

    private void ProcessNode(ConditionsNode node) {
        Condition? passed = null;

        foreach (Condition condition in node.Conditions) {
            if (ProcessCondition((dynamic) condition)) {
                passed = condition;

                break;
            }
        }

        if (passed is null) {
            actor.AppendDebugMessage("Failed conditions\n");

            return;
        }

        actor.AppendDebugMessage($"+ Condition: '{passed.Name}'\n");

        Push(passed);
    }

    private void ProcessNode(JumpNode node) {
        string sequence = node.Type == NodeJumpType.JumpB ? "Jump_B" : "Jump_A";
        NpcTask task = actor.MovementState.TryJumpTo(node.Pos, actor.BattleState.InBattle, node.Speed, node.HeightMultiplier, sequence);

        if (node.IsKeepBattle) {
            actor.BattleState.KeepBattle = true;
        }

        // Airtime plus slack; if the jump never finishes, the AI cancels it and the NPC lands at the target.
        float distance = Vector2.Distance(new Vector2(actor.Position.X, actor.Position.Y), new Vector2(node.Pos.X, node.Pos.Y));
        SetNodeTask(task, (long) (distance / Math.Max(node.Speed, 1) * 1000) + 2000);
    }

    private void ProcessNode(SelectNode node) {
        var weightedEntries = new WeightedSet<(Entry, int)>();
        // useNpcProb: the NPC's skill probabilities weigh the entries (DL-05; the ingest drops them, summon-npcs.json has them).
        int[] weights = SelectWeights(node.Prob, node.useNpcProb, NpcSummons.SkillProbs(actor.Value.Metadata.Id), actor.Skills.Length);

        // Some AI data lists more weights than entries (AI_BlueLukeJunior.xml: 3 and 2); the extra weights choose nothing.
        for (int i = 0; i < Math.Min(weights.Length, node.Entries.Length); ++i) {
            if (!CanEvaluateNode(node.Entries[i])) {
                continue;
            }

            weightedEntries.Add((node.Entries[i], i), weights[i]);
        }

        if (weightedEntries.Count == 0) {
            return;
        }

        (Entry entry, int index) selected = weightedEntries.Get();

        aiStack[aiStack.Count - 1] = new StackEntry {
            Node = node,
            Index = selected.index,
            LockIndex = true,
        };

        Push(selected.entry);
        Process(selected.entry);
    }

    private void ProcessNode(MoveNode node) {
        NpcTask task = actor.MovementState.TryMoveTo(node.Destination, true, node.Animation, node.Speed);

        SetNodeTask(task, node.Limit);
    }

    private void ProcessNode(TriggerSetUserValueNode node) {
        actor.Field.UserValues[node.Key] = node.Value;
    }

    private void ProcessNode(RideNode node) { }

    private void ProcessNode(SetSlaveValueNode node) {
        foreach (FieldNpc slave in SlaveTargets(LiveSlaves(), node.IsRandom, Random.Shared)) {
            SetAiValue(slave.AiExtraData, node.Key, node.Value, node.IsModify);
        }
    }

    private void ProcessNode(SetMasterValueNode node) {
        if (actor.IsSlave && actor.Summoner is { IsDead: false } master) {
            SetAiValue(master.AiExtraData, node.Key, node.Value, node.IsModify);
        }
    }

    private void ProcessNode(RunawayNode node) {
        if (!actor.Field.TryGetActor(actor.BattleState.TargetId, out IActor? target)) {
            return;
        }

        // till = how far from the target to run (every runaway node has a till or a skill)
        float distance = node.Till;

        if (node.SkillIdx != 0) {
            SkillMetadata? skill = TryGetSkill(node.SkillIdx);

            if (skill is null) {
                return;
            }

            distance = skill.Data.Detect.Distance; // naive impl, might need to revisit
        }

        if (distance <= 0) {
            return;
        }

        NpcTask task = actor.MovementState.TryMoveTargetDistance(actor.BattleState.Target!, distance, true, node.Animation, 1);

        SetNodeTask(task, node.Limit);
    }

    private void ProcessNode(MinimumHpNode node) {
        // A standing floor until the next phase lowers it (R-06): it ran once and healed back once, so no hold acted.
        // Health already below it is raised on the next update, as the heal-back did.
        actor.SetHpFloor(node.HpPercent);
    }

    private void ProcessNode(BuffNode node) {
        IActor target = actor;

        if (node.IsTarget) {
            IActor? newTarget = null;

            if (!actor.Field.TryGetActor(actor.BattleState.TargetId, out newTarget)) {
                return;
            }

            target = newTarget;
        }

        if (node.Type == NodeBuffType.Remove) {
            target.Buffs.Remove(node.Id, actor.ObjectId);

            return;
        }

        target.Buffs.AddBuff(actor, actor, node.Id, node.Level, actor.Field.FieldTick);
    }

    private void ProcessNode(TargetEffectNode node) {
        actor.Field.Broadcast(NpcNoticePacket.TargetEffect(actor.BattleState.TargetId, node.EffectName));
    }

    private void ProcessNode(ShowVibrateNode node) { }

    private void ProcessNode(SidePopupNode node) {
        actor.Field.Broadcast(NpcNoticePacket.SidePopup(node.Type, node.Duration, node.Illust, node.Voice, node.Script, node.Sound));
    }

    private void ProcessNode(SetValueRangeTargetNode node) {
        int range = node.Radius;
        int height = node.Height;

        if (range == 0) {
            range = 10;
        }

        if (height == 0) {
            height = 10;
        }

        foreach (FieldNpc npc in actor.Field.EnumerateNpcs()) {
            if (Vector2.Distance(new Vector2(npc.Position.X, npc.Position.Y), new Vector2(actor.Position.X, actor.Position.Y)) > range) {
                continue;
            }

            if (npc.Position.Z < actor.Position.Z - height) {
                continue;
            }

            if (npc.Position.Z > actor.Position.Z + height) {
                continue;
            }

            SetAiValue(npc.AiExtraData, node.Key, node.Value, node.IsModify);
        }
    }

    private void ProcessNode(AnnounceNode node) {
        actor.Field.Broadcast(NpcNoticePacket.Announce(node.Message, node.DurationTick));
    }

    private void ProcessNode(ModifyRoomTimeNode node) { }

    private void ProcessNode(HideVibrateAllNode node) { }

    private void ProcessNode(TriggerModifyUserValueNode node) {
        AddUserValue(actor.Field.UserValues, node.Key, node.Value);
    }

    private void ProcessNode(CreateRandomRoomNode node) { }

    private void ProcessNode(CreateInteractObjectNode node) { }

    private void ProcessNode(SuicideNode node) {
        actor.SetHpFloor(0);
        actor.Stats.Values[BasicAttribute.Health].Current = 0;
    }


    private bool ProcessCondition(DistanceOverCondition node) {
        if (actor.BattleState.Target is null) {
            return false;
        }

        float targetDistance = (actor.BattleState.Target.Position - actor.Position).LengthSquared();

        return IsFarther(targetDistance, node.Value);
    }

    private bool ProcessCondition(CombatTimeCondition node) {
        long start = actor.BattleState.BattleStartTick;
        return start > 0 && InCombatWindow(actor.Field.FieldTick - start, node.BattleTimeBegin, node.BattleTimeLoop, node.BattleTimeEnd);
    }

    private bool ProcessCondition(DistanceLessCondition node) {
        if (actor.BattleState.Target is null) {
            return false;
        }

        float targetDistance = (actor.BattleState.Target.Position - actor.Position).LengthSquared();

        return IsCloser(targetDistance, node.Value);
    }

    private bool ProcessCondition(SkillRangeCondition node) {
        SkillMetadata? skill = TryGetSkill(node.SkillIdx, node.SkillLev);

        if (skill is null) {
            return false;
        }

        if (actor.BattleState.Target is null) {
            return false;
        }

        float targetDistance = (actor.BattleState.Target.Position - actor.Position).LengthSquared();
        // +10 to account for the npc moving to skill range distance & allowing for it to attack slightly outside
        float detectDistance = skill.Data.Detect.Distance + 10;

        return detectDistance * detectDistance >= targetDistance; // naive, need to implement more sophisticated collision detection
    }

    private bool ProcessCondition(ExtraDataCondition node) {
        int value = 0;
        if (actor is FieldPet pet) {
            switch (node.Key) {
                case "tamingPoint":
                    value = pet.TamingPoint;
                    break;
                default: // aiPresets
                    return pet.Metadata.AiPresets.Contains(node.Key);
            }
        }

        return PerformOperation(node.Op, node.Value, actor.AiExtraData.GetValueOrDefault(node.Key, value));
    }

    private bool ProcessCondition(SlaveCountCondition node) {
        // Exact count (Shoo and Booboo test 4, 3, 2, 1, 0 in turn); with useSummonGroup only that group's slaves.
        return LiveSlaves().Count(slave => !node.UseSummonGroup || slave.SummonGroup == node.SummonGroup) == node.Count;
    }

    private bool ProcessCondition(SlaveCountOpCondition node) {
        return false;
    }

    private bool ProcessCondition(HpOverCondition node) {
        Stat health = actor.Stats.Values[BasicAttribute.Health];

        return node.Value <= ((float) health.Current / (float) health.Total) * 100f;
    }

    private bool ProcessCondition(StateCondition node) {
        if (actor.BattleState.Target is null) {
            return false;
        }

        ActorState state = ActorState.None;

        if (actor.BattleState.Target is FieldPlayer player) {
            state = player.State;
        }

        if (actor.BattleState.Target is FieldNpc npc) {
            state = npc.MovementState.State;
        }

        return node.TargetState switch {
            AiConditionTargetState.GrabTarget => state == ActorState.GrabTarget,
            AiConditionTargetState.HoldMe => state == ActorState.Hold,
            _ => false,
        };
    }

    private bool ProcessCondition(AdditionalCondition node) {
        if (actor.BattleState.Target is null && node.IsTarget) {
            return false;
        }
        if (node.IsTarget) {
            return actor.BattleState.Target!.Buffs.HasBuff(node.Id, node.Level, node.OverlapCount);
        }

        return actor.Buffs.HasBuff(node.Id, node.Level, node.OverlapCount);
    }

    private bool ProcessCondition(HpLessCondition node) {
        Stat health = actor.Stats.Values[BasicAttribute.Health];

        return node.Value >= ((float) health.Current / (float) health.Total) * 100f;
    }

    private bool ProcessCondition(TrueCondition node) {
        return true;
    }

    private void ProcessNode(SummonNode node) {
        int? npcId = SummonRules.Resolve(NpcSummons.For(actor.Value.Metadata.Id), node.NpcId);
        if (npcId is null || !actor.Field.NpcMetadata.TryGet(npcId.Value, out NpcMetadata? metadata)) {
            actor.AppendDebugMessage($"Summon {node.NpcId}: not in the summon list\n");
            return;
        }

        bool linked = node.Master != NodeSummonMaster.None;
        int live = actor.Field.EnumerateNpcs().Count(npc => npc.Summoner == actor && npc.IsSlave == linked && (!linked || npc.SummonGroup == node.Group));
        int count = SummonRules.Count(node.NpcCount, node.NpcCountMax, live, linked);
        for (int i = 0; i < count; i++) {
            if (node.DelayTick > 0) {
                actor.Field.Scheduler.Schedule(() => Summon(node, metadata!, linked), TimeSpan.FromMilliseconds(node.DelayTick));
            } else {
                Summon(node, metadata!, linked);
            }
        }
    }

    private void Summon(SummonNode node, NpcMetadata metadata, bool linked) {
        if (actor.IsDead) {
            return; // a delayed summon whose summoner died meanwhile
        }

        Vector3 position = SummonRules.Position(actor.Position, actor.BattleState.Target?.Position, node.SummonPos, node.SummonPosOffset,
            node.SummonTargetOffset, node.SummonRadius, Random.Shared);
        FieldNpc? summon = actor.Field.SpawnNpc(metadata, position, node.SummonRot != Vector3.Zero ? node.SummonRot : actor.Rotation);
        if (summon == null) {
            return;
        }

        summon.Summoner = actor;
        summon.IsSlave = linked;
        summon.SummonGroup = node.Group;
        if (node.Option?.Contains(NodeSummonOption.MasterHp) == true) {
            // The next phase's body starts with the summoner's health (Turka, Madria).
            var health = summon.Stats.Values[BasicAttribute.Health];
            health.Current = Math.Clamp(actor.Stats.Values[BasicAttribute.Health].Current, 1, health.Total);
        }
        actor.Field.Broadcast(FieldPacket.AddNpc(summon));
        actor.Field.Broadcast(ProxyObjectPacket.AddNpc(summon));
        if (node.LifeTime > 0) {
            actor.Field.RemoveNpc(summon.ObjectId, SummonRules.LifeTime(node.LifeTime));
        }
        actor.AppendDebugMessage($"Summon {metadata.Id} ({(linked ? $"slave, group {node.Group}" : "unlinked")})\n");
    }

    private void ProcessNode(RemoveSlavesNode node) {
        actor.RemoveSlaves();
    }

    private List<FieldNpc> LiveSlaves() => actor.Field.EnumerateNpcs().Where(npc => npc.Summoner == actor && npc.IsSlave && !npc.IsDead).ToList();

    private void ProcessNode(RemoveMeNode node) {
        actor.RemoveSlaves();
        actor.Field.RemoveNpc(actor.ObjectId);
    }
}
