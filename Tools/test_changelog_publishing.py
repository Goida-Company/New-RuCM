#!/usr/bin/env python3

import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import yaml

from Tools.actions_changelogs_since_last_run import (
    changelog_entries_since,
    diff_changelog,
)
from Tools import update_changelog


def entry(entry_id, url, time):
    return {
        "id": entry_id,
        "url": url,
        "time": time,
        "author": "tester",
        "changes": [{"type": "Fix", "message": "Исправление"}],
    }


class ChangelogPublishingTest(unittest.TestCase):
    def test_new_entry_is_found_when_legacy_ids_overlap(self):
        old = {
            "Entries": [
                entry(1, "https://example.test/pull/1", "2026-07-18T00:00:00Z"),
                entry(2, "https://example.test/pull/2", "2026-07-19T00:00:00Z"),
            ]
        }
        current = {
            "Entries": [
                entry(1, "https://example.test/pull/2", "2026-07-19T00:00:00Z"),
                entry(2, "https://example.test/pull/3", "2026-07-20T00:00:00Z"),
            ]
        }

        self.assertEqual(
            [item["url"] for item in diff_changelog(old, current)],
            ["https://example.test/pull/3"],
        )

    def test_manual_entries_are_compared_by_stable_timestamp(self):
        old = {"Entries": [entry(10, None, "2026-07-19T00:00:00Z")]}
        current = {"Entries": [entry(1, None, "2026-07-19T00:00:00Z")]}

        self.assertEqual(list(diff_changelog(old, current)), [])

    def test_backfill_selects_existing_entries_since_date(self):
        changelog = {
            "Entries": [
                entry(499, "https://example.test/pull/1", "2026-07-18T23:59:59Z"),
                entry(500, "https://example.test/pull/2", "2026-07-19T00:00:00Z"),
                entry(501, "https://example.test/pull/3", "2026-07-20T00:00:00Z"),
            ]
        }

        self.assertEqual(
            [
                item["url"]
                for item in changelog_entries_since(changelog, "2026-07-19T00:00:00Z")
            ],
            ["https://example.test/pull/2", "https://example.test/pull/3"],
        )

    def test_sorting_does_not_renumber_persistent_ids(self):
        data = {
            "Entries": [
                entry(501, "https://example.test/pull/3", "2026-07-20T00:00:00Z"),
                entry(499, "https://example.test/pull/1", "2026-07-18T00:00:00Z"),
            ]
        }

        update_changelog.sort_entries(data)

        self.assertEqual([item["id"] for item in data["Entries"]], [499, 501])

    def test_existing_duplicate_pr_entries_are_removed(self):
        entries = [
            entry(499, "https://example.test/pull/1", "2026-07-18T00:00:00Z"),
            entry(500, "https://example.test/pull/1", "2026-07-18T00:00:00Z"),
            entry(501, None, "2026-07-19T00:00:00Z"),
            entry(502, None, "2026-07-19T00:00:00Z"),
        ]

        result = update_changelog.deduplicate_entries(entries)

        self.assertEqual([item["id"] for item in result], [499, 501, 502])

    def test_assembler_skips_backfill_duplicates_and_keeps_ids_stable(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            changelog_path = root / "CMU.yml"
            parts_path = root / "Parts"
            parts_path.mkdir()

            existing = {
                "Entries": [
                    entry(499, "https://example.test/pull/1", "2026-07-18T00:00:00Z"),
                    entry(500, "https://example.test/pull/2", "2026-07-19T00:00:00Z"),
                ]
            }
            changelog_path.write_text(
                yaml.safe_dump(existing, allow_unicode=True), encoding="utf-8"
            )

            duplicate = {
                "author": "tester",
                "time": "2026-07-19T00:00:00Z",
                "url": "https://example.test/pull/2",
                "category": "CMU",
                "changes": [{"type": "Fix", "message": "Дубликат"}],
            }
            new_part = {
                "author": "tester",
                "time": "2026-07-20T00:00:00Z",
                "url": "https://example.test/pull/3",
                "category": "CMU",
                "changes": [{"type": "Fix", "message": "Новая запись"}],
            }
            (parts_path / "duplicate.yml").write_text(
                yaml.safe_dump(duplicate, allow_unicode=True), encoding="utf-8"
            )
            (parts_path / "new.yml").write_text(
                yaml.safe_dump(new_part, allow_unicode=True), encoding="utf-8"
            )

            argv = [
                "update_changelog.py",
                str(changelog_path),
                str(parts_path),
                "--category",
                "CMU",
            ]
            with patch.object(sys, "argv", argv), patch.object(
                update_changelog, "MAX_ENTRIES", 2
            ):
                update_changelog.main()

            result = yaml.safe_load(changelog_path.read_text(encoding="utf-8-sig"))
            self.assertEqual(
                [item["url"] for item in result["Entries"]],
                ["https://example.test/pull/2", "https://example.test/pull/3"],
            )
            self.assertEqual([item["id"] for item in result["Entries"]], [500, 501])
            self.assertEqual(list(parts_path.iterdir()), [])


if __name__ == "__main__":
    unittest.main()
