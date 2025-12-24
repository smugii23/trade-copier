#region Using declarations
using System;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
#endregion

//This namespace holds Add ons in this folder and is required. Do not change it. 

namespace NinjaTrader.NinjaScript.AddOns
{
	public class TradeCopierAddOn : NinjaTrader.NinjaScript.AddOnBase
	{
		private readonly System.Collections.Generic.HashSet<string> seenWorkingOrders = new System.Collections.Generic.HashSet<string>(); // a set to make sure working orders only once 
		private Account master;
		private string masterAccountName = "Sim101";
		private readonly System.Collections.Generic.Dictionary<string, int> slaveAccounts = new System.Collections.Generic.Dictionary<string, int> // a dictionary of slave accounts with the number of contracts
		{
		    { "Sim102", 2 }
		};
		// used to add/remove control center items
		private NinjaTrader.Gui.Tools.NTMenuItem myMenuItem;
		private NinjaTrader.Gui.Tools.NTMenuItem controlCenterNewMenu;


		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description	= @"Trade Copier (v0)";
				Name		= "TradeCopierAddOn";
			}
			else if (State == State.Terminated)
			{
			    UnhookMaster();
			}

		}

		protected override void OnWindowCreated(System.Windows.Window window)
		{
			// because the control center doesn't exist at startup, wait for it
			var cc = window as NinjaTrader.Gui.ControlCenter;
			if (cc == null)
				return;

			// find the "New" tab in control center
			controlCenterNewMenu = cc.FindFirst("ControlCenterMenuItemNew") 
    			as NinjaTrader.Gui.Tools.NTMenuItem;

			if (controlCenterNewMenu == null)
				return;

			// create a menu item
			myMenuItem = new NinjaTrader.Gui.Tools.NTMenuItem
			{
				Header = "Trade Copier (v0)",
				Style  = System.Windows.Application.Current.TryFindResource("MainMenuItem") as System.Windows.Style
			};
			// insert the menu item into the "New" tab
			controlCenterNewMenu.Items.Add(myMenuItem);
			myMenuItem.Click += OnMenuItemClick;

			Print("[TradeCopier] Menu item added to Control Center -> New");
		}
		
		// remove the menu item if the control center is destroyed or refreshed
		protected override void OnWindowDestroyed(System.Windows.Window window)
		{
			var cc = window as NinjaTrader.Gui.ControlCenter;
			if (cc == null)
				return;

			if (myMenuItem != null)
			{
				myMenuItem.Click -= OnMenuItemClick;
				if (controlCenterNewMenu != null)
					controlCenterNewMenu.Items.Remove(myMenuItem);

				myMenuItem = null;
				controlCenterNewMenu = null;
			}
		}
		
		// when you click the item, it should: 1. log that you clicked it 2. print the connected accounts 3. hook the master order updates
		private void OnMenuItemClick(object sender, System.Windows.RoutedEventArgs e)
		{
		    Print("[TradeCopier] Clicked Trade Copier (v0)");
		    PrintAccounts();
		    HookMaster();
		}
		private Account[] GetConnectedAccounts()
		{
			// the problem with just return Account.All is that it brings all accounts, whether they are inactive/active or connected
		    lock (Account.All)
		    {
				// return only the accounts that are connected currently
		        return Account.All
		            .Where(a => a != null
		                        && a.Connection != null
		                        && a.Connection.Status == ConnectionStatus.Connected)
		            .ToArray();
		    }
		}


		private void PrintAccounts()
		{
		    var connected = GetConnectedAccounts();
		    Print("[TradeCopier] Connected accounts: " + string.Join(", ", connected.Select(a => a.Name)));
		}

		private void HookMaster()
		{
			// finds the master account in the connected accounts. if missing, logs and quits
		    if (master != null) return;
			
		    master = GetConnectedAccounts().FirstOrDefault(a => a.Name == masterAccountName);
		
		    if (master == null)
		    {
		        Print($"[TradeCopier] Master '{masterAccountName}' NOT found (or not connected).");
		        return;
		    }
		
			// clear the working order state because it's a new instance
		    seenWorkingOrders.Clear();
			
			// subscribe to the master account's order state
		    master.OrderUpdate -= OnMasterOrderUpdate;
		    master.OrderUpdate += OnMasterOrderUpdate;
		
		    Print($"[TradeCopier] Hooked master: {master.Name}");
		}


		// unsubscribe from OrderUpdate and clear master
		private void UnhookMaster()
		{
			if (master == null) return;

			master.OrderUpdate -= OnMasterOrderUpdate;
			Print($"[TradeCopier] Unhooked master: {master.Name}");
			master = null;
		}
		private Account GetConnectedAccountByName(string name)
		{
		    return GetConnectedAccounts()
		        .FirstOrDefault(a => a.Name == name);
		}


		private bool liveSubmitEnabled = true; // true to actually copy trades, false for just logs (testing/debugging)
		
		private void OnMasterOrderUpdate(object sender, OrderEventArgs e)
		{
			// grab the order
		    var o = e?.Order;
		    if (o == null) return;
		
		    // (debugging) had issues with stop losses, see what stops are doing
		    if (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)
		    {
		        Print($"[DEBUG STOP] Id={o.OrderId} State={o.OrderState} " +
		              $"Action={o.OrderAction} Qty={o.Quantity} " +
		              $"Lmt={o.LimitPrice} Stp={o.StopPrice} OCO={o.Oco} Name={o.Name}");
		    }
		
		    // initialize to false in the beginning
		    bool shouldCopy = false;
		
		    if (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)
		    {
		        shouldCopy = (o.OrderState == OrderState.Accepted);
		    }
		    else if (o.OrderType == OrderType.Limit)
		    {
		        shouldCopy = (o.OrderState == OrderState.Working);
		    }
		    else
		    {
		        shouldCopy = (o.OrderState == OrderState.Working);
		    }
		
		    if (!shouldCopy)
		        return;
		
		    var id = o.OrderId;
		    if (!seenWorkingOrders.Add(id))
		        return;
		
		    foreach (var kvp in slaveAccounts)
		    {
		        string slaveName = kvp.Key;
		        int contracts = kvp.Value;
		
		        var slave = GetConnectedAccountByName(slaveName);
		        if (slave == null)
		        {
		            Print($"[TradeCopier] Slave '{slaveName}' not found or not connected.");
		            continue;
		        }
		
		        Print($"[COPY PLAN] MasterOrderId={id} -> Slave={slave.Name} " +
		              $"{o.OrderAction} {contracts} {o.Instrument?.FullName} " +
		              $"Type={o.OrderType} State={o.OrderState} Lmt={o.LimitPrice} Stp={o.StopPrice}");
		
		        if (!liveSubmitEnabled)
		            continue;
		
		        try
		        {
		            double limitPrice = (o.OrderType == OrderType.Limit || o.OrderType == OrderType.StopLimit) ? o.LimitPrice : 0;
		            double stopPrice  = (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit) ? o.StopPrice : 0;
		
		            var copy = slave.CreateOrder(
		                o.Instrument,
		                o.OrderAction,
		                o.OrderType,
		                o.TimeInForce,
		                contracts,
		                limitPrice,
		                stopPrice,
		                string.Empty,
		                "CopiedByTradeCopier",
		                null
		            );
		
		            slave.Submit(new[] { copy });
		
		            Print($"[LIVE SUBMIT] Sent to {slave.Name}: {o.OrderAction} {contracts} {o.Instrument?.FullName} {o.OrderType} ({o.OrderState})");
		        }
		        catch (Exception ex)
		        {
		            Print($"[TradeCopier] LIVE SUBMIT failed for {slaveName}: {ex}");
		        }
		    }
		}





	}
}

