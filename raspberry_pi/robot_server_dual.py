"""
SO-ARM101 Dual Robot TCP Server
================================
두 대의 SO-ARM101 로봇을 LeRobot SDK로 제어하는 TCP 소켓 서버.

Unity 클라이언트로부터 JSON 명령을 받아 모터를 제어.

JSON 프로토콜:
    {"mode": "robot1|robot2|mirror", "motor": "모터명", "value": -100~100}

모터 이름:
    shoulder_pan, shoulder_lift, elbow_flex,
    wrist_flex, wrist_roll, gripper

실행:
    source ~/lerobot-env/bin/activate
    python robot_server_dual.py

종료:
    Ctrl+C
"""

import socket
import json
import time
from lerobot.motors.feetech import FeetechMotorsBus
from lerobot.motors.motors_bus import Motor, MotorNormMode, MotorCalibration


# ===== 설정 =====
ROBOT1_PORT = '/dev/serial/by-id/usb-1a86_USB_Single_Serial_5B14112388-if00'
ROBOT1_CALIB = '/home/sw/.cache/huggingface/lerobot/calibration/robots/so_follower/robot1.json'

ROBOT2_PORT = '/dev/serial/by-id/usb-1a86_USB_Single_Serial_5B14029636-if00'
ROBOT2_CALIB = '/home/sw/.cache/huggingface/lerobot/calibration/robots/so_follower/robot2.json'

SERVER_HOST = '0.0.0.0'
SERVER_PORT = 5000


def make_bus(port, cal_file):
    """모터 버스 객체 생성 및 캘리브레이션 적용."""
    cal_data = json.load(open(cal_file))
    calibration = {k: MotorCalibration(**v) for k, v in cal_data.items()}
    
    bus = FeetechMotorsBus(port=port, motors={
        'shoulder_pan':  Motor(1, 'sts3215', MotorNormMode.RANGE_M100_100),
        'shoulder_lift': Motor(2, 'sts3215', MotorNormMode.RANGE_M100_100),
        'elbow_flex':    Motor(3, 'sts3215', MotorNormMode.RANGE_M100_100),
        'wrist_flex':    Motor(4, 'sts3215', MotorNormMode.RANGE_M100_100),
        'wrist_roll':    Motor(5, 'sts3215', MotorNormMode.RANGE_M100_100),
        'gripper':       Motor(6, 'sts3215', MotorNormMode.RANGE_M100_100),
    })
    
    time.sleep(1)
    bus.connect()
    bus.write_calibration(calibration)
    bus.enable_torque()
    return bus


def main():
    # ===== 두 로봇 연결 =====
    print('로봇 1 연결중...')
    robot1 = make_bus(ROBOT1_PORT, ROBOT1_CALIB)
    
    print('로봇 2 연결중...')
    robot2 = make_bus(ROBOT2_PORT, ROBOT2_CALIB)
    
    print('두 로봇 연결 완료!')
    
    # ===== TCP 서버 시작 =====
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((SERVER_HOST, SERVER_PORT))
    server.listen(1)
    print(f'유니티 연결 대기중... (포트 {SERVER_PORT})')
    
    # ===== 메인 루프 =====
    while True:
        conn, addr = server.accept()
        print(f'유니티 연결됨: {addr}')
        
        while True:
            try:
                data = conn.recv(1024).decode('utf-8').strip()
                if not data:
                    break
                
                cmd = json.loads(data)
                mode = cmd.get('mode', 'robot1')
                motor = cmd['motor']
                value = cmd['value']
                
                # 모드별 라우팅
                if mode == 'robot1':
                    robot1.write('Goal_Position', motor, value)
                elif mode == 'robot2':
                    robot2.write('Goal_Position', motor, value)
                elif mode == 'mirror':
                    robot1.write('Goal_Position', motor, value)
                    robot2.write('Goal_Position', motor, value)
                
                print(f'[{mode}] {motor} -> {value}')
                conn.send('OK\n'.encode('utf-8'))
                
            except Exception as e:
                print(f'오류: {e}')
                break
        
        conn.close()
        print('연결 끊김, 다시 대기중...')


if __name__ == '__main__':
    main()
