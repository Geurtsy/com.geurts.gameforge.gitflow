using System.Threading.Tasks;
using Geurts.GameForge.GitFlow.Editor;

namespace Geurts.GameForge.GitFlow.Examples
{
    /// <summary>Explicit code path for the same read-only refresh used by the Editor controls.</summary>
    public static class InspectionExample
    {
        /// <summary>Reads once through the active lifecycle owner; the returned snapshot retains unavailable evidence.</summary>
        /// <param name="existingRepository">Existing local Git checkout to observe.</param>
        /// <returns>The shared immutable snapshot, or null if the module is disabled.</returns>
        public static async Task<GitFlowSnapshot> InspectOnce(string existingRepository)
        {
            GitFlowInspection service = GitFlowBrick.Service;
            return service == null ? null : await service.RefreshAsync(new GitFlowTarget(existingRepository));
        }
    }
}
