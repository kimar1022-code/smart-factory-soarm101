#!/bin/bash
# 로봇 서버 기동 스크립트 (라즈베리파이에서 실행)
# pkill 패턴에 [] 를 쓰는 이유: 자기 자신의 명령줄과 매칭되어 세션이 죽는 것을 막기 위함

# ⚠️ 반드시 -9 여야 한다.
#    기본 SIGTERM 은 서버의 종료 핸들러를 깨우고, 그 핸들러가
#    disable_torque() 를 호출해 12V 팔이 중력으로 주저앉는다.
#    재시작할 때마다 팔이 떨어지면 안 되므로 핸들러를 건너뛰고 즉시 죽인다.
#    (토크는 모터가 유지하므로 프로세스가 사라져도 팔은 그대로 서 있다)
pkill -9 -f "[r]obot_server_dual" 2>/dev/null
sleep 2

cd /home/sw || exit 1
source /home/sw/lerobot-env/bin/activate || exit 1

setsid nohup python -u /home/sw/robot_server_dual.py > /home/sw/server.log 2>&1 < /dev/null &
sleep 15

echo "=== LOG ==="
cat /home/sw/server.log 2>&1

echo "=== PROC ==="
pgrep -af "[r]obot_server_dual" || echo "(dead)"

echo "=== PORT ==="
ss -ltn | grep 5000 || echo "(closed)"
