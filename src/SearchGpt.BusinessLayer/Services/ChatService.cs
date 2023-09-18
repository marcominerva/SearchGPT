using System.Text;
using System.Text.Json.Serialization;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using ChatGptNet;
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

    private const string ContentFilteredMessage = "The response was filtered by the content filtering system. Please modify your prompt and retry. To learn more about content filtering policies please read the documentation: https://go.microsoft.com/fwlink/?linkid=2198766";

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
        var question = await SetupAsync(request);
        var response = await chatGptClient.AskAsync(request.ConversationId, question, addToConversationHistory: false);

        string answer;
        if (response.IsContentFiltered)
        {
            answer = ContentFilteredMessage;
        }
        else
        {
            answer = response.GetContent();
            await chatGptClient.AddInteractionAsync(request.ConversationId, request.Message, answer);
        }

        return new ChatResponse(answer);
    }

    public async IAsyncEnumerable<string> AskStreamAsync(ChatRequest request)
    {
        var question = await SetupAsync(request);
        var responseStream = chatGptClient.AskStreamAsync(request.ConversationId, question, addToConversationHistory: false);

        var answer = new StringBuilder();
        await foreach (var response in responseStream)
        {
            if (response.IsContentFiltered)
            {
                yield return ContentFilteredMessage;
            }
            else
            {
                var content = response.GetContent();
                answer.Append(content);

                yield return content;
            }
        }

        if (answer.Length > 0)
        {
            await chatGptClient.AddInteractionAsync(request.ConversationId, request.Message, answer.ToString());
        }
    }

    public async Task<Result> DeleteAsync(Guid conversationId)
    {
        await chatGptClient.DeleteConversationAsync(conversationId);
        return Result.Ok();
    }

    private async Task<string> SetupAsync(ChatRequest request)
    {
        var conversationExists = await chatGptClient.ConversationExistsAsync(request.ConversationId);
        if (!conversationExists)
        {
            await chatGptClient.SetupAsync(request.ConversationId, """
                You are an Assistant that acts as a search engine. You can use only the information that are included in this chat.
                If you don't know the answer, suggest to refine the question. When you answer, always use the same language of the question.
                """);
        }

        var chatGptResponse = await chatGptClient.AskAsync(request.ConversationId, $$"""
            Generate a search query based on names and concepts extracted from the following question, taking into account also the previous queries:
            ---
            {{request.Message}}
            ---
            """, new ChatGptParameters { Temperature = 0 });

        var query = chatGptResponse.GetContent().Trim('"');

        var searchResults = await searchClient.SearchAsync<SearchDocument>(query, searchOptions);
        return GenerateQuestion(searchResults);

        string GenerateQuestion(SearchResults<SearchDocument> searchResults)
        {
            var builder = new StringBuilder();

            foreach (var result in searchResults.GetResults())
            {
                var highligths = string.Join(Environment.NewLine, result.Highlights["content"]);

                builder.AppendLine(highligths);
                builder.AppendLine("---");
            }

            var question = $"""
                Giving the following information:
                ---
                {builder}
                Answer the following question:
                ---
                {request.Message}
                """;

            return question;
        }
    }
}

public class SearchDocument
{
    [JsonPropertyName("content")]
    public string Content { get; set; }
}