# StoreAndCraft

**Your base runs with you.**
Loot finds its chest. One key clears your pockets. Craft, upgrade and build straight from storage. Stations refill themselves from chests and put their output back. Storage displays, feed trough, scarecrow and more.

**Required on the server and on every client (same version).** Valheim 1.0 · BepInExPack 5.4.2350+. Console players cannot load the mod.

**Bug reports:** https://github.com/morda0511/StoreAndCraft/issues/1
**Discord:** https://discord.gg/aVKVVmyzj

**1.6.0:** the beehives (Ice, Royal Jelly, Crystal), berry bushes, wild herbs and mushrooms are no longer part of StoreAndCraft. Want them? Download **Forager's Garden**: everything you built with them comes back. Until then nothing is lost, queens and spores wait in your chests.

---

## Features

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

### Storage displays (hammer, Storage tab)
- Carved boards: small, medium, large, upright and wide. Vanilla sign displays too
- **E:** pick categories (small: one category or up to 4 items), search box
- **Right click** an item on the display (or in that menu): camera flies to the chest that holds it, the chest blinks (again = next chest)
- **Min** field in each item tile of that menu: below this amount the count on the display turns red (empty or 0 = off)
- **Shift + right click:** switch layout
- **Shift + left click:** display scale
- **Alt+E** on small: name / amount. **Alt+R:** scan range per display (the only place to set it, 10 m until set)
- Every display shows all of its items by itself (no shared pages)
- Chests set to Ignore are not counted

### Feed trough (hammer)
- Food filter, links, auto-fill (**Alt+E**)
- Hungry tames walk to it and eat

### Scarecrow (hammer)
- Plants a crop grid, harvests when all is ripe, replants
- **Shift / Ctrl + wheel:** width / length. **E:** crop, harvest / plant, auto-fill, links
- Needs cultivated ground. Unlocks with the Cultivator recipe
- Plants again 30 s after the harvest. The hover shows that countdown, the time until the harvest and the seeds the grid needs
- Seed plants too: **Carrot (for seeds)** and so on grow seeds, so the seed supply never runs out
- **Patterns:** pick several crops (each gets a colour) and draw on the grid which crop grows where
- Own ranges in the Morda Mod Manager: player to scarecrow, scarecrow to chest
- Plants that other mods add to the Cultivator show up in its list too, for example the berry bushes, herbs and mushrooms of **Forager's Garden**

### Armor stand
- **E** swaps your armor with the stand's
- **Alt+E**: pick a preset item from your bag per weapon / shield / tool slot. **E** swaps it with the stand's piece, back and forth
- Without a preset, **E** puts the stand's weapon into your bag

### Chest settings (**Alt+E**)
- **Ignore:** skipped by everything
- **Show on display:** ignored chest still counts on displays
- **Only build/craft:** you fill it (by hand or dump), no ground pickup or station output, stations never take from it. Only crafting and building take from it
- **Only stations:** no dump, no ground pickup, no crafting or building from it. Only stations and links fill and empty it
- **l1-l9:** link for stations
- Settings are saved on the chest, not in its name

### Morda Mod Manager
- **F10** opens the list of all your mods. Click one to change its settings (switches, sliders, numbers, lists, hotkeys), then **Save**. The back arrow returns to the list
- StoreAndCraft: all ranges, all switches, station capacities and **every hotkey** (click it, press the new key)
- Servers: only admins from adminlist.txt can save the server values
- **Activity log** (optional): every transfer in a short list, renamed chests show as Chest[Name], lines fade out one by one

---

## Install

1. Install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
2. Put `StoreAndCraft.dll` in `BepInEx/plugins/StoreAndCraft/` (or use Thunderstore / r2modman)
3. Servers: same version on the server and every client
