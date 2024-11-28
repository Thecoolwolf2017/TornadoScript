namespace TornadoScript.ScriptMain.Commands
{
    public interface ICommandHandler
    {
        string Execute(string[] args);
    }
}
