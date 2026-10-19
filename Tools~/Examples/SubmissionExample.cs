using System.Threading.Tasks;
using Geurts.GameForge.GitFlow.Editor;

namespace Geurts.GameForge.GitFlow.Examples
{
    /// <summary>Worker handoff through the same shared service as the Submit controls.</summary>
    public static class SubmissionExample
    {
        /// <summary>Submits one exact snapshot and returns its pending original ID without starting or waiting for a publisher.</summary>
        public static Task<GitFlowSubmissionResult> SubmitOnce(string repository, string source, string integratedBase, string completeHandoff)
        {
            if (GitFlowBrick.QueueService == null) throw new System.InvalidOperationException("Enable GitFlow in God.");
            return GitFlowBrick.QueueService.SubmitAsync(new GitFlowSubmission(repository, source, integratedBase, "patch", "Reviewed update", completeHandoff));
        }
    }
}
