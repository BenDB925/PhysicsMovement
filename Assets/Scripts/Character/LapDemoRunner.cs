using System.Collections;
using System.IO;
using PhysicsDrivenMovement.Core;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Drop-in demo component for Arena_01 that runs the real PlayerRagdoll prefab
    /// around the Top Gear test circuit. Attach to any GameObject in the scene,
    /// assign the PlayerRagdoll prefab, hit Play, and watch.
    ///
    /// The ghost driver feeds directional input via PlayerMovement.SetMoveInputForTest
    /// each FixedUpdate, advancing through the same 24-waypoint circuit used by
    /// LapCourseTests. Lap time and personal best are displayed on screen and saved
    /// to Logs/lap-pb.txt.
    ///
    /// Collaborators: <see cref="PlayerMovement"/>, <see cref="CharacterState"/>,
    /// <see cref="CameraFollow"/>.
    /// </summary>
    public class LapDemoRunner : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────

        [Header("Prefab")]
        [Tooltip("Assign the PlayerRagdoll prefab here.")]
        [SerializeField] private GameObject _playerRagdollPrefab;

        [Header("Spawn")]
        [Tooltip("Where to spawn the character. Defaults to the first waypoint. " +
                 "Adjust Y if Arena_01 floor is not at Y=0.")]
        [SerializeField] private Vector3 _spawnOffset = new Vector3(0f, 1.1f, 0f);

        [Header("Timing")]
        [Tooltip("Seconds to let the character settle before the lap starts.")]
        [SerializeField, Range(1f, 5f)] private float _settleSeconds = 2f;

        [Header("Display")]
        [SerializeField] private bool _showOnScreenHUD = true;

        // ── Course geometry (mirrors LapCourseTests) ──────────────────────────────

        private static readonly Vector3[] CourseWaypoints =
        {
            // (1) Start
            new Vector3( 0f,  0f,  0f),

            // (2) Long straight — 15 m north
            new Vector3( 0f,  0f,  5f),
            new Vector3( 0f,  0f, 10f),
            new Vector3( 0f,  0f, 15f),

            // (3) Tight 180° hairpin
            new Vector3( 4f,  0f, 15f),
            new Vector3( 8f,  0f, 15f),
            new Vector3(12f,  0f, 15f),
            new Vector3(16f,  0f, 15f),
            new Vector3(16f,  0f, 10f),

            // (4) Chicane — left-right-left
            new Vector3(12f,  0f,  8f),
            new Vector3(16f,  0f,  4f),
            new Vector3(12f,  0f,  0f),

            // (5) Slalom — 5 alternating gates
            new Vector3( 8f,  0f, -4f),
            new Vector3( 4f,  0f, -8f),
            new Vector3( 8f,  0f,-12f),
            new Vector3( 4f,  0f,-16f),
            new Vector3( 8f,  0f,-20f),

            // (6) S-bend
            new Vector3( 4f,  0f,-24f),
            new Vector3( 0f,  0f,-20f),
            new Vector3(-4f,  0f,-16f),
            new Vector3( 0f,  0f,-12f),

            // (7) Return to start
            new Vector3( 0f,  0f, -8f),
            new Vector3( 0f,  0f, -4f),
            new Vector3( 0f,  0f,  0f),
        };

        private static float GetGateRadius(int index)
        {
            if (index <= 3) return 1.5f;   // start + straight
            if (index <= 20) return 1.0f;   // corners, slalom, S-bend
            return 1.5f;                    // return leg
        }

        // ── Runtime state ─────────────────────────────────────────────────────────

        private GameObject     _instance;
        private PlayerMovement    _movement;
        private CharacterState    _characterState;
        private BalanceController _balance;
        private Transform         _hipsTransform;

        private int   _currentWaypoint;
        private int   _fallCount;
        private int   _gatesHit;
        private float _lapStartTime;
        private float _lapTime;
        private bool  _lapComplete;
        private bool  _lapRunning;

        // Navigation suspension state — entered on Fallen/GettingUp, exited once stable.
        private bool _navSuspended  = false;
        private int  _stableFrames  = 0;
        private const int  StableFramesRequired = 10;
        private const float StableTiltDeg       = 15f;

        private string _statusLine  = "Settling…";
        private string _pbLine      = "";
        private string _lastLapLine = "";

        // ── Unity lifecycle ───────────────────────────────────────────────────────

        private void Start()
        {
            if (_playerRagdollPrefab == null)
            {
                Debug.LogError("[LapDemoRunner] No PlayerRagdoll prefab assigned! " +
                               "Drag the PlayerRagdoll prefab into the Inspector.", this);
                enabled = false;
                return;
            }

            LoadPB();
            StartCoroutine(RunLap());
        }

        private void FixedUpdate()
        {
            if (!_lapRunning || _movement == null) return;

            // Ghost driver: suspend navigation on Fallen/GettingUp.
            // Wait for "good state" (Standing + tilt < StableTiltDeg for StableFramesRequired
            // consecutive frames) before resuming, so we don't immediately push the character
            // over again while BC is still restoring upright posture.
            bool fallen    = _characterState != null &&
                             (_characterState.CurrentState == CharacterStateType.Fallen ||
                              _characterState.CurrentState == CharacterStateType.GettingUp);

            if (fallen)
            {
                _navSuspended = true;
                _stableFrames = 0;
            }

            if (_navSuspended)
            {
                bool standing = _characterState != null &&
                                _characterState.CurrentState == CharacterStateType.Standing;
                float tilt    = _balance != null ? _balance.TiltAngleDeg : 90f;
                bool stable   = standing && tilt < StableTiltDeg;

                if (stable) _stableFrames++;
                else        _stableFrames = 0;

                if (_stableFrames >= StableFramesRequired)
                {
                    _navSuspended = false;
                    _stableFrames = 0;
                }
                else
                {
                    _movement.SetMoveInputForTest(Vector2.zero);
                    return;
                }
            }

            // Point toward current waypoint on XZ plane.
            // Lookahead blend: as we get within LookaheadRadius of the current gate,
            // start blending toward the next gate's direction. This prevents the sharp
            // input snap at high-angle corners (gate 9 is a 108° turn).
            const float LookaheadRadius = 6f;
            Vector3 hipsPos   = _hipsTransform.position;
            Vector3 target    = CourseWaypoints[_currentWaypoint];
            Vector3 toTarget  = new Vector3(target.x - hipsPos.x, 0f, target.z - hipsPos.z);
            float   dist      = toTarget.magnitude;

            if (dist > 0.01f)
            {
                Vector3 dir = toTarget / dist;

                // Blend toward next waypoint direction when approaching current gate.
                int nextIdx = _currentWaypoint + 1;
                if (nextIdx < CourseWaypoints.Length && dist < LookaheadRadius)
                {
                    Vector3 nextTarget  = CourseWaypoints[nextIdx];
                    Vector3 toNext      = new Vector3(nextTarget.x - target.x, 0f, nextTarget.z - target.z);
                    if (toNext.sqrMagnitude > 0.01f)
                    {
                        float blend = 1f - (dist / LookaheadRadius); // 0 at LookaheadRadius, 1 at gate centre
                        dir = Vector3.Slerp(dir, toNext.normalized, blend * 0.6f).normalized;
                    }
                }

                _movement.SetMoveInputForTest(new Vector2(dir.x, dir.z));
            }

            // Check gate hit.
            if (dist < GetGateRadius(_currentWaypoint))
            {
                _gatesHit++;
                _currentWaypoint++;

                if (_currentWaypoint >= CourseWaypoints.Length)
                {
                    _lapTime     = Time.time - _lapStartTime;
                    _lapComplete = true;
                    _lapRunning  = false;
                    _movement.SetMoveInputForTest(Vector2.zero);
                }
            }
        }

        // ── Lap coroutine ─────────────────────────────────────────────────────────

        private IEnumerator RunLap()
        {
            // STEP 1: Spawn the real prefab at the start gate.
            Vector3 spawnPos = CourseWaypoints[0] + _spawnOffset;
            _instance        = Instantiate(_playerRagdollPrefab, spawnPos, Quaternion.identity);

            _hipsTransform  = _instance.transform;
            _movement       = _instance.GetComponentInChildren<PlayerMovement>();
            _characterState = _instance.GetComponentInChildren<CharacterState>();
            _balance        = _instance.GetComponentInChildren<BalanceController>();

            if (_movement == null)
            {
                Debug.LogError("[LapDemoRunner] PlayerMovement not found on prefab or any child. " +
                               "Ensure PlayerRagdoll has PlayerMovement on the Hips GO.");
                yield break;
            }

            // STEP 2: Wire CameraFollow to the new instance if one exists in the scene.
            CameraFollow cam = FindFirstObjectByType<CameraFollow>();
            if (cam != null)
            {
                var targetField = typeof(CameraFollow)
                    .GetField("_target",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);
                targetField?.SetValue(cam, _hipsTransform);
            }

            // STEP 3: Settle — let the character stand up naturally.
            // Also null the camera reference so the ghost driver gets raw world-space input.
            // The camera still follows for visual purposes but must not rotate the input vector —
            // ghost driver waypoints are world-space coordinates, not camera-relative directions.
            _statusLine = $"Settling ({_settleSeconds:F0}s)…";
            float settleEnd = Time.time + _settleSeconds;
            bool cameraNulled = false;
            while (Time.time < settleEnd)
            {
                yield return new WaitForFixedUpdate();
                // Null camera after first physics frame so Start() has already cached it.
                if (!cameraNulled && _movement != null)
                {
                    _movement.SetCameraForTest(null);
                    cameraNulled = true;
                }
            }

            // STEP 4: Start lap.
            _currentWaypoint = 1;   // skip waypoint 0 (spawn position)
            _gatesHit        = 1;   // spawn counts as gate 0
            _fallCount       = 0;
            _lapStartTime    = Time.time;
            _lapComplete     = false;
            _lapRunning      = true;
            _statusLine      = "LAP RUNNING…";

            CharacterStateType prevState = CharacterStateType.Standing;

            // STEP 5: Wait for lap to complete or timeout.
            float lapTimeout = Time.time + 60f;
            while (!_lapComplete && Time.time < lapTimeout)
            {
                // Edge-detect falls.
                if (_characterState != null)
                {
                    CharacterStateType state = _characterState.CurrentState;
                    if (state == CharacterStateType.Fallen &&
                        prevState != CharacterStateType.Fallen)
                    {
                        _fallCount++;
                        Debug.Log($"[LapDemoRunner] Fall #{_fallCount} at gate {_currentWaypoint}, " +
                                  $"elapsed {Time.time - _lapStartTime:F1}s");
                    }
                    prevState = state;
                }

                _statusLine = $"Gate {_currentWaypoint}/{CourseWaypoints.Length - 1}  ·  " +
                              $"Falls: {_fallCount}  ·  " +
                              $"{Time.time - _lapStartTime:F1}s" +
                              (_characterState?.CurrentState == CharacterStateType.Fallen
                                  ? "  ·  [FALLEN — waiting to get up]" : "");

                yield return new WaitForFixedUpdate();
            }

            // STEP 6: Lap finished — record result.
            _lapRunning  = false;
            _lapComplete = true;

            if (_movement != null)
            {
                _movement.SetMoveInputForTest(Vector2.zero);
            }

            if (_currentWaypoint >= CourseWaypoints.Length)
            {
                _lastLapLine = $"LAP COMPLETE  ·  {_lapTime:F2}s  ·  Falls: {_fallCount}  ·  " +
                               $"Gates: {_gatesHit}/{CourseWaypoints.Length}";
                Debug.Log($"[LapDemoRunner] {_lastLapLine}");
                SavePBIfFaster(_lapTime, _fallCount, _gatesHit);
            }
            else
            {
                float elapsed = Time.time - _lapStartTime;
                _lastLapLine = $"DNF  ·  {elapsed:F2}s  ·  Falls: {_fallCount}  ·  " +
                               $"Gates: {_gatesHit}/{CourseWaypoints.Length} (did not finish)";
                Debug.LogWarning($"[LapDemoRunner] {_lastLapLine}");
            }

            _statusLine = _lastLapLine;
        }

        // ── PB persistence ────────────────────────────────────────────────────────

        private string PBFilePath =>
            Path.Combine(Application.dataPath, "..", "Logs", "lap-pb.txt");

        private float _pbTime = float.MaxValue;

        private void LoadPB()
        {
            string path = PBFilePath;
            if (!File.Exists(path))
            {
                _pbLine = "PB: --";
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length > 0)
                {
                    _pbLine = lines[0];
                    // Parse PB time from first line: "PB: 23.65s | ..."
                    string[] parts = lines[0].Split('|');
                    if (parts.Length > 0)
                    {
                        string timePart = parts[0].Replace("PB:", "").Replace("s", "").Trim();
                        float.TryParse(timePart, out _pbTime);
                    }
                }
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[LapDemoRunner] Could not read PB file: {e.Message}");
            }
        }

        private void SavePBIfFaster(float lapTime, int falls, int gates)
        {
            if (falls > 0 || lapTime >= _pbTime) return;   // PB requires clean lap

            string path = PBFilePath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string date   = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                string pbLine = $"PB: {lapTime:F2}s | Falls: {falls} | " +
                                $"Gates: {gates}/{CourseWaypoints.Length} | Date: {date}";

                string previousLine = File.Exists(path)
                    ? File.ReadAllLines(path)[0]
                    : "Previous: --";

                File.WriteAllText(path, $"{pbLine}\n{previousLine}\n");

                _pbLine = pbLine;
                _pbTime = lapTime;
                Debug.Log($"[LapDemoRunner] 🏆 New PB! {pbLine}");
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[LapDemoRunner] Could not save PB: {e.Message}");
            }
        }

        // ── On-screen HUD ─────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_showOnScreenHUD) return;

            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                fontSize  = 18,
                alignment = TextAnchor.UpperLeft,
            };
            style.normal.textColor = Color.white;

            string hud = $"🏎  TOP GEAR TEST TRACK\n" +
                         $"{_statusLine}\n" +
                         $"{_pbLine}";

            GUI.Box(new Rect(10, 10, 480, 90), hud, style);

            // Draw waypoint gizmos in screen space (rough overlay).
        }

        // ── Scene gizmos ──────────────────────────────────────────────────────────

        private void OnDrawGizmos()
        {
            for (int i = 0; i < CourseWaypoints.Length; i++)
            {
                Vector3 wp = CourseWaypoints[i];
                Gizmos.color = i == _currentWaypoint ? Color.yellow :
                               i < _currentWaypoint  ? Color.green  : Color.cyan;
                Gizmos.DrawWireSphere(wp, GetGateRadius(i));

                if (i < CourseWaypoints.Length - 1)
                {
                    Gizmos.color = Color.white;
                    Gizmos.DrawLine(wp, CourseWaypoints[i + 1]);
                }
            }
        }
    }
}
