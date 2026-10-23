using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Serilog;

namespace Maple2.Server.Game.Trigger.Helpers;

public class TriggerState {
    private readonly Trigger trigger;
    private readonly Trigger.State triggerState;
    private readonly TriggerContext triggerContext;
    private readonly Trigger.RandomCondition[] randoms;

    public readonly string Name;

    public TriggerState(Trigger trigger, TriggerContext context) {
        this.trigger = trigger;
        triggerState = trigger.States.First();
        triggerContext = context;
        Name = triggerState.Name;
        randoms = triggerState.Conditions.OfType<Trigger.RandomCondition>().ToArray();
    }

    public TriggerState(Trigger trigger, Trigger.State nextState, TriggerContext context) {
        this.trigger = trigger;
        triggerState = nextState;
        triggerContext = context;
        Name = triggerState.Name;
        randoms = triggerState.Conditions.OfType<Trigger.RandomCondition>().ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TriggerState? OnEnter() {
        Trigger.State.OnEnter? onEnter = triggerState.Enter;
        if (onEnter == null) return null;

        foreach (IAction actionNode in onEnter.Actions) {
            ExecuteAction(actionNode);
        }

        if (onEnter.NextState is not null && GetNextState(onEnter.NextState, out Trigger.State? nextState)) {
            return new TriggerState(trigger, nextState, triggerContext);
        }

        return null;
    }


    public TriggerState? OnTick() {
        LinkedList<ICondition> conditions = triggerState.Conditions;
        if (conditions.Count == 0) return null;

        // One roll per tick for the state's random_condition branches; the other conditions are evaluated as before.
        Trigger.RandomCondition? picked = null;
        if (randoms.Length > 0) {
            int index = PickRandom(randoms.Select(random => random.Weight).ToArray(), Random.Shared.NextDouble());
            picked = index >= 0 ? randoms[index] : null;
        }

        foreach (ICondition condition in conditions) {
            if (!(condition is Trigger.RandomCondition ? condition == picked : CallCondition(condition))) continue;

            foreach (IAction actionNode in condition.Actions) {
                ExecuteAction(actionNode);
            }

            if (condition.NextState is not null && GetNextState(condition.NextState, out Trigger.State? nextState)) {
                return new TriggerState(trigger, nextState, triggerContext);
            }

            return null;
        }
        return null;
    }

    /// <summary>random_condition: a state's random branches share one roll by relative weight (the data's sums run
    /// from 2 to 1,000). Returns the index of the branch that fires for <paramref name="roll"/> in [0, 1), or -1 when
    /// no weight is positive.</summary>
    public static int PickRandom(IReadOnlyList<float> weights, double roll) {
        double total = weights.Where(weight => weight > 0).Sum(weight => (double) weight);
        if (total <= 0) {
            return -1;
        }
        double rest = roll * total;
        int last = -1;
        for (int i = 0; i < weights.Count; i++) {
            if (weights[i] <= 0) {
                continue;
            }
            last = i;
            rest -= weights[i];
            if (rest < 0) {
                return i;
            }
        }
        return last; // rounding: a roll just below 1
    }

    public void OnExit() {
        Trigger.State.OnExit? onExit = triggerState.Exit;
        if (onExit == null) return;
        foreach (IAction actionNode in onExit.Actions) {
            ExecuteAction(actionNode);
        }
    }

    private bool GetNextState(string nextStateName, [NotNullWhen(true)] out Trigger.State? nextState) {
        nextState = trigger.States.FirstOrDefault(x => x.Name == nextStateName);
        return nextState != null;
    }

    private void ExecuteAction(IAction action) {
        try {
            action.Execute(triggerContext);
        } catch (Exception e) {
            Log.Logger.Error(e, "Error executing action '{ActionName}'", nameof(action));
        }
    }

    private bool CallCondition(ICondition condition) {
        try {
            return condition.Evaluate(triggerContext);
        } catch (Exception e) {
            Log.Logger.Error(e, "Error evaluating condition '{ConditionName}'", nameof(condition));
            return false;
        }
    }
}
