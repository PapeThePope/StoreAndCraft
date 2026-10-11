# Changelog

## 1.6.0

**Important:** the Ice, Royal Jelly and Crystal beehives (with their queens), the berry bushes, wild herbs and mushrooms (with their spores) are no longer part of StoreAndCraft. Want them? From 1.6.0 on download the mod **Forager's Garden**: everything you built with them is back at once. Without it your built hives and plants stay hidden, and your queens and spores wait in your chests as "(needs Forager's Garden)" items, nothing is lost.

### NEW FEATURES
- The **scarecrow** lists plants that other mods add to the Cultivator, for example the berry bushes, herbs and mushrooms of Forager's Garden

### FIX
- **Feed trough auto-fill** refills as soon as only one food is left: the same food, up to one full stack of it (berries: 1 left, up to 49 more), as much as your chests have. An empty trough gets the first allowed food, one stack at most

## 1.5.0

### NEW FEATURES
- **Chest setting "Only build/craft"**: you fill it (by hand or with dump), nothing else goes in by itself (no ground items, no station output, stations never take from it), only crafting and building take from it. It replaces "Manual fill": chests that had Manual fill count as "Only stations" now
- **Stations wait when their chest cannot take the product**: smelter, blast furnace, kiln, eitr refinery, windmill and spinning wheel with auto-store (**N**) switch off while the chests are full or no chest with their link is in reach, and a notice shows above them: "Chest full", "No linked chest", "No chest nearby" (the last one only shows, the station keeps running)
- The same notices above the grill (its food still goes to the ground, so it does not burn), beehive, sap extractor, fermenter and scarecrow
- A chest shows "Status: Full" / "Status: Almost full" when you look at it. It counts how full the stacks are, not only the slots: a slot with one wood still has room for more
- **Ice, Royal Jelly and Crystal Beehive**: craft an Ice Queen Bee (Queen Bee + Ice), a Royal Jelly Queen Bee (Queen Bee + Royal Jelly) or a Crystal Queen Bee (Queen Bee + Crystal) at the workbench, then build the white / yellow / violet beehive with it (the bees around it get its colour). It makes ice / royal jelly / crystal at the same pace as honey, and auto-store works like for honey
- **Plantable berry bushes and wild herbs** (Cultivator): Raspberry, Blueberry and Cloudberry bushes, Dandelion, Thistle, Fiddlehead and Smoke puff. A bush sapling costs 5 berries, a herb (Dandelion, Thistle, Fiddlehead, Smoke puff) 1 and gives 3 per harvest, the plant stands there grown right away (as if just picked) and grows its harvest back, and the scarecrow plants and harvests them too (Raspberries (bush) ... in its crop list)
- **Scarecrow patterns**: pick several crops (each gets a colour) and draw on a grid which crop grows where, so you can lay out a garden. Every kind of plant is harvested once all plants of that kind are ripe, the grid spacing follows the biggest plant
- **Scarecrow ranges**: the scarecrow has its own ranges, player → scarecrow and scarecrow → chest (mod manager F10, 0 = same as auto-fill)
- **Scarecrow**: Kale and Oat are in the crop list, and **Kale seeds (for seeds)** plants kale seeds to grow more kale seeds
- **Everything works in the whole loaded base** (mod manager F10, on by default): dump, middle-click, crafting / building from chests, take stack, search, auto-fill, station output, auto-store of ground items and the scarecrow use every chest in the area that is loaded around you, no ranges to set. Items still only go into chests that already hold them (and links / chest modes still apply). Each item type remembers the chest it went into last and goes there first, the base is only searched again when that chest is full. Switch it off to use ranges in meters (now under Advanced). Storage Displays keep their own range
- **Storage Display minimum**: every item tile in the display filter (**E**) has a small **Min** field. When the display shows less than that, the count turns red (empty or 0 = off). Each display keeps its own minimums
- **PierSupports** (other mod): the extra pier segments it builds down to the ground are paid from your chests too, like any other piece

### UI IMPROVEMENTS
- Mod manager (F10): the **Stack / Sort buttons** can be switched off, separately for the inventory and for the chest
- Mod manager (F10): the notices above stations can be switched off
- Mod manager (F10): **Save** works without admin rights too (it saves the settings for your own PC)
- **Activity log**: lines leave one after another from the top with a soft fade (each one stays readable for at least 3 s) instead of all standing for 12 s. Renamed chests show as Chest[Name], e.g. Chest[Tin Ore]
- The **Storage Display range** is only set on each display (**Alt+R**); the default slider in the mod manager (F10) is gone. A display without its own range uses 10 m
- Bloodgold is listed under Metals in the Storage Display filter
- Kale, Oat and their seeds are listed under Crops & Seeds in the Storage Display filter

### FIX
- **Storage Displays** no longer cause short stutters when many chests change: the work is split over several frames, displays that show the same chests share it, and a display that is slow to redraw waits a little longer before the next redraw
- The crafting list counts the items in your chests with much less work, also with large chest ranges
- Storage Displays update with much less work when chests change (each chest is only read again when it changed)
- Auto-fill spreads its station checks over several frames instead of doing them all at once
- Storage Displays show their items only within 20 m (the board itself stays visible), and displays you cannot see (out of view or farther away) no longer recount and redraw; they update as soon as you look at them
- Dump, middle-click, take stack, search, station output and auto-store only look at chests that hold the item, and no longer reload every chest in range before a dump (Valheim keeps loaded chests up to date by itself)
- Big bases run smoother: Storage Displays, chest searches, auto-stack, auto-store and station checks do much less work per frame and create less garbage (fewer short stutters)
- Items on the ground that do not fit into a chest are tried less often
- **Scarecrow**: crops that only grew in one biome (Flax, Barley, Onion, Turnip ...) now grow on any cultivated ground inside the scarecrow's grid
- **Auto-stack** (and the Stack / Sort buttons) no longer swap a 9/50 and a 50/50 stack with each other again and again; only partial stacks are merged
- Displays that stayed empty for a while after joining a server look for newly loaded chests more often
- If the display models cannot be loaded (for example a damaged upload on the server), the log now says so
- **Auto-store of ground items** no longer takes fish: live fish in the sea or a fish pond stay where they are (pick fish up by hand)
- Saving in the mod manager (F10) on a server no longer shows a long yellow message with all ranges
- **Storage Displays** no longer share pages with other displays that have the same selection: every display shows all of its items by itself (after changing a layout some items were missing until the range was changed)

## 1.4.2

### NEW FEATURES
- **Plantable mushrooms** (Cultivator): Mushroom, Yellow mushroom and Blue mushroom
  - Plant a mushroom on cultivated ground: it grows **spores** (3 per mushroom)
  - Plant the spores: they grow mushrooms. Same cycle as carrot and carrot seeds
  - New items: Mushroom spores, Yellow mushroom spores, Blue mushroom spores, each in its colour
  - The scarecrow plants and harvests them too (Mushroom (for spores) and Mushroom spores in its crop list)
- **Scarecrow**: after a harvest it waits 30 s before planting again, the hover text counts down ("Auto-plant in") and shows the time until the harvest ("Harvest in")
- **Scarecrow**: seed plants too (Carrot (for seeds), Onion (for seeds) ...): plant the vegetable, harvest seeds, so the seeds never run out
- **Scarecrow**: the crop list and the hover show how many seeds the whole grid needs

### UI IMPROVEMENTS
- Mod manager: scrollbar next to the list (drag it with the mouse)
- Station label for an empty input says what the station takes: Wood (kiln), Grain (windmill), Flax (spinning wheel), Ore (smelter)
- Much quieter log: only real problems show up

### FIX
- Scarecrow: **Level ground** and **Cultivate** only touch the planted grid (before they reached far beyond it), pressing again no longer makes spikes
- Scarecrow: no seeds are planted where a plant would have no room to grow, and a stuck plant no longer blocks the harvest
- Scarecrow: choosing another crop while the old one is still growing no longer leaves the old one unharvested
- Scarecrow: harvest after a plant has just grown no longer waits a whole minute
- Feed trough, scarecrow and displays were magenta (no texture) with Vulkan / DirectX 12
- **Only stations** chests are no longer used for crafting, building or Epic Loot, only by stations and links
- Lit fire / torch that can be switched off: **E** switches it off again (it refilled it instead)

## 1.4.0

### NEW FEATURES
- **Scarecrow** (hammer, Storage tab): plants a crop grid, harvests when all is ripe, replants
  - **Shift / Ctrl + mouse wheel**: width / length (up to 21)
  - **E**: panel with crop, harvest / plant, auto-fill, links
  - Panel buttons **Level ground** (to the scarecrow's height, click twice) and **Cultivate** (like the Cultivator), only on a press
  - Seeds from chests, crops into chests (links l1-l9 work)
  - Needs cultivated ground. Unlocks with the Cultivator recipe. Costs Scythe, Cultivator, 5 leather scraps, 3 wood
- **Carved storage displays** (hammer, Storage tab): small, medium, large, each upright and wide, own icon and recipe
  - Each category in its own area, **Shift+RMB** switches layouts (sign displays too)
  - Small: one category or up to 4 items
  - Search box in the filter menu, filters list only items you have discovered
  - **Right click** an item on a display (or in its filter menu): the camera flies to the chest that holds it and the chest blinks (click again for the next one)
- **Feed trough**: **Alt+E** food filter, links, auto-fill (refills when empty)
- **Fermenter**: **Alt+E** mead filter, auto-fill. Add the same mead base in the first 60 s (batch up to 5)
- **Armor stand**: **E** swaps the armor you wear with the stand's. **Alt+E**: preset per weapon / shield / tool slot, **E** swaps it with the stand's piece. F10 switch
- **Chest settings**: switches Ignore, Show on display, Manual fill, **Only stations**
  - Only stations: no dump, no ground pickup, stations and links still fill it
  - No more `[I]` / `[H]` / `[M]` in the chest name, old names convert on confirm
- **Torches**: F10 option auto-fill for new torches, World Override for all torches
- **Station capacities** in F10 (admin): kiln, smelter, blast furnace, eitr refinery, beehive
- **Sap extractor**: **N** auto-store puts the sap into chests
- Linked stations show in red what their chests ran out of
- **Catch-up while away** (off by default): stations finish the time their area was unloaded
- **Mod manager** (F10): list of all mods, change the settings of other mods in the game (Save writes their config file), back arrow to the list
- **Sort** and **Stack** buttons on inventory and chests
- **Hugin** tells what is new once per update (stays until you press Esc or walk away)

### UI IMPROVEMENTS
- F10 panel: Valheim look, wider, hotkey of each feature next to its slider (click, press new key, Esc cancels, Backspace unbinds)
- All menus in Valheim look, darker at night like vanilla
- New models for the carved displays, new hammer icons (displays, trough, scarecrow)
- Sorting moved from **R** to the Sort button
- Inventory: **Stack** and **Sort** sit at the top right above the inventory (the old spot was where trash can mods put theirs)
- Small things: yellow checkbox tick, centered icons and amounts, activity log state remembered, matching link buttons

### FIX
- Scarecrow harvest goes straight into chests (also when you are further away), not onto the ground
- Scarecrow crop list shows only what can be planted (seeds, not the vegetable)
- Chests and feed trough looked empty after login until opened
- F10: capacities were not saved, admins on servers could not edit
- Less multiplayer lag around many stations, displays no longer rescan constantly
- Epic Loot items no longer go into other players' chests
- Display items: wrong position, mirrored, missing counts, flicker
- Linked stations only fill chests with the same link
- **Alt+E** on fermenter / fire no longer also does **E**
- Sort / Stack / Take all buttons could sit in the wrong place
- Character no longer walks while the display filter is open
- Log error (WearNTear.UpdateBiome) from displays

**Note:** needs the same version on server and all clients (network protocol changed).

## 1.3.44

### NEW FEATURES
- F10 settings panel: sliders and number fields for all ranges, activity log checkbox, Save button, short info on what each range does. On a server only admins (adminlist) can save; in a solo world everyone can
- New `AutoFillChestRange`: stations take items from chests around the station, not around the player (0 = same as AutoFillRange)
- Putting items into a chest wakes nearby stations right away (about 1 s instead of up to 25 s)
- Changing a station filter or link with Alt+E makes the station react at once

### UI IMPROVEMENTS
- Activity log / settings key moved from F11 to F10 (F11 is the Valheim screenshot key). Old F11 configs switch over automatically

### FIX
- Auto-fill stopped for kilns, smelters and windmills when many torches or fires were in range
- All stations now get a fair turn, so a large base no longer starves stations at the end of the list
- Two players near the same station no longer fill it twice at the same time
- A cooking station with no allowed food no longer blocks auto-fill for other stations

## 1.3.43

### NEW FEATURES
- Auto-store (**N**) on the fermenter: finished mead goes into a nearby chest (ground only if no matching chest)
- Fire and torch hover shows the remaining burn time in minutes, in green next to the name
- F11 activity log shows every mod action: dump, store, take stack, auto-store from the ground, craft and build pulls, build grab, station [E], feed trough, sort, auto-stack, B / N toggles, station filter and link, chest rename, config changes

### UI IMPROVEMENTS
- Fires and torches no longer show Alt+E Settings (they only take wood or resin)

### FIX
- Shift+E on fires fills to max again instead of adding only 1 wood
- Range settings are limited to 0-1000 m in the config file and in chat commands
- Fires and torches ignore station links and take fuel from unlinked chests

## 1.3.42

### NEW FEATURES
- F11 toggles an activity log (up to 10 lines under the TopLeft status text, same font as "You are cold"; starts off). Lines like `Chest 10x Tin -> Smelter` / `Kiln 5x Coal -> Chest`
- Auto-store (**N**): finished kiln / smelter / blast-furnace bars, cooking food, and beehive honey go into nearby chests (independent from Auto-fill **B**)
- Station Alt+E filter also blocks manually inserting denied wood / ore / food from your bag (not only chest pull)

### UI IMPROVEMENTS
- Station output toggle (**N**) labeled Auto-store / Auto-Lagern
- Alt+E labeled **Settings** everywhere (chest, station, hover)
- Station Settings: Display-style item cells (sprite + name + toggle, 2 per row); link grid (l1-l9) in Valheim bronze style, sized and placed in the parchment margin
- Chest rename link grid sits a few pixels higher (no panel-edge clip)
- Large Storage Display category labels show the full name again (Scale / Layout); Medium/Large keep readable amounts

### FIX
- Auto-store (**N**) deposit order: matching linked chest first (item already inside); if full, unlinked chest that already holds the item; never a different link - otherwise ground drop
- Auto-fill (**B**) never takes from your bag - chests only (matching link, or both unlinked). Removed StationFillSkipInventory config
- Auto-fill uses chests around the station (not only around the player) while you stay in range; reacts faster and retries once on a stale chest view
- Station link l1-l9: kiln/smelter/oven/fire no longer pull from the wrong channel
- Removed Remote Automation completely (including background keep-alive / zone scans)
- Opening rename chest no longer spams LiberationSans font missing warnings

## 1.3.41

### FIX
- Station auto-fill / Shift+[E] fill-to-max no longer take from your bag by default (`StationFillSkipInventory` defaults to on). Set it to false in config if you want bag-first

## 1.3.40

### FIX
- Storage Displays no longer flicker when changing Scale or Layout
- Kiln / smelter chest pull no longer takes wood or ore from unlinked chests
- Food Preparation Table: Raw Fish (and other only-one-ingredient recipes) craft from nearby chests and remove the fish from the chest

## 1.3.39

### UI IMPROVEMENTS
- Station hover prompts use one order everywhere: vanilla actions, then two-key shortcuts, then single keys

### FIX
- Cooking: raw meat from the hotbar or chests (including Volture meat) works on cooking stations again
- Cooking auto-fill places food on the spit/oven instead of treating it as not allowed
- Stone oven no longer shows a third empty hover on the body (food door and wood only)

## 1.3.38

### FIX
- Cooking from chests: raw meat (including Vulture meat) cooks on the spit/oven again instead of only appearing in your bag
- Remote Automation temporarily disabled (unstable when far from the station)
- Shift+[E] Fill to max works on fireplaces and torches (and cooking-station fuel)

## 1.3.36

### NEW FEATURES
- Ballista (Mistlands turret) auto-fill: press [B] to load ammo from bag and nearby chests

### UI IMPROVEMENTS
- Kiln / smelter hover: no duplicate Auto-fill / Fill to max; order is Add ? Fill to max ? Chest pull filter ? Auto-fill
- Favorites: star badge only ? no gold tint on the item icon

### FIX
- Station auto-fill finds items in nearby chests again when chest inventories looked empty after the wipe-guard (Frost Foundry / smelters / kilns)
- Frost Foundry casts (and other max-stack-1 items) can be pulled from chests again ? LeaveOne no longer blocks the only copy

## 1.3.35

### FIX
- Cooking auto-fill (campfire sticks / spit / oven) pulls food from chests again like pressing [E]; stone ovens still fall back to network add when the under-fire is out

## 1.3.34

### NEW FEATURES
- Hold Shift and press [E] on a kiln, smelter, or blast furnace to fill fuel or ore to max from your bag and nearby chests

### FIX
- Storage Displays no longer clear nearby chest contents when counting items (stronger empty-load guard)
- Feed Trough keeps food visible and usable after you put carrots or other animal food inside
- Config sync no longer loops forever when the server handshake works but the config package fails; the client keeps local settings and warns once

## 1.3.33

### FIX
- Forge of Potential: chest idols now count and can be spent. LeaveOne does not apply there (one idol in a chest is enough)

## 1.3.32

### FIX
- Select Types menu opens without a long freeze (no full item-list rebuild for every category)

## 1.3.31

### NEW FEATURES
- Station auto-fill: new config `StationFillSkipInventory` ? when on, kilns, smelters, ovens, and fires take from chests only, never from your bag
- Feed Trough: hungry tames walk up to the trough and eat there, like food on the ground
- Auto-drop (**N**) also works on beehives ? honey drops on the ground for auto-store

### UI IMPROVEMENTS
- Feed Trough shows a small food sparkle when it has food inside (hidden when empty)
- Storage Display scale: Shift+LMB steps **25%** (?2?+4); icon and count stay one pair with room for 4-digit totals; category labels stay fixed
- Storage Display layout: Shift+RMB toggles **Classic** ? **Compact** on Medium/Large (saved per board). Compact starts each category on its own row (label + items)
- Storage Display type menu: categories on top with on/off toggles, items for the selected category below (icon + one toggle each); faster scroll

### FIX
- Storage Displays no longer wipe nearby chests when counting items (iron chests included)
- Dump / sort / auto-fill leave equipment and quick-slot mod overflow alone (e.g. EquipmentAndQuickSlotsPlus Z V B); Wider / Deeper Pockets bag rows work like normal inventory
- Coming back to base after a raid no longer saves empty reinforced chests (lag spike / unload race)
- Feed Trough hover tells you to put food inside
- Storage Display: switching Compact ? Classic no longer flickers / blanks the board (Medium header strip restored)
- Storage Display only reads chests ? no re-Load over a full bag and no empty Save if a count briefly shows 0
- Large Classic scale grows icon and count together (taller rows); categories pack without overlapping numbers
- Select Types menu uses Valheim fonts (no LiberationSans missing-font spam)

## 1.3.30

### NEW FEATURES
- Storage Display scale: Shift + left click on Medium / Large cycles -10..+10 (icons and counts); Small cycles Name + Amount / No name / Icon only
- Feed Trough: hammer piece (Storage tab) - put animal food inside; nearby hungry tames eat from it

### UI IMPROVEMENTS
- Chest rename: Ignore and Show on Display use the new on/off toggle buttons (plus l1-l9 link grid)

### FIX
- Safer chest saves so items no longer vanish when a chest briefly looks empty on load
- Empty chests keep their custom rename (no longer lose the name / show broken empty titles)
