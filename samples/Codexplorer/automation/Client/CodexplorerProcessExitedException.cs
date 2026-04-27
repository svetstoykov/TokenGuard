namespace Codexplorer.Automation.Client;

internal sealed class CodexplorerProcessExitedException : CodexplorerAutomationTransportException
{
    public CodexplorerProcessExitedException(string message, int? exitCode, string standardError)
        : base(message)
    {
        this.ExitCode = exitCode;
        this.StandardError = standardError;
    }

    public int? ExitCode { get; }

    public string StandardError { get; }
}
