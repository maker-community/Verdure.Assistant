"""Reference implementation of the Bumi tone chain, and the A/B variants used to pick it.

Usage:
    python make_variants.py <input.wav>

Writes alongside the input:
    *-B-level.wav        level only, peak -3 dBFS
    *-C-bright.wav       level + 2.5 kHz shelf +5 dB, limiter -3 dBFS
    *-D-bright-hp.wav    level + 2.5 kHz shelf +6 dB + 150 Hz high-pass, limiter -3 dBFS
    *-E-loud-bright.wav  level + 3 kHz shelf +8 dB + 120 Hz high-pass, limiter -2 dBFS

D is what the assistant ships (see docs/AUDIO_QUALITY_TUNING_BUMI.md). E was
rejected: louder, but the sibilants started to bite at module volume 100.
Only numpy is required.
"""

import sys
import wave

import numpy as np


def load(path):
    with wave.open(path, "rb") as handle:
        channels, rate, frames = handle.getnchannels(), handle.getframerate(), handle.getnframes()
        raw = handle.readframes(frames)
    data = np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768.0
    if channels > 1:
        data = data.reshape(-1, channels).mean(axis=1)
    return data, rate


def save_stereo(path, samples, rate):
    pcm = (np.clip(samples, -1.0, 1.0) * 32767.0).astype("<i2")
    with wave.open(path, "wb") as handle:
        handle.setnchannels(2)
        handle.setsampwidth(2)
        handle.setframerate(rate)
        handle.writeframes(np.repeat(pcm, 2).tobytes())


def biquad(x, b0, b1, b2, a1, a2):
    y = np.empty_like(x)
    x1 = x2 = y1 = y2 = 0.0
    for i, value in enumerate(x):
        out = (b0 * value) + (b1 * x1) + (b2 * x2) - (a1 * y1) - (a2 * y2)
        x2, x1 = x1, value
        y2, y1 = y1, out
        y[i] = out
    return y


def high_pass(x, rate, cutoff_hz, q=0.7071):
    w0 = 2 * np.pi * cutoff_hz / rate
    cos, sin = np.cos(w0), np.sin(w0)
    alpha = sin / (2 * q)
    a0 = 1 + alpha
    return biquad(
        x,
        ((1 + cos) / 2) / a0,
        (-(1 + cos)) / a0,
        ((1 + cos) / 2) / a0,
        (-2 * cos) / a0,
        (1 - alpha) / a0,
    )


def high_shelf(x, rate, cutoff_hz, gain_db):
    amplitude = 10 ** (gain_db / 40.0)
    w0 = 2 * np.pi * cutoff_hz / rate
    cos, sin = np.cos(w0), np.sin(w0)
    alpha = sin / 2 * np.sqrt((amplitude + 1 / amplitude) * (1 / 1.0 - 1) + 2)
    two_sqrt_a_alpha = 2 * np.sqrt(amplitude) * alpha
    a0 = (amplitude + 1) - ((amplitude - 1) * cos) + two_sqrt_a_alpha
    return biquad(
        x,
        (amplitude * ((amplitude + 1) + ((amplitude - 1) * cos) + two_sqrt_a_alpha)) / a0,
        (-2 * amplitude * ((amplitude - 1) + ((amplitude + 1) * cos))) / a0,
        (amplitude * ((amplitude + 1) + ((amplitude - 1) * cos) - two_sqrt_a_alpha)) / a0,
        (2 * ((amplitude - 1) - ((amplitude + 1) * cos))) / a0,
        ((amplitude + 1) - ((amplitude - 1) * cos) - two_sqrt_a_alpha) / a0,
    )


def limiter(x, ceiling_db, rate, release_ms=120.0):
    ceiling = 10 ** (ceiling_db / 20.0)
    coeff = 1 - np.exp(-1.0 / max(1.0, rate * release_ms / 1000.0))
    gain = 1.0
    y = np.empty_like(x)
    for i, value in enumerate(x):
        magnitude = abs(value)
        target = ceiling / magnitude if magnitude > ceiling else 1.0
        gain = target if target < gain else gain + ((target - gain) * coeff)
        y[i] = value * gain
    return y


def normalize_to_peak(x, target_db):
    return x * (10 ** (target_db / 20.0) / np.max(np.abs(x)))


def describe(name, x):
    print(
        f"{name:<14} peak {20 * np.log10(np.max(np.abs(x))):6.1f} dBFS  "
        f"rms {20 * np.log10(np.sqrt(np.mean(x**2))):6.1f} dBFS"
    )


def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)

    source = sys.argv[1]
    x, rate = load(source)
    describe("input", x)

    level = normalize_to_peak(x, -3.0)
    save_stereo(source.replace(".wav", "-B-level.wav"), level, rate)

    bright = limiter(high_shelf(normalize_to_peak(x, -6.0), rate, 2500, 5.0), -3.0, rate)
    save_stereo(source.replace(".wav", "-C-bright.wav"), bright, rate)

    bright_hp = limiter(high_pass(high_shelf(normalize_to_peak(x, -4.0), rate, 2500, 6.0), rate, 150), -3.0, rate)
    save_stereo(source.replace(".wav", "-D-bright-hp.wav"), bright_hp, rate)

    loud = limiter(high_pass(high_shelf(normalize_to_peak(x, -4.0), rate, 3000, 8.0), rate, 120), -2.0, rate)
    save_stereo(source.replace(".wav", "-E-loud-bright.wav"), loud, rate)

    for name, variant in [
        ("B-level", level),
        ("C-bright", bright),
        ("D-bright-hp", bright_hp),
        ("E-loud-bright", loud),
    ]:
        describe(name, variant)


if __name__ == "__main__":
    main()
