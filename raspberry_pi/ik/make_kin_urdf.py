#!/usr/bin/env python3
"""
so101.urdf 에서 <visual> / <collision> 을 걷어낸 '기구학 전용' URDF 를 만든다.

왜: IK 는 관절 축·원점·한계만 있으면 풀린다. 메시는 필요 없다.
    그런데 placo(pinocchio)는 URDF 에 mesh 참조가 있으면 파일을 찾으려 들고,
    없으면 ValueError 로 적재 자체가 실패한다.
    라파에 DAE 13개를 올려두는 것보다, 안 쓰는 참조를 지우는 쪽이 실패 지점이 적다.

관절·링크·origin·axis·limit·inertial 은 그대로 둔다 — 이게 기구학의 전부다.
"""
import xml.etree.ElementTree as ET

SRC = "/home/sw/ik/so101.urdf"
DST = "/home/sw/ik/so101_kin.urdf"

tree = ET.parse(SRC)
root = tree.getroot()

removed = {"visual": 0, "collision": 0}
for link in root.iter("link"):
    for tag in ("visual", "collision"):
        for node in list(link.findall(tag)):
            link.remove(node)
            removed[tag] += 1

# material 은 visual 안에서만 쓰였으므로 최상위 정의도 정리한다
mats = 0
for node in list(root.findall("material")):
    root.remove(node)
    mats += 1

tree.write(DST, encoding="utf-8", xml_declaration=True)

print(f"visual {removed['visual']}개, collision {removed['collision']}개, material {mats}개 제거")
print(f"-> {DST}")

joints = [j.get("name") for j in root.findall("joint")]
print(f"남은 joint {len(joints)}개: {joints}")
links = [l.get("name") for l in root.findall("link")]
print(f"남은 link {len(links)}개: {links}")
