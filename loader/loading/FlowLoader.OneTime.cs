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
        private static readonly Guid FamilyAdultRoleGuid = new Guid( "2639F9A5-2AAE-4E48-A8C3-4FFE86681E42" );
        private static readonly Guid FamilyChildRoleGuid = new Guid( "C8B1814F-6AA7-4055-B2D7-48FE20429CB9" );

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
        /// The Involved Adults rule on a day, with Rock's "last 12 months" counted from the first of the month 11 months back.
        /// </summary>
        private static bool IsInvolvedAdult( AudiencePerson p, DateTime day ) => IsInvolved( p, day, day.Date.AddDays( 1 ), new DateTime( day.Year, day.Month, 1 ).AddMonths( -11 ) );

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
    }
}
