using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skybridge.Game
{
    public enum SkybridgeAudioMode
    {
        Build,
        Test,
        Silent,
    }

    public sealed class SkybridgeAudioEngine : MonoBehaviour
    {
        private const float MasterGain = 0.18f;
        private const float BuildAmbienceGain = 0.018f;
        private const float TestAmbienceGain = 0.035f;

        private enum Waveform
        {
            Sine,
            Square,
            Triangle,
            Sawtooth,
        }

        private readonly List<AudioClip> generatedClips = new();
        private AudioSource effects;
        private AudioSource ambience;
        private AudioLowPassFilter ambienceFilter;
        private AudioClip lockPrimaryClip;
        private AudioClip lockSecondaryClip;
        private AudioClip invalidClip;
        private AudioClip deleteClip;
        private AudioClip warningClip;
        private AudioClip testPulseClip;
        private AudioClip breakNoiseClip;
        private AudioClip breakSweepClip;
        private AudioClip successPrimaryClip;
        private AudioClip successSecondaryClip;
        private AudioClip successFinalClip;
        private bool muted;
        private SkybridgeAudioMode mode = SkybridgeAudioMode.Build;
        private Coroutine pulseRoutine;

        public SkybridgeAudioMode Mode => mode;

        public void Initialize(bool startMuted)
        {
            effects = gameObject.AddComponent<AudioSource>();
            effects.playOnAwake = false;
            effects.spatialBlend = 0f;
            effects.volume = MasterGain;
            ambience = gameObject.AddComponent<AudioSource>();
            ambience.playOnAwake = false;
            ambience.loop = true;
            ambience.spatialBlend = 0f;
            ambience.volume = MasterGain * BuildAmbienceGain;
            ambienceFilter = ambience.gameObject.AddComponent<AudioLowPassFilter>();
            ambienceFilter.cutoffFrequency = 170f;

            // Keep these clips one-shot and schedule the same offsets as the
            // Web Audio implementation.  The AudioSource volume is the 0.18
            // master gain; each Play call below supplies the per-tone gain.
            lockPrimaryClip = CreateTone("MagneticLockPrimary", 760f, 0.055f, Waveform.Square);
            lockSecondaryClip = CreateTone("MagneticLockSecondary", 1140f, 0.04f, Waveform.Sine);
            invalidClip = CreateTone("Invalid", 145f, 0.09f, Waveform.Sawtooth);
            deleteClip = CreateSweep("Release", 520f, 180f, 0.1f, Waveform.Triangle);
            warningClip = CreateTone("Warning", 540f, 0.07f, Waveform.Square);
            testPulseClip = CreateTone("TestPulse", 128f, 0.045f, Waveform.Square);
            breakNoiseClip = CreateNoise("BreakNoise", 0.34f);
            breakSweepClip = CreateSweep("BreakSweep", 115f, 42f, 0.32f, Waveform.Sawtooth);
            successPrimaryClip = CreateTone("SuccessPrimary", 392f, 0.12f, Waveform.Triangle);
            successSecondaryClip = CreateTone("SuccessSecondary", 523.25f, 0.12f, Waveform.Triangle);
            successFinalClip = CreateTone("SuccessFinal", 783.99f, 0.22f, Waveform.Triangle);
            ambience.clip = CreateTone("Ambience", 52f, 1f, Waveform.Sine, seamless: true);
            ambience.Play();
            SetMuted(startMuted);
        }

        public void SetMuted(bool value)
        {
            muted = value;
            if (effects != null) effects.mute = value;
            if (ambience != null) ambience.mute = value;
        }

        public void PlayLock()
        {
            Play(lockPrimaryClip, 0.12f);
            Play(lockSecondaryClip, 0.06f, 0.035f);
        }

        public void PlayInvalid() => Play(invalidClip, 0.08f);
        public void PlayDelete() => Play(deleteClip, 0.08f);

        public void PlayWarning()
        {
            Play(warningClip, 0.08f);
            Play(warningClip, 0.08f, 0.18f);
        }

        public void PlayBreak()
        {
            Play(breakNoiseClip, 0.32f);
            Play(breakSweepClip, 0.18f);
        }

        public void PlaySuccess()
        {
            Play(successPrimaryClip, 0.09f);
            Play(successSecondaryClip, 0.09f, 0.11f);
            Play(successFinalClip, 0.1f, 0.22f);
        }

        public void SetTestMode(bool testing)
        {
            SetMode(testing ? SkybridgeAudioMode.Test : SkybridgeAudioMode.Build);
        }

        public void SetSilentMode()
        {
            SetMode(SkybridgeAudioMode.Silent);
        }

        public void SetMode(SkybridgeAudioMode nextMode)
        {
            mode = nextMode;
            if (ambience == null) return;
            ambience.pitch = 1f;
            ambience.volume = nextMode switch
            {
                SkybridgeAudioMode.Test => MasterGain * TestAmbienceGain,
                SkybridgeAudioMode.Silent => 0f,
                _ => MasterGain * BuildAmbienceGain,
            };
            if (pulseRoutine != null)
            {
                StopCoroutine(pulseRoutine);
                pulseRoutine = null;
            }
            if (nextMode == SkybridgeAudioMode.Test) pulseRoutine = StartCoroutine(TestPulseRoutine());
        }

        private System.Collections.IEnumerator WarningRoutine()
        {
            // Kept for backwards compatibility with serialized scenes that
            // may still reference this method; warning playback is now
            // scheduled directly by PlayWarning for exact offsets.
            yield break;
        }

        private System.Collections.IEnumerator TestPulseRoutine()
        {
            while (mode == SkybridgeAudioMode.Test)
            {
                yield return new WaitForSecondsRealtime(0.52f);
                if (mode == SkybridgeAudioMode.Test) Play(testPulseClip, 0.018f);
            }
            pulseRoutine = null;
        }

        private void Play(AudioClip clip, float volume, float delaySeconds = 0f)
        {
            if (muted || effects == null || clip == null) return;
            if (delaySeconds <= 0f)
            {
                effects.PlayOneShot(clip, volume);
                return;
            }

            StartCoroutine(PlayAfterDelay(clip, volume, delaySeconds));
        }

        private System.Collections.IEnumerator PlayAfterDelay(AudioClip clip, float volume, float delaySeconds)
        {
            yield return new WaitForSecondsRealtime(delaySeconds);
            if (!muted && effects != null && clip != null) effects.PlayOneShot(clip, volume);
        }

        private AudioClip CreateTone(
            string clipName,
            float frequency,
            float duration,
            Waveform waveform,
            float amplitude = 1f,
            bool seamless = false)
        {
            const int sampleRate = 44100;
            var sampleCount = Mathf.Max(64, Mathf.RoundToInt(duration * sampleRate));
            var samples = new float[sampleCount];
            for (var index = 0; index < sampleCount; index++)
            {
                var t = index / (float)sampleRate;
                var normalizedPhase = Mathf.Repeat(t * frequency, 1f);
                var signal = WaveformSample(waveform, normalizedPhase);
                var envelope = seamless
                    ? 1f
                    : Mathf.Pow(0.001f, Mathf.Clamp01(t / Mathf.Max(0.001f, duration)));
                samples[index] = signal * amplitude * envelope;
            }
            return CreateClip(clipName, samples, sampleRate);
        }

        private AudioClip CreateSweep(
            string clipName,
            float startFrequency,
            float endFrequency,
            float duration,
            Waveform waveform,
            float amplitude = 1f)
        {
            const int sampleRate = 44100;
            var sampleCount = Mathf.Max(64, Mathf.RoundToInt(duration * sampleRate));
            var samples = new float[sampleCount];
            var logRatio = Mathf.Log(Mathf.Max(0.001f, endFrequency / Mathf.Max(0.001f, startFrequency)));
            var phase = 0f;
            for (var index = 0; index < sampleCount; index++)
            {
                var t = index / (float)sampleRate;
                var progress = Mathf.Clamp01(t / Mathf.Max(0.001f, duration));
                var frequency = startFrequency * Mathf.Exp(logRatio * progress);
                phase += frequency / sampleRate;
                phase = Mathf.Repeat(phase, 1f);
                var signal = WaveformSample(waveform, phase);
                var envelope = Mathf.Pow(0.001f, progress);
                samples[index] = signal * amplitude * envelope;
            }
            return CreateClip(clipName, samples, sampleRate);
        }

        private AudioClip CreateNoise(string clipName, float duration, float amplitude = 1f)
        {
            const int sampleRate = 44100;
            var sampleCount = Mathf.Max(64, Mathf.RoundToInt(duration * sampleRate));
            var samples = new float[sampleCount];
            var random = new System.Random(137);
            for (var index = 0; index < sampleCount; index++)
            {
                var phase = index / (float)sampleCount;
                var noise = (float)random.NextDouble() * 2f - 1f;
                samples[index] = noise * amplitude * Mathf.Pow(1f - phase, 2f);
            }
            return CreateClip(clipName, samples, sampleRate);
        }

        private static float WaveformSample(Waveform waveform, float phase)
        {
            switch (waveform)
            {
                case Waveform.Square:
                    return phase < 0.5f ? 1f : -1f;
                case Waveform.Triangle:
                    return 1f - 4f * Mathf.Abs(Mathf.Round(phase) - phase);
                case Waveform.Sawtooth:
                    return phase * 2f - 1f;
                default:
                    return Mathf.Sin(phase * Mathf.PI * 2f);
            }
        }

        private AudioClip CreateClip(string clipName, float[] samples, int sampleRate)
        {
            var clip = AudioClip.Create(clipName, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            generatedClips.Add(clip);
            return clip;
        }

        private void OnDestroy()
        {
            if (pulseRoutine != null) StopCoroutine(pulseRoutine);
            foreach (var clip in generatedClips)
            {
                if (clip != null) Destroy(clip);
            }
        }
    }
}
