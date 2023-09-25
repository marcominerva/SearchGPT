using OperationResults;
using SearchGpt.Shared.Models;

namespace SearchGpt.BusinessLayer.Services.Interfaces;

public interface IChatService
{
    Task<Result<ChatResponse>> AskAsync(ChatRequest request);

    IAsyncEnumerable<string> AskStreamAsync(ChatRequest request);
}