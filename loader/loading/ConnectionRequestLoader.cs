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

        Reason: the connection board's due and overdue labels and the analytics read these columns.
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
            var context = new RequestContext( rules, today, activityTypeIds, leaders, monthIndex, p50, p90 );

            // Connector group members are inserted one by one, so the bulk-copy Ids start after them.
            SetDefaultConnectors( leaders );
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
                var pool = Requesters( rules, people, campusKey, volunteers, requested, random );

                var pendingCount = Math.Max( rules.PendingPerCampus.Min, ( int ) Math.Round( connectedCount * rules.PendingPerCampus.PercentOfConnected / 100.0 ) ) - cast.Count( r => r.IsPending );
                var activeCount = ( int ) Math.Round( connectedCount * stateMix.GetValueOrDefault( "active" ) / stateMix["connected"] ) - cast.Count( r => r.State == StateActive );
                var inactiveCount = ( int ) Math.Round( connectedCount * stateMix.GetValueOrDefault( "inactive" ) / stateMix["connected"] ) - cast.Count( r => r.State == StateInactive );
                var futureCount = rules.TestCasesPerCampus.FutureFollowUp - cast.Count( r => r.State == StateFutureFollowUp );
                var transferCount = rules.TestCasesPerCampus.Transferred;

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
            Console.WriteLine( $"  Connection requests: {tables.Requests.Rows.Count:N0} with {tables.Activities.Rows.Count:N0} activities; {tables.Members.Rows.Count:N0} new volunteers waiting as Pending" );
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
        private static List<LoadedPerson> Requesters( ConnectionRequestRules rules, PeopleLoader people, string campusKey, HashSet<Guid> volunteers, HashSet<int> requested, SeededRandom random )
        {
            return people.Households
                .Where( h => h.CampusKey == campusKey )
                .SelectMany( h => h.Members )
                .Where( p => p.IsAdult && p.CanAttend && !volunteers.Contains( p.Cast.Guid ) && !requested.Contains( p.PersonId ) && rules.RequesterStatuses.ContainsKey( p.Cast.ConnectionStatus ?? string.Empty ) )
                .DistinctBy( p => p.PersonId )
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
        private PlannedRequest Open( RequestContext context, SeededRandom random, string team, LoadedPerson person, int state, NumberRange daysAgo )
        {
            var status = state == StateActive ? random.Pick( context.Rules.ActiveStatuses ) : random.Chance( 0.6 ) ? "In Progress" : "No Contact";
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
            var connector = request.WantsConnector ? context.Leaders.GetValueOrDefault( (request.Campus, request.Team) ) : ( int? ) null;
            connector = connector == 0 ? null : connector;
            var end = request.Connected ?? context.Today;
            var requestId = tables.NextRequestId++;
            var due = request.Created.AddDays( 7 );
            var isConnected = request.State == StateConnected;

            tables.Requests.Rows.Add( requestId, opportunityId, request.Person.AliasId, request.Comment, _connections.RequestStatusIds[request.Status], request.State, Value( request.FollowUp ), request.CampusId,
                isConnected ? groupId : ( object ) DBNull.Value, isConnected ? _serving.MemberRoleId : ( object ) DBNull.Value, isConnected ? MemberPending : ( object ) DBNull.Value,
                Value( connector ), _connections.ConnectionTypeId, due, request.Created.AddDays( 5 ), Value( request.Connected ), isConnected && request.Connected <= due,
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
        /// Records a background check result on the person, as Rock's background check attributes hold it.
        /// </summary>
        private void AddBackgroundCheck( Tables tables, LoadedPerson person, DateTime checkedOn, string result )
        {
            var ids = tables.BackgroundCheckAttributeIds;
            tables.PersonValues.Rows.Add( false, ids["BackgroundChecked"], person.PersonId, "True", DBNull.Value, true, true, Guid.NewGuid(), _foreignKey, checkedOn, checkedOn );
            tables.PersonValues.Rows.Add( false, ids["BackgroundCheckDate"], person.PersonId, checkedOn.ToString( "o" ), checkedOn, DBNull.Value, true, Guid.NewGuid(), _foreignKey, checkedOn, checkedOn );
            tables.PersonValues.Rows.Add( false, ids["BackgroundCheckResult"], person.PersonId, result, DBNull.Value, DBNull.Value, true, Guid.NewGuid(), _foreignKey, checkedOn, checkedOn );
        }

        /// <summary>
        /// Makes each team leader the default connector for their team's opportunity at their campus,
        /// and a member of Rock's Global Connector Group, which every opportunity uses.
        /// </summary>
        private void SetDefaultConnectors( Dictionary<(string Campus, string Team), int> leaders )
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

                if ( connectorGroup != null )
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

            public RequestContext( ConnectionRequestRules rules, DateTime today, Dictionary<string, int> activityTypeIds, Dictionary<(string, string), int> leaders, Dictionary<int, double> monthIndex, double p50, double p90 )
            {
                Rules = rules;
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
                BackgroundCheckAttributeIds = db.Query( @"
SELECT [a].[Key], [a].[Id]
FROM [Attribute] AS [a]
INNER JOIN [EntityType] AS [e] ON [e].[Id] = [a].[EntityTypeId]
WHERE [e].[Name] = 'Rock.Model.Person' AND [a].[Key] IN ( 'BackgroundChecked', 'BackgroundCheckDate', 'BackgroundCheckResult' )" ).ToDictionary( r => ( string ) r["Key"], r => Convert.ToInt32( r["Id"] ) );
            }

            public int NextRequestId { get; set; }

            public int NextMemberId { get; set; }

            public Dictionary<string, int> BackgroundCheckAttributeIds { get; }

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
            }
        }
    }
}
