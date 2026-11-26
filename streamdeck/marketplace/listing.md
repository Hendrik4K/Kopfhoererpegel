# Elgato Marketplace listing (copy into Maker Console)

Pictures in this folder are generated: `kopfhoererpegel-streamdeck.exe --store marketplace`
(thumbnail.png + galerie-1..3.png, 1920 x 960). App icon: `bilder/plugin.png` / `plugin@2x.png` (256 / 512).

## Name
Headphone Level

## Description (max. 1,500 characters)
See how loud your AirPods really are, right on your Stream Deck, like the headphone level on iPhone.

The key shows the live level in dB(A), a status (OK, Loud from 80 dB, Very loud from 90 dB) and an 18-segment bar from 20 to 110 dB. The front segments pulse with the beat. When the level gets very loud, the key flashes red, so you notice even while gaming or streaming.

Press the key to switch to the last 60 seconds: one bar per second in the status colour, with a dashed line at 80 dB. There is also a separate History action.

How it works: the plugin reads what Windows sends to your AirPods (passive loopback, nothing is opened on the headphones, so they stay in music mode), applies A-weighting, averages over one second and adds the AirPods volume. Like on iPhone the level is calculated from the signal, not measured in your ear, so treat it as a guide, not a medical measurement.

Requirements: Windows 10 or 11, AirPods (any model) connected via Bluetooth. Works in English and German.

AirPods and iPhone are trademarks of Apple Inc. This plugin is not affiliated with or endorsed by Apple.

## Keywords
AirPods, headphones, hearing, volume, dB, decibel, sound level, loudness, audio, health

## Category
Audio

## Release notes 1.0.0
First release: live dB level of your AirPods with status, beat bar, red warning flash and 60-second history.
