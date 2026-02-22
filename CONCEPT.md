# CONCEPT.md — Hands Off! (Working Title)

> A 4-player asymmetric physics brawler. Three thieves. One security guard. A museum full of priceless, very knockoverable things.
>
> **Working title: "Hands Off!"** — has a real *Oi* energy to it.

---

## The Pitch

Gang Beasts combat meets asymmetric multiplayer in a physics-filled museum. Three thieves try to blend in, grab exhibits, and escape. One security guard watches the cameras from a booth *inside* the museum, trying to spot them before they vanish — then unleashes absolute chaos when they do.

---

## Players

| Role | Count | Goal |
|------|-------|------|
| Thief | 3 | Steal exhibits, escape with them before time runs out |
| Security | 1 | Identify thieves among NPC tourists, trigger interventions, stop the heist |

4 players total. Security is not a spectator — their booth can be raided.

---

## The Round Structure

### Phase 1 — Pre-Round (Loadout)
- Everyone spends earned currency
- **Security** buys intervention charges (dog, sprinklers, lockdown, etc.)
- **Thieves** spend on cosmetics and tactical perks (better grip, quieter movement, longer disguise duration)
- Quick, punchy — not a long lobby screen

### Phase 2 — Infiltration (Stealth Phase)
- Thieves spawn as part of the NPC tourist crowd
- Security watches camera feeds (with a short delay — not omniscient)
- Thieves move toward targets, trying not to blow cover
- Security must *commit* to an alarm — false alarms waste an intervention and tip off the real thieves
- Tension: do you wait for a better read, or act now before they grab something?

### Phase 3 — Pandemonium (Once Alarm Triggers)
- Gloves off. Thieves sprint for exhibits and exits. Security unleashes interventions.
- Physics chaos: artifacts sliding, tourists panicking, the guard dog ruining everyone's day
- Thieves have a window (e.g. 60 seconds) to escape with goods before round ends
- Security tries to physically intercept, lock doors, cut off routes

### Round End
- **Thieves escape with goods** → earn currency + score; stolen exhibit is *gone* from the museum next round
- **Security catches everyone** → security earns currency; new exhibit donated (higher value, more chaotic)
- The museum state *persists* across rounds in a session — it literally gets stripped bare or grows more elaborate

---

## Security Interventions

Bought pre-round, limited charges. Mix of targeted and area effects.

| Intervention | Effect |
|-------------|--------|
| 🚿 Sprinkler System | Floods a room — floors slippery, everyone ragdolls |
| 🐕 Guard Dog | Physics ragdoll dog barrels into nearest person (might backfire) |
| ⚡ Electric Floor | Zaps a zone — anyone standing there gets launched |
| 💨 Ventilation Blast | Huge air burst through a corridor — sends people (and loot) flying |
| 🔒 Lockdown | Seals a room — traps whoever's inside together |
| 🎵 PA Announcement | Disorients NPCs or causes tourist panic stampede |
| 🤖 Animatronic Activate | Wakes up a static exhibit (knight, dino skeleton) — starts swinging |
| 💡 Lights Out | Cuts lights in a specific room — thieves navigate blind |

**Design principle:** some interventions backfire. Release the dog near a thief carrying a vase and it might knock the vase to the exit by accident. Security should feel like they're barely in control.

**Camera delay:** feeds are ~3 seconds behind real time. Security can never be a puppet master — just a very stressed person hitting buttons.

---

## The Security Booth

- Physically located *inside* the museum
- Has monitors, a big red button panel, maybe a sandwich
- Thieves can *raid the booth* — knock out security, disable interventions for a period
- Adds a high-risk high-reward option for thieves: go for the booth early and buy everyone time, but you're not stealing anything while you're doing it

---

## Thief Disguise Mechanic

- Thieves spawn blending with NPC tourists (same idle animations, same movement speed)
- Disguise breaks if: you touch an exhibit, run, punch someone, or get too close to a camera
- Perks can extend disguise duration or let you carry small items while still disguised
- Security must *commit* to identifying someone — wrong call wastes an intervention

---

## Progression — The Rocket League Garage

The thing you fiddle with between rounds and sessions.

### Cosmetics
- Outfits, hats, accessories for your thief
- Security booth decorations (plants, posters, a better chair)
- Slow unlock drip — something new every few rounds

### Perks (Gameplay-Affecting)
Unlocked through play, equipped pre-round. Small advantages, not game-breaking.

**Thief perks (examples):**
- **Sticky Fingers** — can carry two small items at once
- **Cat Burglar** — quieter movement, disguise lasts longer
- **Getaway Artist** — faster sprint but can't grab exhibits while sprinting
- **Hard Head** — takes one more hit before going ragdoll

**Security perks (examples):**
- **Extra Charge** — one additional intervention per round
- **Reinforced Booth** — door takes longer to break down
- **Eagle Eye** — camera delay reduced from 3s to 1.5s
- **Panic Button** — one use per game: triggers ALL interventions simultaneously (complete chaos)

### Personal Trophy Case
- Your thief's "apartment" lobby screen
- Displays what you've successfully stolen across sessions
- Little dioramas of your best heists (procedurally captioned)

---

## The Newspaper Headline Screen

After every round, a procedurally generated newspaper front page:

> **"PRICELESS DINOSAUR LOST TO MAN IN BERET"**
> *Security guard electrocutes himself for third consecutive week*

Generated from what actually happened — who stole what, what went wrong, any spectacular backfires. Screenshot-able. Will be screenshotted constantly.

---

## The Museum as Meta-Game

Across a full session (multiple rounds):
- Successfully stolen exhibits are **removed** from the map — the museum gets emptier
- Successfully defended exhibits stay; new ones get added when security wins — higher value, more chaotic physics
- By the end of a session, the museum is either stripped bare or an increasingly absurd gallery of priceless objects

Creates a natural session arc without needing a complex meta-progression system.

---

## Open Questions

- **Respawning?** Does security respawn if thieves raid the booth, or is it one life?
- **NPC tourists** — passive obstacles only, or do they react (call police, get in the way)?
- **Map size** — single large museum floor, or multiple rooms with different themes (Egyptian wing, dinosaurs, modern art)?
- **Win condition** — first to X points across rounds, or fixed number of rounds?
- **Name** — "Museum Heist" is a placeholder. Something weirder needed.

---

*Created: 2026-02-22. Based on brainstorm with Benny. Update as concept evolves.*
