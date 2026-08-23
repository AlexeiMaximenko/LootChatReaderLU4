import concurrent.futures
import html
import json
import math
import os
import pathlib
import re
import time
import urllib.error
import urllib.parse
import urllib.request


ROOT = pathlib.Path(__file__).resolve().parents[1]
CATALOG = ROOT / "Resources" / "item-icons.json"
BASE_URL = "https://mw2.wiki/lu4-b-w-c/"
USER_AGENT = "LU4LootChatReader-build/1.0"
PAGE_SIZE = 100

ITEM_ID_PATTERN = re.compile(r'href=["\']/lu4-b-w-c/item/(\d+)[^"\']*["\']', re.IGNORECASE)
TAG_PATTERN = re.compile(r"<[^>]+>")
TOTAL_PATTERN = re.compile(r"(?:Item|Предмет)\s*(\d+)", re.IGNORECASE)
DETAIL_TYPE_PATTERN = re.compile(
    r'<span\s+class=["\']item-name__type["\']>\s*([^<]+?)\s*</span>',
    re.IGNORECASE,
)
LEGACY_TYPE_FALLBACKS = {
    5249: "Other",
    716: "Armor",
    718: "Armor",
    720: "Armor",
    722: "Armor",
    723: "Armor",
    724: "Armor",
}


def fetch(url: str) -> str:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    for attempt in range(4):
        try:
            with urllib.request.urlopen(request, timeout=45) as response:
                return response.read().decode("utf-8", errors="replace")
        except (OSError, urllib.error.URLError):
            if attempt == 3:
                raise
            time.sleep(0.5 * (attempt + 1))
    raise RuntimeError(f"Unable to fetch {url}")


def result_url(item_type: int, item_subtype: int | None, page: int) -> str:
    parameters = {
        "Search[query]": "",
        "Search[search_type]": "0",
        "Search[item_type]": str(item_type),
        "per_page": str(PAGE_SIZE),
        "page": str(page),
    }
    if item_subtype is not None:
        parameters["Search[item_subtype]"] = str(item_subtype)
    return urllib.parse.urljoin(BASE_URL, "search/result") + "?" + urllib.parse.urlencode(parameters)


def parse_ids(page_html: str) -> set[int]:
    return {int(value) for value in ITEM_ID_PATTERN.findall(page_html)}


def read_filter_ids(item_type: int, item_subtype: int | None = None) -> set[int]:
    first_html = fetch(result_url(item_type, item_subtype, 1))
    plain = TAG_PATTERN.sub(" ", html.unescape(first_html))
    total_match = TOTAL_PATTERN.search(re.sub(r"\s+", " ", plain))
    if total_match is None:
        raise RuntimeError(f"Item total was not found for type={item_type}, subtype={item_subtype}")
    total = int(total_match.group(1))
    page_count = max(1, math.ceil(total / PAGE_SIZE))
    ids = parse_ids(first_html)
    if page_count > 1:
        urls = [result_url(item_type, item_subtype, page) for page in range(2, page_count + 1)]
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
            for page_html in executor.map(fetch, urls):
                ids.update(parse_ids(page_html))
    print(
        f"type={item_type} subtype={item_subtype} total={total} parsed={len(ids)}",
        flush=True,
    )
    return ids


def read_type_metadata() -> tuple[dict[int, str], dict[int, dict[int, str]]]:
    search_html = fetch(urllib.parse.urljoin(BASE_URL, "search?Search%5Bsearch_type%5D=0"))
    types = {
        int(value): html.unescape(label.strip())
        for label, value in re.findall(
            r'<label[^>]*>([^<]+)<input[^>]*name=["\']Search\[item_type\]["\'][^>]*value=["\'](-?\d+)["\']',
            search_html,
            re.IGNORECASE,
        )
        if int(value) >= 0
    }
    subtype_match = re.search(r"window\._searchItemData\s*=\s*(\{.*?\});", search_html, re.DOTALL)
    if subtype_match is None:
        raise RuntimeError("Subtype metadata was not found.")
    raw_subtypes = json.loads(subtype_match.group(1))
    subtypes = {
        int(item_type): {int(value): label for value, label in values.items()}
        for item_type, values in raw_subtypes.items()
    }
    return types, subtypes


def read_detail_type(entry: dict) -> tuple[int, str]:
    page_html = fetch(urllib.parse.urljoin(BASE_URL, entry["ItemPath"]))
    match = DETAIL_TYPE_PATTERN.search(page_html)
    parsed = html.unescape(match.group(1).strip()) if match else ""
    return entry["Id"], parsed or LEGACY_TYPE_FALLBACKS.get(entry["Id"], "Unknown")


def main() -> int:
    entries = json.loads(CATALOG.read_text(encoding="utf-8-sig"))
    catalog_ids = {entry["Id"] for entry in entries}
    type_names, subtype_names = read_type_metadata()
    types_by_id: dict[int, str] = {}

    for item_type, type_name in sorted(type_names.items()):
        for item_id in read_filter_ids(item_type):
            if item_id in catalog_ids:
                types_by_id[item_id] = type_name

    for item_type, subtypes in sorted(subtype_names.items()):
        type_name = type_names[item_type]
        for item_subtype, subtype_name in sorted(subtypes.items()):
            for item_id in read_filter_ids(item_type, item_subtype):
                if item_id in catalog_ids:
                    types_by_id[item_id] = f"{type_name} / {subtype_name}"

    missing_entries = [entry for entry in entries if entry["Id"] not in types_by_id]
    if missing_entries:
        print(f"Reading {len(missing_entries)} unmatched item pages", flush=True)
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
            for item_id, item_type in executor.map(read_detail_type, missing_entries):
                types_by_id[item_id] = item_type

    for entry in entries:
        entry["Type"] = types_by_id[entry["Id"]]

    temporary = CATALOG.with_suffix(CATALOG.suffix + f".{os.getpid()}.tmp")
    temporary.write_text(json.dumps(entries, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    os.replace(temporary, CATALOG)

    counts: dict[str, int] = {}
    for entry in entries:
        counts[entry["Type"]] = counts.get(entry["Type"], 0) + 1
    print(f"Updated {len(entries)} catalog entries with {len(counts)} exact types.")
    for item_type, count in sorted(counts.items()):
        print(f"  {item_type}: {count}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
