# Golden vectors — porting the engine to C# (Unity)

A port of `@dorduncu/engine` is correct when it reproduces **every** value in these files. They are generated from the
TypeScript engine (`npx tsx packages/engine/scripts/golden.ts`) and a test fails whenever they are out of date.

**Tiles:** `R`/`Y`/`B`/`K` (kırmızı, sarı, mavi, siyah) + value `1..13`, e.g. `K7`; `F` = fake okey.
Deck ids: `0..103` in order color (R, Y, B, K) → value 1..13 → copy 0..1; `104`, `105` = the two fake okeys.

| File | What to reproduce |
|---|---|
| `crypto.json` | `sha256(utf8(msg))` and `hmacSha256(utf8(key), utf8(msg))` as lowercase hex (msg contains Turkish letters: UTF-8!) |
| `rng.json` | `SeededRng(serverSeed, clientSeed)`: 12 × `nextUint32()` then a fresh instance's 12 × `nextInt(7)` |
| `deals.json` | `shuffle(createDeck(), serverSeed, clientSeed)` → `shuffledIds`; `deal(shuffled, 4, starter)` → indicator, okey, hands (ids), wall (ids) |
| `hands.json` | `isNormalWin(hand14, okey)`, `isPairsWin(hand14, okey)`, `findWinningDiscard(hand15, okey)` (index into hand15, type, okeyDiscard, penalty) |
| `lp.json` | `lpDelta(lost, win, games, quit)` |
| `tournaments.json` | `drawFirstRound(humans, capacity, seed)`, then per round the given rankings → `nextRound(...)` tables, and final `standings` |

C# notes: `System.Security.Cryptography.SHA256` / `HMACSHA256` give identical bytes; `SeededRng` works on unsigned 32-bit
integers (use `uint` and unchecked arithmetic); `nextInt` uses rejection sampling exactly as in `core.ts`.
