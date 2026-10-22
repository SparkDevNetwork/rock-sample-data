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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Loading
{
    /*
        10/22/26 - CLAUDE

        Pledges are planned before giving and written after it. The plan tells the giving step what each
        giving pattern gives to the campaign: a campaign line on its usual gifts, extra gifts, or both. The
        giving step sizes the line from the gifts it actually plans, so an on-pace pledge is on pace on any
        install day. Rock's pledge save hook writes history only against the pledge itself, which no Rock
        screen shows, so no history is written here.

        Reason: Pledges and the gifts that follow them always agree (docs/pledges.md).
    */

    /// <summary>
    /// What one giving pattern gives to the campaign. A null total means gifts with no pledge.
    /// </summary>
    internal sealed record CampaignGiving( string Fund, DateTime LineStart, DateTime CampaignEnd, decimal? Total, decimal LineFactor, DateTime? LineStop, IReadOnlyList<CampaignGift> Gifts );

    /// <summary>
    /// One extra campaign gift, given the way the giver usually gives, or as stock.
    /// </summary>
    internal sealed record CampaignGift( DateTime When, decimal Amount, bool Stock );

    /// <summary>
    /// One pledge to write.
    /// </summary>
    internal sealed record PlannedPledge( LoadedPerson Pledger, decimal Total, string Frequency, DateTime Created, DateTime Start, DateTime End );

    /// <summary>
    /// Plans the campaign's pledges and campaign gifts, and writes the pledges.
    /// </summary>
    internal sealed class PledgeLoader
    {
        /// <summary>
        /// Rock's transaction frequency defined values, which Rock uses for pledge frequencies too.
        /// </summary>
        private static readonly Dictionary<string, Guid> FrequencyGuids = new Dictionary<string, Guid>
        {
            ["one-time"] = new Guid( "82614683-7FB4-4F16-9087-6F85945A7B16" ),
            ["weekly"] = new Guid( "35711E44-131B-4534-B0B2-F0A749292362" ),
            ["bi-weekly"] = new Guid( "72990023-0D43-4554-8D32-28461CAB8920" ),
            ["monthly"] = new Guid( "1400753C-A0F9-4A45-8A1D-81C98450BD1F" ),
            ["quarterly"] = new Guid( "BF08EA03-C52A-4364-B142-12EBCA7CA14A" ),
            ["yearly"] = new Guid( "AC88C37A-901E-4CBB-947B-11348C208192" )
        };

        private readonly RockDatabase _db;
        private readonly CampaignConfiguration _campaign;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public PledgeLoader( RockDatabase db, CampaignConfiguration campaign, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _campaign = campaign;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// The pledges to write. Filled by <see cref="Plan"/>.
        /// </summary>
        public List<PlannedPledge> Pledges { get; } = new List<PlannedPledge>();

        /// <summary>
        /// What each giving pattern gives to the campaign, for the giving step. Filled by <see cref="Plan"/>.
        /// </summary>
        public Dictionary<CastGiving, CampaignGiving> Giving { get; } = new Dictionary<CastGiving, CampaignGiving>();

        /// <summary>
        /// Plans every household's campaign entries.
        /// </summary>
        public void Plan( List<LoadedHousehold> households )
        {
            var commitment = _campaign.CommitmentSunday( _dates.Anchor );
            var end = _campaign.End( commitment );

            foreach ( var household in households )
            {
                foreach ( var entry in household.Cast.Campaign )
                {
                    PlanEntry( household, entry, commitment, end );
                }
            }
        }

        /// <summary>
        /// Plans one Cast campaign entry: its pledge, its campaign line, and its extra gifts.
        /// </summary>
        private void PlanEntry( LoadedHousehold household, CastCampaign entry, DateTime commitment, DateTime end )
        {
            var random = new SeededRandom( $"{_seed}:campaign:{household.Cast.Guid}:{entry.Person}" );
            var giver = household.Members.First( m => m.Cast.Key == entry.Person );
            var patterns = household.Cast.Giving.Where( g => g.Person == entry.Person ).ToList();
            var pattern = patterns.FirstOrDefault( g => g.Persona == entry.Frequency )
                ?? patterns.FirstOrDefault( g => ( _dates.Resolve( g.Ended ) ?? DateTime.MaxValue ) >= commitment )
                ?? patterns.Last();

            var weeks = entry.PledgedWeeksAfterCommitment;
            var created = weeks.HasValue
                ? commitment.AddDays( 7 * weeks.Value ).AddHours( 11 )
                : commitment.AddDays( -7 * random.Between( _campaign.QuietPhaseWeeks.Min, _campaign.QuietPhaseWeeks.Max ) ).AddHours( random.Between( 9, 17 ) );
            var start = created.Date > commitment ? created.Date : commitment;

            var gifts = entry.Gifts.Select( g => new CampaignGift( commitment.AddDays( 7 * g.WeeksAfterCommitment + 1 ).AddHours( 12 ), g.Amount, g.Stock ) ).ToList();
            if ( entry.FollowThrough == "paid-in-full" && entry.Pledge.HasValue )
            {
                gifts.Add( new CampaignGift( start.AddDays( 8 ).AddHours( 12 ), entry.Pledge.Value, false ) );
            }

            var lineStop = entry.FollowThrough == PledgeProgress.Stopped
                ? _dates.Resolve( pattern.Ended ) ?? start.AddDays( 7 * random.Between( 12, 20 ) )
                : ( DateTime? ) null;
            var lineFactor = entry.Pledge.HasValue ? _campaign.LineFactors.GetValueOrDefault( entry.FollowThrough ?? string.Empty ) : 0;
            Giving[pattern] = new CampaignGiving( _campaign.Fund, start, end, entry.Pledge, lineFactor, lineStop, gifts );

            if ( entry.Pledge.HasValue )
            {
                Pledges.Add( new PlannedPledge( giver, entry.Pledge.Value, entry.Frequency, created, start, end ) );
            }
        }

        /// <summary>
        /// Writes the planned pledges to the campaign's fund.
        /// </summary>
        public void Write( Guid fundGuid )
        {
            var fundId = _db.RequireIdByGuid( "FinancialAccount", fundGuid );
            var frequencyIds = FrequencyGuids.ToDictionary( f => f.Key, f => _db.RequireIdByGuid( "DefinedValue", f.Value ) );
            var table = TableFactory.Create( "PersonAliasId:int", "AccountId:int", "TotalAmount:decimal", "PledgeFrequencyValueId:int", "StartDate:date", "EndDate:date", "StartDateKey:int", "EndDateKey:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            foreach ( var pledge in Pledges )
            {
                table.Rows.Add( pledge.Pledger.AliasId, fundId, pledge.Total, pledge.Frequency == null ? DBNull.Value : frequencyIds[pledge.Frequency],
                    pledge.Start, pledge.End, DateKey( pledge.Start ), DateKey( pledge.End ), Guid.NewGuid(), _foreignKey, pledge.Created, pledge.Created );
            }

            _db.BulkInsert( "FinancialPledge", table, keepIdentity: false );
            _db.Log.Write( $"  Pledges: {Pledges.Count:N0} (${Pledges.Sum( p => p.Total ):N0}), campaign gifts from {Giving.Count:N0} giving patterns" );
        }

        private static int DateKey( DateTime date ) => int.Parse( date.ToString( "yyyyMMdd" ) );
    }
}
