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
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/30/26 - CLAUDE

        A one-time flow has one run, on one date, filled from its audience data view that morning, as Rock's
        job starts it from the flow's single-date schedule. Its run finished long before install, so the flow
        shows Completed: messaging is closed, as the job closes it once its one run is done, and Rock never
        reads it again. That is why its audience needs no guard. The loader works out the audience from the
        view's rule as of the run date, and verify runs the same rule as of install day against Rock's own
        engine running the view.

        Built on the Rock Follow-Up's audience is Involved Adults whose family has not pledged to the
        campaign, and its goal is the pledge households view: everyone in a family where someone pledged,
        through Rock's Family Members transformation. A person converts on the day their family's first
        pledge was made. Involved Adults reads Rock's Last Gave attribute, which the giving job writes. On
        install day the rule reads that value. Before then, the rule rebuilds it from the giving unit's gifts.

        Reason: decisions D-comm-flows-slice4-1 and 220.
    */

    internal sealed partial class FlowLoader
    {
        private const int OneTime = 3;
        private static readonly Guid ContributionGuid = new Guid( "2D607262-52D6-4724-910D-5C6E8FB89ACC" );
        private static readonly Guid LastGaveAttributeGuid = new Guid( "02F64263-E290-399E-4487-FC236F4DE81F" );
        private static readonly Guid FamilyAdultRoleGuid = new Guid( "2639F9A5-2AAE-4E48-A8C3-4FFE86681E42" );
        private static readonly Guid FamilyChildRoleGuid = new Guid( "C8B1814F-6AA7-4055-B2D7-48FE20429CB9" );
        private static readonly Guid BusinessRecordTypeGuid = new Guid( "BF64ADD3-E70A-44CE-9C4B-E76BBED37550" );

        /// <summary>
        /// When each person's family first pledged to the campaign, loaded the first time a rule needs it.
        /// </summary>
        private Dictionary<int, DateTime> _pledgeHouseholds;

        /// <summary>
        /// The context the last load used, so a later step can reuse the audience facts it read.
        /// </summary>
        private FlowContext _context;

        /// <summary>
        /// Whether the flow starts its runs from a schedule and an audience view, instead of from a workflow.
        /// </summary>
        private static bool IsScheduled( FlowDefinition flow ) => flow.Trigger == "recurring" || flow.Trigger == "oneTime";

        /// <summary>
        /// Rock's CommunicationFlowTriggerType for the flow.
        /// </summary>
        private static int TriggerValue( FlowDefinition flow ) => flow.Trigger switch
        {
            "recurring" => Recurring,
            "oneTime" => OneTime,
            _ => OnDemand
        };

        /// <summary>
        /// A one-time flow's date, if it falls from <paramref name="from"/> through <paramref name="until"/>.
        /// </summary>
        private IEnumerable<DateTime> OneTimeDate( FlowDefinition flow, DateTime from, DateTime until )
        {
            if ( _source.Campaign == null )
            {
                return Enumerable.Empty<DateTime>();
            }

            var day = _source.Campaign.CommitmentSunday( _dates.Anchor ).AddDays( flow.Schedule.DaysAfter );
            return day >= from && day <= until ? new[] { day } : Enumerable.Empty<DateTime>();
        }

        /// <summary>
        /// The one run of a one-time flow, with everyone its audience view held that morning. Empty when the load has
        /// no campaign pledges for the audience to leave out.
        /// </summary>
        private List<(DateTime Start, List<EmailPerson> People)> OneTimeRun( FlowDefinition flow, FlowContext context, DateTime firstDay )
        {
            var runs = new List<(DateTime Start, List<EmailPerson> People)>();
            if ( PledgeHouseholds().Count == 0 )
            {
                _db.Log.Write( $"  Communication flow: {flow.Name} skipped, this load has no {_source.Campaign?.Name} pledges" );
                return runs;
            }

            foreach ( var day in RunDates( flow, firstDay, _dates.Anchor.Date.AddDays( -1 ) ) )
            {
                if ( LastSend( flow, day ) >= _dates.Anchor )
                {
                    throw new InvalidOperationException( $"configuration/flows.json: flow {flow.Key} is one-time, and its run on {day:yyyy-MM-dd} would still be sending on install day. Its one run must finish before install." );
                }

                var people = OneTimeAudience( flow, AudienceFacts( context ), day ).Where( p => context.ByAlias.ContainsKey( p.AliasId ) )
                    .Select( p => context.ByAlias[p.AliasId] ).OrderBy( p => p.PersonId ).ToList();
                if ( people.Count > 0 )
                {
                    runs.Add( (day, people) );
                }
            }

            return runs;
        }

        /// <summary>
        /// Who a one-time flow's audience view finds on a day, by its rule. A one-time flow's run is finished at install,
        /// so its view carries no guard.
        /// </summary>
        private IEnumerable<AudiencePerson> OneTimeAudience( FlowDefinition flow, List<AudiencePerson> facts, DateTime day )
        {
            var end = day.Date.AddDays( 1 );
            var pledged = PledgeHouseholds();
            Func<AudiencePerson, bool> rule = flow.Audience?.Rule switch
            {
                "involvedAdultsNotYetPledged" => p => IsInvolvedAdult( p, day ) && !( pledged.TryGetValue( p.PersonId, out var first ) && first < end ),
                _ => throw new InvalidOperationException( $"configuration/flows.json: flow {flow.Key} has audience rule '{flow.Audience?.Rule}', which the loader does not know." )
            };

            return facts.Where( p => p.IsActive && rule( p ) );
        }

        /// <summary>
        /// The Involved Adults view on a day: an adult who came to worship in Rock's "last 12 months", is in a small
        /// group or on a serving team, or whose Last Gave falls in the last 365 days. A business is never in a person
        /// data view.
        /// </summary>
        private static bool IsInvolvedAdult( AudiencePerson p, DateTime day )
        {
            var end = day.Date.AddDays( 1 );
            var lastTwelveMonths = new DateTime( day.Year, day.Month, 1 ).AddMonths( -11 );
            return p.IsAdult && !p.IsBusiness && (p.Count( lastTwelveMonths, end ) >= 1 || p.InGroup( p.SmallGroups, end ) || p.InGroup( p.ServingTeams, end )
                || p.GaveSince( day.Date.AddDays( -365 ), end ) );
        }

        /// <summary>
        /// Who Involved Adults finds on install day with a reserved email or none, as the guarded view would: the people
        /// a scheduled communication's audience starts from. Reuses the facts the flows loaded, when they did.
        /// </summary>
        internal List<(int PersonId, int AliasId)> ReservedInvolvedAdultsOnInstallDay()
        {
            var context = _context ?? new FlowContext
            {
                ReservedEmailSuffix = "@" + ( _source.Flows?.ReservedEmailDomain ?? "example.org" ),
                EmailAddresses = _db.Query( "SELECT [Id], [Email] FROM [Person] WHERE [ForeignKey] = @ForeignKey OR [Id] = 1", P( "@ForeignKey", _foreignKey ) )
                    .ToDictionary( r => ( int ) r["Id"], r => r["Email"] as string )
            };
            return AudienceFacts( context ).Where( p => p.IsActive && p.IsReserved && IsInvolvedAdult( p, _dates.Anchor.Date ) )
                .Select( p => (p.PersonId, p.AliasId) ).ToList();
        }

        /// <summary>
        /// The pledge households goal: each person's date is their family's first campaign pledge.
        /// </summary>
        private GoalPlan PledgeHouseholdsGoal( FlowDefinition flow, int type )
        {
            var viewGuid = _source.Reporting?.DataViews.FirstOrDefault( v => v.Key == flow.Goal.View )?.Guid
                ?? throw new InvalidOperationException( $"configuration/flows.json: flow {flow.Key} names data view '{flow.Goal.View}', which configuration/reporting.json does not have." );
            return new GoalPlan
            {
                TypeValue = type,
                SettingsJson = JsonSerializer.Serialize( new { ConversionGoalSettings = new { EnteredDataViewSettings = new { DataViewGuid = viewGuid } } } ),
                Dates = PledgeHouseholds().ToDictionary( h => h.Key, h => new List<DateTime> { h.Value } ),
                WindowEnd = ( start, days ) => start.AddDays( days )
            };
        }

        /// <summary>
        /// When each person's family first pledged to the campaign, as Rock's Family Members transformation of the
        /// pledgers view finds them: anyone with an adult or child place in a family, not archived, where an adult or
        /// child pledged.
        /// </summary>
        private Dictionary<int, DateTime> PledgeHouseholds()
        {
            if ( _pledgeHouseholds != null )
            {
                return _pledgeHouseholds;
            }

            var fund = _source.Campaign == null ? null : _source.Accounts?.Accounts.FirstOrDefault( a => a.Name == _source.Campaign.Fund );
            _pledgeHouseholds = fund == null ? new Dictionary<int, DateTime>() : _db.Query( @"
SELECT [m].[PersonId], MIN( [fp].[CreatedDateTime] ) AS [Pledged]
FROM [FinancialPledge] AS [fp]
INNER JOIN [FinancialAccount] AS [fa] ON [fa].[Id] = [fp].[AccountId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [fp].[PersonAliasId]
INNER JOIN [GroupMember] AS [c] ON [c].[PersonId] = [pa].[PersonId]
INNER JOIN [GroupTypeRole] AS [cr] ON [cr].[Id] = [c].[GroupRoleId]
INNER JOIN [GroupMember] AS [m] ON [m].[GroupId] = [c].[GroupId] AND [m].[IsArchived] = 0
INNER JOIN [GroupTypeRole] AS [mr] ON [mr].[Id] = [m].[GroupRoleId]
WHERE [fa].[Guid] = @Fund AND [fp].[CreatedDateTime] IS NOT NULL
    AND [cr].[Guid] IN ( @Adult, @Child ) AND [mr].[Guid] IN ( @Adult, @Child )
GROUP BY [m].[PersonId]", P( "@Fund", fund.Guid ), P( "@Adult", FamilyAdultRoleGuid ), P( "@Child", FamilyChildRoleGuid ) )
                .ToDictionary( r => ( int ) r["PersonId"], r => ( DateTime ) r["Pledged"] );
            return _pledgeHouseholds;
        }

        /// <summary>
        /// Adds what Rock's Last Gave reads to each person: the value Rock's giving job wrote, and the days their giving
        /// unit gave, to rebuild it for a past day.
        /// </summary>
        private void AddGivingFacts( List<AudiencePerson> facts )
        {
            var gifts = _db.Query( @"
SELECT DISTINCT [p].[GivingId], CAST( [ft].[TransactionDateTime] AS DATE ) AS [Day]
FROM [FinancialTransaction] AS [ft]
INNER JOIN [DefinedValue] AS [type] ON [type].[Id] = [ft].[TransactionTypeValueId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [ft].[AuthorizedPersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [type].[Guid] = @Contribution AND [ft].[TransactionDateTime] IS NOT NULL AND [p].[GivingId] IS NOT NULL", P( "@Contribution", ContributionGuid ) )
                .GroupBy( r => ( string ) r["GivingId"] )
                .ToDictionary( g => g.Key, g => g.Select( r => ToDay( ( DateTime ) r["Day"] ) ).OrderBy( d => d ).ToArray() );
            var people = _db.Query( @"
SELECT [p].[Id], [p].[GivingId], CASE WHEN [rt].[Guid] = @Business THEN 1 ELSE 0 END AS [IsBusiness]
FROM [Person] AS [p]
LEFT JOIN [DefinedValue] AS [rt] ON [rt].[Id] = [p].[RecordTypeValueId]
WHERE [p].[ForeignKey] = @ForeignKey OR [p].[Id] = 1", P( "@ForeignKey", _foreignKey ), P( "@Business", BusinessRecordTypeGuid ) )
                .ToDictionary( r => ( int ) r["Id"], r => (GivingId: r["GivingId"] as string, IsBusiness: Convert.ToInt32( r["IsBusiness"] ) == 1) );
            var lastGave = _db.Query( @"
SELECT [av].[EntityId], [av].[Value]
FROM [AttributeValue] AS [av]
INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
WHERE [a].[Guid] = @Attribute AND ISNULL( [av].[Value], '' ) <> ''", P( "@Attribute", LastGaveAttributeGuid ) )
                .ToDictionary( r => ( int ) r["EntityId"], r => DateTime.Parse( ( string ) r["Value"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind ) );

            foreach ( var person in facts )
            {
                person.InstallDay = _dates.Anchor.Date;
                var record = people.GetValueOrDefault( person.PersonId );
                person.IsBusiness = record.IsBusiness;
                person.GiftDays = record.GivingId is string givingId && gifts.TryGetValue( givingId, out var days ) ? days : Array.Empty<int>();
                person.LastGave = lastGave.TryGetValue( person.PersonId, out var last ) ? last : null;
            }
        }

        private sealed partial class AudiencePerson
        {
            public DateTime InstallDay { get; set; }

            /// <summary>
            /// A business, which Rock's person data views leave out, even when it gave.
            /// </summary>
            public bool IsBusiness { get; set; }

            /// <summary>
            /// Each day the person's giving unit gave, as a day number, in order.
            /// </summary>
            public int[] GiftDays { get; set; } = Array.Empty<int>();

            /// <summary>
            /// Rock's Last Gave on install day, as the giving job wrote it.
            /// </summary>
            public DateTime? LastGave { get; set; }

            /// <summary>
            /// Whether Rock's Last Gave, as of the moment before <paramref name="end"/>, is on or after <paramref name="from"/>.
            /// On install day it is the value Rock's giving job wrote. Before then, it is the unit's last gift up to that day.
            /// </summary>
            public bool GaveSince( DateTime from, DateTime end )
            {
                if ( end > InstallDay )
                {
                    return LastGave.HasValue && LastGave.Value >= from;
                }

                var index = Array.BinarySearch( GiftDays, ToDay( end ) - 1 );
                index = index < 0 ? ~index - 1 : index;
                return index >= 0 && GiftDays[index] >= ToDay( from );
            }
        }
    }
}
