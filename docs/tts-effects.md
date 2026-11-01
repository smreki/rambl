# TTS voice effects

Effects process audio after speech synthesis and can work with any supported TTS model or voice.

- [x] **Walkie-talkie** — narrow radio sound with adjustable tone, compression, distortion, static, and optional transmission clicks.
- [x] **Robot** — metallic modulation with adjustable modulation, robot pitch, and intensity. Robot pitch sets the metallic carrier frequency, rather than shifting the speaker's pitch.
- [ ] **Telephone** — classic phone-call sound, with tone, compression, and distortion controls.
- [x] **Spacesuit / Mission Control** — clear, compressed communications headset sound, with adjustable headset tone, static, and optional opening/closing transmission beeps.
- [ ] **PA / Megaphone** — loudspeaker sound, with distortion, tone, and echo controls.
- [ ] **Broadcast radio** — full, steady announcer voice, with bass, brightness, and compression controls.
- [ ] **Room / Hall** — speech with reverberation, with room size, reverb, and dry/effect mix controls.
- [ ] **Lo-fi / Retro computer** — crunchy digital speech, with bit depth, sample rate, and intensity controls.
- [ ] **Chorus / Alien** — layered, otherworldly voice, with pitch spread, depth, and mix controls.

Checked effects are implemented; unchecked effects are ideas for future additions.

## Radio profiles

Copilot and Pilot & ATC use the Walkie-talkie effect with different settings; they are profiles rather than separate effects.

| Setting | Copilot | Pilot & ATC |
| --- | --- | --- |
| Radio tone | 70% | 85% |
| Compression | 75% | 80% |
| Distortion | 8% | 15% |
| Static | 2% | 5% |
| Transmission clicks | Off | On |
