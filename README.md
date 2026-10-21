![Store & Craft - Your base runs with you](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/store-and-craft-banner.png)

<br>

**Loot finds its chest. One key clears your pockets. Craft, upgrade and build straight from storage.**

Stations refill themselves from chests and put their output back. Storage displays show your stock, a scarecrow farms for you, and one mod manager (**F10**) holds every setting and hotkey.

> **Required on the server and on every client (same version).** Valheim 1.0 · BepInExPack 5.4.2350+ · Console players cannot load the mod.

[Bug reports](https://github.com/morda0511/StoreAndCraft/issues/1) · [Discord](https://discord.gg/9bD8sSdvX) · [Forager's Garden](https://github.com/morda0511/ForagersGarden)

**Looking for the beehives, berry bushes, wild herbs and mushrooms?** They live in **Forager's Garden** now. Install it and everything you built with them comes back; until then nothing is lost, queens and spores wait in your chests.

![](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/divider-norse.png)

## At a glance

| ![Store](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/icon-store-160.png) | ![Craft](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/icon-craft-160.png) | ![Stations](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/icon-stations-160.png) | ![Displays](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/icon-displays-160.png) |
|:---:|:---:|:---:|:---:|
| **Store**<br>Ground items and your pockets find their chest | **Craft**<br>Recipes, upgrades and building use your chests | **Stations**<br>Fill themselves, store their own output | **Displays**<br>Live stock on boards, red below your minimum |

| ![Feed trough](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/icon-feed-160.png) | ![Scarecrow](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/icon-scarecrow-160.png) | ![Armor stand](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/icon-armor-160.png) | ![Settings](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/icon-settings-160.png) |
|:---:|:---:|:---:|:---:|
| **Feed trough**<br>Your tames walk up and eat | **Scarecrow**<br>Plants, harvests and replants the whole field | **Armor stand**<br>Swap your gear with one key | **Control**<br>One manager, every switch and hotkey |

**Works with:** Epic Loot · PlanBuild · PierSupports · Path of Valheiman · Forager's Garden

![](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/divider-norse.png)

## Hotkeys

Every key can be changed in the mod manager (**F10**).

| Key | What it does |
|:---|:---|
| **.** | Dump: your inventory into matching chests (hotbar and favorites stay) |
| **0** | Restock: bring the items marked *Fill* up to their amount from chests |
| **Middle mouse** | Store the hovered item (inventory open) |
| **Ctrl + middle mouse** | Fill the hovered stack from chests |
| **F** | Favorite an item: never dumped or stored |
| **Y** | Search: the nearest chest with the item blinks |
| **C + place** | Grab a piece's materials from chests, nothing is placed |
| **E / Shift + E** | Refill a station / fill it to the max |
| **B / N** | Station auto-fill / auto-store |
| **Alt + E** | Settings of the chest, station or display you look at |
| **Alt + R** | Scan range of a display |
| **F10** | Morda Mod Manager |

![](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/divider-norse.png)

## Store and craft

### Whole loaded base
- **On by default:** dump, crafting and building, stations, auto-store and the scarecrow use every chest in the area Valheim has loaded around you. No ranges to set
- Items still only go into chests that already hold them, links and chest settings still apply
- Each item type remembers the chest it went into last, the base is only searched again when that chest is full
- Switch it off in the Morda Mod Manager to keep the old ranges in meters (under Advanced)
- Valheim only loads the area around you: keep chests within about **80 m** of their station

### Store
- **Auto-store:** ground items go into matching chests (one player in range is enough)
- Chest takes an item only if the type is already inside (switch in the Morda Mod Manager)
- Carts and ships count as chests. Player-built chests only. Ward rules apply
- **Dump** (**.**): inventory into matching chests, hotbar and favorites stay
- **Middle mouse** (inventory open): store the hovered item
- **Ctrl + middle mouse**: fill the hovered stack from chests
- **Sort** and **Stack** buttons on inventory and chest panels
- **Favorites** (**F**): never dumped or stored
- **Leave on the ground** (button at the top of F10, only for you): pick items in a window with search, auto-store never picks them up from the ground (for example eggs)
- **Keep in inventory** (button at the top of F10, only for you): an amount per item stays in your inventory when you dump (50 wood, keep 10: 40 go into the chests)
- **Restock** (**0**, change it in F10): brings the items marked **Fill** in that list up to their amount from nearby chests. Chests set to Ignore or Only stations are skipped
- **Search** (**Y**): nearest chest with the item blinks, map ping
- A chest shows **Status: Full / Almost full** when you look at it (counts the stacks, not only the slots)
- Fish are never picked up from the ground: they stay in the sea or your fish pond

### Craft and build from chests
- Recipes, upgrades and hammer pieces use chest contents
- Missing materials are taken from chests directly
- Yellow requirement text = part comes from a chest
- 1 item stays in each chest (switch in the Morda Mod Manager)
- **C + place**: grab a piece's materials from chests, nothing is placed
- Epic Loot: craft, enchanting table, Forge of Potential use chest stock
- PierSupports: the extra pier segments are paid from chests too
- PlanBuild: plan pieces take their materials from chests too

![](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/divider-norse.png)

## Automation

### Stations
- **[E]** refill: fuel, ore, food from chests (inventory first)
- **B** auto-fill: refills when empty, never from your bag
- **N** auto-store: finished bars, food, honey, mead go into chests
- Works on kiln, smelter, blast furnace, cooking stations, oven, fermenter, turrets, fires, torches; **N** also on beehive and sap extractor
- **Alt+E** settings: allowed items, link **l1-l9**
- **Links:** station fills from, and outputs to, chests with the same link
- **Torches:** auto-fill for new torches, World Override for all torches
- **Station capacities** (admin): kiln, smelter, blast furnace, eitr refinery, beehive
- **Fermenter:** mead filter, add the same base in the first 60 s (batch up to 5)
- Red label above a linked station when its chests ran out
- **Stations wait when their chest is full:** "Chest full", "No linked chest", "No chest nearby" above them (switch in the Morda Mod Manager)
- **Catch-up while away** (off by default): stations finish the time their area was unloaded

### Chest settings (**Alt+E**)
- **Ignore:** skipped by everything
- **Show on display:** ignored chest still counts on displays
- **Only build/craft:** you fill it (by hand or dump), no ground pickup or station output, stations never take from it. Only crafting and building take from it
- **Only stations:** no dump, no ground pickup, no crafting or building from it. Only stations and links fill and empty it
- **l1-l9:** link for stations
- Settings are saved on the chest, not in its name

![](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/divider-norse.png)

## Build and farm

All pieces are in the hammer's **Store and Craft** tab.

### Storage displays
- Carved boards: small, medium, large, upright and wide. Vanilla sign displays too
- **E:** pick categories (small: one category or up to 4 items), search box
- **Right click** an item on the display (or in that menu): camera flies to the chest that holds it, the chest blinks (again = next chest)
- **Min** field in each item tile of that menu: below this amount the count on the display turns red (empty or 0 = off)
- **Weapons**, **Armor** and **Trinkets** are categories too (every item has its own tile)
- **Path of Valheiman:** own categories there (PoV Runes, Essences & Sigils, Azurite, Jewels, Oils & Bombs)
- **Shift + right click:** switch layout
- **Shift + left click:** display scale
- **Alt+E** on small: name / amount. **Alt+R:** scan range per display (the only place to set it, 10 m until set)
- Every display shows all of its items by itself (no shared pages)
- Chests set to Ignore are not counted

### Feed trough
- Food filter, links, auto-fill (**Alt+E**)
- Hungry tames walk to it and eat

### Scarecrow
- Plants a crop grid, harvests when all is ripe, replants
- **Shift / Ctrl + wheel:** width / length. **E:** crop, harvest / plant, auto-fill, links
- Needs cultivated ground. Unlocks with the Cultivator recipe
- A spot that lost its cultivated ground is cultivated again by itself (only the ground color, never the height)
- Plants again 30 s after the harvest. The hover shows that countdown, the time until the harvest and the seeds the grid needs
- Seed plants too: **Carrot (for seeds)** and so on grow seeds, so the seed supply never runs out
- **Patterns:** pick several crops (each gets a colour) and draw on the grid which crop grows where
- Own ranges in the Morda Mod Manager: player to scarecrow, scarecrow to chest
- Plants that other mods add to the Cultivator show up in its list too, for example the berry bushes, herbs and mushrooms of **Forager's Garden**

### Armor stand
- **E** swaps your armor with the stand's
- **Alt+E**: pick a preset item from your bag per weapon / shield / tool slot. **E** swaps it with the stand's piece, back and forth
- Without a preset, **E** puts the stand's weapon into your bag

![](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/divider-norse.png)

## Control

### Morda Mod Manager
- **F10** opens the list of all your mods. Click one to change its settings (switches, sliders, numbers, lists, hotkeys), then **Save**. The back arrow returns to the list
- StoreAndCraft: all ranges, all switches, station capacities and **every hotkey** (click it, press the new key)
- Servers: only admins from adminlist.txt can save the server values
- **Activity log** (optional): every transfer in a short list, renamed chests show as Chest[Name], lines fade out one by one

![](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/divider-norse.png)

## More mods by Morda

| Mod | What it does |
|:---|:---|
| **[WorkbenchesPlus](https://thunderstore.io/c/valheim/p/Morda/WorkbenchesPlus/)** | Sort and filter crafting menus: craftable first, categories, armor and weapon sets by biome tier |
| **[Forager's Garden](https://thunderstore.io/c/valheim/p/Morda/ForagersGarden/)** | Berry bushes, wild herbs and mushrooms with the Cultivator, plus Ice, Royal Jelly and Crystal beehives (formerly part of StoreAndCraft) |
| **[Morda's Gearhold](https://thunderstore.io/c/valheim/p/Morda/MordasGearhold/)** | Three switchable equipment sets, shared inventory, 8 hotkey quickslots, ammo slots, trinkets, a weightless coin slot, transmog looks, armour dyes and weapon glow |
| **[Valheim Mannequins](https://thunderstore.io/c/valheim/p/Morda/ValheimMannequins/)** | Player-shaped mannequins: dress them with real gear, pose and name them, keep several gear sets |
| **[ServerRates](https://thunderstore.io/c/valheim/p/Morda/ServerRates/)** | Server-side rates for dedicated servers (PC and console), live apply, presets. Joining players need no install |

![](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/divider-norse.png)

## Install

1. Install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
2. Put `StoreAndCraft.dll` in `BepInEx/plugins/StoreAndCraft/` (or use Thunderstore / r2modman)
3. Servers: same version on the server and every client

![Standing stones in the mist](https://raw.githubusercontent.com/morda0511/StoreAndCraft/main/thunderstore-assets/store-and-craft/store-and-craft-footer.png)
