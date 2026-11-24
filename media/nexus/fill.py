"""Fills the Nexus page text for one version and prints it; the Nexus workflow pushes what this prints, so a local run
shows exactly what the page will say.

Usage: python3 fill.py <file> <version> <game version> <repository> <git ref>
  {{version}}, {{game_version}}  replaced by the values given
  {{media:<path>}}               on a line of its own: the image media/<path>, linked from GitHub at <git ref> and
                                 collapsed in a spoiler; consecutive lines share one
An image that doesn't exist here is left out, so a line naming one can go in before its image does. Any other {{...}}
is left in place for the caller to refuse.
"""

import re
import sys
from pathlib import Path

MEDIA = Path(__file__).resolve().parent.parent
PATH = r"([A-Za-z0-9._/-]+)"


def fill(text: str, version: str, game: str, repository: str, ref: str) -> str:
    raw = f"https://raw.githubusercontent.com/{repository}/{ref}/media"

    def image(path: str) -> str | None:
        return f"[img]{raw}/{path}[/img]" if (MEDIA / path).is_file() else None

    out: list[str] = []
    spoiler: list[str] = []

    def flush() -> None:
        if spoiler:
            out.append("[spoiler]" + "\n".join(spoiler) + "[/spoiler]")
            spoiler.clear()

    for line in text.splitlines():
        if m := re.fullmatch(r"\s*\{\{media:" + PATH + r"\}\}\s*", line):
            if found := image(m.group(1)):
                spoiler.append(found)
            continue
        flush()
        out.append(line.replace("{{version}}", version).replace("{{game_version}}", game))
    flush()
    return "\n".join(out) + "\n"


if __name__ == "__main__":
    if len(sys.argv) != 6:
        sys.exit(__doc__)
    file, version, game, repository, ref = sys.argv[1:]
    sys.stdout.write(fill(Path(file).read_text(encoding="utf-8"), version, game, repository, ref))
