using System.Text;
using System.Text.Json.Serialization;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using ChatGptNet;
using ChatGptNet.Extensions;
using ChatGptNet.Models;
using OperationResults;
using SearchGpt.BusinessLayer.Services.Interfaces;
using SearchGpt.Shared.Models;

namespace SearchGpt.BusinessLayer.Services;

public class ChatService : IChatService
{
    private readonly IChatGptClient chatGptClient;
    private readonly SearchClient searchClient;

    private static readonly SearchOptions searchOptions;

    static ChatService()
    {
        searchOptions = new SearchOptions
        {
            HighlightFields = { "content-10" }
        };

        searchOptions.Select.Add("content");
    }

    public ChatService(IChatGptClient chatGptClient, SearchClient searchClient)
    {
        this.chatGptClient = chatGptClient;
        this.searchClient = searchClient;
    }

    public async Task<Result<ChatResponse>> AskAsync(ChatRequest request)
    {
        await SetupAsync(request);

        var response = await chatGptClient.AskAsync(request.ConversationId, request.Message);
        return new ChatResponse(response.GetMessage());
    }

    public async IAsyncEnumerable<string> AskStreamAsync(ChatRequest request)
    {
        await SetupAsync(request);

        var responseStream = chatGptClient.AskStreamAsync(request.ConversationId, request.Message);

        await foreach (var response in responseStream.AsDeltas())
        {
            yield return response;
        }
    }

    public async Task<Result> DeleteAsync(Guid conversationId)
    {
        await chatGptClient.DeleteConversationAsync(conversationId);
        return Result.Ok();
    }

    private async Task SetupAsync(ChatRequest request)
    {
        var chatGptResponse = await chatGptClient.AskAsync($$"""
            Generate a search query based on names and concepts extracted from the following question:
            ---
            {{request.Message}}
            ---
            When you answer, always use the same language of the question.
            """, new ChatGptParameters { Temperature = 0 });

        var query = chatGptResponse.GetMessage().Trim('"');

        var searchResults = await searchClient.SearchAsync<SearchDocument>(query, searchOptions);
        await LoadDocumentsAsync(searchResults);

        async Task LoadDocumentsAsync(SearchResults<SearchDocument> searchResults)
        {
            var builder = new StringBuilder();

            foreach (var result in searchResults.GetResults())
            {
                var highligths = string.Join(Environment.NewLine, result.Highlights["content"]);

                builder.AppendLine(highligths);
                builder.AppendLine("---");
            }

            var setupMessage = $$"""
                You are an assistant that knows the following information only:
                ---
                {{builder}}
                When you answer, always use the same language of the question.
                You can use only the information above to answer questions.
                If you don't know the answer, reply suggesting to refine the question.
                """;

            await chatGptClient.SetupAsync(request.ConversationId, setupMessage);
        }
    }
}

public class SearchDocument
{
    [JsonPropertyName("content")]
    public string Content { get; set; }
}