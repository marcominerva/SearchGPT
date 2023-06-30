using SearchGpt.Shared.Models;
using OperationResults;

namespace SearchGpt.BusinessLayer.Services.Interfaces;

public interface IChatService
{
    Task<Result<ChatResponse>> AskAsync(ChatRequest request);

    IAsyncEnumerable<string> AskStreamAsync(ChatRequest request);

    Task<Result> DeleteAsync(Guid conversationId);
}