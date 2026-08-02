using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// Record/Play 핵심 로직.
    /// 프로젝트 관리, 스텝 조작, JSON 저장/로드, 재생.
    /// </summary>
    public class RecordManager : MonoBehaviour
    {
        [Header("매니저 연결")]
        public SOArmDualManager dualManager;

        [Header("재생 상태 (읽기 전용)")]
        [SerializeField] private bool isPlaying = false;
        [SerializeField] private int currentStepIndex = -1;
        [SerializeField] private string statusMessage = "Idle";

        // ── 현재 진행 중인 프로젝트 ──
        public RecordProject CurrentProject { get; private set; }

        public bool IsPlaying => isPlaying;
        public int CurrentStepIndex => currentStepIndex;
        public string StatusMessage => statusMessage;

        // ── 재생 코루틴 핸들 (정지용) ──
        private Coroutine playbackCoroutine;

        // ── 저장 폴더 경로 ──
        // 관제 화면이 "어디에 저장되는지"를 보여줘야 해서 공개로 바꿨다.
        // 저장하고 나서 파일을 못 찾는 일이 실제로 잦다.
        public string RecordingsFolder
        {
            get
            {
                // 프로젝트 폴더 바로 아래 Recordings/
                string folder = Path.Combine(Application.dataPath, "..", "Recordings");
                if (!Directory.Exists(folder))
                    Directory.CreateDirectory(folder);
                return folder;
            }
        }

        void Awake()
        {
            // 시작 시 빈 프로젝트 생성
            CurrentProject = RecordProject.NewProject("Untitled");
        }

        void Start()
        {
            if (dualManager == null)
                dualManager = FindAnyObjectByType<SOArmDualManager>();
        }

        // ════════════════════════════════════════
        //                프로젝트 관리
        // ════════════════════════════════════════

        public void NewProject(string name = "Untitled")
        {
            StopPlayback();
            CurrentProject = RecordProject.NewProject(name);
            statusMessage = $"새 프로젝트: {name}";
            Debug.Log($"[Record] {statusMessage}");
        }

        public void SetProjectName(string name)
        {
            if (CurrentProject == null) return;
            CurrentProject.projectName = name;
            CurrentProject.Touch();
        }

        // ════════════════════════════════════════
        //                스텝 추가
        // ════════════════════════════════════════

        /// <summary>한 로봇의 현재 자세를 스텝으로 추가</summary>
        public void AddMotionStep(string target, int velocity, int acceleration)
        {
            if (CurrentProject == null) return;
            if (dualManager == null) { Debug.LogWarning("[Record] dualManager null"); return; }

            var wp = new Waypoint();
            wp.type = "motion";
            wp.target = target;  // "robot1" / "robot2" / "both"
            wp.velocity = velocity;
            wp.acceleration = acceleration;
            wp.delayAfter = 1.0f;   // 동작 사이 기본 텀

            // 현재 슬라이더값(시뮬 목표값)을 캡처
            if (target == "robot1" || target == "both")
            {
                wp.joints = CaptureJoints(dualManager.robot1);
                wp.gripper = CaptureGripper(dualManager.robot1);
            }
            if (target == "robot2" || target == "both")
            {
                wp.joints2 = CaptureJoints(dualManager.robot2);
                wp.gripper2 = CaptureGripper(dualManager.robot2);
            }

            // 안 쓰는 쪽도 비워 둔다
            if (target == "robot1") { wp.joints2 = new float[6]; wp.gripper2 = 50f; }
            if (target == "robot2") { wp.joints = new float[6]; wp.gripper = 50f; }

            wp.name = $"{target} 자세";

            CurrentProject.waypoints.Add(wp);
            CurrentProject.RenumberSteps();
            CurrentProject.Touch();
            statusMessage = $"✚ Step {wp.stepNumber} 추가 ({target})";
            Debug.Log($"[Record] {statusMessage}");
        }

        /// <summary>대기 스텝 추가</summary>
        public void AddWaitStep(float seconds)
        {
            if (CurrentProject == null) return;
            var wp = new Waypoint();
            wp.type = "wait";
            wp.duration = seconds;
            wp.name = $"대기 {seconds:F1}초";
            CurrentProject.waypoints.Add(wp);
            CurrentProject.RenumberSteps();
            CurrentProject.Touch();
            statusMessage = $"⏱ Wait {seconds}s 추가";
            Debug.Log($"[Record] {statusMessage}");
        }

        /// <summary>반복 시작 스텝 추가</summary>
        public void AddLoopStartStep(int count)
        {
            if (CurrentProject == null) return;
            var wp = new Waypoint();
            wp.type = "loop_start";
            wp.loopCount = count;
            wp.name = $"반복 시작 ({count}회)";
            CurrentProject.waypoints.Add(wp);
            CurrentProject.RenumberSteps();
            CurrentProject.Touch();
            statusMessage = $"🔁 Loop start ({count}회) 추가";
            Debug.Log($"[Record] {statusMessage}");
        }

        /// <summary>반복 끝 스텝 추가</summary>
        public void AddLoopEndStep()
        {
            if (CurrentProject == null) return;
            var wp = new Waypoint();
            wp.type = "loop_end";
            wp.name = "반복 끝";
            CurrentProject.waypoints.Add(wp);
            CurrentProject.RenumberSteps();
            CurrentProject.Touch();
            statusMessage = "🔁 Loop end 추가";
            Debug.Log($"[Record] {statusMessage}");
        }

        // ════════════════════════════════════════
        //                스텝 조작
        // ════════════════════════════════════════

        public void RemoveStep(int index)
        {
            if (CurrentProject == null) return;
            if (index < 0 || index >= CurrentProject.waypoints.Count) return;
            CurrentProject.waypoints.RemoveAt(index);
            CurrentProject.RenumberSteps();
            CurrentProject.Touch();
            statusMessage = $"🗑 Step 삭제";
            Debug.Log($"[Record] {statusMessage}");
        }

        public void MoveStepUp(int index)
        {
            if (CurrentProject == null) return;
            if (index <= 0 || index >= CurrentProject.waypoints.Count) return;
            var temp = CurrentProject.waypoints[index];
            CurrentProject.waypoints[index] = CurrentProject.waypoints[index - 1];
            CurrentProject.waypoints[index - 1] = temp;
            CurrentProject.RenumberSteps();
            CurrentProject.Touch();
        }

        public void MoveStepDown(int index)
        {
            if (CurrentProject == null) return;
            if (index < 0 || index >= CurrentProject.waypoints.Count - 1) return;
            var temp = CurrentProject.waypoints[index];
            CurrentProject.waypoints[index] = CurrentProject.waypoints[index + 1];
            CurrentProject.waypoints[index + 1] = temp;
            CurrentProject.RenumberSteps();
            CurrentProject.Touch();
        }

        public void RenameStep(int index, string newName)
        {
            if (CurrentProject == null) return;
            if (index < 0 || index >= CurrentProject.waypoints.Count) return;
            CurrentProject.waypoints[index].name = newName;
            CurrentProject.Touch();
        }

        // ════════════════════════════════════════
        //               저장 / 불러오기
        // ════════════════════════════════════════

        /// <summary>현재 프로젝트를 JSON 파일로 저장</summary>
        public bool SaveProject(string fileName)
        {
            if (CurrentProject == null) return false;

            try
            {
                if (!fileName.EndsWith(".json")) fileName += ".json";
                string path = Path.Combine(RecordingsFolder, fileName);
                string json = JsonUtility.ToJson(CurrentProject, prettyPrint: true);
                File.WriteAllText(path, json);
                statusMessage = $"💾 저장됨: {fileName}";
                Debug.Log($"[Record] {statusMessage} → {path}");
                return true;
            }
            catch (Exception e)
            {
                statusMessage = $"❌ 저장 실패: {e.Message}";
                Debug.LogError($"[Record] {statusMessage}");
                return false;
            }
        }

        /// <summary>JSON 파일에서 프로젝트 불러오기</summary>
        public bool LoadProject(string fileName)
        {
            try
            {
                if (!fileName.EndsWith(".json")) fileName += ".json";
                string path = Path.Combine(RecordingsFolder, fileName);
                if (!File.Exists(path))
                {
                    statusMessage = $"❌ 파일 없음: {fileName}";
                    Debug.LogWarning($"[Record] {statusMessage}");
                    return false;
                }
                string json = File.ReadAllText(path);
                CurrentProject = JsonUtility.FromJson<RecordProject>(json);
                CurrentProject.RenumberSteps();  // 안전하게 재정렬
                MigrateLegacy();
                statusMessage = $"📂 불러옴: {CurrentProject.projectName}";
                Debug.Log($"[Record] {statusMessage}");
                return true;
            }
            catch (Exception e)
            {
                statusMessage = $"❌ 불러오기 실패: {e.Message}";
                Debug.LogError($"[Record] {statusMessage}");
                return false;
            }
        }

        /// <summary>
        /// 예전 파일 보정.
        ///
        /// 옛 IMGUI 화면은 robot2 단독 스텝의 관절을 joints 에 넣었고, 새 코드는
        /// 언제나 joints2 를 읽는다. 그대로 두면 그 스텝들이 (0,0,0,0,0) 으로
        /// 실행돼 실물이 0 자세로 꺾인다. 불러올 때 한 번 옮겨 준다.
        /// </summary>
        void MigrateLegacy()
        {
            if (CurrentProject?.waypoints == null) return;
            int moved = 0;

            foreach (var wp in CurrentProject.waypoints)
            {
                if (wp == null || wp.type != "motion" || wp.target != "robot2") continue;
                if (wp.joints == null) continue;
                if (wp.joints2 == null) wp.joints2 = new float[6];

                // joints2 는 비었는데 joints 에 값이 있으면 옛 형식이다
                if (AllZero(wp.joints2) && !AllZero(wp.joints))
                {
                    Array.Copy(wp.joints, wp.joints2, Mathf.Min(wp.joints.Length, 6));
                    wp.gripper2 = wp.gripper;
                    wp.joints = new float[6];
                    moved++;
                }
            }

            if (moved > 0)
                Debug.Log($"[Record] 예전 형식 R2 스텝 {moved}개를 joints2 로 옮겼습니다");
        }

        static bool AllZero(float[] a)
        {
            if (a == null) return true;
            foreach (var v in a) if (Mathf.Abs(v) > 0.001f) return false;
            return true;
        }

        /// <summary>Recordings/ 폴더의 모든 .json 파일 목록</summary>
        public string[] ListSavedFiles()
        {
            try
            {
                if (!Directory.Exists(RecordingsFolder)) return new string[0];
                var files = Directory.GetFiles(RecordingsFolder, "*.json");
                for (int i = 0; i < files.Length; i++)
                    files[i] = Path.GetFileName(files[i]);
                return files;
            }
            catch { return new string[0]; }
        }

        // ════════════════════════════════════════
        //                  재생
        // ════════════════════════════════════════

        public void StartPlayback()
        {
            if (isPlaying) { Debug.LogWarning("[Record] 이미 재생 중"); return; }
            if (CurrentProject == null || CurrentProject.waypoints.Count == 0)
            {
                statusMessage = "❌ 재생할 스텝 없음";
                return;
            }
            playbackCoroutine = StartCoroutine(PlaybackRoutine());
        }

        public void StopPlayback()
        {
            if (playbackCoroutine != null)
            {
                StopCoroutine(playbackCoroutine);
                playbackCoroutine = null;
            }
            isPlaying = false;
            currentStepIndex = -1;
            statusMessage = "⏹ 정지";
        }

        IEnumerator PlaybackRoutine()
        {
            isPlaying = true;
            statusMessage = "▶ 재생 시작";
            Debug.Log($"[Record] {statusMessage}");

            // 반복 처리를 위한 스택 (loop_start 인덱스와 남은 반복 횟수)
            var loopStack = new Stack<(int index, int remaining)>();

            int i = 0;
            while (i < CurrentProject.waypoints.Count && isPlaying)
            {
                currentStepIndex = i;
                var wp = CurrentProject.waypoints[i];
                statusMessage = $"▶ Step {wp.stepNumber}: {wp.GetDisplayText()}";
                Debug.Log($"[Record] {statusMessage}");

                switch (wp.type)
                {
                    case "motion":
                        yield return ExecuteMotion(wp);
                        break;

                    case "wait":
                        yield return new WaitForSeconds(wp.duration);
                        break;

                    case "loop_start":
                        loopStack.Push((i, wp.loopCount - 1));
                        break;

                    case "loop_end":
                        if (loopStack.Count > 0)
                        {
                            var top = loopStack.Pop();
                            if (top.remaining > 0)
                            {
                                loopStack.Push((top.index, top.remaining - 1));
                                i = top.index;  // loop_start로 점프
                            }
                        }
                        break;
                }

                i++;
            }

            isPlaying = false;
            currentStepIndex = -1;
            statusMessage = "✅ 재생 완료";
            Debug.Log($"[Record] {statusMessage}");
        }

        /// <summary>
        /// 스텝 하나만 실행한다.
        ///
        /// 루틴을 만드는 중에는 "이 자세가 맞나"를 확인하려고 그 스텝만 돌려보고 싶을 때가 많다.
        /// 전체 재생으로 확인하려면 앞 스텝을 다 거쳐야 해서 오래 걸리고, 중간에 있는
        /// 대기·반복까지 같이 돌아간다.
        /// </summary>
        public void PlayStep(int index)
        {
            if (CurrentProject == null) return;
            if (index < 0 || index >= CurrentProject.waypoints.Count)
            { Debug.LogWarning($"[Record] 스텝 번호가 범위를 벗어났다: {index}"); return; }
            if (isPlaying) { Debug.LogWarning("[Record] 재생 중에는 단독 실행을 받지 않는다"); return; }

            playbackCoroutine = StartCoroutine(SingleStepRoutine(index));
        }

        IEnumerator SingleStepRoutine(int index)
        {
            isPlaying = true;
            currentStepIndex = index;

            var wp = CurrentProject.waypoints[index];
            statusMessage = $"▶ Step {wp.stepNumber} 단독 실행";
            Debug.Log($"[Record] {statusMessage}");

            switch (wp.type)
            {
                case "motion":
                    yield return ExecuteMotion(wp);
                    break;
                case "wait":
                    yield return new WaitForSeconds(wp.duration);
                    break;
                default:
                    // 반복 시작/끝은 표식일 뿐이라 혼자 실행할 동작이 없다.
                    statusMessage = $"Step {wp.stepNumber} 은 반복 표식이라 실행할 동작이 없습니다";
                    Debug.Log($"[Record] {statusMessage}");
                    break;
            }

            isPlaying = false;
            currentStepIndex = -1;
            playbackCoroutine = null;
            if (wp.type == "motion" || wp.type == "wait")
                statusMessage = $"✅ Step {wp.stepNumber} 완료";
        }

        /// <summary>robot2 의 관절은 언제나 joints2 에 있다. 단독이든 both 든 마찬가지다.</summary>
        static float[] R2Joints(Waypoint wp) => wp.joints2;

        IEnumerator ExecuteMotion(Waypoint wp)
        {
            if (dualManager == null) yield break;

            // Robot1
            if (wp.target == "robot1" || wp.target == "both")
            {
                ApplyJoints(dualManager.robot1, wp.joints);
                ApplyGripper(dualManager.robot1, wp.gripper);
            }
            // Robot2
            if (wp.target == "robot2" || wp.target == "both")
            {
                ApplyJoints(dualManager.robot2, R2Joints(wp));
                ApplyGripper(dualManager.robot2, wp.gripper2);
            }

            // ⚠️ 목표만 던지고 고정 시간만 기다리면 안 된다.
            //    팔은 초당 6~7° 정도로 움직이는데 0.5초마다 다음 스텝이 목표를 덮어써서,
            //    중간 자세들을 스쳐 지나가고 마지막 자세로만 간다.
            //    "스텝을 다 진행하지 않는다" 의 정체가 이것이었다.
            //    실제 도착을 확인하고 넘어간다.
            yield return WaitUntilArrived(wp);

            // 그리퍼도 다 움직인 뒤에 넘어간다.
            //
            // 도착 판정(NearPose)은 그리퍼를 일부러 뺀다 — 물건을 물면 목표까지
            // 닫히지 않아 영영 도착이 안 되기 때문이다. 그런데 그 바람에 관절만
            // 도착하면 곧장 다음 스텝으로 넘어가, 그리퍼가 닫히는 중에 팔이 출발했다.
            // "그리퍼 닫으면서 다음으로 넘어간다" 가 이 증상이다.
            yield return WaitGripperDone(wp);

            // 다 움직인 뒤 쉬었다가 다음 동작으로 간다.
            float gap = Mathf.Max(stepGapSec, wp.delayAfter);
            if (gap > 0f)
            {
                // 멈춰 있는 동안 화면이 "정지한 것"처럼 보이지 않게 남은 시간을 보여준다.
                statusMessage = $"⏸ Step {wp.stepNumber} 완료 — 다음까지 {gap:F1}초";
                yield return new WaitForSeconds(gap);
            }
        }

        [Tooltip("그리퍼가 멈춘 것으로 볼 때까지 필요한 정지 시간(초).")]
        public float gripperSettleSec = 0.4f;

        [Tooltip("그리퍼를 기다리는 최대 시간(초). 넘으면 포기하고 다음으로 간다.")]
        public float gripperTimeoutSec = 4f;

        /// <summary>
        /// 그리퍼가 더 이상 움직이지 않을 때까지 기다린다.
        ///
        /// "목표에 닿았나" 로 판단할 수 없다. 물건을 물면 거기서 멈추므로 목표에는
        /// 영영 닿지 않는다. 그래서 "값이 더 안 변하는가" 로 끝을 본다 —
        /// 허공에서 다 닫히든, 물건에 막혀 서든 똑같이 잡힌다.
        /// </summary>
        IEnumerator WaitGripperDone(Waypoint wp)
        {
            // 명령이 서보까지 나가기 전에 "안 움직인다"고 판정하면 안 된다.
            // 송신 루프가 한 바퀴 돌 시간은 주고 본다.
            float leadIn = Time.time + 0.35f;
            while (Time.time < leadIn)
            {
                if (!isPlaying) yield break;
                yield return null;
            }

            float deadline = Time.time + gripperTimeoutSec;
            float still = 0f;
            float prev = GripReading(wp);

            while (Time.time < deadline)
            {
                if (!isPlaying) yield break;
                yield return null;

                float now = GripReading(wp);
                if (Mathf.Abs(now - prev) < 0.2f) still += Time.deltaTime;
                else still = 0f;
                prev = now;

                if (still >= gripperSettleSec) yield break;   // 멈췄다
            }

            statusMessage = $"⚠ Step {wp.stepNumber} 그리퍼가 계속 움직입니다 — 다음으로 넘어갑니다";
            Debug.LogWarning($"[Record] {statusMessage}");
        }

        /// <summary>이 스텝이 건드리는 그리퍼들의 실제 위치 합. 변화만 보면 되므로 합으로 충분하다.</summary>
        float GripReading(Waypoint wp)
        {
            float sum = 0f;
            if (wp.target == "robot1" || wp.target == "both") sum += GripAngle(dualManager?.robot1);
            if (wp.target == "robot2" || wp.target == "both") sum += GripAngle(dualManager?.robot2);
            return sum;
        }

        /// <summary>그리퍼는 마지막 관절이다. 목표값이 아니라 실제로 읽힌 각도를 본다.</summary>
        static float GripAngle(SOArmManager robot)
        {
            if (robot == null) return 0f;
            try { return robot.GetJointAngle(robot.JointCount - 1); }
            catch { return 0f; }
        }

        [Header("재생")]
        [Tooltip("한 동작이 끝난 뒤 다음 동작까지 무조건 쉬는 시간(초).\n" +
                 "스텝에 저장된 delayAfter 가 이보다 길면 그 값을 쓴다.")]
        public float stepGapSec = 10.0f;

        [Tooltip("스텝 도착으로 인정할 오차(도). 크게 주면 다음 스텝으로 일찍 넘어가 부드럽게 이어진다.")]
        public float arriveToleranceDeg = 3f;

        [Tooltip("한 스텝 도착을 기다리는 최대 시간(초). 넘으면 포기하고 다음으로 간다.\n" +
                 "무한 대기로 재생이 멈춰 팔이 중간 자세에 방치되는 것을 막는다.")]
        public float arriveTimeoutSec = 30f;

        /// <summary>목표에 실제로 닿을 때까지 기다린다. 못 닿으면 시간 제한으로 빠져나온다.</summary>
        IEnumerator WaitUntilArrived(Waypoint wp)
        {
            float deadline = Time.time + arriveTimeoutSec;

            while (Time.time < deadline)
            {
                if (!isPlaying) yield break;          // 정지 누르면 즉시 중단
                if (Arrived(wp)) yield break;
                yield return null;
            }

            statusMessage = $"⚠ Step {wp.stepNumber} 도착 확인 실패 — 다음으로 넘어갑니다";
            Debug.LogWarning($"[Record] {statusMessage}");
        }

        bool Arrived(Waypoint wp)
        {
            if (wp.target == "robot1" || wp.target == "both")
                if (!NearPose(dualManager?.robot1, wp.joints)) return false;

            if (wp.target == "robot2" || wp.target == "both")
                if (!NearPose(dualManager?.robot2, R2Joints(wp))) return false;

            return true;
        }

        bool NearPose(SOArmManager robot, float[] target)
        {
            if (robot == null || target == null) return true;   // 확인할 수 없으면 막지 않는다

            // 그리퍼(마지막)는 제외한다. 물건을 물면 목표까지 안 닫혀 영영 도착이 안 된다.
            // 대신 WaitGripperDone 이 "더 이상 안 움직임" 으로 그리퍼를 따로 기다린다.
            for (int i = 0; i < Mathf.Min(target.Length, 5); i++)
            {
                float cur;
                try { cur = robot.GetJointAngle(i); }
                catch { continue; }
                if (Mathf.Abs(cur - target[i]) > arriveToleranceDeg) return false;
            }
            return true;
        }

        // ════════════════════════════════════════
        //                헬퍼
        // ════════════════════════════════════════

        /// <summary>
        /// 로봇의 현재 관절 각도를 읽는다.
        ///
        /// 예전에는 여기가 0 만 돌려주는 껍데기였다. 작성 당시 SOArmManager 가
        /// 각도를 노출하지 않아 UI 가 슬라이더 값을 넘겨주는 구조로 미뤄뒀던 것인데,
        /// 그 바람에 UI 를 거치지 않고 AddMotionStep 을 부르면 스텝이 전부
        /// (0,0,0,0,0) 으로 저장됐다. 지금은 GetJointAngle 이 있으니 직접 읽는다.
        ///
        /// 교시 중에는 실물을 손으로 민 자리가 목표로 반영되므로(AdoptRealPose)
        /// 이 값이 곧 "지금 이 자세" 다.
        /// </summary>
        float[] CaptureJoints(SOArmManager robot)
        {
            var arr = new float[6];
            if (robot == null) return arr;

            for (int i = 0; i < 6; i++)
            {
                // 관절 하나가 실패해도 스텝 저장 전체가 죽으면 안 된다.
                try { arr[i] = robot.GetJointAngle(i); }
                catch { arr[i] = 0f; }
            }
            return arr;
        }

        float CaptureGripper(SOArmManager robot)
        {
            if (robot == null) return 50f;
            try { return robot.GetGripperPercent(); }
            catch { return 50f; }
        }

        void ApplyJoints(SOArmManager robot, float[] joints)
        {
            if (robot == null || joints == null) return;
            for (int i = 0; i < Mathf.Min(joints.Length, 6); i++)
                robot.SetJointTarget(i, joints[i]);
        }

        void ApplyGripper(SOArmManager robot, float gripper)
        {
            if (robot == null) return;
            robot.SetGripperTarget(gripper);
        }

        // ════════════════════════════════════════
        //          UI 직접 호출용 (슬라이더값 전달)
        // ════════════════════════════════════════

        /// <summary>UI에서 슬라이더 값을 직접 전달받아 스텝 추가</summary>
        public void AddMotionStepFromUI(
            string target,
            float[] r1Joints, float r1Gripper,
            float[] r2Joints, float r2Gripper,
            int velocity, int acceleration)
        {
            if (CurrentProject == null) return;

            var wp = new Waypoint();
            wp.type = "motion";
            wp.target = target;
            wp.velocity = velocity;
            wp.acceleration = acceleration;
            wp.delayAfter = 1.0f;   // 동작 사이 기본 텀

            // 항상 안전한 복사
            wp.joints = new float[6];
            wp.joints2 = new float[6];

            if (target == "robot1" || target == "both")
            {
                Array.Copy(r1Joints, wp.joints, 6);
                wp.gripper = r1Gripper;
            }
            // ⚠️ robot2 는 단독이든 both 든 언제나 joints2 에 넣는다.
            //    예전에는 단독일 때만 joints 에 넣었는데, 재생 쪽은 joints2 를 읽어
            //    R2 단독 스텝이 전부 (0,0,0,0,0) 으로 실행됐다. 실물이 0 자세로 꺾인다.
            if (target == "robot2" || target == "both")
            {
                Array.Copy(r2Joints, wp.joints2, 6);
                wp.gripper2 = r2Gripper;
            }

            wp.name = $"{target} 자세";

            CurrentProject.waypoints.Add(wp);
            CurrentProject.RenumberSteps();
            CurrentProject.Touch();
            statusMessage = $"✚ Step {wp.stepNumber} 추가 ({target})";
            Debug.Log($"[Record] {statusMessage}");
        }
    }
}