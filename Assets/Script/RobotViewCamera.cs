using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 로봇 두 대를 보기 위한 궤도(orbit) 카메라.
    ///
    /// 씬 뷰에서 카메라 트랜스폼을 직접 만지는 대신, **대상을 중심으로 돌려 보는** 방식이다.
    /// 관제 화면에서는 "어디를 보고 있는지"가 항상 예측 가능해야 해서,
    /// 프리셋 버튼으로 정해진 시점에 즉시 갈 수 있게 했다.
    ///
    /// 조작
    ///   우클릭 드래그 : 회전
    ///   휠           : 확대/축소
    ///   휠클릭 드래그 : 평행이동
    ///   1~6          : 프리셋
    ///   F            : 두 로봇이 다 보이게 다시 맞춤
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class RobotViewCamera : MonoBehaviour
    {
        public enum Preset { Overview = 0, Front = 1, Side = 2, Top = 3, Iso = 4, Robot1 = 5, Robot2 = 6 }

        [Header("대상 (비우면 자동 탐색)")]
        public Transform robot1;
        public Transform robot2;

        [Header("현재 시점")]
        public Vector3 pivot = Vector3.zero;
        public float distance = 1.6f;
        public float yaw = 35f;
        public float pitch = 22f;

        [Header("한계")]
        public float minDistance = 0.25f;
        public float maxDistance = 6f;
        public float minPitch = -20f;
        public float maxPitch = 85f;

        [Header("감도")]
        public float orbitSpeed = 4f;
        public float zoomSpeed = 0.6f;
        public float panSpeed = 0.0015f;

        [Tooltip("클수록 빠르게 목표 시점으로 붙는다. 0 이면 즉시 이동.")]
        public float smooth = 10f;

        [Header("조작 허용")]
        public bool allowMouse = true;
        public bool allowHotkeys = true;

        // 목표값 — 실제 카메라는 이쪽으로 부드럽게 따라간다
        Vector3 tPivot;
        float tDistance, tYaw, tPitch;

        void Start()
        {
            AutoFindTargets();
            tPivot = pivot; tDistance = distance; tYaw = yaw; tPitch = pitch;
            FrameAll(instant: true);
        }

        void AutoFindTargets()
        {
            if (robot1 != null && robot2 != null) return;

            var dual = FindAnyObjectByType<SOArmDualManager>();
            if (dual != null)
            {
                if (robot1 == null && dual.robot1 != null) robot1 = dual.robot1.transform;
                if (robot2 == null && dual.robot2 != null) robot2 = dual.robot2.transform;
            }
        }

        void Update()
        {
            if (allowMouse) HandleMouse();
            if (allowHotkeys) HandleHotkeys();

            // 목표를 향해 부드럽게 수렴
            float k = smooth <= 0f ? 1f : 1f - Mathf.Exp(-smooth * Time.unscaledDeltaTime);
            pivot = Vector3.Lerp(pivot, tPivot, k);
            distance = Mathf.Lerp(distance, tDistance, k);
            yaw = Mathf.LerpAngle(yaw, tYaw, k);
            pitch = Mathf.Lerp(pitch, tPitch, k);

            ApplyTransform();
        }

        void HandleMouse()
        {
            // UI 위에서는 카메라가 돌지 않도록 — IMGUI 패널을 드래그하다 시점이 튀는 것을 막는다
            if (GUIUtility.hotControl != 0) return;

            if (Input.GetMouseButton(1))
            {
                tYaw += Input.GetAxis("Mouse X") * orbitSpeed;
                tPitch = Mathf.Clamp(tPitch - Input.GetAxis("Mouse Y") * orbitSpeed, minPitch, maxPitch);
            }

            if (Input.GetMouseButton(2))
            {
                float s = panSpeed * tDistance * 60f;
                tPivot -= transform.right * Input.GetAxis("Mouse X") * s;
                tPivot -= transform.up * Input.GetAxis("Mouse Y") * s;
            }

            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) > 0.0001f)
                tDistance = Mathf.Clamp(tDistance * (1f - wheel * zoomSpeed * 10f), minDistance, maxDistance);
        }

        void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) ApplyPreset(Preset.Overview);
            if (Input.GetKeyDown(KeyCode.Alpha2)) ApplyPreset(Preset.Front);
            if (Input.GetKeyDown(KeyCode.Alpha3)) ApplyPreset(Preset.Side);
            if (Input.GetKeyDown(KeyCode.Alpha4)) ApplyPreset(Preset.Top);
            if (Input.GetKeyDown(KeyCode.Alpha5)) ApplyPreset(Preset.Iso);
            if (Input.GetKeyDown(KeyCode.Alpha6)) ApplyPreset(Preset.Robot1);
            if (Input.GetKeyDown(KeyCode.F))      FrameAll(instant: false);
        }

        void ApplyTransform()
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.rotation = rot;
            transform.position = pivot - rot * Vector3.forward * distance;
        }

        // ── 프리셋 ──────────────────────────────────────────────

        public void ApplyPreset(Preset p)
        {
            switch (p)
            {
                case Preset.Overview: FrameAll(false); tYaw = 35f;  tPitch = 22f; break;
                case Preset.Front:    FrameAll(false); tYaw = 0f;   tPitch = 8f;  break;
                case Preset.Side:     FrameAll(false); tYaw = 90f;  tPitch = 8f;  break;
                case Preset.Top:      FrameAll(false); tYaw = 0f;   tPitch = 85f; break;
                case Preset.Iso:      FrameAll(false); tYaw = 45f;  tPitch = 30f; break;
                case Preset.Robot1:   FocusOn(robot1); break;
                case Preset.Robot2:   FocusOn(robot2); break;
            }
        }

        /// <summary>이름으로도 부를 수 있게 — UI 버튼에서 쓰기 편하다.</summary>
        public void ApplyPreset(int index) => ApplyPreset((Preset)index);

        public void FocusOn(Transform t)
        {
            if (t == null) return;
            var b = GetBounds(t);
            tPivot = b.center;
            tDistance = Mathf.Clamp(FitDistance(b) * 0.8f, minDistance, maxDistance);
        }

        /// <summary>두 로봇이 모두 화면에 들어오도록 맞춘다.</summary>
        public void FrameAll(bool instant)
        {
            Bounds? total = null;
            foreach (var t in new[] { robot1, robot2 })
            {
                if (t == null) continue;
                var b = GetBounds(t);
                if (total == null) total = b;
                else { var v = total.Value; v.Encapsulate(b); total = v; }
            }

            if (total == null) return;

            tPivot = total.Value.center;
            tDistance = Mathf.Clamp(FitDistance(total.Value), minDistance, maxDistance);

            if (instant)
            {
                pivot = tPivot; distance = tDistance; yaw = tYaw; pitch = tPitch;
                ApplyTransform();
            }
        }

        float FitDistance(Bounds b)
        {
            var cam = GetComponent<Camera>();
            float radius = Mathf.Max(b.extents.magnitude, 0.1f);
            float fov = (cam != null ? cam.fieldOfView : 60f) * Mathf.Deg2Rad;
            return radius / Mathf.Max(Mathf.Sin(fov * 0.5f), 0.01f) * 1.15f;
        }

        /// <summary>렌더러가 없으면 트랜스폼 위치만이라도 쓴다.</summary>
        static Bounds GetBounds(Transform t)
        {
            var rends = t.GetComponentsInChildren<Renderer>();
            if (rends == null || rends.Length == 0) return new Bounds(t.position, Vector3.one * 0.3f);

            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }
    }
}
