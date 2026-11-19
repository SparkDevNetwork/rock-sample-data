// <copyright>
// Copyright by the Spark Development Network
//
// Licensed under the Rock Community License (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.rockrms.com/license
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//

namespace RockSampleData.Loader.Model
{
    /// <summary>
    /// configuration/benevolence.json: the benevolence types, their security, the case worker, the Cast's requests, and
    /// the rules for generated requests.
    /// </summary>
    internal sealed class BenevolenceConfiguration
    {
        public List<BenevolenceTypeDefinition> Types { get; set; } = new List<BenevolenceTypeDefinition>();

        /// <summary>
        /// The security every type gets, as Rock's security editor saves it.
        /// </summary>
        public List<CategorySecurityRule> Security { get; set; } = new List<CategorySecurityRule>();

        public BenevolencePerson CaseWorker { get; set; }

        /// <summary>
        /// The workflow every church type starts when a request is entered, or null for none.
        /// </summary>
        public BenevolenceWorkflowDefinition Workflow { get; set; }

        public List<CastBenevolenceRequest> Cast { get; set; } = new List<CastBenevolenceRequest>();

        public GeneratedBenevolenceRule Generated { get; set; }

        /// <summary>
        /// What the team gives when it approves a request, by type key.
        /// </summary>
        public Dictionary<string, List<BenevolenceResultRule>> Results { get; set; } = new Dictionary<string, List<BenevolenceResultRule>>();

        public BenevolenceText Text { get; set; }

        public BenevolenceAgreement Agreement { get; set; }

        /// <summary>
        /// The security walkthrough's benevolence questions, which verify asks Rock.
        /// </summary>
        public List<AccessExpectation> Access { get; set; } = new List<AccessExpectation>();
    }

    internal sealed class BenevolenceTypeDefinition
    {
        public string Key { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// The name of a new type. Rock's default type has none here and keeps its own.
        /// </summary>
        public string Name { get; set; }

        public string Description { get; set; }
    }

    internal sealed class BenevolenceWorkflowDefinition
    {
        public Guid Guid { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// A workflow type category's key in configuration/categories.json.
        /// </summary>
        public string Category { get; set; }

        public string WorkTerm { get; set; }

        public string IconCssClass { get; set; }

        /// <summary>
        /// The staff member who built the workflow.
        /// </summary>
        public Guid Author { get; set; }

        /// <summary>
        /// How many years before install day the workflow and its triggers were built.
        /// </summary>
        public int BuiltYearsAgo { get; set; }
    }

    internal sealed class BenevolencePerson
    {
        public Guid Person { get; set; }

        public string Name { get; set; }
    }

    /// <summary>
    /// A Cast person's request, with the dates and amounts their notes tell.
    /// </summary>
    internal sealed class CastBenevolenceRequest
    {
        public string Key { get; set; }

        public Guid Guid { get; set; }

        public Guid Requester { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// A type key.
        /// </summary>
        public string Type { get; set; }

        public RelativeDate Requested { get; set; }

        /// <summary>
        /// The staff member who typed the request in.
        /// </summary>
        public Guid EnteredBy { get; set; }

        public RelativeDate Decided { get; set; }

        /// <summary>
        /// The staff member who saved the decision.
        /// </summary>
        public Guid DecidedBy { get; set; }

        /// <summary>
        /// Pending, Approved, or Denied: Rock's request statuses.
        /// </summary>
        public string Status { get; set; }

        public string Text { get; set; }

        public string ResultSummary { get; set; }

        public string NextSteps { get; set; }

        public List<CastBenevolenceResult> Results { get; set; } = new List<CastBenevolenceResult>();

        /// <summary>
        /// Whether the request has a signed assistance agreement as a document.
        /// </summary>
        public bool Agreement { get; set; }

        public string Story { get; set; }
    }

    internal sealed class CastBenevolenceResult
    {
        /// <summary>
        /// Rock's result type, by name.
        /// </summary>
        public string Type { get; set; }

        public decimal? Amount { get; set; }

        public string Summary { get; set; }

        public RelativeDate Paid { get; set; }
    }

    internal sealed class GeneratedBenevolenceRule
    {
        public double PerThousandAdultsPerYear { get; set; }

        /// <summary>
        /// How likely each connection status is to ask, relative to the others. A status not listed never asks.
        /// </summary>
        public Dictionary<string, double> RequesterWeights { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Requests made in the last this many days still wait as Pending.
        /// </summary>
        public int PendingDays { get; set; }

        /// <summary>
        /// The share of decided requests with each status.
        /// </summary>
        public Dictionary<string, double> Status { get; set; } = new Dictionary<string, double>();

        public int[] DecidedDaysAfter { get; set; }

        public int[] PaidDaysAfterDecision { get; set; }

        /// <summary>
        /// The share of requests of each type, by type key.
        /// </summary>
        public Dictionary<string, double> Types { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// The types whose approved requests have a signed assistance agreement.
        /// </summary>
        public List<string> AgreementTypes { get; set; } = new List<string>();
    }

    internal sealed class BenevolenceResultRule
    {
        /// <summary>
        /// Rock's result type, by name.
        /// </summary>
        public string Type { get; set; }

        public double Weight { get; set; }

        /// <summary>
        /// The amount's range in dollars, or null for a referral with no amount.
        /// </summary>
        public decimal[] Amount { get; set; }

        /// <summary>
        /// Rounds the amount to this many dollars, or leaves cents when null.
        /// </summary>
        public int? Round { get; set; }

        public string Summary { get; set; }
    }

    internal sealed class BenevolenceText
    {
        /// <summary>
        /// What people ask for, by type key.
        /// </summary>
        public Dictionary<string, List<string>> Requests { get; set; } = new Dictionary<string, List<string>>();

        public List<string> NextSteps { get; set; } = new List<string>();

        public List<string> Denied { get; set; } = new List<string>();
    }

    internal sealed class BenevolenceAgreement
    {
        public string Title { get; set; }

        /// <summary>
        /// The agreement's terms. "{church}" is the organization's name.
        /// </summary>
        public List<string> Paragraphs { get; set; } = new List<string>();
    }
}
