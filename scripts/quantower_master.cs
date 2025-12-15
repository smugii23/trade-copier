// the C# script to listen for the orders placed and cancelled on Quantower

using System;
using TradingPlatform.BusinessLayer;

// used a strategy class to listen to events
public class OrderSpy : Strategy
{
    private string masterAccountName = "PST-002553-G024";

    protected override void OnRun()
    {
        // whenever an order is added, run the OnOrderAdded function
        Core.Instance.OrderAdded += OnOrderAdded;
        Core.Instance.OrderRemoved += OnOrderRemoved;

        Core.Loggers.Log("OrderSpy running.", LoggingLevel.Info);
    }

    protected override void OnStop()
    {
        Core.Instance.OrderAdded -= OnOrderAdded;
        Core.Instance.OrderRemoved -= OnOrderRemoved;
    }

    private void OnOrderAdded(Order o)
    {
        // only check for orders coming from the master account
        if (o?.Account == null || o.Account.Name != masterAccountName)
            return;

        Core.Loggers.Log(
            $"ORDER_ADDED id={o.Id} symbol={o.Symbol?.Name} side={o.Side} qty={o.Quantity} type={o.Type} price={o.Price} status={o.Status}",
            LoggingLevel.Info
        );
    }

    private void OnOrderRemoved(Order o)
    {
        if (o?.Account == null || o.Account.Name != masterAccountName)
            return;

        Core.Loggers.Log(
            $"ORDER_REMOVED id={o.Id} symbol={o.Symbol?.Name} status={o.Status}",
            LoggingLevel.Info
        );
    }
}
