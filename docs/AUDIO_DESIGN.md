# Lakskongen Salmon-Sim: Audio Design Specification

## Overview
The audio landscape of "Lakskongen" is designed to be immersive, reflecting the authentic atmosphere of a Norwegian coastal salmon farm. The dynamic audio system reacts to weather, camera zoom level, and ongoing farm activities.

## 1. Audio Categories & Descriptions

### Ambient Ocean/Coastal
- **Waves:** Ranging from gentle lapping against pens to crashing storm surges.
- **Wind:** Whistling through facility structures, dynamically linked to wind speed.
- **Wildlife:** Seagulls (occasional cries), eagles (rare), seals (rare distant barks).
- **Seasonal:** Crisp, howling winds in winter; gentler breezes and more bird activity in summer.

### Weather-Dependent Audio
- **Rain:** Pitter-patter on metal surfaces, hissing on water.
- **Storms:** Deep thunder rumble, aggressive wind howling, heavy wave crashes.
- **Calm:** Extremely subtle water movement, distant buoy bells.

### Operational Sounds
- **Fish Behavior:** Splashing during feeding times, jumping sounds, mass movement during stress/disease events.
- **Machinery:** Automated feed barge blowers, generators (low hum), processing plant conveyer belts and processing hums.
- **Vessels:** Wellboat diesel engine chug, workboat outboard motors, horn signals.

### UI & Interaction
- **Clicks:** Crisp, tactile clicks for menus.
- **Notifications:** Gentle bells for milestones.
- **Alerts:** Distinct, urgent (but not obnoxious) tones for disease outbreaks, low oxygen, or equipment failure.

### Music Themes
- **Main Menu:** Melancholic, expansive Nordic ambient/folk (strings, synth pads).
- **Gameplay:** Very subtle, non-intrusive ambient beds that fade in and out.
- **Events:** Tense percussive beds for storms/crises, triumphant swells for major contracts/harvests.

## 2. Godot Audio Implementation Plan

### AudioBus Layout
A hierarchical structure in Godot's `default_bus_layout.tres`:
- `Master`
  - `BGM` (Background Music)
  - `Ambient` (Weather, Ocean)
  - `SFX_World` (Boats, Fish, Machinery - Spatialized)
  - `SFX_UI` (Menus, Alerts - Non-spatialized)

### Dynamic Audio System
- **Camera Zoom:** As the camera zooms out to map view, `SFX_World` is heavily low-pass filtered and lowered in volume. Wind ambient volume increases. Zooming in on a pen isolates the fish splashing and localized water sounds.
- **Spatial Audio:** `AudioStreamPlayer2D` (or 3D) used for boats, pens, and facilities to provide positional awareness.

### Event-Triggered Audio
- **Harvest:** Massive splashing, machinery whirring, crane hydraulics.
- **Disease Outbreak:** Ominous musical cue, erratic fish splashing.
- **Feeding:** Feed blower start-up sequence, pellet spray sounds, sudden fish surface frenzy.

## 3. Suggested Sound Sources (Free/CC0)
- **Freesound.org:** Great for raw ocean, wind, and machinery recordings.
- **Sonniss GDC Audio Archive:** High-quality environmental and mechanical sound effects.
- **Incompetech / Kevin MacLeod:** For placeholder event stingers.
- **Kenney.nl:** UI audio packs.
