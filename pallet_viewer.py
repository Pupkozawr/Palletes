from __future__ import annotations

import argparse
import csv
import itertools
from collections import defaultdict
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection


def parse_float(value: str) -> float:
    return float(str(value).strip())


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


def is_out_of_pallet_bounds(box, pallet):
    return (
        box["x"] < pallet["origin_x"]
        or box["y"] < pallet["origin_y"]
        or box["z"] < pallet["origin_z"]
        or box["X"] > pallet["origin_x"] + pallet["dx"]
        or box["Y"] > pallet["origin_y"] + pallet["dy"]
        or box["Z"] > pallet["origin_z"] + pallet["dz"]
    )


def has_four_corner_support(box, same_pallet_boxes, tol=1e-9):
    if abs(box["z"]) <= tol:
        return True

    corners = [
        (box["x"], box["y"]),
        (box["X"], box["y"]),
        (box["x"], box["Y"]),
        (box["X"], box["Y"]),
    ]

    for cx, cy in corners:
        supported = False
        for other in same_pallet_boxes:
            if other["ID"] == box["ID"]:
                continue
            if abs(other["Z"] - box["z"]) > tol:
                continue
            if other["x"] - tol <= cx <= other["X"] + tol and other["y"] - tol <= cy <= other["Y"] + tol:
                supported = True
                break
        if not supported:
            return False

    return True


def analyze_layout(containers, pallets, boxes):
    out_of_bounds = []
    intersections = []
    unsupported = []
    missing_pallet = []
    pallet_out_of_container = []
    pallet_overlaps = []

    boxes_by_pallet = defaultdict(list)
    for box in boxes:
        boxes_by_pallet[box["PalletId"]].append(box)

    for box in boxes:
        pallet = pallets.get(box["PalletId"])
        if pallet is None:
            missing_pallet.append(str(box["ID"]))
            continue
        if is_out_of_pallet_bounds(box, pallet):
            out_of_bounds.append(str(box["ID"]))
        if not has_four_corner_support(box, boxes_by_pallet[box["PalletId"]]):
            unsupported.append(str(box["ID"]))

    for a, b in itertools.combinations(boxes, 2):
        if boxes_intersect(a, b):
            intersections.append((str(a["ID"]), str(b["ID"])))

    for pallet in pallets.values():
        container_id = pallet.get("container_id", "")
        if not container_id:
            continue
        container = containers.get(container_id)
        if container is None:
            pallet_out_of_container.append((pallet["id"], container_id))
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

    return {
        "out_of_bounds": out_of_bounds,
        "intersections": intersections,
        "unsupported": unsupported,
        "missing_pallet": missing_pallet,
        "pallet_out_of_container": pallet_out_of_container,
        "pallet_overlaps": pallet_overlaps,
    }


def build_palette(items):
    cmap = plt.get_cmap("tab20")
    colors = {}
    for idx, item in enumerate(sorted(items)):
        colors[item] = cmap(idx % 20)
    return colors


def draw_scene(containers, pallets, boxes, container_id=None, pallet_id=None, save_to=None, show_ids=False):
    if pallet_id:
        if pallet_id not in pallets:
            raise ValueError(f"Unknown pallet id: {pallet_id}")
        selected_pallets = {pallet_id: pallets[pallet_id]}
        selected_boxes = [box for box in boxes if box["PalletId"] == pallet_id]
        selected_container_ids = {pallets[pallet_id].get("container_id", "")}
        selected_containers = {cid: containers[cid] for cid in selected_container_ids if cid in containers}
    elif container_id:
        if container_id not in containers:
            raise ValueError(f"Unknown container id: {container_id}")
        selected_containers = {container_id: containers[container_id]}
        selected_pallets = {pid: p for pid, p in pallets.items() if p.get("container_id") == container_id}
        selected_boxes = [box for box in boxes if box["PalletId"] in selected_pallets]
    else:
        default_container_id = next(iter(containers.keys()), "")
        if not default_container_id:
            raise ValueError("Nothing to draw.")
        selected_containers = {default_container_id: containers[default_container_id]}
        selected_pallets = {pid: p for pid, p in pallets.items() if p.get("container_id") == default_container_id}
        selected_boxes = [box for box in boxes if box["PalletId"] in selected_pallets]

    analysis = analyze_layout(containers, pallets, boxes)
    bad_ids = set(analysis["out_of_bounds"] + analysis["unsupported"] + analysis["missing_pallet"])
    for a, b in analysis["intersections"]:
        bad_ids.add(a)
        bad_ids.add(b)

    fig = plt.figure(figsize=(13, 10))
    ax = fig.add_subplot(111, projection="3d")

    colors = build_palette(selected_pallets.keys())

    for container in selected_containers.values():
        faces = make_faces(
            container["origin_x"],
            container["origin_y"],
            container["origin_z"],
            container["dx"],
            container["dy"],
            container["dz"],
        )
        poly = Poly3DCollection(faces, alpha=0.04, linewidths=0.8, edgecolors="black")
        ax.add_collection3d(poly)

    for pallet in selected_pallets.values():
        base_height = max(10.0, pallet["dz"] * 0.02)
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
            alpha=0.10,
            linewidths=0.5,
            facecolors=colors[pallet["id"]],
            edgecolors=colors[pallet["id"]],
        )
        ax.add_collection3d(poly)
        ax.text(
            pallet["origin_x"] + pallet["dx"] / 2,
            pallet["origin_y"] + pallet["dy"] / 2,
            pallet["origin_z"] + base_height,
            pallet["id"],
            fontsize=8,
            ha="center",
        )

    for box in selected_boxes:
        faces = make_faces(box["x"], box["y"], box["z"], box["dx"], box["dy"], box["dz"])
        poly = Poly3DCollection(
            faces,
            alpha=0.38 if box["ID"] not in bad_ids else 0.72,
            linewidths=0.45,
            facecolors=colors.get(box["PalletId"], (0.5, 0.5, 0.5, 1.0)),
            edgecolors="black",
        )
        ax.add_collection3d(poly)

        if show_ids:
            ax.text(
                (box["x"] + box["X"]) / 2,
                (box["y"] + box["Y"]) / 2,
                (box["z"] + box["Z"]) / 2,
                str(box["ID"]),
                fontsize=6,
            )

    if selected_containers:
        x_min = min(c["origin_x"] for c in selected_containers.values())
        y_min = min(c["origin_y"] for c in selected_containers.values())
        z_min = min(c["origin_z"] for c in selected_containers.values())
        x_max = max(c["origin_x"] + c["dx"] for c in selected_containers.values())
        y_max = max(c["origin_y"] + c["dy"] for c in selected_containers.values())
        z_max = max(c["origin_z"] + c["dz"] for c in selected_containers.values())
        title = f"Container view: {', '.join(selected_containers.keys())}"
    else:
        pallet = next(iter(selected_pallets.values()))
        x_min = pallet["origin_x"]
        y_min = pallet["origin_y"]
        z_min = pallet["origin_z"]
        x_max = pallet["origin_x"] + pallet["dx"]
        y_max = pallet["origin_y"] + pallet["dy"]
        z_max = pallet["origin_z"] + pallet["dz"]
        title = f"Pallet view: {pallet['id']}"

    ax.set_xlim(x_min, x_max)
    ax.set_ylim(y_min, y_max)
    ax.set_zlim(z_min, z_max)
    ax.set_xlabel("X")
    ax.set_ylabel("Y")
    ax.set_zlabel("Z")
    ax.set_title(title)

    try:
        ax.set_box_aspect((max(1.0, x_max - x_min), max(1.0, y_max - y_min), max(1.0, z_max - z_min)))
    except Exception:
        pass

    print_summary(containers, pallets, boxes, analysis)

    plt.tight_layout()
    if save_to:
        plt.savefig(save_to, dpi=180, bbox_inches="tight")
        print(f"\nSaved image to: {save_to}")
    else:
        plt.show()


def print_summary(containers, pallets, boxes, analysis):
    print("\n=== Layout Summary ===")
    print(f"Containers: {len(containers)}")
    print(f"Pallets: {len(pallets)}")
    print(f"Boxes: {len(boxes)}")
    print(f"Container IDs: {', '.join(containers.keys()) if containers else '-'}")
    print(f"Pallet IDs: {', '.join(pallets.keys()) if pallets else '-'}")

    if analysis["missing_pallet"]:
        print("\nBoxes with missing pallet ids:")
        print(", ".join(analysis["missing_pallet"]))
    else:
        print("\nBoxes with missing pallet ids: none")

    if analysis["out_of_bounds"]:
        print("\nBoxes outside pallet bounds:")
        print(", ".join(analysis["out_of_bounds"]))
    else:
        print("\nBoxes outside pallet bounds: none")

    if analysis["intersections"]:
        print("\nBox intersections:")
        for a, b in analysis["intersections"][:50]:
            print(f"{a} <-> {b}")
        if len(analysis["intersections"]) > 50:
            print(f"... and {len(analysis['intersections']) - 50} more")
    else:
        print("\nBox intersections: none")

    if analysis["unsupported"]:
        print("\nBoxes without 4-corner support:")
        print(", ".join(analysis["unsupported"]))
    else:
        print("\nBoxes without 4-corner support: none")

    if analysis["pallet_out_of_container"]:
        print("\nPallets outside container bounds:")
        for pallet_id, container_id in analysis["pallet_out_of_container"]:
            print(f"{pallet_id} in {container_id}")
    else:
        print("\nPallets outside container bounds: none")

    if analysis["pallet_overlaps"]:
        print("\nOverlapping pallets inside containers:")
        for a, b, container_id in analysis["pallet_overlaps"]:
            print(f"{a} <-> {b} in {container_id}")
    else:
        print("\nOverlapping pallets inside containers: none")


def main():
    parser = argparse.ArgumentParser(description="Visualize packing layout from CSV.")
    parser.add_argument("file", help="Packed CSV file")
    parser.add_argument("--save", help="Save figure to PNG")
    parser.add_argument("--show-ids", action="store_true", help="Draw box IDs")
    parser.add_argument("--container", help="Draw only one container by id")
    parser.add_argument("--pallet", help="Draw only one pallet by id")
    args = parser.parse_args()

    containers, pallets, boxes = load_layout_csv(args.file)
    draw_scene(
        containers,
        pallets,
        boxes,
        container_id=args.container,
        pallet_id=args.pallet,
        save_to=args.save,
        show_ids=args.show_ids,
    )


if __name__ == "__main__":
    main()
