// IMPORTANT: Follow the manifest-selected Geurts Technical and Folder Structure techniques.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Immutable request evidence observed at one remote control commit; recorded delivery is not independently certified.</summary>
    public sealed class GitFlowRequest
    {
        public string Id { get; }
        public string State { get; }
        public string Title { get; }
        public string Source { get; }
        public string Size { get; }
        public string Version { get; }
        public string PublishedCommit { get; }
        public string Failure { get; }
        public string ControlCommit { get; }
        public string EvidenceJson { get; }
        public string ReceiptJson { get; }
        public string ReceiptDetail { get; }

        /// <summary>Freezes an adapter's request fields and original evidence without rewriting source identities.</summary>
        public GitFlowRequest(string id, string state, string title, string source, string size, string version,
            string publishedCommit, string failure, string controlCommit, string evidenceJson, string receiptJson, string receiptDetail)
        {
            Id = id; State = state; Title = title; Source = source; Size = size; Version = version;
            PublishedCommit = publishedCommit; Failure = failure; ControlCommit = controlCommit;
            EvidenceJson = evidenceJson; ReceiptJson = receiptJson; ReceiptDetail = receiptDetail;
        }
    }

    /// <summary>Bounded inspection result shared unchanged between Editor, API and God operations.</summary>
    public sealed class GitFlowSnapshot
    {
        public string Repository { get; }
        public string Remote { get; }
        public DateTime RefreshStartedUtc { get; }
        public DateTime RefreshedUtc { get; }
        public string QueueState { get; }
        public string ProcessorState { get; }
        public string ProcessorDetail { get; }
        public string LeaseState { get; }
        public string LeaseDetail { get; }
        public ReadOnlyCollection<GitFlowRequest> Requests { get; }
        public ReadOnlyCollection<string> Issues { get; }
        public bool Complete => QueueState == "Observed" && Issues.Count == 0;

        /// <summary>Copies observations so caller changes cannot alter the displayed evidence.</summary>
        public GitFlowSnapshot(string repository, string remote, DateTime started, string queueState,
            string processorState, string processorDetail, string leaseState, string leaseDetail,
            IEnumerable<GitFlowRequest> requests, IEnumerable<string> issues)
        {
            Repository = repository; Remote = remote; RefreshStartedUtc = started; RefreshedUtc = DateTime.UtcNow;
            QueueState = queueState; ProcessorState = processorState; ProcessorDetail = processorDetail;
            LeaseState = leaseState; LeaseDetail = leaseDetail;
            Requests = new List<GitFlowRequest>(requests).AsReadOnly(); Issues = new List<string>(issues).AsReadOnly();
        }
    }

    /// <summary>Explicit refresh scope. These transient values never change repository or processor configuration.</summary>
    public sealed class GitFlowTarget
    {
        public string Repository { get; }
        public string Remote { get; }
        /// <summary>Selects a local Git checkout and an existing remote name for one observation.</summary>
        public GitFlowTarget(string repository, string remote = "origin") { Repository = repository; Remote = remote; }
    }
}
