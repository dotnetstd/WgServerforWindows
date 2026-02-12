using System.Linq;

namespace WgAPI.Commands
{
    public class ShowCommand : WireGuardCommand
    {
        public ShowCommand(string interfaceName, params string[] subCommands) : base
        (
            @switch: "show",
            whichExe: WhichExe.WGExe,
            args: new[] { interfaceName }.Concat(subCommands).ToArray()
        )
        {
        }
    }
}
