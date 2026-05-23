using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace RobotControl
{
    /// <summary>
    /// 라즈베리파이 소켓 서버에 연결해서 SO-ARM 로봇 제어.
    /// 로봇1, 로봇2, 미러모드 지원.
    /// </summary>
    public class RobotSocketClient : MonoBehaviour
    {
        public enum RobotMode
        {
            Robot1,     // 1번 로봇만 제어
            Robot2,     // 2번 로봇만 제어
            Mirror      // 두 로봇 동시에
        }

        [Header("서버 연결")]
        [Tooltip("라즈베리파이 IP 주소")]
        public string serverIP = "192.168.45.18";
        [Tooltip("서버 포트")]
        public int serverPort = 5000;

        [Header("현재 모드")]
        public RobotMode currentMode = RobotMode.Robot1;

        [Header("연결 상태")]
        [SerializeField] private bool isConnected = false;
        [SerializeField] private string statusMessage = "Disconnected";

        private TcpClient client;
        private NetworkStream stream;

        public bool IsConnected => isConnected;
        public string StatusMessage => statusMessage;

        void Start()
        {
            Connect();
        }

        void OnDestroy()
        {
            Disconnect();
        }

        /// <summary>서버에 연결.</summary>
        public void Connect()
        {
            try
            {
                client = new TcpClient();
                client.Connect(serverIP, serverPort);
                stream = client.GetStream();
                isConnected = true;
                statusMessage = $"Connected to {serverIP}:{serverPort}";
                Debug.Log($"[RobotSocket] {statusMessage}");
            }
            catch (Exception e)
            {
                isConnected = false;
                statusMessage = $"Connection failed: {e.Message}";
                Debug.LogError($"[RobotSocket] {statusMessage}");
            }
        }

        /// <summary>연결 해제.</summary>
        public void Disconnect()
        {
            try
            {
                stream?.Close();
                client?.Close();
                isConnected = false;
                statusMessage = "Disconnected";
                Debug.Log("[RobotSocket] Disconnected");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RobotSocket] Disconnect error: {e.Message}");
            }
        }

        /// <summary>모터 각도 명령 전송.</summary>
        /// <param name="motor">모터 이름 (shoulder_pan, shoulder_lift 등)</param>
        /// <param name="value">각도 값 (-100 ~ 100)</param>
        public void SendMotorCommand(string motor, float value)
        {
            if (!isConnected)
            {
                Debug.LogWarning("[RobotSocket] Not connected!");
                return;
            }

            try
            {
                string mode = currentMode.ToString().ToLower();
                // JSON 형식: {"mode": "robot1", "motor": "shoulder_pan", "value": 30}
                string json = "{\"mode\": \"" + mode + "\", \"motor\": \"" + motor + "\", \"value\": " + value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "}\n";
                byte[] data = Encoding.UTF8.GetBytes(json);
                stream.Write(data, 0, data.Length);
                Debug.Log($"[RobotSocket] Sent: {json.Trim()}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RobotSocket] Send error: {e.Message}");
                isConnected = false;
            }
        }

        /// <summary>모드 변경.</summary>
        public void SetMode(RobotMode mode)
        {
            currentMode = mode;
            Debug.Log($"[RobotSocket] Mode changed to: {mode}");
        }

        /// <summary>UI 버튼용 - 모드 변경 (int로 받음).</summary>
        public void SetModeFromInt(int modeIndex)
        {
            SetMode((RobotMode)modeIndex);
        }
    }
}