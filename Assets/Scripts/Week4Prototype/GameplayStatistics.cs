using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameLab.Week4
{
    /// <summary>
    /// 퍼즐 플레이 기록을 씬 전환 뒤 엔딩까지 보존한다.
    /// </summary>
    [DefaultExecutionOrder(-1100)]
    public sealed class GameplayStatistics : MonoBehaviour
    {
        private const string GameplaySceneName = "KHP_Puzzles";

        private static GameplayStatistics instance;

        private int rotationCount;
        private int undoCount;
        private int restartCount;
        private double runStartedAt;
        private double completedElapsedSeconds;
        private bool runStarted;
        private bool runActive;

        public readonly struct Snapshot
        {
            public Snapshot(int rotations, int undos, int restarts, double elapsedSeconds)
            {
                Rotations = rotations;
                Undos = undos;
                Restarts = restarts;
                ElapsedSeconds = elapsedSeconds;
            }

            public int Rotations { get; }
            public int Undos { get; }
            public int Restarts { get; }
            public double ElapsedSeconds { get; }
        }

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

        public static void RecordRotation()
        {
            GameplayStatistics statistics = EnsureInstance();
            if (statistics.runActive) statistics.rotationCount++;
        }

        public static void RecordUndo()
        {
            GameplayStatistics statistics = EnsureInstance();
            if (statistics.runActive) statistics.undoCount++;
        }

        public static void RecordRestart()
        {
            GameplayStatistics statistics = EnsureInstance();
            if (statistics.runActive) statistics.restartCount++;
        }

        /// <summary>마지막 KeyCube가 놓인 순간의 플레이 시간을 확정한다.</summary>
        public static void CompleteRun()
        {
            GameplayStatistics statistics = EnsureInstance();
            statistics.CompleteActiveRun();
        }

        public static Snapshot GetSnapshot()
        {
            GameplayStatistics statistics = EnsureInstance();
            double elapsed = statistics.runActive
                ? Time.realtimeSinceStartupAsDouble - statistics.runStartedAt
                : statistics.completedElapsedSeconds;
            return new Snapshot(
                statistics.rotationCount,
                statistics.undoCount,
                statistics.restartCount,
                System.Math.Max(0d, elapsed));
        }

        private static GameplayStatistics EnsureInstance()
        {
            if (instance != null) return instance;

            instance = FindFirstObjectByType<GameplayStatistics>();
            if (instance != null) return instance;

            GameObject statisticsObject = new("GameplayStatistics");
            instance = statisticsObject.AddComponent<GameplayStatistics>();
            return instance;
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
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (instance != this) return;

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            instance = null;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == GameplaySceneName)
            {
                BeginRun();
                return;
            }

            if (mode == LoadSceneMode.Single)
            {
                CompleteActiveRun();
            }
        }

        private void BeginRun()
        {
            rotationCount = 0;
            undoCount = 0;
            restartCount = 0;
            completedElapsedSeconds = 0d;
            runStartedAt = Time.realtimeSinceStartupAsDouble;
            runStarted = true;
            runActive = true;
        }

        private void CompleteActiveRun()
        {
            if (!runStarted || !runActive) return;

            completedElapsedSeconds = System.Math.Max(
                0d,
                Time.realtimeSinceStartupAsDouble - runStartedAt);
            runActive = false;
        }
    }
}
