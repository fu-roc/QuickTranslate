"""Extract and compress the most common single-word entries from ECDICT's ecdict.csv."""

import argparse
import csv
import gzip
import re
from pathlib import Path


WORD_PATTERN = re.compile(r"[a-z]+(?:['-][a-z]+)*")
ENTRY_LIMIT = 50_000
UNRANKED = 1_000_000
OUTPUT = Path(__file__).resolve().parents[1] / "src/QuickTranslate/Assets/ecdict.tsv.gz"


def build_dictionary(source: Path) -> None:
    entries: dict[str, tuple[int, list[str]]] = {}
    with source.open(encoding="utf-8-sig", newline="") as csv_file:
        for row in csv.DictReader(csv_file):
            word = row["word"].strip().lower().replace("’", "'")
            if len(word) > 80 or not WORD_PATTERN.fullmatch(word):
                continue

            translation = row["translation"].replace(r"\n", "\n")
            lines = dict.fromkeys(" ".join(line.split()) for line in translation.splitlines())
            definitions = [line for line in lines if line]
            if not definitions:
                continue

            ranks = (int(row[column] or 0) for column in ("bnc", "frq"))
            rank = min((value for value in ranks if value > 0), default=UNRANKED)
            if word not in entries or rank < entries[word][0]:
                entries[word] = (rank, definitions)

    common_words = sorted(entries, key=lambda word: (entries[word][0], word))[:ENTRY_LIMIT]
    with OUTPUT.open("wb") as output:
        with gzip.GzipFile(filename="", fileobj=output, mode="wb", mtime=0) as compressed:
            for word in sorted(common_words):
                fields = [word, *entries[word][1]]
                compressed.write(("\t".join(fields) + "\n").encode("utf-8"))
    print(f"Wrote {len(common_words):,} entries ({OUTPUT.stat().st_size:,} bytes) to {OUTPUT}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="Path to the upstream ecdict.csv file")
    build_dictionary(parser.parse_args().source)
