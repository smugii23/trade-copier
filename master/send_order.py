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

ORDER_MAP = {}

# changed the structure so that orders can be modified or cancelled
def broadcast_order(event_type, master_order_id, symbol=None, side=None, qty=None, order_type="MARKET", limit_price=None, time_in_force="DAY"):
    """Send a trade order to all follower agents."""
    payload = {
        "event_type": event_type,                  # for now, can be place, modify, or cancel
        "master_order_id": str(master_order_id),
        "symbol": symbol,
        "side": side,
        "qty": qty,
        "order_type": order_type,
        "limit_price": limit_price,
        "time_in_force": time_in_force,
        "source": "MASTER_LAPTOP",
        "timestamp": datetime.datetime.now(datetime.UTC).isoformat(),
        "meta": {
            "note": "test broadcast",
        },
    }

    # loop through each address in the followers list, and send a POST request
    for base_url in FOLLOWERS:
        url = f"{base_url}/order"
        try:
            resp = requests.post(url, json=payload, timeout=1.0)
            print("Sent to", url, ".", resp.status_code, resp.text)

            # only relevant once follower returns a follower_order_id
            if event_type == "PLACE" and resp.ok:
                try:
                    data = resp.json()
                    follower_order_id = data.get("follower_order_id")
                    if follower_order_id:
                        ORDER_MAP[(str(master_order_id), base_url)] = str(follower_order_id)
                except Exception:
                    pass 

        except Exception as e:
            print("Error sending to", url, ":", e)


if __name__ == "__main__":
    # test payload shape using a stable id for this "order"
    master_order_id = str(uuid.uuid4())

    # testing limits, modifications, and cancels
    broadcast_order(
        event_type="PLACE",
        master_order_id=master_order_id,
        symbol="MESM5",
        side="BUY",
        qty=2,
        order_type="LIMIT",
        limit_price=5000.25,
        time_in_force="DAY"
    )

    broadcast_order(
        event_type="MODIFY",
        master_order_id=master_order_id,
        qty=3,
        order_type="LIMIT",
        limit_price=5000.00
    )

    broadcast_order(
        event_type="CANCEL",
        master_order_id=master_order_id
    )