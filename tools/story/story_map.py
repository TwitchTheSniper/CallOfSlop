#!/usr/bin/env python3
"""The story map: one read-only page of every storylet in Story/Storylets, as a flowchart with each beat spelt out.

    python3 tools/story/story_map.py [OUT.html]

It only reads the game's files (the game is the source of truth), so run it again after the storylets change. The page
is for reading: what should change is said in words, and the files are edited to match (docs/story/STORY.md "Storylets").
"""
import json, pathlib, re, sys

ROOT = pathlib.Path(__file__).resolve().parents[2]


def read(path):
    text = re.sub(r"^\s*//.*$", "", path.read_text(encoding="utf-8"), flags=re.M)
    data = json.loads(re.sub(r",(\s*[}\]])", r"\1", text))
    data["file"] = path.name
    return data


def main():
    files = [read(p) for p in sorted((ROOT / "Story" / "Storylets").glob("*.json"))]
    page = (pathlib.Path(__file__).parent / "story_map.html").read_text(encoding="utf-8")
    page = page.replace("/*DATA*/[]", json.dumps(files).replace("</", "<\\/"))
    out = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else pathlib.Path("story_map_out.html")
    out.write_text(page, encoding="utf-8")
    print(f"{out}: {sum(len(f.get('storylets', [])) for f in files)} storylets from {len(files)} files")


if __name__ == "__main__":
    main()
