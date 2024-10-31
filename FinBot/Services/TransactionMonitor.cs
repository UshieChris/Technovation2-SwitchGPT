using FinBot.Data;
using System.Collections.Concurrent;

namespace FinBot.Services;

public class TransactionMonitor : BackgroundService
{
    const int DuplicateTransactionTimeThreshold = 5;
    const double CreditVariabilityThreshold = 70;
    const double DebitVariabilityThreshold = 10;

    readonly Dictionary<string, TransactionMetadata> _creditTransactionAverages = new();
    readonly Dictionary<string, TransactionMetadata> _debitTransactionAverages = new();
    readonly Dictionary<string, decimal> _userBalances = new();
    readonly Dictionary<string, DateTime> _userRecommendationTimestamp = new();
    readonly ConcurrentDictionary<string, DateTime> _transactionTimestampCache = new();


    readonly Timer _duplicateTimer;
    readonly Timer _recommedationsTimer;
    readonly IServiceScope _serviceScope;

    public TransactionMonitor(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScope = serviceScopeFactory.CreateScope();
        _duplicateTimer = new Timer(_ => ResetDuplicatesCache(), null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        // TODO: Change recommendations interval to 1 day
        _recommedationsTimer = new Timer(_ => SendLoanAndInvestmentRecommendations(), null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Initialise();
        var queueFactory = _serviceScope.ServiceProvider.GetRequiredService<TransactionQueue>();

        while (!ct.IsCancellationRequested)
        {
            if (queueFactory.Transactions.Count < 1)
            {
                await Task.Delay(5000);
                continue;
            }

            var transaction = queueFactory.Transactions.Peek();
            ValidateThreshold(transaction);
            ValidateDuplicates(transaction);
            UpdateAverage(transaction);
            UpdateBalance(transaction);
            queueFactory.Transactions.Dequeue();
        }
    }

    void Initialise()
    {
        var dbContext = _serviceScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transactionGroups = dbContext.Transactions
            .ToList()
            .GroupBy(x => x.UserId);

        foreach (var group in transactionGroups)
        {
            var creditTransactions = group.Where(x => x.Type == TransactionType.Credit).ToList();
            var debitTransactions = group.Where(x => x.Type == TransactionType.Debit).ToList();

            decimal creditAverage = creditTransactions.Sum(x => x.Amount) / creditTransactions.Count;
            decimal debitAverage = debitTransactions.Sum(x => x.Amount) / debitTransactions.Count;

            _creditTransactionAverages.Add(group.Key, new TransactionMetadata
            {
                Average = creditAverage,
                Count = creditTransactions.Count
            });
            _debitTransactionAverages.Add(group.Key, new TransactionMetadata
            {
                Average = debitAverage,
                Count = debitTransactions.Count
            });
        }
    }

    void ValidateThreshold(Transaction transaction)
    {
        bool isFirstTransaction = transaction.Type == TransactionType.Credit
            ? _creditTransactionAverages.ContainsKey(transaction.UserId) == false
            : _debitTransactionAverages.ContainsKey(transaction.UserId) == false;

        if (isFirstTransaction)
            return;

        // TODO: Implement better threshold logic
        decimal currentAverage = transaction.Type == TransactionType.Credit
            ? _creditTransactionAverages[transaction.UserId].Average
            : _debitTransactionAverages[transaction.UserId].Average;


        double percentageDifference = (double)(Math.Abs(transaction.Amount - currentAverage) / currentAverage);
        double anomalyScore = percentageDifference * 100;
        bool isAnomalous = transaction.Type == TransactionType.Credit && percentageDifference > CreditVariabilityThreshold
            || transaction.Type == TransactionType.Debit && percentageDifference > DebitVariabilityThreshold;

        if (isAnomalous)
        {
            var messageSender = _serviceScope.ServiceProvider.GetRequiredService<MessageService>();
            messageSender.SendThresholdExceededWarning(transaction.UserId, transaction, anomalyScore);
        }
    }

    void ValidateDuplicates(Transaction transaction)
    {
        string key = $"{transaction.UserId}-{transaction.Type}-{transaction.Amount}";
        bool similarTransactionExists = _transactionTimestampCache.TryGetValue(key, out DateTime previousTimeStamp);
        bool isDuplicate = false;

        if (similarTransactionExists)
        {
            isDuplicate = (transaction.TimeStamp - previousTimeStamp).TotalMinutes < DuplicateTransactionTimeThreshold;
            _transactionTimestampCache.TryUpdate(key, transaction.TimeStamp, previousTimeStamp);
        }
        else
        {
            _transactionTimestampCache.TryAdd(key, transaction.TimeStamp);
        }

        if (isDuplicate)
        {
            var messageSender = _serviceScope.ServiceProvider.GetRequiredService<MessageService>();
            messageSender.SendDuplicateTransactionWarning(transaction.UserId, transaction);
        }
    }

    void UpdateAverage(Transaction transaction)
    {
        if (transaction.Type == TransactionType.Credit)
            AddCredit(transaction);
        else
            AddDebit(transaction);


        void AddDebit(Transaction transaction)
        {
            if (_debitTransactionAverages.ContainsKey(transaction.UserId))
            {
                string userId = transaction.UserId;
                decimal currentTotal = _debitTransactionAverages[userId].Average * _debitTransactionAverages[userId].Count;
                decimal newTotal = currentTotal + transaction.Amount;
                _debitTransactionAverages[userId].Count++;
                _debitTransactionAverages[userId].Average = newTotal / _debitTransactionAverages[userId].Count;
            }
            else
            {
                _debitTransactionAverages.Add(transaction.UserId, new TransactionMetadata
                {
                    Average = transaction.Amount,
                    Count = 1
                });
            }

        }
        void AddCredit(Transaction transaction)
        {
            if (_creditTransactionAverages.ContainsKey(transaction.UserId))
            {
                string userId = transaction.UserId;
                decimal currentTotal = _creditTransactionAverages[userId].Average * _creditTransactionAverages[userId].Count;
                decimal newTotal = currentTotal + transaction.Amount;
                _creditTransactionAverages[userId].Count++;
                _creditTransactionAverages[userId].Average = newTotal / _creditTransactionAverages[userId].Count;
            }
            else
            {
                _creditTransactionAverages.Add(transaction.UserId, new TransactionMetadata
                {
                    Average = transaction.Amount,
                    Count = 1
                });
            }
        }
    }

    void UpdateBalance(Transaction transaction)
    {
        decimal amount = transaction.Type == TransactionType.Credit ? transaction.Amount : -transaction.Amount;

        if (_userBalances.ContainsKey(transaction.UserId))
            _userBalances[transaction.UserId] += amount;
        else
            _userBalances.Add(transaction.UserId, amount);
    }

    void SendLoanAndInvestmentRecommendations()
    {
        foreach (var user in _userBalances)
        {
            bool hasPreviousRecommendation = _userRecommendationTimestamp.ContainsKey(user.Key);
            bool recommendedInPastWeek = hasPreviousRecommendation ? (DateTime.Now - _userRecommendationTimestamp[user.Key]).TotalDays < 7 : false;

            if (recommendedInPastWeek)
                continue;

            // TODO: Implement configurable balance logic for recommendations using averages and percentages
            if (user.Value < 1_000)
            {
                var messageSender = _serviceScope.ServiceProvider.GetRequiredService<MessageService>();
                messageSender.SendLoanRecommendation(user.Key, user.Value);
                _userRecommendationTimestamp[user.Key] = DateTime.Now;
            }

            else if (user.Value > 1_500_000)
            {
                var messageSender = _serviceScope.ServiceProvider.GetRequiredService<MessageService>();
                messageSender.SendInvestmentRecommendation(user.Key, user.Value);
                _userRecommendationTimestamp[user.Key] = DateTime.Now;
            }


        }
    }

    void ResetDuplicatesCache()
    {
        // Remove all expired transactions from the cache
        _transactionTimestampCache.Where(x => (DateTime.Now - x.Value).TotalMinutes > DuplicateTransactionTimeThreshold)
            .ToList()
            .ForEach(x => _transactionTimestampCache.TryRemove(x));
    }


    public class TransactionMetadata
    {
        public decimal Average { get; set; }
        public int Count { get; set; }
    }
}

public class TransactionQueue
{
    public bool IsEnabled { get; set; } = true;
    public Queue<Transaction> Transactions { get; set; } = new();
}