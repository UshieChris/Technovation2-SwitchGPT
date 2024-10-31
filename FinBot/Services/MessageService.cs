using Azure;
using FinBot.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace FinBot.Services;

public class MessageService
{
    private IServiceScopeFactory _serviceScopeFactory;
    private readonly TelegramBotClient _bot;

    public MessageService(IServiceScopeFactory serviceScopeFactory, TelegramBotClient bot)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _bot = bot;

        // Save all messages to DB
        MessageStream.Subscribe(async (message) =>
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ChatMessages.Add(message.Message);
            await dbContext.SaveChangesAsync();


            if (message.Message.IsBotMessage)
            {
                var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == message.Message.UserId);

                if (user == null)
                    return;

                await _bot.SendTextMessageAsync(
                    chatId: user.MessagingId,
                    text: message.Message.Text
                );
            }

            if (message.Message.IsBotMessage)
                return;

            var promptingService = scope.ServiceProvider.GetRequiredService<PromptingService>();
            var response = await promptingService.SendResponse(message.Message);

            var updatedMessage = response.Item1;
            int c = 0;
            await foreach (var chunk in response.Item2)
            {
                var position = c == 0 
                    ? LLMResponseChunkPosition.Start
                    : LLMResponseChunkPosition.Middle;
                updatedMessage.Text += chunk?.Content ?? "";
                LLMOngoingMessageStream.OnNext((updatedMessage, position));
                c++;
            }

            LLMOngoingMessageStream.OnNext((updatedMessage, LLMResponseChunkPosition.End));
            LLMCompletedMessageStream.OnNext(updatedMessage);
        });

        LLMCompletedMessageStream.Subscribe(async message =>
        {

            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.ChatMessages.Add(message);
            await dbContext.SaveChangesAsync();

            var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == message.UserId);

            if (user == null)
                return;

            await _bot.SendTextMessageAsync(
                chatId: user.MessagingId,
                text: message.Text
            );
        });


        // Listen for incoming messages from External Chat app
        _bot.OnMessage += async (message, updateType) =>
        {
            if (message.Text == null)
                return;

            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var chatId = message.Chat.Id;

            var user = await dbContext.Users.FirstOrDefaultAsync(x => x.MessagingId == chatId.ToString());

            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = message.Chat.Username,
                    FirstName = message.Chat.FirstName!,
                    LastName = message.Chat.LastName!,
                    Email = message.Chat.Username + "@finbot.com",
                    MessagingId = chatId.ToString(),
                };
                dbContext.Users.Add(user);
                await dbContext.SaveChangesAsync();
                await dbContext.SeedUserData(user.Id);
            }

            if (message.Text == "/start")
                message.Text = "Hello there";

            // Create a new chat message
            var chatMessage = new ChatMessage
            {
                UserId = user.Id,
                IsBotMessage = false,
                Text = message.Text,
                TimeStamp = DateTime.Now,
            };

            // Send message to stream
            MessageStream.OnNext((chatMessage, true));
        };
    }

    public Subject<(ChatMessage Message, bool ShouldUpdateUI)> MessageStream { get; } = new();
    public Subject<(ChatMessage Message, LLMResponseChunkPosition position)> LLMOngoingMessageStream { get; } = new();
    public Subject<ChatMessage> LLMCompletedMessageStream { get; } = new();

    public void SendThresholdExceededWarning(string userId, Transaction transaction, double anomalyScore)
    {
        anomalyScore = Math.Round(anomalyScore, 2);
        var message = new ChatMessage
        {
            UserId = userId,
            IsBotMessage = true,
            Text =
                $"""
                    I have noticed a {transaction.Type} transaction of ₦{Math.Round(transaction.Amount, 2)} on your account. This exceeds your all-time average by {anomalyScore}%.
                    Please confirm if this transaction is legitimate.
                """,
            TimeStamp = DateTime.Now
        };
        MessageStream.OnNext((message, true));
    }

    public void SendDuplicateTransactionWarning(string userId, Transaction transaction)
    {
        var message = new ChatMessage
        {
            UserId = userId,
            IsBotMessage = true,
            Text =
                $"""
                    Hi, how're you doing?
                    It seems a duplicate {transaction.Type} transaction  of ₦{Math.Round(transaction.Amount, 2)} may have occurred on your account.
                    Please confirm and reach out to your account officer if further help is required.
                """,
            TimeStamp = DateTime.Now
        };
        MessageStream.OnNext((message, true));
    }

    public void SendLoanRecommendation(string userId, decimal userBalance)
    {
        var message = new ChatMessage
        {
            UserId = userId,
            IsBotMessage = true,
            Text =
                $"""
                    Hi, you seem to be running low on balance. Would you like to apply for a loan?
                    
                    Checkout our loan options at https://example.com/loans
                """,
            TimeStamp = DateTime.Now
        };
        MessageStream.OnNext((message, true));
    }

    public void SendInvestmentRecommendation(string userId, decimal userBalance)
    {
        var message = new ChatMessage
        {
            UserId = userId,
            IsBotMessage = true,
            Text =
                $"""
                    Hi, you seem to be keeping a lot of money in the bank. Would you mind to invest in assets that tend to increase the value of your money?
                    
                    Checkout our investment options at https://example.com/investments
                """,
            TimeStamp = DateTime.Now
        };
        MessageStream.OnNext((message, true));
    }
}

public enum LLMResponseChunkPosition
{
    Start,
    Middle,
    End
}
