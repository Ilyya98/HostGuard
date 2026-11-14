# HostGuard

Small, dependency-free C# building blocks for a **game host that accepts messages from untrusted clients**: bounded
binary reading, a sealed whitelist of message types, per-sender rate limits with strikes, safe text for names and chat,
safe file paths from data, and a join handshake.

It targets **.NET Standard 2.1**, so it works in Unity, Godot (C#) and plain .NET. No reflection, no serializer that
creates types from data, and no allocations on the read path beyond strings you ask for.

> Status: **0.1, early.** The API may change. It came out of the multiplayer security design for a turn-based
> roguelike, and is published separately so other small game projects can use and review it.

## Why

In listen-server and co-op games, one player's machine is the host, and every other player's client can send it
anything. The usual failures are boring and serious:

- a length prefix of 2 GB, and the host allocates it;
- a type name in the data, and the serializer creates it (`BinaryFormatter`, `TypeNameHandling`);
- NaN positions, out-of-range enum values, negative counts;
- names with markup (`<size=500>`) or right-to-left overrides that make chat lie;
- a "save name" of `../../something`;
- a client that sends 10,000 messages a second.

HostGuard handles these at the message boundary, so game code receives only values that passed the checks.

## Pieces

| Type | What it does |
| --- | --- |
| `BoundedReader` / `BoundedWriter` | Little-endian binary format with caps on message size, string bytes, counts and nesting depth. Strict UTF-8, finite floats only, shortest-form var-ints, 0/1 booleans, range-checked reads, trailing-byte check. The writer enforces the same rules, so a correct sender never produces a refused message. |
| `MessageRegistry<TContext>` | Whitelist of numeric message ids → decoder + optional `allow` rule + `apply`. Sealed at startup. Frame = `[u16 type][var-uint length][payload]`, exact length required, per-type payload cap. Decodes fully, then checks the rule, then applies, so a refused message never half-applies. |
| `RateLimiter` | Token bucket per sender, integer maths, caller-supplied clock (deterministic in tests and replays). |
| `HostGate<TContext>` | Rate limit + registry + strikes. Returns `Accepted`, `Rejected` (with a reason) or `Disconnect`. Only refusals are caught; bugs in your `apply` code still throw. |
| `SafeText` | Strip control and direction-override characters, escape rich-text tags, validate player names. |
| `SafePath` | Turn a name from data into a path that is guaranteed to stay inside a root folder. |
| `Handshake`, `JoinRequest`, `ContentHash` | Protocol version, build id, content hash and name checks when a client joins. |

## Example

```csharp
using HostGuard;

const ushort Move = 1;

var registry = new MessageRegistry<Session>();
registry.Register(Move, "Move", maxPayloadBytes: 12,
    decode: (ref BoundedReader r) => new MoveCommand(
        actor: r.ReadInt32(0, 1_000_000),
        dx: r.ReadInt32(-1, 1),
        dy: r.ReadInt32(-1, 1)),
    apply: (cmd, session) => session.Queue(cmd),
    allow: (cmd, session) => session.CurrentSenderOwns(cmd.Actor));
registry.Seal();

var gate = new HostGate<Session>(registry, new RateLimiter(burst: 20, perSecond: 10), strikeLimit: 5);

// For every frame from the transport:
GateResult result = gate.Receive(peerId, frameBytes, nowMs, session);
if (result.Verdict == GateVerdict.Disconnect) transport.Kick(peerId);
```

Sending:

```csharp
var payload = new BoundedWriter();
payload.WriteInt32(actorId);
payload.WriteInt32(1);
payload.WriteInt32(0);

var frame = new BoundedWriter();
MessageRegistry<Session>.WriteFrame(frame, Move, payload);
transport.Send(frame.Written);
```

## Threat model

**In scope:** a connected client (modified or not) sending arbitrary bytes to the host. HostGuard aims to ensure that
such bytes cannot crash the host, make it allocate without bound, create arbitrary types, inject markup into other
players' UIs, write files outside a chosen folder, or flood it with messages without being disconnected.

**Not in scope (non-goals):**

- **Authentication and encryption.** Use your transport (DTLS, Steam networking, a relay) for that.
- **Cheating within the rules.** HostGuard checks that messages are well-formed and lets you plug in game rules
  (`allow`); deciding what is legal in your game is your code. Keep the host authoritative.
- **Proving client integrity.** `ContentHash` is FNV-1a. It catches accidental mismatches (different mods or versions),
  not a client that lies.
- **Transport-level denial of service.** The rate limiter counts messages that reached your code; connection floods
  must be stopped earlier.
- **Unicode look-alikes in names.** `IsValidName` allows letters from any script. Add your own rules if
  impersonation matters for your game.

## Building and testing

```bash
dotnet test -c Release
```

The tests include a seeded fuzz test that sends 60,000 random and mutated frames through the gate.

## Contributing

Issues and pull requests are welcome, especially new test cases for inputs that should be refused. See
[SECURITY.md](SECURITY.md) for reporting vulnerabilities privately.

## License

[MIT](LICENSE)
