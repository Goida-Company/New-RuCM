#!/usr/bin/env python3
"""
Assembles the changelog .yml parts into a changelog file.
Each part includes: author (required), changes (required), time, url, category
Prunes the oldest past 500 entries.
usage: update_changelog.py <changelog-file> <parts-dir> --category "Main"
"""

import os
from typing import List, Any
import yaml
import argparse
import datetime

from changelog_translation import translate_changelog

MAX_ENTRIES = 500

HEADER_RE = r"(?::cl:|🆑) *\r?\n(.+)$"
ENTRY_RE = r"^ *[*-]? *(\S[^\n\r]+)\r?$"

CATEGORY_MAIN = "Main"


# From https://stackoverflow.com/a/37958106/4678631
class NoDatesSafeLoader(yaml.SafeLoader):
    @classmethod
    def remove_implicit_resolver(cls, tag_to_remove):
        if not "yaml_implicit_resolvers" in cls.__dict__:
            cls.yaml_implicit_resolvers = cls.yaml_implicit_resolvers.copy()

        for first_letter, mappings in cls.yaml_implicit_resolvers.items():
            cls.yaml_implicit_resolvers[first_letter] = [
                (tag, regexp) for tag, regexp in mappings if tag != tag_to_remove
            ]


# Hrm yes let's make the fucking default of our serialization library to PARSE ISO-8601
# but then output garbage when re-serializing.
NoDatesSafeLoader.remove_implicit_resolver("tag:yaml.org,2002:timestamp")


def sort_entries(data):
    if "Entries" not in data:
        return data
    data["Entries"].sort(key=lambda e: e.get("time", ""))
    return data


def deduplicate_entries(entries: List[Any]) -> List[Any]:
    result = []
    seen_urls = set()

    for entry in entries:
        url = entry.get("url")
        if url and url in seen_urls:
            print(f"Removing duplicate changelog entry for {url}")
            continue
        if url:
            seen_urls.add(url)
        result.append(entry)

    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("changelog_file")
    parser.add_argument("parts_dir")
    parser.add_argument("--category", default=CATEGORY_MAIN)
    parser.add_argument(
        "--translate",
        action="store_true",
        help="translate all untranslated changelog messages to Russian",
    )
    args = parser.parse_args()
    category = args.category

    with open(args.changelog_file, "r", encoding="utf-8-sig") as f:
        raw = yaml.load(f, Loader=NoDatesSafeLoader)

    if raw is None:
        raw = {}
    current_data: dict[str, Any] = raw

    # Get the existing entries, or an empty list if the key is missing.
    entries_list: List[Any] = current_data.get("Entries", [])
    max_id = max(map(lambda e: e["id"], entries_list), default=0)
    entries_list = deduplicate_entries(entries_list)
    existing_urls = {entry.get("url") for entry in entries_list if entry.get("url")}

    processed_parts = []
    for partname in os.listdir(args.parts_dir):
        if not partname.endswith(".yml"):
            continue

        partpath = os.path.join(args.parts_dir, partname)
        print(partpath)

        with open(partpath, "r", encoding="utf-8-sig") as f:
            partyaml = yaml.load(f, Loader=NoDatesSafeLoader)

        part_category = partyaml.get("category", CATEGORY_MAIN)
        if part_category != category:
            print(f"Skipping: wrong category ({part_category} vs {category})")
            continue

        author = partyaml["author"]
        time = partyaml.get(
            "time", datetime.datetime.now(datetime.timezone.utc).isoformat()
        )
        changes = partyaml["changes"]
        url = partyaml.get("url")

        if url and url in existing_urls:
            print(f"Skipping: changelog entry for {url} already exists")
            processed_parts.append(partpath)
            continue

        if not isinstance(changes, list):
            changes = [changes]

        if len(changes):
            # Don't add empty changelog entries...
            max_id += 1
            new_id = max_id

            entries_list.append(
                {
                    "author": author,
                    "time": time,
                    "changes": changes,
                    "id": new_id,
                    "url": url,
                }
            )
            if url:
                existing_urls.add(url)
        processed_parts.append(partpath)
    print(f"Have {len(entries_list)} changelog entries")

    # Backfilled entries may be older than the current tail. Sort before pruning
    # so the actual oldest entries are removed, regardless of discovery order.
    entries_list.sort(key=lambda entry: entry.get("time", ""))
    overflow = len(entries_list) - MAX_ENTRIES
    if overflow > 0:
        print(f"Removing {overflow} old entries.")
        entries_list = entries_list[overflow:]

    new_data = {"Entries": entries_list}
    for key, value in current_data.items():
        if key != "Entries":
            new_data[key] = value

    # IDs are persistent: Discord and clients use them to identify unseen entries.
    new_data = sort_entries(new_data)

    if args.translate:
        translated_count = translate_changelog(new_data)
        print(f"Translated {translated_count} changelog messages to Russian")

    with open(args.changelog_file, "w", encoding="utf-8-sig") as f:
        yaml.safe_dump(new_data, f)

    # Keep parts intact when assembly, translation, or writing fails.
    for partpath in processed_parts:
        os.remove(partpath)


if __name__ == "__main__":
    main()
