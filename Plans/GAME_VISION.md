# Game Vision — Working Notes
*Captured 2026-03-25*

## Concept
**Wipeout / Only Up style obstacle course game**

You're trying to get from A to B. The course is actively trying to knock you down. Everything in between is physics chaos. The ragdoll *is* the game.

## Why it fits the tech
- Ragdoll reacts physically to everything — falls, bounces, spinning obstacles, ropes
- The crumple + get-up sequence becomes part of the gameplay loop, not a frustration
- Arm controls + grabbing (ropes, ledges) extends the movement vocabulary naturally
- Physics brawler roots mean obstacles hitting the player will look and feel great

## Core loop
- Navigate a handcrafted obstacle course
- Obstacles actively try to knock you down (spinning arms, swinging things, projectiles, etc.)
- Fall = you tumble physically, get up, keep going (or fall further back down)
- Reach the destination = win

## Key design decisions (agreed)

**Handcrafted course** — not procedural
- More controllable for solo dev
- Enables curated "wow moments" — scenic reveals, perfectly timed obstacles, that corner where you see how far you still have to go
- Asset packs from Unity Asset Store for environment dressing

**No respawning** — "Getting Over It" style
- If you fall, you fall. Job done.
- The fall itself is the punishment — ragdoll tumbling back down IS the game
- Makes every inch of progress feel earned
- Baby mode with checkpoints as optional accessibility setting (keeps it welcoming)

**Multiplayer** — back of mind, not blocking
- Not required for launch
- Co-op could be really fun — watch your mate ragdoll off a platform while you cling on
- Proximity voice chat would be gold for co-op moments (clips itself)
- Design obstacles with two players in mind when the time comes — leave room on screen
- Async leaderboards / ghost runs could add replayability without real-time netcode

## Movement roadmap (before level design starts)
1. ✅ Fall & get-up (Plan 07 — nearly done)
2. 🔜 Auto-sprint (Plan 08)
3. 🔜 Land into a run (seamless landing-to-sprint)
4. 🔜 Arm controls + grabbing (ropes, ledges) — the big unlock

## Solo dev notes
- Tight scope: obstacle design + physics tuning, not open world or dialogue
- Asset packs handle visuals, Benny handles feel
- The movement system doing the heavy lifting means level design is the main creative work

## Vibe reference
- Only Up (verticality, no respawn, physics)
- Wipeout (obstacle course, getting knocked around)
- Getting Over It (punishing, earned progress)
- Fall Guys (chaos, fun to watch)
