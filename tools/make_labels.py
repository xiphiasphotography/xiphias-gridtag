#!/usr/bin/env python3
"""Build labels for `gridtag eval` from existing XMP sidecars.

Your already-tagged archive contains keywords like "#69" in dc:subject
(see docs/reference/*.xmp). This script turns them into ground-truth labels:

    path;numbers        (numbers comma-separated; first = primary car)

Output files
  labels.csv          photos with exactly ONE start-number keyword (unambiguous primary car)
  labels.review.csv   photos to fix by hand: several numbers (primary order unknown) or a
                      number that is not in the entry list.  Columns: path;numbers;reason
                      Put the primary car FIRST, then move the row into labels.csv.
Photos without any "#nr" keyword are NOT written (untagged is not the same as "no car").
Use --include-untagged only for folders where you have verified there is no car.

Example
  python tools/make_labels.py "D:\\Archief\\2025-09-13 - GTWC Zandvoort" ^
      --entrylist samples/entrylist.csv --max-per-number 12 --sample 400 --out work/labels.csv

Only reads files; never modifies photos or sidecars. Python 3.8+, no dependencies.
"""
import argparse
import csv
import random
import re
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

RAW_EXT = {".arw", ".cr2", ".cr3", ".nef", ".nrw", ".raf", ".orf", ".rw2", ".dng", ".pef", ".srw",
           ".iiq", ".3fr", ".erf", ".mef", ".mrw", ".x3f", ".srf", ".sr2", ".kdc", ".dcr"}
RDF = "{http://www.w3.org/1999/02/22-rdf-syntax-ns#}"
DC = "{http://purl.org/dc/elements/1.1/}"
NUMBER_KEYWORD = re.compile(r"^#\s*([0-9A-Za-z]{1,4})$")


def normalize(n: str) -> str:
    """Same rules as NumberNormalizer in XiPHiAS.GridTag.Core."""
    s = n.strip().lstrip("#").strip().upper()
    if len(s) > 1 and s.isdigit():
        s = s.lstrip("0") or "0"
    return s


def load_entrylist(path: Path):
    with open(path, encoding="utf-8-sig", newline="") as fh:
        return {normalize(r["number"]) for r in csv.DictReader(fh, delimiter=";")}


def subject_numbers(xmp: Path):
    root = ET.parse(xmp).getroot()
    found = []
    for subject in root.iter(DC + "subject"):
        for li in subject.iter(RDF + "li"):
            m = NUMBER_KEYWORD.match((li.text or "").strip())
            if m:
                n = normalize(m.group(1))
                if n not in found:
                    found.append(n)
    return found


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("root", type=Path, help="folder to scan recursively (RAW files + .xmp sidecars)")
    ap.add_argument("--out", type=Path, default=Path("work/labels.csv"))
    ap.add_argument("--entrylist", type=Path, help="entry list; numbers not in it go to the review file")
    ap.add_argument("--max-per-number", type=int, default=0, help="cap photos per car number (0 = no cap)")
    ap.add_argument("--sample", type=int, default=0, help="random sample of N photos after balancing (0 = all)")
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--include-untagged", action="store_true",
                    help="also write photos without number keyword, with an EMPTY numbers cell (= no car). "
                         "Only use on folders you verified.")
    args = ap.parse_args()

    if not args.root.is_dir():
        print(f"Not a folder: {args.root}", file=sys.stderr)
        return 3
    valid = load_entrylist(args.entrylist) if args.entrylist else None

    raws, xmps = {}, []
    for p in args.root.rglob("*"):
        ext = p.suffix.lower()
        if ext in RAW_EXT:
            raws[(str(p.parent).lower(), p.stem.lower())] = p
        elif ext == ".xmp":
            xmps.append(p)

    single, review = [], []
    untagged = []
    no_raw = bad_xmp = skipped_semicolon = 0
    for x in sorted(xmps):
        raw = raws.get((str(x.parent).lower(), x.stem.lower()))
        if raw is None:
            no_raw += 1
            continue
        if ";" in str(raw):
            skipped_semicolon += 1
            continue
        try:
            nums = subject_numbers(x)
        except ET.ParseError:
            bad_xmp += 1
            continue
        if not nums:
            untagged.append(raw)
        elif len(nums) > 1:
            review.append((raw, ",".join(nums), "multiple_numbers_set_primary_first"))
        elif valid is not None and nums[0] not in valid:
            review.append((raw, nums[0], "unknown_number"))
        else:
            single.append((raw, nums[0]))

    rng = random.Random(args.seed)
    if args.max_per_number > 0:
        by_number = defaultdict(list)
        for item in single:
            by_number[item[1]].append(item)
        single = []
        for items in by_number.values():
            rng.shuffle(items)
            single.extend(items[: args.max_per_number])
    if args.sample > 0 and len(single) > args.sample:
        single = rng.sample(single, args.sample)
    rows = [(str(p), n) for p, n in single]
    if args.include_untagged:
        rows += [(str(p), "") for p in untagged]
    rows.sort()

    args.out.parent.mkdir(parents=True, exist_ok=True)
    with open(args.out, "w", encoding="utf-8", newline="") as fh:
        w = csv.writer(fh, delimiter=";", lineterminator="\n")
        w.writerow(["path", "numbers"])
        w.writerows(rows)
    review_path = args.out.with_name(args.out.stem + ".review.csv")
    with open(review_path, "w", encoding="utf-8", newline="") as fh:
        w = csv.writer(fh, delimiter=";", lineterminator="\n")
        w.writerow(["path", "numbers", "reason"])
        w.writerows(sorted((str(p), n, r) for p, n, r in review))

    counts = defaultdict(int)
    for _, n in rows:
        counts[n] += 1
    print(f"labels written    : {len(rows)}  -> {args.out}")
    print(f"needs manual fix  : {len(review)}  -> {review_path}")
    print(f"untagged (skipped): {len(untagged)}{' (written as no-car)' if args.include_untagged else ''}")
    print(f"sidecars without RAW: {no_raw}, unreadable XMP: {bad_xmp}, paths with ';' skipped: {skipped_semicolon}")
    print(f"distinct cars in labels: {len([k for k in counts if k])}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
