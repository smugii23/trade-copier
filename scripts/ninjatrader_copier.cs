#region Using declarations
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
#endregion

// This namespace holds AddOns in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.AddOns
{
    public class TradeCopierAddOn : NinjaTrader.NinjaScript.AddOnBase
    {
        // Set your master account here

        private string masterAccountName = "Sim101";

        // Layer 1: slave accounts within the NT instance - (name, contracts)
        private readonly System.Collections.Generic.Dictionary<string, int> slaveAccounts =
            new System.Collections.Generic.Dictionary<string, int>
            {
                { "Sim102", 2 }
            };

        // Layer 2: desktop receiver URL (set to your desktop's LAN IP)
        private string desktopReceiverUrl = "http://YOUR_DESKTOP_IP:8080/order";

        // false = logs only, true = actual orders on the slave accounts
        private bool liveSubmitEnabled = true;

        private Account master;

        // maps the master order ids to slave orders that were created. used to look up slave orders for modifies and cancels
        private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Order>> orderMap =
            new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Order>>();

		// httpclient to send orders to the desktop (layer 2). fails after 2 secs for now
        private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        private NinjaTrader.Gui.Tools.NTMenuItem myMenuItem;
        private NinjaTrader.Gui.Tools.NTMenuItem controlCenterNewMenu;





        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Trade Copier — Laptop Master";
                Name        = "TradeCopierAddOn";
            }
            else if (State == State.Terminated)
            {
                UnhookMaster();
            }
        }

        // Control Center stuff (opening and closing the addOn)

        protected override void OnWindowCreated(System.Windows.Window window)
        {
            var cc = window as NinjaTrader.Gui.ControlCenter;
            if (cc == null) return;

            controlCenterNewMenu = cc.FindFirst("ControlCenterMenuItemNew")
                as NinjaTrader.Gui.Tools.NTMenuItem;
            if (controlCenterNewMenu == null) return;

            myMenuItem = new NinjaTrader.Gui.Tools.NTMenuItem
            {
                Header = "Trade Copier",
                Style  = System.Windows.Application.Current.TryFindResource("MainMenuItem") as System.Windows.Style
            };
            controlCenterNewMenu.Items.Add(myMenuItem);
            myMenuItem.Click += OnMenuItemClick;

            Print("[TradeCopier] Menu item added to Control Center -> New");
        }

        protected override void OnWindowDestroyed(System.Windows.Window window)
        {
            var cc = window as NinjaTrader.Gui.ControlCenter;
            if (cc == null) return;

            if (myMenuItem != null)
            {
                myMenuItem.Click -= OnMenuItemClick;
                if (controlCenterNewMenu != null)
                    controlCenterNewMenu.Items.Remove(myMenuItem);
                myMenuItem = null;
                controlCenterNewMenu = null;
            }
        }

        private void OnMenuItemClick(object sender, System.Windows.RoutedEventArgs e)
        {
            Print("[TradeCopier] Activating...");
            PrintAccounts();
            HookMaster();
        }

        // helper functions that get accounts active

        private Account[] GetConnectedAccounts()
        {
            lock (Account.All)
            {
                return Account.All
                    .Where(a => a != null
                             && a.Connection != null
                             && a.Connection.Status == ConnectionStatus.Connected)
                    .ToArray();
            }
        }

        private Account GetConnectedAccountByName(string name)
        {
            return GetConnectedAccounts().FirstOrDefault(a => a.Name == name);
        }

        private void PrintAccounts()
        {
            var connected = GetConnectedAccounts();
            Print("[TradeCopier] Connected accounts: " + string.Join(", ", connected.Select(a => a.Name)));
        }

        // hook/unhook (veriying master)

        private void HookMaster()
        {
            UnhookMaster();

            master = GetConnectedAccounts().FirstOrDefault(a => a.Name == masterAccountName);
            if (master == null)
            {
                Print($"[TradeCopier] Master '{masterAccountName}' not found or not connected.");
                return;
            }

            orderMap.Clear();
            master.OrderUpdate += OnMasterOrderUpdate;
            Print($"[TradeCopier] Hooked master: {master.Name}");
        }

        private void UnhookMaster()
        {
            if (master == null) return;
            master.OrderUpdate -= OnMasterOrderUpdate;
            Print($"[TradeCopier] Unhooked master: {master.Name}");
            master = null;
        }

        // order events

        private void OnMasterOrderUpdate(object sender, OrderEventArgs e)
        {
            var o = e?.Order;
            if (o == null) return;

            // in case empty order id
            var id = o.OrderId;
            if (string.IsNullOrEmpty(id)) return;

            // debug code for stops
            if (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)
            {
                Print($"[DEBUG STOP] Id={id} State={o.OrderState} " +
                      $"Action={o.OrderAction} Qty={o.Quantity} " +
                      $"Lmt={o.LimitPrice} Stp={o.StopPrice} OCO={o.Oco} Name={o.Name}");
            }

            // cencelled orders
            if (o.OrderState == OrderState.Cancelled)
            {
                HandleCancel(id, o);
                return;
            }

            // determines if the order should be copied or modified
            bool isActionableState;
            if (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)
                isActionableState = (o.OrderState == OrderState.Accepted);
            else if (o.OrderType == OrderType.Limit)
                isActionableState = (o.OrderState == OrderState.Working);
            else // Market
                isActionableState = (o.OrderState == OrderState.Accepted ||
                                     o.OrderState == OrderState.Working);

            if (!isActionableState) return;

            // checks if the id is in the ordermap for a new order vs modify
            if (!orderMap.ContainsKey(id))
                HandleNewOrder(id, o);
            else
                HandleModify(id, o);
        }

        // new order handling

        private void HandleNewOrder(string id, Order o)
        {
            var copiedOrders = new System.Collections.Generic.List<Order>();

            foreach (var kvp in slaveAccounts)
            {
                string slaveName = kvp.Key;
                int    contracts = kvp.Value;

                var slave = GetConnectedAccountByName(slaveName);
                if (slave == null)
                {
                    Print($"[TradeCopier] Slave '{slaveName}' not found or not connected.");
                    continue;
                }

                Print($"[NEW] {id} -> {slave.Name} | " +
                      $"{o.OrderAction} {contracts} {o.Instrument?.FullName} " +
                      $"Type={o.OrderType} Lmt={o.LimitPrice} Stp={o.StopPrice}");

                if (!liveSubmitEnabled) continue;

                try
                {
                    double limitPrice = (o.OrderType == OrderType.Limit      ||
                                         o.OrderType == OrderType.StopLimit)
                                        ? o.LimitPrice : 0;
                    double stopPrice  = (o.OrderType == OrderType.StopMarket ||
                                         o.OrderType == OrderType.StopLimit)
                                        ? o.StopPrice : 0;

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
                    copiedOrders.Add(copy);

                    Print($"[LIVE SUBMIT] {slave.Name}: {o.OrderAction} {contracts} " +
                          $"{o.Instrument?.FullName} {o.OrderType}");
                }
                catch (Exception ex)
                {
                    Print($"[TradeCopier] Submit failed for '{slaveName}': {ex.Message}");
                }
            }

            orderMap[id] = copiedOrders;

            BroadcastToDesktop("PLACE", o);
        }

        // modify order handling

        private void HandleModify(string id, Order o)
        {
            var slaveOrders = orderMap[id];

            double newLimitPrice = (o.OrderType == OrderType.Limit      ||
                                    o.OrderType == OrderType.StopLimit)
                                   ? o.LimitPrice : 0;
            double newStopPrice  = (o.OrderType == OrderType.StopMarket ||
                                    o.OrderType == OrderType.StopLimit)
                                   ? o.StopPrice : 0;

            Print($"[MODIFY] {id} | new Lmt={newLimitPrice} Stp={newStopPrice}");

            if (!liveSubmitEnabled)
            {
                Print($"[MODIFY DRY RUN] Would change {slaveOrders.Count} slave order(s).");
                BroadcastToDesktop("MODIFY", o);
                return;
            }

            foreach (var slaveOrder in slaveOrders)
            {
                try
                {
                    // set the changed properties on the order, then call Account.Change(Order[])
                    slaveOrder.LimitPriceChanged = newLimitPrice;
                    slaveOrder.StopPriceChanged  = newStopPrice;
                    slaveOrder.QuantityChanged   = slaveOrder.Quantity; // keep original slave qty
                    slaveOrder.Account.Change(new[] { slaveOrder });

                    Print($"[LIVE MODIFY] {slaveOrder.Account.Name}: Lmt={newLimitPrice} Stp={newStopPrice}");
                }
                catch (Exception ex)
                {
                    Print($"[TradeCopier] Modify failed for order {slaveOrder.OrderId}: {ex.Message}");
                }
            }

            BroadcastToDesktop("MODIFY", o);
        }

        // cancel order handling

        private void HandleCancel(string id, Order o)
        {
            if (!orderMap.TryGetValue(id, out var slaveOrders))
            {
                // master order cancelled before placing
                Print($"[CANCEL] {id} — not in orderMap, nothing to cancel.");
                return;
            }

            Print($"[CANCEL] {id} — cancelling {slaveOrders.Count} slave order(s).");

            if (!liveSubmitEnabled)
            {
                Print($"[CANCEL DRY RUN] Would cancel {slaveOrders.Count} slave order(s).");
                orderMap.Remove(id);
                BroadcastToDesktop("CANCEL", o);
                return;
            }

            foreach (var slaveOrder in slaveOrders)
            {
                try
                {
                    if (slaveOrder.OrderState == OrderState.Working ||
                        slaveOrder.OrderState == OrderState.Accepted)
                    {
                        slaveOrder.Account.Cancel(new[] { slaveOrder });
                        Print($"[LIVE CANCEL] {slaveOrder.Account.Name}: cancelled {slaveOrder.OrderId}");
                    }
                    else
                    {
                        Print($"[CANCEL SKIP] {slaveOrder.OrderId} already in state {slaveOrder.OrderState}");
                    }
                }
                catch (Exception ex)
                {
                    Print($"[TradeCopier] Cancel failed for order {slaveOrder.OrderId}: {ex.Message}");
                }
            }

            orderMap.Remove(id);
            BroadcastToDesktop("CANCEL", o);
        }

        // layer 2 broadcasting, need to do more testing 

        private void BroadcastToDesktop(string eventType, Order o)
        {
            try
            {
                double limitPrice = (o.OrderType == OrderType.Limit      ||
                                     o.OrderType == OrderType.StopLimit)
                                    ? o.LimitPrice : 0;
                double stopPrice  = (o.OrderType == OrderType.StopMarket ||
                                     o.OrderType == OrderType.StopLimit)
                                    ? o.StopPrice : 0;

                string json = "{"
                    + $"\"eventType\":\"{eventType}\","
                    + $"\"masterOrderId\":\"{EscapeJson(o.OrderId)}\","
                    + $"\"symbol\":\"{EscapeJson(o.Instrument?.FullName)}\","
                    + $"\"orderAction\":\"{o.OrderAction}\","
                    + $"\"orderType\":\"{o.OrderType}\","
                    + $"\"timeInForce\":\"{o.TimeInForce}\","
                    + $"\"qty\":{o.Quantity},"
                    + $"\"limitPrice\":{limitPrice},"
                    + $"\"stopPrice\":{stopPrice},"
                    + $"\"source\":\"LAPTOP_MASTER\","
                    + $"\"timestamp\":\"{DateTime.UtcNow:O}\""
                    + "}";

                Print($"[L2 {eventType}] {(liveSubmitEnabled ? "Sending" : "DRY RUN")} -> {desktopReceiverUrl}");
                Print($"[L2 PAYLOAD] {json}");

                if (!liveSubmitEnabled) return;

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                httpClient.PostAsync(desktopReceiverUrl, content).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        Print($"[TradeCopier] L2 broadcast failed: {t.Exception?.GetBaseException().Message}");
                    else
                        Print($"[TradeCopier] L2 response: {(int)t.Result.StatusCode}");
                });
            }
            catch (Exception ex)
            {
                Print($"[TradeCopier] L2 broadcast error: {ex.Message}");
            }
        }

        private static string EscapeJson(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}