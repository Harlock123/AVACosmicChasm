#!/usr/bin/env python3
"""Synthesises AVACosmicChasm's sound effects and tunes into ../src/AVACosmicChasm/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, saw, sine and noise voices, in the spirit
of the hum and crackle of a colour vector arcade cabinet. The title music is the opening of J. S. Bach's Toccata in
D minor (BWV 565), which is in the public domain, arranged here for a chip organ; the station, escape and game-over
jingles are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVACosmicChasm", "Sounds")
RNG = random.Random(1983)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


def gain(part, g):
    return [v * g for v in part]


def lowpass(samples, cutoff):
    a = 1 - math.exp(-2 * math.pi * cutoff / RATE)
    out, y = [], 0.0
    for v in samples:
        y += a * (v - y)
        out.append(y)
    return out


def echoed(dry, taps=((0.09, 0.3), (0.19, 0.15))):
    """The ring of a big arcade cabinet's loudspeaker."""
    out = dry + [0.0] * int(RATE * 0.4)
    for delay, g in taps:
        k = int(RATE * delay)
        for i, v in enumerate(dry):
            out[i + k] += v * g
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def seq(bpm, notes, kind="pulse", g=0.55, release=0.6, vibrato=0.0, legato=1.0):
    """A line of (note, beats); '-' is a rest. Notes can be cut short (legato below 1) for a detached feel."""
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        if note == "-":
            out.extend(silence(d))
            continue
        sounding = tone(d * legato, hz(note), kind=kind, release=release, gain=g, vibrato=vibrato, vib_rate=6)
        out.extend(sounding + silence(d - len(sounding) / RATE))
    return out


def fit(part, seconds):
    n = int(RATE * seconds)
    return (part + [0.0] * n)[:n]


def drums(bpm, beats, pattern):
    """A drum track: pattern is a string per beat - k kick, s snare, h hat, '.' nothing."""
    beat = 60.0 / bpm
    out = []
    for i in range(beats):
        c = pattern[i % len(pattern)]
        if c == "k":
            hit = tone(beat, 130, 45, "sine", attack=0.002, release=2.2, gain=0.9)
        elif c == "s":
            hit = mix(tone(beat, 2200, 900, "noise", attack=0.001, release=3.0, gain=0.45), tone(beat * 0.5, 220, 160, "tri", release=2, gain=0.3))
        elif c == "h":
            hit = tone(beat * 0.5, 7000, 6500, "noise", attack=0.001, release=4, gain=0.16)
        else:
            hit = []
        out.extend(fit(hit, beat))
    return out



# ───────────────────────────── the music ─────────────────────────────

def organ(seconds, note, g=0.5):
    """A chip organ: a square and a saw an octave apart, with a slow swell and a sustained tail."""
    f = hz(note)
    return mix(tone(seconds, f, kind="square", attack=0.02, release=0.35, gain=g * 0.5),
               tone(seconds, f * 2, kind="saw", attack=0.02, release=0.45, gain=g * 0.25),
               tone(seconds, f / 2, kind="tri", attack=0.03, release=0.3, gain=g * 0.5))


def organ_line(bpm, notes, g=0.5, octaves=(0,)):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        if note == "-":
            out.extend(silence(d))
            continue
        parts = []
        for o in octaves:
            name = note[:-1] + str(int(note[-1]) + o)
            parts.append(organ(d * 0.96, name, g))
        out.extend(fit(mix(*parts), d))
    return out


def toccata():
    """The opening of Bach's Toccata in D minor: the famous mordent falling to a low C sharp and D, three times down
    the octaves, then the diminished chord built up from the pedal and resolved."""
    bpm = 72
    phrase = [("A4", 0.125), ("G4", 0.125), ("A4", 1.25), ("-", 0.5),
              ("G4", 0.125), ("F4", 0.125), ("E4", 0.125), ("D4", 0.125), ("C#4", 0.5), ("D4", 1.5), ("-", 0.5)]
    def down(notes, k):
        return [(n if n == "-" else n[:-1] + str(int(n[-1]) - k), b) for n, b in notes]
    first = organ_line(bpm, phrase, 0.5, octaves=(0, -1))
    second = organ_line(bpm, down(phrase, 1), 0.5, octaves=(0, -1))
    # The third time, the run is spread out an octave lower still.
    third = organ_line(bpm, down(phrase, 2), 0.55, octaves=(0, 1))
    beat = 60.0 / bpm
    # The chord rolled up from the pedal D: C sharp, E, G, B flat, then all of it held, resolving to D minor.
    roll = []
    for i, n in enumerate(["D2", "C#3", "E3", "G3", "Bb3", "C#4", "E4"]):
        roll.append(silence(i * beat * 0.12) + organ(beat * 2.6 - i * beat * 0.12, n, 0.32))
    held = mix(*roll)
    resolve = mix(*[organ(beat * 2.5, n, 0.32) for n in ["D2", "D3", "F3", "A3", "D4", "F4"]])
    return echoed(cat(first, second, third, held, resolve, silence(0.8)), taps=((0.13, 0.35), (0.27, 0.2), (0.38, 0.1)))


def station_tune():
    """An original: a rising fanfare of fourths, the start of each station."""
    bpm = 140
    lead = [("D5", 0.5), ("G5", 0.5), ("C6", 0.5), ("-", 0.25), ("A#5", 0.25), ("C6", 1), ("F5", 0.5), ("A#5", 0.5), ("D6", 2)]
    bass = [("D3", 1), ("C3", 1), ("A#2", 1), ("F2", 1), ("G2", 2)]
    return mix(seq(bpm, lead, kind="saw", g=0.45, release=0.4, vibrato=0.005), seq(bpm, bass, kind="tri", g=0.6, release=0.5),
               seq(bpm, [(n, 0.25) for n in ["G4", "D5", "G5", "D5"] * 6], kind="square", g=0.1))


def escaped_tune():
    """An original: a bright, quick triumph as the station goes up behind."""
    bpm = 160
    lead = [("C5", 0.33), ("E5", 0.33), ("G5", 0.34), ("C6", 1), ("B5", 0.5), ("C6", 0.5), ("D6", 0.5), ("E6", 0.5),
            ("D6", 0.5), ("B5", 0.5), ("G5", 0.5), ("D6", 0.5), ("C6", 3)]
    bass = [("C3", 2), ("G2", 1), ("C3", 1), ("G2", 2), ("C2", 3)]
    return mix(seq(bpm, lead, kind="pulse", g=0.5, release=0.4), seq(bpm, bass, kind="tri", g=0.6, release=0.5),
               drums(bpm * 2, 18, "k.s.k.s."))


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The ship's gun: a short descending zap.
    write("Fire.wav", mix(tone(0.12, 2400, 500, "square", attack=0.001, release=1.3, gain=0.6), tone(0.04, 6000, 3000, "noise", release=2, gain=0.25)), 0.3)
    write("EnemyFire.wav", tone(0.22, 700, 1400, "pulse", attack=0.002, release=1.0, vibrato=0.05, vib_rate=40), 0.22)
    write("Blast.wav", mix(lowpass(tone(0.5, 3200, 300, "noise", attack=0.001, release=1.3), 2400), tone(0.3, 180, 50, "sine", attack=0.002, release=1.2, gain=0.7)), 0.5)
    write("ShipBoom.wav", cat(mix(lowpass(tone(1.5, 2400, 90, "noise", attack=0.002, release=0.7), 1400), tone(1.3, 120, 30, "saw", vibrato=0.25, vib_rate=9, release=0.8, gain=0.6)),
                              lowpass(tone(0.6, 700, 150, "noise", attack=0.02, release=1.2, gain=0.4), 700)), 0.62)
    write("CoreBoom.wav", cat(mix(lowpass(tone(2.6, 1800, 60, "noise", attack=0.004, release=0.6), 1000), tone(2.4, 70, 22, "saw", vibrato=0.3, vib_rate=7, release=0.6, gain=0.7),
                                  tone(2.0, 400, 60, "square", vibrato=0.15, vib_rate=13, release=0.9, gain=0.3)),
                              lowpass(tone(1.0, 500, 100, "noise", attack=0.05, release=1.0, gain=0.4), 500)), 0.7)
    write("CoreHit.wav", mix(tone(0.25, 300, 140, "square", attack=0.001, release=1.0, gain=0.6), tone(0.25, 302, 141, "saw", release=1.0, gain=0.4),
                             lowpass(tone(0.15, 2000, 500, "noise", release=2, gain=0.4), 1800)), 0.42)
    write("PlateHit.wav", mix(tone(0.1, 1800, 1500, "sine", attack=0.001, release=2.0), tone(0.06, 4000, 2000, "noise", release=3, gain=0.3)), 0.25)
    write("Ping.wav", mix(tone(0.14, 2600, 2400, "sine", attack=0.001, release=2.0), tone(0.12, 3700, 3500, "sine", attack=0.001, release=2.4, gain=0.5)), 0.22)
    # The drill: a grinding buzz, played over and over while it bites.
    write("Drill.wav", mix(tone(0.13, 140, 150, "saw", attack=0.002, release=0.4, vibrato=0.2, vib_rate=55), lowpass(tone(0.13, 3000, 2000, "noise", release=0.5, gain=0.5), 2500)), 0.32)
    write("DoorOpen.wav", cat(mix(lowpass(tone(0.3, 2500, 400, "noise", attack=0.001, release=1.0), 2000), tone(0.25, 200, 60, "sine", release=1.0, gain=0.6)),
                              *[tone(0.05, hz(n), kind="pulse", release=0.4, gain=0.5) for n in ["G5", "C6", "E6"]]), 0.42)
    write("Warp.wav", mix(tone(0.35, 180, 1400, "saw", attack=0.01, release=0.8, vibrato=0.04, vib_rate=25, gain=0.5), tone(0.35, 360, 2800, "sine", attack=0.01, release=0.8, gain=0.3)), 0.32)
    write("Emerge.wav", mix(tone(0.4, 90, 260, "square", attack=0.05, release=0.5, vibrato=0.15, vib_rate=18, gain=0.4), lowpass(tone(0.4, 1200, 600, "noise", attack=0.05, release=0.8, gain=0.3), 900)), 0.3)
    write("Shield.wav", tone(0.18, 500, 1100, "sine", attack=0.005, release=0.6, vibrato=0.06, vib_rate=35), 0.3)
    write("Bounce.wav", mix(tone(0.12, 420, 260, "tri", attack=0.001, release=1.2), tone(0.08, 900, 600, "square", release=1.6, gain=0.3)), 0.35)
    write("Alarm.wav", cat(tone(0.12, 880, kind="square", attack=0.002, release=0.2, gain=0.5), silence(0.04), tone(0.12, 660, kind="square", attack=0.002, release=0.2, gain=0.5)), 0.3)
    write("Siren.wav", tone(0.8, 500, 1100, "saw", attack=0.02, release=0.3, vibrato=0.02, vib_rate=6, gain=0.5), 0.3)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6", "C7", "E7", "G7"]], tone(0.3, hz("C7"), kind="pulse", release=0.6)), 0.3)
    # The thrust: a low, rough roar that loops without a seam.
    roar = lowpass(tone(2.0, 220, 220, "noise", attack=0.0001, release=0.0001, gain=1.0), 500)
    n = int(RATE * 0.1)
    loop = [roar[i] * min(1, (len(roar) - i) / n) + (roar[i - len(roar) + n] * (1 - (len(roar) - i) / n) if len(roar) - i < n else 0) for i in range(len(roar))]
    loop = loop[n:]
    write("Thrust.wav", [v * (1 + 0.15 * math.sin(2 * math.pi * 23 * i / RATE)) for i, v in enumerate(loop)], 0.35)

    # The music.
    write("TitleTune.wav", toccata(), 0.4)
    write("StationTune.wav", station_tune(), 0.38)
    write("EscapedTune.wav", escaped_tune(), 0.38)
    write("GameOver.wav", mix(seq(64, [("A4", 1), ("F4", 1), ("D4", 1), ("C#4", 1), ("D4", 4)], kind="tri", g=0.5),
                              seq(64, [("D3", 2), ("A#2", 2), ("A2", 2), ("D2", 2)], kind="tri", g=0.6)), 0.42)


if __name__ == "__main__":
    main()
