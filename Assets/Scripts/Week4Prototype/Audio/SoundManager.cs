using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GameLab.Week4
{
    /// <summary>
    /// 모든 BGM, 환경음, UI, 3D 효과음을 하나의 진입점에서 재생한다.
    /// Scene에 직접 배치하지 않아도 플레이 시작 전에 한 개만 자동 생성된다.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class SoundManager : MonoBehaviour
    {
        private const string CatalogResourcePath = "Audio/GameSoundCatalog";
        private const string VolumePreferencePrefix = "Week4.Audio.Volume.";

        private sealed class Voice
        {
            public AudioSource Source;
            public SoundEventId EventId;
            public SoundBus Bus;
            public int Token;
            public int ClipIndex = -1;
            public float BaseVolume;
            public float StartedAt;
            public float ExpectedEndTime;
            public Coroutine FadeCoroutine;
        }

        private static SoundManager instance;

        [SerializeField] private SoundCatalog catalog;
        [Min(1)] [SerializeField] private int initialSfxPoolSize = 12;
        [Min(1)] [SerializeField] private int maximumSfxPoolSize = 32;
        [SerializeField] private bool playDefaultBgm = true;
        [SerializeField] private bool playDefaultAmbience = true;
        [Tooltip("Catalog에 빈 Event가 있을 때 Console에 경고를 남깁니다.")]
        [SerializeField] private bool logMissingEntries;

        private readonly List<Voice> sfxVoices = new();
        private readonly Dictionary<SoundEventId, float> lastPlayTimes = new();
        private readonly Dictionary<SoundEventId, int> lastClipIndices = new();
        private readonly HashSet<SoundEventId> missingEventWarnings = new();
        private readonly float[] busVolumes = { 1f, 1f, 1f, 1f, 1f };

        private Voice bgmVoice;
        private Voice ambienceVoice;
        private Transform sourceRoot;
        private int nextToken = 1;
        private bool warnedMissingCatalog;

        public static SoundManager Instance
        {
            get
            {
                EnsureInstance();
                return instance;
            }
        }

        public static bool HasInstance => instance != null;
        public SoundCatalog Catalog => catalog;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureInstance();
        }

        /// <summary>효과음과 반복음을 동일한 API로 요청한다.</summary>
        public static SoundHandle Play(SoundEventId eventId, Vector3? worldPosition = null)
        {
            return Instance.PlayInternal(eventId, worldPosition);
        }

        /// <summary>핸들이 가리키는 재생을 Catalog의 페이드 아웃 시간으로 멈춘다.</summary>
        public static void Stop(SoundHandle handle)
        {
            if (instance == null || !handle.IsValid) return;
            instance.StopInternal(handle.Token, null);
        }

        /// <summary>핸들이 가리키는 재생을 지정한 시간 동안 줄여 멈춘다.</summary>
        public static void Stop(SoundHandle handle, float fadeOut)
        {
            if (instance == null || !handle.IsValid) return;
            instance.StopInternal(handle.Token, Mathf.Max(0f, fadeOut));
        }

        public static void SetVolume(SoundBus bus, float linearVolume, bool save = true)
        {
            Instance.SetBusVolume(bus, linearVolume, save);
        }

        private static void EnsureInstance()
        {
            if (instance != null) return;

            instance = FindFirstObjectByType<SoundManager>();
            if (instance != null) return;

            GameObject managerObject = new("SoundManager");
            instance = managerObject.AddComponent<SoundManager>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            if (catalog == null)
            {
                catalog = Resources.Load<SoundCatalog>(CatalogResourcePath);
            }

            CreateSources();
            LoadSavedVolumes();
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            ReleaseCompletedVoice(bgmVoice, now);
            ReleaseCompletedVoice(ambienceVoice, now);
            foreach (Voice voice in sfxVoices)
            {
                ReleaseCompletedVoice(voice, now);
            }
        }

        private void Start()
        {
            if (playDefaultBgm) PlayInternal(SoundEventId.MainBgm, null);
            if (playDefaultAmbience) PlayInternal(SoundEventId.RoomAmbience, null);
        }

        private void ReleaseCompletedVoice(Voice voice, float now)
        {
            if (voice == null || voice.Token == 0 || voice.Source.loop) return;
            if (now >= voice.ExpectedEndTime || !voice.Source.isPlaying)
            {
                ReleaseVoice(voice);
            }
        }

        private void CreateSources()
        {
            if (sourceRoot != null) return;

            GameObject root = new("AudioSources");
            root.transform.SetParent(transform, false);
            sourceRoot = root.transform;
            bgmVoice = CreateVoice("BGM");
            ambienceVoice = CreateVoice("Ambience");

            int poolSize = Mathf.Clamp(initialSfxPoolSize, 1, maximumSfxPoolSize);
            for (int index = 0; index < poolSize; index++)
            {
                sfxVoices.Add(CreateVoice($"SFX_{index + 1:00}"));
            }
        }

        private Voice CreateVoice(string sourceName)
        {
            GameObject sourceObject = new(sourceName);
            sourceObject.transform.SetParent(sourceRoot, false);
            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            return new Voice { Source = source };
        }

        private SoundHandle PlayInternal(SoundEventId eventId, Vector3? worldPosition)
        {
            if (eventId == SoundEventId.None) return SoundHandle.Invalid;
            if (catalog == null)
            {
                if (logMissingEntries && !warnedMissingCatalog)
                {
                    warnedMissingCatalog = true;
                    Debug.LogWarning(
                        $"SoundManager: Resources/{CatalogResourcePath}.asset을 찾지 못했습니다. " +
                        "Catalog을 연결하면 재생이 활성화됩니다.",
                        this);
                }

                return SoundHandle.Invalid;
            }

            if (!catalog.TryGetEntry(eventId, out SoundEntry entry) || !entry.HasPlayableClip)
            {
                if (logMissingEntries && missingEventWarnings.Add(eventId))
                {
                    Debug.LogWarning($"SoundManager: {eventId} 클립이 Catalog에 없어 재생을 건너뜁니다.", this);
                }

                return SoundHandle.Invalid;
            }

            float now = Time.unscaledTime;
            if (lastPlayTimes.TryGetValue(eventId, out float lastTime) && now - lastTime < entry.Cooldown)
            {
                return SoundHandle.Invalid;
            }

            if (entry.MaxSimultaneous > 0 && CountActiveVoices(eventId) >= entry.MaxSimultaneous)
            {
                return SoundHandle.Invalid;
            }

            int clipIndex = SelectClipIndex(entry);
            if (clipIndex < 0) return SoundHandle.Invalid;

            AudioClip clip = entry.Clips[clipIndex];
            Voice voice = entry.Bus switch
            {
                SoundBus.Bgm => bgmVoice,
                SoundBus.Ambience => ambienceVoice,
                _ => AcquireSfxVoice()
            };
            if (voice == null) return SoundHandle.Invalid;

            lastPlayTimes[eventId] = now;
            lastClipIndices[eventId] = clipIndex;
            StartVoice(voice, eventId, entry, clip, clipIndex, worldPosition);
            return new SoundHandle(voice.Token);
        }

        private void StartVoice(
            Voice voice,
            SoundEventId eventId,
            SoundEntry entry,
            AudioClip clip,
            int clipIndex,
            Vector3? worldPosition)
        {
            StopVoiceImmediate(voice);
            voice.Token = nextToken++;
            if (nextToken <= 0) nextToken = 1;
            voice.EventId = eventId;
            voice.Bus = entry.Bus;
            voice.ClipIndex = clipIndex;
            voice.BaseVolume = entry.Volume;
            voice.StartedAt = Time.unscaledTime;

            AudioSource source = voice.Source;
            source.clip = clip;
            source.outputAudioMixerGroup = entry.Output;
            source.loop = entry.Loop;
            source.pitch = Random.Range(entry.MinPitch, entry.MaxPitch);
            source.spatialBlend = worldPosition.HasValue ? entry.SpatialBlend : 0f;
            source.minDistance = entry.MinDistance;
            source.maxDistance = entry.MaxDistance;
            source.transform.position = worldPosition ?? transform.position;

            float targetVolume = GetSourceVolume(entry.Bus, entry.Volume);
            source.volume = entry.FadeIn > 0f ? 0f : targetVolume;
            source.Play();
            voice.ExpectedEndTime = entry.Loop
                ? float.PositiveInfinity
                : Time.unscaledTime + clip.length / Mathf.Max(0.01f, Mathf.Abs(source.pitch));

            if (entry.FadeIn > 0f)
            {
                voice.FadeCoroutine = StartCoroutine(FadeVolume(voice, targetVolume, entry.FadeIn, false));
            }
        }

        private Voice AcquireSfxVoice()
        {
            foreach (Voice voice in sfxVoices)
            {
                if (voice.Token == 0) return voice;
            }

            if (sfxVoices.Count < maximumSfxPoolSize)
            {
                Voice added = CreateVoice($"SFX_{sfxVoices.Count + 1:00}");
                sfxVoices.Add(added);
                return added;
            }

            Voice oldest = null;
            foreach (Voice voice in sfxVoices)
            {
                if (voice.Source.loop) continue;
                if (oldest == null || voice.StartedAt < oldest.StartedAt) oldest = voice;
            }

            return oldest;
        }

        private int SelectClipIndex(SoundEntry entry)
        {
            var validIndices = new List<int>();
            for (int index = 0; index < entry.Clips.Count; index++)
            {
                if (entry.Clips[index] != null) validIndices.Add(index);
            }

            if (validIndices.Count == 0) return -1;
            if (validIndices.Count == 1) return validIndices[0];

            lastClipIndices.TryGetValue(entry.EventId, out int previousIndex);
            int selected = validIndices[Random.Range(0, validIndices.Count)];
            for (int attempt = 0; attempt < 4 && selected == previousIndex; attempt++)
            {
                selected = validIndices[Random.Range(0, validIndices.Count)];
            }

            return selected;
        }

        private int CountActiveVoices(SoundEventId eventId)
        {
            int count = 0;
            if (bgmVoice.Token != 0 && bgmVoice.EventId == eventId) count++;
            if (ambienceVoice.Token != 0 && ambienceVoice.EventId == eventId) count++;
            foreach (Voice voice in sfxVoices)
            {
                if (voice.Token != 0 && voice.EventId == eventId) count++;
            }

            return count;
        }

        private void StopInternal(int token, float? fadeOverride)
        {
            Voice voice = FindVoice(token);
            if (voice == null) return;

            float fadeOut = fadeOverride ?? GetEntryFadeOut(voice.EventId);
            if (fadeOut <= 0f)
            {
                StopVoiceImmediate(voice);
                return;
            }

            if (voice.FadeCoroutine != null) StopCoroutine(voice.FadeCoroutine);
            voice.FadeCoroutine = StartCoroutine(FadeVolume(voice, 0f, fadeOut, true));
        }

        private IEnumerator FadeVolume(Voice voice, float target, float duration, bool stopAfterFade)
        {
            float start = voice.Source.volume;
            float elapsed = 0f;
            while (elapsed < duration && voice.Token != 0)
            {
                elapsed += Time.unscaledDeltaTime;
                voice.Source.volume = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            if (voice.Token != 0)
            {
                voice.Source.volume = target;
                if (stopAfterFade)
                {
                    voice.FadeCoroutine = null;
                    ReleaseVoice(voice);
                    yield break;
                }
            }

            voice.FadeCoroutine = null;
        }

        private Voice FindVoice(int token)
        {
            if (bgmVoice.Token == token) return bgmVoice;
            if (ambienceVoice.Token == token) return ambienceVoice;
            foreach (Voice voice in sfxVoices)
            {
                if (voice.Token == token) return voice;
            }

            return null;
        }

        private float GetEntryFadeOut(SoundEventId eventId)
        {
            return catalog != null && catalog.TryGetEntry(eventId, out SoundEntry entry)
                ? entry.FadeOut
                : 0f;
        }

        private void ReleaseVoice(Voice voice)
        {
            if (voice.FadeCoroutine != null)
            {
                StopCoroutine(voice.FadeCoroutine);
                voice.FadeCoroutine = null;
            }

            voice.Source.Stop();
            voice.Source.clip = null;
            voice.Token = 0;
            voice.EventId = SoundEventId.None;
        }

        private void StopVoiceImmediate(Voice voice)
        {
            if (voice == null) return;
            if (voice.FadeCoroutine != null)
            {
                StopCoroutine(voice.FadeCoroutine);
                voice.FadeCoroutine = null;
            }

            ReleaseVoice(voice);
        }

        private void LoadSavedVolumes()
        {
            foreach (SoundBus bus in System.Enum.GetValues(typeof(SoundBus)))
            {
                float saved = PlayerPrefs.GetFloat(VolumePreferencePrefix + bus, 1f);
                SetBusVolume(bus, saved, false);
            }
        }

        private void SetBusVolume(SoundBus bus, float linearVolume, bool save)
        {
            int index = (int)bus;
            if (index < 0 || index >= busVolumes.Length) return;

            float clamped = Mathf.Clamp01(linearVolume);
            busVolumes[index] = clamped;
            if (save)
            {
                PlayerPrefs.SetFloat(VolumePreferencePrefix + bus, clamped);
                PlayerPrefs.Save();
            }

            string parameter = catalog != null ? catalog.GetVolumeParameter(bus) : null;
            if (catalog != null && catalog.AudioMixer != null && !string.IsNullOrWhiteSpace(parameter))
            {
                float decibels = clamped <= 0.0001f ? -80f : Mathf.Log10(clamped) * 20f;
                catalog.AudioMixer.SetFloat(parameter, decibels);
                return;
            }

            ApplySourceBusVolume(bgmVoice, bus);
            ApplySourceBusVolume(ambienceVoice, bus);
            foreach (Voice voice in sfxVoices) ApplySourceBusVolume(voice, bus);
        }

        private void ApplySourceBusVolume(Voice voice, SoundBus changedBus)
        {
            if (voice == null || voice.Token == 0) return;
            if (changedBus != SoundBus.Master && voice.Bus != changedBus) return;
            voice.Source.volume = GetSourceVolume(voice.Bus, voice.BaseVolume);
        }

        private float GetSourceVolume(SoundBus bus, float baseVolume)
        {
            if (catalog != null && catalog.AudioMixer != null) return baseVolume;
            return baseVolume * busVolumes[(int)SoundBus.Master] * busVolumes[(int)bus];
        }
    }
}
