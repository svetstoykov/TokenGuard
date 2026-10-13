using TokenGuard.Core.Models;

namespace TokenGuard.Tests.TokenCounting;

/// <summary>
///     Represents one message of the reference fixture together with the token count a real tokenizer gave it.
/// </summary>
/// <param name="Id">The unique name of the sample.</param>
/// <param name="Kind">The text kind of the sample, which selects its tolerance.</param>
/// <param name="Message">The message to count.</param>
/// <param name="ReferenceTokens">The recorded token count of the message.</param>
internal sealed record TokenCountReferenceSample(string Id, string Kind, ContextMessage Message, int ReferenceTokens);
