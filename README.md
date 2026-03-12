NinjaTrader 8 Trade Copier
A NinjaTrader 8 AddOn that mirrors orders from a master account to multiple follower accounts, both within the same NT8 instance and across machines over a local network.
I made layer 2 because I have two Rithmic accounts that can't run on the same instance. This lets me send orders to another NT instance to copy trade two Rithmic connections.

How it works
There are two layers:
Layer 1 — Intra-instance copying
The laptop AddOn hooks into a master account's OrderUpdate events. When a new order hits, it gets copied to each configured follower account with its own contract size. Modifies and cancels are tracked and mirrored too, so if you move a limit or cancel an order, the followers stay in sync.
Layer 2 — Cross-machine copying (LAN)
The same event that triggers Layer 1 also fires an HTTP POST to a second machine on the local network. The desktop runs its own NT8 instance with the receiver AddOn, which has a lightweight HTTP listener, receives the order, and places it into the configured accounts on that machine.

Contract sizing for follower accounts isn't like other copiers, where it's a multiplier (which I've found to be a bit annoying). Each follower account has its own contract size independent of the master. So if the master trades 1 MES, a follower can be configured to trade 2, 5, whatever you want.

Setup
Prerequisites

NinjaTrader 8 on both machines
Both machines on the same local network

1. Config file
Copy master/config.example.json to master/config.json and fill in your IP address.
2. Install the AddOns
Drop the .cs files into:
Documents/NinjaTrader 8/bin/Custom/AddOns/
3. Configure account names and contract sizes
At the top of ninjatrader_copier.cs, update these to match your actual account names:
csharpprivate string masterAccountName = "YOUR_MASTER_ACCOUNT";

private readonly Dictionary<string, int> slaveAccounts = new Dictionary<string, int>
{
    { "FOLLOWER_ACCOUNT_1", 2 },
    { "FOLLOWER_ACCOUNT_2", 1 }
};
Do the same in desktop_receiver.cs for the desktop follower accounts.
4. Desktop HTTP listener setup
The receiver AddOn uses HttpListener which needs either NT8 running as Administrator, or a one-time URL reservation. Run this in an admin command prompt on the desktop:
netsh http add urlacl url=http://*:8080/ user=YOUR_WINDOWS_USERNAME
5. Activate
On the laptop: Control Center -> New -> Trade Copier
On the desktop: Control Center -> New -> Trade Copier — Receiver



Roadmap

 Intra-instance order copying (Layer 1)
 Cross-instance order copying over LAN (Layer 2)
 Modify and cancel mirroring
 Config file support in C# AddOns (in progress)
 Auto trade journal — fill events pipe directly into a journal entry with symbol, side, qty, fill price, and timestamp
 Potentially an interface in NT
