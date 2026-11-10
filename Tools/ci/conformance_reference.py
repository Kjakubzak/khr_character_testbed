#!/usr/bin/env python3
"""Reference evaluator for the shared KHR_character conformance fixtures.

Usage: python3 Tools/ci/conformance_reference.py [FIXTURE_DIR ...]

For every Tools/fixtures/conformance/<NAME>/expected.json (or the directories given), this loads the
fixture's .gltf, computes each case's outputs from the asset data alone, following the normative
draft at glTF-private 314c95c5 (KhronosGroup/glTF PR #2512 at 7ab5368a), and compares them with the
hand-written expected values. It exits non-zero on any disagreement.

It is a reference for the spec's arithmetic, not a renderer or an importer: it decodes accessors,
samples LINEAR/STEP animation channels, evaluates expressions (t = e*T, suppression sum-and-clamp,
additive aggregation y = b + sum(s - n)), visibility predicates, camera-hint selection, look-at
target points and reference poses. Where the spec leaves composition to the host (node rotation),
the SHOULD cases are checked with three methods (axis-angle sum and both multiplication orders) to
show that the tolerance accepts any approximately additive method.
"""

import base64
import json
import math
import pathlib
import struct
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2] / "Tools" / "fixtures" / "conformance"
NODE_HINT = "KHR_node_visibility_hint"
PRIMITIVE_HINT = "KHR_mesh_primitive_visibility_hint"
CAMERA_HINT = "KHR_node_camera_hint"
LOOKAT = "KHR_node_lookat_target"
EXPRESSION = "KHR_character_expression"
SUPPRESSION = "KHR_character_expression_suppression"
DERIVED = ("KHR_character_expression_joint", "KHR_character_expression_morphtarget",
           "KHR_character_expression_texture_transform")
POSE = "KHR_character_reference_pose"
CORE_VISIBILITY = "KHR_node_visibility"
STANDARD_VISIBILITY_ROLES = ("always", "first_person", "third_person")
STANDARD_CAMERA_ROLES = ("default_view", "detail", "first_person", "framing_view", "full_body", "orbit_view",
                         "portrait", "third_person")
COMPONENTS = {5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2), 5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4)}
WIDTH = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


# ── Math ──────────────────────────────────────────────────────────────────────────────────────

def q_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return [aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz]


def q_conj(q):
    return [-q[0], -q[1], -q[2], q[3]]


def q_norm(q):
    length = math.sqrt(sum(c * c for c in q))
    return [c / length for c in q]


def q_log(q):
    """Rotation vector (axis * angle, radians) of a unit quaternion, shortest arc."""
    q = q_norm(q)
    if q[3] < 0:
        q = [-c for c in q]
    s = math.sqrt(q[0] ** 2 + q[1] ** 2 + q[2] ** 2)
    if s < 1e-12:
        return [0.0, 0.0, 0.0]
    angle = 2 * math.atan2(s, q[3])
    return [c / s * angle for c in q[:3]]


def q_exp(v):
    angle = math.sqrt(sum(c * c for c in v))
    if angle < 1e-12:
        return [0.0, 0.0, 0.0, 1.0]
    s = math.sin(angle / 2) / angle
    return [v[0] * s, v[1] * s, v[2] * s, math.cos(angle / 2)]


def slerp(a, b, t):
    dot = sum(x * y for x, y in zip(a, b))
    if dot < 0:
        b, dot = [-c for c in b], -dot
    if dot > 0.9995:
        return q_norm([x + (y - x) * t for x, y in zip(a, b)])
    theta = math.acos(dot)
    wa, wb = math.sin((1 - t) * theta) / math.sin(theta), math.sin(t * theta) / math.sin(theta)
    return [wa * x + wb * y for x, y in zip(a, b)]


def mat_mul(a, b):
    return [[sum(a[r][k] * b[k][c] for k in range(4)) for c in range(4)] for r in range(4)]


def trs_matrix(t, r, s):
    x, y, z, w = r
    rot = [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
           [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
           [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]
    return [[rot[i][0] * s[0], rot[i][1] * s[1], rot[i][2] * s[2], t[i]] for i in range(3)] + [[0, 0, 0, 1]]


def column_major(m):
    return [[m[c * 4 + r] for c in range(4)] for r in range(4)]


# ── Asset access ──────────────────────────────────────────────────────────────────────────────

class Asset:
    def __init__(self, path):
        self.path = pathlib.Path(path)
        self.root = json.loads(self.path.read_text())
        self.buffers = []
        for buffer in self.root.get("buffers", []):
            uri = buffer["uri"]
            if uri.startswith("data:"):
                self.buffers.append(base64.b64decode(uri.split(",", 1)[1]))
            else:
                self.buffers.append((self.path.parent / uri).read_bytes())
        self.nodes = self.root.get("nodes", [])
        self.parent = {}
        for index, node in enumerate(self.nodes):
            for child in node.get("children", []):
                self.parent[child] = index
        self.names = {node.get("name"): index for index, node in enumerate(self.nodes)}

    def accessor(self, index):
        accessor = self.root["accessors"][index]
        view = self.root["bufferViews"][accessor["bufferView"]]
        code, size = COMPONENTS[accessor["componentType"]]
        width = WIDTH[accessor["type"]]
        stride = view.get("byteStride") or size * width
        data = self.buffers[view["buffer"]]
        base = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
        return [list(struct.unpack_from("<" + code * width, data, base + i * stride)) for i in range(accessor["count"])]

    def animation_index(self, name):
        for index, animation in enumerate(self.root.get("animations", [])):
            if animation.get("name") == name:
                return index
        raise KeyError(name)

    def channel_property(self, channel):
        target = channel["target"]
        if target.get("path") == "pointer":
            return target["extensions"]["KHR_animation_pointer"]["pointer"]
        return f"/nodes/{target['node']}/{target['path']}"

    def sample(self, animation, channel, t):
        sampler = animation["samplers"][channel["sampler"]]
        times = [row[0] for row in self.accessor(sampler["input"])]
        values = self.accessor(sampler["output"])
        if len(values) != len(times):  # packed weights: split the flat output into one row per key
            flat = [c for row in values for c in row]
            width = len(flat) // len(times)
            values = [flat[i * width:(i + 1) * width] for i in range(len(times))]
        interpolation = sampler.get("interpolation", "LINEAR")
        if interpolation not in ("LINEAR", "STEP"):
            raise NotImplementedError(interpolation)
        if t <= times[0]:
            return list(values[0])
        if t >= times[-1]:
            return list(values[-1])
        k = max(i for i in range(len(times)) if times[i] <= t)
        if interpolation == "STEP":
            return list(values[k])
        u = (t - times[k]) / (times[k + 1] - times[k])
        if self.channel_property(channel).endswith("/rotation") and len(values[k]) == 4:
            return slerp(values[k], values[k + 1], u)
        return [a + (b - a) * u for a, b in zip(values[k], values[k + 1])]

    def duration(self, animation):
        used = {channel["sampler"] for channel in animation["channels"]}
        return max(self.accessor(animation["samplers"][s]["input"])[-1][0] for s in used)

    def authored(self, pointer):
        """Authored initial value of a property pointer, with specification defaults."""
        parts = pointer.strip("/").split("/")
        leaf = parts[-1]
        if parts[0] == "nodes" and len(parts) == 3:
            node = self.nodes[int(parts[1])]
            if leaf == "weights":
                mesh = self.root["meshes"][node["mesh"]]
                return list(node.get("weights") or mesh.get("weights") or [0.0] * len(mesh["primitives"][0]["targets"]))
            return list(node.get(leaf, {"translation": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1]}[leaf]))
        owner = self.root
        for part in parts[:-1]:
            owner = owner[int(part)] if isinstance(owner, list) else owner[part]
        if "KHR_texture_transform" in parts:
            value = owner.get(leaf, {"offset": [0, 0], "scale": [1, 1], "rotation": 0}[leaf])
        elif CORE_VISIBILITY in parts:
            value = owner.get(leaf, True)
        else:
            value = owner[leaf]
        return list(value) if isinstance(value, list) else [value]

    def state_overrides(self, state):
        """Property values set by sampling one ordinary animation at a time (the host's evaluation state)."""
        if not state or not state.get("animation"):
            return {}
        animation = self.root["animations"][self.animation_index(state["animation"])]
        return {self.channel_property(c): self.sample(animation, c, state["time"]) for c in animation["channels"]}

    def local_matrix(self, index, overrides):
        node = self.nodes[index]
        if "matrix" in node and not any(k.startswith(f"/nodes/{index}/") for k in overrides):
            return column_major(node["matrix"])
        get = lambda leaf: overrides.get(f"/nodes/{index}/{leaf}", self.authored(f"/nodes/{index}/{leaf}"))
        return trs_matrix(get("translation"), get("rotation"), get("scale"))

    def global_matrix(self, index, overrides):
        matrix = self.local_matrix(index, overrides)
        while index in self.parent:
            index = self.parent[index]
            matrix = mat_mul(self.local_matrix(index, overrides), matrix)
        return matrix


# ── Expressions and suppression ───────────────────────────────────────────────────────────────

def effective_drivers(asset, drivers):
    entries = asset.root["extensions"][EXPRESSION]["expressions"]
    u = [min(max(float(drivers.get(entry["name"], 0)), 0.0), 1.0) for entry in entries]
    total = [0.0] * len(entries)
    for source, entry in enumerate(entries):
        for s in entry.get("extensions", {}).get(SUPPRESSION, {}).get("suppressions", []):
            kind, amount = s.get("type", "blend"), s.get("amount", 1.0)
            if kind == "blend":
                total[s["target"]] += amount * u[source]
            elif kind == "block":
                total[s["target"]] += amount if u[source] > s.get("threshold", 0.0) else 0.0
            # any other (custom) type: no supported companion here, so it contributes 0
    return [u[i] * (1 - min(max(total[i], 0.0), 1.0)) for i in range(len(entries))]


def compose_expressions(asset, drivers, rotation_method="axis_angle_sum"):
    entries = asset.root["extensions"][EXPRESSION]["expressions"]
    v = effective_drivers(asset, drivers)
    records = {}
    for index, entry in enumerate(entries):
        if v[index] <= 0:
            continue  # e = 0: empty response
        animation = asset.root["animations"][entry["animation"]]
        t = v[index] * asset.duration(animation)
        listed = [c for name in DERIVED for c in entry.get("extensions", {}).get(name, {}).get("channels", [])]
        for channel_index in listed:
            channel = animation["channels"][channel_index]
            prop = asset.channel_property(channel)
            records.setdefault(prop, []).append((asset.sample(animation, channel, t), asset.sample(animation, channel, 0)))
    result = {}
    for prop, items in records.items():
        b = asset.authored(prop)
        if prop.startswith("/nodes/") and prop.endswith("/rotation"):
            deltas = [q_mul(q_conj(n), q_norm(s)) for s, n in items]
            if rotation_method == "axis_angle_sum":
                total = [sum(q_log(d)[k] for d in deltas) for k in range(3)]
                result[prop] = q_norm(q_mul(b, q_exp(total)))
            else:
                ordered = deltas if rotation_method == "product_forward" else list(reversed(deltas))
                q = b
                for d in ordered:
                    q = q_mul(q, d)
                result[prop] = q_norm(q)
        else:
            # translation, weights, texture offset and rotation (MUST); node and texture scale (SHOULD)
            result[prop] = [b[k] + sum(s[k] - n[k] for s, n in items) for k in range(len(b))]
    return result


def close(actual, expected, tolerance):
    actual = actual if isinstance(actual, list) else [actual]
    expected = expected if isinstance(expected, list) else [expected]
    return len(actual) == len(expected) and all(abs(a - e) <= tolerance for a, e in zip(actual, expected))


def check_expression(asset, case):
    methods = ["axis_angle_sum"]
    if any("rotation_vector_deg" in p for p in case["expected"]["properties"]) and case["level"] == "SHOULD":
        methods += ["product_forward", "product_reverse"]
    problems = []
    for method in methods:
        result = compose_expressions(asset, case["input"]["drivers"], method)
        for prop in case["expected"]["properties"]:
            actual = result.get(prop["target"])
            if "rotation_vector_deg" in prop:
                if actual is None:
                    problems.append(f"{prop['target']}: no response")
                    continue
                vector = [math.degrees(c) for c in q_log(actual)]
                if not close(vector, prop["rotation_vector_deg"], prop["tolerance_deg"]):
                    problems.append(f"{prop['target']} [{method}]: rotation vector {fmt(vector)} deg, expected "
                                    f"{prop['rotation_vector_deg']} +/- {prop['tolerance_deg']}")
            elif prop["value"] is None:
                if actual is not None:
                    problems.append(f"{prop['target']}: expected no response, got {fmt(actual)}")
            elif actual is None or not close(actual, prop["value"], prop.get("tolerance", 1e-5)):
                problems.append(f"{prop['target']}: got {fmt(actual)}, expected {prop['value']}")
    return problems


def check_suppression(asset, case):
    entries = asset.root["extensions"][EXPRESSION]["expressions"]
    v = effective_drivers(asset, case["input"]["drivers"])
    by_name = {entry["name"]: v[i] for i, entry in enumerate(entries)}
    return [f"{name}: got {by_name[name]:.6g}, expected {value}"
            for name, value in case["expected"]["effective"].items() if abs(by_name[name] - value) > 1e-6]


# ── Visibility ────────────────────────────────────────────────────────────────────────────────

def hint_allows(roles, host):
    """KHR_node_visibility_hint Roles: OR over recognized roles; no recognized role means no restriction."""
    recognized = [r for r in roles if r in STANDARD_VISIBILITY_ROLES or r in host["logic"]]
    if not recognized or "always" in recognized:
        return True
    for role in recognized:
        if role in STANDARD_VISIBILITY_ROLES and role not in host["logic"]:
            return True  # a standard role without application logic applies in every view
        if role in host["applies"]:
            return True
    return False


def check_visibility(asset, case):
    host = case["input"]["host"]
    overrides = asset.state_overrides(case["input"].get("state"))

    def chain(index):
        while True:
            yield index
            if index not in asset.parent:
                return
            index = asset.parent[index]

    def node_visible(index):
        hints = all(hint_allows(asset.nodes[i]["extensions"][NODE_HINT]["roles"], host)
                    for i in chain(index) if NODE_HINT in asset.nodes[i].get("extensions", {}))
        core = all(bool(overrides.get(f"/nodes/{i}/extensions/{CORE_VISIBILITY}/visible",
                                      [asset.nodes[i].get("extensions", {}).get(CORE_VISIBILITY, {}).get("visible", True)])[0])
                   for i in chain(index))
        return hints and core

    problems = []
    for name, expected in case["expected"].get("nodes", {}).items():
        actual = node_visible(asset.names[name])
        if actual != expected:
            problems.append(f"node {name}: drawn={actual}, expected {expected}")
    for key, expected in case["expected"].get("primitives", {}).items():
        name, primitive = key.split("#")
        node = asset.nodes[asset.names[name]]
        prim = asset.root["meshes"][node["mesh"]]["primitives"][int(primitive)]
        own = PRIMITIVE_HINT not in prim.get("extensions", {}) or hint_allows(prim["extensions"][PRIMITIVE_HINT]["roles"], host)
        actual = own and node_visible(asset.names[name])
        if actual != expected:
            problems.append(f"primitive {key}: drawn={actual}, expected {expected}")
    return problems


# ── Camera hints, look-at targets, reference poses ────────────────────────────────────────────

def check_camera(asset, case):
    hints = [(i, n["extensions"][CAMERA_HINT]) for i, n in enumerate(asset.nodes) if CAMERA_HINT in n.get("extensions", {})]
    query, expected = case["input"]["query"], case["expected"]
    if query == "default_view":
        chosen = min(i for i, h in hints if h["role"] == "default_view")
        return [] if chosen == expected["node_index"] and asset.nodes[chosen]["name"] == expected["node"] else \
            [f"default view is node {chosen}, expected {expected['node_index']}"]
    if query == "default_affordances":
        recognized = case["input"]["host"]["recognized_custom_roles"]
        names = [asset.nodes[i]["name"] for i, h in hints if h["role"] in STANDARD_CAMERA_ROLES or h["role"] in recognized]
        return [] if names == expected["nodes"] else [f"surfaced {names}, expected {expected['nodes']}"]
    problems = []
    for name, want in expected["hints"].items():
        index = asset.names[name]
        m = asset.global_matrix(index, {})
        position = [m[r][3] for r in range(3)]
        forward = [-m[r][2] for r in range(3)]
        length = math.sqrt(sum(c * c for c in forward))
        forward = [c / length for c in forward]
        target = asset.nodes[index]["extensions"][CAMERA_HINT].get("targetNode")
        subject = None
        if target is not None and target != index and 0 <= target < len(asset.nodes):
            tm = asset.global_matrix(target, {})
            subject = [tm[r][3] for r in range(3)]
        if not close(position, want["position"], expected["tolerance"]):
            problems.append(f"{name}: position {fmt(position)}, expected {want['position']}")
        if not close(forward, want["forward"], expected["tolerance"]):
            problems.append(f"{name}: forward {fmt(forward)}, expected {want['forward']}")
        if (subject is None) != (want["subject_point"] is None) or (
                subject is not None and not close(subject, want["subject_point"], expected["tolerance"])):
            problems.append(f"{name}: subject {fmt(subject)}, expected {want['subject_point']}")
    return problems


def check_lookat(asset, case):
    problems = []
    markers = [i for i, n in enumerate(asset.nodes) if LOOKAT in n.get("extensions", {})]
    for number, (instance, want) in enumerate(zip(case["input"]["instances"], case["expected"]["instances"])):
        overrides = asset.state_overrides(instance["state"])
        for index in markers:
            m = asset.global_matrix(index, overrides)
            point = [m[r][3] for r in range(3)]
            name = asset.nodes[index]["name"]
            if not close(point, want[name], case["expected"]["tolerance"]):
                problems.append(f"instance {number} {name}: {fmt(point)}, expected {want[name]}")
    return problems


def check_pose(asset, case):
    animations = asset.root.get("animations", [])
    if case["input"].get("query") == "tagged_animations":
        names = [a["name"] for a in animations if POSE in a.get("extensions", {})]
        return [] if names == case["expected"]["animations"] else [f"tagged {names}"]
    animation = animations[asset.animation_index(case["input"]["animation"])]
    expected, tolerance = case["expected"], case["expected"]["tolerance"]
    problems = []
    if animation["extensions"][POSE]["poseType"] != expected["poseType"]:
        problems.append("poseType differs")
    # Single-key samplers give the same value at every time; sample at the key time.
    overrides = {asset.channel_property(c): asset.sample(animation, c, 0) for c in animation["channels"]}
    for name, want in expected["local"].items():
        index = asset.names[name]
        node = asset.nodes[index]
        if "matrix" in want:
            targeted = any(k.startswith(f"/nodes/{index}/") for k in overrides)
            if targeted or not close(node.get("matrix"), want["matrix"], tolerance):
                problems.append(f"{name}: expected its authored matrix")
            continue
        for leaf in ("translation", "rotation", "scale"):
            value = overrides.get(f"/nodes/{index}/{leaf}", asset.authored(f"/nodes/{index}/{leaf}"))
            if not close(value, want[leaf], tolerance):
                problems.append(f"{name}.{leaf}: {fmt(value)}, expected {want[leaf]}")
    for name, want in expected["global_translation"].items():
        m = asset.global_matrix(asset.names[name], overrides)
        point = [m[r][3] for r in range(3)]
        if not close(point, want, tolerance):
            problems.append(f"{name} global translation {fmt(point)}, expected {want}")
    return problems


def fmt(value):
    if value is None:
        return "None"
    return "[" + ", ".join(f"{c:.6g}" for c in value) + "]" if isinstance(value, list) else f"{value:.6g}"


CHECKS = {"expression": check_expression, "suppression": check_suppression, "visibility": check_visibility,
          "camera_hint": check_camera, "lookat_target": check_lookat, "reference_pose": check_pose}


def main():
    directories = [pathlib.Path(p) for p in sys.argv[1:]] or sorted(
        p for p in ROOT.iterdir() if p.is_dir() and p.name != "invalid" and (p / "expected.json").exists())
    failures = total = 0
    for directory in directories:
        expected = json.loads((directory / "expected.json").read_text())
        asset = Asset(directory / expected["asset"])
        for case in expected["cases"]:
            total += 1
            problems = CHECKS[expected["kind"]](asset, case)
            status = "PASS" if not problems else "FAIL"
            print(f"{status} {expected['fixture']}: {case['name']} [{case['level']}]")
            for problem in problems:
                print(f"     {problem}")
            failures += bool(problems)
    print(f"{total - failures}/{total} cases agree with the reference evaluator")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
