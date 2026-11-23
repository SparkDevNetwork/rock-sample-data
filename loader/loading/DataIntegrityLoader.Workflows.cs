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
    internal sealed partial class DataIntegrityLoader
    {
        /// <summary>
        /// Rock's Person Data Error workflow type and its three activities.
        /// </summary>
        internal static readonly Guid PersonDataErrorTypeGuid = new Guid( "221BF486-A82C-40A7-85B7-BB44DA45582F" );

        private static readonly Guid EntryActivityGuid = new Guid( "F238E2D8-AAE0-481F-BABF-955C0A3343CC" );
        private static readonly Guid PendingActivityGuid = new Guid( "31D6A610-9F0B-4136-8FEA-8F4E192D75B8" );
        private static readonly Guid CompleteActivityGuid = new Guid( "E9F9E0BE-D38B-4017-954A-5B15C1EED2DE" );

        /// <summary>
        /// Rock's Photo Request workflow type, its one activity, and the application group a reply puts the person in.
        /// </summary>
        internal static readonly Guid PhotoRequestTypeGuid = new Guid( "036F2F0B-C2DC-49D0-A17B-CCDAC7FC71E2" );

        private static readonly Guid PhotoRequestActivityGuid = new Guid( "BBDA3B74-44A2-41C0-849B-3D8939C027FA" );

        internal static readonly Guid PhotoRequestGroupGuid = new Guid( "2108EF9C-10DC-4466-973D-D25AAB7818BE" );

        private const int InactiveMemberStatus = 0;
        private const int ActiveMemberStatus = 1;
        private const int PendingMemberStatus = 2;

        private readonly DataTable _workflows = TableFactory.Create( "Id:int", "WorkflowTypeId:int", "Name:string", "Status:string", "IsProcessing:bool", "ActivatedDateTime:date",
            "LastProcessedDateTime:date", "CompletedDateTime:date", "WorkflowIdNumber:int", "WorkflowId:string", "InitiatorPersonAliasId:int", "EntityTypeId:int", "EntityId:int", "Guid:guid",
            "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "ForeignKey:string" );

        private readonly DataTable _activities = TableFactory.Create( "Id:int", "WorkflowId:int", "ActivityTypeId:int", "ActivatedDateTime:date", "LastProcessedDateTime:date",
            "CompletedDateTime:date", "AssignedPersonAliasId:int", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int",
            "ForeignKey:string" );

        private readonly DataTable _actions = TableFactory.Create( "ActivityId:int", "ActionTypeId:int", "LastProcessedDateTime:date", "CompletedDateTime:date", "FormAction:string", "Guid:guid",
            "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "ForeignKey:string" );

        private readonly DataTable _values = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "IsPersistedValueDirty:bool", "Guid:guid", "ForeignKey:string",
            "CreatedDateTime:date", "ModifiedDateTime:date" );

        private int _nextWorkflowId;
        private int _nextActivityId;

        private static object Value( DateTime? value ) => value.HasValue ? value.Value : DBNull.Value;

        private static object Value( int? value ) => value.HasValue ? value.Value : DBNull.Value;

        /// <summary>
        /// One of a workflow type's actions, in its activity's order.
        /// </summary>
        private sealed class ActionTypeRow
        {
            public int Id { get; set; }

            public bool IsForm { get; set; }

            /// <summary>
            /// The action runs only when its criteria hold. Rock leaves an action whose criteria fail with no processed time.
            /// </summary>
            public bool HasCriteria { get; set; }
        }

        private List<ActionTypeRow> ActionTypes( int activityTypeId )
        {
            return _db.Query( "SELECT [Id], [WorkflowFormId], [CriteriaAttributeGuid] FROM [WorkflowActionType] WHERE [ActivityTypeId] = @Activity ORDER BY [Order], [Id]", P( "@Activity", activityTypeId ) )
                .Select( r => new ActionTypeRow { Id = ( int ) r["Id"], IsForm = r["WorkflowFormId"] is int, HasCriteria = r["CriteriaAttributeGuid"] is Guid } )
                .ToList();
        }

        private Dictionary<string, int> WorkflowAttributes( int typeId )
        {
            return _db.Query( @"
SELECT [a].[Key], [a].[Id]
FROM [Attribute] AS [a]
WHERE [a].[EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Workflow' )
    AND [a].[EntityTypeQualifierColumn] = 'WorkflowTypeId' AND [a].[EntityTypeQualifierValue] = @Type", P( "@Type", typeId.ToString() ) )
                .ToDictionary( r => ( string ) r["Key"], r => ( int ) r["Id"] );
        }

        /// <summary>
        /// The next workflow number Rock's save hook would give a type: one more than its newest.
        /// </summary>
        private int NextNumber( int typeId ) => Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [WorkflowIdNumber] ), 0 ) + 1 FROM [Workflow] WHERE [WorkflowTypeId] = @Type", P( "@Type", typeId ) ) );

        private void StartWorkflowIds()
        {
            if ( _nextWorkflowId == 0 )
            {
                _nextWorkflowId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [Workflow]" ) );
                _nextActivityId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [WorkflowActivity]" ) );
            }
        }

        private int AddActivity( int workflowId, int activityTypeId, DateTime activated, DateTime lastProcessed, DateTime? completed, int? assignedAliasId, int byAliasId, string seed )
        {
            var id = _nextActivityId++;
            _activities.Rows.Add( id, workflowId, activityTypeId, activated, lastProcessed, Value( completed ), Value( assignedAliasId ), SeededRandom.StableGuid( $"{seed}:activity:{activityTypeId}" ),
                activated, lastProcessed, byAliasId, byAliasId, _foreignKey );
            return id;
        }

        private void AddAction( int activityId, int actionTypeId, DateTime? processed, DateTime? completed, string formAction, DateTime created, int byAliasId, string seed )
        {
            _actions.Rows.Add( activityId, actionTypeId, Value( processed ), Value( completed ), Text( formAction ), SeededRandom.StableGuid( $"{seed}:action:{actionTypeId}" ),
                created, processed ?? created, byAliasId, byAliasId, _foreignKey );
        }

        private void AddValue( int attributeId, int workflowId, string value, DateTime at, string seed, string key )
        {
            _values.Rows.Add( false, attributeId, workflowId, value, true, SeededRandom.StableGuid( $"{seed}:value:{key}" ), _foreignKey, at, at );
        }

        private void WriteWorkflowRows()
        {
            _db.BulkInsert( "Workflow", _workflows, keepIdentity: true );
            _db.BulkInsert( "WorkflowActivity", _activities, keepIdentity: true );
            _db.BulkInsert( "WorkflowAction", _actions, keepIdentity: false );
            _db.BulkInsert( "AttributeValue", _values, keepIdentity: false );
            _workflows.Clear();
            _activities.Clear();
            _actions.Clear();
            _values.Clear();
        }

        /*
            11/23/26 - CLAUDE

            Staff start Rock's Person Data Error workflow from a person's profile (the Bio block's Workflow Actions open
            WorkflowEntry with the person as the entity). Its Entry activity saves the workflow, sets the person and the
            reporter, and shows the reporter's form. Submit starts Pending, which renames the workflow to the person's
            name and assigns it to the person alias in its Assign Worker action, Rock's admin, who is Alisha Marble here
            (decision 177). Her form waits in My Workflows. Complete starts the last activity, which emails the reporter
            without saving the email and completes the workflow, and Rock then marks every activity complete at that moment.

            Reason: Rock's Workflows page under Data Integrity, and Alisha's My Workflows, hold real reports (decision
            D-integrity-5).
        */

        /// <summary>
        /// Writes each Cast data error report as the Person Data Error workflow Rock leaves, open or completed.
        /// </summary>
        private string WriteDataErrors( DataErrorReports reports, PeopleLoader people )
        {
            var typeId = _db.IdByGuid( "WorkflowType", PersonDataErrorTypeGuid );
            var loaded = reports.Reports.Where( r => people.People.ContainsKey( r.Person ) && people.People.ContainsKey( r.ReportedBy ) ).ToList();
            if ( typeId == null || loaded.Count == 0 )
            {
                return typeId == null ? "  Skipped Person Data Error reports (this Rock has no Person Data Error workflow type)" : "  Person Data Error reports: none";
            }

            StartWorkflowIds();
            var entryId = _db.RequireIdByGuid( "WorkflowActivityType", EntryActivityGuid );
            var pendingId = _db.RequireIdByGuid( "WorkflowActivityType", PendingActivityGuid );
            var completeId = _db.RequireIdByGuid( "WorkflowActivityType", CompleteActivityGuid );
            var entryActions = ActionTypes( entryId );
            var pendingActions = ActionTypes( pendingId );
            var completeActions = ActionTypes( completeId );
            var attributes = WorkflowAttributes( typeId.Value );
            var worker = AssignedWorker( pendingActions );
            var number = NextNumber( typeId.Value );
            var open = 0;
            foreach ( var report in loaded )
            {
                var person = people.People[report.Person];
                var reporter = people.People[report.ReportedBy].AliasId;
                var submitted = At( report.Reported );
                var opened = submitted.AddMinutes( -2 );
                var resolved = report.Resolved == null ? ( DateTime? ) null : At( report.Resolved );
                var seed = $"data-integrity:data-error:{report.Key}";
                var workflowId = _nextWorkflowId++;
                var modifiedBy = resolved.HasValue ? worker : reporter;
                _workflows.Rows.Add( workflowId, typeId.Value, FullName( person ), resolved.HasValue ? "Completed" : "Active", false, opened, resolved ?? submitted, Value( resolved ),
                    number, $"{number:00000}", reporter, _personTypeId, person.PersonId, SeededRandom.StableGuid( $"{seed}:workflow" ), opened, resolved ?? submitted, reporter, modifiedBy, _foreignKey );
                number++;

                // Entry: the reporter's form, submitted. Rock marks every activity complete when the workflow completes.
                var entry = AddActivity( workflowId, entryId, opened, submitted, resolved ?? submitted, null, reporter, seed );
                foreach ( var action in entryActions )
                {
                    var at = action.IsForm || entryActions.IndexOf( action ) > entryActions.FindIndex( a => a.IsForm ) ? submitted : opened;
                    AddAction( entry, action.Id, at, at, action.IsForm ? "Submit" : null, opened, reporter, seed );
                }

                // Pending: renamed, assigned, and the worker's form, waiting or completed.
                var pending = AddActivity( workflowId, pendingId, submitted, resolved ?? submitted, resolved, worker, modifiedBy, seed );
                foreach ( var action in pendingActions )
                {
                    AddAction( pending, action.Id, action.IsForm ? resolved ?? submitted : submitted, action.IsForm ? resolved : submitted, action.IsForm && resolved.HasValue ? "Complete" : null,
                        submitted, action.IsForm ? modifiedBy : reporter, seed );
                }

                AddValue( attributes["Person"], workflowId, person.AliasGuid.ToString(), opened, seed, "person" );
                AddValue( attributes["ReportedBy"], workflowId, people.People[report.ReportedBy].AliasGuid.ToString(), opened, seed, "reported-by" );
                AddValue( attributes["Details"], workflowId, report.Details, submitted, seed, "details" );
                if ( resolved.HasValue )
                {
                    var complete = AddActivity( workflowId, completeId, resolved.Value, resolved.Value, resolved, null, worker, seed );
                    foreach ( var action in completeActions )
                    {
                        AddAction( complete, action.Id, resolved, resolved, null, resolved.Value, worker, seed );
                    }

                    AddValue( attributes["Resolution"], workflowId, report.Resolution, resolved.Value, seed, "resolution" );
                    if ( report.Fix != null )
                    {
                        AddHistory( person, report.Fix.ValueName, report.Fix.OldValue, report.Fix.NewValue, null, null, resolved.Value.AddMinutes( -1 ), worker, $"data-error:{report.Key}" );
                    }
                }
                else
                {
                    open++;
                }
            }

            WriteWorkflowRows();
            return $"  Person Data Error reports: {loaded.Count:N0} ({open:N0} waiting for the assignee)";
        }

        /// <summary>
        /// The person alias Rock's Assign Worker action names in its Person setting.
        /// </summary>
        private int AssignedWorker( List<ActionTypeRow> pendingActions )
        {
            var ids = string.Join( ", ", pendingActions.Select( a => a.Id ) );
            var aliasGuid = _db.Scalar( $@"
SELECT TOP 1 [av].[Value]
FROM [AttributeValue] AS [av]
INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
WHERE [av].[EntityId] IN ( {ids} ) AND [a].[Key] = 'Person' AND [a].[EntityTypeQualifierColumn] = 'EntityTypeId'" ) as string;
            var aliasId = Guid.TryParse( aliasGuid, out var guid ) ? _db.IdByGuid( "PersonAlias", guid ) : null;
            return aliasId ?? throw new InvalidOperationException( "Rock's Person Data Error workflow assigns no person, so its reports have no one to wait for." );
        }

        /*
            11/23/26 - CLAUDE

            Staff start Rock's Photo Request workflow from a person's profile too. It saves the workflow, checks whether the
            person opted out before (a Set Warning action runs only then, so Rock leaves it unprocessed), shows the sender a
            form for the message, emails the person without saving the email, and completes. The person's reply is not part
            of the workflow: Rock's Photo Upload page sets their photo and puts them in the Photo Request group as Pending,
            Verify Photos makes them Active, and the opt-out page makes them Inactive. An upload here is the person's own
            Cast photo, dated the day they sent it, so no new image is needed.

            Reason: Rock's Photo Requests pages and Verify Photos have real people waiting (decision D-integrity-6).
        */

        /// <summary>
        /// Writes each photo request as the completed Photo Request workflow, and each reply in Rock's Photo Request group.
        /// </summary>
        private string WritePhotoRequests( PhotoRequests requests, PeopleLoader people, bool arePhotosLoaded )
        {
            var typeId = _db.IdByGuid( "WorkflowType", PhotoRequestTypeGuid );
            var groupId = _db.IdByGuid( "Group", PhotoRequestGroupGuid );
            var loaded = requests.Requests.Where( r => people.People.ContainsKey( r.Person ) && people.People.ContainsKey( requests.Sender ) ).ToList();
            if ( typeId == null || groupId == null || loaded.Count == 0 )
            {
                return typeId == null || groupId == null ? "  Skipped photo requests (this Rock has no Photo Request workflow type or group)" : "  Photo requests: none";
            }

            StartWorkflowIds();
            var activityTypeId = _db.RequireIdByGuid( "WorkflowActivityType", PhotoRequestActivityGuid );
            var actionTypes = ActionTypes( activityTypeId );
            var attributes = WorkflowAttributes( typeId.Value );
            var group = _db.Query( "SELECT [g].[GroupTypeId], [gt].[DefaultGroupRoleId] FROM [Group] AS [g] INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId] WHERE [g].[Id] = @Id", P( "@Id", groupId.Value ) ).Single();
            var members = TableFactory.Create( "IsSystem:bool", "GroupId:int", "GroupTypeId:int", "PersonId:int", "GroupRoleId:int", "GroupMemberStatus:int", "DateTimeAdded:date", "InactiveDateTime:date",
                "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "ForeignKey:string" );
            var sender = people.People[requests.Sender];
            var number = NextNumber( typeId.Value );
            var waiting = 0;
            foreach ( var request in loaded )
            {
                var person = people.People[request.Person];
                var sent = At( request.Sent );
                var opened = sent.AddMinutes( -1 );
                var seed = $"data-integrity:photo-request:{request.Person}";
                var workflowId = _nextWorkflowId++;
                _workflows.Rows.Add( workflowId, typeId.Value, "New Photo Request", "Completed", false, opened, sent, sent, number, $"{number:00000}", sender.AliasId, _personTypeId, person.PersonId,
                    SeededRandom.StableGuid( $"{seed}:workflow" ), opened, sent, sender.AliasId, sender.AliasId, _foreignKey );
                number++;

                var activity = AddActivity( workflowId, activityTypeId, opened, sent, sent, null, sender.AliasId, seed );
                var formIndex = actionTypes.FindIndex( a => a.IsForm );
                for ( var i = 0; i < actionTypes.Count; i++ )
                {
                    var action = actionTypes[i];
                    var at = action.HasCriteria ? ( DateTime? ) null : i < formIndex ? opened : sent;
                    AddAction( activity, action.Id, at, at, action.IsForm ? "Send" : null, opened, sender.AliasId, seed );
                }

                AddValue( attributes["Person"], workflowId, person.AliasGuid.ToString(), opened, seed, "person" );
                AddValue( attributes["Sender"], workflowId, sender.AliasGuid.ToString(), opened, seed, "sender" );
                AddValue( attributes["OptOut"], workflowId, "False", opened, seed, "opt-out" );
                AddValue( attributes["CustomMessage"], workflowId, requests.Message, sent, seed, "message" );

                // The reply: an upload waits as Pending until verified, and an opt-out is Inactive. An upload needs the person's photo.
                if ( request.OptedOut != null )
                {
                    var optedOut = At( request.OptedOut );
                    members.Rows.Add( false, groupId.Value, group["GroupTypeId"], person.PersonId, group["DefaultGroupRoleId"], InactiveMemberStatus, optedOut, optedOut,
                        SeededRandom.StableGuid( $"{seed}:member" ), optedOut, optedOut, person.AliasId, person.AliasId, _foreignKey );
                }
                else if ( request.Uploaded != null && arePhotosLoaded )
                {
                    var uploaded = At( request.Uploaded );
                    var verified = request.Verified == null ? ( DateTime? ) null : At( request.Verified );
                    members.Rows.Add( false, groupId.Value, group["GroupTypeId"], person.PersonId, group["DefaultGroupRoleId"], verified.HasValue ? ActiveMemberStatus : PendingMemberStatus, uploaded, DBNull.Value,
                        SeededRandom.StableGuid( $"{seed}:member" ), uploaded, verified ?? uploaded, person.AliasId, verified.HasValue ? sender.AliasId : person.AliasId, _foreignKey );
                    _db.Execute( "UPDATE [f] SET [f].[CreatedDateTime] = @Uploaded, [f].[ModifiedDateTime] = @Uploaded FROM [BinaryFile] AS [f] INNER JOIN [Person] AS [p] ON [p].[PhotoId] = [f].[Id] WHERE [p].[Id] = @Id",
                        P( "@Uploaded", uploaded ), P( "@Id", person.PersonId ) );
                    waiting += verified.HasValue ? 0 : 1;
                }
            }

            WriteWorkflowRows();
            _db.BulkInsert( "GroupMember", members, keepIdentity: false );
            return $"  Photo requests: {loaded.Count:N0}, with {members.Rows.Count:N0} replies in the Photo Request group ({waiting:N0} photos waiting to be verified)";
        }
    }
}
