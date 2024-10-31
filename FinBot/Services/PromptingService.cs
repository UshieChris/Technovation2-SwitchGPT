using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel;
using FinBot.Data;
using Microsoft.EntityFrameworkCore;
using AuthorRole = Microsoft.SemanticKernel.ChatCompletion.AuthorRole;
using ChatHistory = Microsoft.SemanticKernel.ChatCompletion.ChatHistory;
using FinBot.Extensions;
using System.Text;

namespace FinBot.Services;

public class PromptingService
{
    private readonly IChatCompletionService _chatCompletionService;
    private readonly ApplicationDbContext _dbContext;

    public PromptingService(
        IChatCompletionService chatCompletionService, IServiceScopeFactory serviceScopeFactory,
        ApplicationDbContext dbContext)
    {
        _chatCompletionService = chatCompletionService;
        _dbContext = dbContext;
    }

    public async Task<(ChatMessage message, IAsyncEnumerable<StreamingChatMessageContent>)> SendResponse(ChatMessage chatMessage)
    {
        string userPrompt = chatMessage.Text;
        FinBotIntent intent = await DetermineIntent(userPrompt);

        var messages = await _dbContext.ChatMessages
            .Where(m => m.UserId == chatMessage.UserId)
            .OrderBy(m => m.TimeStamp)
            .ToListAsync();

        ChatHistory chatHistory = 
        [
            new ChatMessageContent(AuthorRole.System, "You are a bot who provides financial assistance to users")
        ];

        chatHistory.AddRange(messages.Select(m => new ChatMessageContent(m.IsBotMessage ? AuthorRole.Assistant : AuthorRole.User, m.Text)));

        IAsyncEnumerable<StreamingChatMessageContent> responseStream;
        switch (intent)
        {
            case FinBotIntent.GenericChat:
                responseStream = _chatCompletionService.GetStreamingChatMessageContentsAsync(chatHistory);
                break;
            case FinBotIntent.SpendingInsights:
                var transactions = _dbContext.Transactions
                    .Where(t => t.UserId == chatMessage.UserId)
                    .ToList();
                responseStream = GetSpendingInsights(transactions);
                break;
            case FinBotIntent.FinancialRecommendations:
                responseStream = _chatCompletionService.GetStreamingChatMessageContentsAsync(chatHistory);
                break;
            default:
                responseStream = _chatCompletionService.GetStreamingChatMessageContentsAsync(chatHistory);
                break;
        }

        var response = new ChatMessage
        {
            Text = "",
            UserId = chatMessage.UserId,
            TimeStamp = DateTime.Now,
            IsBotMessage = true,
        };

        return (response, responseStream);
    }

    async Task<FinBotIntent> DetermineIntent(string userPrompt)
    {
        ChatHistory chatHistory =
            [
                new ChatMessageContent(AuthorRole.System,
                """
                    You are an assistant who determines the user's intent. Here are the possible intents and their keywords:
                        1. Having a generic chat on finance - GenericChat
                        2. Requesting spending insights - SpendingInsights
                        3. Requesting financial recommendations - FinancialRecommendations

                    Response Format:
                    ```{ "intent": "GenericChat" }``` or ```{ "intent": "SpendingInsights" }``` or ```{ "intent": "FinancialRecommendations" }```
                            
                    You are to always respond in the JSON format provided above and ensure that nothing else is stated in your response.
                """),
                new ChatMessageContent(AuthorRole.User, userPrompt),
        ];


        ChatMessageContent chatResult = await _chatCompletionService.GetChatMessageContentAsync(chatHistory);
        string content = chatResult.Content == null
            ? ""
            : new StringBuilder(chatResult.Content.Trim())
                .Replace("```", "")
                .Replace("User:", "")
                .Replace("Assistant:", "")
                .Replace("System:", "")
                .ToString();

        var intentJson = new { Intent = "" };
        intentJson = content.FromJsonAnonymously(intentJson);

        return intentJson.Intent switch
        {
            "GenericChat" => FinBotIntent.GenericChat,
            "SpendingInsights" => FinBotIntent.SpendingInsights,
            "FinancialRecommendations" => FinBotIntent.FinancialRecommendations,
            _ => FinBotIntent.GenericChat,
        };
    }

    IAsyncEnumerable<StreamingChatMessageContent> GetSpendingInsights(List<Transaction> transactions)
    {
        string transactionsJson = transactions.ToJson();

        string userPrompt = "I would like to get insights on my spending";
        ChatHistory chatHistory =
            [
                new ChatMessageContent(AuthorRole.System,
                $"""
                    You are an assistant who provides financial insights based on a user's transactions.
                    The transaction data is in a JSON format provided to you below. This data contains the following fields:
                        1. Id - Unique identifier for the transaction
                        2. UserId - Unique identifier for the user
                        3. Amount - The amount of the transaction
                        4. Narration - Description of the transaction
                        5. TimeStamp - The time the transaction occurred
                        6. Type - Indicates if the transaction is a credit or debit

                            
                    You are to analyze the transaction data and respond with useful information based on the data provided.

                    {transactionsJson}
                """),
                new ChatMessageContent(AuthorRole.User, userPrompt),
        ];


        return _chatCompletionService.GetStreamingChatMessageContentsAsync(chatHistory);
    }
}

public enum FinBotIntent
{
    GenericChat,
    SpendingInsights,
    FinancialRecommendations
}