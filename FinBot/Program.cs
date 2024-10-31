using FinBot.Components;
using FinBot.Data;
using LLama.Common;
using LLama;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel.ChatCompletion;
using Radzen;
using LLamaSharp.SemanticKernel.ChatCompletion;
using LLamaSharp.SemanticKernel;
using FinBot.Services;
using Telegram.Bot;

namespace FinBot;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddCascadingAuthenticationState();


        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

        builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase("FinBotDB"));
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();

        builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();


        builder.Services.AddScoped<PromptingService>();
        builder.Services.AddSingleton(_ => new TelegramBotClient(builder.Configuration["TelegramBotToken"]!));
        builder.Services.AddSingleton<MessageService>();
        builder.Services.AddSingleton<TransactionQueue>();
        
        builder.Services.AddHostedService<TransactionMonitor>();

        builder.Services.AddRadzenComponents();

        // Load weights into memory
        var parameters = new ModelParams(builder.Configuration["ModelPath"]!)
        {
            UseMemorymap = true,
            UseMemoryLock = true,
            GpuLayerCount = 15000,
        };
        using var model = LLamaWeights.LoadFromFile(parameters);
        var slex = new StatelessExecutor(model, parameters);

        builder.Services.AddSingleton<IChatCompletionService>(sp =>
        {
            return new LLamaSharpChatCompletion(slex,
                new LLamaSharpPromptExecutionSettings()
                {
                    MaxTokens = -1,
                    Temperature = 0,
                    TopP = 0.1,
                    StopSequences = ["User:"],
                });
        });

        var app = builder.Build();

        //using var scope = app.Services.CreateScope();
        //scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SeedData().GetAwaiter().GetResult();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseMigrationsEndPoint();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
