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

        /// <summary>
        /// How many weeks before Commitment Sunday staff and lead givers pledge, as a range.
        /// </summary>
        public CampaignRange QuietPhaseWeeks { get; set; }

        /// <summary>
        /// How much of the pace each line follow-through gives: ahead, on-pace, behind, and stopped (until it stops).
        /// </summary>
        public Dictionary<string, decimal> LineFactors { get; set; } = new Dictionary<string, decimal>();

        /// <summary>
        /// How generated households and businesses take part.
        /// </summary>
        public CampaignGenerated Generated { get; set; }

        public CampaignPledgeEntry PledgeEntry { get; set; }

        /// <summary>
        /// The contribution statement template whose pledge settings show the campaign.
        /// </summary>
        public Guid StatementTemplateGuid { get; set; }

        /// <summary>
        /// Commitment Sunday, the campaign's first day, for a load on <paramref name="anchor"/>.
        /// </summary>
        public DateTime CommitmentSunday( DateTime anchor )
        {
            var installSunday = anchor.Date.AddDays( -( int ) anchor.DayOfWeek );
            return installSunday.AddDays( -7 * CommitmentSundayWeeksBeforeInstall );
        }

        /// <summary>
        /// The campaign's last day.
        /// </summary>
        public DateTime End( DateTime commitmentSunday ) => commitmentSunday.AddYears( LengthYears ).AddDays( -1 );
    }

    /// <summary>
    /// An inclusive whole-number range.
    /// </summary>
    internal sealed class CampaignRange
    {
        public int Min { get; set; }

        public int Max { get; set; }
    }

    /// <summary>
    /// How generated households and businesses pledge and give. Each mix is a set of weights.
    /// </summary>
    internal sealed class CampaignGenerated
    {
        /// <summary>
        /// How likely each kind of giving unit is to pledge: business, steady, rhythmic, and other.
        /// </summary>
        public Dictionary<string, double> PledgerWeights { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// A rhythmic giver who has given at least this long is a steady giver.
        /// </summary>
        public int SteadyYears { get; set; }

        /// <summary>
        /// The share of pledgers, by yearly giving, who make lead gifts.
        /// </summary>
        public double LeadShare { get; set; }

        /// <summary>
        /// The fewest pledges each campus has, counting the Cast's.
        /// </summary>
        public int MinimumPledgesPerCampus { get; set; }

        /// <summary>
        /// How many years of the unit's giving a pledge is, for lead and other pledgers.
        /// </summary>
        public Dictionary<string, CampaignSpan> PledgeYears { get; set; } = new Dictionary<string, CampaignSpan>();

        /// <summary>
        /// When pledges are made: quiet-phase, commitment-sunday, soon-after, and later.
        /// </summary>
        public Dictionary<string, double> Pledged { get; set; } = new Dictionary<string, double>();

        public Dictionary<string, double> RhythmicFrequency { get; set; } = new Dictionary<string, double>();

        public Dictionary<string, double> OtherFrequency { get; set; } = new Dictionary<string, double>();

        public Dictionary<string, double> LineFollowThrough { get; set; } = new Dictionary<string, double>();

        public Dictionary<string, double> OneTimeFollowThrough { get; set; } = new Dictionary<string, double>();

        public Dictionary<string, double> YearlyFollowThrough { get; set; } = new Dictionary<string, double>();

        public Dictionary<string, double> NoFrequencyFollowThrough { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// The share of givers who did not pledge who give to the campaign anyway.
        /// </summary>
        public double GiftsOnlyShare { get; set; }
    }

    /// <summary>
    /// An inclusive range of numbers.
    /// </summary>
    internal sealed class CampaignSpan
    {
        public double Min { get; set; }

        public double Max { get; set; }
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
