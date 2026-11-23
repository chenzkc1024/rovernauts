using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rovernauts
{
    /// <summary>
    /// Mixes the rover motor and synthesized one-shot cues. Audio clips in Resources/Audio
    /// can replace individual cues by name.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager I { get; private set; }

        const int SampleRate = 22050;
        const float MasterVolume = 0.62f;
        AudioSource motor;
        enum Wave { Sine, Square, Saw, Triangle }

        readonly List<AudioClip> generated = new List<AudioClip>();
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        AudioSource[] voices;
        int nextVoice;

        void Awake()
        {
            I = this;
            voices = new AudioSource[24];
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
                voices[i].spatialBlend = 0f;
            }

            motor = gameObject.AddComponent<AudioSource>();
            motor.clip = MakeMotorLoop();
            generated.Add(motor.clip);
            motor.loop = true;
            motor.volume = 0f;
            motor.Play();

            float[] arpeggio = { 523f, 659f, 784f, 1047f };
            Register("shoot", 0.15f, t => 170f + 1050f * Mathf.Exp(-t * 4.5f), Wave.Triangle, 0.38f, 2.2f);
            Register("enemy_shot", 0.18f, t => Mathf.Lerp(520f, 180f, t), Wave.Triangle, 0.1f, 1.2f);
            Register("hit", 0.11f, t => Mathf.Lerp(920f, 370f, t), Wave.Triangle, 0.62f, 2.8f);
            Register("hurt", 0.2f, t => Mathf.Lerp(220f, 80f, t), Wave.Square, 0.35f, 1f);
            Register("explode", 0.73f, t => Mathf.Lerp(110f, 35f, t), Wave.Triangle, 0.64f, 2f);
            Register("boss", 1.3f, t => 70f + 18f * Mathf.Sin(t * 40f), Wave.Saw, 0.15f, 0.6f);
            Register("dash", 0.3f, t => Mathf.Lerp(250f, 1000f, t), Wave.Saw, 0.1f, 1f);
            Register("upgrade", 0.45f, t => arpeggio[Mathf.Min(3, (int)(t * 4f))], Wave.Square, 0f, 0.5f);
            Register("alarm", 0.7f, t => ((int)(t * 6f)) % 2 == 0 ? 620f : 820f, Wave.Square, 0f, 0.2f);
            Register("meteor", 0.6f, t => Mathf.Lerp(1800f, 200f, t), Wave.Sine, 0.25f, 0.8f);
            Register("spawn", 0.25f, t => Mathf.Lerp(200f, 600f, t), Wave.Sine, 0.1f, 1f);
            Register("victory", 1.45f, t => arpeggio[Mathf.Min(3, (int)(t * 7f))] * 0.5f, Wave.Triangle, 0f, 0.8f);
            Register("bite", 0.1f, t => Mathf.Lerp(300f, 120f, t), Wave.Square, 0.4f, 1.5f);
        }

        void Update()
        {
            var gm = GameManager.I;
            float speed = gm != null && gm.State == GameState.Playing
                ? Mathf.Clamp01(gm.Rover.Velocity.magnitude / 22f) : 0f;
            float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 8f);
            motor.volume = Mathf.Lerp(motor.volume, speed * 0.18f, blend);
            motor.pitch = Mathf.Lerp(motor.pitch, 0.8f + speed * 0.77f, blend);
        }

        static AudioClip MakeMotorLoop()
        {
            float[] samples = new float[SampleRate];
            for (int i = 0; i < samples.Length; i++)
            {
                float phase = i * 2f * Mathf.PI / SampleRate;
                float revolution = Mathf.Sin(phase * 47f + 0.38f * Mathf.Sin(phase * 7f));
                samples[i] = revolution * 0.55f + Mathf.Sin(phase * 94f) * 0.16f + Mathf.Sin(phase * 141f) * 0.08f;
            }
            var clip = AudioClip.Create("Rover traction motor", SampleRate, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            foreach (var clip in generated) Destroy(clip);
        }

        public static void Play(string name, float volume = 1f, float pitchJitter = 0.08f)
        {
            if (I == null || !I.clips.TryGetValue(name, out var clip)) return;
            // Throttle so 30 enemies dying on one frame doesn't clip the mixer.
            float now = Time.unscaledTime;
            if (I.lastPlayed.TryGetValue(name, out float last) && now - last < 0.035f) return;
            I.lastPlayed[name] = now;
            var gm = GameManager.I;
            if (gm != null && gm.Mode == CrewMode.LanDriver) gm.Lan.AddCue(name, volume);

            var source = I.voices[I.nextVoice];
            I.nextVoice = (I.nextVoice + 1) % I.voices.Length;
            source.pitch = 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
            source.PlayOneShot(clip, volume * MasterVolume);
        }

        void Register(string name, float duration, Func<float, float> frequency, Wave wave, float noise, float decay)
        {
            var custom = Resources.Load<AudioClip>("Audio/" + name);
            if (custom == null)
            {
                custom = Synthesize(name, duration, frequency, wave, noise, decay);
                generated.Add(custom);
            }
            clips[name] = custom;
        }

        static AudioClip Synthesize(string name, float duration, Func<float, float> frequency, Wave wave, float noise, float decay)
        {
            int count = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[count];
            var rng = new System.Random(count);
            float filteredNoise = 0f;
            double phase = 0;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                phase += frequency(t) / SampleRate;
                float p = (float)(phase - Math.Floor(phase));
                float s = wave switch
                {
                    Wave.Square => p < 0.5f ? 1f : -1f,
                    Wave.Saw => p * 2f - 1f,
                    Wave.Triangle => 1f - 4f * Mathf.Abs(p - 0.5f),
                    _ => Mathf.Sin(p * Mathf.PI * 2f),
                };
                filteredNoise = Mathf.Lerp(filteredNoise, (float)rng.NextDouble() * 2f - 1f, name == "explode" ? 0.14f : 0.65f);
                s = Mathf.Lerp(s, filteredNoise, noise);
                float attack = Mathf.Clamp01(i / (SampleRate * 0.004f));
                float envelope = attack * Mathf.Pow(1f - t, decay);
                data[i] = s * envelope * 0.5f;
            }
            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
