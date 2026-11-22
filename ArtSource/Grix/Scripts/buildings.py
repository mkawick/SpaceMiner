"""Grix buildings: Rafinerija (nautilus refinery) and Tvornica (brood-hive bot factory).
Blender convention: front faces -Y, up is +Z. Export with export_unity.py.
Run:  python3 -c "import sys; sys.argv=['x','--','out']; exec(open('buildings.py').read())"
"""
import os, math, random
exec(open('helpers.py').read())
from mathutils import Vector, Matrix
random.seed(11)
M['shell'] = mat('Grix_Shell', '#3a0f2f', metal=0.2, rough=0.12, alpha=0.6, coat=0.7)   # the Tvornica's see-through dome

def frame_ring(bm, centre, n_in, w, radius, segs, flatten=0.95):
    """A ring of verts around centre in the plane spanned by n_in and w."""
    return [bm.verts.new(centre + radius * (math.cos(2 * math.pi * k / segs) * n_in + flatten * math.sin(2 * math.pi * k / segs) * w))
            for k in range(segs)]

# ---------------------------------------------------------------- Rafinerija: nautilus refinery
def build_rafinerija(coll):
    root = empty('Grix_Rafinerija', (0, 0, 0), coll, size=1.5, kind='PLAIN_AXES')
    parts = []

    # Logarithmic spiral shell. Spiral lies in the Y/Z plane (side view); the opening is at the bottom
    # and faces the front (-Y). Each whorl grows 2.6x. The tube overlaps the inner whorl like a real nautilus.
    growth = math.log(2.6) / (2 * math.pi)
    th0, th1 = -3.6 * math.pi, 0.0
    R1 = 3.6                      # spiral radius at the opening
    steps, segs = 300, 40
    bm = bmesh.new()
    rings, ring_th, ring_c = [], [], []
    for s in range(steps + 1):
        th = th0 + (th1 - th0) * s / steps
        r = R1 * math.exp(growth * th)
        # angle convention: opening (th = 0) sits at the front-bottom of the spiral
        ang = th - math.pi * 0.5
        P = Vector((0.0, -r * math.cos(ang), r * math.sin(ang)))
        dP = Vector((0.0, -(growth * math.cos(ang) - math.sin(ang)), growth * math.sin(ang) + math.cos(ang))).normalized()
        n_in = Vector((1, 0, 0)).cross(dP).normalized()       # in-plane normal
        if n_in.dot(-P) < 0: n_in = -n_in                    # pointing toward the spiral centre
        # Involute like a real nautilus: each whorl is wide enough to wrap over the earlier ones, so the
        # flanks are smooth and only a small dimple shows in the centre. Chamber walls are inside, not on the skin.
        rho = 0.85 * r
        # raised growth ridges across the shell (Grix chitin look), every 0.5 rad
        if abs(((th / 0.5) % 1.0) - 0.5) > 0.44: rho *= 1.03
        rings.append(frame_ring(bm, P, n_in, Vector((1, 0, 0)), rho, segs, flatten=0.7))
        ring_th.append(th); ring_c.append(P.copy())
    for s in range(steps):
        a, b = rings[s], rings[s + 1]
        for k in range(segs):
            f = bm.faces.new((a[k], a[(k + 1) % segs], b[(k + 1) % segs], b[k]))
            # Faces must point away from the tube's centre line, or game engines (back-face culling) show the inside.
            f.normal_update()
            if f.normal.dot(f.calc_center_median() - (ring_c[s] + ring_c[s + 1]) * 0.5) < 0:
                f.normal_flip()
            th = ring_th[s]
            phi = 2 * math.pi * (k + 0.5) / segs
            outer = math.cos(phi) < 0.15                     # the outside of the whorl (away from centre)
            # classic nautilus flame stripes on the outer surface, fading out toward the opening
            # flame stripes sweep across the flanks from the centre dimple to the rim, fading toward the opening
            stripe = math.sin(th * 4.6 - 1.6 * math.cos(phi) + 0.35 * math.sin(3 * phi)) > 0.5 and th < -1.1 and math.cos(phi) < 0.55
            f.material_index = 1 if (outer and stripe) else 0
    # inner floor of the opening (dark), team-colour rim
    me = bpy.data.meshes.new('Shell'); bm.to_mesh(me); bm.free()
    me.materials.append(M['white']); me.materials.append(M['red'])
    for p in me.polygons: p.use_smooth = True
    shell = link(bpy.data.objects.new('Shell', me), coll)
    parts.append(shell)

    # opening: dark mouth, team rim, at the end of the tube (th = 0)
    ang = -math.pi * 0.5
    mouthC = Vector((0, -R1 * math.cos(ang), R1 * math.sin(ang)))       # (0, 0, -R1)
    tangent = Vector((0, -(growth * math.cos(ang) - math.sin(ang)), growth * math.sin(ang) + math.cos(ang))).normalized()
    rhoM = 0.85 * R1
    for nm, rr, off, mk, th_ in (('mouthRim', 1.04, 0.0, 'team', 0.35), ('mouthHole', 0.86, -0.25, 'hole', 0.2)):
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=rhoM * rr, radius2=rhoM * rr, depth=th_)
        bmesh.ops.scale(bm, vec=(0.7, 1.0, 1.0), verts=bm.verts)
        q = tangent.to_track_quat('Z', 'Y')
        bmesh.ops.rotate(bm, verts=bm.verts, matrix=q.to_matrix())
        bmesh.ops.translate(bm, vec=mouthC + tangent * off, verts=bm.verts)
        parts.append(from_bm(bm, nm, M[mk], coll, True))

    # glowing chamber windows along the side of the outer whorl (ore being digested)
    for i in range(6):
        th = -0.7 - i * 0.8
        r = R1 * math.exp(growth * th); a = th - math.pi * 0.5
        P = Vector((0, -r * math.cos(a), r * math.sin(a)))
        rho = 0.85 * r
        for side in (1, -1):
            c = P + Vector((side * rho * 0.69, 0, 0))
            s_ = 0.22 * rho
            parts.append(ellipsoid('window', tuple(c), (s_ * 0.3, s_ * 1.5, s_ * 0.8), M['glow'], coll, 12, 8,
                                   rot=(math.degrees(-a), 0, 0)))

    zb = -(R1 + rhoM) + 0.35 - 1.0          # plinth bottom: the mouth sinks slightly into the plinth
    plinth_top = zb + 1.0
    # tentacle intake arms around the mouth, reaching forward and down to the ground
    for i in range(7):
        a = math.radians(-66 + i * (132 / 6))
        base = mouthC + Vector((math.sin(a) * rhoM * 0.75, -0.3, math.cos(a) * rhoM * 0.55 - rhoM * 0.1))
        reach = Vector((math.sin(a) * 3.6, -2.6 - abs(math.sin(a)) * 0.8, 0))
        pts = [base, base + reach * 0.35 + Vector((0, 0, -0.6)), base + reach * 0.75 + Vector((0, 0, -1.8)),
               base + reach + Vector((0, 0, -2.6))]
        pts = [Vector((p.x, p.y, max(p.z, plinth_top + 0.25))) for p in pts]
        parts += limb('tentacle', [tuple(p) for p in pts], [0.36, 0.27, 0.18, 0.08], M['chitin2'], coll, verts=8)
        parts.append(ellipsoid('tip', tuple(pts[-1]), (0.18,) * 3, M['team'], coll, 8, 6))
        for t in (0.35, 0.6):   # suckers
            p = pts[1].lerp(pts[2], t)
            parts.append(ellipsoid('sucker', tuple(p + Vector((0, 0, -0.25))), (0.13, 0.13, 0.07), M['white'], coll, 8, 4))

    # siphon (hyponome) under the mouth: vents vapour while refining
    sA = mouthC + Vector((0, -0.6, -rhoM * 0.55))
    sB = sA + Vector((0, -2.4, -0.4))
    parts.append(segment('siphon', tuple(sA), tuple(sB), 0.75, 0.45, M['chitin2'], coll, 14))
    parts.append(ellipsoid('siphonGlow', tuple(sB), (0.42, 0.08, 0.42), M['glow'], coll, 12, 4))

    # chitin plinth (hexagonal, Grix) with Croatian trim
    parts.append(hexprism('plinth', (0, 0), 8.6, 1.0, M['chitin'], coll, z0=zb, rot=math.radians(30)))
    parts.append(hexprism('trimR', (0, 0), 8.75, 0.18, M['red'], coll, z0=zb + 0.55, rot=math.radians(30)))
    parts.append(hexprism('trimW', (0, 0), 8.75, 0.12, M['white'], coll, z0=zb + 0.73, rot=math.radians(30)))
    parts.append(hexprism('trimB', (0, 0), 8.75, 0.12, M['blue'], coll, z0=zb + 0.85, rot=math.radians(30)))

    body = join(parts, 'Body')
    # lift everything so the plinth sits on z = 0
    for o in (body,):
        o.location.z = -zb - 1.0 + 1.0
    body.parent = root

    # flag on top of the shell
    top_z = body.location.z + 1.85 * R1 * math.exp(growth * -math.pi)
    flag = quad('Flag', (0, 0, 0), 1.6, M['flag'], coll)
    flag.location = (0, 0, top_z + 0.02)
    flag.parent = root
    # sockets
    dp = empty('DockPoints', (0, 0, 0), coll, root, size=0.5, kind='PLAIN_AXES')
    for i, x in enumerate((-3.0, 0.0, 3.0)):
        empty(f'DockPoint_{i}', (x, -9.5, 1.0), coll, dp, size=0.6, kind='SPHERE')
    vent = empty('Vent', tuple(sB + Vector((0, 0, body.location.z))), coll, root, size=0.4, kind='SINGLE_ARROW')
    return root

# ---------------------------------------------------------------- Tvornica: brood-hive bot factory
def build_tvornica(coll):
    root = empty('Grix_Tvornica', (0, 0, 0), coll, size=1.5, kind='PLAIN_AXES')
    parts = []
    # plinth
    parts.append(hexprism('plinth', (0, 0), 9.6, 1.0, M['chitin'], coll, z0=0, rot=math.radians(30)))
    parts.append(hexprism('trimR', (0, 0), 9.75, 0.18, M['red'], coll, z0=0.55, rot=math.radians(30)))
    parts.append(hexprism('trimW', (0, 0), 9.75, 0.12, M['white'], coll, z0=0.73, rot=math.radians(30)))
    parts.append(hexprism('trimB', (0, 0), 9.75, 0.12, M['blue'], coll, z0=0.85, rot=math.radians(30)))
    # brood dome: a 60%-opaque chitin shell; one ring of brood cells around its foot, the machine visible inside
    domeR, domeC = 6.2, Vector((0, 1.0, 1.0))
    parts.append(ellipsoid('dome', tuple(domeC), (domeR, domeR, domeR * 0.85), M['shell'], coll, 32, 20))
    for ring in range(1, 2):
        el = math.radians(8 + ring * 15)
        n = max(6, int(round(2 * math.pi * math.cos(el) * domeR / 1.5)))
        for k in range(n):
            az = 2 * math.pi * (k + 0.5 * (ring % 2)) / n
            nrm = Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el) / 0.85)).normalized()
            pos = domeC + Vector((math.cos(el) * math.cos(az) * domeR, math.cos(el) * math.sin(az) * domeR, math.sin(el) * domeR * 0.85))
            if pos.y < -2.5 and abs(pos.x) < 3.0 and pos.z < 5.0:
                continue                                   # leave room for the hatch
            roll = random.random()
            mk = 'glow' if roll < 0.28 else ('white' if roll < 0.7 else 'red')
            bm = bmesh.new()
            bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.62, radius2=0.62, depth=0.22)
            q = nrm.to_track_quat('Z', 'Y')
            bmesh.ops.rotate(bm, verts=bm.verts, matrix=q.to_matrix())
            bmesh.ops.translate(bm, vec=pos + nrm * 0.05, verts=bm.verts)
            parts.append(from_bm(bm, 'cell', M[mk], coll, False))
    # ---- the machine under the shell (all solid) ----
    coreR = domeR * 0.4                                   # 40% of the shell's diameter
    coreC = domeC + Vector((0, 0.3, 0))
    parts.append(ellipsoid('core', tuple(coreC), (coreR, coreR, coreR * 0.9), M['team'], coll, 28, 16))
    for z, rr, mk in ((0.25, 1.02, 'chitin'), (1.15, 0.86, 'chitin'), (1.85, 0.55, 'chitin')):   # panel seams
        parts.append(ellipsoid('seam', tuple(coreC + Vector((0, 0, z))), (coreR * rr, coreR * rr, 0.07), M[mk], coll, 28, 4))
    parts.append(hexprism('coreBase', (coreC.x, coreC.y), coreR * 1.18, 0.5, M['chitin'], coll, z0=1.0))
    parts.append(segment('stack', tuple(coreC + Vector((0, 0, coreR * 0.82))), tuple(coreC + Vector((0, 0, coreR * 0.9 + 1.1))),
                         0.45, 0.32, M['chitin'], coll, 12))
    parts.append(ellipsoid('stackGlow', tuple(coreC + Vector((0, 0, coreR * 0.9 + 1.15))), (0.3, 0.3, 0.12), M['glow'], coll, 12, 6))
    for i in range(8):                                    # bolts / access hatches around the core
        a = 2 * math.pi * i / 8
        p = coreC + Vector((math.cos(a) * coreR * 0.97, math.sin(a) * coreR * 0.97, 0.55))
        parts.append(ellipsoid('bolt', tuple(p), (0.16, 0.16, 0.16), M['white'], coll, 8, 6))
    # conduits (faction colours): thick pipes from the core to the hatch, the plinth and two tanks
    conduit_cols = ['red', 'white', 'blue']
    pipes = [
        [coreC + Vector((0, -coreR * 0.9, 0.5)), Vector((0, -3.2, 1.5)), Vector((0, -4.0, 2.3))],              # to the hatch
        [coreC + Vector((coreR * 0.8, -0.8, 0.6)), Vector((3.6, -1.6, 1.6)), Vector((4.8, -2.4, 1.2))],
        [coreC + Vector((-coreR * 0.8, -0.8, 0.6)), Vector((-3.6, -1.6, 1.6)), Vector((-4.8, -2.4, 1.2))],
        [coreC + Vector((coreR * 0.75, 1.0, 0.7)), Vector((3.0, 3.4, 1.9)), Vector((3.4, 4.6, 1.3))],
        [coreC + Vector((-coreR * 0.75, 1.0, 0.7)), Vector((-3.0, 3.4, 1.9)), Vector((-3.4, 4.6, 1.3))],
        [coreC + Vector((0, coreR * 0.9, 1.2)), Vector((0, 3.8, 2.6)), Vector((0, 5.0, 1.4))],
    ]
    for i, pts in enumerate(pipes):
        mk = conduit_cols[i % 3]
        parts += limb('conduit', [tuple(p) for p in pts], [0.32, 0.32, 0.32], M[mk], coll, verts=10)
        for p in pts[1:]:                                 # couplings
            parts.append(ellipsoid('coupling', tuple(p), (0.42, 0.42, 0.42), M['chitin'], coll, 10, 6))
    for sx in (1, -1):                                    # two feed tanks, inside the shell
        parts.append(segment('tank', (3.6 * sx, 3.0, 1.0), (3.6 * sx, 3.0, 2.6), 0.75, 0.75, M['white'], coll, 16))
        parts.append(ellipsoid('tankCap', (3.6 * sx, 3.0, 2.6), (0.75, 0.75, 0.38), M['red'], coll, 16, 8))

    # assembly line: a conveyor ring around the core carrying half-built bees, robot arms working over it
    ringR, ringZ = 3.7, 1.35
    ringC = Vector((coreC.x, coreC.y, ringZ))
    n_ring = 28
    for k in range(n_ring):
        a0, a1 = 2 * math.pi * k / n_ring, 2 * math.pi * (k + 1) / n_ring
        p0 = ringC + Vector((math.cos(a0) * ringR, math.sin(a0) * ringR, 0))
        p1 = ringC + Vector((math.cos(a1) * ringR, math.sin(a1) * ringR, 0))
        parts.append(segment('track', tuple(p0), tuple(p1), 0.28, 0.28, M['blue'] if k % 2 else M['chitin'], coll, 8))
    for k in range(7):                                    # bots in progress: shells, glowing larvae, half-assembled
        a = 2 * math.pi * (k + 0.3) / 7
        p = ringC + Vector((math.cos(a) * ringR, math.sin(a) * ringR, 0.42))
        heading = (math.degrees(a) + 90)
        if k % 3 == 0:
            parts.append(ellipsoid('larva', tuple(p), (0.28, 0.5, 0.26), M['glow'], coll, 12, 8, rot=(0, 0, heading)))
        elif k % 3 == 1:
            parts.append(ellipsoid('shellBot', tuple(p), (0.3, 0.55, 0.28), M['chitin'], coll, 12, 8, rot=(0, 0, heading)))
            parts.append(ellipsoid('shellBand', tuple(p + Vector((0, 0, 0.05))), (0.32, 0.1, 0.3), M['red'], coll, 12, 4, rot=(0, 0, heading)))
        else:
            parts.append(ellipsoid('frameBot', tuple(p), (0.22, 0.45, 0.2), M['white'], coll, 10, 6, rot=(0, 0, heading)))
    for k in range(4):                                    # robot arms from the core base out over the line
        a = 2 * math.pi * (k + 0.125) / 4
        d = Vector((math.cos(a), math.sin(a), 0))
        base = Vector((coreC.x, coreC.y, 1.5)) + d * coreR * 1.15
        elbow = Vector((coreC.x, coreC.y, 3.4)) + d * (coreR * 1.15 + 0.9)
        wrist = Vector((coreC.x, coreC.y, 2.6)) + d * (ringR - 0.1)
        tool = wrist + Vector((0, 0, -0.55))
        parts += limb('arm', [tuple(base), tuple(elbow), tuple(wrist)], [0.2, 0.16, 0.12], M['white'], coll, verts=8)
        for j in (base, elbow, wrist):
            parts.append(ellipsoid('armJoint', tuple(j), (0.24, 0.24, 0.24), M['red'], coll, 10, 6))
        parts.append(segment('armTool', tuple(wrist), tuple(tool), 0.12, 0.03, M['chitin'], coll, 8))
        parts.append(ellipsoid('weld', tuple(tool), (0.1, 0.1, 0.1), M['glow'], coll, 8, 6))
    # launch cradle where finished bots leave the ring for the hatch tube
    cr = ringC + Vector((0, -ringR, 0.1))
    parts.append(hexprism('cradle', (cr.x, cr.y), 0.8, 0.3, M['chitin'], coll, z0=ringZ - 0.2))
    parts.append(hexprism('cradleGlow', (cr.x, cr.y), 0.6, 0.06, M['glow'], coll, z0=ringZ + 0.1))
    # thin cables drooping from the stack to the inside of the shell's foot
    for i in range(0, 6, 2):
        a = 2 * math.pi * (i + 0.5) / 6
        A = coreC + Vector((math.cos(a) * 0.4, math.sin(a) * 0.4, coreR * 0.9 + 0.8))
        B = Vector((domeC.x + math.cos(a) * domeR * 0.82, domeC.y + math.sin(a) * domeR * 0.82, 1.1))
        mid = A.lerp(B, 0.55) + Vector((0, 0, -0.9))
        parts += limb('cable', [tuple(A), tuple(mid), tuple(B)], [0.08, 0.08, 0.08], M[conduit_cols[i % 3]], coll, verts=6)

    # crown: team-colour ring and the flag
    dy = -2.6
    top = domeC + Vector((0, dy, domeR * 0.85 * math.sqrt(max(0.0, 1 - (dy / domeR) ** 2))))
    parts.append(hexprism('crown', (top.x, top.y), 1.5, 0.4, M['team'], coll, z0=top.z - 0.15))
    parts.append(hexprism('crownCap', (top.x, top.y), 1.25, 0.12, M['white'], coll, z0=top.z + 0.25))
    # hatching tube at the front, where new bots emerge, flanked by mandible arches
    hA = Vector((0, -3.6, 2.6)); hB = Vector((0, -7.6, 2.0))
    parts.append(segment('hatch', tuple(hA), tuple(hB), 2.0, 2.3, M['chitin'], coll, 18))
    parts.append(segment('hatchRim', tuple(hB), tuple(hB + Vector((0, -0.35, -0.05))), 2.45, 2.45, M['team'], coll, 18))
    parts.append(segment('hatchHole', tuple(hB + Vector((0, 0.3, 0))), tuple(hB + Vector((0, -0.4, -0.06))), 1.75, 1.75, M['hole'], coll, 18))
    parts.append(segment('hatchGlow', tuple(hB + Vector((0, 1.4, 0.15))), tuple(hB + Vector((0, 1.2, 0.15))), 1.5, 1.5, M['glow'], coll, 18))
    for sx in (1, -1):
        parts += limb('mandible', [(2.6 * sx, -3.2, 1.0), (3.4 * sx, -6.2, 2.4), (2.4 * sx, -8.6, 2.0), (1.1 * sx, -9.0, 1.2)],
                      [0.55, 0.45, 0.3, 0.08], M['white'], coll, verts=8)
    # brood mother: a striped, segmented abdomen curling over the back of the dome
    cols = ['red', 'white', 'blue', 'red', 'white']
    for i in range(5):
        t = i / 4
        a = math.radians(12 + t * 62)
        c = Vector((0, 1.0 + math.cos(a) * 7.0, 1.0 + math.sin(a) * 5.4))
        r = 2.3 - 1.0 * t
        parts.append(ellipsoid('seg', tuple(c), (r * 1.15, r * 0.9, r), M['chitin'], coll, 18, 10, rot=(math.degrees(a) - 90, 0, 0)))
        parts.append(ellipsoid('band', tuple(c + Vector((0, math.sin(a) * 0.5, -math.cos(a) * 0.5))), (r * 1.18, 0.18, r * 1.03),
                               M[cols[i]], coll, 18, 6, rot=(math.degrees(a) - 90, 0, 0)))
    # six stubby legs on the plinth corners
    for i in range(6):
        a = math.radians(60 * i + 30)
        c, s_ = math.cos(a), math.sin(a)
        parts += limb('leg', [(c * 8.2, s_ * 8.2, 0.6), (c * 10.0, s_ * 10.0, 1.6), (c * 10.6, s_ * 10.6, 0.0)],
                      [0.4, 0.3, 0.1], M['chitin'], coll, verts=8)
    body = join(parts, 'Body'); body.parent = root
    flag = quad('Flag', (top.x, top.y, top.z + 0.38), 1.6, M['flag'], coll); flag.parent = root
    sp = empty('SpawnPoint', tuple(hB + Vector((0, -2.0, 0))), coll, root, size=0.6, kind='SINGLE_ARROW')
    return root

cR = new_coll('Rafinerija'); cT = new_coll('Tvornica')
raf = build_rafinerija(cR); tvo = build_tvornica(cT)
print('TRIS', raf.name, tri_count(raf)); print('TRIS', tvo.name, tri_count(tvo))
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'Grix_Buildings.blend'))
print('DONE')
