using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Central CSV event logger (singleton) for reward collection and collectible expirations.
/// Creates one pair of CSV files per Play session (Editor) / app run (Build).
/// Public static wrapper API is allocation-light. Writes buffered lines, periodic flush.
/// </summary>
public class EventCsvLogger : MonoBehaviour
{
    // ---------------- Configuration (serialized) ----------------
    [Header("File Naming / Session")]
    [Tooltip("Folder name (relative) or absolute path if contains ':/'). Default absolute initial path is overriden by this if absolute.")] public string logsFolderName = @"D:\\Desarrollo\\ml-agents\\trabajo-de-fin-de-master\\results\\logs-csv"; // absolute default as per prompt
    [Tooltip("Base filename for rewards; session prefix added before it")] public string rewardsBaseFileName = "rewards_log.csv";
    [Tooltip("Base filename for expirations; session prefix added before it")] public string expirationsBaseFileName = "expirations_log.csv";
    [Tooltip("Add timestamp prefix yyyyMMdd_HHmmss to isolate sessions")] public bool useSessionTimestampPrefix = true;
    [Tooltip("If filename exists, append _01, _02, ...")] public bool addUniqueSuffixOnCollision = true;
    [Tooltip("Use UTC for timestamp prefix (recommended for multi-machine consistency)")] public bool includeUtcInPrefix = true;

    [Header("Buffer / Flush")] public int flushIntervalSeconds = 5; // periodic coroutine flush
    [Tooltip("Flush automatically when accumulated unsaved lines across files >= this")] public int bufferFlushCount = 64;

    [Header("Lifecycle / Conditions")]
    public bool autoCreateSingleton = true;
    public bool persistBetweenScenes = true;
    public bool logInEditor = true;
    public bool logInBuild = true;

    // ---------------- Runtime state (public readonly) ----------------
    [NonSerialized] public string sessionId;
    [NonSerialized] public DateTime sessionStartUtc;
    [NonSerialized] public string rewardsFilePath;
    [NonSerialized] public string expirationsFilePath;

    // ---------------- Internal ----------------
    private static EventCsvLogger s_Instance;
    private StreamWriter _rewardsWriter;
    private StreamWriter _expirationsWriter;
    private int _pendingLinesTotal;
    private readonly StringBuilder _lineBuilder = new StringBuilder(256);
    private float _nextFlushTime;
    private bool _initialized;
    private bool _disabledForPlatform; // respect logInEditor/logInBuild

    // Headers (exact order per spec)
    // Added group_reward_step (value of private Agent.m_GroupReward at logging time via reflection)
    private const string REWARDS_HEADER = "wall_time_iso,unix_time,episode,area_id,cycle_index,subcycle_index,agent_id,collectible_allowed_id,reward_amount,agent_pos_x,agent_pos_z,cumulative_reward_after,group_reward_step";
    private const string EXPIRATIONS_HEADER = "wall_time_iso,unix_time,episode,area_id,cycle_index,subcycle_index,collectible_allowed_id,collectible_pos_x,collectible_pos_z";

    // --------------- Public static API ---------------
    public static void LogReward(CompetitiveArea area, float rewardAmount)
    {
        // Minimal wrapper (legacy signature). Without agent we cannot provide full info; no-op if insufficient.
        if (area == null) return;
        EnsureInstance();
        if (!s_Instance.CanLog()) return;
        // This form expects caller to supply richer overload instead (Used internally from CollectibleGoal).
    }

    public static void LogReward(CompetitiveArea area, CompetitiveAgent agent, CollectibleGoal collectible, float rewardAmount, float cumulativeAfter)
    {
        if (area == null || agent == null || collectible == null) return;
        EnsureInstance();
        if (!s_Instance.CanLog()) return;
        s_Instance.WriteReward(area, agent, collectible, rewardAmount, cumulativeAfter);
    }

    public static void LogExpiration(CompetitiveArea area, CollectibleGoal collectible)
    {
        if (area == null || collectible == null) return;
        EnsureInstance();
        if (!s_Instance.CanLog()) return;
        s_Instance.WriteExpiration(area, collectible);
    }

    public static void BeginEpisode(int episodeIndex)
    {
        EnsureInstance();
        // nothing to persist immediately; episode index written on events
    }

    public static void Flush()
    {
        if (s_Instance != null) s_Instance.DoFlush();
    }

    public static void Close()
    {
        if (s_Instance != null) s_Instance.InternalClose();
    }

    private static void EnsureInstance()
    {
        if (s_Instance != null) return;
        if (!Application.isPlaying) return; // only during play mode
        if (!FindExistingInstance())
        {
            var go = new GameObject("__EventCsvLogger");
            s_Instance = go.AddComponent<EventCsvLogger>();
        }
    }

    private static bool FindExistingInstance()
    {
        s_Instance = FindObjectOfType<EventCsvLogger>();
        return s_Instance != null;
    }

    private bool CanLog() => !_disabledForPlatform && _initialized;

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(gameObject); // enforce singleton
            return;
        }
        s_Instance = this;
        if (persistBetweenScenes) DontDestroyOnLoad(gameObject);

#if UNITY_EDITOR
        if (!logInEditor) { _disabledForPlatform = true; return; }
#else
        if (!logInBuild) { _disabledForPlatform = true; return; }
#endif
        InitializeSession();
    }

    private void InitializeSession()
    {
        sessionStartUtc = DateTime.UtcNow;
        sessionId = BuildSessionId();
        string baseFolder = ResolveFolderPath();
        Directory.CreateDirectory(baseFolder);
        rewardsFilePath = ComposeUniquePath(baseFolder, rewardsBaseFileName);
        expirationsFilePath = ComposeUniquePath(baseFolder, expirationsBaseFileName);
        try
        {
            _rewardsWriter = new StreamWriter(new FileStream(rewardsFilePath, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8, 4096, false);
            _expirationsWriter = new StreamWriter(new FileStream(expirationsFilePath, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8, 4096, false);
            if (new FileInfo(rewardsFilePath).Length == 0)
                _rewardsWriter.WriteLine(REWARDS_HEADER);
            if (new FileInfo(expirationsFilePath).Length == 0)
                _expirationsWriter.WriteLine(EXPIRATIONS_HEADER);
            _initialized = true;
            if (flushIntervalSeconds > 0) _nextFlushTime = Time.realtimeSinceStartup + flushIntervalSeconds;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"EventCsvLogger failed to initialize: {ex.Message}. Disabling logging.");
            _disabledForPlatform = true;
        }
    }

    private string BuildSessionId()
    {
        DateTime now = includeUtcInPrefix ? DateTime.UtcNow : DateTime.Now;
        if (!useSessionTimestampPrefix) return "session";
        return now.ToString("yyyyMMdd_HHmmss");
    }

    private string ResolveFolderPath()
    {
        string folder = logsFolderName;
        if (string.IsNullOrWhiteSpace(folder)) folder = "logs";
        bool isAbsolute = folder.Contains(":\\") || folder.Contains(":/") || Path.IsPathRooted(folder);
        if (!isAbsolute)
        {
            // Place relative folder under project root (parent of Assets) if possible, otherwise persistentDataPath
            try
            {
                string projectRoot = Application.dataPath;
                if (!string.IsNullOrEmpty(projectRoot))
                {
                    projectRoot = Directory.GetParent(projectRoot)?.FullName ?? projectRoot;
                    return Path.Combine(projectRoot, folder);
                }
            }
            catch { }
            return Path.Combine(Application.persistentDataPath, folder);
        }
        // Validate absolute; fallback if permission issues later
        return folder;
    }

    private string ComposeUniquePath(string folder, string baseName)
    {
        string prefix = useSessionTimestampPrefix ? sessionId + "_" : string.Empty;
        string fileName = prefix + baseName;
        string full = Path.Combine(folder, fileName);
        if (!addUniqueSuffixOnCollision || !File.Exists(full)) return full;
        int idx = 1;
        string nameNoExt = Path.GetFileNameWithoutExtension(baseName);
        string ext = Path.GetExtension(baseName);
        while (File.Exists(full) && idx < 100)
        {
            fileName = prefix + nameNoExt + "_" + idx.ToString("00") + ext;
            full = Path.Combine(folder, fileName);
            idx++;
        }
        return full;
    }

    private void Update()
    {
        if (!_initialized || _disabledForPlatform) return;
        if (flushIntervalSeconds > 0 && Time.realtimeSinceStartup >= _nextFlushTime)
        {
            DoFlush();
            _nextFlushTime = Time.realtimeSinceStartup + flushIntervalSeconds;
        }
    }

    // Reflection cache for private Agent.m_GroupReward (per-step group reward). Safe for Editor/Player.
    private static readonly System.Reflection.FieldInfo s_AgentGroupRewardField = typeof(Unity.MLAgents.Agent)
        .GetField("m_GroupReward", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private void WriteReward(CompetitiveArea area, CompetitiveAgent agent, CollectibleGoal collectible, float rewardAmount, float cumulativeAfter)
    {
        var nowUtc = DateTime.UtcNow;
        double unix = (nowUtc - DateTime.UnixEpoch).TotalMilliseconds / 1000.0;
        // Obtain current step group reward via reflection (0 if field missing or null agent)
        float groupRewardStep = 0f;
        try
        {
            if (agent != null && s_AgentGroupRewardField != null)
            {
                object val = s_AgentGroupRewardField.GetValue(agent);
                if (val is float f) groupRewardStep = f;
            }
        }
        catch { /* ignore reflection failures to avoid impacting logging */ }
        _lineBuilder.Length = 0;
        _lineBuilder.Append(nowUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)).Append(',')
            .Append(unix.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
            .Append(area != null ? area.GetEpisodeIndex().ToString() : "0").Append(',')
            .Append(area != null ? area.areaId.ToString() : "0").Append(',')
            .Append(area != null ? area.GetCyclesCompleted().ToString() : "0").Append(',')
            .Append(area != null ? area.GetCurrentSubcycleIndex().ToString() : "0").Append(',')
            .Append(agent.agentId).Append(',')
            .Append(collectible != null ? collectible.allowedAgentId.ToString() : "0").Append(',')
            .Append(rewardAmount.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(agent.transform.position.x.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
            .Append(agent.transform.position.z.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
            .Append(cumulativeAfter.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(groupRewardStep.ToString(CultureInfo.InvariantCulture));
        _rewardsWriter.WriteLine(_lineBuilder.ToString());
        _pendingLinesTotal++;
        AutoFlushIfNeeded();
    }

    private void WriteExpiration(CompetitiveArea area, CollectibleGoal collectible)
    {
        var nowUtc = DateTime.UtcNow;
        double unix = (nowUtc - DateTime.UnixEpoch).TotalMilliseconds / 1000.0;
        Vector3 pos = collectible.transform.position;
        _lineBuilder.Length = 0;
        _lineBuilder.Append(nowUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)).Append(',')
            .Append(unix.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
            .Append(area != null ? area.GetEpisodeIndex().ToString() : "0").Append(',')
            .Append(area != null ? area.areaId.ToString() : "0").Append(',')
            .Append(area != null ? area.GetCyclesCompleted().ToString() : "0").Append(',')
            .Append(area != null ? area.GetCurrentSubcycleIndex().ToString() : "0").Append(',')
            .Append(collectible.allowedAgentId).Append(',')
            .Append(pos.x.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
            .Append(pos.z.ToString("F4", CultureInfo.InvariantCulture));
        _expirationsWriter.WriteLine(_lineBuilder.ToString());
        _pendingLinesTotal++;
        AutoFlushIfNeeded();
    }

    private void AutoFlushIfNeeded()
    {
        if (bufferFlushCount > 0 && _pendingLinesTotal >= bufferFlushCount)
        {
            DoFlush();
        }
    }

    private void DoFlush()
    {
        try
        {
            _rewardsWriter?.Flush();
            _expirationsWriter?.Flush();
            _pendingLinesTotal = 0;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"EventCsvLogger flush failed: {ex.Message}");
        }
    }

    private void InternalClose()
    {
        DoFlush();
        _rewardsWriter?.Dispose();
        _expirationsWriter?.Dispose();
        _rewardsWriter = null;
        _expirationsWriter = null;
        _initialized = false;
    }

    private void OnDestroy()
    {
        if (s_Instance == this)
        {
            InternalClose();
            s_Instance = null;
        }
    }

    private void OnApplicationQuit()
    {
        try
        {
            InternalClose();
        }
        catch { }
    }
}
