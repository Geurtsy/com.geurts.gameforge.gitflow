// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
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
        public string Version => "0.2.0";
        public string[] Dependencies => new[] { "com.geurts.gameforge.god" };
        public static GitFlowInspection Service { get; private set; }
        public static GitFlowQueue QueueService { get; private set; }
        public static GitFlowConfiguration ConfigurationService { get; private set; }
        public static BrickSettingsStore Settings { get; private set; }
        private GitFlowInspection _owned;

        /// <summary>Creates the idle shared inspector and publishes it within this brick's lifecycle.</summary>
        public void Start(BrickContext context)
        {
            if (Service != null) throw new InvalidOperationException("GitFlow already has an active owner.");
            _owned = new GitFlowInspection(); Service = _owned;
            QueueService = new GitFlowQueue(); ConfigurationService = new GitFlowConfiguration(); Settings = context.Settings;
            context.Provide(_owned); context.Own(_owned);
            context.Provide(QueueService); context.Provide(ConfigurationService);
        }

        /// <summary>Cancels only this owner's inspection and releases its shared reference.</summary>
        public void Stop() { _owned?.Dispose(); if (ReferenceEquals(Service, _owned)) { Service = null; QueueService = null; ConfigurationService = null; Settings = null; } _owned = null; }
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
            foreach (string id in new[] { "submit_request", "apply_policy", "configure_publisher", "start_publisher", "stop_publisher" })
                yield return new ForgeOperation(id, "Explicit GitFlow " + id.Replace('_', ' ') + "; supply the documented typed payload and confirmed=true.", InspectMutation, ExecuteMutation, confirmationRequired: true, mutatesProject: true);
        }

        /// <summary>Checks typed mutation admission only; discovery and inspection never read/write project or remote data.</summary>
        private static ForgeOperationResult InspectMutation(ForgeOperationRequest request)
        {
            bool valid = request.OperationId == "submit_request" ? request.Payload is GitFlowSubmission : request.OperationId == "apply_policy" ? request.Payload is GitFlowPolicyReview :
                request.OperationId == "configure_publisher" ? request.Payload is GitFlowPublisherOptions : request.OperationId == "start_publisher" ? request.Payload is GitFlowHostStart : request.Payload is GitFlowHostStop;
            return ForgeOperationResult.For(request, GitFlowBrick.ConfigurationService == null ? FORGE_OPERATION_STATUS.UNAVAILABLE : valid ? FORGE_OPERATION_STATUS.SUCCEEDED : FORGE_OPERATION_STATUS.BLOCKED,
                GitFlowBrick.ConfigurationService == null ? "Enable GitFlow in God." : valid ? "Typed input admitted; explicit confirmed=true execution is required. No work has run." : "Supply the documented typed payload.", FORGE_VERIFICATION.NOT_APPLICABLE);
        }

        /// <summary>Routes explicit confirmed operations to the same services as the controls without starting a publisher during submission.</summary>
        private static async Task<ForgeOperationResult> ExecuteMutation(ForgeOperationRequest request)
        {
            if (request.Argument("confirmed") != "true") return ForgeOperationResult.For(request, FORGE_OPERATION_STATUS.BLOCKED, "Review the exact scope and supply confirmed=true for this operation.", FORGE_VERIFICATION.NOT_APPLICABLE);
            if (GitFlowBrick.ConfigurationService == null) return ForgeOperationResult.For(request, FORGE_OPERATION_STATUS.UNAVAILABLE, "Enable GitFlow in God.");
            string detail;
            switch (request.OperationId)
            {
                case "submit_request":
                    GitFlowSubmissionResult submitted = await GitFlowBrick.QueueService.SubmitAsync(request.RequirePayload<GitFlowSubmission>());
                    detail = submitted.Id + " — " + (submitted.Reused ? "Original request retained; recorded state " + submitted.State : "Queued — awaiting integration") + ". Verified control " + submitted.ControlCommit + ". Submit once and stop waiting; no publisher was started."; break;
                case "apply_policy":
                    var review = request.RequirePayload<GitFlowPolicyReview>(); GitFlowBrick.ConfigurationService.ApplyPolicy(review);
                    detail = "Saved reviewed policy at " + review.Path + ". Index and branch unchanged. Commit/review it through the project's owning Git workflow before submitting."; break;
                case "configure_publisher":
                    var options = request.RequirePayload<GitFlowPublisherOptions>(); GitFlowBrick.ConfigurationService.ConfigurePublisher(options);
                    detail = "Configured only the stopped dedicated root " + options.root + ". No host was started."; break;
                case "start_publisher":
                    var start = request.RequirePayload<GitFlowHostStart>();
                    int pid = GitFlowBrick.ConfigurationService.StartPublisher(start.Root, start.Executable, start.TrustedManifest, true, start.ResumeId, start.ResumeReason);
                    detail = "Started independent runner PID " + pid + ". This is launch acknowledgement; refresh explicit recorded health/lease evidence to establish its state."; break;
                case "stop_publisher":
                    GitFlowBrick.ConfigurationService.RequestStop(request.RequirePayload<GitFlowHostStop>().Root);
                    detail = "Graceful stop requested. It takes effect between requests; this is not stopped-owner proof."; break;
                default: throw new ArgumentException("Unknown GitFlow operation.");
            }
            return ForgeOperationResult.For(request, FORGE_OPERATION_STATUS.SUCCEEDED, detail, request.OperationId == "submit_request" ? FORGE_VERIFICATION.VERIFIED : FORGE_VERIFICATION.UNKNOWN);
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
