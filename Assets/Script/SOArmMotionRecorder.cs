using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 직접교시(Direct Teaching) 녹화/재생.
    ///
    /// 【쓰는 법】
    ///   1. 수동모드 ON  → 실물 토크가 풀려 손으로 밀 수 있다
    ///   2. 녹화 시작    → 폴링으로 들어오는 실물 각도를 시간과 함께 쌓는다
    ///   3. 손으로 동작을 보여준다
    ///   4. 녹화 정지 → 수동모드 OFF (토크 복구)
    ///   5. 재생        → 쌓아둔 궤적을 원래 속도로 되돌려 준다
    ///
    /// 【왜 "실물 각도"를 녹화하나 (슬라이더 값이 아니라)】
    ///   손으로 미는 동안 슬라이더는 움직이지 않는다. 실제로 일어난 일은
    ///   폴링으로 읽은 각도뿐이다. 그래서 OnAnglesReceived 를 원본으로 삼는다.
    ///   덕분에 수동모드가 아니어도(슬라이더 조작 중에도) 그대로 녹화된다.
    ///
    /// 【왜 시간을 같이 저장하나】
    ///   폴링 주기는 일정하지 않다 (네트워크/타임아웃으로 흔들린다).
    ///   프레임 번호로 재생하면 속도가 원본과 달라지므로 시각을 함께 남긴다.
    ///
    /// 【안전】
    ///   · 재생은 반드시 SOArmManager 를 거친다 → 속도 제한/소프트 리밋/비상정지가 그대로 적용된다
    ///   · 비상정지가 걸리면 재생을 즉시 멈춘다
    ///   · 수동모드가 켜진 채로는 재생하지 않는다 (토크가 풀려 있어 명령이 먹지 않는다)
    /// </summary>
    public class SOArmMotionRecorder : MonoBehaviour
    {
        [Serializable]
        public class Frame
        {
            public float t;             // 녹화 시작 기준 경과 시간(초)
            public float[] angles;      // 관절 각도(도)
            public float gripper;       // 그리퍼 열림 %
        }

        [Serializable]
        public class Clip
        {
            public string name = "motion";
            public string robot = "robot1";
            public List<Frame> frames = new List<Frame>();

            public float Duration => frames.Count > 0 ? frames[frames.Count - 1].t : 0f;
        }

        [Header("대상 로봇")]
        public SOArmManager target;

        [Header("녹화")]
        [Tooltip("이 각도(도) 미만으로만 움직였으면 프레임을 버린다.\n" +
                 "모터 노이즈로 파일이 부풀고 재생이 떨리는 것을 막는다.\n" +
                 "0 이면 전부 저장.")]
        [Range(0f, 5f)]
        public float minDeltaDeg = 0.3f;

        [Tooltip("최대 녹화 시간(초). 넘으면 자동 정지 — 메모리 폭주 방지.")]
        public float maxRecordSeconds = 120f;

        [Header("재생")]
        [Tooltip("1 = 녹화 속도 그대로, 0.5 = 절반 속도.\n" +
                 "실물이 못 따라오면 낮춰라. 재생이 원본보다 빠를 이유는 없다.")]
        [Range(0.1f, 2f)]
        public float playbackSpeed = 1f;

        [Tooltip("재생을 반복한다. 시연용.")]
        public bool loop = false;

        [Header("상태 (읽기 전용)")]
        public bool isRecording;
        public bool isPlaying;

        /// <summary>현재 클립. 녹화하면 덮어쓴다.</summary>
        public Clip clip = new Clip();

        public int FrameCount => clip?.frames?.Count ?? 0;
        public float ClipDuration => clip?.Duration ?? 0f;
        /// <summary>재생 진행률 0~1. 재생 중이 아니면 0.</summary>
        public float PlayProgress { get; private set; }

        public event Action<string> OnRecorderEvent;

        float recordStartTime;
        float[] lastRecorded;
        Coroutine playRoutine;

        // ── 녹화 ────────────────────────────────────────────────
        // 구독은 녹화할 때만 건다.
        // OnEnable 에서 걸면 런타임에 AddComponent 로 붙였을 때
        // target 이 아직 비어 있어 구독이 조용히 실패한다.
        SOArmRealController subscribed;

        void Subscribe()
        {
            var r = target != null ? target.real : null;
            if (subscribed == r) return;
            Unsubscribe();
            if (r != null) { r.OnAnglesReceived += HandleAngles; subscribed = r; }
        }

        void Unsubscribe()
        {
            if (subscribed != null) subscribed.OnAnglesReceived -= HandleAngles;
            subscribed = null;
        }

        void OnDisable() => Unsubscribe();

        public void StartRecording()
        {
            if (target == null)
            {
                Warn("대상 로봇이 지정되지 않았다");
                return;
            }
            if (isPlaying) StopPlayback();

            clip = new Clip
            {
                name = $"motion_{DateTime.Now:yyyyMMdd_HHmmss}",
                robot = target.real != null ? target.real.robotServerMode : "sim",
            };
            lastRecorded = null;
            recordStartTime = Time.time;
            Subscribe();
            isRecording = true;

            // 실물이 안 붙어 있으면 폴링이 안 오므로 시뮬 각도로 녹화한다.
            Notify(target.real != null && target.real.IsConnected
                ? "🔴 녹화 시작 — 실물 각도 기록"
                : "🔴 녹화 시작 — 실물 미연결이라 시뮬 각도를 기록한다");
        }

        public void StopRecording()
        {
            if (!isRecording) return;
            isRecording = false;
            Notify($"⏹ 녹화 정지 — {FrameCount} 프레임, {ClipDuration:F1}초");
        }

        /// <summary>실물 폴링이 각도를 보내올 때마다 호출 (녹화의 원본).</summary>
        void HandleAngles(Dictionary<string, float> angles)
        {
            if (!isRecording || target == null) return;

            var joints = target.real != null ? target.real.joints : null;
            if (joints == null) return;

            float[] cur = new float[joints.Length];
            for (int i = 0; i < joints.Length; i++)
                cur[i] = angles.TryGetValue(joints[i].motorName, out float d)
                       ? d : target.real.GetJointAngle(i);

            Capture(cur, target.GetGripperPercent());
        }

        void Update()
        {
            if (!isRecording) return;

            if (Time.time - recordStartTime > maxRecordSeconds)
            {
                Warn($"최대 녹화 시간 {maxRecordSeconds:F0}초 도달 — 자동 정지");
                StopRecording();
                return;
            }

            // 실물이 없으면 폴링 이벤트가 오지 않는다. 시뮬 각도로 대신 채운다.
            bool realFeeding = target != null && target.real != null && target.real.IsConnected;
            if (!realFeeding && target != null)
            {
                int n = target.JointCount;
                float[] cur = new float[n];
                for (int i = 0; i < n; i++) cur[i] = target.GetJointAngle(i);
                Capture(cur, target.GetGripperPercent());
            }
        }

        void Capture(float[] cur, float gripper)
        {
            // 노이즈 프레임 걸러내기 — 첫 프레임은 무조건 남긴다(시작 자세이므로)
            if (lastRecorded != null && minDeltaDeg > 0f)
            {
                float maxDelta = 0f;
                for (int i = 0; i < cur.Length && i < lastRecorded.Length; i++)
                    maxDelta = Mathf.Max(maxDelta, Mathf.Abs(cur[i] - lastRecorded[i]));
                if (maxDelta < minDeltaDeg) return;
            }

            clip.frames.Add(new Frame
            {
                t = Time.time - recordStartTime,
                angles = (float[])cur.Clone(),
                gripper = gripper,
            });
            lastRecorded = (float[])cur.Clone();
        }

        // ── 재생 ────────────────────────────────────────────────
        public void StartPlayback()
        {
            if (target == null) { Warn("대상 로봇이 지정되지 않았다"); return; }
            if (FrameCount < 2) { Warn("재생할 궤적이 없다 — 먼저 녹화하라"); return; }
            if (isRecording) StopRecording();

            // 수동모드에서는 토크가 풀려 있어 명령이 먹지 않는다.
            // 조용히 실패하면 "재생했는데 안 움직인다" 로만 보이므로 먼저 끈다.
            if (target.IsTeachMode)
            {
                Notify("수동모드가 켜져 있어 먼저 해제한다 (토크가 풀려 있으면 재생이 안 먹는다)");
                target.SetTeachMode(false);
            }

            if (playRoutine != null) StopCoroutine(playRoutine);
            playRoutine = StartCoroutine(PlayRoutine());
        }

        public void StopPlayback()
        {
            if (playRoutine != null) { StopCoroutine(playRoutine); playRoutine = null; }
            if (isPlaying) Notify("⏹ 재생 정지");
            isPlaying = false;
            PlayProgress = 0f;
        }

        IEnumerator PlayRoutine()
        {
            isPlaying = true;
            Notify($"▶ 재생 시작 — {FrameCount} 프레임, {ClipDuration / Mathf.Max(0.01f, playbackSpeed):F1}초");

            do
            {
                // 첫 프레임 자세로 먼저 이동하고 자리를 잡을 시간을 준다.
                // 바로 궤적을 흘리면 현재 자세와 시작점 사이를 실물이 못 따라와
                // 처음 몇 초가 통째로 어긋난다.
                ApplyFrame(clip.frames[0]);
                yield return new WaitForSeconds(1.0f);

                float t0 = Time.time;
                int idx = 0;
                while (idx < clip.frames.Count)
                {
                    if (target != null && target.real != null && target.real.EmergencyStopped)
                    {
                        Warn("🛑 비상정지 감지 — 재생 중단");
                        StopPlayback();
                        yield break;
                    }

                    float elapsed = (Time.time - t0) * playbackSpeed;

                    // 시간이 지난 프레임은 건너뛴다 (재생이 밀려도 궤적 전체 길이는 유지)
                    while (idx < clip.frames.Count - 1 && clip.frames[idx + 1].t <= elapsed) idx++;

                    ApplyFrame(clip.frames[idx]);
                    PlayProgress = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, ClipDuration));

                    if (elapsed >= ClipDuration) break;
                    yield return null;
                }
            } while (loop && isPlaying);

            Notify("✅ 재생 완료");
            isPlaying = false;
            PlayProgress = 0f;
            playRoutine = null;
        }

        void ApplyFrame(Frame f)
        {
            if (target == null || f?.angles == null) return;
            // SOArmManager 를 거쳐야 속도 제한 / 소프트 리밋 / 비상정지가 모두 적용된다.
            target.SetAllJointTargets(f.angles);
            target.SetGripperTarget(f.gripper);
        }

        // ── 저장 / 불러오기 ──────────────────────────────────────
        /// <summary>Application.persistentDataPath 아래 Motions 폴더.</summary>
        public static string MotionDir => Path.Combine(Application.persistentDataPath, "Motions");

        public string SaveToFile()
        {
            if (FrameCount == 0) { Warn("저장할 궤적이 없다"); return null; }
            Directory.CreateDirectory(MotionDir);
            string path = Path.Combine(MotionDir, clip.name + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(clip, true), Encoding.UTF8);
            Notify($"💾 저장 완료 — {path}");
            return path;
        }

        public bool LoadFromFile(string path)
        {
            if (!File.Exists(path)) { Warn($"파일 없음: {path}"); return false; }
            try
            {
                clip = JsonUtility.FromJson<Clip>(File.ReadAllText(path, Encoding.UTF8));
                Notify($"📂 불러오기 완료 — {clip.name}, {FrameCount} 프레임, {ClipDuration:F1}초");
                return true;
            }
            catch (Exception e)
            {
                Warn($"불러오기 실패: {e.Message}");
                return false;
            }
        }

        /// <summary>저장된 궤적 파일 목록 (최신순).</summary>
        public static string[] ListSavedFiles()
        {
            if (!Directory.Exists(MotionDir)) return Array.Empty<string>();
            var files = Directory.GetFiles(MotionDir, "*.json");
            Array.Sort(files, (a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
            return files;
        }

        public void ClearClip()
        {
            StopPlayback();
            clip = new Clip();
            lastRecorded = null;
            Notify("궤적 지움");
        }

        void Notify(string msg)
        {
            Debug.Log($"[Recorder] {msg}");
            OnRecorderEvent?.Invoke(msg);
        }

        void Warn(string msg)
        {
            Debug.LogWarning($"[Recorder] {msg}");
            OnRecorderEvent?.Invoke("⚠ " + msg);
        }
    }
}
