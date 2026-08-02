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
            wp.delayAfter = 0.5f;

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

            // both가 아닌 경우 사용 안 하는 배열도 안전하게 초기화
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
                float[] joints = wp.target == "both" ? wp.joints2 : wp.joints;
                float gripper = wp.target == "both" ? wp.gripper2 : wp.gripper;
                ApplyJoints(dualManager.robot2, joints);
                ApplyGripper(dualManager.robot2, gripper);
            }

            // 스텝 완료 대기 (delayAfter는 다음 스텝 가기 전 추가 대기)
            yield return new WaitForSeconds(Mathf.Max(0.1f, wp.delayAfter));
        }

        // ════════════════════════════════════════
        //                헬퍼
        // ════════════════════════════════════════

        float[] CaptureJoints(SOArmManager robot)
        {
            float[] arr = new float[6];
            if (robot == null) return arr;
            for (int i = 0; i < 6; i++)
            {
                // SOArmManager는 슬라이더 값을 직접 노출하지 않으니,
                // 시뮬 컨트롤러의 LastTargets에서 가져옴
                // (없으면 0으로)
                arr[i] = 0f;
            }
            // 실제 캡처는 UI에서 슬라이더 값을 전달받는 방식이 더 정확
            // → AddMotionStepWithJoints(...) 오버로드를 UI에서 사용하도록 함
            return arr;
        }

        float CaptureGripper(SOArmManager robot)
        {
            // 마찬가지로 UI 슬라이더값이 진실. 기본값 50 반환.
            return 50f;
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
            wp.delayAfter = 0.5f;

            // 항상 안전한 복사
            wp.joints = new float[6];
            wp.joints2 = new float[6];

            if (target == "robot1" || target == "both")
            {
                Array.Copy(r1Joints, wp.joints, 6);
                wp.gripper = r1Gripper;
            }
            if (target == "robot2")
            {
                // robot2 단독일 때는 joints 필드에 넣음
                Array.Copy(r2Joints, wp.joints, 6);
                wp.gripper = r2Gripper;
            }
            if (target == "both")
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