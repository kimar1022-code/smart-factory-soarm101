#!/usr/bin/env python3
"""서버의 ik/fk 명령을 TCP 로 실제로 때려본다. 모터는 안 움직인다."""
import json
import socket
import time

HOST, PORT = "127.0.0.1", 5000


class Client:
    def __init__(self):
        self.s = socket.create_connection((HOST, PORT), timeout=5)
        self.buf = ""

    def call(self, msg):
        self.s.sendall((json.dumps(msg) + "\n").encode())
        while "\n" not in self.buf:
            chunk = self.s.recv(4096).decode("utf-8", errors="ignore")
            if not chunk:
                raise RuntimeError("서버가 연결을 닫았다")
            self.buf += chunk
        line, self.buf = self.buf.split("\n", 1)
        return json.loads(line)

    def close(self):
        self.s.close()


c = Client()
print("=" * 62)

print("1) fk — 홈자세(전부 0도)")
r = c.call({"type": "fk", "joints": [0, 0, 0, 0, 0]})
print("  ", r)
assert r.get("ok"), "fk 실패"
home = r["position"]

print("\n2) fk — shoulder_pan 30도")
r = c.call({"type": "fk", "joints": [30, 0, 0, 0, 0]})
print("  ", r)

print("\n3) ik — 홈에서 +5cm 앞으로")
target = [round(home[0] + 0.05, 5), home[1], home[2]]
t0 = time.perf_counter()
r = c.call({"type": "ik", "current": [0, 0, 0, 0, 0], "target": target})
dt = (time.perf_counter() - t0) * 1000
print(f"   목표 {target}")
print("  ", r)
print(f"   왕복 시간(TCP 포함): {dt:.1f} ms")

print("\n4) ik 결과를 fk 로 되확인")
if r.get("ok"):
    r2 = c.call({"type": "fk", "joints": r["joints"]})
    print("   되돌린 위치:", r2.get("position"))
    print("   목표      :", target)

print("\n5) ik — 도달 불가능한 목표 (2m 앞)")
r = c.call({"type": "ik", "current": [0, 0, 0, 0, 0], "target": [2.0, 0.0, 0.2]})
print("  ", {k: r[k] for k in ("ok", "error_mm", "converged", "iters") if k in r})
print("   -> converged=false 로 유니티가 '도달 불가'를 표시할 수 있다")

print("\n6) 잘못된 입력 방어")
for bad in ({"type": "ik", "current": [0, 0], "target": [0.3, 0, 0.2]},
            {"type": "ik", "current": [0, 0, 0, 0, 0], "target": [0.3]},
            {"type": "fk", "joints": None}):
    r = c.call(bad)
    print("  ", bad, "->", {k: r.get(k) for k in ("ok", "error")})

print("\n7) 기존 명령이 안 깨졌는지 (get)")
r = c.call({"type": "get", "mode": "robot1"})
print("   ok =", r.get("ok"), " robot1 키 =", list(r.get("robot1", {}).keys())[:3], "...")

c.close()
print("=" * 62)
print("끝. 모터는 건드리지 않았다.")
