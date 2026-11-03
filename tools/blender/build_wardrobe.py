"""
Anger Foot VR 1.2's wardrobe parts, made in Blender from code like the rest: THE KILLBOT's endoskeleton, a chrome
machine under the skin in the game's chunky style. Each part rides on one of the hero's bones and is sized to it at
run time:
- limb parts (kb_upper, kb_lower, kb_finger, kb_neck): one unit long up +z (from the bone to the next), built round a
  0.05 m radius (kb_finger: 0.01 m), their front (the hydraulics' side) +y;
- kb_joint (an elbow or a knee: hinge across x), kb_shoulder, kb_vertebra: round their middle;
- kb_skull (with its jaw, teeth and eyes), kb_ribcage, kb_pelvis: life size round their middle, front +y;
- kb_palm: from the wrist, fingers up +z, back of the hand +y; kb_foot: from the ankle, toes +y.
Materials: endo_chrome (polished), endo_dark (the joints' cores, hydraulic bodies, panel lines), endo_teeth, endo_eye
(the red glow).
"""
import math
import bpy
from mathutils import Vector
import afvr as A
from build_rage import at

CHROME, DARK, TEETH, EYE = "endo_chrome", "endo_dark", "endo_teeth", "endo_eye"


def rbox(name, size, loc, material, round_=0.008, rot=(0, 0, 0)):
    ob = A.box(name, size, (0, 0, 0), material, round_=round_, segments=2)
    return at(ob, loc, rot)


def cyl(name, r, depth, loc, material, axis='Z', segments=20, round_=0.0):
    return A.cylinder(name, r, depth, loc, material, segments=segments, axis=axis, round_=round_)


def ball(name, r, loc, material, scale=(1, 1, 1), segments=20, rings=12):
    prof = [(r * math.sin(math.pi * k / rings), -r * math.cos(math.pi * k / rings)) for k in range(rings + 1)]
    prof[0] = (0.0, -r)
    prof[-1] = (0.0, r)
    ob = A.lathe(name, prof, segments, (0, 0, 0), material)
    return at(ob, loc, (0, 0, 0), scale)


def upper():
    p = [rbox("u_strut", (0.075, 0.055, 0.76), (0, 0, 0.5), CHROME, 0.014)]
    for s in (-1, 1):
        p.append(rbox("u_line", (0.004, 0.03, 0.56), (s * 0.0385, 0, 0.5), DARK, 0.001))
    p.append(cyl("u_hinge", 0.052, 0.1, (0, 0, 0.06), CHROME, 'X', round_=0.006))
    p.append(cyl("u_core", 0.032, 0.104, (0, 0, 0.06), DARK, 'X'))
    # the hydraulic up the front: its body, its polished rod, its mounts
    p.append(cyl("u_hyd", 0.021, 0.44, (0, 0.046, 0.36), DARK, round_=0.003))
    p.append(cyl("u_rod", 0.01, 0.3, (0, 0.046, 0.72), CHROME))
    for z in (0.14, 0.87):
        p.append(rbox("u_mount", (0.032, 0.03, 0.026), (0, 0.04, z), DARK, 0.004))
    return A.join("kb_upper", p)


def lower():
    p = []
    for s in (-1, 1):
        p.append(cyl("l_strut", 0.019, 0.86, (s * 0.027, 0, 0.5), CHROME, round_=0.004))
    for z in (0.06, 0.94):
        p.append(rbox("l_plate", (0.09, 0.052, 0.05), (0, 0, z), CHROME, 0.012))
    p.append(rbox("l_rib", (0.074, 0.03, 0.028), (0, 0, 0.5), DARK, 0.004))
    p.append(cyl("l_hyd", 0.017, 0.4, (0, -0.04, 0.33), DARK, round_=0.003))
    p.append(cyl("l_rod", 0.009, 0.32, (0, -0.04, 0.7), CHROME))
    return A.join("kb_lower", p)


def joint():
    p = [cyl("j_hinge", 0.05, 0.11, (0, 0, 0), CHROME, 'X', round_=0.008), cyl("j_core", 0.03, 0.116, (0, 0, 0), DARK, 'X'),
         rbox("j_cap", (0.07, 0.022, 0.06), (0, 0.046, 0), CHROME, 0.01)]
    return A.join("kb_joint", p)


def shoulder():
    return A.join("kb_shoulder", [ball("s_ball", 0.045, (0, 0, 0), CHROME), cyl("s_cap", 0.03, 0.02, (0, 0, 0.042), DARK)])


def vertebra():
    p = [cyl("v_disc", 0.028, 0.022, (0, 0, 0), CHROME, round_=0.004), rbox("v_spine", (0.013, 0.036, 0.013), (0, -0.032, 0), DARK, 0.003),
         rbox("v_side", (0.07, 0.012, 0.012), (0, -0.008, 0), DARK, 0.003)]
    return A.join("kb_vertebra", p)


def skull():
    p = [ball("k_cranium", 0.1, (0, -0.005, 0.02), CHROME, (0.82, 0.95, 1.0), 28, 16)]
    p.append(rbox("k_brow", (0.15, 0.04, 0.03), (0, 0.078, 0.02), CHROME, 0.012))
    for s in (-1, 1):
        # the eye sockets, deep, and the red eyes in them
        p.append(ball("k_socket", 0.029, (s * 0.038, 0.078, -0.008), DARK))
        p.append(ball("k_eye", 0.015, (s * 0.038, 0.098, -0.008), EYE))
        p.append(rbox("k_cheek", (0.032, 0.055, 0.026), (s * 0.062, 0.055, -0.046), CHROME, 0.01, (0, 0, s * 12)))
        p.append(cyl("k_temple", 0.009, 0.075, (s * 0.074, 0.018, -0.078), DARK))
    p.append(rbox("k_maxilla", (0.1, 0.055, 0.03), (0, 0.068, -0.072), CHROME, 0.01))
    p.append(rbox("k_nose", (0.022, 0.012, 0.03), (0, 0.098, -0.034), DARK, 0.004))
    # the jaw: a U round the front, its hinges at the back
    jaw = [(-0.066, 0.0, -0.085), (-0.062, 0.05, -0.112), (-0.038, 0.085, -0.128), (0.0, 0.094, -0.132), (0.038, 0.085, -0.128), (0.062, 0.05, -0.112), (0.066, 0.0, -0.085)]
    p.append(A.tube_along("k_jaw", jaw, 0.014, CHROME, sides=10))
    p.append(rbox("k_chin", (0.06, 0.022, 0.026), (0, 0.09, -0.132), CHROME, 0.008))
    for row, z, y in (("up", -0.09, 0.094), ("down", -0.112, 0.09)):
        for k in range(8):
            x = -0.035 + k * 0.01
            p.append(rbox("k_tooth_" + row, (0.0085, 0.008, 0.016), (x, y - abs(x) * 0.25, z), TEETH, 0.0015))
    return A.join("kb_skull", p)


def ribcage():
    p = [rbox("r_spineplate", (0.05, 0.03, 0.3), (0, -0.1, 0), DARK, 0.01)]
    for k in range(5):
        z = 0.12 - k * 0.058
        w = 0.12 + math.sin((k + 0.6) / 5.0 * math.pi) * 0.035
        for s in (-1, 1):
            pts = []
            for i in range(10):
                a = math.pi * i / 9.0  # (from the back round the side to the front)
                pts.append((s * (0.02 + math.sin(a) * w), -0.1 + (1 - math.cos(a)) * 0.1, z - math.sin(a * 0.5) * 0.02))
            p.append(A.tube_along("r_rib", pts, 0.011, CHROME, sides=8))
    p.append(rbox("r_sternum", (0.036, 0.022, 0.23), (0, 0.105, -0.01), CHROME, 0.008))
    for s in (-1, 1):
        p.append(A.tube_along("r_clavicle", [(0, 0.1, 0.125), (s * 0.08, 0.07, 0.135), (s * 0.15, 0.015, 0.13)], 0.012, CHROME, sides=8))
    p.append(cyl("r_core", 0.022, 0.03, (0, 0.118, 0.045), DARK, 'Y'))
    return A.join("kb_ribcage", p)


def pelvis():
    prof = [(0.03, -0.06), (0.11, -0.04), (0.13, 0.0), (0.12, 0.04), (0.08, 0.06), (0.03, 0.06)]
    bowl = at(A.lathe("p_bowl", prof, 28, (0, 0, 0), CHROME), (0, 0, 0), (0, 0, 0), (1, 0.62, 1))
    p = [bowl]
    for s in (-1, 1):
        p.append(ball("p_socket", 0.036, (s * 0.092, 0, -0.05), DARK))
    p.append(cyl("p_mount", 0.03, 0.05, (0, -0.02, 0.075), DARK))
    return A.join("kb_pelvis", p)


def palm():
    p = [rbox("h_palm", (0.085, 0.026, 0.086), (0, 0, 0.046), CHROME, 0.01), cyl("h_knuckles", 0.012, 0.086, (0, 0, 0.09), DARK, 'X'),
         cyl("h_wrist", 0.02, 0.05, (0, 0, 0.0), DARK, 'X'), rbox("h_back", (0.06, 0.008, 0.05), (0, 0.016, 0.045), DARK, 0.003)]
    return A.join("kb_palm", p)


def finger():
    return A.join("kb_finger", [rbox("f_seg", (0.018, 0.016, 0.78), (0, 0, 0.5), CHROME, 0.005), cyl("f_knuckle", 0.011, 0.021, (0, 0, 0.0), DARK, 'X')])


def foot():
    p = [ball("ft_ankle", 0.034, (0, 0, 0), DARK), rbox("ft_plate", (0.085, 0.2, 0.036), (0, 0.07, -0.07), CHROME, 0.012),
         rbox("ft_toe", (0.08, 0.06, 0.03), (0, 0.195, -0.075), CHROME, 0.01), rbox("ft_heel", (0.062, 0.05, 0.03), (0, -0.03, -0.075), DARK, 0.008),
         cyl("ft_strut", 0.016, 0.06, (0, 0.02, -0.035), CHROME)]
    return A.join("kb_foot", p)


def neck():
    p = [cyl("n_core", 0.012, 0.9, (0, 0, 0.5), DARK)]
    for k in range(3):
        a = 2 * math.pi * k / 3
        p.append(cyl("n_cable", 0.009, 0.9, (math.cos(a) * 0.021, math.sin(a) * 0.021, 0.5), CHROME))
    return A.join("kb_neck", p)


def build(models, keep):
    for make in (upper, lower, joint, shoulder, vertebra, skull, ribcage, pelvis, palm, finger, foot, neck):
        keep(A.smooth(make(), 40))
