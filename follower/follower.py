from flask import Flask, request, jsonify

# plan is to use flask to create a web server to receive trade info and place the trade

app = Flask(__name__)

# when the leader sends a POST req
@app.route("/order", methods=["POST"])
def order():
    """Receive a trade from the master and log it for debug"""
    data = request.get_json(force=True)
    print("\n[FOLLOWER] Received order:")
    print(f"  symbol      = {data.get('symbol')}")
    print(f"  side        = {data.get('side')}")
    print(f"  qty         = {data.get('qty')}")
    print(f"  order_type  = {data.get('order_type')}")
    print(f"  limit_price = {data.get('limit_price')}")
    print(f"  source      = {data.get('source')}")
    print(f"  leader_id   = {data.get('leader_order_id')}")
    print()

    return jsonify({"status": "ok"}), 200


@app.route("/health", methods=["GET"])
def health():
    """Health check"""
    return "ok", 200


if __name__ == "__main__":
    # host="0.0.0.0" takes requests from devices on the same network (127.0.0.1 is for same device)
    app.run(host="0.0.0.0", port=8080)