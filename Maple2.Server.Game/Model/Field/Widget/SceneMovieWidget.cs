using System.Collections.Concurrent;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Game.Model.Widget;

public class SceneMovieWidget : Widget {

    public SceneMovieWidget(FieldManager field) : base(field) {
        Conditions = new ConcurrentDictionary<string, int>();
    }

    // TR-02 (BT-129): only a value the client reported counts; read as 0, an unset IsStop held trigger 52100105_03 in its first cutscene.
    public override bool Check(string name, string arg) {
        return int.TryParse(arg, out int value) && Conditions.TryGetValue(name, out int reported) && reported == value;
    }

    public override void Action(string function, int numericArg, string stringArg) {
        switch (function) {
            case "Clear":
                Clear();
                break;
        }
    }

    private void Clear() {
        Conditions.Clear();
    }
}
