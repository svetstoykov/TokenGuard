namespace Codexplorer.Automation.Client;

internal class CodexplorerAutomationTransportException : Exception
{
    public CodexplorerAutomationTransportException(string message)
        : base(message)
    {
    }

    public CodexplorerAutomationTransportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
