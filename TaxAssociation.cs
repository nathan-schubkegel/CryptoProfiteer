using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CryptoProfiteer
{
  // NOTE: this type is JSON serialized/deserialized
  public class PersistedTaxAssociation
  {
    public string Id { get; set; }
    public List<PersistedTaxAssociationPart> Parts { get; set; }
  }

  // NOTE: this type is JSON serialized/deserialized
  public class PersistedTaxAssociationPart
  {
    public string OrderId { get; set; }
    public Decimal CoinCount { get; set; }
    public Decimal PartCost { get; set; }
  }

  public class TaxAssociation
  {
    private readonly PersistedTaxAssociation _data;

    public TaxAssociation(PersistedTaxAssociation data, IReadOnlyDictionary<string, Order> orders,
      /*IEnumerable<string> doubleBookedOrders*/)
    {
      _data = data;
      
      var parts = new List<TaxAssociationPart>();
      data.Parts ??= new List<PersistedTaxAssociationPart>();
      foreach (var part in data.Parts)
      {
        orders.TryGetValue(part.OrderId, out var order);
        parts.Add(new TaxAssociationPart(part, order));
      }
      Parts = parts;
      
      TotalCostBought = Parts.Where(p => p.Order?.TransactionType == TransactionType.Buy).Sum(p => p.Order.TotalCost);
      TotalCostSold = Parts.Where(p => p.Order?.TransactionType == TransactionType.Sell).Sum(p => p.Order.TotalCost);
      var difference = TotalCostBought + TotalCostSold;
      if (difference > 0)
      {
        MoreSalesNeeded = difference;
      }
      else if (difference < 0)
      {
        MoreBuysNeeded = -difference;
      }
      // else, they both stay zero
    }

    public string Id => _data.Id;
    public string CoinType => Parts.FirstOrDefault(p => p.Order != null)?.Order.CoinType ?? "<unknown>";
    public string FriendlyName => Parts.FirstOrDefault(p => p.Order != null)?.Order.FriendlyName ?? "<unknown>";
    public IReadOnlyList<TaxAssociationPart> Parts { get; }
    public Decimal TotalCostBought { get; }
    public Decimal TotalCostSold { get; }
    public Decimal MoreBuysNeeded { get; }
    public Decimal MoreSalesNeeded { get; }

    public PersistedTaxAssociation GetPersistedData() => _data;
  }
  
  public class TaxAssociationPart
  {
    private readonly PersistedTaxAssociationPart _data;
    
    public TaxAssociationPart(PersistedTaxAssociationPart data, Order order)
    {
      _data = data;
      Order = order;
    }
    
    // NOTE: null if transaction data was deleted w/out maintaining tax association stuff
    public Order Order { get; }
    public Decimal CoinCount => _data.CoinCount;
    public Decimal PartCost => _data.PartCost;
  }
}
