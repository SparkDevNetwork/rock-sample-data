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
        public Dictionary<CastGiving, CampaignGiving> Giving { get; } = new Dictionary<CastGiving, CampaignGiving>( ReferenceEqualityComparer.Instance );

        /// <summary>
        /// Plans the Cast's campaign entries, then picks and plans the generated pledgers so that the share of
        /// giving units with a pledge reaches <paramref name="pledgeRate"/>.
        /// </summary>
        public void Plan( List<LoadedHousehold> households, IReadOnlySet<string> castHouseholdKeys, double pledgeRate )
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

            if ( _campaign.Generated != null )
            {
                PlanGenerated( households.Where( h => !castHouseholdKeys.Contains( h.Cast.Key ) ).ToList(), households, pledgeRate, commitment, end );
            }
        }

        /// <summary>
        /// One generated giving unit that could pledge: its giver, the pattern that would pay, and its yearly giving.
        /// </summary>
        private sealed record Candidate( LoadedHousehold Household, LoadedPerson Giver, CastGiving Pattern, decimal Yearly, double Weight );

        private static readonly string[] Rhythms = { "weekly", "bi-weekly", "monthly", "quarterly" };

        /*
            10/22/26 - CLAUDE

            The pledge rate is graded the way calibration measures it: giving units with a pledge, out of the units
            that gave in the last year. So the generated pledgers are counted, not rolled: enough units are drawn,
            by weight, to bring the Cast's pledges up to the rate. A weighted draw without replacement keeps the
            count exact while steady givers and businesses still pledge far more often than occasional ones.

            Reason: giving.pctUnitsWithPledge is graded (decision 101).
        */

        /// <summary>
        /// Picks generated pledgers and givers of campaign gifts with no pledge, and plans each one.
        /// </summary>
        private void PlanGenerated( List<LoadedHousehold> generated, List<LoadedHousehold> all, double pledgeRate, DateTime commitment, DateTime end )
        {
            var settings = _campaign.Generated;
            var anchor = _dates.Anchor;
            var activeUnits = all.SelectMany( h => h.Cast.Giving.Where( g => GaveWithin( g, anchor.AddDays( -365 ), anchor ) )
                .Select( g => h.Members.First( m => m.Cast.Key == g.Person ).GivingId ) ).Distinct().Count();
            var pledgedUnits = Pledges.Select( p => p.Pledger.GivingId ).ToHashSet();
            var wanted = Math.Max( 0, ( int ) Math.Round( pledgeRate * activeUnits ) - pledgedUnits.Count );

            var candidates = new List<Candidate>();
            foreach ( var household in generated )
            {
                foreach ( var person in household.Cast.Giving.Select( g => g.Person ).Distinct() )
                {
                    // The pattern that pays is the giver's largest one still giving well after Commitment Sunday.
                    var giver = household.Members.First( m => m.Cast.Key == person );
                    var pattern = household.Cast.Giving.Where( g => g.Person == person && GaveWithin( g, commitment.AddDays( 28 ), anchor.AddDays( -14 ) ) )
                        .OrderByDescending( Yearly ).FirstOrDefault();
                    if ( pattern == null || pledgedUnits.Contains( giver.GivingId ) || Giving.ContainsKey( pattern ) )
                    {
                        continue;
                    }

                    var started = _dates.Resolve( pattern.Started ) ?? DateTime.MinValue;
                    var kind = giver.Cast.RecordType == "business" ? "business"
                        : !Rhythms.Contains( pattern.Persona ) ? "other"
                        : started <= anchor.AddYears( -settings.SteadyYears ) ? "steady" : "rhythmic";
                    candidates.Add( new Candidate( household, giver, pattern, Yearly( pattern ), settings.PledgerWeights.GetValueOrDefault( kind, 1 ) ) );
                }
            }

            // A weighted draw without replacement: each candidate's key is u^(1/weight), and the largest keys win.
            var draw = new SeededRandom( $"{_seed}:campaign:pledgers" );
            var keyed = candidates.Select( c => (Candidate: c, Key: Math.Pow( draw.Between( 0.0, 1.0 ), 1 / Math.Max( 0.001, c.Weight ) )) ).ToList();
            var ranked = keyed.OrderByDescending( k => k.Key ).Select( k => k.Candidate ).ToList();
            var pledgers = ranked.Take( wanted ).ToList();

            // Every campus has some pledges, so campus filters on the pledge screens always show data. A small
            // dataset can reach the rate with the Cast alone, so this can carry it above the rate.
            var castCampuses = Pledges.Select( p => all.First( h => h.Members.Contains( p.Pledger ) ).CampusKey ).ToList();
            foreach ( var campus in candidates.Select( c => c.Household.CampusKey ).Distinct() )
            {
                var missing = settings.MinimumPledgesPerCampus - castCampuses.Count( c => c == campus ) - pledgers.Count( p => p.Household.CampusKey == campus );
                pledgers.AddRange( ranked.Where( c => c.Household.CampusKey == campus && !pledgers.Contains( c ) ).Take( Math.Max( 0, missing ) ) );
            }
            var leadCount = pledgers.Count == 0 ? 0 : Math.Max( 1, ( int ) Math.Round( pledgers.Count * settings.LeadShare ) );
            var leads = pledgers.OrderByDescending( c => c.Yearly ).Take( leadCount ).ToHashSet();

            foreach ( var pledger in pledgers )
            {
                PlanGeneratedPledge( pledger, leads.Contains( pledger ), commitment, end );
            }

            foreach ( var giver in candidates.Except( pledgers ) )
            {
                var random = new SeededRandom( $"{_seed}:campaign:{giver.Household.Cast.Guid}:{giver.Giver.Cast.Key}:gifts" );
                if ( !random.Chance( settings.GiftsOnlyShare ) )
                {
                    continue;
                }

                var gifts = Enumerable.Range( 0, random.Between( 1, 2 ) )
                    .Select( _ => new CampaignGift( RandomDay( random, commitment.AddDays( 7 ), anchor.AddDays( -7 ) ), Math.Max( 25, RoundTo( giver.Yearly * ( decimal ) random.Between( 0.05, 0.2 ), 25 ) ), false ) )
                    .ToList();
                Giving[giver.Pattern] = new CampaignGiving( _campaign.Fund, commitment, end, null, 0, null, gifts );
            }
        }

        /// <summary>
        /// Plans one generated pledge: its size, when it was made, its frequency, and how its gifts follow it.
        /// </summary>
        private void PlanGeneratedPledge( Candidate pledger, bool isLead, DateTime commitment, DateTime end )
        {
            var settings = _campaign.Generated;
            var random = new SeededRandom( $"{_seed}:campaign:{pledger.Household.Cast.Guid}:{pledger.Giver.Cast.Key}" );
            var years = settings.PledgeYears[isLead ? "lead" : "other"];
            var total = Friendly( pledger.Yearly * ( decimal ) random.Between( years.Min, years.Max ) );

            var when = isLead ? "quiet-phase" : random.Pick( settings.Pledged );
            var created = when switch
            {
                "quiet-phase" => commitment.AddDays( -7 * random.Between( _campaign.QuietPhaseWeeks.Min, _campaign.QuietPhaseWeeks.Max ) ).AddHours( random.Between( 9, 17 ) ),
                "soon-after" => commitment.AddDays( 7 * random.Between( 1, 6 ) ).AddHours( 11 ),
                "later" => commitment.AddDays( 7 * random.Between( 7, _campaign.CommitmentSundayWeeksBeforeInstall - 2 ) ).AddHours( 11 ),
                _ => commitment.AddHours( 11 )
            };
            var start = created.Date > commitment ? created.Date : commitment;
            var lastDay = _dates.Anchor.AddDays( -7 );

            var isRhythmic = Rhythms.Contains( pledger.Pattern.Persona );
            var frequency = isRhythmic ? random.Pick( settings.RhythmicFrequency ) : random.Pick( settings.OtherFrequency );
            frequency = frequency == "same" ? pledger.Pattern.Persona : frequency == "none" ? null : frequency;

            var gifts = new List<CampaignGift>();
            var lineFactor = 0m;
            DateTime? lineStop = null;
            switch ( frequency )
            {
                case "one-time":
                    if ( random.Pick( settings.OneTimeFollowThrough ) == "paid-in-full" && start.AddDays( 8 ) <= lastDay )
                    {
                        gifts.Add( new CampaignGift( start.AddDays( 8 ).AddHours( 12 ), total, false ) );
                    }

                    break;

                case "yearly":
                    var installment = start.AddDays( random.Between( 7, 70 ) ).AddHours( 12 );
                    if ( random.Pick( settings.YearlyFollowThrough ) == PledgeProgress.OnPace && installment <= lastDay )
                    {
                        gifts.Add( new CampaignGift( installment, RoundTo( total / _campaign.LengthYears, 5 ), false ) );
                    }

                    break;

                case null:
                    var followThrough = random.Pick( settings.NoFrequencyFollowThrough );
                    var share = ( decimal ) Math.Clamp( ( _dates.Anchor - start ).TotalDays / ( end - start ).TotalDays, 0, 1 );
                    var count = random.Between( 2, 3 );
                    if ( followThrough != PledgeProgress.NotStarted && start.AddDays( 7 ) < lastDay )
                    {
                        var each = RoundTo( total * share * ( followThrough == PledgeProgress.Behind ? 0.5m : 1m ) / count, 5 );
                        gifts.AddRange( Enumerable.Range( 0, count ).Select( _ => new CampaignGift( RandomDay( random, start.AddDays( 7 ), lastDay ), Math.Max( 5, each ), false ) ) );
                    }

                    break;

                default:
                    var lineFollowThrough = random.Pick( settings.LineFollowThrough );
                    lineFactor = _campaign.LineFactors.GetValueOrDefault( lineFollowThrough );
                    lineStop = lineFollowThrough == PledgeProgress.Stopped ? start.AddDays( 7 * random.Between( 12, 20 ) ) : null;
                    break;
            }

            Giving[pledger.Pattern] = new CampaignGiving( _campaign.Fund, start, end, total, lineFactor, lineStop, gifts );
            Pledges.Add( new PlannedPledge( pledger.Giver, total, frequency, created, start, end ) );
        }

        /// <summary>
        /// True when the pattern was giving at some point between the two dates. A one-time pattern gives only on
        /// the day it started, which is the start of history when it names no day.
        /// </summary>
        private bool GaveWithin( CastGiving pattern, DateTime from, DateTime to )
        {
            var started = _dates.Resolve( pattern.Started ) ?? DateTime.MinValue;
            var ended = pattern.Persona == "one-time" ? started : _dates.Resolve( pattern.Ended ) ?? DateTime.MaxValue;
            return started <= to && ended >= from;
        }

        /// <summary>
        /// About how much a pattern gives in a year.
        /// </summary>
        private static decimal Yearly( CastGiving pattern )
        {
            var perYear = pattern.Persona switch
            {
                "weekly" => 52m,
                "bi-weekly" => 26m,
                "monthly" => 12m,
                "quarterly" => 4m,
                "irregular" => 18m,
                "occasional" => 2.5m,
                _ => 1m
            };
            return pattern.Funds.Values.Sum() * perYear * ( pattern.PercentOfPeriodsGiven ?? 90 ) / 100m;
        }

        /// <summary>
        /// Rounds a pledge the way people pledge: hundreds under $1,000, then $500, $1,000, and $5,000 steps.
        /// </summary>
        private static decimal Friendly( decimal amount )
        {
            var step = amount < 1000 ? 100m : amount < 10000 ? 500m : amount < 100000 ? 1000m : 5000m;
            return Math.Max( 100, RoundTo( amount, step ) );
        }

        private static decimal RoundTo( decimal amount, decimal step ) => Math.Round( amount / step, MidpointRounding.AwayFromZero ) * step;

        private static DateTime RandomDay( SeededRandom random, DateTime from, DateTime to )
        {
            return from.AddDays( random.Between( 0, Math.Max( 0, ( int ) ( to - from ).TotalDays ) ) ).AddHours( random.Between( 9, 20 ) );
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
        public void Load( Guid fundGuid )
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

            // Rock has no goal field, so the goal goes in the fund's description: a round number a little under
            // the pledged total, so the campaign has passed its goal (docs/pledges.md).
            var pledged = Pledges.Sum( p => p.Total );
            var step = pledged >= 1000000 ? 100000m : pledged >= 100000 ? 10000m : 1000m;
            var goal = Math.Floor( pledged * 0.95m / step ) * step;
            _db.Execute( "UPDATE [FinancialAccount] SET [Description] = @Description, [PublicDescription] = @Description WHERE [Id] = @Id",
                RockDatabase.P( "@Description", $"{_campaign.Description} Our goal is ${goal:N0}." ), RockDatabase.P( "@Id", fundId ) );

            Console.WriteLine( $"  Pledges: {Pledges.Count:N0} (${pledged:N0} toward a ${goal:N0} goal), campaign gifts from {Giving.Count:N0} giving patterns" );
        }

        private static int DateKey( DateTime date ) => int.Parse( date.ToString( "yyyyMMdd" ) );
    }
}
