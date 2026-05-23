using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace SOArmControl
{
    /// <summary>
    /// 라즈베리파이 LeRobot 서버와 TCP 소켓 통신. (v3)
    ///
    /// 기존 호환:
    ///   - SendMotorCommand(mode, motor, value)  ← 그대로 작동 (응답 X)
    ///
    /// 신규 (양방향):
    ///   - RequestAngles(mode, callback)         ← 현재 각도 요청
    ///   - RequestSetHome(mode, callback)        ← 홈포즈 저장
    ///   - RequestTorque(mode, enable, callback) ← 토크 ON/OFF
    ///   - 백그라운드 스레드가 응답을 받아 큐에 쌓고, 메인 스레드에서 콜백 실행
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

        [Header("디버그")]
        public bool logIncoming = false;
        public bool logOutgoing = false;

        private TcpClient client;
        private NetworkStream stream;
        private Thread receiveThread;
        private volatile bool running;

        // 응답 수신: 백그라운드에서 큐에 쌓고 메인스레드에서 꺼냄
        private readonly ConcurrentQueue<string> incomingResponses = new ConcurrentQueue<string>();
        // 요청한 순서대로 콜백을 매칭 (응답이 보내는 순서대로 온다고 가정 — 한 클라가 lock으로 직렬화됨)
        private readonly ConcurrentQueue<Action<string>> pendingCallbacks = new ConcurrentQueue<Action<string>>();

        // 한 줄(\n) 단위로 자르기 위한 누적 버퍼 (백그라운드 스레드 전용)
        private readonly StringBuilder lineBuffer = new StringBuilder();

        // 전송 동시성 방지 (write 중에 다른 스레드가 끼면 깨짐)
        private readonly object writeLock = new object();

        public bool IsConnected => isConnected;
        public string StatusMessage => statusMessage;

        public event Action<string> OnStatusChanged;

        void Start()
        {
            if (connectOnStart) Connect();
        }

        void Update()
        {
            // 메인 스레드에서 응답 콜백 실행
            while (incomingResponses.TryDequeue(out string response))
            {
                if (logIncoming) Debug.Log($"[SOArmSocket ← Server] {response}");
                if (pendingCallbacks.TryDequeue(out Action<string> cb))
                {
                    try { cb?.Invoke(response); }
                    catch (Exception e) { Debug.LogError($"[SOArmSocket] 콜백 오류: {e}"); }
                }
            }
        }

        void OnDestroy() => Disconnect();
        void OnApplicationQuit() => Disconnect();

        public void Connect()
        {
            try
            {
                Disconnect(); // 기존 연결 정리
                client = new TcpClient();
                client.Connect(serverIP, serverPort);
                stream = client.GetStream();
                isConnected = true;
                running = true;

                receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
                receiveThread.Start();

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
            running = false;
            try { stream?.Close(); } catch { }
            try { client?.Close(); } catch { }
            stream = null;
            client = null;
            isConnected = false;

            if (receiveThread != null && receiveThread.IsAlive)
                receiveThread.Join(200);
            receiveThread = null;

            // 대기 중 콜백 정리 (응답 없이 끝남 처리)
            while (pendingCallbacks.TryDequeue(out var cb))
            {
                try { cb?.Invoke("{\"ok\":false,\"error\":\"disconnected\"}"); } catch { }
            }

            SetStatus("Disconnected");
        }

        // ====== 기존: 모터 명령 (응답 X, 기존 호환) ======
        public bool SendMotorCommand(string mode, string motorName, float value)
        {
            if (!isConnected) return false;
            // 기존 JSON 형식 그대로 (type 필드 없음)
            string json = "{\"mode\": \"" + mode +
                          "\", \"motor\": \"" + motorName +
                          "\", \"value\": " +
                          value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                          "}\n";
            return SendRaw(json, callback: null);
        }

        // ====== 신규: 현재 각도 요청 (응답 O) ======
        public void RequestAngles(string mode, Action<string> callback)
        {
            if (!isConnected) { callback?.Invoke("{\"ok\":false,\"error\":\"not connected\"}"); return; }
            string json = $"{{\"type\":\"get\",\"mode\":\"{mode}\"}}\n";
            SendRaw(json, callback);
        }

        // ====== 신규: 홈포즈 저장 (현재 자세를 새 0점으로) ======
        public void RequestSetHome(string mode, Action<string> callback)
        {
            if (!isConnected) { callback?.Invoke("{\"ok\":false,\"error\":\"not connected\"}"); return; }
            string json = $"{{\"type\":\"set_home\",\"mode\":\"{mode}\"}}\n";
            SendRaw(json, callback);
        }

        // ====== 신규: 토크 ON/OFF ======
        public void RequestTorque(string mode, bool enable, Action<string> callback = null)
        {
            if (!isConnected) { callback?.Invoke("{\"ok\":false,\"error\":\"not connected\"}"); return; }
            string enableStr = enable ? "true" : "false";
            string json = $"{{\"type\":\"torque\",\"mode\":\"{mode}\",\"enable\":{enableStr}}}\n";
            SendRaw(json, callback);
        }

        // ====== 내부 ======
        public bool SendRaw(string json, Action<string> callback = null)
        {
            if (!isConnected || stream == null) return false;

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(json);
                lock (writeLock)
                {
                    // 콜백 큐에 먼저 등록한 뒤 send (응답이 도착했을 때 매칭)
                    if (callback != null) pendingCallbacks.Enqueue(callback);
                    stream.Write(data, 0, data.Length);
                }
                if (logOutgoing) Debug.Log($"[SOArmSocket → Server] {json.Trim()}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SOArmSocket] Send error: {e.Message}");
                isConnected = false;
                SetStatus($"Send error: {e.Message}");
                callback?.Invoke("{\"ok\":false,\"error\":\"send failed\"}");
                return false;
            }
        }

        private void ReceiveLoop()
        {
            byte[] buf = new byte[4096];
            while (running && client != null && client.Connected)
            {
                try
                {
                    int n = stream.Read(buf, 0, buf.Length);
                    if (n <= 0) break;

                    lineBuffer.Append(Encoding.UTF8.GetString(buf, 0, n));
                    while (true)
                    {
                        string acc = lineBuffer.ToString();
                        int idx = acc.IndexOf('\n');
                        if (idx < 0) break;

                        string line = acc.Substring(0, idx).Trim();
                        lineBuffer.Remove(0, idx + 1);
                        if (!string.IsNullOrEmpty(line))
                            incomingResponses.Enqueue(line);
                    }
                }
                catch (Exception)
                {
                    break;
                }
            }
            running = false;
        }

        void SetStatus(string msg)
        {
            statusMessage = msg;
            OnStatusChanged?.Invoke(msg);
        }
    }
}
