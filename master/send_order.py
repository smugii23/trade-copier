import requests
import datetime
import uuid
import json
import os

# load ip addresses from config.json
config_path = os.path.join(os.path.dirname(__file__), "config.json")
with open(config_path, "r") as f:
    config = json.load(f)

DESKTOP_IP = config["desktop_ip"]

FOLLOWERS = [
    f"http://{DESKTOP_IP}:8080",
]

def broadcast_order(symbol, side, qty, order_type="MARKET", limit_price=None):
    """Send a trade order to all follower agents."""
    payload = {
        "symbol": symbol,
        "side": side,
        "qty": qty,
        "order_type": order_type,
        "limit_price": limit_price,
        "time_in_force": "DAY",
        "source": "MASTER_LAPTOP",
        "leader_order_id": str(uuid.uuid4()),
        "timestamp": datetime.datetime.utcnow().isoformat() + "Z",
        "meta": {
            "note": "test broadcast",
        },
    }

    # loop through each address in the followers list, and send a POST request
    for base_url in FOLLOWERS:
        url = f"{base_url}/order"
        try:
            resp = requests.post(url, json=payload, timeout=1.0) # timeout quickly if unreachable
            print("Sent to", url, ".", resp.status_code, resp.text)
        except Exception as e:
            print("Error sending to", url, ":", e)


if __name__ == "__main__":
    broadcast_order("MESM5", "BUY", 2)