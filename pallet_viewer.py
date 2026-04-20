
from pathlib import Path
import argparse
import itertools
import pandas as pd
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection


def load_pallet_csv(path: str):

    raw = pd.read_csv(path, header=None)

    if len(raw) < 3:
        raise ValueError("Файл слишком короткий: ожидается минимум 3 строки.")

    meta = raw.iloc[0].tolist()
    header = raw.iloc[1].tolist()
    data = raw.iloc[2:].copy()
    data.columns = header

    required = ["ID", "x", "y", "z", "X", "Y", "Z"]
    missing = [c for c in required if c not in data.columns]
    if missing:
        raise ValueError(f"Не найдены обязательные столбцы: {missing}")

    for col in required[1:]:
        data[col] = pd.to_numeric(data[col], errors="raise")

    pallet_name = str(meta[1])
    origin_x = float(meta[2])
    origin_y = float(meta[3])
    origin_z = float(meta[4])
    pallet_dx = float(meta[5])
    pallet_dy = float(meta[6])
    max_height = float(meta[7])

    data["dx"] = data["X"] - data["x"]
    data["dy"] = data["Y"] - data["y"]
    data["dz"] = data["Z"] - data["z"]

    if (data[["dx", "dy", "dz"]] <= 0).any().any():
        bad = data[(data["dx"] <= 0) | (data["dy"] <= 0) | (data["dz"] <= 0)]
        raise ValueError(
            "Есть коробки с некорректными размерами (X<=x, Y<=y или Z<=z):\n"
            + bad[["ID", "x", "y", "z", "X", "Y", "Z"]].to_string(index=False)
        )

    pallet = {
        "name": pallet_name,
        "origin_x": origin_x,
        "origin_y": origin_y,
        "origin_z": origin_z,
        "dx": pallet_dx,
        "dy": pallet_dy,
        "max_height": max_height,
    }

    return pallet, data.to_dict(orient="records")


def make_faces(x, y, z, dx, dy, dz):
    p0 = [x,      y,      z]
    p1 = [x + dx, y,      z]
    p2 = [x + dx, y + dy, z]
    p3 = [x,      y + dy, z]
    p4 = [x,      y,      z + dz]
    p5 = [x + dx, y,      z + dz]
    p6 = [x + dx, y + dy, z + dz]
    p7 = [x,      y + dy, z + dz]
    return [
        [p0, p1, p2, p3],
        [p4, p5, p6, p7],
        [p0, p1, p5, p4],
        [p1, p2, p6, p5],
        [p2, p3, p7, p6],
        [p3, p0, p4, p7],
    ]


def boxes_intersect(a, b):
    return (
        a["x"] < b["X"] and a["X"] > b["x"] and
        a["y"] < b["Y"] and a["Y"] > b["y"] and
        a["z"] < b["Z"] and a["Z"] > b["z"]
    )


def is_out_of_bounds(box, pallet):
    return (
        box["x"] < pallet["origin_x"] or
        box["y"] < pallet["origin_y"] or
        box["z"] < pallet["origin_z"] or
        box["X"] > pallet["origin_x"] + pallet["dx"] or
        box["Y"] > pallet["origin_y"] + pallet["dy"] or
        box["Z"] > pallet["origin_z"] + pallet["max_height"]
    )


def has_support(box, boxes, tol=1e-9):
    if abs(box["z"]) <= tol:
        return True

    supporters = []
    for other in boxes:
        if other["ID"] == box["ID"]:
            continue
        if abs(other["Z"] - box["z"]) <= tol:
            overlap_x = max(0, min(box["X"], other["X"]) - max(box["x"], other["x"]))
            overlap_y = max(0, min(box["Y"], other["Y"]) - max(box["y"], other["y"]))
            if overlap_x > 0 and overlap_y > 0:
                supporters.append((overlap_x * overlap_y))

    return sum(supporters) > 0


def analyze_layout(pallet, boxes):
    out_of_bounds = []
    intersections = []
    unsupported = []

    for b in boxes:
        if is_out_of_bounds(b, pallet):
            out_of_bounds.append(str(b["ID"]))
        if not has_support(b, boxes):
            unsupported.append(str(b["ID"]))

    for a, b in itertools.combinations(boxes, 2):
        if boxes_intersect(a, b):
            intersections.append((str(a["ID"]), str(b["ID"])))

    return out_of_bounds, intersections, unsupported


def draw_layout(pallet, boxes, save_to=None, show_ids=False):
    out_of_bounds, intersections, unsupported = analyze_layout(pallet, boxes)

    bad_ids = set(out_of_bounds + unsupported)
    for a, b in intersections:
        bad_ids.add(a)
        bad_ids.add(b)

    fig = plt.figure(figsize=(12, 9))
    ax = fig.add_subplot(111, projection="3d")

    ox, oy, oz = pallet["origin_x"], pallet["origin_y"], pallet["origin_z"]
    px, py = pallet["dx"], pallet["dy"]
    pallet_faces = make_faces(ox, oy, oz, px, py, max(1, pallet["max_height"] * 0.02))
    pallet_poly = Poly3DCollection(pallet_faces, alpha=0.15, linewidths=0.5)
    ax.add_collection3d(pallet_poly)

    for box in boxes:
        faces = make_faces(box["x"], box["y"], box["z"], box["dx"], box["dy"], box["dz"])
        poly = Poly3DCollection(faces, alpha=0.35, linewidths=0.5)
        if str(box["ID"]) in bad_ids:
            poly.set_alpha(0.65)
        ax.add_collection3d(poly)

        if show_ids:
            cx = (box["x"] + box["X"]) / 2
            cy = (box["y"] + box["Y"]) / 2
            cz = (box["z"] + box["Z"]) / 2
            ax.text(cx, cy, cz, str(box["ID"]), fontsize=6)

    ax.set_xlim(ox, ox + px)
    ax.set_ylim(oy, oy + py)
    ax.set_zlim(oz, oz + pallet["max_height"])
    ax.set_xlabel("X")
    ax.set_ylabel("Y")
    ax.set_zlabel("Z")
    ax.set_title(f'Pallet {pallet["name"]}: {len(boxes)} boxes')

    try:
        ax.set_box_aspect((pallet["dx"], pallet["dy"], pallet["max_height"]))
    except Exception:
        pass

    print("\n=== Проверка укладки ===")
    print(f"Коробок: {len(boxes)}")
    print(f"Габарит паллеты: {pallet['dx']} x {pallet['dy']} x {pallet['max_height']}")

    if out_of_bounds:
        print("\nВне границ:")
        print(", ".join(out_of_bounds))
    else:
        print("\nВне границ: нет")

    if intersections:
        print("\nПересечения:")
        for a, b in intersections[:50]:
            print(f"{a} <-> {b}")
        if len(intersections) > 50:
            print(f"... и ещё {len(intersections) - 50}")
    else:
        print("\nПересечения: нет")

    if unsupported:
        print("\nБез опоры снизу:")
        print(", ".join(unsupported))
    else:
        print("\nБез опоры снизу: нет")

    plt.tight_layout()
    if save_to:
        plt.savefig(save_to, dpi=180, bbox_inches="tight")
        print(f"\nКартинка сохранена в: {save_to}")
    else:
        plt.show()


def main():
    parser = argparse.ArgumentParser(description="Визуализация укладки коробок на паллете")
    parser.add_argument("file", help="CSV-файл в формате packed-out")
    parser.add_argument("--save", help="Путь для сохранения PNG")
    parser.add_argument("--show-ids", action="store_true", help="Подписывать ID коробок")
    args = parser.parse_args()

    pallet, boxes = load_pallet_csv(args.file)
    draw_layout(pallet, boxes, save_to=args.save, show_ids=args.show_ids)


if __name__ == "__main__":
    main()
