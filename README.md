# Resound

Spatial audio for procedurally built levels, with nothing baked.

- **Rooms and doorways** live in a `RoomGraph` you fill at runtime. A sound in another room is heard from the
  first doorway on the shortest way round (its `ResoundSource` slides there), as long as that's shorter than the
  muffled way through the walls. Shut doorways (`Portal.Openness`) cost extra and dull the sound.
- **Occlusion**: a few rays from the listener to each playing source; every wall a ray crosses blocks more. Blocked
  sounds get a low-pass and a gain drop (applied in `OnAudioFilterRead`, so your own volume changes still work).
- **Reverb** follows the listener: one `AudioReverbZone` on the listener blends to the acoustics of the room they're
  in. `RoomAcoustics` picks between a small and a large reverb by the room's volume. Music and UI sources should
  set `bypassReverbZones` (Dynamic Music does).

## Setup
1. Put a `ResoundSystem` in your first scene (or let one be made with defaults). Set **Occluders** to the layers
   walls are on.
2. Add `ResoundSource` next to each 3D `AudioSource`. Because it moves its own transform when routing, put the
   source on a child used only for sound. Scripts that make audio in `OnAudioFilterRead` go above it.
3. Rooms: hand-place `ResoundRoom` and `ResoundPortal`, register them from code with `ResoundSystem.Graph`, or for a
   Masonry layout add `MasonryRooms` next to the `LayoutBuilder` (pieces become rooms, joined sockets and level
   links become doorways; pick acoustics by module type or with `Configure`).
4. `ResoundOneShots.Play(clip, position)` for fire-and-forget sounds with the same treatment.

`ResoundDebugPanel`: F7 shows rooms, reverb and each source's occlusion; F6 (while shown) drops a looping test
sound where you stand.
