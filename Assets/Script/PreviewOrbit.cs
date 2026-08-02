using UnityEngine;
using UnityEngine.EventSystems;

namespace SOArmControl
{
    /// <summary>
    /// 관제의 3면 뷰 한 칸을 마우스로 돌려 보게 한다.
    ///
    /// 기본 방향(위/옆/앞)은 그대로 두고 **거기서 얼마나 더 돌렸는지**만 기억한다.
    /// 그래서 초기화하면 언제든 정확히 원래 시점으로 되돌아온다.
    ///
    /// 조작
    ///   좌클릭 드래그 : 회전
    ///   휠           : 확대/축소
    ///   우클릭       : 그 칸만 초기 시점으로
    /// </summary>
    public class PreviewOrbit : MonoBehaviour,
        IDragHandler, IScrollHandler, IPointerClickHandler
    {
        [Tooltip("기본 방향에서 좌우로 돌린 각도")]
        public float yaw;
        [Tooltip("기본 방향에서 위아래로 돌린 각도")]
        public float pitch;
        [Tooltip("1 이면 기본 거리. 작을수록 가까이.")]
        public float zoom = 1f;

        public float dragSpeed = 0.35f;
        public float zoomSpeed = 0.1f;
        public float minPitch = -85f, maxPitch = 85f;
        public float minZoom = 0.35f, maxZoom = 3f;

        /// <summary>사용자가 한 번이라도 돌렸는지. UI 에 "초기화" 안내를 띄울 때 쓴다.</summary>
        public bool Moved => !Mathf.Approximately(yaw, 0f) || !Mathf.Approximately(pitch, 0f)
                             || !Mathf.Approximately(zoom, 1f);

        public void OnDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            yaw += e.delta.x * dragSpeed;
            pitch = Mathf.Clamp(pitch - e.delta.y * dragSpeed, minPitch, maxPitch);
        }

        public void OnScroll(PointerEventData e)
        {
            // 위로 굴리면 가까워진다
            zoom = Mathf.Clamp(zoom * (1f - e.scrollDelta.y * zoomSpeed), minZoom, maxZoom);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) ResetView();
        }

        public void ResetView() { yaw = 0f; pitch = 0f; zoom = 1f; }
    }
}
