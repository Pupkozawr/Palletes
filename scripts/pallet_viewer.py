from __future__ import annotations

import argparse
import csv
import itertools
import math
from collections import defaultdict
from pathlib import Path

import matplotlib


plt = None
Poly3DCollection = None
Rectangle = None


MIN_SUPPORT_AREA_RATIO = 0.65
STATUS_OK = "ok"
STATUS_WARNING = "warning"
STATUS_ERROR = "error"


def setup_matplotlib(use_agg: bool) -> None:
    global plt, Poly3DCollection, Rectangle

    if use_agg:
        matplotlib.use("Agg")

    import matplotlib.pyplot as pyplot
    from matplotlib.patches import Rectangle as PatchRectangle
    from mpl_toolkits.mplot3d.art3d import Poly3DCollection as PolyCollection

    plt = pyplot
    Rectangle = PatchRectangle
    Poly3DCollection = PolyCollection


def parse_float(value: str) -> float:
    return float(str(value).strip())


def parse_int(value: str, default: int = 0) -> int:
    text = str(value).strip()
    if not text:
        return default
    return int(float(text))


def infer_sku(box_id: str) -> str:
    return str(box_id).split("_", 1)[0].strip()


def load_layout_csv(path: str):
    rows = []
    with open(path, "r", encoding="utf-8-sig", newline="") as f:
        reader = csv.reader(f)
        for row in reader:
            if any(cell.strip() for cell in row):
                rows.append([cell.strip() for cell in row])

    if len(rows) < 2:
        raise ValueError("File is too short: expected metadata plus box rows.")

    containers = {}
    pallets = {}
    boxes = []

    i = 0
    while i < len(rows):
        row = rows[i]
        tag = row[0].upper()
        if tag == "CONTAINER":
            if len(row) < 8:
                raise ValueError("CONTAINER row must have 8 columns.")
            containers[row[1]] = {
                "id": row[1],
                "origin_x": parse_float(row[2]),
                "origin_y": parse_float(row[3]),
                "origin_z": parse_float(row[4]),
                "dx": parse_float(row[5]),
                "dy": parse_float(row[6]),
                "dz": parse_float(row[7]),
            }
            i += 1
            continue

        if tag == "PALLET":
            if len(row) < 8:
                raise ValueError("PALLET row must have at least 8 columns.")
            pallets[row[1]] = {
                "id": row[1],
                "container_id": row[8] if len(row) >= 9 else "",
                "origin_x": parse_float(row[2]),
                "origin_y": parse_float(row[3]),
                "origin_z": parse_float(row[4]),
                "dx": parse_float(row[5]),
                "dy": parse_float(row[6]),
                "dz": parse_float(row[7]),
            }
            i += 1
            continue

        break

    if i >= len(rows):
        raise ValueError("Box header row is missing.")

    header = rows[i]
    i += 1

    if not header or header[0] != "ID":
        raise ValueError("Expected box header row starting with ID.")

    if "PalletId" in header:
        expected = ["ID", "PalletId", "x", "y", "z", "X", "Y", "Z"]
    else:
        expected = ["ID", "x", "y", "z", "X", "Y", "Z"]

    missing = [name for name in expected if name not in header]
    if missing:
        raise ValueError(f"Missing required columns: {missing}")

    one_pallet_id = next(iter(pallets.keys()), "")

    for row in rows[i:]:
        if len(row) < len(header):
            row = row + [""] * (len(header) - len(row))
        record = dict(zip(header, row))
        pallet_id = record.get("PalletId", "").strip() or one_pallet_id
        box = {
            "ID": record["ID"],
            "PalletId": pallet_id,
            "SKU": infer_sku(record["ID"]),
            "Weight": 0,
            "Strength": 0,
            "Aisle": 0,
            "Caustic": 0,
            "x": parse_float(record["x"]),
            "y": parse_float(record["y"]),
            "z": parse_float(record["z"]),
            "X": parse_float(record["X"]),
            "Y": parse_float(record["Y"]),
            "Z": parse_float(record["Z"]),
        }
        box["dx"] = box["X"] - box["x"]
        box["dy"] = box["Y"] - box["y"]
        box["dz"] = box["Z"] - box["z"]
        box["volume"] = box["dx"] * box["dy"] * box["dz"]
        boxes.append(box)

    if not pallets and boxes:
        max_x = max(box["X"] for box in boxes)
        max_y = max(box["Y"] for box in boxes)
        max_z = max(box["Z"] for box in boxes)
        pallets["PALLET_1"] = {
            "id": "PALLET_1",
            "container_id": "",
            "origin_x": 0.0,
            "origin_y": 0.0,
            "origin_z": 0.0,
            "dx": max_x,
            "dy": max_y,
            "dz": max_z,
        }
        for box in boxes:
            box["PalletId"] = "PALLET_1"

    if not containers and pallets:
        max_x = max(p["origin_x"] + p["dx"] for p in pallets.values())
        max_y = max(p["origin_y"] + p["dy"] for p in pallets.values())
        max_z = max(p["origin_z"] + p["dz"] for p in pallets.values())
        containers["VIRTUAL_CONTAINER"] = {
            "id": "VIRTUAL_CONTAINER",
            "origin_x": 0.0,
            "origin_y": 0.0,
            "origin_z": 0.0,
            "dx": max_x,
            "dy": max_y,
            "dz": max_z,
        }
        for pallet in pallets.values():
            if not pallet["container_id"]:
                pallet["container_id"] = "VIRTUAL_CONTAINER"

    return containers, pallets, boxes


def load_item_metadata(path: str | None):
    if not path:
        return {}

    rows = []
    with open(path, "r", encoding="utf-8-sig", newline="") as f:
        reader = csv.reader(f)
        for row in reader:
            if any(cell.strip() for cell in row):
                rows.append([cell.strip() for cell in row])

    header_index = None
    for i, row in enumerate(rows):
        if row and row[0].strip().upper() == "SKU":
            header_index = i
            break

    if header_index is None:
        raise ValueError("Input metadata file must contain a SKU header row.")

    header = rows[header_index]
    result = {}
    for row in rows[header_index + 1 :]:
        if len(row) < len(header):
            row = row + [""] * (len(header) - len(row))
        record = dict(zip(header, row))
        sku = str(record.get("SKU", "")).strip()
        if not sku:
            continue
        result[sku] = {
            "SKU": sku,
            "Weight": parse_int(record.get("Weight", ""), 0),
            "Strength": parse_int(record.get("Strength", ""), 0),
            "Aisle": parse_int(record.get("Aisle", ""), 0),
            "Caustic": parse_int(record.get("Caustic", ""), 0),
            "Length": parse_int(record.get("Length", ""), 0),
            "Width": parse_int(record.get("Width", ""), 0),
            "Height": parse_int(record.get("Height", ""), 0),
        }
    return result


def attach_item_metadata(boxes, metadata):
    if not metadata:
        return

    for box in boxes:
        sku = infer_sku(box["ID"])
        box["SKU"] = sku
        item = metadata.get(sku)
        if not item:
            continue
        box["Weight"] = item["Weight"]
        box["Strength"] = item["Strength"]
        box["Aisle"] = item["Aisle"]
        box["Caustic"] = item["Caustic"]


def make_faces(x, y, z, dx, dy, dz):
    p0 = [x, y, z]
    p1 = [x + dx, y, z]
    p2 = [x + dx, y + dy, z]
    p3 = [x, y + dy, z]
    p4 = [x, y, z + dz]
    p5 = [x + dx, y, z + dz]
    p6 = [x + dx, y + dy, z + dz]
    p7 = [x, y + dy, z + dz]
    return [
        [p0, p1, p2, p3],
        [p4, p5, p6, p7],
        [p0, p1, p5, p4],
        [p1, p2, p6, p5],
        [p2, p3, p7, p6],
        [p3, p0, p4, p7],
    ]


def wire_edges(x, y, z, dx, dy, dz):
    vertices = [
        (x, y, z),
        (x + dx, y, z),
        (x + dx, y + dy, z),
        (x, y + dy, z),
        (x, y, z + dz),
        (x + dx, y, z + dz),
        (x + dx, y + dy, z + dz),
        (x, y + dy, z + dz),
    ]
    pairs = [
        (0, 1),
        (1, 2),
        (2, 3),
        (3, 0),
        (4, 5),
        (5, 6),
        (6, 7),
        (7, 4),
        (0, 4),
        (1, 5),
        (2, 6),
        (3, 7),
    ]
    return [(vertices[a], vertices[b]) for a, b in pairs]


def draw_wire_box(ax, x, y, z, dx, dy, dz, color="black", linewidth=0.8, alpha=0.7):
    for a, b in wire_edges(x, y, z, dx, dy, dz):
        ax.plot(
            [a[0], b[0]],
            [a[1], b[1]],
            [a[2], b[2]],
            color=color,
            linewidth=linewidth,
            alpha=alpha,
        )


def overlaps_1d(a1, a2, b1, b2):
    return min(a2, b2) > max(a1, b1)


def boxes_intersect(a, b):
    return (
        a["x"] < b["X"]
        and a["X"] > b["x"]
        and a["y"] < b["Y"]
        and a["Y"] > b["y"]
        and a["z"] < b["Z"]
        and a["Z"] > b["z"]
    )


def overlap_area(a, b):
    dx = min(a["X"], b["X"]) - max(a["x"], b["x"])
    dy = min(a["Y"], b["Y"]) - max(a["y"], b["y"])
    if dx <= 0 or dy <= 0:
        return 0.0
    return dx * dy


def get_box_container_id(box, pallets):
    pallet = pallets.get(box["PalletId"])
    if not pallet:
        return ""
    return pallet.get("container_id", "")


def is_out_of_pallet_bounds(box, pallet, tol=1e-9):
    return (
        box["x"] < pallet["origin_x"] - tol
        or box["y"] < pallet["origin_y"] - tol
        or box["z"] < pallet["origin_z"] - tol
        or box["X"] > pallet["origin_x"] + pallet["dx"] + tol
        or box["Y"] > pallet["origin_y"] + pallet["dy"] + tol
        or box["Z"] > pallet["origin_z"] + pallet["dz"] + tol
    )


def support_area(box, same_pallet_boxes, tol=1e-9):
    area = 0.0
    for other in same_pallet_boxes:
        if other["ID"] == box["ID"]:
            continue
        if abs(other["Z"] - box["z"]) > tol:
            continue
        area += overlap_area(box, other)
    return area


def is_point_supported(cx, cy, z, same_pallet_boxes, skip_id, tol=1e-9):
    for other in same_pallet_boxes:
        if other["ID"] == skip_id:
            continue
        if abs(other["Z"] - z) > tol:
            continue
        if other["x"] - tol <= cx <= other["X"] + tol and other["y"] - tol <= cy <= other["Y"] + tol:
            return True
    return False


def has_four_corner_support(box, same_pallet_boxes, pallet_origin_z=0.0, tol=1e-9):
    if abs(box["z"] - pallet_origin_z) <= tol:
        return True

    corners = [
        (box["x"], box["y"]),
        (box["X"], box["y"]),
        (box["x"], box["Y"]),
        (box["X"], box["Y"]),
    ]

    for cx, cy in corners:
        if not is_point_supported(cx, cy, box["z"], same_pallet_boxes, box["ID"], tol):
            return False

    return True


def support_ratio(box, same_pallet_boxes, pallet_origin_z=0.0, tol=1e-9):
    if abs(box["z"] - pallet_origin_z) <= tol:
        return 1.0
    footprint = box["dx"] * box["dy"]
    if footprint <= 0:
        return 0.0
    return min(1.0, support_area(box, same_pallet_boxes, tol) / footprint)


def center_is_supported(box, same_pallet_boxes, pallet_origin_z=0.0, tol=1e-9):
    if abs(box["z"] - pallet_origin_z) <= tol:
        return True
    return is_point_supported(
        (box["x"] + box["X"]) / 2.0,
        (box["y"] + box["Y"]) / 2.0,
        box["z"],
        same_pallet_boxes,
        box["ID"],
        tol,
    )


def analyze_layout(containers, pallets, boxes):
    out_of_bounds = []
    intersections = []
    unsupported_corners = []
    weak_support_area = []
    unsupported_center = []
    missing_pallet = []
    pallet_out_of_container = []
    pallet_overlaps = []
    support_ratios = {}
    issue_by_box = defaultdict(list)
    issue_by_pallet = defaultdict(list)

    boxes_by_pallet = defaultdict(list)
    for box in boxes:
        boxes_by_pallet[box["PalletId"]].append(box)

    for box in boxes:
        pallet = pallets.get(box["PalletId"])
        if pallet is None:
            missing_pallet.append(str(box["ID"]))
            issue_by_box[box["ID"]].append("missing pallet")
            continue

        same_pallet_boxes = boxes_by_pallet[box["PalletId"]]
        if is_out_of_pallet_bounds(box, pallet):
            out_of_bounds.append(str(box["ID"]))
            issue_by_box[box["ID"]].append("outside pallet")

        ratio = support_ratio(box, same_pallet_boxes, pallet["origin_z"])
        support_ratios[box["ID"]] = ratio

        if not has_four_corner_support(box, same_pallet_boxes, pallet["origin_z"]):
            unsupported_corners.append(str(box["ID"]))
            issue_by_box[box["ID"]].append("corner support")

        if ratio < MIN_SUPPORT_AREA_RATIO:
            weak_support_area.append((str(box["ID"]), ratio))
            issue_by_box[box["ID"]].append(f"support area {ratio:.0%}")

        if not center_is_supported(box, same_pallet_boxes, pallet["origin_z"]):
            unsupported_center.append(str(box["ID"]))
            issue_by_box[box["ID"]].append("center support")

    boxes_by_container = defaultdict(list)
    for box in boxes:
        boxes_by_container[get_box_container_id(box, pallets)].append(box)

    for container_id, container_boxes in boxes_by_container.items():
        for a, b in itertools.combinations(container_boxes, 2):
            if boxes_intersect(a, b):
                intersections.append((str(a["ID"]), str(b["ID"]), container_id))
                issue_by_box[a["ID"]].append(f"intersects {b['ID']}")
                issue_by_box[b["ID"]].append(f"intersects {a['ID']}")

    for pallet in pallets.values():
        container_id = pallet.get("container_id", "")
        if not container_id:
            continue
        container = containers.get(container_id)
        if container is None:
            pallet_out_of_container.append((pallet["id"], container_id))
            issue_by_pallet[pallet["id"]].append("missing container")
            continue
        if (
            pallet["origin_x"] < container["origin_x"]
            or pallet["origin_y"] < container["origin_y"]
            or pallet["origin_z"] < container["origin_z"]
            or pallet["origin_x"] + pallet["dx"] > container["origin_x"] + container["dx"]
            or pallet["origin_y"] + pallet["dy"] > container["origin_y"] + container["dy"]
            or pallet["origin_z"] + pallet["dz"] > container["origin_z"] + container["dz"]
        ):
            pallet_out_of_container.append((pallet["id"], container_id))
            issue_by_pallet[pallet["id"]].append("outside container")

    pallets_by_container = defaultdict(list)
    for pallet in pallets.values():
        pallets_by_container[pallet.get("container_id", "")].append(pallet)

    for container_id, pallet_list in pallets_by_container.items():
        for a, b in itertools.combinations(pallet_list, 2):
            if (
                overlaps_1d(a["origin_x"], a["origin_x"] + a["dx"], b["origin_x"], b["origin_x"] + b["dx"])
                and overlaps_1d(a["origin_y"], a["origin_y"] + a["dy"], b["origin_y"], b["origin_y"] + b["dy"])
                and overlaps_1d(a["origin_z"], a["origin_z"] + a["dz"], b["origin_z"], b["origin_z"] + b["dz"])
            ):
                pallet_overlaps.append((a["id"], b["id"], container_id))
                issue_by_pallet[a["id"]].append(f"overlaps {b['id']}")
                issue_by_pallet[b["id"]].append(f"overlaps {a['id']}")

    return {
        "out_of_bounds": out_of_bounds,
        "intersections": intersections,
        "unsupported_corners": unsupported_corners,
        "weak_support_area": weak_support_area,
        "unsupported_center": unsupported_center,
        "missing_pallet": missing_pallet,
        "pallet_out_of_container": pallet_out_of_container,
        "pallet_overlaps": pallet_overlaps,
        "support_ratios": support_ratios,
        "issue_by_box": issue_by_box,
        "issue_by_pallet": issue_by_pallet,
    }


def selected_layout(containers, pallets, boxes, container_id=None, pallet_id=None):
    if pallet_id:
        if pallet_id not in pallets:
            raise ValueError(f"Unknown pallet id: {pallet_id}")
        selected_pallets = {pallet_id: pallets[pallet_id]}
        selected_boxes = [box for box in boxes if box["PalletId"] == pallet_id]
        selected_container_ids = {pallets[pallet_id].get("container_id", "")}
        selected_containers = {cid: containers[cid] for cid in selected_container_ids if cid in containers}
        title = f"Pallet {pallet_id}"
    elif container_id:
        if container_id not in containers:
            raise ValueError(f"Unknown container id: {container_id}")
        selected_containers = {container_id: containers[container_id]}
        selected_pallets = {pid: p for pid, p in pallets.items() if p.get("container_id") == container_id}
        selected_boxes = [box for box in boxes if box["PalletId"] in selected_pallets]
        title = f"Container {container_id}"
    else:
        default_container_id = next(iter(containers.keys()), "")
        if not default_container_id:
            raise ValueError("Nothing to draw.")
        selected_containers = {default_container_id: containers[default_container_id]}
        selected_pallets = {pid: p for pid, p in pallets.items() if p.get("container_id") == default_container_id}
        selected_boxes = [box for box in boxes if box["PalletId"] in selected_pallets]
        title = f"Container {default_container_id}"

    return selected_containers, selected_pallets, selected_boxes, title


def build_palette(values, cmap_name="tab20"):
    cmap = plt.get_cmap(cmap_name)
    colors = {}
    ordered = sorted(str(v) for v in values)
    for idx, item in enumerate(ordered):
        colors[item] = cmap(idx % cmap.N)
    return colors


def color_key_for_box(box, color_by, analysis):
    if color_by == "sku":
        return str(box.get("SKU") or infer_sku(box["ID"]))
    if color_by == "aisle":
        aisle = box.get("Aisle", 0)
        return f"Aisle {aisle}" if aisle else "Aisle ?"
    if color_by == "caustic":
        return "Caustic" if box.get("Caustic", 0) else "Regular"
    if color_by == "height":
        return "height"
    if color_by == "status":
        return STATUS_ERROR if box["ID"] in analysis["issue_by_box"] else STATUS_OK
    return str(box["PalletId"])


def box_color(box, color_by, palette, analysis, z_min=0.0, z_max=1.0):
    key = color_key_for_box(box, color_by, analysis)
    if color_by == "height":
        span = max(1.0, z_max - z_min)
        t = max(0.0, min(1.0, (((box["z"] + box["Z"]) / 2.0) - z_min) / span))
        return plt.get_cmap("viridis")(t)
    if color_by == "status":
        return (0.90, 0.12, 0.12, 1.0) if key == STATUS_ERROR else (0.20, 0.55, 0.85, 1.0)
    return palette.get(str(key), (0.50, 0.50, 0.50, 1.0))


def color_values_for_boxes(boxes, color_by, analysis):
    if color_by == "height":
        return []
    return {color_key_for_box(box, color_by, analysis) for box in boxes}


def selected_bounds(selected_containers, selected_pallets, selected_boxes):
    things = []
    for c in selected_containers.values():
        things.append((c["origin_x"], c["origin_y"], c["origin_z"], c["origin_x"] + c["dx"], c["origin_y"] + c["dy"], c["origin_z"] + c["dz"]))
    for p in selected_pallets.values():
        things.append((p["origin_x"], p["origin_y"], p["origin_z"], p["origin_x"] + p["dx"], p["origin_y"] + p["dy"], p["origin_z"] + p["dz"]))
    for b in selected_boxes:
        things.append((b["x"], b["y"], b["z"], b["X"], b["Y"], b["Z"]))

    if not things:
        return 0, 1, 0, 1, 0, 1

    x_min = min(t[0] for t in things)
    y_min = min(t[1] for t in things)
    z_min = min(t[2] for t in things)
    x_max = max(t[3] for t in things)
    y_max = max(t[4] for t in things)
    z_max = max(t[5] for t in things)
    return x_min, x_max, y_min, y_max, z_min, z_max


def draw_3d(ax, selected_containers, selected_pallets, selected_boxes, analysis, color_by="pallet", show_ids=False):
    x_min, x_max, y_min, y_max, z_min, z_max = selected_bounds(selected_containers, selected_pallets, selected_boxes)
    palette = build_palette(color_values_for_boxes(selected_boxes, color_by, analysis))

    for container in selected_containers.values():
        draw_wire_box(
            ax,
            container["origin_x"],
            container["origin_y"],
            container["origin_z"],
            container["dx"],
            container["dy"],
            container["dz"],
            color="#222222",
            linewidth=0.9,
            alpha=0.65,
        )

    for pallet in selected_pallets.values():
        base_height = max(12.0, min(45.0, pallet["dz"] * 0.025))
        faces = make_faces(
            pallet["origin_x"],
            pallet["origin_y"],
            pallet["origin_z"],
            pallet["dx"],
            pallet["dy"],
            base_height,
        )
        poly = Poly3DCollection(
            faces,
            alpha=0.16,
            linewidths=0.8,
            facecolors=(0.30, 0.30, 0.30, 1.0),
            edgecolors="#333333",
        )
        ax.add_collection3d(poly)
        ax.text(
            pallet["origin_x"] + pallet["dx"] / 2,
            pallet["origin_y"] + pallet["dy"] / 2,
            pallet["origin_z"] + base_height,
            pallet["id"],
            fontsize=7,
            ha="center",
        )

    for box in sorted(selected_boxes, key=lambda b: (b["z"], b["y"], b["x"])):
        has_issue = box["ID"] in analysis["issue_by_box"]
        color = box_color(box, color_by, palette, analysis, z_min, z_max)
        faces = make_faces(box["x"], box["y"], box["z"], box["dx"], box["dy"], box["dz"])
        poly = Poly3DCollection(
            faces,
            alpha=0.54 if not has_issue else 0.86,
            linewidths=0.35 if not has_issue else 0.95,
            facecolors=color,
            edgecolors="#111111" if not has_issue else "#ff0000",
        )
        ax.add_collection3d(poly)

        if show_ids:
            ax.text(
                (box["x"] + box["X"]) / 2,
                (box["y"] + box["Y"]) / 2,
                (box["z"] + box["Z"]) / 2,
                str(box["ID"]),
                fontsize=5.5,
                ha="center",
            )

    ax.set_xlim(x_min, x_max)
    ax.set_ylim(y_min, y_max)
    ax.set_zlim(z_min, z_max)
    ax.set_xlabel("X, mm")
    ax.set_ylabel("Y, mm")
    ax.set_zlabel("Z, mm")
    ax.view_init(elev=22, azim=-58)
    ax.set_title("3D layout")

    try:
        ax.set_box_aspect((max(1.0, x_max - x_min), max(1.0, y_max - y_min), max(1.0, z_max - z_min)))
    except Exception:
        pass


def draw_top(ax, selected_pallets, selected_boxes, analysis, color_by="pallet", show_ids=False):
    _, _, _, _, z_min, z_max = selected_bounds({}, selected_pallets, selected_boxes)
    palette = build_palette(color_values_for_boxes(selected_boxes, color_by, analysis))

    for pallet in selected_pallets.values():
        rect = Rectangle(
            (pallet["origin_x"], pallet["origin_y"]),
            pallet["dx"],
            pallet["dy"],
            fill=False,
            edgecolor="#111111",
            linewidth=1.4,
        )
        ax.add_patch(rect)
        ax.text(
            pallet["origin_x"] + pallet["dx"] / 2,
            pallet["origin_y"] + pallet["dy"] / 2,
            pallet["id"],
            ha="center",
            va="center",
            fontsize=8,
            color="#222222",
            alpha=0.55,
        )

    for box in sorted(selected_boxes, key=lambda b: b["z"]):
        has_issue = box["ID"] in analysis["issue_by_box"]
        color = box_color(box, color_by, palette, analysis, z_min, z_max)
        rect = Rectangle(
            (box["x"], box["y"]),
            box["dx"],
            box["dy"],
            facecolor=color,
            edgecolor="#ff0000" if has_issue else "#222222",
            linewidth=1.0 if has_issue else 0.35,
            alpha=0.62 if not has_issue else 0.86,
        )
        ax.add_patch(rect)
        if show_ids:
            ax.text(
                (box["x"] + box["X"]) / 2,
                (box["y"] + box["Y"]) / 2,
                str(box["ID"]),
                fontsize=5,
                ha="center",
                va="center",
            )

    x_min, x_max, y_min, y_max, _, _ = selected_bounds({}, selected_pallets, selected_boxes)
    ax.set_xlim(x_min, x_max)
    ax.set_ylim(y_min, y_max)
    ax.set_aspect("equal", adjustable="box")
    ax.set_xlabel("X, mm")
    ax.set_ylabel("Y, mm")
    ax.set_title("Top view")
    ax.grid(True, linewidth=0.25, alpha=0.35)


def draw_side(ax, selected_pallets, selected_boxes, analysis, color_by="pallet", axis="x", show_ids=False):
    _, _, _, _, z_min, z_max = selected_bounds({}, selected_pallets, selected_boxes)
    palette = build_palette(color_values_for_boxes(selected_boxes, color_by, analysis))

    for pallet in selected_pallets.values():
        if axis == "x":
            start = pallet["origin_x"]
            width = pallet["dx"]
        else:
            start = pallet["origin_y"]
            width = pallet["dy"]
        rect = Rectangle(
            (start, pallet["origin_z"]),
            width,
            max(12.0, min(45.0, pallet["dz"] * 0.025)),
            facecolor="#444444",
            edgecolor="#111111",
            linewidth=0.6,
            alpha=0.20,
        )
        ax.add_patch(rect)

    for box in sorted(selected_boxes, key=lambda b: b["z"]):
        has_issue = box["ID"] in analysis["issue_by_box"]
        color = box_color(box, color_by, palette, analysis, z_min, z_max)
        if axis == "x":
            start = box["x"]
            width = box["dx"]
        else:
            start = box["y"]
            width = box["dy"]
        rect = Rectangle(
            (start, box["z"]),
            width,
            box["dz"],
            facecolor=color,
            edgecolor="#ff0000" if has_issue else "#222222",
            linewidth=1.0 if has_issue else 0.35,
            alpha=0.58 if not has_issue else 0.86,
        )
        ax.add_patch(rect)
        if show_ids:
            ax.text(
                start + width / 2,
                (box["z"] + box["Z"]) / 2,
                str(box["ID"]),
                fontsize=5,
                ha="center",
                va="center",
            )

    x_min, x_max, y_min, y_max, z_min, z_max = selected_bounds({}, selected_pallets, selected_boxes)
    if axis == "x":
        ax.set_xlim(x_min, x_max)
        ax.set_xlabel("X, mm")
        title = "Side view X-Z"
    else:
        ax.set_xlim(y_min, y_max)
        ax.set_xlabel("Y, mm")
        title = "Side view Y-Z"
    ax.set_ylim(z_min, z_max)
    ax.set_ylabel("Z, mm")
    ax.set_title(title)
    ax.grid(True, linewidth=0.25, alpha=0.35)


def pallet_metrics(containers, pallets, boxes, analysis):
    boxes_by_pallet = defaultdict(list)
    for box in boxes:
        boxes_by_pallet[box["PalletId"]].append(box)

    rows = []
    for pallet_id, pallet in sorted(pallets.items()):
        pallet_boxes = boxes_by_pallet.get(pallet_id, [])
        if pallet_boxes:
            height = max(box["Z"] for box in pallet_boxes) - pallet["origin_z"]
            volume = sum(box["volume"] for box in pallet_boxes)
            weight = sum(float(box.get("Weight", 0) or 0) for box in pallet_boxes)
            bounding_volume = pallet["dx"] * pallet["dy"] * max(0.0, height)
            pallet_volume = pallet["dx"] * pallet["dy"] * pallet["dz"]
            fill_height = volume / bounding_volume if bounding_volume > 0 else 0.0
            fill_pallet = volume / pallet_volume if pallet_volume > 0 else 0.0
            avg_support = sum(analysis["support_ratios"].get(box["ID"], 1.0) for box in pallet_boxes) / len(pallet_boxes)
        else:
            height = volume = weight = fill_height = fill_pallet = avg_support = 0.0

        rows.append(
            {
                "pallet_id": pallet_id,
                "container_id": pallet.get("container_id", ""),
                "boxes": len(pallet_boxes),
                "height": height,
                "volume": volume,
                "weight": weight,
                "fill_height": fill_height,
                "fill_pallet": fill_pallet,
                "avg_support": avg_support,
                "issues": len(analysis["issue_by_pallet"].get(pallet_id, []))
                + sum(1 for b in pallet_boxes if b["ID"] in analysis["issue_by_box"]),
            }
        )

    return rows


def draw_summary(ax, containers, pallets, boxes, analysis):
    ax.axis("off")
    rows = pallet_metrics(containers, pallets, boxes, analysis)
    error_boxes = len(analysis["issue_by_box"])
    error_pallets = len(analysis["issue_by_pallet"])
    total_weight = sum(float(box.get("Weight", 0) or 0) for box in boxes)
    total_volume = sum(box["volume"] for box in boxes)

    lines = [
        "Summary",
        f"Containers: {len(containers)}",
        f"Pallets: {len(pallets)}",
        f"Boxes: {len(boxes)}",
        f"Total box volume: {total_volume:,.0f} mm^3",
    ]
    if total_weight > 0:
        lines.append(f"Known total weight: {total_weight / 1000.0:,.2f} kg")
    lines.extend(
        [
            f"Boxes with issues: {error_boxes}",
            f"Pallets with issues: {error_pallets}",
            "",
            "Per pallet:",
        ]
    )

    for row in rows[:12]:
        weight_text = f", {row['weight'] / 1000.0:.1f} kg" if row["weight"] > 0 else ""
        lines.append(
            f"{row['pallet_id']}: boxes={row['boxes']}, "
            f"h={row['height']:.0f} mm, "
            f"fill={row['fill_height']:.1%}, "
            f"support={row['avg_support']:.0%}"
            f"{weight_text}"
        )

    if len(rows) > 12:
        lines.append(f"... and {len(rows) - 12} more pallets")

    if error_boxes:
        lines.extend(["", "First box issues:"])
        for box_id, issues in list(analysis["issue_by_box"].items())[:8]:
            lines.append(f"{box_id}: {', '.join(issues[:2])}")

    ax.text(0.0, 1.0, "\n".join(lines), transform=ax.transAxes, va="top", family="monospace", fontsize=8.5)


def draw_dashboard(containers, pallets, boxes, analysis, container_id=None, pallet_id=None, save_to=None, show_ids=False, color_by="pallet"):
    selected_containers, selected_pallets, selected_boxes, title = selected_layout(containers, pallets, boxes, container_id, pallet_id)

    fig = plt.figure(figsize=(17, 11))
    grid = fig.add_gridspec(2, 3, width_ratios=[1.35, 1.0, 0.95], height_ratios=[1.05, 0.95])

    ax3d = fig.add_subplot(grid[:, 0], projection="3d")
    ax_top = fig.add_subplot(grid[0, 1])
    ax_side = fig.add_subplot(grid[1, 1])
    ax_summary = fig.add_subplot(grid[:, 2])

    draw_3d(ax3d, selected_containers, selected_pallets, selected_boxes, analysis, color_by, show_ids)
    draw_top(ax_top, selected_pallets, selected_boxes, analysis, color_by, show_ids)
    draw_side(ax_side, selected_pallets, selected_boxes, analysis, color_by, axis="x", show_ids=show_ids)
    draw_summary(ax_summary, containers, pallets, boxes, analysis)

    fig.suptitle(f"Packing viewer - {title}", fontsize=15, y=0.98)
    fig.tight_layout(rect=(0, 0, 1, 0.965))
    finish_figure(fig, save_to)


def draw_single_view(view, containers, pallets, boxes, analysis, container_id=None, pallet_id=None, save_to=None, show_ids=False, color_by="pallet"):
    selected_containers, selected_pallets, selected_boxes, title = selected_layout(containers, pallets, boxes, container_id, pallet_id)

    if view == "3d":
        fig = plt.figure(figsize=(13, 10))
        ax = fig.add_subplot(111, projection="3d")
        draw_3d(ax, selected_containers, selected_pallets, selected_boxes, analysis, color_by, show_ids)
    elif view == "top":
        fig, ax = plt.subplots(figsize=(12, 9))
        draw_top(ax, selected_pallets, selected_boxes, analysis, color_by, show_ids)
    elif view == "side":
        fig, ax = plt.subplots(figsize=(12, 8))
        draw_side(ax, selected_pallets, selected_boxes, analysis, color_by, axis="x", show_ids=show_ids)
    else:
        raise ValueError(f"Unsupported single view: {view}")

    fig.suptitle(f"{view.upper()} - {title}", fontsize=14)
    fig.tight_layout()
    finish_figure(fig, save_to)


def draw_layers(containers, pallets, boxes, analysis, container_id=None, pallet_id=None, save_to=None, show_ids=False, color_by="pallet", max_layers=12):
    _, selected_pallets, selected_boxes, title = selected_layout(containers, pallets, boxes, container_id, pallet_id)

    if not selected_boxes:
        raise ValueError("No boxes selected for layer view.")

    layers = defaultdict(list)
    for box in selected_boxes:
        layers[(box["PalletId"], round(box["z"], 6))].append(box)

    ordered_layers = sorted(layers.items(), key=lambda item: (item[0][0], item[0][1]))[:max_layers]
    cols = min(3, max(1, len(ordered_layers)))
    rows = int(math.ceil(len(ordered_layers) / cols))
    fig, axes = plt.subplots(rows, cols, figsize=(5.4 * cols, 4.8 * rows), squeeze=False)
    palette = build_palette(color_values_for_boxes(selected_boxes, color_by, analysis))
    _, _, _, _, z_min, z_max = selected_bounds({}, selected_pallets, selected_boxes)

    for ax in axes.flat:
        ax.axis("off")

    for idx, ((pallet_id, z), layer_boxes) in enumerate(ordered_layers):
        ax = axes.flat[idx]
        ax.axis("on")
        pallet = selected_pallets[pallet_id]
        ax.add_patch(
            Rectangle(
                (pallet["origin_x"], pallet["origin_y"]),
                pallet["dx"],
                pallet["dy"],
                fill=False,
                edgecolor="#111111",
                linewidth=1.3,
            )
        )

        for box in sorted(layer_boxes, key=lambda b: (b["y"], b["x"])):
            has_issue = box["ID"] in analysis["issue_by_box"]
            color = box_color(box, color_by, palette, analysis, z_min, z_max)
            ax.add_patch(
                Rectangle(
                    (box["x"], box["y"]),
                    box["dx"],
                    box["dy"],
                    facecolor=color,
                    edgecolor="#ff0000" if has_issue else "#222222",
                    linewidth=1.0 if has_issue else 0.35,
                    alpha=0.72,
                )
            )
            if show_ids:
                ax.text(
                    (box["x"] + box["X"]) / 2,
                    (box["y"] + box["Y"]) / 2,
                    str(box["ID"]),
                    fontsize=5,
                    ha="center",
                    va="center",
                )

        ax.set_title(f"{pallet_id}, bottom z={z:.0f} mm, boxes={len(layer_boxes)}")
        ax.set_xlim(pallet["origin_x"], pallet["origin_x"] + pallet["dx"])
        ax.set_ylim(pallet["origin_y"], pallet["origin_y"] + pallet["dy"])
        ax.set_aspect("equal", adjustable="box")
        ax.grid(True, linewidth=0.25, alpha=0.35)

    fig.suptitle(f"Layer view - {title}", fontsize=14)
    fig.tight_layout(rect=(0, 0, 1, 0.96))
    finish_figure(fig, save_to)


def finish_figure(fig, save_to=None):
    if save_to:
        save_path = Path(save_to)
        save_path.parent.mkdir(parents=True, exist_ok=True)
        fig.savefig(save_path, dpi=180, bbox_inches="tight")
        print(f"Saved image to: {save_path}")
        plt.close(fig)
    else:
        plt.show()


def write_report_csv(path, containers, pallets, boxes, analysis):
    report_path = Path(path)
    report_path.parent.mkdir(parents=True, exist_ok=True)
    rows = pallet_metrics(containers, pallets, boxes, analysis)
    with open(report_path, "w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(
            f,
            fieldnames=[
                "pallet_id",
                "container_id",
                "boxes",
                "height",
                "volume",
                "weight",
                "fill_height",
                "fill_pallet",
                "avg_support",
                "issues",
            ],
        )
        writer.writeheader()
        writer.writerows(rows)
    print(f"Saved report to: {report_path}")


def print_summary(containers, pallets, boxes, analysis):
    print("\n=== Layout Summary ===")
    print(f"Containers: {len(containers)}")
    print(f"Pallets: {len(pallets)}")
    print(f"Boxes: {len(boxes)}")
    print(f"Container IDs: {', '.join(containers.keys()) if containers else '-'}")
    print(f"Pallet IDs: {', '.join(pallets.keys()) if pallets else '-'}")

    rows = pallet_metrics(containers, pallets, boxes, analysis)
    if rows:
        print("\nPer-pallet metrics:")
        for row in rows:
            weight_text = f", weight={row['weight'] / 1000.0:.2f}kg" if row["weight"] > 0 else ""
            print(
                f"  {row['pallet_id']}: boxes={row['boxes']}, "
                f"height={row['height']:.0f}mm, "
                f"fill_by_used_height={row['fill_height']:.1%}, "
                f"avg_support={row['avg_support']:.0%}, "
                f"issues={row['issues']}"
                f"{weight_text}"
            )

    def print_list(title, values, empty_text="none"):
        if values:
            print(f"\n{title}:")
            for value in values[:50]:
                print(f"  {value}")
            if len(values) > 50:
                print(f"  ... and {len(values) - 50} more")
        else:
            print(f"\n{title}: {empty_text}")

    print_list("Boxes with missing pallet ids", analysis["missing_pallet"])
    print_list("Boxes outside pallet bounds", analysis["out_of_bounds"])
    print_list("Boxes without 4-corner support", analysis["unsupported_corners"])
    print_list("Boxes below support-area threshold", [f"{box_id}: {ratio:.1%}" for box_id, ratio in analysis["weak_support_area"]])
    print_list("Boxes without center support", analysis["unsupported_center"])
    print_list("Box intersections", [f"{a} <-> {b} in {cid or '?'}" for a, b, cid in analysis["intersections"]])
    print_list("Pallets outside container bounds", [f"{pallet_id} in {container_id}" for pallet_id, container_id in analysis["pallet_out_of_container"]])
    print_list("Overlapping pallets inside containers", [f"{a} <-> {b} in {cid}" for a, b, cid in analysis["pallet_overlaps"]])


def output_path_for_view(args, view):
    if args.save_dir:
        stem = Path(args.file).stem
        return str(Path(args.save_dir) / f"{stem}-{view}.png")

    if not args.save:
        return None

    if args.view != "all":
        return args.save

    path = Path(args.save)
    return str(path.with_name(f"{path.stem}-{view}{path.suffix or '.png'}"))


def main():
    parser = argparse.ArgumentParser(description="Visualize and diagnose packing layout from packed CSV.")
    parser.add_argument("file", help="Packed CSV file")
    parser.add_argument("--input", help="Original order CSV. Adds SKU, weight, strength, aisle and caustic metadata.")
    parser.add_argument("--save", help="Save figure to PNG. With --view all, suffixes are added.")
    parser.add_argument("--save-dir", help="Save generated figures into this directory.")
    parser.add_argument("--report-csv", help="Write per-pallet metrics to CSV.")
    parser.add_argument("--show-ids", action="store_true", help="Draw box IDs")
    parser.add_argument("--container", help="Draw only one container by id")
    parser.add_argument("--pallet", help="Draw only one pallet by id")
    parser.add_argument(
        "--view",
        choices=["dashboard", "3d", "top", "side", "layers", "all"],
        default="dashboard",
        help="Visualization mode.",
    )
    parser.add_argument(
        "--color-by",
        choices=["pallet", "sku", "aisle", "height", "status", "caustic"],
        default="pallet",
        help="How to color boxes.",
    )
    parser.add_argument("--max-layers", type=int, default=12, help="Maximum number of layer panels for --view layers.")
    args = parser.parse_args()

    setup_matplotlib(use_agg=bool(args.save or args.save_dir))

    containers, pallets, boxes = load_layout_csv(args.file)
    metadata = load_item_metadata(args.input)
    attach_item_metadata(boxes, metadata)
    analysis = analyze_layout(containers, pallets, boxes)

    print_summary(containers, pallets, boxes, analysis)

    if args.report_csv:
        write_report_csv(args.report_csv, containers, pallets, boxes, analysis)

    views = ["dashboard", "3d", "top", "side", "layers"] if args.view == "all" else [args.view]

    for view in views:
        save_to = output_path_for_view(args, view)
        if view == "dashboard":
            draw_dashboard(
                containers,
                pallets,
                boxes,
                analysis,
                container_id=args.container,
                pallet_id=args.pallet,
                save_to=save_to,
                show_ids=args.show_ids,
                color_by=args.color_by,
            )
        elif view == "layers":
            draw_layers(
                containers,
                pallets,
                boxes,
                analysis,
                container_id=args.container,
                pallet_id=args.pallet,
                save_to=save_to,
                show_ids=args.show_ids,
                color_by=args.color_by,
                max_layers=args.max_layers,
            )
        else:
            draw_single_view(
                view,
                containers,
                pallets,
                boxes,
                analysis,
                container_id=args.container,
                pallet_id=args.pallet,
                save_to=save_to,
                show_ids=args.show_ids,
                color_by=args.color_by,
            )


if __name__ == "__main__":
    main()
