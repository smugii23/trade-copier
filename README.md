
This is a local LAN-based trade copier built for learning purposes.

## Architecture (v1)
- Laptop = Master (sends trade events)
- Desktop = Follower (receives events, executes trades)

- Python (may use Go later)
- HTTP (local network)
- Quantower (execution platform)

## Structure
- master/   → sends trade messages
- follower/ → receives and acts on trades

## Status
- v1: send fake trades over LAN
