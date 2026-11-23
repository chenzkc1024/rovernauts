using UnityEngine;

namespace Rovernauts
{
    /// <summary>Two synchronized original score layers, crossfaded as threats enter or leave the arena.</summary>
    public class MissionScore : MonoBehaviour
    {
        const int Rate = 22050;
        const float Beat = 0.56f;
        const int Beats = 32;
        const string MuteKey = "MusicMuted";
        static bool? sessionMuted;
        AudioSource ambience, pulse;

        public bool Muted
        {
            get => sessionMuted ?? (PlayerPrefs.GetInt(MuteKey, 0) != 0);
            set
            {
                sessionMuted = value;
                try
                {
                    PlayerPrefs.SetInt(MuteKey, value ? 1 : 0);
                }
                catch (PlayerPrefsException)
                {
                    Debug.LogWarning("Could not save the music setting. It applies for this session.");
                }
            }
        }

        void Start()
        {
            ambience = CreateTrack("Survey station", false);
            pulse = CreateTrack("Contact", true);
            double start = AudioSettings.dspTime + 0.12;
            ambience.PlayScheduled(start);
            pulse.PlayScheduled(start);
        }

        AudioSource CreateTrack(string name, bool combat)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.volume = 0f;
            source.clip = Compose(name, combat);
            return source;
        }

        void Update()
        {
            if (ambience == null || GameManager.I == null) return;
            var gm = GameManager.I;
            bool playing = gm.State == GameState.Playing;
            bool danger = playing && (EnemyBase.All.Count > 0 || gm.Events.Active != MoonEvent.None);
            float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 2f);
            float ambientLevel = gm.State == GameState.Paused ? 0.12f : 0.32f;
            ambience.volume = Mathf.Lerp(ambience.volume, Muted ? 0f : ambientLevel, blend);
            pulse.volume = Mathf.Lerp(pulse.volume, !Muted && danger ? 0.38f : 0f, blend);
        }

        static AudioClip Compose(string name, bool combat)
        {
            var rhythm = new[] { 0, 7, 12, 7, 3, 7, 10, 12 };
            var roots = new[] { 43, 39, 46, 41 };
            var rng = new System.Random(486);
            int length = Mathf.RoundToInt(Beats * Beat * Rate);
            float[] samples = new float[length];
            for (int i = 0; i < length; i++)
            {
                float beat = i / (Rate * Beat);
                int bar = Mathf.Min(3, (int)(beat / 8f));
                float chordTime = (beat - bar * 8f) * Beat;
                float root = Frequency(roots[bar]);
                float sound;
                if (combat)
                {
                    float step = beat * 2f;
                    int note = (int)step;
                    float age = (step - note) * Beat * 0.5f;
                    float noteHz = Frequency(roots[bar] - 12 + rhythm[note % rhythm.Length]);
                    float synth = (Tone(noteHz, age) + 0.28f * Tone(noteHz * 2f, age)) * Mathf.Exp(-age * 12f);
                    float kickAge = (beat % 2f) * Beat;
                    float kick = Mathf.Sin(2f * Mathf.PI * (48f * kickAge + 7f * (1f - Mathf.Exp(-kickAge * 22f)))) *
                        Mathf.Exp(-kickAge * 15f);
                    float noise = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-age * 58f);
                    sound = 0.17f * synth * Mathf.Clamp01(age * 140f) + 0.19f * kick + 0.025f * noise;
                }
                else
                {
                    float release = 8f * Beat - chordTime;
                    float swell = Mathf.SmoothStep(0f, 1f, Mathf.Min(chordTime, release) / 0.65f);
                    float pad = Tone(root, chordTime) + 0.52f * Tone(root * 1.4983f, chordTime) +
                        0.3f * Tone(root * 2.003f, chordTime);
                    float noteAge = ((beat + 1f) % 2f) * Beat;
                    float bellHz = Frequency(roots[bar] + 24 + rhythm[((int)beat / 2) % rhythm.Length]);
                    float bell = (Tone(bellHz, noteAge) + 0.24f * Tone(bellHz * 2.008f, noteAge)) *
                        Mathf.Exp(-noteAge * 6f) * Mathf.Clamp01(noteAge * 75f);
                    sound = swell * (0.11f * pad + 0.065f * bell);
                }
                float seam = Mathf.Clamp01(Mathf.Min(i, length - 1 - i) / (Rate * 0.015f));
                samples[i] = sound * seam;
            }
            var clip = AudioClip.Create(name, length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static float Frequency(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);
        static float Tone(float frequency, float age) => Mathf.Sin(2f * Mathf.PI * frequency * age);

        void OnDestroy()
        {
            if (ambience != null) Destroy(ambience.clip);
            if (pulse != null) Destroy(pulse.clip);
        }
    }
}
