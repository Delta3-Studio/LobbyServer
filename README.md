# UDP - Lobby Server

Basic example for a server lobby and NAT traversal
using [UDP hole punching](https://en.wikipedia.org/wiki/UDP_hole_punching)

## Running

### Running from source

You need to have [.NET 10](https://dotnet.microsoft.com/en-us/download) installed.

Open a terminal on the [server project directory](/src/LobbyServer) and execute:

```bash
dotnet run .
```

### Running using docker

You need to have [Docker](https://docs.docker.com/get-docker/) installed.

Open a terminal on the [server project directory](/src/LobbyServer) and execute:

```bash
docker compose up
```

### Ports

- Default **HTTP**: `9999`
- Default **UDP** : `8888`

> [!TIP]
> 💡 Check the `API` docs at http://localhost:9999/docs
