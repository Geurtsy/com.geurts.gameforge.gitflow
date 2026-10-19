// IMPORTANT: Follow the manifest-selected Geurts Technical and Folder Structure techniques.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Geurts.GameForge.God;
using Geurts.GameForge.God.Editor;
using UnityEngine.Scripting;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Editor-only lifecycle owner of the shared queue inspector; registration starts no work.</summary>
    [Brick, Preserve]
    public sealed class GitFlowBrick : IBrick
    {
        public const string PackageId = "com.geurts.gameforge.gitflow";
        public string Id => PackageId;
        public string DisplayName => "Geurts Game Forge GitFlow";
        public string Version => "0.1.0";
        public string[] Dependencies => new[] { "com.geurts.gameforge.god" };
        public static GitFlowInspection Service { get; private set; }
        private GitFlowInspection _owned;

        /// <summary>Creates the idle shared inspector and publishes it within this brick's lifecycle.</summary>
        public void Start(BrickContext context)
        {
            if (Service != null) throw new InvalidOperationException("GitFlow already has an active owner.");
            _owned = new GitFlowInspection(); Service = _owned;
            context.Provide(_owned); context.Own(_owned);
        }

        /// <summary>Cancels only this owner's inspection and releases its shared reference.</summary>
        public void Stop() { _owned?.Dispose(); if (ReferenceEquals(Service, _owned)) Service = null; _owned = null; }
    }

    /// <summary>God operation adapter over the same service used by the no-code window.</summary>
    public sealed class GitFlowAutomationProvider : IForgeAutomationProvider
    {
        public int ContractVersion => 1;
        public string OwnerPackageId => GitFlowBrick.PackageId;

        /// <summary>Declares only the implemented, explicit read-only queue refresh.</summary>
        public IEnumerable<ForgeOperation> Operations()
        {
            yield return new ForgeOperation("inspect_queue", "Refresh one bounded read-only queue observation.", InspectRequest, RefreshRequest, mutatesProject: false);
        }

        /// <summary>Offers no implicit project scan; queue observation requires an explicit typed target.</summary>
        public IEnumerable<ForgeAuthoringObjectResult> Inspect(IEnumerable<UnityEngine.Object> targets) => Array.Empty<ForgeAuthoringObjectResult>();

        /// <summary>Reports admission without contacting a remote or inspecting project files.</summary>
        private static ForgeOperationResult InspectRequest(ForgeOperationRequest request) => ForgeOperationResult.For(request,
            GitFlowBrick.Service == null ? FORGE_OPERATION_STATUS.UNAVAILABLE : request.Payload is GitFlowTarget ? FORGE_OPERATION_STATUS.SUCCEEDED : FORGE_OPERATION_STATUS.BLOCKED,
            GitFlowBrick.Service == null ? "Enable GitFlow in God before inspection." : request.Payload is GitFlowTarget ? "Ready for an explicit bounded refresh. No observation has run." : "Supply a GitFlowTarget payload.", FORGE_VERIFICATION.NOT_APPLICABLE);

        /// <summary>Awaits the shared inspector and exposes actual completeness independently from recorded queue outcomes.</summary>
        private static async Task<ForgeOperationResult> RefreshRequest(ForgeOperationRequest request)
        {
            GitFlowInspection service = GitFlowBrick.Service;
            if (service == null) return ForgeOperationResult.For(request, FORGE_OPERATION_STATUS.UNAVAILABLE, "Enable GitFlow in God before inspection.");
            GitFlowSnapshot result = await service.RefreshAsync(request.RequirePayload<GitFlowTarget>());
            return new ForgeOperationResult(GitFlowBrick.PackageId, request.OperationId,
                result.Complete ? FORGE_OPERATION_STATUS.SUCCEEDED : result.QueueState == "Unavailable" ? FORGE_OPERATION_STATUS.UNAVAILABLE : FORGE_OPERATION_STATUS.PARTIAL,
                "Snapshot " + result.RefreshedUtc.ToString("O") + "; queue " + result.QueueState + "; " + result.Requests.Count + " requests. " + string.Join("\n", result.Issues),
                new[] { ForgeTarget.File(result.Repository) }, result.Complete ? FORGE_VERIFICATION.VERIFIED : FORGE_VERIFICATION.UNKNOWN);
        }
    }
}
