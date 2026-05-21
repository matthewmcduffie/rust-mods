# rust-mods

A collection of uMod (Oxide) plugins for Rust server administrators. Each plugin is a single `.cs` file — drop it into your `oxide/plugins/` folder and it loads immediately with sensible defaults.

---

## Plugins

### NPCControl

Control how much damage NPCs deal and cap how many can exist on the map at once.

**Features**
- Scale outgoing damage for bandits and wildlife independently
- Hard cap on bandit and wildlife population — excess spawns are removed on `NextTick`
- Adjust the `animal.population` ConVar without touching server files
- Population count rebuilt every 5 minutes to stay accurate

**Config** — `oxide/config/NPCControl.json`

| Key | Default | Description |
|-----|---------|-------------|
| Bandit outgoing damage multiplier | `1.0` | `0.5` = half damage, `2.0` = double |
| Wildlife outgoing damage multiplier | `1.0` | Same scale |
| Max bandit NPCs on map at one time | `0` | `0` = no cap |
| Max wildlife NPCs on map at one time | `0` | `0` = no cap |
| Wildlife population convar multiplier | `1.0` | Raises/lowers server spawn density |

**Commands** (admin only)

| Command | Example | Description |
|---------|---------|-------------|
| `/npccontrol` | — | Show current values and live counts |
| `/npccontrol banditdmg <float>` | `0.5` | Bandit damage multiplier |
| `/npccontrol wildlifedmg <float>` | `1.5` | Wildlife damage multiplier |
| `/npccontrol maxbandits <int>` | `30` | Cap total bandits (`0` = unlimited) |
| `/npccontrol maxwildlife <int>` | `50` | Cap total wildlife (`0` = unlimited) |
| `/npccontrol wildlifepop <float>` | `2.0` | Wildlife spawn density |

Console versions use `npccontrol` (no slash).

**NPC categories**

- **Bandits:** bandit guards, murderers, scientists, heavy scientists, tunnel/underwater dwellers, scarecrows
- **Wildlife:** bears, polar bears, boars, chickens, stags, wolves, horses, sharks

---

### AirdropControl

Fine-tune airdrop frequency, loot contents, and map markers — without touching server files.

**Features**
- Custom drop timer with a per-interval frequency
- Maximum simultaneous active drops on the map
- Orange map marker spawns when a drop lands, clears when looted out
- Server-wide chat announcements on landing and first loot
- Fully configurable loot table with per-item probability, min/max amounts, and skin support

**Config** — `oxide/config/AirdropControl.json`

| Key | Default | Description |
|-----|---------|-------------|
| Custom drop frequency in minutes | `0` | `0` = use server default timing |
| Max simultaneous active drops | `0` | `0` = no cap |
| Show map marker when drop lands | `true` | |
| Map marker fill color hex | `#FF6600` | Any HTML hex color |
| Map marker radius | `0.3` | `0.1` small → `0.5` large |
| Broadcast when drop lands | `true` | Chat announcement with grid ref |
| Broadcast on first loot | `true` | Shows the looting player's name |
| Override drop loot | `false` | Use the loot table below instead of game defaults |
| Items per drop | `6` | Max slots filled when override is on |
| Loot table | *(see file)* | List of items with shortname, probability, and amounts |

**Commands** (admin only)

| Command | Description |
|---------|-------------|
| `/airdropcontrol` | Show current config and active drop count |
| `airdropcontrol.trigger` | Immediately trigger a drop |
| `airdropcontrol.reload` | Reload config and restart frequency timer |

---

### StorageControl

Resize player inventories and storage container slot counts across the server.

**Features**
- Resize player main inventory and hotbar independently
- Configure slot counts for every vanilla storage container
- Applied on server init (existing containers + connected players) and on every new spawn/login
- Single console command reloads config and reapplies everywhere live

**Config** — `oxide/config/StorageControl.json`

| Container | Config Key | Game Default |
|-----------|-----------|-------------|
| Player main inventory | `PlayerMainSlots` | 24 |
| Player belt / hotbar | `PlayerBeltSlots` | 6 |
| Small wooden box | `SmallBox` | 12 |
| Large wooden box | `LargeBox` | 30 |
| Fridge | `Fridge` | 36 |
| Locker | `Locker` | 30 |
| Drop box | `DropBox` | 6 |
| Vending machine | `VendingMachine` | 9 |
| Tool cupboard | `ToolCupboard` | 12 |
| Furnace | `Furnace` | 6 |
| Large furnace | `LargeFurnace` | 18 |
| Campfire | `Campfire` | 12 |
| BBQ | `BBQ` | 12 |
| Oil refinery | `OilRefinery` | 5 |
| Mixing table | `MixingTable` | 6 |

> **Note:** Do not shrink furnace, campfire, BBQ, oil refinery, or mixing table below their game defaults — those slots have functional roles (fuel, input, output) and reducing them breaks the container.

**Commands** (admin only)

| Command | Description |
|---------|-------------|
| `/storagecontrol` | Show current slot values |
| `storagecontrol.reload` | Reload config and reapply to all containers and players |

---

## Installation

1. Copy the `.cs` file(s) into `oxide/plugins/` on your server.
2. Oxide compiles and loads the plugin automatically.
3. Edit the generated config in `oxide/config/<PluginName>.json`.
4. Use the in-game reload command or `oxide.reload <PluginName>` from the server console.

## Requirements

- [uMod (Oxide)](https://umod.org) for Rust
- No additional dependencies

## License

See [LICENSE](LICENSE).
