using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace GameLab.Week4
{
    /// <summary>사운드 담당자가 클립과 재생 규칙을 Inspector에서 조정하는 데이터다.</summary>
    [Serializable]
    public sealed class SoundEntry
    {
        [SerializeField] private SoundEventId eventId;
        [SerializeField] private SoundBus bus = SoundBus.Sfx;
        [SerializeField] private AudioClip[] clips = Array.Empty<AudioClip>();
        [SerializeField] private AudioMixerGroup output;
        [Range(0f, 1f)] [SerializeField] private float volume = 1f;
        [SerializeField] private Vector2 pitchRange = Vector2.one;
        [Range(0f, 1f)] [SerializeField] private float spatialBlend;
        [Min(0.01f)] [SerializeField] private float minDistance = 1f;
        [Min(0.01f)] [SerializeField] private float maxDistance = 18f;
        [Min(0)] [SerializeField] private int maxSimultaneous = 4;
        [Min(0f)] [SerializeField] private float cooldown;
        [SerializeField] private bool loop;
        [Min(0f)] [SerializeField] private float fadeIn;
        [Min(0f)] [SerializeField] private float fadeOut = 0.12f;

        public SoundEventId EventId => eventId;
        public SoundBus Bus => bus;
        public IReadOnlyList<AudioClip> Clips => clips;
        public AudioMixerGroup Output => output;
        public float Volume => volume;
        public float MinPitch => Mathf.Min(pitchRange.x, pitchRange.y);
        public float MaxPitch => Mathf.Max(pitchRange.x, pitchRange.y);
        public float SpatialBlend => spatialBlend;
        public float MinDistance => minDistance;
        public float MaxDistance => Mathf.Max(minDistance, maxDistance);
        public int MaxSimultaneous => maxSimultaneous;
        public float Cooldown => cooldown;
        public bool Loop => loop;
        public float FadeIn => fadeIn;
        public float FadeOut => fadeOut;

        public bool HasPlayableClip
        {
            get
            {
                if (clips == null) return false;
                foreach (AudioClip clip in clips)
                {
                    if (clip != null) return true;
                }

                return false;
            }
        }
    }

    [CreateAssetMenu(fileName = "GameSoundCatalog", menuName = "GameLab/Audio/Sound Catalog")]
    public sealed class SoundCatalog : ScriptableObject
    {
        [Header("Mixer")]
        [SerializeField] private AudioMixer audioMixer;
        [SerializeField] private string masterVolumeParameter = "MasterVolume";
        [SerializeField] private string bgmVolumeParameter = "BgmVolume";
        [SerializeField] private string ambienceVolumeParameter = "AmbienceVolume";
        [SerializeField] private string sfxVolumeParameter = "SfxVolume";
        [SerializeField] private string uiVolumeParameter = "UiVolume";

        [Header("Sound Events")]
        [SerializeField] private List<SoundEntry> entries = new();

        private Dictionary<SoundEventId, SoundEntry> lookup;

        public AudioMixer AudioMixer => audioMixer;
        public IReadOnlyList<SoundEntry> Entries => entries;

        public bool TryGetEntry(SoundEventId eventId, out SoundEntry entry)
        {
            EnsureLookup();
            return lookup.TryGetValue(eventId, out entry);
        }

        public string GetVolumeParameter(SoundBus bus)
        {
            return bus switch
            {
                SoundBus.Master => masterVolumeParameter,
                SoundBus.Bgm => bgmVolumeParameter,
                SoundBus.Ambience => ambienceVolumeParameter,
                SoundBus.Ui => uiVolumeParameter,
                _ => sfxVolumeParameter
            };
        }

        private void OnEnable()
        {
            lookup = null;
        }

        private void OnValidate()
        {
            lookup = null;
        }

        private void EnsureLookup()
        {
            if (lookup != null) return;

            lookup = new Dictionary<SoundEventId, SoundEntry>();
            foreach (SoundEntry entry in entries)
            {
                if (entry == null || entry.EventId == SoundEventId.None) continue;
                lookup[entry.EventId] = entry;
            }
        }
    }
}
