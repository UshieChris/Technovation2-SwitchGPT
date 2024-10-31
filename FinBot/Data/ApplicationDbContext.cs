using FinBot.Extensions;
using FinBot.Services;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FinBot.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    const int TransactionRepeatTotal = 1;
    const int ChatRepeatTotal = 1;
    private readonly TransactionQueue _transactionQueue;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options,
        TransactionQueue transactionQueue) : base(options)
    {
        _transactionQueue = transactionQueue;
    }

    public DbSet<ChatMessage> ChatMessages { get; set; } = null!;
    public DbSet<Transaction> Transactions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
    }

    public async Task SeedData()
    {
        Users.AddRange(
            new ApplicationUser
            {
                UserName = "smarte",
                Email = "emmanuel@finbot.com",
                FirstName = "Emmanuel",
                LastName = "Adebiyi"
            },
            new ApplicationUser
            {
                UserName = "okobaba",
                Email = "christopherl@finbot.com",
                FirstName = "Christopher",
                LastName = "Ushie"
            },
            new ApplicationUser
            {
                UserName = "nadia",
                Email = "arnold@finbot.com",
                FirstName = "Arnold",
                LastName = "Ighiyiwisi"
            }
        );
        await SaveChangesAsync();

        foreach (var user in Users.ToList())
        {
            for (int i = 0; i < TransactionRepeatTotal; i++)
            {
                Transactions.Add(
                    new Transaction
                    {
                        Amount = Random.Shared.Next(5_000, 100_000),
                        Narration = "Initial deposit",
                        TimeStamp = DateTime.Now.AddMinutes(Random.Shared.Next(-5, 5)),
                        UserId = user.Id,
                        Type = TransactionType.Credit
                    }
                );
            }

            for (int i = 0; i < TransactionRepeatTotal; i++)
            {
                Transactions.Add(
                    new Transaction
                    {
                        Amount = Random.Shared.Next(5_000, 15_000),
                        Narration = "Data purchase",
                        TimeStamp = DateTime.Now.AddMinutes(Random.Shared.Next(-5, 5)),
                        UserId = user.Id,
                        Type = TransactionType.Debit
                    }
                );
            }
        }
        await SaveChangesAsync();


        foreach (var user in Users.ToList())
        {
            for (int i = 0; i < ChatRepeatTotal; i++)
            {
                ChatMessages.Add(
                    new ChatMessage
                    {
                        Text = "Hello, welcome to FinBot, how may I be of service?",
                        TimeStamp = DateTime.Now,
                        UserId = user.Id,
                        IsBotMessage = true
                    }
                );
            }
        }
        await SaveChangesAsync();
    }

    public async Task SeedUserData(string userId) 
    {
        _transactionQueue.IsEnabled = false;
        var transactions = _transactionData.FromJson<List<Transaction>>();
        transactions.ForEach(transaction => transaction.UserId = userId);

        Transactions.AddRange(transactions);
        await SaveChangesAsync();
        _transactionQueue.IsEnabled = true;
    }

    public override int SaveChanges()
    {
        var entities = ChangeTracker.Entries().Where(x => x.State == EntityState.Added).ToList();
        int result = base.SaveChanges();

        foreach (var entity in entities)
        {
            var transaction = entity.Entity as Transaction;

            if (transaction == null) continue;

            _transactionQueue.Transactions.Enqueue(transaction);
        }

        return result;
    }
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entities = ChangeTracker.Entries().Where(x => x.State == EntityState.Added).ToList();
        int result = await base.SaveChangesAsync(cancellationToken);

        foreach (var entity in entities)
        {
            var transaction = entity.Entity as Transaction;

            if (transaction == null) continue;

            if (_transactionQueue.IsEnabled)
                _transactionQueue.Transactions.Enqueue(transaction);
        }

        return result;
    }

    static string _transactionData =
        """
            [
          {
            "amount": 1500.00,
            "narration": "Tuition fee payment",
            "timestamp": "2023-09-01T09:00:00Z",
            "type": 1
          },
          {
            "amount": 500.00,
            "narration": "Bookstore purchase",
            "timestamp": "2023-09-05T10:30:00Z",
            "type": 1
          },
          {
            "amount": 2000.00,
            "narration": "Scholarship deposit",
            "timestamp": "2023-09-10T12:00:00Z",
            "type": 0
          },
          {
            "amount": 300.00,
            "narration": "Cafeteria meal plan",
            "timestamp": "2023-09-12T08:15:00Z",
            "type": 1
          },
          {
            "amount": 1200.00,
            "narration": "Student loan disbursement",
            "timestamp": "2023-09-15T14:00:00Z",
            "type": 0
          },
          {
            "amount": 250.00,
            "narration": "Gym membership fee",
            "timestamp": "2023-09-18T16:00:00Z",
            "type": 1
          },
          {
            "amount": 80.00,
            "narration": "Coffee shop visit",
            "timestamp": "2023-09-20T09:45:00Z",
            "type": 1
          },
          {
            "amount": 500.00,
            "narration": "Part-time job payment",
            "timestamp": "2023-09-25T17:00:00Z",
            "type": 0
          },
          {
            "amount": 100.00,
            "narration": "Laundry service",
            "timestamp": "2023-09-28T11:30:00Z",
            "type": 1
          },
          {
            "amount": 150.00,
            "narration": "Textbook rental",
            "timestamp": "2023-09-30T13:00:00Z",
            "type": 1
          },
          {
            "amount": 400.00,
            "narration": "Refund for dropped course",
            "timestamp": "2023-10-02T10:00:00Z",
            "type": 0
          },
          {
            "amount": 600.00,
            "narration": "Weekend trip expenses",
            "timestamp": "2023-10-05T15:00:00Z",
            "type": 1
          },
          {
            "amount": 800.00,
            "narration": "Internship stipend",
            "timestamp": "2023-10-07T10:15:00Z",
            "type": 0
          },
          {
            "amount": 200.00,
            "narration": "Movie night expenses",
            "timestamp": "2023-10-10T19:30:00Z",
            "type": 1
          },
          {
            "amount": 300.00,
            "narration": "Transportation costs",
            "timestamp": "2023-10-12T08:00:00Z",
            "type": 1
          },
          {
            "amount": 200.00,
            "narration": "Gift from parents",
            "timestamp": "2023-10-15T11:00:00Z",
            "type": 0
          },
          {
            "amount": 250.00,
            "narration": "Phone bill payment",
            "timestamp": "2023-10-18T09:30:00Z",
            "type": 1
          },
          {
            "amount": 500.00,
            "narration": "Scholarship bonus",
            "timestamp": "2023-10-20T13:45:00Z",
            "type": 0
          },
          {
            "amount": 70.00,
            "narration": "Stationery supplies",
            "timestamp": "2023-10-22T14:30:00Z",
            "type": 1
          },
          {
            "amount": 90.00,
            "narration": "Dining out with friends",
            "timestamp": "2023-10-25T18:00:00Z",
            "type": 1
          },
          {
            "amount": 300.00,
            "narration": "Freelance project payment",
            "timestamp": "2023-10-30T16:20:00Z",
            "type": 0
          },
          {
            "amount": 150.00,
            "narration": "Online course subscription",
            "timestamp": "2023-11-01T12:00:00Z",
            "type": 1
          },
          {
            "amount": 250.00,
            "narration": "Research project funding",
            "timestamp": "2023-11-03T15:00:00Z",
            "type": 0
          },
          {
            "amount": 60.00,
            "narration": "Coffee shop study session",
            "timestamp": "2023-11-05T10:15:00Z",
            "type": 1
          },
          {
            "amount": 400.00,
            "narration": "Graduation event deposit",
            "timestamp": "2023-11-10T14:00:00Z",
            "type": 1
          },
          {
            "amount": 1200.00,
            "narration": "Part-time job paycheck",
            "timestamp": "2023-11-12T16:00:00Z",
            "type": 0
          },
          {
            "amount": 300.00,
            "narration": "Fall semester refund",
            "timestamp": "2023-11-15T12:30:00Z",
            "type": 0
          },
          {
            "amount": 150.00,
            "narration": "Public transport card top-up",
            "timestamp": "2023-11-20T09:45:00Z",
            "type": 1
          },
          {
            "amount": 450.00,
            "narration": "Weekend concert ticket",
            "timestamp": "2023-11-25T20:00:00Z",
            "type": 1
          },
          {
            "amount": 1000.00,
            "narration": "Summer internship salary",
            "timestamp": "2023-11-30T14:00:00Z",
            "type": 0
          },
          {
            "amount": 200.00,
            "narration": "Subscription box payment",
            "timestamp": "2023-12-01T11:00:00Z",
            "type": 1
          },
          {
            "amount": 500.00,
            "narration": "Returned security deposit",
            "timestamp": "2023-12-05T16:00:00Z",
            "type": 0
          },
          {
            "amount": 350.00,
            "narration": "Holiday shopping",
            "timestamp": "2023-12-10T10:00:00Z",
            "type": 1
          },
          {
            "amount": 300.00,
            "narration": "Peer tutoring payment",
            "timestamp": "2023-12-12T12:30:00Z",
            "type": 0
          },
          {
            "amount": 80.00,
            "narration": "Snack expenses",
            "timestamp": "2023-12-15T15:00:00Z",
            "type": 1
          },
          {
            "amount": 250.00,
            "narration": "Study abroad fund contribution",
            "timestamp": "2023-12-20T09:30:00Z",
            "type": 0
          },
          {
            "amount": 500.00,
            "narration": "New laptop purchase",
            "timestamp": "2023-12-25T11:00:00Z",
            "type": 1
          },
          {
            "amount": 150.00,
            "narration": "Donation to charity",
            "timestamp": "2023-12-30T10:00:00Z",
            "type": 1
          }
        ]
        """;
}