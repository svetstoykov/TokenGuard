namespace Codexplorer.Automation.Client;

internal sealed class CodexplorerAutomationProtocolException : Exception
{
    public CodexplorerAutomationProtocolException(string? requestId, string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        this.RequestId = requestId;
        this.Code = code;
    }

    public string? RequestId { get; }

    public string Code { get; }
}
