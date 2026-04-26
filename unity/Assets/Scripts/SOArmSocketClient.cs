using System;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 라즈베리파이 LeRobot 서버와의 TCP 소켓 통신.
    /// JSON 프로토콜:
    ///   {"mode": "robot1|robot2|mirror", "motor": "모터명", "value": -100~100}
    /// </summary>
    public class SOArmSocketClient : MonoBehaviour
    {
        [Header("서버 연결")]
        public string serverIP = "192.168.45.18";
        public int serverPort = 5000;

        [Header("자동 연결")]
        public bool connectOnStart = true;

        [Header("연결 상태 (읽기 전용)")]
        [SerializeField] private bool isConnected = false;
        [SerializeField] private string statusMessage = "Disconnected";

        private TcpClient client;
        private NetworkStream stream;

        public bool IsConnected => isConnected;
        public string StatusMessage => statusMessage;

        public event Action<string> OnStatusChanged;

        void Start()
        {
            if (connectOnStart) Connect();
        }

        void OnDestroy()
        {
            Disconnect();
        }

        public void Connect()
        {
            try
            {
                client = new TcpClient();
                client.Connect(serverIP, serverPort);
                stream = client.GetStream();
                isConnected = true;
                SetStatus($"Connected to {serverIP}:{serverPort}");
                Debug.Log($"[SOArmSocket] {statusMessage}");
            }
            catch (Exception e)
            {
                isConnected = false;
                SetStatus($"Connect failed: {e.Message}");
                Debug.LogError($"[SOArmSocket] {statusMessage}");
            }
        }

        public void Disconnect()
        {
            try
            {
                stream?.Close();
                client?.Close();
                isConnected = false;
                SetStatus("Disconnected");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SOArmSocket] Disconnect error: {e.Message}");
            }
        }

        /// <summary>모터 명령 전송.</summary>
        public bool SendMotorCommand(string mode, string motorName, float value)
        {
            if (!isConnected) return false;

            try
            {
                string json = "{\"mode\": \"" + mode +
                              "\", \"motor\": \"" + motorName +
                              "\", \"value\": " +
                              value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                              "}\n";
                byte[] data = Encoding.UTF8.GetBytes(json);
                stream.Write(data, 0, data.Length);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SOArmSocket] Send error: {e.Message}");
                isConnected = false;
                SetStatus($"Send error: {e.Message}");
                return false;
            }
        }

        void SetStatus(string msg)
        {
            statusMessage = msg;
            OnStatusChanged?.Invoke(msg);
        }
    }
}
