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
    /// The contents of campaign.json: the building campaign, its fund, and the Rock settings that point at it.
    /// </summary>
    internal sealed class CampaignConfiguration
    {
        public string Name { get; set; }

        /// <summary>
        /// The name of the campaign's fund in accounts.json.
        /// </summary>
        public string Fund { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// How many weeks before install day's Sunday the campaign started, on Commitment Sunday.
        /// </summary>
        public int CommitmentSundayWeeksBeforeInstall { get; set; }

        public int LengthYears { get; set; }

        public CampaignPledgeEntry PledgeEntry { get; set; }

        /// <summary>
        /// The contribution statement template whose pledge settings show the campaign.
        /// </summary>
        public Guid StatementTemplateGuid { get; set; }
    }

    /// <summary>
    /// The pages whose Pledge Entry block takes new pledges for the campaign.
    /// </summary>
    internal sealed class CampaignPledgeEntry
    {
        public List<Guid> PageGuids { get; set; } = new List<Guid>();

        /// <summary>
        /// The Pledge Entry block types, WebForms and Obsidian. Rock swaps one for the other after startup.
        /// </summary>
        public List<Guid> BlockTypeGuids { get; set; } = new List<Guid>();

        public bool ShowPledgeFrequency { get; set; }
    }
}
