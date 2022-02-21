using System;

namespace CryptoProfiteer
{
  public struct Candle
  {
    public Decimal Open;
    public Decimal Close;
    public Decimal High;
    public Decimal Low;
    
    public Candle(Decimal[] persistedData)
    {
      Open = persistedData[OpenIndex];
      Close = persistedData[CloseIndex];
      High = persistedData[HighIndex];
      Low = persistedData[LowIndex];
    }

    public Decimal[] ToPersistedData() => new [] { Open, Close, High, Low };
    
    public const int OpenIndex = 0;
    public const int CloseIndex = 1;
    public const int HighIndex = 2;
    public const int LowIndex = 3;
    
    public bool IsBearish => Close < Open;
    public bool IsBullish => Close > Open;
  }
}