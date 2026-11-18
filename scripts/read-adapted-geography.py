# Copyright 2026 shinKatana0
# SPDX-License-Identifier: Apache-2.0
"""Inspect or export the complete shipped ODbL adapted database, without WPF.

Python standard library only; this is a development/compliance reader, not a
portable-runtime dependency. No downloads or changes to source data occur.
"""
import argparse
import gzip
import json
import math
from pathlib import Path
import struct
import zipfile


def read_exact(stream, size):
    data = stream.read(size)
    if len(data) != size:
        raise ValueError("Truncated geographic database")
    return data


def lonlat(point):
    x, y = point
    return [x * 360 - 180, math.degrees(math.atan(math.sinh(math.pi * (1 - 2 * y))))]


def road_features(stream):
    if read_exact(stream, 8) != b"GSROAD01":
        raise ValueError("Unknown road database format")
    version, count = struct.unpack("<ii", read_exact(stream, 8))
    if version != 1 or not 0 < count <= 30000:
        raise ValueError("Unknown schema or excessive feature count")
    for _ in range(count):
        kind, x0, y0, x1, y1, paths = struct.unpack("<Bffffi", read_exact(stream, 21))
        if kind > 2 or not 0 < paths <= 384:
            raise ValueError("Invalid road feature")
        lines = []
        for _ in range(paths):
            length, = struct.unpack("<i", read_exact(stream, 4))
            if not 2 <= length <= 768:
                raise ValueError("Invalid road path")
            line = []
            for _ in range(length):
                x, y = struct.unpack("<II", read_exact(stream, 8))
                if x > 1000000000 or y > 1000000000:
                    raise ValueError("Invalid coordinate")
                line.append([x / 1e9, y / 1e9])
            lines.append(line)
        yield {"Kind": ["motorway", "trunk", "primary"][kind], "Bounds": [x0, y0, x1, y1], "Paths": lines}
    if stream.read(1):
        raise ValueError("Trailing data")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--file", type=Path)
    source.add_argument("--zip", type=Path)
    parser.add_argument("--entry", help="Exact ZIP member, for example GeoStack/geography/urban-roads.gslite")
    parser.add_argument("--output", type=Path, help="Optional GeoJSON export; never overwrite an existing file")
    args = parser.parse_args()
    archive = zipfile.ZipFile(args.zip) if args.zip else None
    name = args.entry if archive else args.file.name
    if archive and not args.entry:
        parser.error("--entry is required with --zip")
    try:
        compressed = archive.open(args.entry) if archive else args.file.open("rb")
        with compressed, gzip.GzipFile(fileobj=compressed) as stream:
            if name.endswith(".gslite"):
                features = road_features(stream)
                labels = []
                schema = "GSROAD01 / 1"
            elif name.endswith(".gsatlas"):
                raw = stream.read(32 * 1024 * 1024 + 1)
                if len(raw) > 32 * 1024 * 1024:
                    raise ValueError("Atlas expanded size exceeds reader budget")
                data = json.loads(raw)
                features = iter(data["Features"])
                labels = data.get("Labels", [])
                schema = data.get("Schema")
            else:
                raise ValueError("Reader supports .gslite and .gsatlas adapted databases")
            output = args.output.open("x", encoding="utf-8") if args.output else None
            counts = {"Features": 0, "Vertices": 0, "Labels": len(labels), "Schema": schema}
            try:
                if output:
                    output.write('{"type":"FeatureCollection","features":[')
                for feature in features:
                    if output:
                        if counts["Features"]:
                            output.write(",")
                        # Preserve every original path as a separate line/ring.
                        # Source ring grouping is documented, never invent holes.
                        json.dump({"type": "Feature", "properties": {key: value for key, value in feature.items() if key != "Paths"},
                            "geometry": {"type": "MultiLineString", "coordinates": [[lonlat(point) for point in path] for path in feature["Paths"]]}}, output, ensure_ascii=False)
                    counts["Features"] += 1
                    counts["Vertices"] += sum(len(path) for path in feature["Paths"])
                for label in labels:
                    if output:
                        if counts["Features"]:
                            output.write(",")
                        json.dump({"type": "Feature", "properties": label, "geometry": {"type": "Point", "coordinates": lonlat([label["X"], label["Y"]])}}, output, ensure_ascii=False)
                        counts["Features"] += 1
                if output:
                    output.write("]}\n")
            finally:
                if output:
                    output.close()
            print(json.dumps(counts))
    finally:
        if archive:
            archive.close()


if __name__ == "__main__":
    main()
