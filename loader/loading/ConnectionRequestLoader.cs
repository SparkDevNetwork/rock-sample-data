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

using System.Data;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/19/26 - CLAUDE

        Rock's ConnectionRequest save hook sets DueDate and DueSoonDate from the connection type's
        offsets when a request is added (type level by default: 7 and 5 days), and WasCompletedOnTime
        and ConnectedDateTime when it connects. Bulk copy skips that hook, so this loader sets the same
        values. See Rock/Model/Connection/ConnectionRequest/ConnectionRequest.SaveHook.cs.

        It also writes the person history the hook writes (added, status changed, connected, state changed),
        so the person's History tab shows each request.

        Reason: the connection board's due and overdue labels, the analytics, and the History tab read these.
    */

    /*
        10/27/26 - CLAUDE

        Involvement uses Rock's Duration Per Status mode, where a status change restarts the due date from that
        status's offsets. With due dates fixed at 7 days from the request, almost every open request showed as
        past due. Now each active request draws whether it is due later, this week, or past due from
        connections.json's dueMix, and its last status change is dated to match. A No Contact request never
        changed status, so its request date moves instead. Connected requests keep 7 and 5 days from the
        request, as before.

        Reason: most open requests on a working team's board are not due yet (decision 173).
    */

    /// <summary>
    /// Loads requests to serve: a connected request behind every recent volunteer, people who just
    /// connected and wait on their team leader as Pending, and the active and inactive requests around
    /// them in the profile's state mix. The Cast's requests come from their household files. Each
    /// request has its activity history, and every team leader is the default connector for their
    /// team's opportunity at their campus.
    /// </summary>
    internal sealed class ConnectionRequestLoader
    {
        private static readonly Guid GlobalConnectorGroupGuid = new Guid( "7A2AFC10-BCFA-4CCF-9B6C-C2367D6BE7BE" );
        private static readonly Guid PersonConnectionRequestHistoryCategoryGuid = new Guid( "4B472C6A-1ACF-481E-A2D4-9C44436BBCF5" );

        private const int StateActive = 0;
        private const int StateInactive = 1;
        private const int StateFutureFollowUp = 2;
        private const int StateConnected = 3;
        private const int MemberPending = 2;

        private static readonly string[] ContactActivities = { "Called Left Message", "Called No Answer", "Called", "Contacted Waiting for Response" };

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly ConnectionSetup _connections;
        private readonly ServingSetup _serving;
        private readonly ProfileTargets _profile;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public ConnectionRequestLoader( RockDatabase db, SampleDataSource source, ConnectionSetup connections, ServingSetup serving, ProfileTargets profile, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _connections = connections;
            _serving = serving;
            _profile = profile;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// Plans every campus's requests, then bulk-copies them with their activities and the new Pending members.
        /// </summary>
        public void Load( IReadOnlyList<ServingTeamPlan> plans, PeopleLoader people )
        {
            var rules = _source.Connections.Requests;
            var today = _dates.Anchor.Date;
            var activityTypeIds = _db.Query( "SELECT [Id], [Name] FROM [ConnectionActivityType] WHERE [ConnectionTypeId] IS NULL OR [ConnectionTypeId] = @TypeId", P( "@TypeId", _connections.ConnectionTypeId ) )
                .ToDictionary( r => ( string ) r["Name"], r => Convert.ToInt32( r["Id"] ) );
            var leaders = plans.Where( p => p.Members.Any( m => m.IsLeader ) )
                .ToDictionary( p => (p.Campus, p.Team), p => people.People[p.Members.First( m => m.IsLeader ).Person].AliasId );
            var volunteers = plans.SelectMany( p => p.Members ).Select( m => m.Person ).ToHashSet();
            var stateMix = _profile.Map( rules.StateMixProfilePath );
            var monthIndex = _profile.Map( rules.MonthIndexProfilePath ).ToDictionary( kv => int.Parse( kv.Key ), kv => kv.Value );
            var p50 = _profile.Number( $"{rules.DaysToConnectProfilePath}.p50", 16 );
            var p90 = _profile.Number( $"{rules.DaysToConnectProfilePath}.p90", 97 );
            var statusDue = _source.Connections.ConnectionType.DueDateCalculationMode == "DurationPerStatus"
                ? _source.Connections.RequestStatuses.Where( s => s.DueInDays.HasValue ).ToDictionary( s => s.Name, s => (Due: s.DueInDays.Value, DueSoon: s.DueSoonInDays ?? s.DueInDays.Value) )
                : new Dictionary<string, (int Due, int DueSoon)>();
            var context = new RequestContext( rules, today, activityTypeIds, leaders, monthIndex, p50, p90, statusDue ) { StaffConnectors = StaffConnectors( people ) };
            var monthStart = new DateTime( today.Year, today.Month, 1 );
            var workedStatuses = rules.ActiveStatuses.Where( s => s.Key != "No Contact" ).ToDictionary( s => s.Key, s => s.Value );

            // Connector group members are inserted one by one, so the bulk-copy Ids start after them.
            SetDefaultConnectors( leaders, context.StaffConnectors.Values.Select( s => s.AliasId ) );
            var tables = new Tables( _db );

            foreach ( var campusKey in plans.Select( p => p.Campus ).Distinct() )
            {
                var random = new SeededRandom( $"{_seed}:connections:{campusKey}" );
                var campusId = people.Households.First( h => h.CampusKey == campusKey ).Members.First().CampusId;
                var requests = new List<PlannedRequest>();

                // Every volunteer who joined in the history window came through a connected request.
                foreach ( var plan in plans.Where( p => p.Campus == campusKey ) )
                {
                    foreach ( var member in plan.Members.Where( m => m.Joined >= today.AddDays( -rules.HistoryDays ) ) )
                    {
                        requests.Add( Connected( context, random, plan.Team, people.People[member.Person], member.Joined, isPending: false ) );
                    }
                }

                var connectedCount = requests.Count;
                var cast = CastRequests( context, campusKey, people );
                requests.AddRange( cast );
                var requested = cast.Select( r => r.Person.PersonId ).ToHashSet();
                var pool = Requesters( rules, people, campusKey, volunteers, requested, random, _dates );

                var pendingCount = Math.Max( rules.PendingPerCampus.Min, ( int ) Math.Round( connectedCount * rules.PendingPerCampus.PercentOfConnected / 100.0 ) ) - cast.Count( r => r.IsPending );
                var activeCount = ( int ) Math.Round( connectedCount * stateMix.GetValueOrDefault( "active" ) / stateMix["connected"] ) - cast.Count( r => r.State == StateActive );
                var inactiveCount = ( int ) Math.Round( connectedCount * stateMix.GetValueOrDefault( "inactive" ) / stateMix["connected"] ) - cast.Count( r => r.State == StateInactive );
                var futureCount = rules.TestCasesPerCampus.FutureFollowUp - cast.Count( r => r.State == StateFutureFollowUp );
                var transferCount = rules.TestCasesPerCampus.Transferred;
                var movedIntoThisMonth = new List<DateTime>();

                for ( var i = 0; i < pendingCount && pool.Count > 0; i++ )
                {
                    var connected = today.AddDays( -random.Between( ( int ) rules.PendingDaysAgo.Min, ( int ) rules.PendingDaysAgo.Max ) );
                    requests.Add( Connected( context, random, PickTeam( random ), Take( pool ), connected, isPending: true ) );
                }

                for ( var i = 0; i < activeCount + Math.Max( 0, futureCount ) && pool.Count > 0; i++ )
                {
                    var request = Open( context, random, PickTeam( random ), Take( pool ), StateActive, rules.ActiveDaysAgo );
                    if ( i < futureCount )
                    {
                        request.State = StateFutureFollowUp;
                        request.FollowUp = today.AddDays( random.Between( 14, 60 ) );
                        request.WantsConnector = true;
                    }
                    else if ( i < futureCount + transferCount )
                    {
                        var from = PickTeam( random );
                        if ( from != request.Team )
                        {
                            request.TransferredFrom = from;
                        }
                    }

                    var drawn = request.Created;
                    if ( request.State == StateActive )
                    {
                        PlanDue( context, random, request );
                    }

                    if ( drawn < monthStart && request.Created >= monthStart )
                    {
                        movedIntoThisMonth.Add( drawn );
                    }

                    requests.Add( request );
                }

                // Calibration counts requests by the month they were made and leaves out the current month. A No Contact
                // request moved into this month leaves the measured state mix and month pattern, so a worked request takes
                // its place on the day it was drawn.
                foreach ( var drawn in movedIntoThisMonth )
                {
                    if ( pool.Count == 0 )
                    {
                        break;
                    }

                    var request = Open( context, random, PickTeam( random ), Take( pool ), StateActive, rules.ActiveDaysAgo, workedStatuses );
                    request.Created = drawn;
                    PlanDue( context, random, request );
                    requests.Add( request );
                }

                for ( var i = 0; i < inactiveCount && pool.Count > 0; i++ )
                {
                    requests.Add( Open( context, random, PickTeam( random ), Take( pool ), StateInactive, rules.InactiveDaysAgo ) );
                }

                foreach ( var request in requests )
                {
                    request.Campus = campusKey;
                    request.CampusId = campusId;
                    AddRequest( tables, context, random, request );
                }
            }

            tables.Write( _db );
            _db.Log.Write( $"  Connection requests: {tables.Requests.Rows.Count:N0} with {tables.Activities.Rows.Count:N0} activities; {tables.Members.Rows.Count:N0} new volunteers waiting as Pending" );
        }

        /// <summary>
        /// Picks an opportunity, weighted to the teams that need the most people.
        /// </summary>
        private string PickTeam( SeededRandom random )
        {
            var weights = _source.Serving.Teams.ToDictionary( t => t.Key, t => ( double ) t.Positions.Sum( p => p.Capacity[1] ) );
            return random.Pick( weights );
        }

        private static LoadedPerson Take( List<LoadedPerson> pool )
        {
            var person = pool[0];
            pool.RemoveAt( 0 );
            return person;
        }

        /// <summary>
        /// The campus's adults who might ask to serve, in a seeded order weighted by connection status.
        /// Volunteers and people with a Cast request are left out.
        /// </summary>
        /// <summary>
        /// A requester was out of high school before the earliest Pending placement, so nobody still in high school
        /// joins an adult serving team (decision 169). Grade decides, not age.
        /// </summary>
        private static bool IsOutOfHighSchoolForPending( LoadedPerson person, ConnectionRequestRules rules, AnchorDates dates ) =>
            dates.AdultSince( person.Cast, 0 ) <= dates.Anchor.Date.AddDays( -rules.PendingDaysAgo.Max );

        private static List<LoadedPerson> Requesters( ConnectionRequestRules rules, PeopleLoader people, string campusKey, HashSet<Guid> volunteers, HashSet<int> requested, SeededRandom random, AnchorDates dates )
        {
            return people.Households
                .Where( h => h.CampusKey == campusKey )
                .SelectMany( h => h.Members )
                .Where( p => p.IsAdult && p.CanAttend && IsOutOfHighSchoolForPending( p, rules, dates ) && !volunteers.Contains( p.Cast.Guid ) && !requested.Contains( p.PersonId ) && rules.RequesterStatuses.ContainsKey( p.Cast.ConnectionStatus ?? string.Empty ) )
                .GroupBy( p => p.PersonId ).Select( g => g.First() )
                .Select( p => (Person: p, Key: random.Between( 0.0, 1.0 ) / rules.RequesterStatuses[p.Cast.ConnectionStatus]) )
                .OrderBy( x => x.Key )
                .Select( x => x.Person )
                .ToList();
        }

        /// <summary>
        /// A connected request that ended on the day the person joined the team.
        /// </summary>
        private PlannedRequest Connected( RequestContext context, SeededRandom random, string team, LoadedPerson person, DateTime connected, bool isPending )
        {
            var created = connected.AddDays( -Math.Max( 1, Math.Round( context.DaysToConnect( random ) ) ) );
            return new PlannedRequest
            {
                Team = team,
                Person = person,
                State = StateConnected,
                Status = random.Chance( 0.5 ) ? "Shadowing" : "In Progress",
                Created = created,
                Connected = connected,
                IsPending = isPending,
                WantsConnector = random.Chance( context.Rules.ConnectorPercent.GetValueOrDefault( "Connected" ) / 100.0 ),
                Comment = random.Pick( context.Rules.Comments )
            };
        }

        /// <summary>
        /// An active or inactive request from someone who has not joined a team.
        /// </summary>
        private PlannedRequest Open( RequestContext context, SeededRandom random, string team, LoadedPerson person, int state, NumberRange daysAgo, IReadOnlyDictionary<string, double> activeStatuses = null )
        {
            var status = state == StateActive ? random.Pick( activeStatuses ?? context.Rules.ActiveStatuses ) : random.Chance( 0.6 ) ? "In Progress" : "No Contact";
            if ( status == "Background Check" && !context.Rules.BackgroundCheckTeams.Contains( team ) )
            {
                status = "In Progress";
            }

            var stateName = state == StateActive ? "Active" : "Inactive";
            return new PlannedRequest
            {
                Team = team,
                Person = person,
                State = state,
                Status = status,
                Created = context.CreatedDate( random, daysAgo ),
                WantsConnector = status is "Background Check" or "Shadowing" || random.Chance( context.Rules.ConnectorPercent.GetValueOrDefault( stateName ) / 100.0 ),
                Comment = random.Pick( context.Rules.Comments )
            };
        }

        /// <summary>
        /// Draws whether an active request is due later, this week, or past due on install day, from its status's
        /// due mix, and dates its last status change so its due date lands there. A No Contact request never
        /// changed status, so its request date moves instead.
        /// </summary>
        private static void PlanDue( RequestContext context, SeededRandom random, PlannedRequest request )
        {
            if ( !context.StatusDue.TryGetValue( request.Status, out var offsets ) || !context.Rules.DueMix.TryGetValue( request.Status, out var mix ) )
            {
                return;
            }

            var today = context.Today;
            var pastDue = context.Rules.PastDueDays;
            var due = random.Pick( mix ) switch
            {
                "later" => today.AddDays( random.Between( 8, Math.Max( 8, offsets.Due ) ) ),
                "pastDue" => today.AddDays( -random.Between( ( int ) pastDue.Min, ( int ) pastDue.Max ) ),
                _ => today.AddDays( random.Between( 0, 7 ) )
            };

            var clock = due.AddDays( -offsets.Due );
            clock = clock > today ? today : clock;
            if ( request.Status == "No Contact" )
            {
                request.Created = clock;
                return;
            }

            // A status changes after the request is made, and the request date keeps the measured month pattern.
            request.StatusChanged = clock <= request.Created ? request.Created.AddDays( 1 ) : clock;
        }

        /// <summary>
        /// The Cast's requests at this campus, from their household files.
        /// </summary>
        private List<PlannedRequest> CastRequests( RequestContext context, string campusKey, PeopleLoader people )
        {
            var requests = new List<PlannedRequest>();
            foreach ( var household in people.Households.Where( h => h.CampusKey == campusKey ) )
            {
                foreach ( var person in household.Members.Where( m => m.FamilyId == household.FamilyId ) )
                {
                    foreach ( var cast in person.Cast.ConnectionRequests )
                    {
                        var state = cast.State switch
                        {
                            "Active" => StateActive,
                            "Inactive" => StateInactive,
                            "FutureFollowUp" => StateFutureFollowUp,
                            _ => StateConnected
                        };
                        requests.Add( new PlannedRequest
                        {
                            Team = cast.Opportunity,
                            Person = person,
                            State = state,
                            Status = cast.Status,
                            Created = _dates.Resolve( cast.Created ).Value,
                            Connected = _dates.Resolve( cast.Connected ),
                            FollowUp = cast.FollowUpInDays.HasValue ? context.Today.AddDays( cast.FollowUpInDays.Value ) : null,
                            IsPending = state == StateConnected,
                            WantsConnector = cast.Connector,
                            BackgroundCheck = cast.BackgroundCheck,
                            Comment = cast.Comment
                        } );
                    }
                }
            }

            return requests;
        }

        /// <summary>
        /// Adds the request, its activity history, any background check it records, and the Pending
        /// membership of someone who just connected.
        /// </summary>
        private void AddRequest( Tables tables, RequestContext context, SeededRandom random, PlannedRequest request )
        {
            var rules = context.Rules;
            var opportunityId = _connections.OpportunityIds[request.Team];
            var groupId = _serving.TeamGroupIds[(request.Campus, request.Team)];
            var connector = Connector( context, request );
            var end = request.Connected ?? context.Today;
            var requestId = tables.NextRequestId++;
            var isConnected = request.State == StateConnected;

            // A request leaves No Contact at the first contact, a few days after it was made, unless its due date planned the change.
            var statusChanged = request.Status == "No Contact" ? ( DateTime? ) null
                : request.StatusChanged ?? request.Created.AddDays( random.Between( 1, Math.Max( 1, Math.Min( 3, ( int ) ( end - request.Created ).TotalDays ) ) ) );

            // Connected requests keep Rock's type-level 7 and 5 days from the request. Open ones run from the last status change.
            var (dueDays, dueSoonDays) = !isConnected && context.StatusDue.TryGetValue( request.Status, out var offsets ) ? offsets : (7, 5);
            var dueFrom = isConnected ? request.Created : statusChanged ?? request.Created;
            var due = dueFrom.AddDays( dueDays );

            tables.Requests.Rows.Add( requestId, opportunityId, request.Person.AliasId, request.Comment, _connections.RequestStatusIds[request.Status], request.State, Value( request.FollowUp ), request.CampusId,
                isConnected ? groupId : ( object ) DBNull.Value, isConnected ? _serving.MemberRoleId : ( object ) DBNull.Value, isConnected ? MemberPending : ( object ) DBNull.Value,
                Value( connector ), _connections.ConnectionTypeId, due, dueFrom.AddDays( dueSoonDays ), Value( request.Connected ), isConnected && request.Connected <= due,
                int.Parse( request.Created.ToString( "yyyyMMdd" ) ), 0, Guid.NewGuid(), _foreignKey, request.Created, end );

            void Activity( string type, DateTime when, string note, int? activityOpportunityId = null )
            {
                tables.Activities.Rows.Add( requestId, context.ActivityTypeIds[type], Value( connector ), activityOpportunityId ?? opportunityId, note ?? ( object ) DBNull.Value, Guid.NewGuid(), _foreignKey, when, when );
            }

            DateTime Between( DateTime from, DateTime to ) => from.AddDays( random.Between( 0, Math.Max( 0, ( int ) ( to - from ).TotalDays ) ) );
            string Note( string type ) => rules.Notes.TryGetValue( type, out var notes ) && notes.Count > 0 ? random.Pick( notes ) : null;

            if ( request.TransferredFrom != null )
            {
                Activity( "Transferred", request.Created.AddDays( 1 ), $"Transferred from {_source.Serving.Teams.Single( t => t.Key == request.TransferredFrom ).Name}.", _connections.OpportunityIds[request.TransferredFrom] );
            }

            if ( connector.HasValue )
            {
                Activity( "Assigned", request.Created.AddDays( 1 ), null );
            }

            var contacts = request.Status == "No Contact" ? 0 : request.State == StateConnected ? random.Between( 1, 3 ) : random.Between( 1, 2 );
            var cursor = request.Created.AddDays( 1 );
            for ( var i = 0; i < contacts && cursor <= end; i++ )
            {
                cursor = Between( cursor, end );
                var type = random.Pick( ContactActivities );
                Activity( type, cursor, Note( type ) );
            }

            if ( isConnected )
            {
                if ( random.Chance( 0.6 ) )
                {
                    Activity( "Met the Team Leader", Between( cursor, end ), Note( "Met the Team Leader" ) );
                }

                if ( request.Status == "Shadowing" )
                {
                    Activity( "Shadowed a Team", Between( cursor, end ), Note( "Shadowed a Team" ) );
                }

                Activity( "Connected", end, null );
            }

            AddPersonHistory( tables, random, request, requestId, connector, statusChanged, end );

            if ( request.BackgroundCheck != null )
            {
                AddBackgroundCheck( tables, request.Person, request.Created.AddDays( 7 ), request.BackgroundCheck );
            }

            if ( request.IsPending )
            {
                var memberId = tables.NextMemberId++;
                var joined = request.Connected.Value;
                tables.Members.Rows.Add( memberId, false, groupId, _serving.GroupTypeId, request.Person.PersonId, _serving.MemberRoleId, MemberPending, joined, Guid.NewGuid(), _foreignKey, joined, joined );
                tables.MemberHistory.Rows.Add( memberId, groupId, _serving.MemberRoleId, "Member", false, MemberPending, false, joined, new DateTime( 9999, 1, 1 ), true, joined, joined, Guid.NewGuid(), _foreignKey );
            }
        }

        /// <summary>
        /// Writes the person history Rock's save hook writes for a request, so the person's History tab
        /// shows it: added when the person asked, a status change at first contact, connected, and a
        /// state change when it was set aside or saved for a future follow-up.
        /// </summary>
        private void AddPersonHistory( Tables tables, SeededRandom random, PlannedRequest request, int requestId, int? connector, DateTime? statusChanged, DateTime end )
        {
            var opportunityName = _source.Serving.Teams.Single( t => t.Key == request.Team ).Name;

            void History( string verb, DateTime when, int? by )
            {
                tables.History.Rows.Add( false, tables.HistoryCategoryId, tables.PersonEntityTypeId, request.Person.PersonId, "Request", tables.RequestEntityTypeId, requestId, verb, "Record", opportunityName, false,
                    Value( by ), Guid.NewGuid(), _foreignKey, when, when );
            }

            History( "CONNECTIONREQUESTADDED", request.Created, null );
            if ( statusChanged.HasValue )
            {
                History( "CONNECTIONREQUESTSTATUSMODIFY", statusChanged.Value, connector );
            }

            if ( request.State == StateConnected )
            {
                History( "CONNECTIONREQUESTCONNECTED", end, connector );
            }
            else if ( request.State is StateInactive or StateFutureFollowUp )
            {
                History( "CONNECTIONREQUESTSTATEMODIFY", request.Created.AddDays( random.Between( 1, Math.Max( 1, ( int ) ( end - request.Created ).TotalDays ) ) ), connector );
            }
        }

        /// <summary>
        /// Records a background check result on the person, as Rock's background check attributes hold it.
        /// </summary>
        private void AddBackgroundCheck( Tables tables, LoadedPerson person, DateTime checkedOn, string result )
        {
            var ids = tables.BackgroundCheckAttributeIds;
            tables.PersonValues.Rows.Add( false, ids["BackgroundChecked"], person.PersonId, "True", DBNull.Value, true, true, Guid.NewGuid(), _foreignKey, checkedOn, checkedOn );
            tables.PersonValues.Rows.Add( false, ids["BackgroundCheckDate"], person.PersonId, checkedOn.ToString( "o" ), checkedOn, DBNull.Value, true, Guid.NewGuid(), _foreignKey, checkedOn, checkedOn );
            tables.PersonValues.Rows.Add( false, ids["BackgroundCheckResult"], person.PersonId, result, DBNull.Value, DBNull.Value, true, Guid.NewGuid(), _foreignKey, checkedOn, checkedOn );
        }

        /*
            10/27/26 - CLAUDE

            The team leader at the person's campus connects every request that has a connector, except where a
            Cast staff person's connects rule takes it: Alisha Marble, who signs in as Rock's admin, reaches out
            first to the main campus's active requests at No Contact or In Progress. Which requests have a
            connector does not change, so the measured share holds. The Cast's requests that need a connector
            keep none, and no one connects their own request.

            Reason: Signing in as admin shows real connection work on the board (decision D-admin-2).
        */

        /// <summary>
        /// The Cast staff who connect requests at each campus by their connects rule, with the statuses they take.
        /// </summary>
        private static Dictionary<string, StaffConnector> StaffConnectors( PeopleLoader people )
        {
            var staff = new Dictionary<string, StaffConnector>();
            foreach ( var household in people.Households )
            {
                foreach ( var person in household.Members.Where( m => m.FamilyId == household.FamilyId && m.Cast.Connects != null ) )
                {
                    staff[household.CampusKey] = new StaffConnector { AliasId = person.AliasId, PersonId = person.PersonId, Statuses = person.Cast.Connects.Statuses.ToHashSet() };
                }
            }

            return staff;
        }

        /// <summary>
        /// The connector's alias for a request, or null when it has none.
        /// </summary>
        private static int? Connector( RequestContext context, PlannedRequest request )
        {
            if ( !request.WantsConnector )
            {
                return null;
            }

            var isStaffRequest = request.State == StateActive
                && context.StaffConnectors.TryGetValue( request.Campus, out var staff )
                && staff.Statuses.Contains( request.Status )
                && staff.PersonId != request.Person.PersonId;
            if ( isStaffRequest )
            {
                return context.StaffConnectors[request.Campus].AliasId;
            }

            var leader = context.Leaders.GetValueOrDefault( (request.Campus, request.Team) );
            return leader == 0 ? null : leader;
        }

        /// <summary>
        /// Makes each team leader the default connector for their team's opportunity at their campus, and
        /// each leader and staff connector a member of Rock's Global Connector Group, which every opportunity uses.
        /// </summary>
        private void SetDefaultConnectors( Dictionary<(string Campus, string Team), int> leaders, IEnumerable<int> staffAliasIds )
        {
            var campusIds = _db.Query( "SELECT [Id], [Name] FROM [Campus]" ).ToDictionary( r => ( string ) r["Name"], r => Convert.ToInt32( r["Id"] ) );
            var connectorGroup = _db.Query( @"
SELECT [g].[Id], [t].[DefaultGroupRoleId], [g].[GroupTypeId]
FROM [Group] AS [g]
INNER JOIN [GroupType] AS [t] ON [t].[Id] = [g].[GroupTypeId]
WHERE [g].[Guid] = @Guid", P( "@Guid", GlobalConnectorGroupGuid ) ).FirstOrDefault();

            foreach ( var ((campusKey, team), aliasId) in leaders )
            {
                var campusName = _source.Campuses.Campuses.Single( c => c.Key == campusKey ).Name;
                _db.Execute( "UPDATE [ConnectionOpportunityCampus] SET [DefaultConnectorPersonAliasId] = @AliasId WHERE [ConnectionOpportunityId] = @OpportunityId AND [CampusId] = @CampusId",
                    P( "@AliasId", aliasId ), P( "@OpportunityId", _connections.OpportunityIds[team] ), P( "@CampusId", campusIds[campusName] ) );
            }

            if ( connectorGroup == null )
            {
                return;
            }

            foreach ( var aliasId in leaders.Values.Concat( staffAliasIds ) )
            {
                _db.Execute( @"
INSERT INTO [GroupMember] ( [IsSystem], [GroupId], [GroupTypeId], [PersonId], [GroupRoleId], [GroupMemberStatus], [DateTimeAdded], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
SELECT 0, @GroupId, @GroupTypeId, [pa].[PersonId], @RoleId, 1, GETDATE(), NEWID(), @ForeignKey, GETDATE(), GETDATE()
FROM [PersonAlias] AS [pa]
WHERE [pa].[Id] = @AliasId
    AND NOT EXISTS ( SELECT 1 FROM [GroupMember] AS [m] WHERE [m].[GroupId] = @GroupId AND [m].[PersonId] = [pa].[PersonId] )",
                    P( "@GroupId", connectorGroup["Id"] ), P( "@GroupTypeId", connectorGroup["GroupTypeId"] ), P( "@RoleId", connectorGroup["DefaultGroupRoleId"] ), P( "@AliasId", aliasId ), P( "@ForeignKey", _foreignKey ) );
            }
        }

        /// <summary>
        /// A Cast staff person who connects some requests at their campus instead of the team leader.
        /// </summary>
        private sealed class StaffConnector
        {
            public int AliasId { get; set; }

            public int PersonId { get; set; }

            /// <summary>
            /// The active request statuses this person connects.
            /// </summary>
            public HashSet<string> Statuses { get; set; }
        }

        private static object Value<T>( T? value ) where T : struct => value.HasValue ? value.Value : DBNull.Value;

        /// <summary>
        /// One request, before it is written.
        /// </summary>
        private sealed class PlannedRequest
        {
            public string Team { get; set; }

            public LoadedPerson Person { get; set; }

            public string Campus { get; set; }

            public int CampusId { get; set; }

            public int State { get; set; }

            public string Status { get; set; }

            public DateTime Created { get; set; }

            public DateTime? Connected { get; set; }

            public DateTime? FollowUp { get; set; }

            /// <summary>
            /// The day the request last changed status, when its due date planned it. Null draws the first contact.
            /// </summary>
            public DateTime? StatusChanged { get; set; }

            /// <summary>
            /// True when the person just connected and waits on the team leader as a Pending member.
            /// </summary>
            public bool IsPending { get; set; }

            public bool WantsConnector { get; set; }

            public string TransferredFrom { get; set; }

            public string BackgroundCheck { get; set; }

            public string Comment { get; set; }
        }

        /// <summary>
        /// What every request in this load draws on: the rules, the day, the activity types, the team
        /// leaders, and the measured timing.
        /// </summary>
        private sealed class RequestContext
        {
            private readonly Dictionary<int, double> _monthIndex;
            private readonly double _p50;
            private readonly double _spread;

            public RequestContext( ConnectionRequestRules rules, DateTime today, Dictionary<string, int> activityTypeIds, Dictionary<(string, string), int> leaders, Dictionary<int, double> monthIndex, double p50, double p90,
                Dictionary<string, (int Due, int DueSoon)> statusDue )
            {
                Rules = rules;
                StatusDue = statusDue;
                Today = today;
                ActivityTypeIds = activityTypeIds;
                Leaders = leaders;
                _monthIndex = monthIndex;
                _p50 = p50;

                // A log-normal whose median is p50 and whose 90th percentile is p90 (z = 1.2816).
                _spread = Math.Log( p90 / p50 ) / 1.2816;
            }

            public ConnectionRequestRules Rules { get; }

            public DateTime Today { get; }

            public Dictionary<string, int> ActivityTypeIds { get; }

            public Dictionary<(string, string), int> Leaders { get; }

            /// <summary>
            /// The Cast staff connector at each campus that has one, by campus key.
            /// </summary>
            public Dictionary<string, StaffConnector> StaffConnectors { get; set; } = new Dictionary<string, StaffConnector>();

            /// <summary>
            /// Each status's due and due-soon days, when due dates run from the last status change. Empty otherwise.
            /// </summary>
            public Dictionary<string, (int Due, int DueSoon)> StatusDue { get; }

            /// <summary>
            /// Days from request to connection. SeededRandom.LogNormal has a mean of 1, so its median is
            /// e^(-spread²/2); dividing by that puts the median at p50.
            /// </summary>
            public double DaysToConnect( SeededRandom random ) => _p50 * random.LogNormal( _spread ) / Math.Exp( -_spread * _spread / 2 );

            /// <summary>
            /// A request date in the range, more often in the busy months of the measured month pattern.
            /// </summary>
            public DateTime CreatedDate( SeededRandom random, NumberRange daysAgo )
            {
                var busiest = _monthIndex.Count > 0 ? _monthIndex.Values.Max() : 1.0;
                while ( true )
                {
                    var date = Today.AddDays( -random.Between( ( int ) daysAgo.Min, ( int ) daysAgo.Max ) );
                    if ( _monthIndex.Count == 0 || random.Chance( _monthIndex.GetValueOrDefault( date.Month, busiest ) / busiest ) )
                    {
                        return date;
                    }
                }
            }
        }

        /// <summary>
        /// The in-memory tables and the next Id for each table whose Ids other rows point to.
        /// </summary>
        private sealed class Tables
        {
            public Tables( RockDatabase db )
            {
                NextRequestId = Convert.ToInt32( db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [ConnectionRequest]" ) );
                NextMemberId = Convert.ToInt32( db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [GroupMember]" ) );
                PersonEntityTypeId = Convert.ToInt32( db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Person'" ) );
                RequestEntityTypeId = Convert.ToInt32( db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.ConnectionRequest'" ) );
                HistoryCategoryId = db.RequireIdByGuid( "Category", PersonConnectionRequestHistoryCategoryGuid );
                BackgroundCheckAttributeIds = db.Query( @"
SELECT [a].[Key], [a].[Id]
FROM [Attribute] AS [a]
INNER JOIN [EntityType] AS [e] ON [e].[Id] = [a].[EntityTypeId]
WHERE [e].[Name] = 'Rock.Model.Person' AND [a].[Key] IN ( 'BackgroundChecked', 'BackgroundCheckDate', 'BackgroundCheckResult' )" ).ToDictionary( r => ( string ) r["Key"], r => Convert.ToInt32( r["Id"] ) );
            }

            public int NextRequestId { get; set; }

            public int NextMemberId { get; set; }

            public Dictionary<string, int> BackgroundCheckAttributeIds { get; }

            public int PersonEntityTypeId { get; }

            public int RequestEntityTypeId { get; }

            public int HistoryCategoryId { get; }

            public DataTable History { get; } = TableFactory.Create( "IsSystem:bool", "CategoryId:int", "EntityTypeId:int", "EntityId:int", "Caption:string", "RelatedEntityTypeId:int", "RelatedEntityId:int", "Verb:string", "ChangeType:string", "ValueName:string", "IsSensitive:bool",
                "CreatedByPersonAliasId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable Requests { get; } = TableFactory.Create( "Id:int", "ConnectionOpportunityId:int", "PersonAliasId:int", "Comments:string", "ConnectionStatusId:int", "ConnectionState:int", "FollowupDate:date", "CampusId:int",
                "AssignedGroupId:int", "AssignedGroupMemberRoleId:int", "AssignedGroupMemberStatus:int", "ConnectorPersonAliasId:int", "ConnectionTypeId:int", "DueDate:date", "DueSoonDate:date", "ConnectedDateTime:date", "WasCompletedOnTime:bool",
                "CreatedDateKey:int", "Order:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable Activities { get; } = TableFactory.Create( "ConnectionRequestId:int", "ConnectionActivityTypeId:int", "ConnectorPersonAliasId:int", "ConnectionOpportunityId:int", "Note:string", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable Members { get; } = TableFactory.Create( "Id:int", "IsSystem:bool", "GroupId:int", "GroupTypeId:int", "PersonId:int", "GroupRoleId:int", "GroupMemberStatus:int", "DateTimeAdded:date", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public DataTable MemberHistory { get; } = TableFactory.Create( "GroupMemberId:int", "GroupId:int", "GroupRoleId:int", "GroupRoleName:string", "IsLeader:bool", "GroupMemberStatus:int", "IsArchived:bool",
                "EffectiveDateTime:date", "ExpireDateTime:date", "CurrentRowIndicator:bool", "CreatedDateTime:date", "ModifiedDateTime:date", "Guid:guid", "ForeignKey:string" );

            public DataTable PersonValues { get; } = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "ValueAsDateTime:date", "ValueAsBoolean:bool", "IsPersistedValueDirty:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            /// <summary>
            /// Bulk-copies every table, parents before the rows that point to them.
            /// </summary>
            public void Write( RockDatabase db )
            {
                db.BulkInsert( "ConnectionRequest", Requests, keepIdentity: true );
                db.BulkInsert( "ConnectionRequestActivity", Activities, keepIdentity: false );
                db.BulkInsert( "GroupMember", Members, keepIdentity: true );
                db.BulkInsert( "GroupMemberHistorical", MemberHistory, keepIdentity: false );
                db.BulkInsert( "AttributeValue", PersonValues, keepIdentity: false );
                db.BulkInsert( "History", History, keepIdentity: false );
            }
        }
    }
}
