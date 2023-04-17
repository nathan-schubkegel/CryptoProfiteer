using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
  public interface ITransactionish
  {
    string Id { get; }
    TransactionType TransactionType { get; }
    CryptoExchange Exchange { get; }
    DateTime Time { get; }
    
    string ReceivedCoinType { get; }
    string PaymentCoinType { get; }

    Decimal ReceivedCoinCount { get; }
    Decimal PaymentCoinCount { get; }

    Decimal? ReceivedValueUsd { get; }
    Decimal? PaymentValueUsd { get; }
    
    Decimal? ReceivedPerCoinCostUsd { get; }
    Decimal? PaymentPerCoinCostUsd { get; }
  }
  
  public static class TransactionishExtensions
  {
    public static string FormatExplanation(this ITransactionish o, string contextualCoinType = null)
    {
      if (o.TransactionType == TransactionType.Trade)
      {
        if (contextualCoinType != null)
        {
          if (o.ReceivedCoinType == contextualCoinType)
          {
            return $"Bought for {o.PaymentCoinCount.FormatCoinCount(o.PaymentCoinType)}";
          }
          if (o.PaymentCoinType == contextualCoinType)
          {
            return $"Sold for {o.ReceivedCoinCount.FormatCoinCount(o.ReceivedCoinType)}";
          }
        }

        if (SomeUtils.IsBasicallyUsd(o.PaymentCoinType) && !SomeUtils.IsBasicallyUsd(o.ReceivedCoinType))
        {
          return $"Bought {o.ReceivedCoinType}";
        }
        else if (!SomeUtils.IsBasicallyUsd(o.PaymentCoinType) && SomeUtils.IsBasicallyUsd(o.ReceivedCoinType))
        {
          return $"Sold {o.PaymentCoinType}";
        }

        return $"Acquired {o.ReceivedCoinType}, Paid {o.PaymentCoinType}";
      }
      else return o.TransactionType.ToString();
    }

    public static string FormatCoinCountChange(this ITransactionish o, string contextualCoinType = null)
    {
      if (o.PaymentCoinType == contextualCoinType)
      {
        return $"-{o.PaymentCoinCount.FormatCoinCount(o.PaymentCoinType)}";
      }
      else if (o.ReceivedCoinType == contextualCoinType)
      {
        return o.ReceivedCoinCount.FormatCoinCount(o.ReceivedCoinType);
      }
      else
      {
        return "+" + o.ReceivedCoinCount.FormatCoinCount(o.ReceivedCoinType) +
          " / -" + o.PaymentCoinCount.FormatCoinCount(o.PaymentCoinType);
      }
    }

    public static string FormatExchangeRateUsd(this ITransactionish o, string contextualCoinType = null)
    {
      if (o.TransactionType == TransactionType.Adjustment)
      {
        return string.Empty;
      }
      
      if (o.TransactionType == TransactionType.FuturesPnl)
      {
        if (o.PaymentCoinCount > 0)
        {
          return o.PaymentPerCoinCostUsd.FormatPricePerCoinUsd() + " per " + o.PaymentCoinType;
        }
        else
        {
          return o.ReceivedPerCoinCostUsd.FormatPricePerCoinUsd() + " per " + o.ReceivedCoinType;
        }
      }

      if (o.PaymentCoinType == contextualCoinType)
      {
        if (o.PaymentPerCoinCostUsd != null)
        {
          return o.PaymentPerCoinCostUsd.FormatPricePerCoinUsd() + " per " + o.PaymentCoinType;
        }
        else if (o.ReceivedPerCoinCostUsd != null)
        {
          // infer the payment exchange rate via the received value exchange rate
          var totalValueUsd = o.ReceivedPerCoinCostUsd.Value * o.ReceivedCoinCount;
          var paymentPerCoinCostUsd = MathOrNull(() => totalValueUsd / o.PaymentCoinCount);
          return paymentPerCoinCostUsd.FormatPricePerCoinUsd() + " per " + o.PaymentCoinType;
        }
        else
        {
          return o.PaymentPerCoinCostUsd.FormatPricePerCoinUsd() + " per " + o.PaymentCoinType;
        }
      }
      else if (o.ReceivedCoinType == contextualCoinType)
      {
        if (o.ReceivedPerCoinCostUsd != null)
        {
          return o.ReceivedPerCoinCostUsd.FormatPricePerCoinUsd() + " per " + o.ReceivedCoinType;
        }
        else if (o.PaymentPerCoinCostUsd != null)
        {
          // infer the received value exchange rate via the payment exchange rate
          var totalValueUsd = o.PaymentPerCoinCostUsd.Value * o.PaymentCoinCount;
          var receivedPerCoinCostUsd = MathOrNull(() => totalValueUsd / o.ReceivedCoinCount);
          return receivedPerCoinCostUsd.FormatPricePerCoinUsd() + " per " + o.ReceivedCoinType;
        }
        else
        {
          return o.ReceivedPerCoinCostUsd.FormatPricePerCoinUsd() + " per " + o.ReceivedCoinType;
        }
      }
      else
      {
        // don't print USDT value if it's basically $1.00
        if (SomeUtils.IsBasicallyUsd(o.PaymentCoinType) && !SomeUtils.IsBasicallyUsd(o.ReceivedCoinType) &&
            o.PaymentPerCoinCostUsd > 0.99m && o.PaymentPerCoinCostUsd < 1.01m)
        {
          // tee hee! spooky recursion, assumes ReceivedCoinType and PaymentCoinType are never null!
          return o.FormatExchangeRateUsd(o.ReceivedCoinType);
        }
        else if (!SomeUtils.IsBasicallyUsd(o.PaymentCoinType) && SomeUtils.IsBasicallyUsd(o.ReceivedCoinType) &&
          o.ReceivedPerCoinCostUsd > 0.99m && o.ReceivedPerCoinCostUsd < 1.01m)
        {
          // tee hee! spooky recursion, assumes ReceivedCoinType and PaymentCoinType are never null!
          return o.FormatExchangeRateUsd(o.PaymentCoinType);
        }
        else
        {
          // tee hee! spooky recursion, assumes ReceivedCoinType and PaymentCoinType are never null!
          return o.FormatExchangeRateUsd(o.ReceivedCoinType) + " / " + o.FormatExchangeRateUsd(o.PaymentCoinType);
        }
      }
    }
    
    public static Decimal? GetInferredPaymentPerCoinCostUsd(this ITransactionish o)
    {
      if (o.ReceivedPerCoinCostUsd != null)
      {
        // infer the payment exchange rate via the received value exchange rate
        var totalValueUsd = o.ReceivedPerCoinCostUsd.Value * o.ReceivedCoinCount;
        var paymentPerCoinCostUsd = MathOrNull(() => totalValueUsd / o.PaymentCoinCount);
        return paymentPerCoinCostUsd;
      }
      return null;
    }
    
    public static Decimal? GetInferredReceivedPerCoinCostUsd(this ITransactionish o)
    {
      if (o.PaymentPerCoinCostUsd != null)
      {
        // infer the received value exchange rate via the payment exchange rate
        var totalValueUsd = o.PaymentPerCoinCostUsd.Value * o.PaymentCoinCount;
        var receivedPerCoinCostUsd = MathOrNull(() => totalValueUsd / o.ReceivedCoinCount);
        return receivedPerCoinCostUsd;
      }
      return null;
    }
    
    private static Decimal? MathOrNull(Func<Decimal?> math)
    {
      try
      {
        return math();
      }
      catch
      {
        return (Decimal?)null;
      }
    }
  }
}
