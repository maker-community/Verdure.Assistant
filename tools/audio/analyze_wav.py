"""Measure a WAV file the way the Bumi audio tuning needs it.

Usage:
    python analyze_wav.py <label> <file.wav> [<label> <file.wav> ...]

Prints peak / RMS / crest, clipped samples, adjacent-sample jumps (a splice or a
hard clip shows up here), and band energies relative to the total.
Only numpy is required.
"""

import sys
import wave

import numpy as np


def load(path):
    with wave.open(path, "rb") as handle:
        channels, width, rate, frames = (
            handle.getnchannels(),
            handle.getsampwidth(),
            handle.getframerate(),
            handle.getnframes(),
        )
        raw = handle.readframes(frames)

    if width != 2:
        raise SystemExit(f"{path}: only 16-bit PCM is supported (got {width * 8}-bit)")

    data = np.frombuffer(raw, dtype="<i2").astype(np.float64) / 32768.0
    if channels > 1:
        data = data.reshape(-1, channels).mean(axis=1)
    return data, rate, channels


def band_db(spec, freqs, low, high):
    mask = (freqs >= low) & (freqs < high)
    return 10 * np.log10(np.sum(spec[mask]) + 1e-30)


def report(label, path):
    x, rate, channels = load(path)
    peak = np.max(np.abs(x))
    rms = np.sqrt(np.mean(x**2))
    jumps = np.abs(np.diff(x))

    size = 1 << 15
    window = np.hanning(size)
    frames = [x[i : i + size] * window for i in range(0, len(x) - size, size // 2)]
    if not frames:
        raise SystemExit(f"{path}: need at least {size / rate:.1f}s of audio")
    spec = np.mean([np.abs(np.fft.rfft(frame)) ** 2 for frame in frames], axis=0)
    freqs = np.fft.rfftfreq(size, 1 / rate)
    total = band_db(spec, freqs, 0, rate / 2)

    print(label)
    print(
        f"  dur={len(x) / rate:.2f}s ch={channels} sr={rate} "
        f"peak={20 * np.log10(peak + 1e-12):.1f} dBFS "
        f"rms={20 * np.log10(rms + 1e-12):.1f} dBFS "
        f"crest={20 * np.log10(peak / (rms + 1e-12)):.1f} dB "
        f"clip={int(np.sum(np.abs(x) >= 0.999))}"
    )
    print(f"  sample-jump: p99.9={np.percentile(jumps, 99.9):.3f} max={jumps.max():.3f}")
    for low, high in [(0, 100), (100, 300), (300, 1000), (1000, 2000), (2000, 4000), (4000, 6000), (6000, 8000)]:
        print(f"  {low:5d}-{high:5d} Hz: {band_db(spec, freqs, low, high) - total:6.1f} dBc")
    print()


def main():
    args = sys.argv[1:]
    if not args or len(args) % 2 != 0:
        raise SystemExit(__doc__)
    for label, path in zip(args[0::2], args[1::2]):
        report(label, path)


if __name__ == "__main__":
    main()
