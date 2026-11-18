"""Deterministic Unity owner for an unambiguous additive scene reference conflict."""
from pathlib import Path
import re
import subprocess
import uuid

from GitFlowSession import Snapshot

REFERENCE = re.compile(rb'  (?P<field>[A-Za-z_][A-Za-z_0-9]*): \{fileID: (?P<fileid>-?[1-9][0-9]*), guid: (?P<guid>[a-f0-9]{32}), type: [23]\}')
COMPONENT = re.compile(rb'^--- !u!114 &(-?[1-9][0-9]*)$', re.M)
BLOCK = re.compile(rb'^<<<<<<< [^\n]+\n(?P<ours>.*?)(?:^\|\|\|\|\|\|\| [^\n]+\n.*?)?^=======\n(?P<theirs>.*?)^>>>>>>> [^\n]+\n', re.M | re.S)
CONFLICT = re.compile(rb'^(?:<<<<<<< |=======\s*$|>>>>>>> |\|\|\|\|\|\|\| )', re.M)
HELPER_SOURCE = r"""
#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Geurts.GameForge.GitFlow.NativeRecovery
{
    public static class SceneReference
    {
        [Serializable]
        private class Request { public string scene = "", component = "", field = "", guid = "", fileid = "", token = ""; }
        public static void Save()
        {
            try
            {
                if (!Application.isBatchMode) throw new InvalidOperationException("Expected a batch Editor.");
                var args = Environment.GetCommandLineArgs();
                var index = Array.IndexOf(args, "-gitFlowSceneRecoveryRequest");
                if (index < 0 || index + 1 >= args.Length) throw new InvalidOperationException("Missing exact recovery input.");
                var input = JsonUtility.FromJson<Request>(File.ReadAllText(args[index + 1]));
                long fileId = long.Parse(input.fileid, CultureInfo.InvariantCulture);
                ulong component = unchecked((ulong)long.Parse(input.component, CultureInfo.InvariantCulture));
                var assetPath = AssetDatabase.GUIDToAssetPath(input.guid);
                if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Artwork must be an existing project asset.");
                var asset = AssetDatabase.LoadAllAssetsAtPath(assetPath).Single(item =>
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(item, out var guid, out long id) &&
                    guid == input.guid && id == fileId);
                var scene = EditorSceneManager.OpenScene(input.scene, OpenSceneMode.Single);
                var target = Find(scene, component);
                var obj = new SerializedObject(target);
                var property = obj.FindProperty(input.field);
                if (property == null || property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue)
                    throw new InvalidOperationException("The reviewed field isn't a vacant serialized object reference.");
                Undo.RecordObject(target, "Recover queued scene reference");
                property.objectReferenceValue = asset;
                if (!obj.ApplyModifiedProperties() || property.objectReferenceValue != asset)
                    throw new InvalidOperationException("Unity refused the exact typed reference.");
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Unity could not save the scene.");
                scene = EditorSceneManager.OpenScene(input.scene, OpenSceneMode.Single);
                if (new SerializedObject(Find(scene, component)).FindProperty(input.field).objectReferenceValue != asset)
                    throw new InvalidOperationException("The reference did not survive reopening the scene.");
                Debug.Log("GITFLOW_NATIVE_SCENE_RECOVERY_PASS:" + input.token);
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static MonoBehaviour Find(Scene scene, ulong id) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
            .Single(item => item && GlobalObjectId.GetGlobalObjectIdSlow(item).targetObjectId == id);
    }
}
#endif
"""


def normalized(data):
    return data.replace(b'\r\n', b'\n')


def unique_component(data, component):
    owner = re.compile(rb'^--- !u!114 &' + component + rb'$', re.M)
    start = owner.search(data)
    if not start or owner.search(data, start.end()):
        return b''
    next_component = re.compile(rb'^--- !u!',re.M).search(data,start.end())
    return data[start.start():next_component.start() if next_component else len(data)]


def plan(base, ours, theirs, conflict, additions):
    """Read only: require exactly one incoming external reference and no other semantic scene delta."""
    base, ours, theirs, conflict = map(normalized, (base, ours, theirs, conflict))
    blocks = list(BLOCK.finditer(conflict))
    if len(blocks) != 1 or len(CONFLICT.findall(conflict)) not in (3, 4):
        return None
    match = blocks[0]
    if match['ours'] or not match['theirs'].endswith(b'\n') or match['theirs'].count(b'\n') != 1:
        return None
    line = match['theirs'].rstrip(b'\n')
    ref = REFERENCE.fullmatch(line)
    if not ref:
        return None
    prefix, suffix = conflict[:match.start()], conflict[match.end():]
    if prefix + suffix != ours or any(not row.startswith(b'+') for row in additions):
        return None
    previous = list(COMPONENT.finditer(prefix))
    if not previous or b'\n--- !u!' in prefix[previous[-1].end():]:
        return None
    component = previous[-1][1]
    ancestor, current, incoming = [unique_component(tree, component) for tree in (base, ours, theirs)]
    if not all((ancestor, current, incoming)) or line not in incoming.splitlines():
        return None
    field = b'  ' + ref['field'] + b':'
    if any(row.startswith(field) for row in (ancestor + current).splitlines()):
        return None
    if additions.count(b'+' + line) != 1:
        return None
    for item in additions:
        if item != b'+' + line and (item[1:] not in current.splitlines() or item[1:] not in incoming.splitlines()):
            return None
    return dict(component=component.decode(), field=ref['field'].decode(),
                guid=ref['guid'].decode(), fileid=ref['fileid'].decode()), prefix + line + b'\n' + suffix


def try_merge(q, workspace, value, policy, evidence_root, lease):
    names = q.run(['git','diff','--name-only','--diff-filter=U','-z'],workspace).decode().split('\0')[:-1]
    if not any(Path(name).suffix.lower() == '.unity' for name in names):
        return None
    def refuse(reason, code='native_scene_recovery_unproven'):
        error = q.QueueError('Unity scene merge requires native operator recovery: ' + reason)
        error.diagnostic = dict(schema=1,state='failed',stage='merge',cause_code=code,cause=reason,
                                observed_utc=q.utc(), next_action='Preserve the original merge. Use a licensed native Unity save and resume this exact request.')
        raise error
    if len(names) != 1 or Path(names[0]).suffix != '.unity':
        refuse('mixed or multiple conflicts; original inputs retained.')
    relative = names[0]
    if relative in policy.get('protected_paths', []) or relative in policy.get('native_version_outputs', []):
        refuse('the scene is protected by policy.')
    parts = relative.split('/')
    if parts.count('Assets') != 1 or len(parts) < 2:
        refuse('the scene does not belong to one Unity project.')
    asset = parts.index('Assets')
    project = q.plain(workspace.joinpath(*parts[:asset]))
    path = q.plain(workspace / relative)
    if not (project/'ProjectSettings/ProjectVersion.txt').is_file():
        refuse('cannot establish the exact Unity project.')
    commands = [policy.get('native_version_command', []), *[entry['command'] for entry in policy['checks']]]
    editors = {command[command.index('--unity')+1] for command in commands if '--unity' in command}
    if len(editors) != 1:
        refuse('configure one explicit Unity Editor in the required native commands.')
    editor = q.plain(editors.pop())
    version = re.search(r'^m_EditorVersion: (\S+)\s*$', (project/'ProjectSettings/ProjectVersion.txt').read_text(), re.M)
    if not version or editor.parent.parent.name != version[1] or not editor.is_file():
        refuse('the configured Editor does not match the Unity project revision.')
    stage_rows = q.run(['git','ls-files','-u','-z','--',relative],workspace).split(b'\0')[:-1]
    specs = [row.split(b'\t',1)[0].split() for row in stage_rows]
    if len(specs) != 3 or any(row[0] != b'100644' or row[2] != str(i).encode() for i,row in enumerate(specs,1)):
        refuse('expected a regular file on all three merge stages.')
    hashes = [row[1].decode() for row in specs]
    original = path.read_bytes()
    blobs = [q.run(['git','cat-file','blob',blob],workspace) for blob in hashes]
    delta = q.run(['git','diff','--no-ext-diff','--no-textconv','--unified=0',hashes[0],hashes[2]],workspace)
    changes = [row for row in delta.splitlines() if row.startswith((b'+',b'-')) and not row.startswith((b'+++',b'---'))]
    planned = plan(*blobs,original,changes)
    if planned is None:
        refuse('the conflict is not one unambiguous additive typed reference; the scene was preserved.')
    request, expected = planned
    evidence_root.mkdir(parents=True,exist_ok=True)
    folder = q.plain(evidence_root / ('scene_recovery_' + uuid.uuid4().hex))
    folder.mkdir()
    request.update(scene='/'.join(parts[asset:]),token=uuid.uuid4().hex)
    q.durable(folder/'native-request.json',request)
    (folder/'conflict.unity').write_bytes(original)
    session_paths = sorted({name for check in policy['checks'] for name in check.get('session_outputs',[])})
    helper = q.plain(project/'Assets/GitFlowQueueNativeSceneRecovery.cs')
    meta = helper.with_suffix('.cs.meta')
    build_settings = q.plain(project/'ProjectSettings/EditorBuildSettings.asset')
    build_settings_before = build_settings.read_bytes() if build_settings.is_file() else None
    if helper.exists() or meta.exists():
        refuse('temporary native helper path is occupied.')
    if q.git(workspace,'rev-parse','MERGE_HEAD') != value['source']:
        refuse('merge inputs changed before native authoring.')
    lease.guard()
    created = successful = False
    session = None
    try:
        q.git(workspace,'checkout','--ours','--',relative)
        if q.run(['git','ls-files','-u','-z','--',relative],workspace).split(b'\0')[:-1] != stage_rows:
            refuse('Git changed the retained stages during the native preflight.')
        # Checkout of the baseline refreshes index bookkeeping while keeping the
        # conflict stages. Bind the native session snapshot after this own Git write.
        session = Snapshot(q,workspace,folder,session_paths)
        with helper.open('xb') as stream:
            created = True
            stream.write(HELPER_SOURCE.encode())
        log = folder/'Unity.log'
        args = [str(editor),'-batchmode','-nographics','-nosound','-disable-audio','-projectPath',str(project),
                '-logFile',str(log),'-executeMethod','Geurts.GameForge.GitFlow.NativeRecovery.SceneReference.Save',
                '-gitFlowSceneRecoveryRequest',str(folder/'native-request.json')]
        with (folder/'process.log').open('xb') as out:
            result = q.process_run(args,cwd=project,stdout=out,stderr=out,timeout=900,cancelled=lease.stop,**q.console_options())
        text = log.read_text(encoding='utf-8-sig',errors='replace') if log.is_file() else ''
        if result.returncode or 'GITFLOW_NATIVE_SCENE_RECOVERY_PASS:' + request['token'] not in text:
            if re.search(r'com\.unity\.editor\.headless|Licensing::.*(?:timed.out|refused)|no valid Unity Editor license',text,re.I):
                refuse('The configured publisher cannot obtain a Unity Editor licence.', 'native_scene_licence_unavailable')
            refuse('the Editor did not prove save and reopen; see ' + str(log))
        if normalized(path.read_bytes()) != expected:
            refuse('Unity saved additional scene changes; no scene was staged.')
        successful = True
    except (OSError,subprocess.TimeoutExpired) as error:
        refuse(type(error).__name__ + '; see ' + str(folder))
    finally:
        if created:
            helper.unlink(missing_ok=True)
            meta.unlink(missing_ok=True)
        try:
            if session:
                session.restore()
            if build_settings_before is not None:
                if not build_settings.is_file():
                    refuse('Unity removed EditorBuildSettings.asset; the original is retained without restoration.')
                current = build_settings.read_bytes()
                if current != build_settings_before:
                    (folder/'EditorBuildSettings.original').write_bytes(build_settings_before)
                    if normalized(current) != normalized(build_settings_before):
                        refuse('Unity changed Editor build settings beyond line endings; review the retained output.')
                    build_settings.write_bytes(build_settings_before)
        finally:
            if (not successful and path.is_file() and session is not None
                    and q.git(workspace,'rev-parse','HEAD') == session.head and session.index.read_bytes() == session.index_before):
                (folder/'native-failed.unity').write_bytes(path.read_bytes())
                path.write_bytes(original)
    lease.guard()
    if set(q.git(workspace,'diff','--name-only').splitlines()) != {relative} or q.git(workspace,'ls-files','--others','--exclude-standard'):
        refuse('native run left unexpected worktree changes; review the retained bytes.')
    q.git(workspace,'add','--',relative)
    if q.git(workspace,'ls-files','--unmerged'):
        refuse('a merge stage remains unresolved.')
    log_hash = q.file_digest(log)
    q.durable(folder/'native-result.json',dict(protocol=1,status='saved_and_reopened',token=request['token'],
                                              unity=version[1],log_sha256=log_hash))
    proof = dict(protocol=1,request=value['id'],source=value['source'],path=relative,stages=hashes,
                 scene_blob=q.git(workspace,'rev-parse',':'+relative),scene_sha256=q.file_digest(path),
                 request_sha256=q.file_digest(folder/'native-request.json'),native_log_sha256=log_hash,
                 result_sha256=q.file_digest(folder/'native-result.json'),unity=version[1],model_invoked=False)
    q.durable(folder/'native-proof.json',proof)
    q.git(workspace,'commit','-m','Reconcile queued Unity scene through native serialization')
    q.clean(workspace)
    return proof, folder


def verify_scene(q, workspace, proof, candidate):
    if proof.get('protocol') != 1 or q.git(workspace,'rev-parse',candidate+':'+proof['path']) != proof['scene_blob']:
        raise q.QueueError('The scene no longer matches the native recovery proof.')


def proof_artifacts(q, proof, read):
    result = {}
    for name, key in [('native-request.json', 'request_sha256'), ('native-result.json', 'result_sha256')]:
        data = read(name)
        if q.digest(data) != proof[key]:
            raise q.QueueError('A native scene recovery artifact changed.')
        result['scene-recovery-'+name] = data
    return result
