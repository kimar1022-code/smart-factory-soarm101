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
        [Tooltip("연결이 끊기면 주기적으로 다시 붙는다. 끄면 한 번 끊긴 뒤 영영 복구되지 않는다.")]
        public bool autoReconnect = true;
        [Range(0.5f, 10f)]
        public float reconnectInterval = 2f;

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

        private float reconnectTimer = 0f;

        void Update()
        {
            // 끊기면 스스로 다시 붙는다.
            // 이게 없으면 순간적인 끊김 한 번에 양방향 동기화가 영구 정지한다.
            if (autoReconnect && !isConnected && Application.isPlaying)
            {
                reconnectTimer += Time.deltaTime;
                if (reconnectTimer >= reconnectInterval)
                {
                    reconnectTimer = 0f;
                    Debug.Log($"[SOArmSocket] 연결 끊김 감지 — 재연결 시도 ({serverIP}:{serverPort})");
                    Connect();
                }
            }
            else if (isConnected)
            {
                reconnectTimer = 0f;
            }

            // 응답이 유실되면 콜백이 큐에 남아 이후 응답과 한 칸씩 어긋난다.
            // (요청 A 의 콜백이 요청 B 의 응답을 받는 식) 한 번 어긋나면 영구히 어긋나므로
            // 밀린 콜백이 비정상적으로 쌓이면 통째로 버리고 정렬을 되돌린다.
            const int maxPending = 16;
            if (pendingCallbacks.Count > maxPending)
            {
                int dropped = 0;
                while (pendingCallbacks.Count > 0 && pendingCallbacks.TryDequeue(out _)) dropped++;
                Debug.LogWarning($"[SOArmSocket] 응답 대기 콜백 {dropped}개 폐기 — 큐 정렬 복구");
            }

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

        /// <summary>
        /// 서버에 접속한다.
        ///
        /// ⚠️ 이 컴포넌트는 로봇 2대가 공유한다. 그래서 Start 에서만 3번 불린다
        ///    (SocketClient.connectOnStart + SOArmManager 2개의 autoConnectReal).
        ///    예전에는 부를 때마다 기존 연결을 끊고 새로 맺어서, 방금 연결한 소켓을
        ///    다음 호출이 끊어버렸다. 그래서 이미 연결돼 있으면 아무것도 하지 않는다.
        /// </summary>
        public void Connect()
        {
            if (isConnected && client != null && client.Connected)
                return;   // 이미 살아있는 연결이 있으면 건드리지 않는다

            try
            {
                Disconnect(); // 죽은 연결 정리
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

        // ====== 신규: 수동(Teach) 모드 ON/OFF ======
        // 서버가 중력 부하 없는 관절(pan/wrist_flex/wrist_roll)의 토크만 끈다.
        // STS3215 는 1:345 감속이라 토크를 낮추는 것만으론 손으로 안 밀린다 —
        // 끄는 수밖에 없고, 무게를 드는 lift/elbow 는 끄면 주저앉으므로 유지한다.
        public void RequestTeach(string mode, bool enable, Action<string> callback = null)
        {
            if (!isConnected) { callback?.Invoke("{\"ok\":false,\"error\":\"not connected\"}"); return; }
            string enableStr = enable ? "true" : "false";
            string json = $"{{\"type\":\"teach\",\"mode\":\"{mode}\",\"enable\":{enableStr}}}\n";
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

            // 수신 스레드가 끝났으면 연결은 죽은 것이다.
            // 예전에는 여기서 isConnected 를 그대로 둬서, 스레드가 죽어도
            // IsConnected 가 true 로 남아 폴링이 조용히 멈춘 채 아무도 눈치채지 못했다.
            isConnected = false;
        }

        void SetStatus(string msg)
        {
            statusMessage = msg;
            OnStatusChanged?.Invoke(msg);
        }
    }
}
