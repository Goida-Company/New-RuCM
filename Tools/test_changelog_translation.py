#!/usr/bin/env python3

import unittest

from Tools.changelog_translation import (
    TranslationError,
    needs_translation,
    translate_changelog,
)


class ChangelogTranslationTest(unittest.TestCase):
    def test_only_translates_messages_without_cyrillic(self):
        data = {
            "Entries": [
                {
                    "changes": [
                        {"type": "Add", "message": "Added a marine"},
                        {"type": "Fix", "message": "Исправлен баг"},
                        {"type": "Tweak", "message": "Added a marine"},
                    ]
                }
            ]
        }

        def fake_translator(messages):
            self.assertEqual(messages, ["Added a marine"])
            return ["Добавлен морпех"]

        self.assertEqual(translate_changelog(data, fake_translator), 2)
        self.assertEqual(
            [change["message"] for change in data["Entries"][0]["changes"]],
            ["Добавлен морпех", "Исправлен баг", "Добавлен морпех"],
        )

    def test_rejects_incomplete_translation(self):
        data = {"Entries": [{"changes": [{"message": "Fixed a bug"}]}]}

        with self.assertRaises(TranslationError):
            translate_changelog(data, lambda messages: [])

    def test_translation_predicate(self):
        self.assertTrue(needs_translation("Fixed a bug"))
        self.assertFalse(needs_translation("Исправлен баг"))
        self.assertFalse(needs_translation("   "))


if __name__ == "__main__":
    unittest.main()
