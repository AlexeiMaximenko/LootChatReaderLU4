import concurrent.futures
import json
import os
import pathlib
import time
import urllib.error
import urllib.parse
import urllib.request


ROOT = pathlib.Path(__file__).resolve().parents[1]
CATALOG = ROOT / "Resources" / "item-icons.json"
OUTPUT = ROOT / "Resources" / "item-icons"
BASE_URL = "https://mw2.wiki/"
USER_AGENT = "LU4LootChatReader-build/1.0"


def download(icon_path: str) -> tuple[str, str]:
    url = urllib.parse.urljoin(BASE_URL, icon_path)
    filename = pathlib.PurePosixPath(urllib.parse.urlparse(url).path).name
    destination = OUTPUT / filename
    if destination.exists() and destination.stat().st_size > 0:
        return "cached", filename

    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    for attempt in range(3):
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                content = response.read()
            if not content:
                raise OSError("empty response")
            temporary = destination.with_suffix(destination.suffix + f".{os.getpid()}.tmp")
            temporary.write_bytes(content)
            os.replace(temporary, destination)
            return "downloaded", filename
        except (OSError, urllib.error.URLError) as error:
            if attempt == 2:
                return "failed", f"{filename}: {error}"
            time.sleep(0.4 * (attempt + 1))

    return "failed", filename


def main() -> int:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    entries = json.loads(CATALOG.read_text(encoding="utf-8-sig"))
    paths = sorted({entry["IconPath"] for entry in entries if not entry["IconPath"].endswith("/none.png")})
    counts = {"cached": 0, "downloaded": 0, "failed": 0}
    failures: list[str] = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=16) as executor:
        for index, (status, detail) in enumerate(executor.map(download, paths), start=1):
            counts[status] += 1
            if status == "failed":
                failures.append(detail)
            if index % 100 == 0 or index == len(paths):
                print(
                    f"{index}/{len(paths)} downloaded={counts['downloaded']} "
                    f"cached={counts['cached']} failed={counts['failed']}",
                    flush=True,
                )

    if failures:
        print("Unavailable icons:")
        for failure in failures:
            print(f"  {failure}")

    generic_fallback = OUTPUT / "etc_jewel_box_i00.png"
    if not generic_fallback.exists():
        print("Generic reagent-cache fallback is missing.")
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
