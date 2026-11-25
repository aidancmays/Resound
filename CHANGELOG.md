# Changelog

## 0.1.1
- Corner lenience: rays fan out from both ends, a ray that gets round an edge counts for more (Corner Lenience),
  and nearby sounds are blocked less (Near Lenience Distance). Wider default ray spread (0.9 m) and a louder
  default blocked gain (0.4).

## 0.1.0
- RoomGraph (rooms, doorways, shortest sound path), ResoundSystem (listener reverb, occlusion rays, per-frame
  budget), ResoundSource (occlusion, doorway routing), RoomAcoustics and ReverbSettings presets, ResoundRoom,
  ResoundPortal, ResoundOneShots, ResoundDebugPanel, and the Masonry adapter (MasonryRooms).
