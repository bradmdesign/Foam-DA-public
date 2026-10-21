import unittest
from unittest.mock import patch
import github_release as gh


class ReleaseResumeChecks(unittest.TestCase):
    def test_existing_public_release(self):
        release = {'id':1, 'tag_name':'v1', 'draft':False}
        with patch.object(gh, 'request', return_value=release) as api:
            self.assertEqual(gh.find_release('not-a-credential', 'v1'), release)
            self.assertEqual(api.call_count, 1)

    def test_tag_endpoint_404_resumes_owner_draft(self):
        release = {'id':2, 'tag_name':'v1', 'draft':True}
        with patch.object(gh, 'request', side_effect=[None, [release]]):
            self.assertEqual(gh.find_release('not-a-credential', 'v1')['id'], 2)

    def test_draft_can_be_on_another_page(self):
        older = [{'id':n, 'tag_name':'old-'+str(n)} for n in range(100)]
        with patch.object(gh, 'request', side_effect=[None, older, [{'id':101, 'tag_name':'v1', 'draft':True}]]):
            self.assertEqual(gh.find_release('not-a-credential', 'v1')['id'], 101)

    def test_ambiguous_drafts_refuse_upload(self):
        with patch.object(gh, 'request', side_effect=[None, [{'id':1, 'tag_name':'v1'}, {'id':2, 'tag_name':'v1'}]]):
            with self.assertRaisesRegex(RuntimeError, 'Several release drafts'):
                gh.find_release('not-a-credential', 'v1')

    def test_new_tag_has_no_release(self):
        with patch.object(gh, 'request', side_effect=[None, []]):
            self.assertIsNone(gh.find_release('not-a-credential', 'v1'))


if __name__ == '__main__':
    unittest.main()
