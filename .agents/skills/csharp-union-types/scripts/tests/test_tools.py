from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from check_links import check
from sync import synchronize


class LinkTests(unittest.TestCase):
    def test_local_files_and_duplicate_anchors(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'a.md').write_text('# Title\n# Title\n[second](a.md#title-1)\n[file](b.txt)\n')
            (root / 'b.txt').write_text('content')
            self.assertEqual([], check(root))

    def test_missing_file_and_anchor_are_reported(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'a.md').write_text('# Present\n[missing](none.md)\n[anchor](a.md#absent)\n')
            errors = check(root)
            self.assertEqual(2, len(errors))
            self.assertTrue(any('missing destination' in error for error in errors))
            self.assertTrue(any('missing anchor' in error for error in errors))

    def test_code_fences_do_not_create_headings(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'a.md').write_text('```md\n# Fake\n[ignored](missing.md)\n```\n[bad](a.md#fake)\n')
            self.assertEqual(['a.md:5: a.md#fake missing anchor'], check(root))

    def test_unicode_setext_and_reference_links(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'a.md').write_text('Überblick\n===\n[ok][section]\n[section]: a.md#%C3%BCberblick\n')
            self.assertEqual([], check(root))


class SyncTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.source, self.target = self.root / 'source', self.root / 'target'
        self.source.mkdir()
        (self.source / 'SKILL.md').write_text('original')

    def tearDown(self):
        self.temporary.cleanup()

    def test_check_is_read_only_and_apply_is_repeatable(self):
        self.assertTrue(synchronize(self.source, [self.target]))
        self.assertFalse(self.target.exists())
        synchronize(self.source, [self.target], apply=True)
        self.assertEqual('original', (self.target / 'SKILL.md').read_text())
        self.assertEqual([], synchronize(self.source, [self.target]))

    def test_source_changes_and_removals_flow_only_to_target(self):
        (self.source / 'old.md').write_text('old')
        synchronize(self.source, [self.target], apply=True)
        (self.source / 'old.md').unlink()
        (self.source / 'SKILL.md').write_text('new')
        (self.target / 'unrelated.txt').write_text('keep')
        synchronize(self.source, [self.target], apply=True)
        self.assertFalse((self.target / 'old.md').exists())
        self.assertEqual('new', (self.target / 'SKILL.md').read_text())
        self.assertEqual('keep', (self.target / 'unrelated.txt').read_text())

    def test_relative_parent_segments_are_normalized(self):
        target = self.root / 'unused' / '..' / 'target'
        synchronize(self.source, [target], apply=True)
        self.assertEqual('original', (self.target / 'SKILL.md').read_text())

    def test_independent_target_changes_are_not_overwritten(self):
        synchronize(self.source, [self.target], apply=True)
        (self.target / 'SKILL.md').write_text('independent')
        with self.assertRaisesRegex(ValueError, 'independent edits'):
            synchronize(self.source, [self.target], apply=True)
        self.assertEqual('independent', (self.target / 'SKILL.md').read_text())
        self.assertEqual('original', (self.source / 'SKILL.md').read_text())

    def test_no_recursive_exports_or_symlinks(self):
        with self.assertRaises(ValueError):
            synchronize(self.source, [self.source / 'nested'], apply=True)
        (self.source / 'link').symlink_to(self.source / 'SKILL.md')
        with self.assertRaises(ValueError):
            synchronize(self.source, [self.target], apply=True)

    def test_malicious_receipt_cannot_delete_outside_target(self):
        synchronize(self.source, [self.target], apply=True)
        import json
        receipt = self.target / '.sync-manifest.json'
        value = json.loads(receipt.read_text())
        value['files']['../outside.txt'] = 'hash'
        receipt.write_text(json.dumps(value))
        outside = self.root / 'outside.txt'
        outside.write_text('protected')
        with self.assertRaisesRegex(ValueError, 'Unsafe managed path'):
            synchronize(self.source, [self.target], apply=True)
        self.assertEqual('protected', outside.read_text())

    def test_receipt_cannot_delete_git_metadata(self):
        import json
        synchronize(self.source, [self.target], apply=True)
        metadata = self.target / '.git'
        metadata.mkdir()
        (metadata / 'config').write_text('protected')
        receipt = self.target / '.sync-manifest.json'
        value = json.loads(receipt.read_text())
        value['files']['.git/config'] = 'hash'
        receipt.write_text(json.dumps(value))
        with self.assertRaisesRegex(ValueError, 'Unsafe managed path'):
            synchronize(self.source, [self.target], apply=True)
        self.assertEqual('protected', (metadata / 'config').read_text())

    def test_linked_receipt_cannot_overwrite_an_external_file(self):
        self.target.mkdir()
        outside = self.root / 'private.txt'
        outside.write_text('protected')
        (self.target / '.sync-manifest.json').symlink_to(outside)
        with self.assertRaisesRegex(ValueError, 'receipt must not be a symlink'):
            synchronize(self.source, [self.target], apply=True)
        self.assertEqual('protected', outside.read_text())


if __name__ == '__main__':
    unittest.main()
