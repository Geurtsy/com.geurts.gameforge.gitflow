# Geurts Game Forge GitFlow

Version: 0.1.3. Editor-only read-only queue inspection with noninteractive native credential-manager authentication and batched record reads. Submission, publishing, recovery and AI repair remain outside this maintenance release.

Use [Help and supported APIs](Documentation~/AIUsability.md). Start with **Tools > Geurts Game Forge > GitFlow**, choose an existing Git checkout and remote, and select **Refresh queue once**. God can embed the same tool. Opening/restoring a view performs no remote work. No consumer is installed or updated by source publication.

God 0.39.3 is the only mandatory Geurts brick. Import licensed Odin Inspector and Quantum Console separately. Newtonsoft JSON 3.2.2 supplies bounded metadata parsing. Unity 6000.6.3f1 is the minimum. See the authoritative Commandments catalogue for installation sources after publication; use exact Git candidate commits in disposable validation projects.

The adapter reads codex/queue schema-1 control records (including reported terminal supersession), protocol-1 leases (180 or 1800 seconds), local integration_processor evidence, and matching publication receipts. It does not execute repository scripts. HTTPS delegates only to the existing configured native manager, manager-core or wincred helper, with prompts disabled; credentials stay inside Git and that helper. Absolute local remotes also work. SSH, shell/custom credential helpers and interactive sign-in are unsupported. Unrelated global/system Git settings remain isolated during inspection.

The finite limits are 512 requests, one MiB per JSON object, 16 MiB per object batch, 128 KiB per batch query, 90 seconds per refresh and two seconds between completed refreshes. The inspector advertises refs once, reads exact local objects in batches without lazy fetch/replacement objects, then fetches only missing commits together into its owned temporary store. Caller objects, refs, index, configuration and FETCH_HEAD are not written. A count beyond the limit or unavailable/malformed evidence is never labelled complete.

All results are historical observations. PID presence is not proof of processor ownership. A missing PID can prove the recorded process is stopped, while another owner remains unknown. A stale report remains stale; no lease expires based on its wall-clock date. Released leases are displayed as released. Raw validation evidence is reported, not rerun. A receipt match does not prove current-main ancestry, check success or artifact availability.

See [validation scope](Documentation~/Validation.md) for source/environment evidence and unverified native surfaces. Consumer installation remains manual. No player build is part of this release.

<!-- GEURTS-RELEASE-CONTEXT:BEGIN -->
```json
{
  "dependencies": {
    "com.geurts.gameforge.god": "0.39.3",
    "com.unity.nuget.newtonsoft-json": "3.2.2"
  },
  "packageId": "com.geurts.gameforge.gitflow",
  "packageVersion": "0.1.3"
}
```
<!-- GEURTS-RELEASE-CONTEXT:END -->
