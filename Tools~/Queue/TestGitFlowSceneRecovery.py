"""Guard the native planner against silently discarding scene changes."""
import subprocess
import tempfile
import unittest
from pathlib import Path
from GitFlowSceneRecovery import plan

START = b'%YAML 1.1\n--- !u!114 &123\nMonoBehaviour:\n  m_Enabled: 1\n'
END = b'  _input: {fileID: 44}\n'
COMMON = b'  _common: {fileID: 0}\n'
LINE = b'  _art: {fileID: 11400000, guid: 1234567890abcdef1234567890abcdef, type: 2}\n'


class SceneRecoveryTest(unittest.TestCase):
    def test_real_git_merge_keeps_both_parents_in_scope(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            def git(*args, check=True):
                return subprocess.run(['git','-C',str(root),*args],capture_output=True,check=check)
            git('init','-b','main')
            git('config','user.name','Fixture')
            git('config','user.email','fixture@example.test')
            git('config','core.autocrlf','false')
            scene = root/'Level.unity'
            scene.write_bytes(START+END)
            git('add','--','Level.unity')
            git('commit','-m','base')
            git('switch','-c','incoming')
            scene.write_bytes(START+COMMON+LINE+END)
            git('commit','-am','add artwork')
            git('switch','main')
            scene.write_bytes(START+COMMON+END)
            git('commit','-am','common field')
            self.assertEqual(git('merge','--no-ff','incoming',check=False).returncode,1)
            stages = [git('show',f':{i}:Level.unity').stdout for i in (1,2,3)]
            edits = [row for row in git('diff','--no-ext-diff','--unified=0','main...incoming').stdout.splitlines()
                     if row.startswith((b'+',b'-')) and not row.startswith((b'+++',b'---'))]
            match = plan(*stages,scene.read_bytes(),edits)
            self.assertIsNotNone(match)
            self.assertEqual(match[0]['component'],'123')
            self.assertEqual(match[1],START+COMMON+LINE+END)

    def test_does_not_swallow_unrelated_or_competing_edits(self):
        base, ours = START+END, START+COMMON+END
        theirs = START+COMMON+LINE+END
        conflict = START+COMMON+b'<<<<<<< HEAD\n=======\n'+LINE+b'>>>>>>> incoming\n'+END
        additions = [b'+'+COMMON.strip(b'\n'),b'+'+LINE.strip(b'\n')]
        self.assertIsNone(plan(base,ours,theirs,conflict,additions + [b'-  unrelated: 1']))
        self.assertIsNone(plan(base,ours,theirs,conflict.replace(b'=======',b'  _art: {fileID: 0}\n======='),additions))
        self.assertIsNone(plan(base,ours,theirs,conflict.replace(END,b'  unrelated: 99\n'+END),additions))


if __name__ == '__main__':
    unittest.main()
