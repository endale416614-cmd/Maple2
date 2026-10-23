using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

public class LightCommand : GameCommand {
    public LightCommand(GameSession session) : base(AdminPermissions.GameMaster, "light", "Set or reset the field's ambient and directional light.") {
        var ambient = new Argument<string>("ambient", "Ambient light color in r,g,b format (0-255 each) or 'reset'.");
        var diffuse = new Option<string>(["--diffuse", "-d"], "Diffuse light color in r,g,b format (0-255 each).");
        var specular = new Option<string>(["--specular", "-s"], "Specular light color in r,g,b format (0-255 each).");

        AddArgument(ambient);
        AddOption(diffuse);
        AddOption(specular);

        this.SetHandler<InvocationContext, string, string, string>((ctx, ambientValue, diffuseValue, specularValue) => {
            if (session.Field is null) {
                ctx.Console.Error.WriteLine("Error: No field.");
                return;
            }

            if (ambientValue == "reset") {
                session.Field.RemoveFieldProperty(FieldProperty.AmbientLight);
                session.Field.RemoveFieldProperty(FieldProperty.DirectionalLight);
                ctx.Console.Out.WriteLine("Light reset.");
                ctx.ExitCode = 0;
                return;
            }

            var ambientColor = ParseColor(ambientValue);
            if (ambientColor == null) {
                ctx.Console.Error.WriteLine("Usage: /light <r,g,b>|reset [--diffuse r,g,b] [--specular r,g,b]");
                return;
            }

            session.Field.AddFieldProperty(new FieldPropertyAmbientLight { Color = ambientColor.Value });

            if (!string.IsNullOrEmpty(diffuseValue)) {
                var diffuseColor = ParseColor(diffuseValue);
                if (diffuseColor == null) {
                    ctx.Console.Error.WriteLine("Usage: /light <r,g,b>|reset [--diffuse r,g,b] [--specular r,g,b]");
                    return;
                }

                var specularColor = ParseColor(specularValue ?? "0,0,0");
                if (specularColor == null) {
                    ctx.Console.Error.WriteLine("Usage: /light <r,g,b>|reset [--diffuse r,g,b] [--specular r,g,b]");
                    return;
                }
                session.Field.AddFieldProperty(new FieldPropertyDirectionalLight {
                    DiffuseColor = diffuseColor.Value,
                    SpecularColor = specularColor.Value
                });

                ctx.Console.Out.WriteLine($"Light: ambient {ambientValue}, diffuse {diffuseValue}");
            } else {
                ctx.Console.Out.WriteLine($"Light: ambient {ambientValue}");
            }

            ctx.ExitCode = 0;
        }, ambient, diffuse, specular);
    }

    public static System.Numerics.Vector3? ParseColor(string text) {
        // Split by comma and trim spaces
        string[] parts = text.Split(',');
        if (parts.Length != 3) {
            return null;
        }

        var colors = new int[3];
        for (int i = 0; i < 3; i++) {
            if (!int.TryParse(parts[i].Trim(), out int color)) {
                return null;
            }

            if (color < 0 || color > 255) {
                return null;
            }

            colors[i] = color;
        }

        return new System.Numerics.Vector3(colors[0], colors[1], colors[2]);
    }
}